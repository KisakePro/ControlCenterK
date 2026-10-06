using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ControlCenterK
{
    /// <summary>Accès bas niveau aux périphériques HID (énumération, rapports « feature »).</summary>
    static class Hid
    {
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DID did);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DID did, IntPtr det, int size, out int req, IntPtr di);
        [StructLayout(LayoutKind.Sequential)] struct SP_DID { public int cb; public Guid g; public int f; public IntPtr r; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string n, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

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
                        if (p.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) list.Add(p);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        public static SafeFileHandle Open(string path, bool write)
        {
            return CreateFile(path, write ? 0xC0000000 : 0x80000000, 3 /* partage lecture/écriture */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        }
    }
}
