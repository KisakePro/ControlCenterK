using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace ControlCenterK
{
    /// <summary>
    /// Clavier dont l'éclairage est piloté par l'application : par touche (PerKey) ou d'une seule couleur.
    /// Seules des commandes « en direct » sont envoyées : rien n'est écrit dans la mémoire interne du clavier.
    /// </summary>
    abstract class RgbKeyboard : IDisposable
    {
        public string Name = "", Brand = "", Key = "";
        public bool Experimental = true;

        /// <summary>Codes HID des touches éclairables une à une (vide : couleur unique pour tout le clavier).</summary>
        public int[] Keys = new int[0];
        public bool PerKey { get { return Keys.Length > 0; } }

        /// <summary>Couleur de chaque touche (PerKey) ; les touches absentes du dictionnaire sont éteintes.</summary>
        public virtual bool SetKeys(Dictionary<int, Color> colors) { return false; }

        /// <summary>Une couleur pour tout le clavier.</summary>
        public virtual bool SetAll(Color c)
        {
            var d = new Dictionary<int, Color>();
            foreach (int k in Keys) d[k] = c;
            return SetKeys(d);
        }

        /// <summary>Rend la main à l'éclairage du clavier (quand c'est possible).</summary>
        public virtual void Release() { }

        public abstract bool Alive();

        public abstract void Dispose();
    }

    /// <summary>Clavier présent sur le PC (toutes marques), éclairage réglable ou non.</summary>
    sealed class DetectedKeyboard
    {
        public string Key, Name, Brand;
        public bool Supported;
    }

    static class KeyboardDetect
    {
        static readonly Regex KeyboardWords = new Regex(@"keyboard|clavier|\bapex\b|blackwidow|huntsman|ornata|cynosa|deathstalker|strafe|\bk\d{2,3}\b|\bg\d{3}\b.*key",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Un appareil avec une collection clavier est un clavier, sauf s'il a aussi une collection souris
        /// sans se présenter comme un clavier (souris à touches de macros, récepteurs sans fil).
        /// </summary>
        public static bool IsKeyboard(List<HidInfo> group, string product)
        {
            if (!group.Exists(x => x.UsagePage == 1 && x.Usage == 6)) return false;
            if (!group.Exists(x => x.IsMouse)) return true;
            return product.Length > 0 && KeyboardWords.IsMatch(product);
        }

        public static List<DetectedKeyboard> List(List<HidInfo> all)
        {
            var list = new List<DetectedKeyboard>();
            foreach (var h in all)
            {
                if (!(h.UsagePage == 1 && h.Usage == 6) || list.Exists(k => k.Key == h.Key)) continue;
                var group = all.FindAll(x => x.Key == h.Key);
                string product = MouseDetect.NameOf(group, "");
                if (!IsKeyboard(group, product)) continue;
                string brand = MouseDetect.BrandName(h.Vid);
                if (brand == "") foreach (var x in group) if (x.Manufacturer.Length > 0) { brand = x.Manufacturer; break; }
                string name = product.Length > 0 ? MouseDetect.CleanName(product, brand) : (brand + " Clavier " + h.Key.ToUpperInvariant()).Trim();
                list.Add(new DetectedKeyboard { Key = h.Key, Name = name, Brand = brand });
            }
            return list;
        }

        public static List<RgbKeyboard> Probe(List<HidInfo> all, List<DetectedKeyboard> boards, string preferred)
        {
            var found = new List<RgbKeyboard>();
            var keys = boards.ConvertAll(b => b.Key);
            if (preferred != null && keys.Remove(preferred)) keys.Insert(0, preferred);
            foreach (var key in keys)
            {
                var group = all.FindAll(h => h.Key == key);
                if (group.Count == 0) continue;
                RgbKeyboard kb = null;
                try
                {
                    switch (group[0].Vid)
                    {
                        case MouseDetect.SteelSeries: kb = SteelSeriesKeyboard.Probe(group); break;
                        case MouseDetect.Corsair: kb = CorsairKeyboard.Probe(group); break;
                        case MouseDetect.Razer: kb = RazerKeyboard.Probe(group); break;
                    }
                }
                catch { kb = null; }
                if (kb == null) continue;
                kb.Key = key;
                var b = boards.Find(x => x.Key == key);
                if (kb.Name.Length == 0 && b != null) kb.Name = b.Name;
                if (b != null) { b.Supported = true; b.Name = kb.Name; }
                found.Add(kb);
            }
            return found;
        }
    }
}
