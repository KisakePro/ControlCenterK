using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("ControlCenterK")]
[assembly: System.Reflection.AssemblyProduct("ControlCenterK")]

namespace ControlCenterK
{
    static class Program
    {
        public static Icon AppIcon, TrayIcon;

        public static void MakeIcons()
        {
            AppIcon = Theme.MakeIcon(Theme.S(32));
            TrayIcon = Theme.MakeIcon(Theme.S(16));
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (Array.IndexOf(args, "--quit") >= 0)
            {
                // Demande à l'instance en cours de se fermer proprement (utilisé par l'installateur)
                EventWaitHandle q;
                if (EventWaitHandle.TryOpenExisting(@"Local\ControlCenterK.Quit", out q)) using (q) q.Set();
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, @"Local\ControlCenterK.Instance", out created))
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ControlCenterK.Show"))
            {
                if (!created)
                {
                    show.Set(); // une instance tourne déjà : on lui demande d'afficher sa fenêtre
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Theme.Init();
                Application.Run(new TrayApp(args, show));
            }
        }
    }

    /// <summary>
    /// Modules activables : chacun ne charge ses ressources (port MIDI, threads audio, pages) que s'il est activé.
    /// </summary>
    static class Host
    {
        public static AppConfig Cfg;
        public static Engine Engine;
        public static AudioRouter Router;
        public static event Action ModulesChanged;

        public static void ApplyModules()
        {
            // Contrôleur MIDI : ouvre / ferme le port (le moteur se met en veille sans port ouvert)
            Engine.OpenMidi();

            // Routage audio : création / destruction complète du moteur de routage
            if (Cfg.ModRouter)
            {
                VirtualHost.Sync(Cfg);
                if (Router == null) Router = new AudioRouter(Cfg);
                Engine.Router = Router;
                Router.Sync();
            }
            else if (Router != null)
            {
                Engine.Router = null;
                Router.Dispose();
                Router = null;
                VirtualHost.Stop();
            }
            var h = ModulesChanged;
            if (h != null) h();
        }

        public static void Shutdown()
        {
            if (Router != null) { Engine.Router = null; Router.Dispose(); Router = null; }
            VirtualHost.Stop();
        }
    }

    /// <summary>Application résidente : icône de notification + moteur. La fenêtre est créée à la demande.</summary>
    sealed class TrayApp : ApplicationContext
    {
        readonly AppConfig cfg;
        readonly Engine engine;
        readonly NotifyIcon tray;
        readonly HostWindow host;
        readonly ToolStripMenuItem statusItem;
        readonly RegisteredWaitHandle showWait;
        MainForm main;

        public TrayApp(string[] args, EventWaitHandle show)
        {
            cfg = AppConfig.Load();
            Theme.Apply(cfg.Theme);
            Program.MakeIcons();
            Theme.Changed += () =>
            {
                Program.MakeIcons();
                if (tray != null) { tray.Icon = Program.TrayIcon; tray.ContextMenuStrip.BackColor = Theme.Card; }
            };
            cfg.StartWithWindows = Startup.IsEnabled(); // le registre fait foi (l'installateur peut l'avoir réglé)
            if (cfg.StartWithWindows) Startup.Apply(true); // met à jour le chemin si l'exe a été déplacé

            engine = new Engine(cfg);
            engine.Start();
            // Positions des faders : enregistrées quelle que soit la façon dont l'app se ferme
            Microsoft.Win32.SystemEvents.SessionEnding += (s, e) => engine.FlushValues();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => engine.FlushValues();
            Application.ApplicationExit += (s, e) => engine.FlushValues();
            quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ControlCenterK.Quit");
            Host.Cfg = cfg;
            Host.Engine = engine;
            Host.ApplyModules();

            host = new HostWindow();
            host.DeviceChanged += engine.OnDeviceChange;
            showWait = ThreadPool.RegisterWaitForSingleObject(show, (s, t) => host.BeginInvoke(new Action(ShowMain)), null, -1, false);
            quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvent, (s, t) => host.BeginInvoke(new Action(Quit)), null, -1, true);

            var menu = DarkMenu.Create();
            statusItem = new ToolStripMenuItem("") { Enabled = false };
            var open = new ToolStripMenuItem("Ouvrir", Theme.GlyphImage(Glyphs.Mixer, Theme.Text));
            open.Font = Theme.Ui(9.5f, FontStyle.Bold);
            open.Click += (s, e) => ShowMain();
            var quit = new ToolStripMenuItem("Quitter", Theme.GlyphImage(Glyphs.Close, Theme.Text));
            quit.Click += (s, e) => Quit();
            var profiles = DarkMenu.SubItem("Profil", Glyphs.Mixer);
            updateItem = new ToolStripMenuItem("", Theme.GlyphImage("\uE896", Theme.Green)) { Visible = false };
            updateItem.Click += (s, e) => ShowMain(true);
            menu.Items.AddRange(new ToolStripItem[] { statusItem, updateItem, new ToolStripSeparator(), open, profiles, new ToolStripSeparator(), quit });
            menu.Opening += (s, e) =>
            {
                profiles.DropDownItems.Clear();
                lock (AppConfig.Sync)
                {
                    profiles.Text = "Profil : " + cfg.ActiveProfile;
                    foreach (var p in cfg.Profiles)
                    {
                        string name = p.Name;
                        var it = new ToolStripMenuItem(name) { Checked = name == cfg.ActiveProfile };
                        it.Click += (s2, e2) => engine.SwitchProfile(name);
                        profiles.DropDownItems.Add(it);
                    }
                }
            };

