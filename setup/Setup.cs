using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ControlCenterK - Installation")]
[assembly: AssemblyProduct("ControlCenterK")]

namespace ControlCenterKSetup
{
    /// <summary>
    /// Installateur / désinstallateur de ControlCenterK.
    /// Installation par utilisateur (pas de droits administrateur) dans %LOCALAPPDATA%\Programs.
    /// L'application est embarquée dans cet exe (ressource "payload.exe").
    /// </summary>
    static class Setup
    {
        public const string AppName = "ControlCenterK";
        public const string Version = ControlCenterK.AppVersion.Current;
        public const string ExeName = "ControlCenterK.exe";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ControlCenterK";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValue = "ControlCenterK";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = Array.Exists(args, a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
                || Path.GetFileName(Application.ExecutablePath).Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase);
            Application.Run(new SetupForm(uninstall));
        }

        public static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName); }
        }

        /// <summary>Dossier de la version déjà installée (ou null).</summary>
        public static string InstalledDir
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    {
                        var d = k == null ? null : k.GetValue("InstallLocation") as string;
                        return d != null && File.Exists(Path.Combine(d, ExeName)) ? d : null;
                    }
                }
                catch { return null; }
            }
        }

        /// <summary>Ancien emplacement des réglages (jusqu'à la version 1.1.0).</summary>
        public static string AppDataConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ControlCenterK"); }
        }

        /// <summary>Dossier des réglages : même règle que l'application (dossier choisi, sinon "config" à côté du .exe).</summary>
        public static string ConfigDir(string installDir)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\ControlCenterK"))
                {
                    var d = k == null ? null : k.GetValue("ConfigFolder") as string;
                    if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
                }
            }
            catch { }
            string local = Path.Combine(installDir, "config");
            return File.Exists(Path.Combine(local, "config.json")) ? local : AppDataConfigDir;
        }

        /// <summary>Supprime les fichiers de réglages d'un dossier, puis le dossier s'il est vide (jamais les autres fichiers de l'utilisateur).</summary>
        static void DeleteSettings(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (var f in Directory.GetFiles(folder, "config*.json*")) try { File.Delete(f); } catch { }
                if (Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
            }
            catch { }
        }

        /// <summary>Ferme proprement l'application si elle tourne (elle enregistre ses réglages avant de quitter).</summary>
        const string Legacy = "MidiSoundController"; // nom de l'application avant ControlCenterK

        static Process[] Running()
        {
            var l = new List<Process>(Process.GetProcessesByName("ControlCenterK"));
            l.AddRange(Process.GetProcessesByName(Legacy));
            return l.ToArray();
        }

        public static bool CloseRunningApp(IWin32Window owner)
        {
            if (Running().Length == 0) return true;
            foreach (var name in new[] { "ControlCenterK", Legacy })
            {
                EventWaitHandle q;
                if (EventWaitHandle.TryOpenExisting(@"Local\" + name + ".Quit", out q)) using (q) q.Set();
            }
            for (int i = 0; i < 40 && Running().Length > 0; i++) Thread.Sleep(150);
            var left = Running();
            if (left.Length == 0) return true;
            if (MessageBox.Show(owner, AppName + " est encore en cours d'exécution.\nLe fermer de force pour continuer ?", AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
            foreach (var p in left) try { p.Kill(); p.WaitForExit(3000); } catch { }
            return Running().Length == 0;
        }

        public static void Shortcut(string lnk, string target, string args, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            var t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            try
            {
                dynamic sc = shell.CreateShortcut(lnk);
                sc.TargetPath = target;
                sc.Arguments = args ?? "";
                sc.WorkingDirectory = Path.GetDirectoryName(target);
                sc.Description = description;
                sc.IconLocation = target + ",0";
                sc.Save();
                Marshal.FinalReleaseComObject(sc);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        public static string StartMenuLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); }
        }

        public static string DesktopLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); }
        }

        public static bool StartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && (k.GetValue(RunValue) != null || k.GetValue(Legacy) != null);
        }

        /// <summary>Installe (ou met à jour) l'application.</summary>
        public static void Install(string dir, bool desktop, bool startup, Action<string> step)
        {
            step("Copie des fichiers…");
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, ExeName);
            using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.exe"))
            using (var dst = File.Create(exe))
                src.CopyTo(dst);
            string uninstaller = Path.Combine(dir, "uninstall.exe");
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, uninstaller, true);

            step("Création des raccourcis…");
            Shortcut(StartMenuLink, exe, null, "Mixeur audio et contrôleur MIDI");
            if (desktop) Shortcut(DesktopLink, exe, null, "Mixeur audio et contrôleur MIDI");
            else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);

            step("Enregistrement dans Windows…");
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", AppName);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + uninstaller + "\"");
                k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)((new FileInfo(exe).Length + new FileInfo(uninstaller).Length) / 1024), RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                k.DeleteValue(Legacy, false); // ancienne entrée (avant le renommage)
                if (startup) k.SetValue(RunValue, "\"" + exe + "\" --minimized");
                else k.DeleteValue(RunValue, false);
            }
        }

        /// <summary>Désinstalle l'application. Les réglages ne sont supprimés que si demandé.</summary>
        public static void Uninstall(string dir, bool removeSettings, Action<string> step)
        {
            string config = ConfigDir(dir);
            step("Retrait des périphériques virtuels…");
            ForgetVirtualDevices(config);

            step("Suppression des raccourcis et des entrées Windows…");
            foreach (var l in new[] { StartMenuLink, DesktopLink }) try { if (File.Exists(l)) File.Delete(l); } catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    var v = k == null ? null : k.GetValue(RunValue) as string;
                    if (v != null) k.DeleteValue(RunValue, false);
                }
            }
            catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

            step("Suppression des fichiers…");
            try { File.Delete(Path.Combine(dir, ExeName)); } catch { }
            if (removeSettings)
            {
                DeleteSettings(config);
                DeleteSettings(Path.Combine(dir, "config"));
                DeleteSettings(AppDataConfigDir);
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\ControlCenterK", false); } catch { }
            }

            // uninstall.exe ne peut pas s'effacer lui-même : un petit script le fait une fois fermé
            string self = Application.ExecutablePath;
            if (self.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + self + "\" & rmdir \"" + dir + "\"")
                    { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            else
            {
                // jamais de suppression récursive : le sous-dossier "config" (réglages conservés) doit rester
                try { File.Delete(Path.Combine(dir, "uninstall.exe")); } catch { }
                try { Directory.Delete(dir, false); } catch { }
            }
        }

        /// <summary>Demande au pilote USB/IP d'arrêter de rattacher les périphériques virtuels créés par l'app.</summary>
        static void ForgetVirtualDevices(string configDir)
        {
            try
            {
                string usbip = null;
                foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
                    if (!string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, "USBip", "usbip.exe"))) usbip = Path.Combine(root, "USBip", "usbip.exe");
                string cfg = Path.Combine(configDir, "config.json");
                if (!File.Exists(cfg)) cfg = Path.Combine(Path.GetDirectoryName(AppDataConfigDir), Legacy, "config.json");
                if (usbip == null || !File.Exists(cfg)) return;
                var root2 = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(cfg));
                var router = root2.ContainsKey("Router") ? root2["Router"] as Dictionary<string, object> : null;
                var virtuals = router != null && router.ContainsKey("Virtuals") ? router["Virtuals"] as ArrayList : null;
                if (virtuals == null || virtuals.Count == 0) return;
                foreach (Dictionary<string, object> v in virtuals)
                    Run(usbip, "attach -r 127.0.0.1 -b 1-" + Convert.ToInt32(v["DevNum"]) + " -x");
                Run(usbip, "port --stash");
            }
            catch { }
        }

        static void Run(string exe, string args)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false }))
                    p.WaitForExit(10000);
            }
            catch { }
        }
    }

    /// <summary>Fenêtre d'installation sombre, dans le style de l'application.</summary>
    class SetupForm : Form
    {
        static readonly Color Bg = Color.FromArgb(17, 18, 23), Card = Color.FromArgb(27, 30, 37), Surface = Color.FromArgb(36, 40, 49),
            Border = Color.FromArgb(50, 55, 67), Text1 = Color.FromArgb(232, 234, 240), Muted = Color.FromArgb(142, 148, 165),
            Accent = Color.FromArgb(76, 141, 255), Green = Color.FromArgb(52, 199, 137), Red = Color.FromArgb(255, 84, 98);

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly bool uninstall;
        readonly Panel body;
        readonly float scale;
        int y;

        int S(float v) { return (int)Math.Round(v * scale); }

        public SetupForm(bool uninstall)
        {
            this.uninstall = uninstall;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            Text = Setup.AppName + (uninstall ? " — Désinstallation" : " — Installation");
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BackColor = Bg;
            ForeColor = Text1;
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(600), S(440));
            body = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
            Controls.Add(body);
            if (uninstall) PageUninstall(); else PageInstall();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            DwmSetWindowAttribute(Handle, 20, ref on, 4);
            int col = ColorTranslator.ToWin32(Bg);
            DwmSetWindowAttribute(Handle, 35, ref col, 4);
        }

        #region Construction de l'interface

        void Clear()
        {
            body.Controls.Clear();
            y = S(28);
            var logo = new Panel { Bounds = new Rectangle(S(32), y, S(48), S(48)), BackColor = Bg };
            logo.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                try { using (var ic = new Icon(Icon, S(48), S(48))) e.Graphics.DrawIcon(ic, new Rectangle(0, 0, S(48), S(48))); } catch { }
            };
            body.Controls.Add(logo);
            Lbl(Setup.AppName, new Font("Segoe UI Semibold", 16f), Text1, S(94), y + S(2));
            Lbl("Version " + Setup.Version, Font, Muted, S(96), y + S(34));
            y += S(80);
        }

        Label Lbl(string text, Font f, Color c, int x, int top, int maxW = 0)
        {
            var l = new Label { Text = text, Font = f, ForeColor = c, BackColor = Bg, AutoSize = true, Location = new Point(x, top), UseMnemonic = false };
            if (maxW > 0) l.MaximumSize = new Size(maxW, 0);
            body.Controls.Add(l);
            return l;
        }

        Button Btn(string text, bool primary, int x, int top)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface, ForeColor = primary ? Color.White : Text1,
                Font = new Font("Segoe UI Semibold", 9.5f), Cursor = Cursors.Hand, UseMnemonic = false,
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(100, 160, 255) : Border;
            b.Size = new Size(Math.Max(S(110), TextRenderer.MeasureText(text, b.Font).Width + S(32)), S(36));
            b.Location = new Point(x, top);
            body.Controls.Add(b);
            return b;
        }

        CheckBox Chk(string text, bool value)
        {
            var c = new CheckBox { Text = text, Checked = value, ForeColor = Text1, BackColor = Bg, AutoSize = true, FlatStyle = FlatStyle.Flat, Location = new Point(S(32), y) };
            c.FlatAppearance.BorderColor = Border;
            c.FlatAppearance.CheckedBackColor = Accent;
            body.Controls.Add(c);
            y = c.Bottom + S(10);
            return c;
        }

        #endregion

        #region Pages

        void PageInstall()
        {
            Clear();
            string existing = Setup.InstalledDir;
            Lbl(existing != null ? "Une version est déjà installée : elle va être mise à jour. Vos réglages sont conservés."
                                 : "Mixeur audio Windows, contrôleur MIDI (Korg nanoKONTROL2) et routage façon console.",
                Font, Muted, S(32), y, S(536));
            y += S(46);

            Lbl("DOSSIER D'INSTALLATION", new Font("Segoe UI", 7.5f, FontStyle.Bold), Muted, S(32), y);
            y += S(22);
            var box = new TextBox { Text = existing ?? Setup.DefaultDir, BackColor = Surface, ForeColor = Text1, BorderStyle = BorderStyle.FixedSingle, Bounds = new Rectangle(S(32), y, S(420), S(30)) };
            body.Controls.Add(box);
            var browse = Btn("Parcourir…", false, S(460), y - S(3));
            browse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { SelectedPath = box.Text, Description = "Dossier d'installation" })
                    if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = Path.Combine(dlg.SelectedPath, dlg.SelectedPath.EndsWith(Setup.AppName) ? "" : Setup.AppName).TrimEnd('\\');
            };
            y += S(50);

            var desktop = Chk("Créer un raccourci sur le Bureau", existing == null || File.Exists(Setup.DesktopLink));
            var startup = Chk("Démarrer avec Windows (réduit dans la zone de notification)", existing == null || Setup.StartupEnabled());
            var launch = Chk("Lancer " + Setup.AppName + " à la fin", true);

            var go = Btn(existing != null ? "Mettre à jour" : "Installer", true, S(600 - 32 - 140), S(440 - 64));
            go.Width = S(140);
            var cancel = Btn("Annuler", false, go.Left - S(118), go.Top);
            cancel.Click += (s, e) => Close();
            AcceptButton = go;
            go.Click += (s, e) =>
            {
                string dir = box.Text.Trim();
                if (dir.Length == 0) return;
                if (!Setup.CloseRunningApp(this)) return;
                RunStep(() => Setup.Install(dir, desktop.Checked, startup.Checked, Status), () =>
                {
                    if (launch.Checked) try { Process.Start(Path.Combine(dir, Setup.ExeName)); } catch { }
                    PageDone("Installation terminée",
                        Setup.AppName + " est installé dans :\n" + dir + "\n\nVous le trouverez dans le menu Démarrer" + (desktop.Checked ? " et sur le Bureau." : ".") +
                        "\nPour le désinstaller : Paramètres Windows → Applications installées.");
                });
            };
        }

        void PageUninstall()
        {
            Clear();
            string dir = Setup.InstalledDir ?? Path.GetDirectoryName(Application.ExecutablePath);
            Lbl("Désinstaller " + Setup.AppName + " ?", new Font("Segoe UI Semibold", 11f), Text1, S(32), y);
            y += S(32);
            Lbl("L'application, ses raccourcis et son démarrage automatique seront retirés.\nDossier : " + dir, Font, Muted, S(32), y, S(536));
            y += S(64);
            var settings = Chk("Supprimer aussi mes réglages (profils, routage, thèmes…)", false);
            Lbl("Le pilote USB/IP (usbip-win2), s'il est installé, n'est pas retiré : désinstallez-le séparément si vous n'en avez plus besoin.",
                new Font("Segoe UI", 8.5f), Muted, S(32), y, S(536));

            var go = Btn("Désinstaller", true, S(600 - 32 - 140), S(440 - 64));
            go.Width = S(140);
            go.BackColor = Red;
            go.FlatAppearance.BorderColor = Red;
            go.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 120, 130);
            var cancel = Btn("Annuler", false, go.Left - S(118), go.Top);
            cancel.Click += (s, e) => Close();
            go.Click += (s, e) =>
            {
                if (!Setup.CloseRunningApp(this)) return;
                RunStep(() => Setup.Uninstall(dir, settings.Checked, Status),
                    () => PageDone("Désinstallation terminée", Setup.AppName + " a été retiré de cet ordinateur." +
                        (settings.Checked ? "" : "\nVos réglages sont conservés (ils seront repris en cas de réinstallation).")));
            };
        }

        Label statusLabel;

        void Status(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Status), text); return; }
            if (statusLabel != null) statusLabel.Text = text;
        }

        void RunStep(Action work, Action done)
        {
            Clear();
            statusLabel = Lbl("Veuillez patienter…", new Font("Segoe UI", 10.5f), Text1, S(32), y + S(20), S(536));
            var bar = new ProgressBar { Style = ProgressBarStyle.Marquee, Bounds = new Rectangle(S(32), y + S(64), S(536), S(6)) };
            body.Controls.Add(bar);
            var t = new Thread(() =>
            {
                Exception err = null;
                try { work(); } catch (Exception e) { err = e; }
                BeginInvoke(new Action(() =>
                {
                    if (err == null) done();
                    else PageDone("Échec", "Une erreur est survenue :\n" + err.Message, true);
                }));
            }) { IsBackground = true };
            t.Start();
        }

        void PageDone(string title, string text, bool error = false)
        {
            Clear();
            Lbl((error ? "✕  " : "✓  ") + title, new Font("Segoe UI Semibold", 13f), error ? Red : Green, S(32), y);
            y += S(42);
            Lbl(text, Font, Text1, S(32), y, S(536));
            var close = Btn("Fermer", true, S(600 - 32 - 140), S(440 - 64));
            close.Width = S(140);
            close.Click += (s, e) => Close();
            AcceptButton = close;
        }

        #endregion
    }
}
