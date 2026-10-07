using System;
using System.Collections.Generic;
using System.Drawing;

namespace ControlCenterK
{
    /// <summary>
    /// Souris réglable par l'application (DPI, fréquence, éclairage…). Chaque marque a son pilote :
    /// Corsair, Logitech (HID++), Razer, SteelSeries. Seuls des réglages « en direct » sont envoyés :
    /// rien n'est écrit volontairement dans la mémoire interne de la souris.
    /// </summary>
    abstract class GamingMouse : IDisposable
    {
        public string Name = "", Brand = "", Firmware = "";
        public string Key = "";                 // "vid:pid" : réglages enregistrés par modèle
        public int MinDpi = 100, MaxDpi = 16000, DpiStep = 50;
        public int[] PollRates = new int[0];    // fréquences réglables (Hz), vide si non réglable
        public string[] Zones = new string[0];  // zones d'éclairage, vide si non réglable
        /// <summary>Pilote non testé sur ce modèle précis : affiché comme « expérimental ».</summary>
        public bool Experimental = true;

        /// <summary>Les étapes DPI et le sniper sont gérés par la souris elle-même (Corsair).</summary>
        public virtual bool HardwareStages { get { return false; } }
        /// <summary>Un « mode avancé » (mode logiciel) est nécessaire pour l'éclairage et les boutons supplémentaires.</summary>
        public virtual bool HasAdvancedMode { get { return false; } }

        /// <summary>Bouton supplémentaire pressé (true) / relâché (false), par n° de bouton. Thread de lecture.</summary>
        public event Action<int, bool> Button;

        protected void RaiseButton(int bit, bool down)
        {
            var h = Button;
            if (h != null) h(bit, down);
        }

        /// <summary>Applique la sensibilité d'une valeur unique.</summary>
        public abstract bool SetDpi(int dpi);

        /// <summary>Envoie toutes les étapes (0 = sniper) et sélectionne l'étape en cours.</summary>
        public virtual void ApplyStages(IList<DpiStage> stages, int current)
        {
            SetDpi(stages[current].Dpi);
        }

        /// <summary>Passe à une autre étape (déjà envoyée avec ApplyStages).</summary>
        public virtual void SelectStage(IList<DpiStage> stages, int stage)
        {
            SetDpi(stages[stage].Dpi);
        }

        /// <summary>Lit les étapes réglées dans la souris (premier branchement). Faux si non pris en charge.</summary>
        public virtual bool ReadStages(IList<DpiStage> stages, out int current)
        {
            current = -1;
            return false;
        }

        /// <summary>Sensibilité actuelle de la souris, -1 si inconnue.</summary>
        public virtual int ReadDpi() { return -1; }

        public virtual bool SetPollRate(int hz) { return false; }

        public virtual bool SetZones(Color[] colors) { return false; }

        public virtual void SetAdvanced(bool on) { }

        public virtual void StartButtons() { }

        /// <summary>La souris répond-elle encore ? (débranchée, ou reconnectée après un changement de fréquence)</summary>
        public abstract bool Alive();

        public abstract void Dispose();

