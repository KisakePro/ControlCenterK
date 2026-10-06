using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ControlCenterK
{
    /// <summary>Liste des applications détectées (sessions audio + fenêtres ouvertes). Utilisé seulement par l'UI.</summary>
    static class Catalog
    {
        public class App
        {
            public string Proc;
            public string Name;
            public bool Playing;
        }

        static readonly Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string Describe(string proc, string path)
        {
            string d;
            if (descriptions.TryGetValue(proc, out d)) return d;
            d = null;
            try
            {
                if (path != null)
                {
                    var fvi = FileVersionInfo.GetVersionInfo(path);
                    d = (fvi.FileDescription ?? "").Trim();
                    if (d.Length == 0 || d.Length > 40) d = (fvi.ProductName ?? "").Trim();
                }
            }
            catch { }
            if (string.IsNullOrEmpty(d) || d.Length > 40) d = proc.Length > 0 ? char.ToUpper(proc[0]) + proc.Substring(1) : proc;
            descriptions[proc] = d;
            return d;
        }

        public static List<App> Apps(Engine engine, bool withWindows)
        {
            var map = new Dictionary<string, App>(StringComparer.OrdinalIgnoreCase);
            var sessions = engine.Query(a => a.ListApps()) ?? new List<AppInfo>();
            foreach (var s in sessions)
                map[s.Proc] = new App { Proc = s.Proc, Name = Describe(s.Proc, s.ExePath), Playing = true };

            if (withWindows)
            {
                int self = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == self || p.MainWindowHandle == IntPtr.Zero) continue;
                        string path = Native.ProcessPath((uint)p.Id);
                        string proc = Native.ExeName(path);
                        if (proc == null || map.ContainsKey(proc) || proc == "explorer" || proc == "applicationframehost" || proc == "textinputhost") continue;
                        map[proc] = new App { Proc = proc, Name = Describe(proc, path) };
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            var list = new List<App>(map.Values);
            list.Sort((a, b) => a.Playing != b.Playing ? (a.Playing ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }
    }
}
