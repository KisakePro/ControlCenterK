using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MidiSoundController
{
    /// <summary>
    /// Une cible pilotée par un contrôle.
    /// Type : master | mic | device | app | focus | system | unassigned
    /// Id   : identifiant du périphérique Windows (device) ou nom de l'exécutable sans .exe (app)
    /// </summary>
    public class Target
    {
        public string Type { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }

        public bool Same(Target o)
        {
            return o != null && o.Type == Type && string.Equals(o.Id ?? "", Id ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    public class ControlMapping
    {
        public string Action { get; set; }
        public List<Target> Targets { get; set; }

        public ControlMapping()
        {
            Action = "";
            Targets = new List<Target>();
        }
    }

    /// <summary>Thème de couleurs : accent + teinte du fond (couleurs en #RRGGBB) + luminosité du fond.</summary>
    public class ThemeDef
    {
        public string Name { get; set; }
        public string Accent { get; set; }
        public string Base { get; set; }
        public double Intensity { get; set; }

        public ThemeDef Copy(string name = null)
        {
            return new ThemeDef { Name = name ?? Name, Accent = Accent, Base = Base, Intensity = Intensity };
        }

        public bool SameColors(ThemeDef o)
        {
            return o != null && string.Equals(o.Accent, Accent, StringComparison.OrdinalIgnoreCase)
                && string.Equals(o.Base, Base, StringComparison.OrdinalIgnoreCase) && Math.Abs(o.Intensity - Intensity) < 0.001;
        }
    }

    #region Routage audio

    /// <summary>Paramètres communs d'une tranche de routage (entrée ou sortie).</summary>
    public class RouteNode
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public double Gain { get; set; }      // dB, -60 (silence) .. +12
        public bool Mute { get; set; }

        public RouteNode()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Name = "";
        }
    }

    /// <summary>Une entrée : micro / entrée ligne, ou capture "en boucle" de ce qui est joué sur une sortie.</summary>
    public class RouteInput : RouteNode
    {
        public bool Loopback { get; set; }
        /// <summary>Pour l'extrémité d'un câble virtuel : l'Id de l'autre extrémité (sert à éviter les boucles).</summary>
        public string PairId { get; set; }
        public List<string> Buses { get; set; } // Ids des sorties vers lesquelles l'entrée est envoyée

        public RouteInput() { Buses = new List<string>(); }
    }

    /// <summary>Une sortie physique (bus).</summary>
    public class RouteOutput : RouteNode
    {
        public int DelayMs { get; set; }
    }

    /// <summary>Périphérique virtuel créé par l'application (carte son USB virtuelle).</summary>
    public class VirtualDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int DevNum { get; set; }   // numéro sur le bus USB/IP : busid "1-DevNum"
        /// <summary>"play" = sortie Windows (haut-parleurs), "mic" = entrée Windows (micro), "both" = les deux.</summary>
        public string Kind { get; set; }

        public const string KindPlay = "play", KindMic = "mic", KindBoth = "both";

        public VirtualDef() { Id = Guid.NewGuid().ToString("N").Substring(0, 8); Name = "Virtuel"; Kind = KindBoth; }
    }

    public class RouterConfig
    {
        public List<VirtualDef> Virtuals { get; set; }
        public bool Running { get; set; }
        public string View { get; set; }   // "console" ou "matrix"
        public List<RouteInput> Inputs { get; set; }
        public List<RouteOutput> Outputs { get; set; }

        public RouterConfig()
        {
            Running = true;
            Virtuals = new List<VirtualDef>();
            Inputs = new List<RouteInput>();
            Outputs = new List<RouteOutput>();
        }

        public void Fix()
        {
            if (Virtuals == null) Virtuals = new List<VirtualDef>();
            Virtuals.RemoveAll(v => v == null || v.DevNum <= 0);
            foreach (var v in Virtuals)
                if (v.Kind != VirtualDef.KindPlay && v.Kind != VirtualDef.KindMic) v.Kind = VirtualDef.KindBoth;
            if (Inputs == null) Inputs = new List<RouteInput>();
            if (Outputs == null) Outputs = new List<RouteOutput>();
            Inputs.RemoveAll(x => x == null);
            Outputs.RemoveAll(x => x == null);
            foreach (var i in Inputs) if (i.Buses == null) i.Buses = new List<string>();
            foreach (var i in Inputs) i.Buses.RemoveAll(b => !Outputs.Exists(o => o.Id == b));
        }

        public RouteNode Find(string type, string id)
        {
            if (type == "route_in") return Inputs.Find(x => x.Id == id);
            if (type == "route_out") return Outputs.Find(x => x.Id == id);
            return null;
        }
    }

    #endregion

    public class Profile
    {
        public string Name { get; set; }
        public Dictionary<string, ControlMapping> Controls { get; set; }

        public Profile()
        {
            Name = "Profil";
            Controls = new Dictionary<string, ControlMapping>();
        }
    }

    public class AppConfig
    {
        /// <summary>Verrou partagé entre l'UI (qui modifie) et le worker (qui lit).</summary>
        public static readonly object Sync = new object();

        public string MidiIn { get; set; }          // "" = automatique (nanoKONTROL), "-" = aucune
        public string MidiOut { get; set; }
        public bool StartWithWindows { get; set; }
        public bool StartMinimized { get; set; }
        public bool LedFeedback { get; set; }
        public bool AutoSelect { get; set; }
        public double Curve { get; set; }
        public List<string> Hidden { get; set; }                 // "dev:{id}" ou "app:{exe}"
        public Dictionary<string, string> Aliases { get; set; }
        public ThemeDef Theme { get; set; }
        public List<ThemeDef> SavedThemes { get; set; }
        public bool AutoUpdate { get; set; }                     // vérifier les mises à jour (1 fois par jour)
        public string LastUpdateCheck { get; set; }
        public string NotifiedVersion { get; set; }              // dernière version annoncée (notification affichée une seule fois)
        public bool ModMidi { get; set; }                        // module "Contrôleur MIDI"
        public bool ModRouter { get; set; }                      // module "Routage audio"
        public RouterConfig Router { get; set; }
        public int Jitter { get; set; }                          // seuil anti-tremblement (0 = désactivé)
        public Dictionary<string, int> ControlValues { get; set; } // dernière position connue des faders / potentiomètres
        public string ActiveProfile { get; set; }
        public List<Profile> Profiles { get; set; }
        /// <summary>Mappings du profil actif (même dictionnaire que Profiles[actif].Controls, non dupliqué dans le fichier).</summary>
        public Dictionary<string, ControlMapping> Controls { get; set; }
        public Dictionary<string, int> CcOverrides { get; set; } // id contrôle -> (canal << 8) | cc

        public AppConfig()
        {
            Jitter = 3;
            ControlValues = new Dictionary<string, int>();
            ModMidi = true;
            AutoUpdate = true;
            Router = new RouterConfig();
            Theme = new ThemeDef { Name = "Bleu nuit", Accent = "#4C8DFF", Base = "#AAB4E1", Intensity = 1 };
            SavedThemes = new List<ThemeDef>();
            Profiles = new List<Profile>();
            MidiIn = "";
            MidiOut = "";
            StartMinimized = true;
            LedFeedback = true;
            AutoSelect = true;
            Curve = 1.0;
            Hidden = new List<string>();
            Aliases = new Dictionary<string, string>();
            Controls = new Dictionary<string, ControlMapping>();
            CcOverrides = new Dictionary<string, int>();
        }

        public ControlMapping Get(string id)
        {
            ControlMapping m;
            if (!Controls.TryGetValue(id, out m) || m == null) { m = new ControlMapping(); Controls[id] = m; }
            if (m.Targets == null) m.Targets = new List<Target>();
            if (m.Action == null) m.Action = "";
            return m;
        }

        public string Alias(string key)
        {
            string a;
            return Aliases.TryGetValue(key, out a) && !string.IsNullOrWhiteSpace(a) ? a.Trim() : null;
        }

        public bool IsHidden(string key) { return Hidden.Contains(key); }

        void ApplyDefaults()
        {
            Get("F1").Targets.Add(new Target { Type = "master" });
            for (int i = 1; i <= 8; i++) Get("M" + i).Action = "mutestrip";
            Get("PLAY").Action = "media_play";
            Get("STOP").Action = "media_stop";
            Get("FF").Action = "media_next";
            Get("REW").Action = "media_prev";
        }

        void Fix()
        {
            if (MidiIn == null) MidiIn = "";
            if (MidiOut == null) MidiOut = "";
            if (Hidden == null) Hidden = new List<string>();
            if (Aliases == null) Aliases = new Dictionary<string, string>();
            if (Controls == null) Controls = new Dictionary<string, ControlMapping>();
            if (CcOverrides == null) CcOverrides = new Dictionary<string, int>();
            if (Curve <= 0 || Curve > 5) Curve = 1.0;
            if (Jitter < 0 || Jitter > 20) Jitter = 3;
            if (ControlValues == null) ControlValues = new Dictionary<string, int>();
            if (Router == null) Router = new RouterConfig();
            Router.Fix();
            if (Theme == null) Theme = new ThemeDef { Name = "Bleu nuit", Accent = "#4C8DFF", Base = "#AAB4E1", Intensity = 1 };
            if (Theme.Intensity <= 0) Theme.Intensity = 1;
            if (SavedThemes == null) SavedThemes = new List<ThemeDef>();
            SavedThemes.RemoveAll(t => t == null || string.IsNullOrWhiteSpace(t.Name));

            // Profils (migration : l'ancienne config devient le profil "Par défaut")
            if (Profiles == null) Profiles = new List<Profile>();
            Profiles.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
            if (Profiles.Count == 0) Profiles.Add(new Profile { Name = "Par défaut", Controls = Controls });
            foreach (var p in Profiles)
            {
                if (p.Controls == null) p.Controls = new Dictionary<string, ControlMapping>();
                foreach (var k in new List<string>(p.Controls.Keys)) FixMapping(p.Controls, k);
            }
            if (FindProfile(ActiveProfile) == null) ActiveProfile = Profiles[0].Name;
            Controls = FindProfile(ActiveProfile).Controls;
        }

        static void FixMapping(Dictionary<string, ControlMapping> d, string k)
        {
            var m = d[k];
            if (m == null) { d[k] = new ControlMapping(); return; }
            if (m.Targets == null) m.Targets = new List<Target>();
            if (m.Action == null) m.Action = "";
        }

        #region Profils

        public Profile FindProfile(string name)
        {
            return Profiles.Find(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>Active un profil (appeler sous lock(Sync)).</summary>
        public bool SetActive(string name)
        {
            var p = FindProfile(name);
            if (p == null) return false;
            ActiveProfile = p.Name;
            Controls = p.Controls;
            return true;
        }

        public string UniqueName(string name, Profile except = null)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "Profil";
            string n = name;
            for (int i = 2; ; i++)
            {
                var p = FindProfile(n);
                if (p == null || p == except) return n;
                n = name + " (" + i + ")";
            }
        }

        public static Dictionary<string, ControlMapping> CloneControls(Dictionary<string, ControlMapping> src)
        {
            var js = new JavaScriptSerializer();
            return js.Deserialize<Dictionary<string, ControlMapping>>(js.Serialize(src)) ?? new Dictionary<string, ControlMapping>();
        }

        public class ProfileFile
        {
            public string Format { get; set; }
            public int Version { get; set; }
            public Profile Profile { get; set; }
        }

        const string ProfileFormat = "MidiSoundController.Profile";

        public static string ExportProfile(Profile p)
        {
            return Pretty(new JavaScriptSerializer().Serialize(new ProfileFile { Format = ProfileFormat, Version = 1, Profile = p }));
        }

        public static Profile ImportProfile(string json)
        {
            var f = new JavaScriptSerializer().Deserialize<ProfileFile>(json);
            if (f == null || f.Format != ProfileFormat || f.Profile == null)
                throw new InvalidDataException("Ce fichier n'est pas un profil MIDI Sound Controller.");
            var p = f.Profile;
            if (p.Controls == null) p.Controls = new Dictionary<string, ControlMapping>();
            foreach (var k in new List<string>(p.Controls.Keys)) FixMapping(p.Controls, k);
            return p;
        }

        #endregion

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MidiSoundController"); }
        }

        static string FilePath { get { return Path.Combine(Folder, "config.json"); } }

        public static AppConfig Load()
        {
            AppConfig c = null;
            try
            {
                if (File.Exists(FilePath))
                    c = new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(FilePath, Encoding.UTF8));
            }
            catch
            {
                try { File.Copy(FilePath, FilePath + ".corrompu", true); } catch { }
            }
            if (c == null) { c = new AppConfig(); c.ApplyDefaults(); }
            c.Fix();
            return c;
        }

        public void Save()
        {
            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    // Controls est déjà stocké dans Profiles : on ne l'écrit pas deux fois.
                    var active = Controls;
                    string json;
                    Controls = null;
                    try { json = Pretty(new JavaScriptSerializer().Serialize(this)); }
                    finally { Controls = active; }
                    string tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));
                    if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                    else File.Move(tmp, FilePath);
                }
                catch { }
            }
        }

        static string Pretty(string json)
        {
            var sb = new StringBuilder(json.Length * 2);
            int ind = 0;
            bool str = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (str)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) sb.Append(json[++i]);
                    else if (c == '"') str = false;
                    continue;
                }
                switch (c)
                {
                    case '"': str = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(c).Append(json[++i]); break; }
                        sb.Append(c).Append('\n').Append(' ', ++ind * 2); break;
                    case '}':
                    case ']': sb.Append('\n').Append(' ', --ind * 2).Append(c); break;
                    case ',': sb.Append(",\n").Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Noms et icônes affichés pour les cibles.</summary>
    public static class Names
    {
        public static string Display(AppConfig cfg, Target t)
        {
            switch (t.Type)
            {
                case "master": return "Sortie par défaut";
                case "mic": return "Micro par défaut";
                case "focus": return "Application au premier plan";
                case "system": return "Sons système";
                case "unassigned": return "Applications non assignées";
                case "device": return cfg.Alias("dev:" + t.Id) ?? t.Name ?? "Périphérique";
                case "app": return cfg.Alias("app:" + t.Id) ?? t.Name ?? t.Id;
                case "route_in":
                case "route_out":
                    {
                        var n = cfg.Router.Find(t.Type, t.Id);
                        return "Routage · " + (n != null ? n.Name : t.Name ?? "?");
                    }
            }
            return t.Name ?? t.Type;
        }

        public static bool IsCaptureId(string id)
        {
            // Les identifiants MMDevice sont de la forme {0.0.0.xxxx}.{guid} (sortie) ou {0.0.1.xxxx}.{guid} (entrée)
            return id != null && id.StartsWith("{0.0.1.", StringComparison.Ordinal);
        }

        public static string Glyph(Target t)
        {
            switch (t.Type)
            {
                case "master": return Glyphs.Volume;
                case "mic": return Glyphs.Mic;
                case "focus": return Glyphs.Focus;
                case "system": return Glyphs.System;
                case "unassigned": return Glyphs.Apps;
                case "device": return IsCaptureId(t.Id) ? Glyphs.Mic : Glyphs.Speaker;
                case "app": return Glyphs.App;
                case "route_in":
                case "route_out": return Glyphs.Route;
            }
            return Glyphs.Volume;
        }
    }

    public static class Glyphs
    {
        public const string Volume = "\uE767", Mic = "\uE720", Speaker = "\uE7F5", Headphone = "\uE7F6",
            App = "\uE7C4", Apps = "\uE71D", Focus = "\uE7F4", System = "\uE770", Settings = "\uE713",
            Mixer = "\uE9E9", Close = "\uE711", Add = "\uE710", Chevron = "\uE70D", Refresh = "\uE72C",
            Check = "\uE73E", Folder = "\uE838", Info = "\uE946", Plug = "\uE957", Learn = "\uE7C9", Palette = "\uE790", Save = "\uE74E", Route = "\uE8AB", Loop = "\uE8EE", More = "\uE712", Power = "\uE7E8";
    }
}
