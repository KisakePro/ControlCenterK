using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MidiSoundController
{
    /// <summary>
    /// Carte son USB virtuelle (USB Audio Class 1.0, 48 kHz, stéréo 16 bits), exposée à Windows via USB/IP.
    /// Windows la prend pour un vrai périphérique et charge son pilote audio intégré : elle apparaît donc dans
    /// tous les logiciels sous la forme « Haut-parleurs (Nom) » et « Microphone (Nom) ».
    ///  - ce que les logiciels jouent sur les haut-parleurs arrive ici (point de terminaison isochrone OUT 0x01) ;
    ///  - ce que la console envoie est lu par les logiciels sur le micro (point de terminaison isochrone IN 0x82).
    /// </summary>
    public sealed class VirtualAudioDevice
    {
        internal static readonly SampleFormat Format = new SampleFormat { Rate = 48000, Channels = 2, Bits = 16, BlockAlign = 4 };
        const int FramesPerMs = 48, BytesPerFrame = 4, MaxPacket = 196;
        const int EPIPE = -32, ECONNRESET = -104;

        public readonly string Id, Name;
        public readonly int DevNum;
        public string BusId { get { return "1-" + DevNum; } }
        public string DeviceId { get { return "vdev:" + Id; } }
        public int DevIdValue { get { return (1 << 16) | DevNum; } }

        internal volatile VirtualSourceNode Source; // console : entrée alimentée par les haut-parleurs virtuels
        internal volatile VirtualSinkNode Sink;     // console : sortie vers le micro virtuel
        public volatile bool Connected;
        public volatile bool SpeakerOpen, MicOpen;

        readonly byte[] deviceDesc, configDesc;
        readonly string[] strings;

        /// <summary>Le périphérique a-t-il une sortie Windows (haut-parleurs) / une entrée Windows (micro) ?</summary>
        public readonly bool HasPlay, HasMic;
        readonly int playIface, micIface, numIfaces;

        public VirtualAudioDevice(string id, string name, int devNum, string kind)
        {
            Id = id;
            Name = name;
            DevNum = devNum;
            HasPlay = kind != VirtualDef.KindMic;
            HasMic = kind != VirtualDef.KindPlay;
            playIface = HasPlay ? 1 : -1;
            micIface = HasMic ? (HasPlay ? 2 : 1) : -1;
            numIfaces = 1 + (HasPlay ? 1 : 0) + (HasMic ? 1 : 0);
            strings = new[] { null, "MIDI Sound Controller", name, "MSC" + id.ToUpperInvariant() };
            deviceDesc = new byte[]
            {
                18, 1, 0x00, 0x02, 0, 0, 0, 64,
                0x09, 0x12,                     // idVendor 0x1209 (pid.codes, open source)
                0x01, 0x00,                     // idProduct 0x0001 (identifiant de test pid.codes)
                0x00, 0x01, 1, 2, 3, 1,
            };
            configDesc = BuildConfig();
        }

        byte[] BuildConfig()
        {
            var b = new List<byte>();
            // Configuration (longueur totale complétée à la fin)
            b.AddRange(new byte[] { 9, 2, 0, 0, (byte)numIfaces, 1, 0, 0x80, 50 });
            // Interface 0 : Audio Control
            b.AddRange(new byte[] { 9, 4, 0, 0, 0, 1, 1, 0, 0 });
            int acStart = b.Count;
            var header = new List<byte> { 0, 0x24, 1, 0x00, 0x01, 0, 0, (byte)(numIfaces - 1) };   // en-tête AC
            if (HasPlay) header.Add((byte)playIface);
            if (HasMic) header.Add((byte)micIface);
            header[0] = (byte)header.Count;
            b.AddRange(header);
            if (HasPlay)
            {
                b.AddRange(new byte[] { 12, 0x24, 2, 1, 0x01, 0x01, 0, 2, 0x03, 0x00, 0, 0 });    // IT 1 : flux USB (lecture)
                b.AddRange(new byte[] { 9, 0x24, 3, 2, 0x01, 0x03, 0, 1, 0 });                    // OT 2 : haut-parleurs
            }
            if (HasMic)
            {
                b.AddRange(new byte[] { 12, 0x24, 2, 3, 0x01, 0x02, 0, 2, 0x03, 0x00, 0, 0 });    // IT 3 : microphone
                b.AddRange(new byte[] { 9, 0x24, 3, 4, 0x01, 0x01, 0, 3, 0 });                    // OT 4 : flux USB (enregistrement)
            }
            int acLen = b.Count - acStart;
            b[acStart + 5] = (byte)acLen;
            b[acStart + 6] = (byte)(acLen >> 8);
            if (HasPlay) AddStreaming(b, playIface, 1, 0x01, 0x09); // lecture, EP 0x01 isochrone adaptatif
            if (HasMic) AddStreaming(b, micIface, 4, 0x82, 0x05);   // enregistrement, EP 0x82 isochrone asynchrone
            b[2] = (byte)b.Count;
            b[3] = (byte)(b.Count >> 8);
            return b.ToArray();
        }

        static void AddStreaming(List<byte> b, int iface, int terminal, int ep, int attrs)
        {
            b.AddRange(new byte[] { 9, 4, (byte)iface, 0, 0, 1, 2, 0, 0 });                    // alt 0 : aucun débit
            b.AddRange(new byte[] { 9, 4, (byte)iface, 1, 1, 1, 2, 0, 0 });                    // alt 1 : flux actif
            b.AddRange(new byte[] { 7, 0x24, 1, (byte)terminal, 1, 0x01, 0x00 });              // AS général : PCM
            b.AddRange(new byte[] { 11, 0x24, 2, 1, 2, 2, 16, 1, 0x80, 0xBB, 0x00 });          // Type I : 2 can., 16 bits, 48 kHz
            b.AddRange(new byte[] { 9, 5, (byte)ep, (byte)attrs, MaxPacket & 0xFF, MaxPacket >> 8, 1, 0, 0 });
            b.AddRange(new byte[] { 7, 0x25, 1, 0x01, 0, 0, 0 });                              // EP audio : contrôle de fréquence
        }

        /// <summary>Structure usbip_usb_device (312 octets) décrivant ce périphérique.</summary>
        internal byte[] UsbIpDeviceInfo()
        {
            var b = new byte[312];
            Encoding.ASCII.GetBytes("/sys/devices/msc/" + BusId).CopyTo(b, 0);
            Encoding.ASCII.GetBytes(BusId).CopyTo(b, 256);
            Be.Put32(b, 288, 1);
            Be.Put32(b, 292, (uint)DevNum);
            Be.Put32(b, 296, 2);                // USB_SPEED_FULL
            Be.Put16(b, 300, 0x1209);
            Be.Put16(b, 302, 0x0001);
            Be.Put16(b, 304, 0x0100);
            b[306] = 0; b[307] = 0; b[308] = 0; // classe définie par les interfaces
            b[309] = 1;                         // bConfigurationValue
            b[310] = 1;                         // bNumConfigurations
            b[311] = (byte)numIfaces;           // bNumInterfaces
            return b;
        }

        /// <summary>Liste des interfaces (classe, sous-classe, protocole, bourrage) pour OP_REP_DEVLIST.</summary>
        internal byte[] UsbIpInterfaces()
        {
            var b = new List<byte> { 1, 1, 0, 0 };
            for (int i = 1; i < numIfaces; i++) b.AddRange(new byte[] { 1, 2, 0, 0 });
            return b.ToArray();
        }

        #region Connexion USB/IP

        sealed class IsoUrb
        {
            public uint Seq, StartFrame, NumPackets;
            public bool In;
            public int[] Offset, Length;
            public byte[] Data;
            public double Due;
        }

        readonly object qLock = new object();
        readonly List<IsoUrb> pending = new List<IsoUrb>();
        double nextOut, nextIn;
        Stream stream;
        readonly object writeLock = new object();
        readonly Stopwatch clock = new Stopwatch();
        volatile bool serving;

        internal void Disconnect()
        {
            var s = stream;
            if (s != null) try { s.Close(); } catch { }
        }

        /// <summary>Traite les URB d'une connexion importée jusqu'à sa fermeture.</summary>
        internal void Serve(Stream s)
        {
            stream = s;
            serving = true;
            Connected = true;
            clock.Restart();
            var sched = new Thread(Scheduler) { IsBackground = true, Name = "Périphérique virtuel " + Name, Priority = ThreadPriority.Highest };
            sched.Start();
            var hdr = new byte[48];
            try
            {
                while (true)
                {
                    Be.ReadExact(s, hdr, 48);
                    uint cmd = Be.Get32(hdr, 0), seq = Be.Get32(hdr, 4), dir = Be.Get32(hdr, 12), ep = Be.Get32(hdr, 16);
                    if (cmd == 1)
                    {
                        uint len = Be.Get32(hdr, 24), startFrame = Be.Get32(hdr, 28), np = Be.Get32(hdr, 32);
                        var setup = new byte[8];
                        Array.Copy(hdr, 40, setup, 0, 8);
                        byte[] data = null;
                        if (dir == 0 && len > 0) { data = new byte[len]; Be.ReadExact(s, data, (int)len); }
                        bool iso = ep != 0 && np != 0xFFFFFFFF && np > 0 && np < 4096;
                        int[] off = null, plen = null;
                        if (iso)
                        {
                            var d = new byte[np * 16];
                            Be.ReadExact(s, d, d.Length);
                            off = new int[np];
                            plen = new int[np];
                            for (int i = 0; i < np; i++) { off[i] = (int)Be.Get32(d, i * 16); plen[i] = (int)Be.Get32(d, i * 16 + 4); }
                        }
                        if (ep == 0) Control(seq, setup, dir == 1, len, data, np);
                        else if (iso) Enqueue(new IsoUrb { Seq = seq, In = dir == 1, NumPackets = np, StartFrame = startFrame, Offset = off, Length = plen, Data = data });
                        else Reply(seq, EPIPE, null, 0, np, null, null, null);
                    }
                    else if (cmd == 2)
                    {
                        uint target = Be.Get32(hdr, 20);
                        int st = 0;
                        lock (qLock)
                        {
                            int i = pending.FindIndex(u => u.Seq == target);
                            if (i >= 0) { pending.RemoveAt(i); st = ECONNRESET; }
                        }
                        var r = new byte[48];
                        Be.Put32(r, 0, 4);
                        Be.Put32(r, 4, seq);
                        Be.Put32(r, 20, (uint)st);
                        Send(r, null);
                    }
                    else break;
                }
            }
            catch { }
            finally
            {
                serving = false;
                Connected = false;
                SpeakerOpen = MicOpen = false;
                lock (qLock) pending.Clear();
                sched.Join(500);
            }
        }

        void Send(byte[] header, byte[] tail)
        {
            lock (writeLock)
            {
                stream.Write(header, 0, header.Length);
                if (tail != null && tail.Length > 0) stream.Write(tail, 0, tail.Length);
                stream.Flush();
            }
        }

        /// <summary>Envoie un RET_SUBMIT (statut, données IN éventuelles, descripteurs isochrones éventuels).</summary>
        void Reply(uint seq, int status, byte[] data, int actual, uint np, int[] off, int[] len, int[] act)
        {
            var h = new byte[48];
            Be.Put32(h, 0, 3);
            Be.Put32(h, 4, seq);
            Be.Put32(h, 20, (uint)status);
            Be.Put32(h, 24, (uint)actual);
            Be.Put32(h, 28, 0);
            Be.Put32(h, 32, np);
            Be.Put32(h, 36, 0);
            int isoBytes = off == null ? 0 : off.Length * 16;
            var tail = new byte[(data != null ? actual : 0) + isoBytes];
            if (data != null && actual > 0) Array.Copy(data, 0, tail, 0, actual);
            if (off != null)
            {
                int p = tail.Length - isoBytes;
                for (int i = 0; i < off.Length; i++, p += 16)
                {
                    Be.Put32(tail, p, (uint)off[i]);
                    Be.Put32(tail, p + 4, (uint)len[i]);
                    Be.Put32(tail, p + 8, (uint)act[i]);
                    Be.Put32(tail, p + 12, 0);
                }
            }
            Send(h, tail);
        }

        #endregion

        #region Requêtes de contrôle (EP 0)

        int altSpeaker, altMic;

        void Control(uint seq, byte[] setup, bool dirIn, uint len, byte[] outData, uint np)
        {
            int type = setup[0], req = setup[1], value = setup[2] | setup[3] << 8, index = setup[4] | setup[5] << 8, wLength = setup[6] | setup[7] << 8;
            byte[] resp = null;
            bool ok = true;
            if ((type & 0x60) == 0) // requêtes standard
            {
                switch (req)
                {
                    case 6: // GET_DESCRIPTOR
                        int dt = value >> 8, di = value & 0xFF;
                        if (dt == 1) resp = deviceDesc;
                        else if (dt == 2) resp = configDesc;
                        else if (dt == 3) resp = StringDesc(di);
                        else ok = false; // qualificatif, BOS… : non pris en charge (STALL)
                        if (resp == null) ok = false;
                        break;
                    case 0: resp = new byte[2]; break;                 // GET_STATUS
                    case 8: resp = new byte[] { 1 }; break;            // GET_CONFIGURATION
                    case 10: resp = new byte[] { (byte)(index == playIface ? altSpeaker : index == micIface ? altMic : 0) }; break; // GET_INTERFACE
                    case 11:                                           // SET_INTERFACE
                        if (index == playIface) { altSpeaker = value; SpeakerOpen = value == 1; }
                        else if (index == micIface) { altMic = value; MicOpen = value == 1; lock (qLock) nextIn = 0; }
                        break;
                    case 1: case 3: case 5: case 9: break;             // CLEAR/SET_FEATURE, SET_ADDRESS, SET_CONFIGURATION
                    default: ok = false; break;
                }
            }
            else if ((type & 0x60) == 0x20 && (type & 0x1F) == 2) // requêtes de classe vers un point de terminaison
            {
                if (req == 0x01) { }                                         // SET_CUR (fréquence) : seul 48 kHz existe
                else if ((req & 0x80) != 0) resp = new byte[] { 0x80, 0xBB, 0x00 }; // GET_CUR/MIN/MAX/RES : 48000
                else ok = false;
            }
            else ok = false;

            if (!ok) { Reply(seq, EPIPE, null, 0, np, null, null, null); return; }
            if (dirIn)
            {
                int n = resp == null ? 0 : Math.Min(resp.Length, Math.Min(wLength, (int)len));
                Reply(seq, 0, resp, n, np, null, null, null);
            }
            else Reply(seq, 0, null, (int)len, np, null, null, null);
        }

        byte[] StringDesc(int i)
        {
            if (i == 0) return new byte[] { 4, 3, 0x09, 0x04 };
            if (i >= strings.Length) return null;
            var t = Encoding.Unicode.GetBytes(strings[i]);
            var b = new byte[2 + t.Length];
            b[0] = (byte)b.Length;
            b[1] = 3;
            t.CopyTo(b, 2);
            return b;
        }

        #endregion

        #region Flux isochrones cadencés en temps réel

        /// <summary>
        /// Un vrai périphérique USB consomme / produit 1 paquet par milliseconde. On rend donc chaque URB
        /// au moment exact où ses paquets auraient été transférés : c'est ce qui donne l'horloge à Windows.
        /// </summary>
        void Enqueue(IsoUrb u)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            lock (qLock)
            {
                bool idle = !pending.Exists(x => x.In == u.In);
                double next = u.In ? nextIn : nextOut;
                if (idle && next < now - 5) next = now; // reprise après une pause
                u.Due = next + u.NumPackets;
                if (u.In) nextIn = u.Due; else nextOut = u.Due;
                pending.Add(u);
            }
        }

        void Scheduler()
        {
            var timer = new PreciseTimer();
            var due = new List<IsoUrb>();
            var f = new float[FramesPerMs * 64 * 2];
            try
            {
                while (serving)
                {
                    timer.Wait(1);
                    double now = clock.Elapsed.TotalMilliseconds;
                    due.Clear();
                    lock (qLock)
                    {
                        for (int i = 0; i < pending.Count; i++)
                            if (pending[i].Due <= now) { due.Add(pending[i]); pending.RemoveAt(i--); }
                    }
                    due.Sort((a, b) => a.Due.CompareTo(b.Due));
                    foreach (var u in due)
                    {
                        try { Complete(u, ref f); }
                        catch (IOException) { serving = false; }
                        catch (ObjectDisposedException) { serving = false; }
                    }
                }
            }
            finally { timer.Dispose(); }
        }

        void Complete(IsoUrb u, ref float[] f)
        {
            int n = (int)u.NumPackets;
            var act = new int[n];
            if (!u.In)
            {
                // Haut-parleurs virtuels : on récupère le son joué par les logiciels
                int total = 0, frames = 0;
                for (int i = 0; i < n; i++) { act[i] = u.Length[i]; total += u.Length[i]; frames += u.Length[i] / BytesPerFrame; }
                var src = Source;
                if (src != null && u.Data != null && frames > 0)
                {
                    if (f.Length < frames * 2) f = new float[frames * 2];
                    int k = 0;
                    for (int i = 0; i < n; i++)
                    {
                        int end = Math.Min(u.Data.Length, u.Offset[i] + u.Length[i]);
                        for (int p = u.Offset[i]; p + 1 < end; p += 2) f[k++] = (short)(u.Data[p] | u.Data[p + 1] << 8) / 32768f;
                    }
                    src.Push(f, k / 2);
                }
                Reply(u.Seq, 0, null, total, u.NumPackets, u.Offset, u.Length, act);
            }
            else
            {
                // Micro virtuel : on fournit le mix de la console (48 trames par milliseconde)
                int perPacket = Math.Min(FramesPerMs, MaxPacket / BytesPerFrame);
                int frames = 0;
                for (int i = 0; i < n; i++) { act[i] = Math.Min(u.Length[i] / BytesPerFrame, perPacket) * BytesPerFrame; frames += act[i] / BytesPerFrame; }
                if (f.Length < frames * 2) f = new float[frames * 2];
                var sink = Sink;
                if (sink != null && MicOpen) sink.Pull(f, frames);
                else Array.Clear(f, 0, frames * 2);
                var data = new byte[frames * BytesPerFrame];
                for (int i = 0, p = 0; i < frames * 2; i++, p += 2)
                {
                    float v = f[i];
                    short sv = (short)(v > 1f ? 32767 : v < -1f ? -32767 : v * 32767f);
                    data[p] = (byte)sv;
                    data[p + 1] = (byte)(sv >> 8);
                }
                Reply(u.Seq, 0, data, data.Length, u.NumPackets, u.Offset, u.Length, act);
            }
        }

        #endregion
    }

    /// <summary>Attente précise (minuterie haute résolution de Windows 10+, sinon Sleep).</summary>
    sealed class PreciseTimer : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWaitableTimerExW(IntPtr attr, string name, uint flags, uint access);
        [DllImport("kernel32.dll")] static extern bool SetWaitableTimer(IntPtr h, ref long due, int period, IntPtr cb, IntPtr arg, bool resume);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

        readonly IntPtr h;
        readonly bool fallback;

        public PreciseTimer()
        {
            h = CreateWaitableTimerExW(IntPtr.Zero, null, 0x2 /* HIGH_RESOLUTION */, 0x1F0003);
            if (h == IntPtr.Zero) { fallback = true; timeBeginPeriod(1); }
        }

        public void Wait(int ms)
        {
            if (fallback) { Thread.Sleep(ms); return; }
            long due = -10000L * ms;
            SetWaitableTimer(h, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
            WaitForSingleObject(h, 100);
        }

        public void Dispose()
        {
            if (fallback) timeEndPeriod(1);
            else CloseHandle(h);
        }
    }

    static class Be
    {
        public static uint Get32(byte[] b, int o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }
        public static void Put32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        public static void Put16(byte[] b, int o, int v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }

        public static void ReadExact(Stream s, byte[] b, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(b, got, n - got);
                if (r <= 0) throw new EndOfStreamException();
                got += r;
            }
        }
    }
}
