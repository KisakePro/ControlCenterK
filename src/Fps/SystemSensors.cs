using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace ControlCenterK
{
    /// <summary>
    /// Utilisation et température du processeur et de la carte graphique, pour l'affichage en jeu.
    /// Utilisation : mesurée directement (temps système de Windows pour le processeur, compteurs « GPU Engine »
    /// pour la carte graphique, comme le Gestionnaire des tâches). Températures : lues dans HWiNFO s'il tourne avec
    /// le partage des capteurs activé (Windows ne les fournit pas aux programmes sans pilote).
    /// </summary>
    static class SystemSensors
    {
        public static float CpuUsage { get; private set; }
        /// <summary>Mémoire vive : pourcentage utilisé, Go utilisés et Go au total.</summary>
        public static float RamUsage { get; private set; }
        public static float RamUsedGb { get; private set; }
        public static float RamTotalGb { get; private set; }
        public static float GpuUsage { get; private set; }
        /// <summary>Températures en °C, NaN si inconnues.</summary>
        public static float CpuTemp { get; private set; }
        public static float GpuTemp { get; private set; }
        /// <summary>Vrai si HWiNFO partage ses capteurs (source de la température du processeur).</summary>
        public static bool HwInfo { get; private set; }
        /// <summary>Vrai si la sonde de ControlCenterK fournit la température du processeur.</summary>
        public static bool ProbeActive { get; private set; }
        /// <summary>Source des valeurs de la carte graphique : "AMD", "NVIDIA", "HWiNFO" ou "Windows".</summary>
        public static string GpuSource { get; private set; }

        static SystemSensors() { CpuTemp = GpuTemp = float.NaN; }

        /// <summary>Relève toutes les valeurs (à appeler 2 à 4 fois par seconde).</summary>
        public static void Update()
        {
            UpdateCpu();
            UpdateRam();
            UpdateTemps();
            // température du processeur : sonde de ControlCenterK d'abord, sinon HWiNFO
            CpuProbe.EnsureRunning();
            float probe = CpuProbe.Read();
            ProbeActive = !float.IsNaN(probe);
            if (ProbeActive) CpuTemp = probe;
            // carte graphique : pilote du fabricant d'abord (sans rien installer), puis HWiNFO, puis Windows (charge seule)
            float load, temp;
            if (Amd(out load, out temp)) GpuSource = "AMD";
            else if (Nvidia(out load, out temp)) GpuSource = "NVIDIA";
            else if (hwGpuLoad >= 0) { load = hwGpuLoad; temp = hwGpuTemp; GpuSource = "HWiNFO"; }
            else { UpdateGpu(); load = GpuUsage; temp = hwGpuTemp; GpuSource = "Windows"; }
            GpuUsage = load;
            GpuTemp = temp;
        }

        #region Pilotes graphiques (AMD ADL, NVIDIA NVML)

        delegate IntPtr AdlAlloc(int size);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Main_Control_Create(AdlAlloc cb, int connected, out IntPtr ctx);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr ctx, out int n);
        [DllImport("atiadlxx.dll")] static extern int ADL2_New_QueryPMLogData_Get(IntPtr ctx, int adapter, IntPtr output);
        static readonly AdlAlloc adlAlloc = size => Marshal.AllocHGlobal(size); // gardé en champ pour le ramasse-miettes
        static IntPtr adl, adlBuf;
        static int adlAdapter = -1;
        static bool adlFailed;
        const int AdlSize = 4 + 256 * 8; // taille + 256 capteurs (pris en charge, valeur)

        /// <summary>AMD : journal des capteurs du pilote (8 = température, 19 = activité du cœur graphique).</summary>
        static bool Amd(out float load, out float temp)
        {
            load = 0; temp = float.NaN;
            if (adlFailed) return false;
            try
            {
                if (adl == IntPtr.Zero)
                {
                    int n;
                    if (ADL2_Main_Control_Create(adlAlloc, 1, out adl) != 0 || ADL2_Adapter_NumberOfAdapters_Get(adl, out n) != 0) { adlFailed = true; return false; }
                    adlBuf = Marshal.AllocHGlobal(AdlSize);
                    for (int a = 0; a < n && adlAdapter < 0; a++) if (Query(a)) adlAdapter = a;
                    if (adlAdapter < 0) { adlFailed = true; return false; }
                }
                if (!Query(adlAdapter)) return false;
                if (Marshal.ReadInt32(adlBuf, 4 + 8 * 8) != 0) temp = Marshal.ReadInt32(adlBuf, 8 + 8 * 8);
                if (Marshal.ReadInt32(adlBuf, 4 + 19 * 8) != 0) load = Math.Min(100, Marshal.ReadInt32(adlBuf, 8 + 19 * 8));
                return true;
            }
            catch (DllNotFoundException) { adlFailed = true; return false; }
            catch (EntryPointNotFoundException) { adlFailed = true; return false; }
        }

        static bool Query(int adapter)
        {
            for (int i = 0; i < AdlSize; i += 4) Marshal.WriteInt32(adlBuf, i, 0);
            Marshal.WriteInt32(adlBuf, 0, AdlSize);
            return ADL2_New_QueryPMLogData_Get(adl, adapter, adlBuf) == 0 && Marshal.ReadInt32(adlBuf, 4 + 8 * 8) != 0;
        }

        [StructLayout(LayoutKind.Sequential)] struct NvUtil { public uint Gpu, Memory; }
        [DllImport("nvml.dll")] static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvUtil util);
        static IntPtr nvDevice;
        static bool nvFailed;

        /// <summary>NVIDIA : bibliothèque de gestion livrée avec le pilote.</summary>
        static bool Nvidia(out float load, out float temp)
        {
            load = 0; temp = float.NaN;
            if (nvFailed) return false;
            try
            {
                if (nvDevice == IntPtr.Zero && (nvmlInit_v2() != 0 || nvmlDeviceGetHandleByIndex_v2(0, out nvDevice) != 0)) { nvFailed = true; return false; }
                uint t;
                NvUtil u;
                if (nvmlDeviceGetTemperature(nvDevice, 0 /* cœur */, out t) == 0) temp = t;
                if (nvmlDeviceGetUtilizationRates(nvDevice, out u) == 0) load = u.Gpu;
                return true;
            }
            catch (DllNotFoundException) { nvFailed = true; return false; }
            catch (EntryPointNotFoundException) { nvFailed = true; return false; }
        }

        #endregion

        #region Mémoire vive

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX { public int Length, Load; public ulong Total, Avail, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        static void UpdateRam()
        {
            var m = new MEMORYSTATUSEX { Length = Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (!GlobalMemoryStatusEx(ref m) || m.Total == 0) return;
            const float Gb = 1024f * 1024 * 1024;
            RamTotalGb = m.Total / Gb;
            RamUsedGb = (m.Total - m.Avail) / Gb;
            RamUsage = 100f * (m.Total - m.Avail) / m.Total;
        }

        #endregion

        #region Processeur

        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        static long lastIdle, lastTotal;

        static void UpdateCpu()
        {
            long idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return;
            long total = kernel + user; // le temps noyau inclut le temps d'inactivité
            long dIdle = idle - lastIdle, dTotal = total - lastTotal;
            if (lastTotal != 0 && dTotal > 0) CpuUsage = Math.Max(0, Math.Min(100, 100f * (dTotal - dIdle) / dTotal));
            lastIdle = idle;
            lastTotal = total;
        }

        #endregion

        #region Carte graphique

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);

        static IntPtr query, counter;
        static bool pdhFailed;

        /// <summary>
        /// Dernier recours : utilisation du moteur 3D (somme de tous les processus, carte la plus chargée). Les moteurs
        /// vidéo (encodage d'un stream…) sont ignorés : ils ne disent rien de la charge du jeu.
        /// </summary>
        static void UpdateGpu()
        {
            if (pdhFailed) return;
            if (query == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0 ||
                    PdhAddEnglishCounter(query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out counter) != 0) { pdhFailed = true; return; }
                PdhCollectQueryData(query);
                return;
            }
            if (PdhCollectQueryData(query) != 0) return;
            uint size = 0, count;
            if (PdhGetFormattedCounterArray(counter, 0x200 /* DOUBLE */, ref size, out count, IntPtr.Zero) != 0x800007D2 /* PDH_MORE_DATA */) return;
            var buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(counter, 0x200, ref size, out count, buf) != 0) return;
                var engines = new Dictionary<string, double>();
                int item = IntPtr.Size + 16; // nom, puis état + valeur
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = buf + i * item;
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p));
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p + IntPtr.Size + 8));
                    int at = name == null ? -1 : name.IndexOf("luid_", StringComparison.Ordinal);
                    if (at < 0) continue;
                    int phys = name.IndexOf("_phys_", at, StringComparison.Ordinal);
                    string key = phys > 0 ? name.Substring(at, phys - at) : name.Substring(at); // carte, tous processus et moteurs 3D
                    double s;
                    engines.TryGetValue(key, out s);
                    engines[key] = s + v;
                }
                double max = 0;
                foreach (var v in engines.Values) max = Math.Max(max, v);
                GpuUsage = (float)Math.Min(100, max);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        #endregion

        #region Températures (HWiNFO)

        static readonly string[] CpuLabels = { "CPU (Tctl/Tdie)", "CPU Package", "CPU Die (average)", "Core Temperatures", "CPU" };
        static readonly string[] GpuLabels = { "GPU Temperature", "GPU Core Temperature", "GPU Hot Spot Temperature" };
        static readonly string[] GpuLoadLabels = { "GPU Utilization", "GPU Core Load", "GPU Load" };
        static float hwGpuLoad = -1, hwGpuTemp = float.NaN;

        /// <summary>
        /// Mémoire partagée de HWiNFO (« Shared Memory Support ») : en-tête, puis capteurs, puis lectures
        /// (type, capteur, identifiant, libellés, unité, valeur…).
        /// </summary>
        static void UpdateTemps()
        {
            float cpu = float.NaN, gpu = float.NaN, load = -1;
            int cpuRank = int.MaxValue, gpuRank = int.MaxValue, loadRank = int.MaxValue;
            try
            {
                using (var mm = MemoryMappedFile.OpenExisting(@"Global\HWiNFO_SENS_SM2", MemoryMappedFileRights.Read))
                using (var v = mm.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                {
                    if (v.ReadUInt32(0) != 0x53695748 /* "HWiS" */) throw new InvalidOperationException();
                    long ro = v.ReadUInt32(32), rs = v.ReadUInt32(36), rn = v.ReadUInt32(40);
                    var label = new byte[128];
                    for (long i = 0; i < rn; i++)
                    {
                        long b = ro + i * rs;
                        uint type = v.ReadUInt32(b);
                        if (type != 1 && type != 7) continue; // température ou utilisation
                        v.ReadArray(b + 12, label, 0, 128);
                        string l = Encoding.Default.GetString(label);
                        int z = l.IndexOf('\0');
                        if (z >= 0) l = l.Substring(0, z);
                        if (type == 7)
                        {
                            int rl = Array.IndexOf(GpuLoadLabels, l);
                            if (rl >= 0 && rl < loadRank) { loadRank = rl; load = (float)v.ReadDouble(b + 284); }
                            continue;
                        }
                        int rc = Array.IndexOf(CpuLabels, l), rg = Array.IndexOf(GpuLabels, l);
                        if (rc >= 0 && rc < cpuRank) { cpuRank = rc; cpu = (float)v.ReadDouble(b + 284); }
                        if (rg >= 0 && rg < gpuRank) { gpuRank = rg; gpu = (float)v.ReadDouble(b + 284); }
                    }
                }
                HwInfo = true;
            }
            catch { HwInfo = false; }
            CpuTemp = cpu;
            hwGpuTemp = gpu;
            hwGpuLoad = load;
        }

        #endregion
    }
}
