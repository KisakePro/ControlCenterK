using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

namespace MidiSoundController
{
    public class UpdateInfo
    {
        public string Version;      // ex. "1.2.0"
        public string Title;
        public string Notes;        // texte de la Release (nouveautés)
        public string PageUrl;      // page de la Release sur GitHub
        public string SetupUrl;     // lien direct vers le setup
        public long SetupSize;
        public string ChecksumUrl;  // fichier .sha256 publié à côté du setup
    }

    /// <summary>
    /// Mises à jour via les Releases GitHub du dépôt (AppVersion.Repo).
    /// Une seule requête HTTPS, faite en arrière-plan, au plus une fois par jour : rien ne tourne en permanence.
    /// </summary>
    public static class Updater
    {
        static string ApiLatest { get { return "https://api.github.com/repos/" + AppVersion.Repo + "/releases/latest"; } }

        /// <summary>Résultat du dernier contrôle (null si aucune version plus récente).</summary>
        public static volatile UpdateInfo Available;
        public static event Action Changed;

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            var wc = new WebClient();
            wc.Headers.Add("User-Agent", "MidiSoundController/" + AppVersion.Current);
            return wc;
        }

        /// <summary>Compare deux versions "1.2.3" (un éventuel préfixe "v" est ignoré).</summary>
        public static int Compare(string a, string b)
        {
            var pa = Parts(a);
            var pb = Parts(b);
            for (int i = 0; i < 4; i++)
                if (pa[i] != pb[i]) return pa[i].CompareTo(pb[i]);
            return 0;
        }

        static int[] Parts(string v)
        {
            var r = new int[4];
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int dash = v.IndexOf('-');
            if (dash >= 0) v = v.Substring(0, dash);
            var s = v.Split('.');
            for (int i = 0; i < 4 && i < s.Length; i++) int.TryParse(s[i], out r[i]);
            return r;
        }

        /// <summary>Interroge GitHub. Renvoie la dernière version publiée (même si elle n'est pas plus récente).</summary>
        public static UpdateInfo FetchLatest()
        {
            string json;
            using (var wc = Client()) json = wc.DownloadString(ApiLatest);
            var rel = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var info = new UpdateInfo
            {
                Version = ((rel["tag_name"] as string) ?? "").TrimStart('v', 'V'),
                Title = rel.ContainsKey("name") ? rel["name"] as string : null,
                Notes = rel.ContainsKey("body") ? rel["body"] as string : null,
                PageUrl = rel["html_url"] as string,
            };
            var assets = rel["assets"] as ArrayList;
            if (assets != null)
                foreach (Dictionary<string, object> a in assets)
                {
                    string n = (a["name"] as string) ?? "";
                    if (n.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        info.SetupUrl = a["browser_download_url"] as string;
                        info.SetupSize = Convert.ToInt64(a["size"]);
                    }
                    else if (n.EndsWith("-Setup.exe.sha256", StringComparison.OrdinalIgnoreCase))
                        info.ChecksumUrl = a["browser_download_url"] as string;
                }
            return info;
        }

        /// <summary>Contrôle en arrière-plan. "force" ignore la limite d'un contrôle par jour.</summary>
        public static void CheckAsync(AppConfig cfg, bool force, Action<UpdateInfo, string> done)
        {
            if (!force)
            {
                DateTime last;
                lock (AppConfig.Sync)
                    if (!cfg.AutoUpdate || (DateTime.TryParse(cfg.LastUpdateCheck, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.RoundtripKind, out last) && (DateTime.UtcNow - last).TotalHours < 20))
                        return;
            }
            new Thread(() =>
            {
                UpdateInfo latest = null;
                string error = null;
                try { latest = FetchLatest(); }
                catch (WebException e)
                {
                    var r = e.Response as HttpWebResponse;
                    error = r != null && r.StatusCode == HttpStatusCode.NotFound ? "Aucune version publiée pour l'instant." : "Pas de connexion à GitHub (" + e.Message + ")";
                }
                catch (Exception e) { error = e.Message; }
                lock (AppConfig.Sync) cfg.LastUpdateCheck = DateTime.UtcNow.ToString("o");
                cfg.Save();
                Available = latest != null && Compare(latest.Version, AppVersion.Current) > 0 ? latest : null;
                var h = Changed;
                if (h != null) h();
                if (done != null) done(Available, error ?? (latest != null && Available == null ? "Vous avez la dernière version (" + AppVersion.Current + ")." : null));
            }) { IsBackground = true, Name = "Vérification des mises à jour" }.Start();
        }

        /// <summary>
        /// Télécharge le setup de la nouvelle version, vérifie son intégrité (taille + empreinte SHA-256 publiée
        /// avec la Release) puis le lance. Le setup ferme l'application, la met à jour et conserve les réglages.
        /// </summary>
        public static string DownloadAndInstall(UpdateInfo u, Action<int> progress)
        {
            if (u == null || u.SetupUrl == null) return "Cette version ne contient pas d'installateur.";
            if (!u.SetupUrl.StartsWith("https://github.com/" + AppVersion.Repo + "/", StringComparison.OrdinalIgnoreCase))
                return "Lien de téléchargement inattendu : mise à jour annulée.";
            string path = Path.Combine(Path.GetTempPath(), "MidiSoundController-Setup-" + u.Version + ".exe");
            try
            {
                using (var wc = Client())
                {
                    var finished = new ManualResetEvent(false);
                    Exception err = null;
                    wc.DownloadProgressChanged += (s, e) => progress(e.ProgressPercentage);
                    wc.DownloadFileCompleted += (s, e) => { err = e.Error; finished.Set(); };
                    wc.DownloadFileAsync(new Uri(u.SetupUrl), path);
                    finished.WaitOne();
                    if (err != null) throw err;
                }
                var len = new FileInfo(path).Length;
                if (u.SetupSize > 0 && len != u.SetupSize) { File.Delete(path); return "Téléchargement incomplet : mise à jour annulée."; }
                if (u.ChecksumUrl != null)
                {
                    string expected;
                    using (var wc = Client()) expected = wc.DownloadString(u.ChecksumUrl).Trim().Split(' ', '\t', '\r', '\n')[0];
                    string actual;
                    using (var sha = SHA256.Create())
                    using (var f = File.OpenRead(path))
                        actual = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
                    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(path);
                        return "Le fichier téléchargé ne correspond pas à l'empreinte publiée : mise à jour annulée.";
                    }
                }
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return null;
            }
            catch (Exception e) { return "Échec du téléchargement : " + e.Message; }
        }
    }
}
