using System;
using System.Runtime.InteropServices;

namespace MidiSoundController
{
    #region COM interop (flux audio WASAPI)

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        void GetBufferSize(out uint frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported();
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient
    {
        void GetBuffer(uint frames, out IntPtr data);
        void ReleaseBuffer(uint frames, uint flags);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }

    #endregion

    /// <summary>Format d'échantillons d'un flux partagé (toujours le "mix format" du périphérique).</summary>
    sealed class SampleFormat
    {
        public int Rate, Channels, Bits, BlockAlign;
        public bool IsFloat;

        static readonly Guid FloatSubtype = new Guid("00000003-0000-0010-8000-00aa00389b71");

        public static SampleFormat From(IntPtr wfx)
        {
            var f = new SampleFormat
            {
                Channels = Marshal.ReadInt16(wfx, 2),
                Rate = Marshal.ReadInt32(wfx, 4),
                BlockAlign = Marshal.ReadInt16(wfx, 12),
                Bits = Marshal.ReadInt16(wfx, 14),
            };
            int tag = (ushort)Marshal.ReadInt16(wfx, 0);
            if (tag == 3) f.IsFloat = true;
            else if (tag == 0xFFFE)
            {
                var sub = (Guid)Marshal.PtrToStructure(new IntPtr(wfx.ToInt64() + 24), typeof(Guid));
                f.IsFloat = sub == FloatSubtype;
            }
            return f;
        }

        public override string ToString()
        {
            return (Rate / 1000.0).ToString("0.#") + " kHz · " + Channels + " can.";
        }

        /// <summary>Convertit des trames brutes en float entrelacés.</summary>
        public unsafe void ToFloat(IntPtr src, float[] dst, int frames)
        {
            int n = frames * Channels;
            if (IsFloat && Bits == 32) { Marshal.Copy(src, dst, 0, n); return; }
            byte* p = (byte*)src;
            switch (Bits)
            {
                case 16: { short* s = (short*)p; for (int i = 0; i < n; i++) dst[i] = s[i] / 32768f; break; }
                case 24: for (int i = 0; i < n; i++, p += 3) dst[i] = ((p[0] << 8 | p[1] << 16 | p[2] << 24) >> 8) / 8388608f; break;
                case 32: { int* s = (int*)p; for (int i = 0; i < n; i++) dst[i] = s[i] / 2147483648f; break; }
                default: Array.Clear(dst, 0, n); break;
            }
        }

        /// <summary>Convertit des float entrelacés vers le format du périphérique.</summary>
        public unsafe void FromFloat(float[] src, IntPtr dst, int frames)
        {
            int n = frames * Channels;
            if (IsFloat && Bits == 32) { Marshal.Copy(src, 0, dst, n); return; }
            byte* p = (byte*)dst;
            switch (Bits)
            {
                case 16: { short* d = (short*)p; for (int i = 0; i < n; i++) d[i] = (short)(Clip(src[i]) * 32767f); break; }
                case 24:
                    for (int i = 0; i < n; i++, p += 3)
                    {
                        int v = (int)(Clip(src[i]) * 8388607f);
                        p[0] = (byte)v; p[1] = (byte)(v >> 8); p[2] = (byte)(v >> 16);
                    }
                    break;
                case 32: { int* d = (int*)p; for (int i = 0; i < n; i++) d[i] = (int)(Clip(src[i]) * 2147483647.0); break; }
            }
        }

        static float Clip(float v) { return v > 1f ? 1f : v < -1f ? -1f : v; }
    }

    static class Wasapi
    {
        public const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        public const int AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
        public const int AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
        public static Guid IID_AudioClient = typeof(IAudioClient).GUID;
        public static Guid IID_RenderClient = typeof(IAudioRenderClient).GUID;
        public static Guid IID_CaptureClient = typeof(IAudioCaptureClient).GUID;

        [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);
        [DllImport("avrt.dll", CharSet = CharSet.Unicode)] public static extern IntPtr AvSetMmThreadCharacteristicsW(string task, ref int index);
        [DllImport("avrt.dll")] public static extern bool AvRevertMmThreadCharacteristics(IntPtr h);

        public static IAudioClient Activate(string deviceId)
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
            IMMDevice dev = null;
            try
            {
                en.GetDevice(deviceId, out dev);
                object o;
                dev.Activate(ref IID_AudioClient, 23, IntPtr.Zero, out o);
                return (IAudioClient)o;
            }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(en);
            }
        }

        public static void Release(object o)
        {
            if (o == null) return;
            try { Marshal.ReleaseComObject(o); } catch { }
        }
    }
}
