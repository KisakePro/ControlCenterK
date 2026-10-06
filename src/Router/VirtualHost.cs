using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ControlCenterK
{
    /// <summary>Serveur USB/IP local (127.0.0.1) qui expose les cartes son virtuelles au pilote usbip-win2.</summary>
    sealed class UsbIpServer : IDisposable
    {
        public const int Port = 3240;
        readonly Func<List<VirtualAudioDevice>> devices;
        TcpListener listener;
        Thread acceptThread;
        volatile bool stopping;
        readonly List<TcpClient> clients = new List<TcpClient>();

        public string Error { get; private set; }

        public UsbIpServer(Func<List<VirtualAudioDevice>> devices) { this.devices = devices; }

        public bool Start()
        {
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
            }
            catch (Exception e)
            {
                Error = "Port " + Port + " déjà utilisé (" + e.Message + ")";
                return false;
            }
            acceptThread = new Thread(Accept) { IsBackground = true, Name = "Serveur USB/IP" };
            acceptThread.Start();
            return true;
        }

        void Accept()
        {
            while (!stopping)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { break; }
                c.NoDelay = true;
                lock (clients) clients.Add(c);
                new Thread(() => Handle(c)) { IsBackground = true, Name = "Client USB/IP" }.Start();
            }
        }

        void Handle(TcpClient c)
        {
            try
            {
                var s = c.GetStream();
                var op = new byte[8];
                Be.ReadExact(s, op, 8);
                int code = op[2] << 8 | op[3];
                if (code == 0x8005) // OP_REQ_DEVLIST
                {
                    var list = devices();
                    var ms = new MemoryStream();
                    var h = new byte[12];
                    Be.Put16(h, 0, 0x0111);
                    Be.Put16(h, 2, 0x0005);
                    Be.Put32(h, 8, (uint)list.Count);
                    ms.Write(h, 0, 12);
                    foreach (var d in list)
                    {
                        var info = d.UsbIpDeviceInfo();
                        ms.Write(info, 0, info.Length);
                        // interfaces : AC, AS lecture, AS enregistrement
                        var ifs = d.UsbIpInterfaces();
                        ms.Write(ifs, 0, ifs.Length);
                    }
                    var b = ms.ToArray();
                    s.Write(b, 0, b.Length);
                }
                else if (code == 0x8003) // OP_REQ_IMPORT
                {
                    var busid = new byte[32];
                    Be.ReadExact(s, busid, 32);
                    string id = Encoding.ASCII.GetString(busid).TrimEnd('\0');
                    var dev = devices().Find(d => d.BusId == id && !d.Connected);
                    var h = new byte[8];
                    Be.Put16(h, 0, 0x0111);
                    Be.Put16(h, 2, 0x0003);
                    Be.Put32(h, 4, dev == null ? 1u : 0u);
                    s.Write(h, 0, 8);
                    if (dev != null)
                    {
                        var info = dev.UsbIpDeviceInfo();
                        s.Write(info, 0, info.Length);
                        dev.Serve(s); // bloque jusqu'à la déconnexion
                    }
                }
            }
            catch { }
            finally
            {
                lock (clients) clients.Remove(c);
                try { c.Close(); } catch { }
            }
        }

        public void Dispose()
        {
            stopping = true;
            try { if (listener != null) listener.Stop(); } catch { }
            lock (clients) foreach (var c in clients) try { c.Close(); } catch { }
        }
    }

    /// <summary>
    /// Gestion des périphériques virtuels : création / suppression, attache via usbip.exe,
    /// détection et installation du composant usbip-win2 (pilote signé Microsoft, BSD-2).
    /// </summary>
    static class VirtualHost
    {
        static UsbIpServer server;
        static readonly List<VirtualAudioDevice> devices = new List<VirtualAudioDevice>();
        public static string LastError;

        public const string ProjectUrl = "https://github.com/vadimgrn/usbip-win2";

        public static string UsbIpExe
        {
            get
            {
                foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    var p = Path.Combine(root, "USBip", "usbip.exe");
                    if (File.Exists(p)) return p;
                }
                return null;
            }
        }

        public static bool Installed { get { return UsbIpExe != null; } }

        public static List<VirtualAudioDevice> Devices
        {
            get { lock (devices) return new List<VirtualAudioDevice>(devices); }
        }

        public static VirtualAudioDevice Find(string deviceId)
        {
            if (deviceId == null || !deviceId.StartsWith("vdev:")) return null;
            lock (devices) return devices.Find(d => d.DeviceId == deviceId);
        }

        public static bool Running { get { return server != null; } }

        /// <summary>
        /// Met les périphériques publiés en accord avec la config, et démarre / arrête le serveur.
        /// Les objets existants sont conservés (la console garde une référence vers eux).
        /// </summary>
        public static void Sync(AppConfig cfg)
        {
            List<VirtualDef> defs;
            lock (AppConfig.Sync) defs = new List<VirtualDef>(cfg.Router.Virtuals);
            lock (devices)
            {
                foreach (var d in devices.FindAll(x => !defs.Exists(v => v.Id == x.Id))) d.Disconnect();
                devices.RemoveAll(x => !defs.Exists(v => v.Id == x.Id));
                foreach (var v in defs)
                    if (!devices.Exists(x => x.Id == v.Id)) devices.Add(new VirtualAudioDevice(v.Id, v.Name, v.DevNum, v.Kind));
                if (devices.Count > 0 && server == null)
                {
                    server = new UsbIpServer(() => Devices);
                    if (!server.Start()) { LastError = server.Error; server = null; }
                    else LastError = null;
                }
                else if (devices.Count == 0 && server != null) { server.Dispose(); server = null; }
            }
        }

        public static void Stop()
        {
            lock (devices)
            {
                if (server != null) { server.Dispose(); server = null; }
                foreach (var d in devices) d.Disconnect();
                devices.Clear();
            }
        }

        /// <summary>Crée un périphérique virtuel et demande au pilote de l'attacher (de façon persistante).</summary>
        public static VirtualDef Create(AppConfig cfg, string name, string kind, out string error)
        {
            VirtualDef def;
            lock (AppConfig.Sync)
            {
                int n = 1;
                while (cfg.Router.Virtuals.Exists(v => v.DevNum == n)) n++;
                def = new VirtualDef { Name = name, DevNum = n, Kind = kind };
                cfg.Router.Virtuals.Add(def);
            }
            cfg.Save();
            Sync(cfg);
            if (server == null) { error = LastError; return def; }
            error = RunUsbIp(new[] { "attach -r 127.0.0.1 -b 1-" + def.DevNum, "port --stash" });
            return def;
        }

        /// <summary>Supprime un périphérique virtuel (le pilote cesse de le rattacher).</summary>
        public static string Delete(AppConfig cfg, VirtualDef def)
        {
            lock (AppConfig.Sync) cfg.Router.Virtuals.Remove(def);
            cfg.Save();
            var cmds = new List<string> { "attach -r 127.0.0.1 -b 1-" + def.DevNum + " -x", "detach --all" };
            lock (AppConfig.Sync) foreach (var v in cfg.Router.Virtuals) cmds.Add("attach -r 127.0.0.1 -b 1-" + v.DevNum);
            cmds.Add("port --stash");
            Sync(cfg);
            return RunUsbIp(cmds.ToArray());
        }

        /// <summary>Rattache tous les périphériques (bouton « Reconnecter »).</summary>
        public static string Reattach(AppConfig cfg)
        {
            var cmds = new List<string>();
            lock (AppConfig.Sync) foreach (var v in cfg.Router.Virtuals) cmds.Add("attach -r 127.0.0.1 -b 1-" + v.DevNum);
            if (cmds.Count == 0) return null;
            cmds.Add("port --stash");
            return RunUsbIp(cmds.ToArray());
        }

        /// <summary>Exécute des commandes usbip.exe ; demande l'élévation (UAC) si nécessaire.</summary>
        static string RunUsbIp(string[] commands)
        {
            string exe = UsbIpExe;
            if (exe == null) return "Le composant USB/IP n'est pas installé.";
            // 1) essai sans élévation
            bool needAdmin = false;
            var output = new StringBuilder();
            foreach (var c in commands)
            {
                int code = Run(exe, c, output);
                if (code != 0)
                {
                    string o = output.ToString();
                    if (o.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("refus", StringComparison.OrdinalIgnoreCase) >= 0
                        || o.IndexOf("administrat", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("elevat", StringComparison.OrdinalIgnoreCase) >= 0
                        || o.IndexOf("0x5", StringComparison.OrdinalIgnoreCase) >= 0)
                    { needAdmin = true; break; }
                    if (c.StartsWith("port")) return "usbip " + c + " : " + o.Trim();
                }
            }
            if (!needAdmin) return null;
            // 2) une seule demande UAC pour toutes les commandes
            string log = Path.Combine(Path.GetTempPath(), "msc_usbip.log");
            var sb = new StringBuilder("/c \"");
            for (int i = 0; i < commands.Length; i++)
                sb.Append((i > 0 ? " & " : "") + "\"" + exe + "\" " + commands[i] + (i == 0 ? " > \"" : " >> \"") + log + "\" 2>&1");
            sb.Append("\"");
            try
            {
                var p = Process.Start(new ProcessStartInfo("cmd.exe", sb.ToString()) { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
                p.WaitForExit(30000);
            }
            catch (Exception e) { return "Autorisation administrateur refusée (" + e.Message + ")."; }
            return null;
        }

        static int Run(string exe, string args, StringBuilder output)
        {
            output.Clear();
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    output.Append(p.StandardOutput.ReadToEnd());
                    output.Append(p.StandardError.ReadToEnd());
                    p.WaitForExit(15000);
                    return p.ExitCode;
                }
            }
            catch (Exception e) { output.Append(e.Message); return -1; }
        }

        /// <summary>Ouvre la page officielle des versions d'usbip-win2 : l'utilisateur télécharge et installe lui-même.</summary>
        public static void OpenDownloadPage()
        {
            try { Process.Start(ProjectUrl + "/releases/latest"); } catch { }
        }
    }
}
