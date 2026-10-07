using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ControlCenterK
{
    /// <summary>Une interface (collection) HID présente sur le PC.</summary>
    sealed class HidInfo
    {
        public string Path;
        public int Vid, Pid;
        public int UsagePage, Usage;
        public int InLen, OutLen, FeatureLen;   // tailles des rapports, octet d'identifiant compris
        public int Interface = -1;              // n° d'interface USB (« &mi_xx »), -1 si absent
        public string Product = "", Manufacturer = "";

        public bool IsMouse { get { return UsagePage == 1 && Usage == 2; } }
        public string Key { get { return Vid.ToString("x4") + ":" + Pid.ToString("x4"); } }
    }

    /// <summary>Accès bas niveau aux périphériques HID (énumération, rapports « feature » et « output »).</summary>
    static class Hid
    {
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll")] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
        [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll")] static extern bool HidD_GetManufacturerString(SafeFileHandle h, byte[] b, int len);
        [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DID did);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DID did, IntPtr det, int size, out int req, IntPtr di);
        [StructLayout(LayoutKind.Sequential)] struct SP_DID { public int cb; public Guid g; public int f; public IntPtr r; }
        [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }
        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string n, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, byte[] b, int len, out int written, IntPtr ov);
        [DllImport("kernel32.dll")] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);

        /// <summary>Chemins des interfaces HID dont le chemin contient le filtre (ex. "vid_1b1c&amp;pid_1b5c").</summary>
        public static List<string> Paths(string filter)
        {
            var list = new List<string>();
            Guid g;
            HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            try
            {
                for (int i = 0; ; i++)
                {
                    var did = new SP_DID { cb = Marshal.SizeOf(typeof(SP_DID)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did)) break;
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref did, buf, req, out req, IntPtr.Zero)) continue;
                        string p = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                        if (filter == null || p.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) list.Add(p);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        /// <summary>Toutes les interfaces HID (filtre facultatif sur le chemin), avec leurs capacités.</summary>
        public static List<HidInfo> Enumerate(string filter)
        {
            var list = new List<HidInfo>();
            foreach (var p in Paths(filter))
            {
                var i = Info(p);
                if (i != null) list.Add(i);
            }
            return list;
        }

        static HidInfo Info(string path)
        {
            // accès 0 : permet d'interroger aussi les souris et claviers que Windows garde pour lui
            using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var info = new HidInfo { Path = path };
                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                if (!HidD_GetAttributes(h, ref a)) return null;
                info.Vid = a.VendorID;
                info.Pid = a.ProductID;
                IntPtr pre;
                if (HidD_GetPreparsedData(h, out pre))
                {
                    try
                    {
                        HIDP_CAPS caps;
                        if (HidP_GetCaps(pre, out caps) == 0x110000)
                        {
                            info.UsagePage = caps.UsagePage;
                            info.Usage = caps.Usage;
                            info.InLen = caps.InputReportByteLength;
                            info.OutLen = caps.OutputReportByteLength;
                            info.FeatureLen = caps.FeatureReportByteLength;
                        }
                    }
                    finally { HidD_FreePreparsedData(pre); }
                }
                info.Product = HidString(h, true);
                info.Manufacturer = HidString(h, false);
                int mi = path.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
                int n;
                if (mi >= 0 && path.Length >= mi + 6 && int.TryParse(path.Substring(mi + 4, 2), System.Globalization.NumberStyles.HexNumber, null, out n)) info.Interface = n;
                return info;
            }
        }

        static string HidString(SafeFileHandle h, bool product)
        {
            var b = new byte[256];
            bool ok = product ? HidD_GetProductString(h, b, b.Length) : HidD_GetManufacturerString(h, b, b.Length);
            if (!ok) return "";
            string s = Encoding.Unicode.GetString(b);
            int z = s.IndexOf('\0');
            return (z >= 0 ? s.Substring(0, z) : s).Trim();
        }

        public static SafeFileHandle Open(string path, bool write)
        {
            return CreateFile(path, write ? 0xC0000000 : 0x80000000, 3 /* partage lecture/écriture */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        }

        /// <summary>Ouverture sans droits de lecture / écriture : suffit pour les rapports « feature » des souris gardées par Windows.</summary>
        public static SafeFileHandle OpenNoAccess(string path)
        {
            return CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        }

        /// <summary>Envoie un rapport « output » (octet 0 = identifiant), complété à la taille attendue.</summary>
        public static bool Write(SafeFileHandle h, byte[] report, int length)
        {
            var b = new byte[Math.Max(length, report.Length)];
            Array.Copy(report, b, report.Length);
            int written;
            if (WriteFile(h, b, b.Length, out written, IntPtr.Zero) && written > 0) return true;
            return HidD_SetOutputReport(h, b, b.Length);
        }
    }

    /// <summary>
    /// Canal d'échange avec une interface HID : écriture de rapports « output » et lecture des réponses
    /// par un thread bloqué en lecture (0 % CPU au repos). Poignées séparées en lecture et en écriture.
    /// </summary>
    sealed class HidChannel : IDisposable
    {
        readonly HidInfo info;
        readonly SafeFileHandle w, r;
        readonly Queue<byte[]> inbox = new Queue<byte[]>();
        readonly AutoResetEvent arrived = new AutoResetEvent(false);
        volatile bool closing;

        HidChannel(HidInfo info, SafeFileHandle w, SafeFileHandle r)
        {
            this.info = info;
            this.w = w;
            this.r = r;
            new Thread(ReadLoop) { IsBackground = true, Name = "Lecture HID" }.Start();
        }

        public static HidChannel Open(HidInfo info)
        {
            var w = Hid.Open(info.Path, true);
            if (w.IsInvalid) { w.Dispose(); return null; }
            var r = Hid.Open(info.Path, false);
            if (r.IsInvalid) { w.Dispose(); r.Dispose(); return null; }
            return new HidChannel(info, w, r);
        }

        void ReadLoop()
        {
            var buf = new byte[Math.Max(2, info.InLen)];
            try
            {
                using (var fs = new FileStream(r, FileAccess.Read, Math.Max(2, info.InLen), false))
                    while (!closing)
                    {
                        int n = fs.Read(buf, 0, buf.Length);
                        if (n <= 0) continue;
                        var copy = new byte[n];
                        Array.Copy(buf, copy, n);
                        lock (inbox)
                        {
                            inbox.Enqueue(copy);
                            while (inbox.Count > 64) inbox.Dequeue();
                        }
                        arrived.Set();
                    }
            }
            catch { }
        }

        public void Clear() { lock (inbox) inbox.Clear(); }

        public bool Write(byte[] report) { return !closing && Hid.Write(w, report, info.OutLen); }

        /// <summary>Attend un rapport reçu qui satisfait "match" (null après le délai).</summary>
        public byte[] Read(int timeoutMs, Func<byte[], bool> match)
        {
            int end = Environment.TickCount + timeoutMs;
            while (!closing)
            {
                lock (inbox)
                    while (inbox.Count > 0)
                    {
                        var b = inbox.Dequeue();
                        if (match == null || match(b)) return b;
                    }
                int left = end - Environment.TickCount;
                if (left <= 0) return null;
                arrived.WaitOne(left);
            }
            return null;
        }

        public void Dispose()
        {
            closing = true;
            try { Hid.CancelIoEx(r, IntPtr.Zero); } catch { }
            r.Dispose();
            w.Dispose();
            arrived.Set();
        }
    }
}
