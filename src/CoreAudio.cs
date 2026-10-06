using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ControlCenterK
{
    public enum Flow { Render = 0, Capture = 1 }

    #region COM interop (WASAPI)

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(Flow flow, int stateMask, out IMMDeviceCollection devices);
        void GetDefaultAudioEndpoint(Flow flow, int role, out IMMDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IntPtr client);
        void UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        void GetCount(out int count);
        void Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        void OpenPropertyStore(int access, out IPropertyStore store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out int state);
    }

    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Sequential)] struct PropVariant { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify);
        void UnregisterControlChangeNotify(IntPtr notify);
        void GetChannelCount(out int count);
        void SetMasterVolumeLevel(float db, ref Guid ctx);
        void SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        void GetMasterVolumeLevel(out float db);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(int ch, float db, ref Guid ctx);
        void SetChannelVolumeLevelScalar(int ch, float level, ref Guid ctx);
        void GetChannelVolumeLevel(int ch, out float db);
        void GetChannelVolumeLevelScalar(int ch, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl();
        [PreserveSig] int GetSimpleAudioVolume();
        void GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        void GetState(out int state);
        [PreserveSig] int GetDisplayName();
        [PreserveSig] int SetDisplayName();
        [PreserveSig] int GetIconPath();
        [PreserveSig] int SetIconPath();
        [PreserveSig] int GetGroupingParam();
        [PreserveSig] int SetGroupingParam();
        [PreserveSig] int RegisterAudioSessionNotification();
        [PreserveSig] int UnregisterAudioSessionNotification();
        [PreserveSig] int GetSessionIdentifier();
        [PreserveSig] int GetSessionInstanceIdentifier();
        void GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        void SetMasterVolume(float level, ref Guid ctx);
        void GetMasterVolume(out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    // Interface non documentée (mais stable depuis Windows 7) pour changer le périphérique par défaut.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }

    #endregion

    public class DeviceInfo
    {
        public string Id;
        public string Name;
        public Flow Flow;
        public bool IsDefault;
    }

    public class AppInfo
    {
        public string Proc;
        public string ExePath;
    }

    public sealed class VolHandle
    {
        internal IAudioEndpointVolume Ep;
        internal ISimpleAudioVolume Sv;
    }

    /// <summary>
    /// Accès au son Windows (WASAPI). Une instance n'est utilisée que depuis un seul thread (le worker de l'Engine).
    /// Les objets COM sont mis en cache pour que bouger un fader ne coûte presque rien.
    /// </summary>
    public sealed class AudioSystem : IDisposable
    {
        static Guid IID_EndpointVolume = typeof(IAudioEndpointVolume).GUID;
        static Guid IID_SessionManager2 = typeof(IAudioSessionManager2).GUID;
        static Guid Ctx = Guid.Empty;
        static PropertyKey PKEY_FriendlyName = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
        const int CLSCTX_ALL = 23, DEVICE_STATE_ACTIVE = 1, SESSION_EXPIRED = 2;

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

        class Endpoint
        {
            public IMMDevice Dev;
            public IAudioEndpointVolume Vol;
            public IAudioSessionManager2 Mgr;
        }

        class Session
        {
            public IAudioSessionControl2 Ctl;
            public ISimpleAudioVolume Vol;
            public string Proc;
            public string ExePath;
            public bool IsSystem;
        }

        IMMDeviceEnumerator en;
        readonly Dictionary<string, Endpoint> endpoints = new Dictionary<string, Endpoint>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<uint, string> pathCache = new Dictionary<uint, string>();
        List<Session> sessions = new List<Session>();
        int sessionsTick;
        bool sessionsValid;

        public AudioSystem()
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
        }

        static void Release(object o)
        {
            if (o == null) return;
            try { Marshal.ReleaseComObject(o); } catch { }
        }

        /// <summary>Vide tous les caches (appelé quand un périphérique change ou qu'un appel échoue).</summary>
        public void Invalidate()
        {
            ReleaseSessions();
            foreach (var ep in endpoints.Values) { Release(ep.Mgr); Release(ep.Vol); Release(ep.Dev); }
            endpoints.Clear();
            pathCache.Clear();
        }

        void ReleaseSessions()
        {
            foreach (var s in sessions) Release(s.Ctl);
            sessions = new List<Session>();
            sessionsValid = false;
        }

        public void Dispose()
        {
            Invalidate();
            Release(en);
            en = null;
        }

        Endpoint GetEndpoint(string id)
        {
            Endpoint ep;
            if (endpoints.TryGetValue(id, out ep)) return ep;
            try
            {
                IMMDevice dev;
                en.GetDevice(id, out dev);
                int state;
                dev.GetState(out state);
                if (state != DEVICE_STATE_ACTIVE) { Release(dev); return null; }
                object o;
                dev.Activate(ref IID_EndpointVolume, CLSCTX_ALL, IntPtr.Zero, out o);
                ep = new Endpoint { Dev = dev, Vol = (IAudioEndpointVolume)o };
                endpoints[id] = ep;
                return ep;
            }
            catch { return null; }
        }

        public string DefaultId(Flow flow)
        {
            try
            {
                IMMDevice dev;
                en.GetDefaultAudioEndpoint(flow, 1 /* eMultimedia */, out dev);
                string id;
                dev.GetId(out id);
                Release(dev);
                return id;
            }
            catch { return null; }
        }

        public List<DeviceInfo> ListDevices(Flow flow)
        {
            var list = new List<DeviceInfo>();
            string def = DefaultId(flow);
            IMMDeviceCollection col;
            en.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out col);
            try
            {
                int n;
                col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d;
                    col.Item(i, out d);
                    try
                    {
                        string id;
                        d.GetId(out id);
                        list.Add(new DeviceInfo { Id = id, Name = FriendlyName(d), Flow = flow, IsDefault = id == def });
                    }
                    finally { Release(d); }
                }
            }
            finally { Release(col); }
            return list;
        }

        static string FriendlyName(IMMDevice d)
        {
            IPropertyStore ps = null;
            try
            {
                d.OpenPropertyStore(0, out ps);
                PropVariant v;
                ps.GetValue(ref PKEY_FriendlyName, out v);
                string s = v.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(v.p) : null;
                PropVariantClear(ref v);
                return s ?? "Périphérique inconnu";
            }
            catch { return "Périphérique inconnu"; }
            finally { Release(ps); }
        }

        string PathOf(uint pid)
        {
            string p;
            if (pathCache.TryGetValue(pid, out p)) return p;
            if (pathCache.Count > 256) pathCache.Clear();
            p = Native.ProcessPath(pid);
            pathCache[pid] = p;
            return p;
        }

        List<Session> Sessions(bool force)
        {
            int now = Environment.TickCount;
            if (!force && sessionsValid && unchecked(now - sessionsTick) < 1500) return sessions;
            ReleaseSessions();
            var list = new List<Session>();
            IMMDeviceCollection col = null;
            try
            {
                en.EnumAudioEndpoints(Flow.Render, DEVICE_STATE_ACTIVE, out col);
                int n;
                col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    string id = null;
                    IMMDevice d;
                    col.Item(i, out d);
                    try { d.GetId(out id); } finally { Release(d); }
                    var ep = GetEndpoint(id);
                    if (ep == null) continue;
                    try { CollectSessions(ep, list); } catch { }
                }
            }
            catch { }
            finally { Release(col); }
            sessions = list;
            sessionsTick = now;
            sessionsValid = true;
            return list;
        }

        void CollectSessions(Endpoint ep, List<Session> list)
        {
            if (ep.Mgr == null)
            {
                object o;
                ep.Dev.Activate(ref IID_SessionManager2, CLSCTX_ALL, IntPtr.Zero, out o);
                ep.Mgr = (IAudioSessionManager2)o;
            }
            IAudioSessionEnumerator e;
            ep.Mgr.GetSessionEnumerator(out e);
            try
            {
                int n;
                e.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 c = null;
                    try
                    {
                        e.GetSession(i, out c);
                        int state;
                        c.GetState(out state);
                        if (state == SESSION_EXPIRED) { Release(c); continue; }
                        uint pid;
                        c.GetProcessId(out pid);
                        bool sys = c.IsSystemSoundsSession() == 0;
                        string path = sys ? null : PathOf(pid);
                        list.Add(new Session { Ctl = c, Vol = (ISimpleAudioVolume)c, IsSystem = sys, ExePath = path, Proc = Native.ExeName(path) });
                    }
                    catch { Release(c); }
                }
            }
            finally { Release(e); }
        }

        public List<AppInfo> ListApps()
        {
            var seen = new HashSet<string>();
            var list = new List<AppInfo>();
            foreach (var s in Sessions(true))
                if (!s.IsSystem && s.Proc != null && seen.Add(s.Proc))
                    list.Add(new AppInfo { Proc = s.Proc, ExePath = s.ExePath });
            return list;
        }

        /// <summary>Transforme une liste de cibles en poignées de volume concrètes.</summary>
        public List<VolHandle> Resolve(IList<Target> targets, ICollection<string> assignedApps)
        {
            var res = new List<VolHandle>();
            List<Session> ss = null;
            foreach (var t in targets)
            {
                switch (t.Type)
                {
                    case "master": AddEndpoint(res, DefaultId(Flow.Render)); break;
                    case "mic": AddEndpoint(res, DefaultId(Flow.Capture)); break;
                    case "device": AddEndpoint(res, t.Id); break;
                    case "app":
                    case "focus":
                    case "system":
                    case "unassigned":
                        if (ss == null) ss = Sessions(false);
                        string proc = t.Type == "app" ? t.Id : t.Type == "focus" ? Native.ExeName(PathOf(Native.ForegroundPid())) : null;
                        if (t.Type == "focus" && proc == null) break;
                        foreach (var s in ss)
                        {
                            bool match;
                            if (t.Type == "system") match = s.IsSystem;
                            else if (t.Type == "unassigned") match = !s.IsSystem && s.Proc != null && (assignedApps == null || !assignedApps.Contains(s.Proc));
                            else match = !s.IsSystem && string.Equals(s.Proc, proc, StringComparison.OrdinalIgnoreCase);
                            if (match) res.Add(new VolHandle { Sv = s.Vol });
                        }
                        break;
                }
            }
            return res;
        }

        void AddEndpoint(List<VolHandle> res, string id)
        {
            if (id == null) return;
            var ep = GetEndpoint(id);
            if (ep != null) res.Add(new VolHandle { Ep = ep.Vol });
        }

        public void SetVolume(VolHandle h, float v)
        {
            if (v < 0f) v = 0f; else if (v > 1f) v = 1f;
            if (h.Ep != null) h.Ep.SetMasterVolumeLevelScalar(v, ref Ctx);
            else h.Sv.SetMasterVolume(v, ref Ctx);
        }

        public bool GetMute(VolHandle h)
        {
            bool m;
            if (h.Ep != null) h.Ep.GetMute(out m); else h.Sv.GetMute(out m);
            return m;
        }

        public void SetMute(VolHandle h, bool m)
        {
            if (h.Ep != null) h.Ep.SetMute(m, ref Ctx); else h.Sv.SetMute(m, ref Ctx);
        }

        public void SetDefault(string id)
        {
            var pc = (IPolicyConfig)new PolicyConfigCo();
            try
            {
                for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role); // console, multimédia, communications
            }
            finally { Release(pc); }
        }
    }
}
