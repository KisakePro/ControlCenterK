using System;
using System.Collections.Generic;
using System.Threading;

namespace ControlCenterK
{
    public class FanPoint
    {
        public float T { get; set; }    // °C
        public float P { get; set; }    // %
    }

    /// <summary>Réglage d'une prise, conservé dans la configuration.</summary>
    public class FanSetting
    {
        public string Mode { get; set; }            // bios, fixed, curve
        public float Fixed { get; set; }            // % en mode fixed
        public string Source { get; set; }          // cpu, gpu, max, liquid, board
        public List<FanPoint> Points { get; set; }  // 5 points, croissants ; le dernier est toujours à 100 %

        public void Fix()
        {
            if (Mode != "fixed" && Mode != "curve") Mode = "bios";
            if (Source != "gpu" && Source != "max" && Source != "liquid" && Source != "board") Source = "cpu";
            if (Fixed < FanRules.MinPercent) Fixed = FanRules.MinPercent;
            if (Points == null || Points.Count != 5) Points = FanControl.Preset("balanced");
            Points[4].P = 100;
        }
    }

    /// <summary>
    /// Applique les réglages chaque seconde, que la page soit ouverte ou non, tant que le module est actif.
    /// Les vitesses suivent la température progressivement ; une température illisible donne le plein régime.
    /// </summary>
    static class FanControl
    {
        const float RisePerSecond = 8, FallPerSecond = 3;
        const float PumpMatch = 0.05f, PumpMismatch = 0.10f;   // écart relatif avec la pompe de l'AIO
        const int PumpDetectSeconds = 60, PumpForgetSeconds = 30;

        static AppConfig cfg;
        static System.Threading.Timer timer;
        static readonly object sync = new object();
        static readonly Dictionary<string, float> smooth = new Dictionary<string, float>();
        static readonly Dictionary<string, float> holds = new Dictionary<string, float>();
        static List<FanDevice> latest;
        static readonly Dictionary<string, int> pumpSeconds = new Dictionary<string, int>();

        public static void Enable(AppConfig config)
        {
            lock (sync)
            {
                if (timer != null) return;
                cfg = config;
                FanSensors.Start();
                timer = new System.Threading.Timer(_ => Tick(), null, 0, 1000);
            }
        }

        public static void Disable()
        {
            lock (sync)
            {
                if (timer == null) return;
                timer.Dispose();
                timer = null;
                smooth.Clear();
                holds.Clear();
                FanSensors.Stop();
            }
        }

        /// <summary>Vitesse imposée temporairement (identification), prioritaire sur le réglage ; null = fin.</summary>
        public static void Hold(string key, float? percent)
        {
            lock (sync) { if (percent.HasValue) holds[key] = percent.Value; else holds.Remove(key); }
            Tick();
        }

        public static void Changed() { Tick(); }

        public static float Temp(string source)
        {
            List<FanDevice> devs;
            lock (sync) devs = latest;
            return TempOf(source, devs);
        }

        static float TempOf(string source, List<FanDevice> devs)
        {
            switch (source)
            {
                case "gpu": return FanSensors.GpuTemp;
                case "max": return Math.Max(FanSensors.CpuTemp, FanSensors.GpuTemp); // NaN si l'une manque : plein régime
                case "liquid": return Find(devs, "Liquid");
                case "board": return Find(devs, "Motherboard");
                default: return FanSensors.CpuTemp;
            }
        }