            tray = new NotifyIcon { Icon = Program.TrayIcon, Text = "ControlCenterK", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
            engine.StateChanged += () => { try { host.BeginInvoke(new Action(UpdateStatus)); } catch { } };
            Host.ModulesChanged += () => { try { host.BeginInvoke(new Action(UpdateStatus)); } catch { } };
            UpdateStatus();

            bool minimized = cfg.StartMinimized || Array.IndexOf(args, "--minimized") >= 0;
            if (!minimized) ShowMain();
            else TrimLater(3000);

            // Mises à jour : un contrôle discret 30 s après le démarrage (au plus une fois par jour)
            tray.BalloonTipClicked += (s, e) => ShowMain(true);
            Updater.Changed += () => { try { host.BeginInvoke(new Action(OnUpdateAvailable)); } catch { } };
            var later = new System.Windows.Forms.Timer { Interval = 30000 };
            later.Tick += (s, e) => { later.Stop(); later.Dispose(); Updater.CheckAsync(cfg, false, null); };
            later.Start();
        }

        ToolStripMenuItem updateItem;

        void OnUpdateAvailable()
        {
            var u = Updater.Available;
            updateItem.Visible = u != null;
            if (u == null) return;
            updateItem.Text = "Mise à jour disponible : " + u.Version;
            bool notify;
            lock (AppConfig.Sync) { notify = cfg.NotifiedVersion != u.Version; cfg.NotifiedVersion = u.Version; }
            if (notify)
            {
                cfg.Save();
                tray.ShowBalloonTip(8000, "Mise à jour disponible", "ControlCenterK " + u.Version + " est disponible. Cliquez pour l'installer.", ToolTipIcon.Info);
            }
        }

        void UpdateStatus()
        {
            string n = engine.MidiInName;
            statusItem.Text = !cfg.ModMidi ? (Host.Router != null ? "Routage audio actif" : "Modules désactivés")
                : n != null ? n + " connecté" : "Aucun contrôleur MIDI";
            string tip = "ControlCenterK — " + statusItem.Text;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void ShowMain() { ShowMain(false); }

        void ShowMain(bool settings)
        {
            if (main == null || main.IsDisposed)
            {
                main = new MainForm(engine);
                main.FormClosed += (s, e) => { main = null; TrimLater(800); };
                main.Show();
            }
            if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
            main.Activate();
            if (settings) main.OpenSettings();
        }

        static void TrimLater(int ms)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); Native.TrimMemory(); };
            t.Start();
        }

        EventWaitHandle quitEvent;
        RegisteredWaitHandle quitWait;

        void Quit()
        {
            if (quitWait != null) quitWait.Unregister(null);
            if (main != null) main.Close();
            showWait.Unregister(null);
            tray.Visible = false;
            tray.Dispose();
            Host.Shutdown();
            engine.Stop();
            host.Dispose();
            ExitThread();
        }
    }

    /// <summary>Fenêtre invisible qui reçoit les notifications de branchement USB / audio de Windows.</summary>
    sealed class HostWindow : Form
    {
        public event Action DeviceChanged;
        readonly System.Windows.Forms.Timer debounce;
        IntPtr notify;

        public HostWindow()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            debounce = new System.Windows.Forms.Timer { Interval = 900 };
            debounce.Tick += (s, e) =>
            {
                debounce.Stop();
                var h = DeviceChanged;
                if (h != null) h();
            };
            CreateHandle();
            var filter = new Native.DEV_BROADCAST_DEVICEINTERFACE();
            filter.dbcc_size = System.Runtime.InteropServices.Marshal.SizeOf(filter);
            filter.dbcc_devicetype = 5; // DBT_DEVTYP_DEVICEINTERFACE
            notify = Native.RegisterDeviceNotification(Handle, ref filter, 0x4 /* DEVICE_NOTIFY_ALL_INTERFACE_CLASSES */);
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DEVICECHANGE)
            {
                debounce.Stop();
                debounce.Start();
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (notify != IntPtr.Zero) { Native.UnregisterDeviceNotification(notify); notify = IntPtr.Zero; }
            if (disposing) debounce.Dispose();
            base.Dispose(disposing);
        }
    }
}
