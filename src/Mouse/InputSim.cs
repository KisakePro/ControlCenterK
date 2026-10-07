using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>
    /// Simulation de clavier / souris (SendInput) et macros.
    /// Syntaxe d'une macro : étapes séparées par des virgules, par exemple
    ///   Ctrl+C, 50ms, Ctrl+V          raccourcis et délais
    ///   "bonjour", Entrée              texte tapé tel quel, puis une touche
    ///   Maj+F10 x3                     répéter une étape
    ///   ClicGauche, ClicDroit, ClicMilieu, Précédent, Suivant   clics de souris
    /// </summary>
    static class InputSim
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public int type; public InputUnion u; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public int mouseData; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);

        /// <summary>Marqueur des événements créés par l'application (ignorés par son propre crochet souris).</summary>
        public static readonly IntPtr Tag = new IntPtr(0x43434B);

        const uint KEYUP = 0x2, UNICODE = 0x4, EXTENDED = 0x1;

        static void Send(INPUT i) { SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT))); }

        public static void Key(Keys k, bool down)
        {
            var i = new INPUT { type = 1 };
            i.u.ki.wVk = (ushort)k;
            i.u.ki.dwFlags = (down ? 0 : KEYUP) | (IsExtended(k) ? EXTENDED : 0);
            i.u.ki.extra = Tag;
            Send(i);
        }

        static bool IsExtended(Keys k)
        {
            switch (k)
            {
                case Keys.Insert: case Keys.Delete: case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.RControlKey: case Keys.RMenu:
                case Keys.Divide: case Keys.NumLock: case Keys.LWin: case Keys.RWin: case Keys.Apps:
                    return true;
            }
            return k >= Keys.BrowserBack && k <= Keys.LaunchApplication2;
        }

        public static void Text(string s)
        {
            foreach (char c in s)
                foreach (bool down in new[] { true, false })
                {
                    var i = new INPUT { type = 1 };
                    i.u.ki.wScan = c;
                    i.u.ki.dwFlags = UNICODE | (down ? 0 : KEYUP);
                    i.u.ki.extra = Tag;
                    Send(i);
                }
        }

        /// <summary>Bouton de souris : 0 gauche, 1 droit, 2 milieu, 3 précédent, 4 suivant.</summary>
        public static void MouseButton(int b, bool down)
        {
            var i = new INPUT { type = 0 };
            uint[] downs = { 0x2, 0x8, 0x20, 0x80, 0x80 }, ups = { 0x4, 0x10, 0x40, 0x100, 0x100 };
            i.u.mi.dwFlags = down ? downs[b] : ups[b];
            if (b >= 3) i.u.mi.mouseData = b == 3 ? 1 : 2;
            i.u.mi.extra = Tag;
            Send(i);
        }

        #region Combinaisons et macros

        static readonly Dictionary<string, Keys> names = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
        {
            { "ctrl", Keys.ControlKey }, { "control", Keys.ControlKey }, { "maj", Keys.ShiftKey }, { "shift", Keys.ShiftKey },
            { "alt", Keys.Menu }, { "win", Keys.LWin }, { "windows", Keys.LWin },
            { "entrée", Keys.Return }, { "entree", Keys.Return }, { "enter", Keys.Return }, { "échap", Keys.Escape }, { "echap", Keys.Escape }, { "esc", Keys.Escape },
            { "espace", Keys.Space }, { "space", Keys.Space }, { "tab", Keys.Tab }, { "retour", Keys.Back }, { "backspace", Keys.Back },
            { "suppr", Keys.Delete }, { "del", Keys.Delete }, { "inser", Keys.Insert }, { "début", Keys.Home }, { "debut", Keys.Home }, { "fin", Keys.End },
            { "pgprec", Keys.PageUp }, { "pgsuiv", Keys.PageDown }, { "haut", Keys.Up }, { "bas", Keys.Down }, { "gauche", Keys.Left }, { "droite", Keys.Right },
            { "impr", Keys.PrintScreen }, { "pause", Keys.Pause },
            { "lecture", Keys.MediaPlayPause }, { "pistesuivante", Keys.MediaNextTrack }, { "pisteprécédente", Keys.MediaPreviousTrack },
            { "volume+", Keys.VolumeUp }, { "volume-", Keys.VolumeDown }, { "muet", Keys.VolumeMute },
        };

        /// <summary>Convertit "Ctrl+Maj+S" en liste de touches ; null si invalide.</summary>
        public static List<Keys> ParseCombo(string s)
        {
            var keys = new List<Keys>();
            foreach (var raw in s.Split('+'))
            {
                string p = raw.Trim();
                if (p.Length == 0) { if (s.EndsWith("+")) p = "+"; else return null; }
                Keys k;
                if (names.TryGetValue(p, out k)) keys.Add(k);
                else if (p.Length == 1 && char.IsLetterOrDigit(p[0])) keys.Add((Keys)char.ToUpperInvariant(p[0]));
                else if (Enum.TryParse(p, true, out k)) keys.Add(k);
                else return null;
            }
            return keys.Count == 0 ? null : keys;
        }

        public static string Describe(Keys k)
        {
            foreach (var kv in names) if (kv.Value == k) return char.ToUpper(kv.Key[0]) + kv.Key.Substring(1);
            return k.ToString();
        }

        public static void PressCombo(List<Keys> keys)
        {
            foreach (var k in keys) Key(k, true);
            for (int i = keys.Count - 1; i >= 0; i--) Key(keys[i], false);
        }

        /// <summary>Vérifie une macro ; renvoie un message d'erreur ou null.</summary>
        public static string Validate(string macro)
        {
            foreach (var step in Steps(macro))
            {
                string s = step;
                int times;
                StripRepeat(ref s, out times);
                if (s.Length == 0) continue;
                if (IsDelay(s) >= 0 || IsText(s) != null || MouseNames.ContainsKey(s)) continue;
                if (ParseCombo(s) == null) return "Étape non comprise : « " + step + " »";
            }
            return null;
        }

        static readonly Dictionary<string, int> MouseNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "ClicGauche", 0 }, { "ClicDroit", 1 }, { "ClicMilieu", 2 }, { "Précédent", 3 }, { "Precedent", 3 }, { "Suivant", 4 },
        };

        static List<string> Steps(string macro)
        {
            var list = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char c in macro ?? "")
            {
                if (c == '"') quoted = !quoted;
                if (c == ',' && !quoted) { list.Add(cur.ToString().Trim()); cur.Clear(); continue; }
                cur.Append(c);
            }
            if (cur.Length > 0) list.Add(cur.ToString().Trim());
            return list;
        }

        static void StripRepeat(ref string s, out int times)
        {
            times = 1;
            int x = s.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
            if (x > 0 && !s.StartsWith("\"") && int.TryParse(s.Substring(x + 2), out times)) s = s.Substring(0, x).Trim();
            times = Math.Max(1, Math.Min(100, times));
        }

        static int IsDelay(string s)
        {
            int ms;
            return s.EndsWith("ms", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(0, s.Length - 2).Trim(), out ms) ? Math.Min(ms, 10000) : -1;
        }

        static string IsText(string s)
        {
            return s.Length >= 2 && s.StartsWith("\"") && s.EndsWith("\"") ? s.Substring(1, s.Length - 2) : null;
        }

        /// <summary>Exécute une macro dans un thread à part (n'immobilise ni la souris ni l'interface).</summary>
        public static void RunMacro(string macro)
        {
            var steps = Steps(macro);
            new Thread(() =>
            {
                foreach (var step in steps)
                {
                    string s = step;
                    int times;
                    StripRepeat(ref s, out times);
                    for (int t = 0; t < times; t++)
                    {
                        int ms = IsDelay(s);
                        string text = IsText(s);
                        int mb;
                        if (ms >= 0) Thread.Sleep(ms);
                        else if (text != null) Text(text);
                        else if (MouseNames.TryGetValue(s, out mb)) { MouseButton(mb, true); MouseButton(mb, false); }
                        else { var c = ParseCombo(s); if (c != null) PressCombo(c); }
                        if (ms < 0) Thread.Sleep(15);
                    }
                }
            }) { IsBackground = true, Name = "Macro" }.Start();
        }

        #endregion
    }

    /// <summary>Actions communes aux boutons de souris et aux touches : raccourci maintenu, macro, clic, média, volume.</summary>
    static class InputActions
    {
        static readonly HashSet<string> held = new HashSet<string>();

        /// <summary>Exécute l'action (appui / relâchement) ; faux si le type d'action n'est pas une action commune.</summary>
        public static bool Run(string id, MouseAction a, bool down)
        {
            string val = a.Value ?? "";
            switch (a.Kind ?? "")
            {
                case "keys":
                    {
                        var keys = InputSim.ParseCombo(val);
                        if (keys == null) return true;
                        // maintenu tant que le bouton est maintenu (utile en jeu, push-to-talk…)
                        lock (held)
                        {
                            if (down && held.Add(id)) foreach (var k in keys) InputSim.Key(k, true);
                            else if (!down && held.Remove(id)) for (int i = keys.Count - 1; i >= 0; i--) InputSim.Key(keys[i], false);
                        }
                        return true;
                    }
                case "macro": if (down) InputSim.RunMacro(val); return true;
                case "click": { int b; if (int.TryParse(val, out b)) InputSim.MouseButton(b, down); return true; }
                case "media_play": if (down) Native.PressKey(0xB3); return true;
                case "media_next": if (down) Native.PressKey(0xB0); return true;
                case "media_prev": if (down) Native.PressKey(0xB1); return true;
                case "media_mute": if (down) Native.PressKey(0xAD); return true;
                case "vol_up": if (down) Native.PressKey(0xAF); return true;
                case "vol_down": if (down) Native.PressKey(0xAE); return true;
                case "none": return true; // touche désactivée
            }
            return false;
        }
    }
}
