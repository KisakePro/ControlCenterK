using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace ControlCenterK
{
    /// <summary>
    /// Seul fichier qui dépend de LibreHardwareMonitor. Les sécurités sont ici, côté sonde : minimum de vitesse,
    /// pompes jamais pilotées, et tout ce qui n'est pas explicitement demandé repasse au contrôle du BIOS.
    /// </summary>
    static class LhmBridge
    {
        sealed class Session : IDisposable
        {
            public Computer Pc;
            public readonly Dictionary<string, ISensor> Active = new Dictionary<string, ISensor>();
            public readonly Dictionary<string, float> Sent = new Dictionary<string, float>();
            public readonly Dictionary<string, int> StopStart = new Dictionary<string, int>();
            public float CpuTemp = float.NaN, GpuTemp = float.NaN;

            public void Dispose()
            {
                Release(this);
                try { Pc.Close(); } catch { }
            }
        }

        public static IDisposable Open()
        {
            var pc = new Computer { IsMotherboardEnabled = true, IsControllerEnabled = true, IsCpuEnabled = true, IsGpuEnabled = true };
            pc.Open();
            return new Session { Pc = pc };
        }

        public static string Snapshot(IDisposable session)
        {
            var s = (Session)session;
            var sb = new StringBuilder();
            s.CpuTemp = s.GpuTemp = float.NaN;
            foreach (var h in s.Pc.Hardware) Visit(h, sb, s);
            if (!float.IsNaN(s.CpuTemp)) sb.Append("C\t").Append(F(s.CpuTemp)).Append('\n');
            if (!float.IsNaN(s.GpuTemp)) sb.Append("P\t").Append(F(s.GpuTemp)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Température la plus haute du processeur au dernier relevé (NaN si illisible).</summary>
        public static float CpuTemp(IDisposable session) { return ((Session)session).CpuTemp; }

        /// <summary>Applique les consignes demandées (clé « appareil|prise ») et rend au BIOS toutes les autres.</summary>
        public static void Apply(IDisposable session, Dictionary<string, float> wanted)
        {
            var s = (Session)session;
            foreach (var key in new List<string>(s.Active.Keys))
                if (!wanted.ContainsKey(key))
                {
                    try { s.Active[key].Control.SetDefault(); } catch { }
                    s.Active.Remove(key);
                    s.Sent.Remove(key);
                }
            if (wanted.Count == 0) return;
            foreach (var h in s.Pc.Hardware) ApplyTo(h, wanted, s);
        }

        public static void Release(IDisposable session) { Apply(session, new Dictionary<string, float>()); }

        /// <summary>Consigne 0 % : acceptée seulement quelques secondes, quoi que demande l'application.</summary>
        static bool MayStop(Session s, string key)
        {
            int start, now = Environment.TickCount;
            if (!s.StopStart.TryGetValue(key, out start) || now - start > FanRules.StopCooldownSeconds * 1000) { s.StopStart[key] = now; return true; }
            return now - start <= FanRules.MaxStopSeconds * 1000;
        }

        static bool IsGpu(IHardware h) { return h.HardwareType.ToString().StartsWith("Gpu", StringComparison.Ordinal); }

        static void ApplyTo(IHardware h, Dictionary<string, float> wanted, Session s)
        {
            if (IsGpu(h)) return; // la carte graphique gère ses ventilateurs ; on ne lit que sa température
            foreach (var c in h.Sensors)
            {
                if (c.SensorType != SensorType.Control || c.Control == null) continue;
                string name = Key(c.Name);
                if (!FanRules.CanDrive(name)) continue;
                string key = h.Name + "|" + name;
                float pct;
                if (!wanted.TryGetValue(key, out pct)) continue;
                pct = pct <= 0 && MayStop(s, key) ? 0 : Math.Max(FanRules.MinPercent, Math.Min(100, pct));
                float sent;
                if (s.Sent.TryGetValue(key, out sent) && sent == pct) continue;
                c.Control.SetSoftware(pct);
                s.Active[key] = c;
                s.Sent[key] = pct;
            }
            foreach (var sub in h.SubHardware) ApplyTo(sub, wanted, s);
        }

        static void Visit(IHardware h, StringBuilder sb, Session session)
        {
            h.Update();
            var fans = new List<ISensor>();
            var controls = new List<ISensor>();
            var temps = new List<ISensor>();
            foreach (var s in h.Sensors)
            {
                if (s.SensorType == SensorType.Fan) fans.Add(s);
                else if (s.SensorType == SensorType.Control) controls.Add(s);
                else if (s.SensorType == SensorType.Temperature) temps.Add(s);
            }
            if (IsGpu(h))
            {
                foreach (var t in temps)
                    if (t.Value.HasValue && t.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 && !(t.Value.Value <= session.GpuTemp))
                        session.GpuTemp = t.Value.Value;
                return;
            }
            if (h.HardwareType == HardwareType.Cpu)
                foreach (var t in temps)
                    if (t.Value.HasValue && t.Name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) < 0 && !(t.Value.Value <= session.CpuTemp))
                        session.CpuTemp = t.Value.Value;
            if (fans.Count + controls.Count > 0)
            {
                sb.Append("D\t").Append(h.Name).Append('\n');
                foreach (var t in temps) sb.Append("T\t").Append(t.Name).Append('\t').Append(F(t.Value)).Append('\n');
                foreach (var f in fans)
                {
                    // la consigne porte le même nom (« Fan #1 ») ou le nom suivi de « Control » (« Pump Control »)
                    var c = controls.Find(x => Key(x.Name) == Key(f.Name));
                    if (c != null) controls.Remove(c);
                    Line(sb, f.Name, f, c);
                }
                foreach (var c in controls) Line(sb, Key(c.Name), null, c);
            }
            foreach (var sub in h.SubHardware) Visit(sub, sb, session);
        }

        static string Key(string name) { return name.EndsWith(" Control", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 8) : name; }

        static void Line(StringBuilder sb, string name, ISensor fan, ISensor control)
        {
            sb.Append("F\t").Append(name).Append('\t').Append(F(fan != null ? fan.Value : null)).Append('\t')
              .Append(F(control != null ? control.Value : null)).Append('\t')
              .Append(control != null && control.Control != null ? "1" : "0").Append('\n');
        }

        static string F(float? v) { return v.HasValue ? v.Value.ToString("0.#", CultureInfo.InvariantCulture) : ""; }
    }
}
