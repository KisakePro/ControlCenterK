using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ControlCenterK
{
    static class Native
    {
        public const int MIM_DATA = 0x3C3;
        public const uint CALLBACK_FUNCTION = 0x30000;
        public const int WM_DEVICECHANGE = 0x0219;

        [DllImport("user32.dll")] static extern short GetKeyState(int vk);
        /// <summary>Touche Windows enfoncée (non signalée par Control.ModifierKeys).</summary>
        public static bool WinDown() { return GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0; }

        public delegate void MidiInProc(IntPtr hMidiIn, int wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MIDIINCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwSupport;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MIDIOUTCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public ushort wTechnology;
            public ushort wVoices;
            public ushort wNotes;
            public ushort wChannelMask;
            public uint dwSupport;
        }

        [DllImport("winmm.dll")] public static extern int midiInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern int midiInGetDevCapsW(UIntPtr id, ref MIDIINCAPS caps, int size);
        [DllImport("winmm.dll")] public static extern int midiInOpen(out IntPtr h, int id, MidiInProc cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] public static extern int midiInStart(IntPtr h);
        [DllImport("winmm.dll")] public static extern int midiInStop(IntPtr h);
        [DllImport("winmm.dll")] public static extern int midiInReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern int midiInClose(IntPtr h);
        [DllImport("winmm.dll")] public static extern int midiOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern int midiOutGetDevCapsW(UIntPtr id, ref MIDIOUTCAPS caps, int size);
        [DllImport("winmm.dll")] public static extern int midiOutOpen(out IntPtr h, int id, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] public static extern int midiOutShortMsg(IntPtr h, uint msg);
        [DllImport("winmm.dll")] public static extern int midiOutReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern int midiOutClose(IntPtr h);

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        [StructLayout(LayoutKind.Sequential)]
        public struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
            public short dbcc_name;
        }
        [DllImport("user32.dll")] public static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, ref DEV_BROADCAST_DEVICEINTERFACE filter, int flags);
        [DllImport("user32.dll")] public static extern bool UnregisterDeviceNotification(IntPtr handle);

        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder buf, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        public static string ProcessPath(uint pid)
        {
            if (pid == 0) return null;
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int n = sb.Capacity;
                return QueryFullProcessImageNameW(h, 0, sb, ref n) ? sb.ToString(0, n) : null;
            }
            finally { CloseHandle(h); }
        }

        public static string ExeName(string path)
        {
            return path == null ? null : Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        }

        public static uint ForegroundPid()
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            return pid;
        }

        public static void PressKey(byte vk)
        {
            keybd_event(vk, 0, 1, UIntPtr.Zero);       // KEYEVENTF_EXTENDEDKEY
            keybd_event(vk, 0, 1 | 2, UIntPtr.Zero);   // + KEYEVENTF_KEYUP
        }

        /// <summary>Libère la RAM inutilisée : appelé quand la fenêtre est fermée et l'app repart en arrière-plan.</summary>
        public static void TrimMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
        }
    }
}
