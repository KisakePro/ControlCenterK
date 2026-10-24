using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace ControlCenterK
{
    /// <summary>
    /// Sonde de température du processeur (Ryzen) : la température n'est lisible que par un pilote noyau.
    /// On utilise PawnIO (pilote signé, open source) et son module « AMDFamily17 » (LGPL-2.1, embarqué).
    /// PawnIO exige les droits administrateur : la sonde est ControlCenterK lancé avec « --sonde » par une tâche
    /// planifiée (droits élevés, créée une seule fois). Elle publie la température en mémoire partagée et s'arrête
    /// d'elle-même quand ControlCenterK n'est plus lancé.
    /// </summary>
    static class CpuProbe
    {
        const string TaskName = "ControlCenterK Sonde";
        const string MapName = @"Global\ControlCenterK.Sensors";
        const string PawnLib = @"C:\Program Files\PawnIO\PawnIOLib.dll";
        const int Magic = 0x4B43434B; // "KCCK"

        public static bool PawnIOInstalled { get { return File.Exists(PawnLib); } }

        #region Côté ControlCenterK

        /// <summary>Tâche planifiée présente ?</summary>
        public static bool Installed()
        {
            return Run("schtasks.exe", "/Query /TN \"" + TaskName + "\"") == 0;
        }

        /// <summary>Crée la tâche (droits élevés, à l'ouverture de session) et la démarre : une demande administrateur.</summary>
        public static bool Install()
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string user = WindowsIdentity.GetCurrent().Name;
            string script =
                "$a = New-ScheduledTaskAction -Execute '" + exe.Replace("'", "''") + "' -Argument '--sonde'\r\n" +
                "$t = New-ScheduledTaskTrigger -AtLogOn -User '" + user.Replace("'", "''") + "'\r\n" +
                "$p = New-ScheduledTaskPrincipal -UserId '" + user.Replace("'", "''") + "' -LogonType Interactive -RunLevel Highest\r\n" +
                "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew\r\n" +
                "Register-ScheduledTask -TaskName '" + TaskName + "' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null\r\n" +
                "Start-ScheduledTask -TaskName '" + TaskName + "'\r\n";
            return RunElevated(script) && Installed();
        }

        public static bool Uninstall()
        {
            return RunElevated("Stop-ScheduledTask -TaskName '" + TaskName + "' -ErrorAction SilentlyContinue\r\n" +
                               "Unregister-ScheduledTask -TaskName '" + TaskName + "' -Confirm:$false\r\n") && !Installed();
        }

        static DateTime lastStart;

        /// <summary>Lance la sonde si elle est installée mais ne tourne pas (au plus toutes les 30 s).</summary>
        public static void EnsureRunning()
        {
            if (Running() || (DateTime.Now - lastStart).TotalSeconds < 30) return;
            lastStart = DateTime.Now;
            ThreadPool.QueueUserWorkItem(_ => { if (Installed()) Run("schtasks.exe", "/Run /TN \"" + TaskName + "\""); });
        }

        static bool Running()
        {
            try { using (MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read)) return true; }
            catch { return false; }
        }

        /// <summary>Dernière température publiée par la sonde (NaN si absente ou figée depuis plus de 5 s).</summary>
        public static float Read()
        {
            try
            {
                using (var mm = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read))
                using (var v = mm.CreateViewAccessor(0, 16, MemoryMappedFileAccess.Read))
                {
                    if (v.ReadInt32(0) != Magic) return float.NaN;
                    long stamp = v.ReadInt64(8);
                    if (Math.Abs(Environment.TickCount - (int)stamp) > 5000) return float.NaN;
                    return v.ReadSingle(4);
                }
            }
            catch { return float.NaN; }
        }

        static int Run(string exe, string args)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        static bool RunElevated(string script)
        {
            string file = Path.Combine(Path.GetTempPath(), "ControlCenterK-sonde.ps1");
            File.WriteAllText(file, "$ErrorActionPreference = 'Stop'\r\n" + script, new System.Text.UTF8Encoding(true));
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + file + "\"")
                    { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
                    p.WaitForExit();
                return true;
            }
            catch (System.ComponentModel.Win32Exception) { return false; } // refusé
            finally { try { File.Delete(file); } catch { } }
        }

        #endregion

        #region Côté sonde (processus élevé)

        [DllImport(PawnLib)] static extern int pawnio_open(out IntPtr h);
        [DllImport(PawnLib)] static extern int pawnio_load(IntPtr h, byte[] blob, UIntPtr size);
        [DllImport(PawnLib, CharSet = CharSet.Ansi)] static extern int pawnio_execute(IntPtr h, string name, ulong[] inp, UIntPtr inSize, ulong[] outp, UIntPtr outSize, out UIntPtr ret);
        [DllImport(PawnLib)] static extern int pawnio_close(IntPtr h);

        /// <summary>Boucle de la sonde : lit Tctl deux fois par seconde tant que ControlCenterK tourne.</summary>
        public static void RunProbe()
        {
            IntPtr h;
            if (!PawnIOInstalled || pawnio_open(out h) != 0) return;
            try
            {
                byte[] blob;
                using (var s = typeof(CpuProbe).Assembly.GetManifestResourceStream("AMDFamily17.bin"))
                using (var ms = new MemoryStream()) { s.CopyTo(ms); blob = ms.ToArray(); }
                if (pawnio_load(h, blob, (UIntPtr)blob.Length) != 0) return;

                // lisible par ControlCenterK (utilisateur sans droits élevés), modifiable seulement par la sonde
                var sec = new MemoryMappedFileSecurity();
                sec.AddAccessRule(new AccessRule<MemoryMappedFileRights>(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), MemoryMappedFileRights.Read, AccessControlType.Allow));
                sec.AddAccessRule(new AccessRule<MemoryMappedFileRights>(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), MemoryMappedFileRights.FullControl, AccessControlType.Allow));
                using (var mm = MemoryMappedFile.CreateOrOpen(MapName, 16, MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.None, sec, HandleInheritability.None))
                using (var v = mm.CreateViewAccessor(0, 16))
                using (var pci = new Mutex(false, @"Global\Access_PCI")) // partagé avec les autres outils (HWiNFO, LibreHardwareMonitor…)
                {
                    int idle = 0;
                    while (idle < 60) // 30 s sans ControlCenterK : la sonde s'arrête
                    {
                        float t = Tctl(h, pci);
                        if (!float.IsNaN(t))
                        {
                            v.Write(4, t);
                            v.Write(8, (long)Environment.TickCount);
                            v.Write(0, Magic);
                        }
                        idle = Process.GetProcessesByName("ControlCenterK").Length > 1 ? 0 : idle + 1;
                        Thread.Sleep(500);
                    }
                }
            }
            finally { pawnio_close(h); }
        }

        /// <summary>Tctl : registre SMN 0x59800 (THM_TCON_CUR_TMP), 0,125 °C par pas, -49 °C si la plage étendue est active.</summary>
        static float Tctl(IntPtr h, Mutex pci)
        {
            var o = new ulong[1];
            UIntPtr ret;
            bool own;
            try { own = pci.WaitOne(200); } catch (AbandonedMutexException) { own = true; }
            try
            {
                if (pawnio_execute(h, "ioctl_read_smn", new ulong[] { 0x59800 }, (UIntPtr)1, o, (UIntPtr)1, out ret) != 0) return float.NaN;
            }
            finally { if (own) pci.ReleaseMutex(); }
            uint v = (uint)o[0];
            float temp = ((v >> 21) & 0x7FF) * 0.125f;
            if ((v & 0x80000) != 0) temp -= 49;
            return temp;
        }

        #endregion
    }
}