        static float Find(List<FanDevice> devs, string name)
        {
            if (devs != null)
                foreach (var d in devs)
                    foreach (var t in d.Temps)
                        if (t.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return t.Value;
            return float.NaN;
        }

        static void Tick()
        {
            lock (sync)
            {
                if (timer == null) return;
                try
                {
                    string error;
                    var devs = FanSensors.Read(out error);
                    if (devs == null) CpuProbe.EnsureRunning();
                    else { latest = devs; DetectPumpSignals(devs); }
                    foreach (var d in latest ?? new List<FanDevice>())
                        foreach (var f in d.Fans)
                        {
                            string key = d.Name + "|" + f.Name;
                            if (!f.Controllable || !FanRules.CanDrive(f.Name)) continue;
                            FanSensors.Set(key, Target(key, latest));
                        }
                    FanSensors.Heartbeat();
                }
                catch { }
            }
        }

        /// <summary>Tours/min de la pompe d'un AIO USB (0 si aucune).</summary>
        static float PumpRpm(List<FanDevice> devs)
        {
            if (devs != null)
                foreach (var d in devs)
                    foreach (var f in d.Fans)
                        if (!FanRules.CanDrive(f.Name) && f.Rpm > 0) return f.Rpm;
            return 0;
        }

        /// <summary>
        /// Une pompe alimentée par l'AIO renvoie souvent ses tours/min sur une prise de la carte mère : cette prise n'a aucun
        /// effet sur elle. Repérée en lecture seule : même vitesse que la pompe, sans écart, pendant une minute.
        /// Seules les prises laissées au BIOS sont étudiées, et la décision est conservée jusqu'à un écart durable.
        /// </summary>
        static void DetectPumpSignals(List<FanDevice> devs)
        {
            float pump = PumpRpm(devs);
            if (pump <= 0) return;
            foreach (var d in devs)
                foreach (var f in d.Fans)
                {
                    if (!FanRules.CanDrive(f.Name) || !f.Controllable || !(f.Rpm > 0)) continue;
                    string key = d.Name + "|" + f.Name, role;
                    FanSetting s;
                    lock (AppConfig.Sync)
                    {
                        if (!cfg.FanRoles.TryGetValue(key, out role)) role = null;
                        if (!cfg.Fans.TryGetValue(key, out s)) s = null;
                    }
                    if (role == "fan" || holds.ContainsKey(key)) { pumpSeconds.Remove(key); continue; }
                    float gap = Math.Abs(f.Rpm - pump) / pump;
                    bool counting = role == "pump" ? gap > PumpMismatch : gap <= PumpMatch && (s == null || s.Mode == "bios");
                    int n;
                    pumpSeconds.TryGetValue(key, out n);
                    n = counting ? n + 1 : Math.Max(0, n - 3); // les relevés sautent par paliers : un écart isolé n'annule pas tout
                    pumpSeconds[key] = n;
                    if (n < (role == "pump" ? PumpForgetSeconds : PumpDetectSeconds)) continue;
                    pumpSeconds.Remove(key);
                    lock (AppConfig.Sync) { if (role == "pump") cfg.FanRoles.Remove(key); else cfg.FanRoles[key] = "pump"; }
                    cfg.Save();
                }
        }

        static bool IsPumpSignal(string key, List<FanDevice> devs)
        {
            string role;
            lock (AppConfig.Sync) if (!cfg.FanRoles.TryGetValue(key, out role) || role != "pump") return false;
            return PumpRpm(devs) > 0;
        }

        public static bool IsPumpSignal(string key)
        {
            List<FanDevice> devs;
            lock (sync) devs = latest;
            return IsPumpSignal(key, devs);
        }

        /// <summary>L'utilisateur indique que cette prise n'est pas la pompe : plus jamais détectée.</summary>
        public static void NotPump(string key)
        {
            lock (AppConfig.Sync) cfg.FanRoles[key] = "fan";
            cfg.Save();
            Tick();
        }

        static float? Target(string key, List<FanDevice> devs)
        {
            float held;
            if (holds.TryGetValue(key, out held)) return held;
            if (IsPumpSignal(key, devs)) { smooth.Remove(key); return null; }
            FanSetting s;
            lock (AppConfig.Sync) if (!cfg.Fans.TryGetValue(key, out s)) s = null;
            if (s == null || s.Mode == "bios") { smooth.Remove(key); return null; }
            if (s.Mode == "fixed") { smooth.Remove(key); return s.Fixed; }
            float goal = Interpolate(s.Points, TempOf(s.Source, devs));
            float cur;
            if (!smooth.TryGetValue(key, out cur)) cur = goal;
            cur = goal > cur ? Math.Min(goal, cur + RisePerSecond) : Math.Max(goal, cur - FallPerSecond);
            smooth[key] = cur;
            return cur;
        }

        /// <summary>Vitesse de la courbe pour une température ; plein régime si la température est illisible.</summary>
        public static float Interpolate(List<FanPoint> pts, float temp)
        {
            if (float.IsNaN(temp) || temp <= 0) return 100;
            if (temp <= pts[0].T) return pts[0].P;
            for (int i = 1; i < pts.Count; i++)
                if (temp <= pts[i].T)
                {
                    float span = pts[i].T - pts[i - 1].T;
                    return span <= 0 ? pts[i].P : pts[i - 1].P + (pts[i].P - pts[i - 1].P) * (temp - pts[i - 1].T) / span;
                }
            return 100;
        }

        public static List<FanPoint> Preset(string name)
        {
            float[,] v = name == "silent" ? new float[,] { { 40, 30 }, { 55, 30 }, { 70, 45 }, { 80, 70 }, { 88, 100 } }
                       : name == "performance" ? new float[,] { { 30, 50 }, { 45, 60 }, { 60, 80 }, { 70, 90 }, { 80, 100 } }
                       : new float[,] { { 35, 30 }, { 50, 40 }, { 65, 60 }, { 75, 80 }, { 85, 100 } };
            var res = new List<FanPoint>();
            for (int i = 0; i < 5; i++) res.Add(new FanPoint { T = v[i, 0], P = v[i, 1] });
            return res;
        }
    }
}
