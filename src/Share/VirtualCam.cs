using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ControlCenterK
{
    /// <summary>
    /// Écrit des images dans la caméra virtuelle d'OBS (module DirectShow « obs-virtualcam-module »), par la même
    /// file en mémoire partagée qu'OBS : en-tête + 3 images NV12 tournantes. La caméra apparaît dans Discord
    /// sous le nom « KStS Virtual Screen » une fois le module enregistré sous ce nom.
    /// </summary>
    sealed unsafe class VirtualCam : IDisposable
    {
        const string MapName = "OBSVirtualCamVideo";
        const int HeaderSize = 80, FrameHeader = 32;
        const uint StateStarting = 1, StateReady = 2, StateStopping = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFileMapping(IntPtr file, IntPtr attr, uint protect, uint sizeHigh, uint sizeLow, string name);
        [DllImport("kernel32.dll")] static extern IntPtr MapViewOfFile(IntPtr map, uint access, uint offHigh, uint offLow, UIntPtr size);
        [DllImport("kernel32.dll")] static extern bool UnmapViewOfFile(IntPtr view);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        readonly IntPtr map;
        readonly byte* view;
        readonly int cx, cy;
        readonly int[] offsets = new int[3];

        public int Width { get { return cx; } }
        public int Height { get { return cy; } }

        VirtualCam(IntPtr map, byte* view, int cx, int cy, int[] offsets)
        {
            this.map = map; this.view = view; this.cx = cx; this.cy = cy; this.offsets = offsets;
        }

        static int Align(int v) { return (v + 31) & ~31; }

        /// <summary>Ouvre la caméra en cx × cy (pairs) ; renvoie null et un message si elle est déjà utilisée (OBS…).</summary>
        public static VirtualCam Open(int cx, int cy, int fps, out string error)
        {
            error = null;
            var busy = OpenFileMapping(4 /* FILE_MAP_READ */, false, MapName);
            if (busy != IntPtr.Zero)
            {
                CloseHandle(busy);
                error = "La caméra virtuelle est déjà utilisée (la caméra virtuelle d'OBS est peut-être démarrée).";
                return null;
            }
            int frame = cx * cy * 3 / 2;
            var off = new int[3];
            int size = Align(HeaderSize);
            for (int i = 0; i < 3; i++) { off[i] = size; size = Align(size + frame + FrameHeader); }

            var map = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, 4 /* PAGE_READWRITE */, 0, (uint)size, MapName);
            if (map == IntPtr.Zero) { error = "Impossible de créer la caméra virtuelle (erreur " + Marshal.GetLastWin32Error() + ")."; return null; }
            var view = (byte*)MapViewOfFile(map, 0xF001F /* FILE_MAP_ALL_ACCESS */, 0, 0, UIntPtr.Zero);
            if (view == null) { CloseHandle(map); error = "Impossible d'ouvrir la caméra virtuelle."; return null; }

            // en-tête : write_idx, read_idx, state, offsets[3], type, cx, cy, interval (100 ns), réservé
            for (int i = 0; i < HeaderSize; i++) view[i] = 0;
            *(uint*)(view + 8) = StateStarting;
            for (int i = 0; i < 3; i++) *(uint*)(view + 12 + 4 * i) = (uint)off[i];
            *(uint*)(view + 24) = 0; // vidéo
            *(uint*)(view + 28) = (uint)cx;
            *(uint*)(view + 32) = (uint)cy;
            *(ulong*)(view + 40) = (ulong)(10000000 / fps);
            return new VirtualCam(map, view, cx, cy, off);
        }

        /// <summary>Envoie une image BGRA (cx × cy, de haut en bas, 4 octets par pixel) convertie en NV12.</summary>
        public void Write(byte* bgra, long timestampNs)
        {
            int inc = Interlocked.Increment(ref *(int*)view);
            int idx = (int)((uint)inc % 3);
            byte* frame = view + offsets[idx];
            ToNv12(bgra, frame + FrameHeader, cx, cy);
            *(long*)frame = timestampNs;
            *(int*)(view + 4) = inc;              // read_idx
            *(uint*)(view + 8) = StateReady;
        }

        /// <summary>BGRA → NV12 (BT.601, plage limitée) : luminance par pixel, chrominance moyennée sur 2 × 2.</summary>
        static void ToNv12(byte* src, byte* dst, int w, int h)
        {
            byte* uv = dst + w * h;
            int stride = w * 4;
            for (int y = 0; y < h; y += 2)
            {
                byte* r0 = src + y * stride, r1 = r0 + stride;
                byte* y0 = dst + y * w, y1 = y0 + w;
                byte* c = uv + (y / 2) * w;
                for (int x = 0; x < w; x += 2)
                {
                    int b00 = r0[0], g00 = r0[1], R00 = r0[2], b01 = r0[4], g01 = r0[5], R01 = r0[6];
                    int b10 = r1[0], g10 = r1[1], R10 = r1[2], b11 = r1[4], g11 = r1[5], R11 = r1[6];
                    y0[0] = (byte)(((66 * R00 + 129 * g00 + 25 * b00 + 128) >> 8) + 16);
                    y0[1] = (byte)(((66 * R01 + 129 * g01 + 25 * b01 + 128) >> 8) + 16);
                    y1[0] = (byte)(((66 * R10 + 129 * g10 + 25 * b10 + 128) >> 8) + 16);
                    y1[1] = (byte)(((66 * R11 + 129 * g11 + 25 * b11 + 128) >> 8) + 16);
                    int R = (R00 + R01 + R10 + R11) >> 2, G = (g00 + g01 + g10 + g11) >> 2, B = (b00 + b01 + b10 + b11) >> 2;
                    c[0] = (byte)(((-38 * R - 74 * G + 112 * B + 128) >> 8) + 128);
                    c[1] = (byte)(((112 * R - 94 * G - 18 * B + 128) >> 8) + 128);
                    r0 += 8; r1 += 8; y0 += 2; y1 += 2; c += 2;
                }
            }
        }

        public void Dispose()
        {
            *(uint*)(view + 8) = StateStopping;
            UnmapViewOfFile((IntPtr)view);
            CloseHandle(map);
        }
    }
}
