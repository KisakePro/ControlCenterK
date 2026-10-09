using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ControlCenterK
{
    /// <summary>
    /// Compte les images affichées par processus grâce au suivi d'événements Windows (ETW), comme PresentMon :
    /// rien n'est injecté dans les jeux. Sources : DXGI (DirectX 10 à 12), Direct3D 9, et le noyau graphique
    /// (DxgKrnl) pour le reste (Vulkan, OpenGL…). Nécessite les droits administrateur.
    /// </summary>
    static class FpsCapture
    {
        const string SessionName = "ControlCenterK-FPS";
        static readonly Guid Dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");    // Present_Start : 42
        static readonly Guid D3D9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");    // Present_Start : 1
        static readonly Guid DxgKrnl = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D"); // Present_Info : 184

        /// <summary>Compteurs d'un processus pour la tranche en cours.</summary>
        sealed class Counter { public int Api, Kernel; }

        static readonly Dictionary<int, Counter> counts = new Dictionary<int, Counter>();
        static ulong session, trace;
        static IntPtr props, loggerName;
        static Thread worker;
        static EventRecordCallback callback; // gardé en champ pour le ramasse-miettes

        public static bool Running { get { return worker != null; } }
        /// <summary>Dernière erreur au démarrage (null si tout va bien).</summary>
        public static string Error { get; private set; }
        public static bool NeedsAdmin { get; private set; }

        #region API ETW

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void EventRecordCallback(IntPtr record);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int StartTrace(out ulong handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int ControlTrace(ulong handle, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll")] static extern int EnableTraceEx2(ulong handle, ref Guid provider, uint code, byte level, ulong any, ulong all, uint timeout, IntPtr param);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern ulong OpenTrace(IntPtr logfile);
        [DllImport("advapi32.dll")] static extern int ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] static extern int CloseTrace(ulong handle);

        const int PropsSize = 120, NameBytes = 1024;

        /// <summary>EVENT_TRACE_PROPERTIES (64 bits) suivi de la place pour le nom de session.</summary>
        static IntPtr NewProps()
        {
            var p = Marshal.AllocHGlobal(PropsSize + NameBytes);
            for (int i = 0; i < PropsSize + NameBytes; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, PropsSize + NameBytes);   // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);                       // Wnode.ClientContext : horodatage QPC
            Marshal.WriteInt32(p, 44, 0x20000);                 // Wnode.Flags : WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 48, 64);                      // BufferSize (Ko)
            Marshal.WriteInt32(p, 52, 4);                       // MinimumBuffers
            Marshal.WriteInt32(p, 56, 16);                      // MaximumBuffers
            Marshal.WriteInt32(p, 64, 0x100);                   // LogFileMode : EVENT_TRACE_REAL_TIME_MODE
            Marshal.WriteInt32(p, 68, 1);                       // FlushTimer (s)
            Marshal.WriteInt32(p, 116, PropsSize);              // LoggerNameOffset
            return p;
        }

        #endregion

        public static void Start()
        {
            if (worker != null) return;
            Error = null;
            NeedsAdmin = false;
            if (IntPtr.Size != 8) { Error = "Le compteur de FPS nécessite Windows 64 bits."; return; }
            props = NewProps();
            // une session restée ouverte (plantage précédent) est d'abord fermée
            ControlTrace(0, SessionName, props, 1 /* EVENT_TRACE_CONTROL_STOP */);
            Marshal.FreeHGlobal(props);
            props = NewProps();
            int err = StartTrace(out session, SessionName, props);
            if (err != 0)
            {
                Marshal.FreeHGlobal(props); props = IntPtr.Zero;
                NeedsAdmin = err == 5;
                Error = err == 5 ? "Droits administrateur nécessaires pour mesurer les FPS." : "Impossible de démarrer la mesure (erreur " + err + ").";
                return;
            }
            var g = Dxgi; EnableTraceEx2(session, ref g, 1, 4, 0, 0, 0, IntPtr.Zero);
            g = D3D9; EnableTraceEx2(session, ref g, 1, 4, 0, 0, 0, IntPtr.Zero);
            g = DxgKrnl; EnableTraceEx2(session, ref g, 1, 4, 0x8000001 /* Base | Present */, 0, 0, IntPtr.Zero);

            // EVENT_TRACE_LOGFILEW (64 bits : 448 octets)
            var log = Marshal.AllocHGlobal(448);
            for (int i = 0; i < 448; i++) Marshal.WriteByte(log, i, 0);
            loggerName = Marshal.StringToHGlobalUni(SessionName);
            Marshal.WriteIntPtr(log, 8, loggerName);                          // LoggerName
            Marshal.WriteInt32(log, 28, 0x100 | 0x10000000);                  // ProcessTraceMode : temps réel + EVENT_RECORD
            callback = OnEvent;
            Marshal.WriteIntPtr(log, 424, Marshal.GetFunctionPointerForDelegate(callback)); // EventRecordCallback
            trace = OpenTrace(log);
            Marshal.FreeHGlobal(log);
            if (trace == ulong.MaxValue)
            {
                Error = "Impossible d'ouvrir la mesure (erreur " + Marshal.GetLastWin32Error() + ").";
                StopSession();
                return;
            }
            worker = new Thread(() => { try { ProcessTrace(new[] { trace }, 1, IntPtr.Zero, IntPtr.Zero); } catch { } })
            { IsBackground = true, Name = "Mesure des FPS" };
            worker.Start();
        }

        public static void Stop()
        {
            if (worker == null) { StopSession(); return; }
            StopSession();                 // arrête la session : ProcessTrace se termine
            CloseTrace(trace);
            worker.Join(2000);
            worker = null;
            lock (counts) counts.Clear();
        }

        static void StopSession()
        {
            if (props != IntPtr.Zero)
            {
                ControlTrace(session, null, props, 1);
                Marshal.FreeHGlobal(props);
                props = IntPtr.Zero;
            }
            if (loggerName != IntPtr.Zero) { Marshal.FreeHGlobal(loggerName); loggerName = IntPtr.Zero; }
        }

        static void OnEvent(IntPtr rec)
        {
            // EVENT_HEADER : ThreadId à +8, ProcessId à +12, ProviderId à +24, EventDescriptor.Id à +40
            int id = Marshal.ReadInt16(rec, 40) & 0xFFFF;
            if (id != 42 && id != 1 && id != 184) return;
            var provider = (Guid)Marshal.PtrToStructure(rec + 24, typeof(Guid));
            bool api = (id == 42 && provider == Dxgi) || (id == 1 && provider == D3D9);
            bool kernel = id == 184 && provider == DxgKrnl;
            if (!api && !kernel) return;
            int pid = Marshal.ReadInt32(rec, 12);
            lock (counts)
            {
                Counter c;
                if (!counts.TryGetValue(pid, out c)) counts[pid] = c = new Counter();
                if (api) c.Api++; else c.Kernel++;
            }
        }

        /// <summary>
        /// Images présentées par chaque processus depuis le dernier appel (puis remise à zéro).
        /// Les événements DirectX priment ; le noyau graphique ne sert que pour les autres API.
        /// </summary>
        public static Dictionary<int, int> TakeCounts()
        {
            var r = new Dictionary<int, int>();
            lock (counts)
            {
                foreach (var kv in counts) r[kv.Key] = kv.Value.Api > 0 ? kv.Value.Api : kv.Value.Kernel;
                counts.Clear();
            }
            return r;
        }
    }
}
