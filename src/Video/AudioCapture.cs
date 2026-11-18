using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ControlCenterK
{
    /// <summary>
    /// Une source de son pour la vidéo : ce que joue le PC (bouclage WASAPI de la sortie par défaut) ou le micro par
    /// défaut. Converti en 48 kHz stéréo. Le bouclage ne livre rien quand le PC est muet : les trous sont comblés par
    /// du silence d'après l'horloge de l'enregistrement, pour que le son reste calé sur l'image.
    /// </summary>
    sealed class AudioSource : IDisposable
    {
        public const int Rate = 48000;

        readonly bool loopback;
        readonly string deviceId;   // "default" = périphérique par défaut de Windows
        readonly Stopwatch clock;
        readonly Thread thread;
        readonly object sync = new object();
        float[] ring = new float[Rate * 2 * 4];
        int count;               // trames disponibles
        long total;              // trames produites depuis le début
        volatile bool stopping;
        public string Error;

        public AudioSource(bool loopback, string deviceId, Stopwatch clock)
        {
            this.loopback = loopback;
            this.deviceId = deviceId;
            this.clock = clock;
            thread = new Thread(Run) { IsBackground = true, Name = loopback ? "Son du PC" : "Micro" };
            thread.Start();
        }

        /// <summary>Copie jusqu'à "frames" trames stéréo dans dst ; complète par du silence ce qui manque.</summary>
        public void Read(float[] dst, int frames)
        {
            lock (sync)
            {
                int n = Math.Min(frames, count);
                Array.Copy(ring, 0, dst, 0, n * 2);
                Array.Clear(dst, n * 2, (frames - n) * 2);
                Array.Copy(ring, n * 2, ring, 0, (count - n) * 2);
                count -= n;
            }
        }

        void Push(float[] data, int frames)
        {
            lock (sync)
            {
                if ((count + frames) * 2 > ring.Length)
                {
                    // personne ne lit (encodeur bloqué) : on garde au plus 10 s
                    if ((count + frames) > Rate * 10) { int drop = count + frames - Rate * 10; Array.Copy(ring, drop * 2, ring, 0, (count - drop) * 2); count -= drop; }
                    if ((count + frames) * 2 > ring.Length) Array.Resize(ref ring, (count + frames) * 4);
                }
                Array.Copy(data, 0, ring, count * 2, frames * 2);
                count += frames;
                total += frames;
            }
        }

        void Silence(int frames)
        {
            var z = new float[frames * 2];
            Push(z, frames);
        }

        void Run()
        {
            IAudioClient client = null;
            IAudioCaptureClient cc = null;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                IMMDevice dev;
                if (deviceId == "default") en.GetDefaultAudioEndpoint(loopback ? Flow.Render : Flow.Capture, 0, out dev);
                else en.GetDevice(deviceId, out dev);
                object o;
                dev.Activate(ref Wasapi.IID_AudioClient, 23, IntPtr.Zero, out o);
                client = (IAudioClient)o;
                Wasapi.Release(dev);
                Wasapi.Release(en);
                IntPtr wfx;
                client.GetMixFormat(out wfx);
                var fmt = SampleFormat.From(wfx);
                int hr = client.Initialize(0, loopback ? (uint)Wasapi.AUDCLNT_STREAMFLAGS_LOOPBACK : 0u, 1000000, 0, wfx, IntPtr.Zero);
                Wasapi.CoTaskMemFree(wfx);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                client.GetService(ref Wasapi.IID_CaptureClient, out o);
                cc = (IAudioCaptureClient)o;
                client.Start();
                var tmp = new float[4096];
                var stereo = new float[4096];
                var resampler = new Resampler(fmt.Rate);
                while (!stopping)
                {
                    Thread.Sleep(10);
                    uint next;
                    cc.GetNextPacketSize(out next);
                    while (next > 0 && !stopping)
                    {
                        IntPtr data;
                        uint frames, flags;
                        ulong p1, p2;
                        hr = cc.GetBuffer(out data, out frames, out flags, out p1, out p2);
                        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                        int n = (int)frames, ch = fmt.Channels;
                        if (tmp.Length < n * ch) tmp = new float[n * ch];
                        if (stereo.Length < n * 2) stereo = new float[n * 2];
                        if ((flags & Wasapi.AUDCLNT_BUFFERFLAGS_SILENT) != 0) Array.Clear(tmp, 0, n * ch);
                        else fmt.ToFloat(data, tmp, n);
                        cc.ReleaseBuffer(frames);
                        for (int i = 0; i < n; i++)
                        {
                            float l = tmp[i * ch], r = ch > 1 ? tmp[i * ch + 1] : l;
                            if (ch > 2) { float c = tmp[i * ch + 2] * 0.7f; l += c; r += c; } // voie centrale (5.1, 7.1)
                            stereo[i * 2] = l; stereo[i * 2 + 1] = r;
                        }
                        float[] outBuf;
                        int m = resampler.Process(stereo, n, out outBuf);
                        Fill(m);
                        if (m > 0) Push(outBuf, m);
                        cc.GetNextPacketSize(out next);
                    }
                    Fill(0);
                }
                client.Stop();
            }
            catch (Exception e) { Error = (loopback ? "Son du PC : " : "Micro : ") + e.Message; }
            finally
            {
                Wasapi.Release(cc);
                Wasapi.Release(client);
            }
        }

        // comble par du silence l'écart entre l'horloge et ce qui a été reçu (bouclage muet, démarrage)
        void Fill(int incoming)
        {
            long expected = clock.ElapsedTicks * Rate / Stopwatch.Frequency;
            long have;
            lock (sync) have = total;
            long gap = expected - (have + incoming) - Rate / 10; // 100 ms de marge pour l'arrivée des paquets
            if (gap > 0) Silence((int)Math.Min(gap, Rate * 5));
        }

        public void Dispose()
        {
            stopping = true;
            thread.Join(2000);
        }

        /// <summary>Rééchantillonnage linéaire stéréo vers 48 kHz (suffisant pour la voix et le son d'un PC).</summary>
        sealed class Resampler
        {
            readonly double step;
            double pos;
            float lastL, lastR;
            float[] output = new float[8192];

            public Resampler(int rate) { step = rate / (double)AudioSource.Rate; }

            public int Process(float[] input, int frames, out float[] result)
            {
                result = output;
                if (step == 1.0)
                {
                    if (output.Length < frames * 2) output = result = new float[frames * 2];
                    Array.Copy(input, output, frames * 2);
                    return frames;
                }
                int max = (int)(frames / step) + 2;
                if (output.Length < max * 2) output = result = new float[max * 2];
                int n = 0;
                // pos : position dans [dernière trame précédente, trames reçues], la trame -1 étant "last"
                while (pos < frames - 1 + 1e-9 && n < max)
                {
                    int i = (int)Math.Floor(pos);
                    double f = pos - i;
                    float l0 = i < 0 ? lastL : input[i * 2], r0 = i < 0 ? lastR : input[i * 2 + 1];
                    float l1 = input[(i + 1) * 2], r1 = input[(i + 1) * 2 + 1];
                    output[n * 2] = (float)(l0 + (l1 - l0) * f);
                    output[n * 2 + 1] = (float)(r0 + (r1 - r0) * f);
                    n++;
                    pos += step;
                }
                pos -= frames;
                lastL = input[(frames - 1) * 2];
                lastR = input[(frames - 1) * 2 + 1];
                return n;
            }
        }
    }

    /// <summary>Mélange des sources en PCM 16 bits stéréo 48 kHz pour l'encodeur.</summary>
    sealed class AudioMixer : IDisposable
    {
        readonly List<AudioSource> sources = new List<AudioSource>();
        float[] tmp = new float[0], sum = new float[0];

        /// <summary>Identifiants de périphériques : "" = aucun, "default" = celui de Windows.</summary>
        public AudioMixer(string output, string mic, Stopwatch clock)
        {
            if (!string.IsNullOrEmpty(output)) sources.Add(new AudioSource(true, output, clock));
            if (!string.IsNullOrEmpty(mic)) sources.Add(new AudioSource(false, mic, clock));
        }

        public string Error
        {
            get { foreach (var s in sources) if (s.Error != null) return s.Error; return null; }
        }

        public short[] Pull(int frames)
        {
            if (tmp.Length < frames * 2) { tmp = new float[frames * 2]; sum = new float[frames * 2]; }
            Array.Clear(sum, 0, frames * 2);
            foreach (var s in sources)
            {
                s.Read(tmp, frames);
                for (int i = 0; i < frames * 2; i++) sum[i] += tmp[i];
            }
            var pcm = new short[frames * 2];
            for (int i = 0; i < frames * 2; i++)
            {
                float v = sum[i];
                pcm[i] = (short)(v > 1f ? 32767 : v < -1f ? -32767 : v * 32767f);
            }
            return pcm;
        }

        public void Dispose() { foreach (var s in sources) s.Dispose(); }
    }
}