        protected static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }
    }

    /// <summary>Souris présente sur le PC (toutes marques), réglable ou non par l'application.</summary>
    sealed class DetectedMouse
    {
        public string Key, Name, Brand;
        public bool Supported;   // un pilote a pris la main
    }

    /// <summary>Détection des souris branchées et choix du pilote.</summary>
    static class MouseDetect
    {
        public const int Corsair = 0x1B1C, Logitech = 0x046D, Razer = 0x1532, SteelSeries = 0x1038;

        public static string BrandName(int vid)
        {
            switch (vid)
            {
                case Corsair: return "Corsair";
                case Logitech: return "Logitech";
                case Razer: return "Razer";
                case SteelSeries: return "SteelSeries";
            }
            return "";
        }

        /// <summary>Souris branchées (une entrée par modèle), d'après les collections HID « souris ».</summary>
        public static List<DetectedMouse> List(List<HidInfo> all)
        {
            var list = new List<DetectedMouse>();
            foreach (var h in all)
            {
                if (!h.IsMouse || list.Exists(m => m.Key == h.Key)) continue;
                var group = all.FindAll(x => x.Key == h.Key);
                string product = NameOf(group, "");
                if (IsKeyboard(group, product)) continue;
                string brand = BrandName(h.Vid);
                if (brand == "") foreach (var x in group) if (x.Manufacturer.Length > 0) { brand = x.Manufacturer; break; }
                string name = product.Length > 0 ? CleanName(product, brand) : (brand + " Souris " + h.Key.ToUpperInvariant()).Trim();
                list.Add(new DetectedMouse { Key = h.Key, Name = name, Brand = brand });
            }
            return list;
        }

        /// <summary>
        /// Les claviers de jeu exposent souvent une collection « souris » (macros) : un appareil qui a aussi
        /// une collection clavier n'est gardé que s'il se présente comme une souris ou un récepteur sans fil.
        /// </summary>
        static bool IsKeyboard(List<HidInfo> group, string product)
        {
            if (!group.Exists(x => x.UsagePage == 1 && x.Usage == 6)) return false;
            string p = product.ToLowerInvariant();
            foreach (var w in new[] { "mouse", "souris", "receiver", "récepteur", "dongle", "lightspeed", "unifying", "hyperspeed", "slipstream" })
                if (p.Contains(w)) return false;
            return true;
        }

        /// <summary>Essaie les pilotes sur les souris branchées ; "preferred" (vid:pid) est essayée en premier.</summary>
        public static List<GamingMouse> Probe(List<HidInfo> all, List<DetectedMouse> mice, string preferred)
        {
            var found = new List<GamingMouse>();
            var keys = new List<string>();
            foreach (var m in mice) keys.Add(m.Key);
            // les récepteurs sans fil Logitech n'ont pas toujours de collection « souris » propre au modèle
            foreach (var h in all) if (h.Vid == Logitech && h.UsagePage == 0xFF00 && !keys.Contains(h.Key)) keys.Add(h.Key);
            if (preferred != null && keys.Remove(preferred)) keys.Insert(0, preferred);
            foreach (var key in keys)
            {
                var group = all.FindAll(h => h.Key == key);
                if (group.Count == 0) continue;
                GamingMouse dev = null;
                try
                {
                    switch (group[0].Vid)
                    {
                        case Corsair: dev = CorsairMouse.Probe(group); break;
                        case Logitech: dev = LogitechMouse.Probe(group); break;
                        case Razer: dev = RazerMouse.Probe(group); break;
                        case SteelSeries: dev = SteelSeriesMouse.Probe(group); break;
                    }
                }
                catch { dev = null; }
                if (dev == null) continue;
                dev.Key = key;
                if (dev.Brand.Length == 0) dev.Brand = BrandName(group[0].Vid);
                found.Add(dev);
                var d = mice.Find(m => m.Key == key);
                if (d != null) { d.Supported = true; if (dev.Name.Length > 0) d.Name = dev.Name; }
                else mice.Add(new DetectedMouse { Key = key, Name = dev.Name, Brand = dev.Brand, Supported = true });
            }
            return found;
        }

        /// <summary>
        /// Nom lisible : « CORSAIR NIGHTSWORD RGB Gaming Mouse » devient « Corsair Nightsword RGB ».
        /// </summary>
        public static string CleanName(string product, string brand)
        {
            string s = product.Trim();
            foreach (var junk in new[] { " Gaming Mouse", " Mouse", " Souris" })
                if (s.EndsWith(junk, StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - junk.Length).Trim();
            if (s.StartsWith(brand, StringComparison.OrdinalIgnoreCase)) s = s.Substring(brand.Length).Trim();
            // mots tout en majuscules : casse normale, sauf sigles courts (RGB, HERO…)
            var words = s.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (w.Length > 4 && w == w.ToUpperInvariant() && w.ToLowerInvariant() != w)
                    words[i] = w.Substring(0, 1) + w.Substring(1).ToLowerInvariant();
            }
            s = string.Join(" ", words).Trim();
            return s.Length == 0 ? brand : brand + " " + s;
        }

        /// <summary>Nom lisible d'une interface (produit, sinon marque + identifiant).</summary>
        public static string NameOf(List<HidInfo> group, string fallback)
        {
            foreach (var h in group) if (h.Product.Length > 0) return h.Product;
            return fallback;
        }
    }
}
