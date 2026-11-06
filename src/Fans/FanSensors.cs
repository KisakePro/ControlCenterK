using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace ControlCenterK
{
    /// <summary>Une prise de ventilateur (ou pompe) vue par la sonde.</summary>
    sealed class FanReading
    {
        public string Name;
        public float Rpm = float.NaN;
        public float Percent = float.NaN;   // consigne actuelle
        public bool Controllable;
    }

    /// <summary>Un contrôleur : puce de la carte mère ou AIO USB.</summary>
    sealed class FanDevice
    {
        public string Name;
        public readonly List<FanReading> Fans = new List<FanReading>();
        public readonly List<KeyValuePair<string, float>> Temps = new List<KeyValuePair<string, float>>();
    }

    /// <summary>Règles de sécurité, appliquées par la sonde quoi que demande l'application.</summary>
    static class FanRules
    {
        public const float MinPercent = 30;       // jamais d'arrêt : on ne sait pas encore quelle prise refroidit le processeur
        public const int MaxStopSeconds = 40;     // arrêt complet (repérage) : limité par la sonde…
        public const int StopCooldownSeconds = 90; // … et pas de nouvel arrêt de cette prise avant ce délai
        public const float HotCpu = 90;           // au-delà : tout repasse au BIOS…
        public const float CooledCpu = 75;        // … jusqu'à redescendre ici
        public const int HeartbeatMs = 5000;      // application muette plus longtemps : tout repasse au BIOS

        public static bool CanDrive(string name) { return name.IndexOf("Pump", StringComparison.OrdinalIgnoreCase) < 0; }
    }

    /// <summary>Texte en mémoire partagée. En-tête : magic, compteur (impair = écriture en cours), horodatage, longueur.</summary>
    static class ShmText
    {
        public const int Size = 65536, Magic = 0x4E41464B; // "KFAN"

        public static void Write(MemoryMappedViewAccessor v, ref int seq, string text)
        {
            var b = Encoding.UTF8.GetBytes(text);
            int len = Math.Min(b.Length, Size - 16);
            v.Write(4, ++seq);
            v.Write(8, Environment.TickCount);
            v.Write(12, len);
            v.WriteArray(16, b, 0, len);
            v.Write(4, ++seq);
            v.Write(0, Magic);
        }

        /// <summary>Null si rien de valide ou si le texte est plus vieux que maxAgeMs.</summary>
        public static string Read(MemoryMappedViewAccessor v, int maxAgeMs)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int seq = v.ReadInt32(4);
                if (v.ReadInt32(0) != Magic || (seq & 1) != 0) { Thread.Sleep(5); continue; }
                int stamp = v.ReadInt32(8), len = v.ReadInt32(12);
                if (len < 0 || len > Size - 16) return null;
                var bytes = new byte[len];
                v.ReadArray(16, bytes, 0, len);
                if (v.ReadInt32(4) != seq) continue; // publication en cours : on relit
                if (Math.Abs(Environment.TickCount - stamp) > maxAgeMs) return null;
                return Encoding.UTF8.GetString(bytes);
            }
            return null;
        }
    }

    /// <summary>
    /// Module Ventilateurs, côté application (sans droits élevés). Lire et piloter exigent un pilote : c'est la sonde qui
    /// le fait. Elle publie ses relevés en mémoire partagée et lit les consignes de l'application dans une seconde zone
    /// (créée ici : un processus sans droits ne peut pas écrire dans un objet créé par la sonde). Sans l'événement Local,
    /// la sonde ne charge jamais LibreHardwareMonitor. Les consignes viennent de FanControl.
    /// </summary>
    static class FanSensors
    {
        internal const string WantedName = @"Local\ControlCenterK.FansWanted";
        internal const string CommandName = @"Local\ControlCenterK.FanCommands";
        internal const string MapName = @"Global\ControlCenterK.Fans";

        static EventWaitHandle wanted;
        static MemoryMappedFile commandMap;
        static MemoryMappedViewAccessor commandView;
        static int commandSeq;
        static readonly Dictionary<string, float> overrides = new Dictionary<string, float>();

        public static bool Guarded { get; private set; }
        public static float CpuTemp { get; private set; }
        public static float GpuTemp { get; private set; }

        static readonly object sync = new object();

        public static void Start()
        {
            if (wanted == null)
            {
                wanted = new EventWaitHandle(false, EventResetMode.ManualReset, WantedName);
                commandMap = MemoryMappedFile.CreateOrOpen(CommandName, ShmText.Size);
                commandView = commandMap.CreateViewAccessor(0, ShmText.Size);
            }
            CpuProbe.EnsureRunning();
        }

        public static void Stop()
        {
            lock (sync) overrides.Clear();
            if (wanted == null) return;
            Heartbeat();
            commandView.Dispose();
            commandMap.Dispose();
            wanted.Dispose();
            wanted = null;
        }

        /// <summary>Consigne en % ; null = rendre la prise au BIOS.</summary>
        public static void Set(string key, float? percent)
        {
            lock (sync) { if (percent.HasValue) overrides[key] = percent.Value; else overrides.Remove(key); }
        }

        public static float? Get(string key)
        {
            float v;
            lock (sync) return overrides.TryGetValue(key, out v) ? v : (float?)null;
        }

        /// <summary>À appeler chaque seconde : sans cela, la sonde rend tout au BIOS.</summary>
        public static void Heartbeat()
        {
            if (commandView == null || wanted == null) return;
            var sb = new StringBuilder();
            lock (sync)
            {
                foreach (var kv in overrides) sb.Append(kv.Key).Append('\t').Append(kv.Value.ToString("0.#", CultureInfo.InvariantCulture)).Append('\n');
                ShmText.Write(commandView, ref commandSeq, sb.ToString());
            }
        }

        /// <summary>Dernier relevé ; null si la sonde ne publie rien depuis plus de 8 s. error : échec remonté par la sonde.</summary>
        public static List<FanDevice> Read(out string error)
        {
            error = null;
            try
            {
                using (var mm = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read))
                using (var v = mm.CreateViewAccessor(0, ShmText.Size, MemoryMappedFileAccess.Read))
                {
                    string text = ShmText.Read(v, 8000);
                    if (text != null) return Parse(text, out error);
                }
            }
            catch { }
            return null;
        }

        static float Num(string s)
        {
            float f;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : float.NaN;
        }

        static List<FanDevice> Parse(string text, out string error)
        {
            error = null;
            bool guarded = false;
            float cpu = float.NaN, gpu = float.NaN;
            var res = new List<FanDevice>();
            FanDevice cur = null;
            foreach (var line in text.Split('\n'))
            {
                var p = line.Split('\t');
                if (p[0] == "E" && p.Length > 1) error = p[1];
                else if (p[0] == "G") guarded = true;
                else if (p[0] == "C" && p.Length > 1) cpu = Num(p[1]);
                else if (p[0] == "P" && p.Length > 1) gpu = Num(p[1]);
                else if (p[0] == "D" && p.Length > 1) { cur = new FanDevice { Name = p[1] }; res.Add(cur); }
                else if (p[0] == "T" && p.Length > 2 && cur != null) cur.Temps.Add(new KeyValuePair<string, float>(p[1], Num(p[2])));
                else if (p[0] == "F" && p.Length > 4 && cur != null)
                    cur.Fans.Add(new FanReading { Name = p[1], Rpm = Num(p[2]), Percent = Num(p[3]), Controllable = p[4] == "1" });
            }
            Guarded = guarded;
            CpuTemp = cpu;
            GpuTemp = gpu;
            return res;
        }
    }

    /// <summary>Côté sonde (processus élevé) : héberge LibreHardwareMonitor tant que le module est actif.</summary>
    static class FanHost
    {
        static volatile bool stop;
        static bool resolver;

        public static void RequestStop() { stop = true; }

        public static void Run()
        {
            try
            {
                var sec = new MemoryMappedFileSecurity();
                sec.AddAccessRule(new AccessRule<MemoryMappedFileRights>(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), MemoryMappedFileRights.Read, AccessControlType.Allow));
                sec.AddAccessRule(new AccessRule<MemoryMappedFileRights>(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), MemoryMappedFileRights.FullControl, AccessControlType.Allow));
                using (var mm = MemoryMappedFile.CreateOrOpen(FanSensors.MapName, ShmText.Size, MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.None, sec, HandleInheritability.None))
                using (var v = mm.CreateViewAccessor(0, ShmText.Size))
                {
                    int seq = 0;
                    IDisposable hw = null;
                    string failure = null;
                    bool hot = false;
                    try
                    {
                        while (!stop)
                        {
                            EventWaitHandle w;
                            bool want = EventWaitHandle.TryOpenExisting(FanSensors.WantedName, out w);
                            if (want) w.Dispose();
                            if (!want)
                            {
                                if (hw != null) { hw.Dispose(); hw = null; }
                                failure = null;
                                hot = false;
                                Thread.Sleep(1000);
                                continue;
                            }
                            if (hw == null && failure == null)
                            {
                                try { hw = Open(); }
                                catch (Exception ex) { failure = ex.GetType().Name + " : " + ex.Message; }
                            }
                            string text;
                            try
                            {
                                if (failure != null) text = "E\t" + Clean(failure);
                                else
                                {
                                    text = Snapshot(hw);
                                    float t = CpuTemp(hw);
                                    if (t >= FanRules.HotCpu) hot = true;
                                    else if (t <= FanRules.CooledCpu) hot = false;
                                    if (hot) text = "G\n" + text;
                                    Apply(hw, hot ? new Dictionary<string, float>() : ReadCommands());
                                }
                            }
                            catch (Exception ex)
                            {
                                text = "E\t" + Clean(ex.Message);
                                try { if (hw != null) Release(hw); } catch { }
                            }
                            ShmText.Write(v, ref seq, text);
                            Thread.Sleep(500);
                        }
                    }
                    finally { if (hw != null) hw.Dispose(); }
                }
            }
            catch { }
        }

        /// <summary>Consignes de l'application ; vide si elle ne donne plus signe de vie (plantée, figée, fermée).</summary>
        static Dictionary<string, float> ReadCommands()
        {
            var res = new Dictionary<string, float>();
            try
            {
                using (var mm = MemoryMappedFile.OpenExisting(FanSensors.CommandName, MemoryMappedFileRights.Read))
                using (var v = mm.CreateViewAccessor(0, ShmText.Size, MemoryMappedFileAccess.Read))
                {
                    string text = ShmText.Read(v, FanRules.HeartbeatMs);
                    if (text == null) return res;
                    foreach (var line in text.Split('\n'))
                    {
                        var p = line.Split('\t');
                        float f;
                        if (p.Length == 2 && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out f) && !float.IsNaN(f)) res[p[0]] = f;
                    }
                }
            }
            catch { }
            return res;
        }

        static string Clean(string s) { return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        // Séparés pour que les types de la bibliothèque ne soient résolus qu'après l'installation du chargeur.
        static IDisposable Open()
        {
            if (!resolver) { AppDomain.CurrentDomain.AssemblyResolve += Resolve; resolver = true; }
            return LhmBridge.Open();
        }

        static string Snapshot(IDisposable hw) { return LhmBridge.Snapshot(hw); }
        static float CpuTemp(IDisposable hw) { return LhmBridge.CpuTemp(hw); }
        static void Apply(IDisposable hw, Dictionary<string, float> wanted) { LhmBridge.Apply(hw, wanted); }
        static void Release(IDisposable hw) { LhmBridge.Release(hw); }

        /// <summary>Charge la bibliothèque et ses dépendances depuis les ressources de l'exe (fichier unique).</summary>
        static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            using (var s = typeof(FanHost).Assembly.GetManifestResourceStream("lhm." + name + ".dll"))
            {
                if (s == null) return null;
                var b = new byte[s.Length];
                s.Read(b, 0, b.Length);
                return Assembly.Load(b);
            }
        }
    }
}
