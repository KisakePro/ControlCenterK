using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ControlCenterK
{
    static class Db
    {
        public const double Min = -60, Max = 12;

        public static float ToGain(double db) { return db <= Min ? 0f : (float)Math.Pow(10, db / 20); }

        /// <summary>Retombée d'un vumètre : suit les crêtes, redescend progressivement et tombe à 0 sous -60 dB.</summary>
        public static float Decay(float peak, float old, float k)
        {
            float n = Math.Max(peak, old * k);
            return n < 0.001f ? 0f : n;
        }

        /// <summary>Vrai si deux niveaux s'affichent différemment (par pas de 0,5 dB), pour ne redessiner que si utile.</summary>
        public static bool Differs(float a, float b)
        {
            if ((a <= 0) != (b <= 0)) return true;
            if (a <= 0) return false;
            return (int)(40 * Math.Log10(a)) != (int)(40 * Math.Log10(b));
        }

        public static double FromLevel(double level)
        {
            return level <= 0.001 ? Min : Math.Max(Min, Math.Min(Max, 20 * Math.Log10(level)));
        }
    }

    /// <summary>
    /// Tampon circulaire un producteur / un consommateur, sans verrou.
    /// Le producteur (thread de capture) écrit des trames, le consommateur (thread de sortie) les lit.
    /// </summary>
    sealed class Ring
    {
        public readonly int Channels, Rate;
        readonly float[] buf;
        readonly int cap;
        long w, r;

        public Ring(int channels, int rate, int capFrames)
        {
            Channels = channels;
            Rate = rate;
            cap = capFrames;
            buf = new float[capFrames * channels];
        }

        public long WritePos { get { return Volatile.Read(ref w); } }
        public long ReadPos { get { return Volatile.Read(ref r); } }

        public void Write(float[] src, int frames)
        {
            long wr = w;
            long space = cap - (wr - Volatile.Read(ref r));
            if (frames > space) frames = (int)space; // le consommateur ne suit plus : on jette le surplus
            for (int f = 0; f < frames; f++)
                Array.Copy(src, f * Channels, buf, (int)((wr + f) % cap) * Channels, Channels);
            Volatile.Write(ref w, wr + frames);
        }

        public void Frame(long pos, float[] dst)
        {
            Array.Copy(buf, (int)(pos % cap) * Channels, dst, 0, Channels);
        }

        public void Commit(long pos) { Volatile.Write(ref r, pos); }
    }

    /// <summary>Liaison entrée → sortie, avec l'état du rééchantillonneur côté sortie.</summary>
    sealed class Link
    {
        public readonly RouteInput In;
        public readonly RouteOutput Out;
        public volatile Ring Ring; // créé par le thread de capture quand le format est connu

        // état du consommateur (thread de sortie uniquement)
        public Ring MapRing;
        public int MapOutCh = -1;
        public int[] Map;
        public float[] A, B;
        public double T, Corr;
        public bool Primed;
        public float LastGain;

        public Link(RouteInput i, RouteOutput o) { In = i; Out = o; }
    }

    /// <summary>Base des threads audio : boucle avec reconnexion automatique en cas d'erreur.</summary>
    abstract class IoNode
    {
        public readonly string DeviceId;
        protected volatile Link[] links = new Link[0];
        protected readonly AutoResetEvent evt = new AutoResetEvent(false);
        readonly ManualResetEvent stopEvt = new ManualResetEvent(false);
        protected volatile bool stopping;
        Thread th;

        public volatile string Status;
        public volatile SampleFormat Format;
        public float PeakL, PeakR;

        protected IoNode(string deviceId) { DeviceId = deviceId; }

        public void SetLinks(Link[] l) { links = l; }

        public void Start(string name)
        {
            th = new Thread(Run) { IsBackground = true, Name = name, Priority = ThreadPriority.Highest };
            th.SetApartmentState(ApartmentState.MTA);
            th.Start();
        }

        public void Stop()
        {
            stopping = true;
            stopEvt.Set();
            evt.Set();
            if (th != null) th.Join(1500);
        }

        void Run()
        {
            int task = 0;
            IntPtr mm = IntPtr.Zero;
            try { mm = Wasapi.AvSetMmThreadCharacteristicsW("Pro Audio", ref task); } catch { }
            while (!stopping)
            {
                try { RunOnce(); }
                catch (Exception e) { Status = "Erreur : " + Describe(e); }
                if (!stopping) stopEvt.WaitOne(2000); // nouvelle tentative (périphérique débranché...)
            }
            if (mm != IntPtr.Zero) try { Wasapi.AvRevertMmThreadCharacteristics(mm); } catch { }
            Status = null;
        }

        static string Describe(Exception e)
        {
            var ce = e as COMException;
            if (ce == null) return e.Message;
            switch ((uint)ce.ErrorCode)
            {
                case 0x88890004: return "périphérique débranché ou désactivé";
                case 0x8889000A: return "périphérique utilisé en mode exclusif par une autre application";
                case 0x80070490: return "périphérique introuvable";
                default: return "0x" + ce.ErrorCode.ToString("X8");
            }
        }

        /// <summary>Initialise le client audio : en mode événement si possible, sinon en interrogation.</summary>
        protected IAudioClient Open(uint extraFlags, long bufferHns, out SampleFormat fmt, out bool polling)
        {
            polling = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var client = Wasapi.Activate(DeviceId);
                IntPtr wfx;
                client.GetMixFormat(out wfx);
                fmt = SampleFormat.From(wfx);
                uint flags = extraFlags | (attempt == 0 ? (uint)Wasapi.AUDCLNT_STREAMFLAGS_EVENTCALLBACK : 0u);
                int hr = client.Initialize(0, flags, bufferHns, 0, wfx, IntPtr.Zero);
                Wasapi.CoTaskMemFree(wfx);
                if (hr >= 0)
                {
                    polling = attempt == 1;
                    if (!polling) client.SetEventHandle(evt.SafeWaitHandle.DangerousGetHandle());
                    return client;
                }
                Wasapi.Release(client);
                if (attempt == 1) Marshal.ThrowExceptionForHR(hr);
            }
            throw new InvalidOperationException();
        }

        protected abstract void RunOnce();

        /// <summary>Envoie des trames capturées vers toutes les liaisons de cette entrée.</summary>
        protected void Distribute(float[] tmp, int n, SampleFormat fmt)
        {
            Peak(tmp, n, fmt.Channels);
            foreach (var l in links)
            {
                var ring = l.Ring;
                if (ring == null || ring.Channels != fmt.Channels || ring.Rate != fmt.Rate)
                    l.Ring = ring = new Ring(fmt.Channels, fmt.Rate, (int)(fmt.Rate * 1.1));
                ring.Write(tmp, n);
            }
        }

        /// <summary>Mixe toutes les liaisons arrivant sur une sortie, puis applique son gain.</summary>
        protected void MixOutput(RouteOutput output, ref float lastGain, float[] mix, int n, SampleFormat fmt)
        {
            Array.Clear(mix, 0, n * fmt.Channels);
            foreach (var l in links) RenderNode.MixLink(l, mix, n, fmt);
            float g = output.Mute ? 0f : Db.ToGain(output.Gain);
            int ch = fmt.Channels;
            for (int i = 0; i < n; i++)
            {
                float gi = lastGain + (g - lastGain) * (i + 1) / n;
                for (int c = 0; c < ch; c++) mix[i * ch + c] *= gi;
            }
            lastGain = g;
            Peak(mix, n, ch);
        }

        protected void Peak(float[] data, int frames, int ch)
        {
            float l = 0, r = 0;
            for (int i = 0; i < frames; i++)
            {
                float a = Math.Abs(data[i * ch]);
                if (a > l) l = a;
                if (ch > 1) { float b = Math.Abs(data[i * ch + 1]); if (b > r) r = b; }
            }
            if (ch == 1) r = l;
            if (l > PeakL) PeakL = l;
            if (r > PeakR) PeakR = r;
        }
    }

    sealed class CaptureNode : IoNode
    {
        public readonly bool Loopback;

        public CaptureNode(string deviceId, bool loopback) : base(deviceId) { Loopback = loopback; }

        protected override void RunOnce()
        {
            SampleFormat fmt;
            bool polling;
            var client = Open(Loopback ? (uint)Wasapi.AUDCLNT_STREAMFLAGS_LOOPBACK : 0u, 200000, out fmt, out polling);
            IAudioCaptureClient cc = null;
            try
            {
                Format = fmt;
                object o;
                client.GetService(ref Wasapi.IID_CaptureClient, out o);
                cc = (IAudioCaptureClient)o;
                uint bufFrames;
                client.GetBufferSize(out bufFrames);
                var tmp = new float[Math.Max(1, bufFrames) * fmt.Channels];
                client.Start();
                Status = "OK";
                while (!stopping)
                {
                    evt.WaitOne(polling ? 10 : 100);
                    while (!stopping)
                    {
                        uint next;
                        cc.GetNextPacketSize(out next);
                        if (next == 0) break;
                        IntPtr data;
                        uint frames, flags;
                        ulong p1, p2;
                        int hr = cc.GetBuffer(out data, out frames, out flags, out p1, out p2);
                        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                        if (frames == 0) { cc.ReleaseBuffer(0); break; }
                        int n = (int)frames;
                        if (tmp.Length < n * fmt.Channels) tmp = new float[n * fmt.Channels];
                        if ((flags & Wasapi.AUDCLNT_BUFFERFLAGS_SILENT) != 0) Array.Clear(tmp, 0, n * fmt.Channels);
                        else fmt.ToFloat(data, tmp, n);
                        cc.ReleaseBuffer(frames);
                        Distribute(tmp, n, fmt);
                    }
                }
                client.Stop();
            }
            finally
            {
                Wasapi.Release(cc);
                Wasapi.Release(client);
            }
        }
    }

    sealed class RenderNode : IoNode
    {
        const double BaseLatencyMs = 30;
        readonly RouteOutput output;
        float lastGain;

        public RenderNode(RouteOutput output) : base(output.DeviceId) { this.output = output; }

        protected override void RunOnce()
        {
            SampleFormat fmt;
            bool polling;
            var client = Open(0u, 300000, out fmt, out polling);
            IAudioRenderClient rc = null;
            try
            {
                Format = fmt;
                object o;
                client.GetService(ref Wasapi.IID_RenderClient, out o);
                rc = (IAudioRenderClient)o;
                uint bufFrames;
                client.GetBufferSize(out bufFrames);
                IntPtr p;
                rc.GetBuffer(bufFrames, out p);
                rc.ReleaseBuffer(bufFrames, Wasapi.AUDCLNT_BUFFERFLAGS_SILENT);
                var mix = new float[bufFrames * fmt.Channels];
                client.Start();
                Status = "OK";
                while (!stopping)
                {
                    evt.WaitOne(polling ? 10 : 200);
                    uint pad;
                    client.GetCurrentPadding(out pad);
                    int n = (int)(bufFrames - pad);
                    if (n <= 0) continue;
                    MixOutput(output, ref lastGain, mix, n, fmt);
                    rc.GetBuffer((uint)n, out p);
                    fmt.FromFloat(mix, p, n);
                    rc.ReleaseBuffer((uint)n, 0);
                }
                client.Stop();
            }
            finally
            {
                Wasapi.Release(rc);
                Wasapi.Release(client);
            }
        }

        /// <summary>
        /// Ajoute une entrée au mix. Rééchantillonnage linéaire avec correction de dérive :
        /// les horloges de deux cartes son ne sont jamais parfaitement identiques, on accélère / ralentit
        /// très légèrement la lecture pour garder le tampon autour de sa cible (latence + délai de la sortie).
        /// </summary>
        internal static void MixLink(Link l, float[] mix, int n, SampleFormat fmt)
        {
            var ring = l.Ring;
            if (ring == null) return;
            int outCh = fmt.Channels, inCh = ring.Channels;
            if (l.MapRing != ring || l.MapOutCh != outCh)
            {
                l.Map = new int[outCh];
                for (int c = 0; c < outCh; c++) l.Map[c] = c < inCh ? c : inCh == 1 ? 0 : -1;
                l.A = new float[inCh];
                l.B = new float[inCh];
                l.MapRing = ring;
                l.MapOutCh = outCh;
                l.Primed = false;
            }

            double target = ring.Rate * (BaseLatencyMs + Math.Max(0, l.Out.DelayMs)) / 1000.0;
            long r = ring.ReadPos, wp = ring.WritePos, avail = wp - r;
            if (!l.Primed)
            {
                if (avail < target + 2) return;
                if (avail > target + 2) { r = wp - (long)target - 2; }
                ring.Frame(r, l.A);
                ring.Frame(r + 1, l.B);
                r += 2;
                l.T = 0;
                l.Corr = 0;
                l.Primed = true;
                avail = wp - r;
            }
            else if (avail > target * 2 + ring.Rate * 0.1)
            {
                r = wp - (long)target; // trop de retard accumulé : on se recale
                avail = wp - r;
            }

            double err = Math.Max(-1, Math.Min(1, (avail - target) / Math.Max(1, target)));
            l.Corr += (err * 0.004 - l.Corr) * 0.05;
            double step = (double)ring.Rate / fmt.Rate * (1 + l.Corr);

            float g = l.In.Mute ? 0f : Db.ToGain(l.In.Gain);
            float g0 = l.LastGain;
            var map = l.Map;
            var A = l.A;
            var B = l.B;
            double t = l.T;
            for (int i = 0; i < n; i++)
            {
                float gi = g0 + (g - g0) * (i + 1) / n;
                float tt = (float)t;
                int o = i * outCh;
                for (int c = 0; c < outCh; c++)
                {
                    int ic = map[c];
                    if (ic >= 0) mix[o + c] += (A[ic] + (B[ic] - A[ic]) * tt) * gi;
                }
                t += step;
                while (t >= 1)
                {
                    t -= 1;
                    if (r >= ring.WritePos) { l.Primed = false; goto done; } // plus rien à lire : on attendra le prochain remplissage
                    var tmp = A; A = B; B = tmp;
                    ring.Frame(r, B);
                    r++;
                }
            }
        done:
            l.A = A;
            l.B = B;
            l.T = t;
            l.LastGain = g;
            ring.Commit(r);
        }
    }

    /// <summary>Entrée de la console alimentée par un périphérique virtuel (ce que les logiciels jouent sur ses haut-parleurs).</summary>
    sealed class VirtualSourceNode : IoNode
    {
        public readonly VirtualAudioDevice Device;

        public VirtualSourceNode(VirtualAudioDevice d) : base(d.DeviceId) { Device = d; }

        protected override void RunOnce()
        {
            Format = VirtualAudioDevice.Format;
            Device.Source = this;
            try
            {
                while (!stopping)
                {
                    Status = Device.Connected ? "OK" : "Erreur : périphérique virtuel pas encore connecté";
                    evt.WaitOne(500);
                }
            }
            finally { if (Device.Source == this) Device.Source = null; }
        }

        /// <summary>Appelé par le périphérique virtuel au rythme réel (1 ms).</summary>
        public void Push(float[] data, int frames) { Distribute(data, frames, VirtualAudioDevice.Format); }
    }

    /// <summary>Sortie de la console vers un périphérique virtuel (son micro, lu par les autres logiciels).</summary>
    sealed class VirtualSinkNode : IoNode
    {
        public readonly VirtualAudioDevice Device;
        readonly RouteOutput output;
        float lastGain;

        public VirtualSinkNode(VirtualAudioDevice d, RouteOutput o) : base(d.DeviceId) { Device = d; output = o; }

        protected override void RunOnce()
        {
            Format = VirtualAudioDevice.Format;
            Device.Sink = this;
            try
            {
                while (!stopping)
                {
                    Status = Device.Connected ? "OK" : "Erreur : périphérique virtuel pas encore connecté";
                    evt.WaitOne(500);
                }
            }
            finally { if (Device.Sink == this) Device.Sink = null; }
        }

        /// <summary>Appelé par le périphérique virtuel quand Windows réclame des échantillons micro.</summary>
        public void Pull(float[] mix, int frames) { MixOutput(output, ref lastGain, mix, frames, VirtualAudioDevice.Format); }
    }

    /// <summary>
    /// Module "Routage audio" : capture des entrées (micros ou sorties en boucle), mixage et envoi vers plusieurs sorties.
    /// Aucun thread n'existe tant que le moteur est arrêté ou qu'aucune tranche n'est configurée.
    /// </summary>
    public sealed class AudioRouter : IDisposable
    {
        readonly AppConfig cfg;
        readonly object gate = new object();
        readonly Dictionary<string, IoNode> captures = new Dictionary<string, IoNode>();
        readonly Dictionary<string, IoNode> renders = new Dictionary<string, IoNode>();
        readonly Dictionary<string, Link> links = new Dictionary<string, Link>();
        Timer saveTimer;

        public AudioRouter(AppConfig cfg) { this.cfg = cfg; }

        public static bool IsFeedback(RouteInput i, RouteOutput o)
        {
            if (i == null || o == null || o.DeviceId == null) return false;
            return (i.Loopback && i.DeviceId == o.DeviceId) || (i.PairId != null && i.PairId == o.DeviceId);
        }

        /// <summary>Met les threads en accord avec la configuration (à appeler après chaque changement de structure).</summary>
        public void Sync()
        {
            var stop = new List<IoNode>();
            lock (gate)
            {
                bool running;
                var wantLinks = new Dictionary<string, Link>();
                var wantCap = new Dictionary<string, RouteInput>();
                var wantRen = new Dictionary<string, RouteOutput>();
                lock (AppConfig.Sync)
                {
                    running = cfg.Router.Running;
                    if (running)
                    {
                        foreach (var i in cfg.Router.Inputs)
                        {
                            if (string.IsNullOrEmpty(i.DeviceId)) continue;
                            wantCap[i.Id] = i;
                            foreach (var b in i.Buses)
                            {
                                var o = cfg.Router.Outputs.Find(x => x.Id == b);
                                if (o == null || string.IsNullOrEmpty(o.DeviceId) || IsFeedback(i, o)) continue;
                                string key = i.Id + "|" + o.Id;
                                Link l;
                                if (!links.TryGetValue(key, out l) || l.In != i || l.Out != o) l = new Link(i, o);
                                wantLinks[key] = l;
                                wantRen[o.Id] = o;
                            }
                        }
                    }
                }

                foreach (var k in new List<string>(captures.Keys))
                {
                    RouteInput i;
                    var node = captures[k];
                    var cn = node as CaptureNode;
                    if (!wantCap.TryGetValue(k, out i) || i.DeviceId != node.DeviceId || (cn != null && i.Loopback != cn.Loopback))
                    {
                        stop.Add(node);
                        captures.Remove(k);
                    }
                }
                foreach (var k in new List<string>(renders.Keys))
                {
                    RouteOutput o;
                    var node = renders[k];
                    if (!wantRen.TryGetValue(k, out o) || o.DeviceId != node.DeviceId)
                    {
                        stop.Add(node);
                        renders.Remove(k);
                    }
                }

                links.Clear();
                foreach (var kv in wantLinks) links[kv.Key] = kv.Value;

                foreach (var kv in wantCap)
                {
                    IoNode node;
                    if (!captures.TryGetValue(kv.Key, out node))
                    {
                        var vd = VirtualHost.Find(kv.Value.DeviceId);
                        node = vd != null ? (IoNode)new VirtualSourceNode(vd) : new CaptureNode(kv.Value.DeviceId, kv.Value.Loopback);
                        captures[kv.Key] = node;
                        node.SetLinks(LinksWhere(l => l.In.Id == kv.Key));
                        node.Start("Routage entrée");
                    }
                    else node.SetLinks(LinksWhere(l => l.In.Id == kv.Key));
                }
                foreach (var kv in wantRen)
                {
                    IoNode node;
                    if (!renders.TryGetValue(kv.Key, out node))
                    {
                        var vd = VirtualHost.Find(kv.Value.DeviceId);
                        node = vd != null ? (IoNode)new VirtualSinkNode(vd, kv.Value) : new RenderNode(kv.Value);
                        renders[kv.Key] = node;
                        node.SetLinks(LinksWhere(l => l.Out.Id == kv.Key));
                        node.Start("Routage sortie");
                    }
                    else node.SetLinks(LinksWhere(l => l.Out.Id == kv.Key));
                }
            }
            foreach (var n in stop) n.Stop();
        }

        Link[] LinksWhere(Predicate<Link> p)
        {
            var list = new List<Link>();
            foreach (var l in links.Values) if (p(l)) list.Add(l);
            return list.ToArray();
        }

        public void Dispose()
        {
            var all = new List<IoNode>();
            lock (gate)
            {
                all.AddRange(captures.Values);
                all.AddRange(renders.Values);
                captures.Clear();
                renders.Clear();
                links.Clear();
            }
            foreach (var n in all) n.Stop();
            if (saveTimer != null) saveTimer.Dispose();
        }

        public int ThreadCount
        {
            get { lock (gate) return captures.Count + renders.Count; }
        }

        IoNode Node(bool input, string id)
        {
            lock (gate)
            {
                IoNode n;
                return (input ? captures : renders).TryGetValue(id, out n) ? n : null;
            }
        }

        public string Status(bool input, string id)
        {
            var n = Node(input, id);
            return n == null ? null : n.Status;
        }

        public string FormatText(bool input, string id)
        {
            var n = Node(input, id);
            var f = n == null ? null : n.Format;
            return f == null ? null : f.ToString();
        }

        /// <summary>Crête depuis la dernière lecture (0..1), puis remise à zéro.</summary>
        public void ReadPeaks(bool input, string id, out float l, out float r)
        {
            var n = Node(input, id);
            if (n == null) { l = r = 0; return; }
            l = n.PeakL;
            r = n.PeakR;
            n.PeakL = n.PeakR = 0;
        }

        /// <summary>Enregistre la config un peu plus tard (pour ne pas écrire le fichier à chaque mouvement de fader).</summary>
        public void SaveSoon()
        {
            lock (gate)
            {
                if (saveTimer == null) saveTimer = new Timer(_ => cfg.Save(), null, 1500, Timeout.Infinite);
                else saveTimer.Change(1500, Timeout.Infinite);
            }
        }
    }
}
