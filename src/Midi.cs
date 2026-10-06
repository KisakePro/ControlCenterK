using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ControlCenterK
{
    public static class MidiDevices
    {
        public static List<string> Inputs()
        {
            var list = new List<string>();
            int n = Native.midiInGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new Native.MIDIINCAPS();
                list.Add(Native.midiInGetDevCapsW((UIntPtr)i, ref caps, Marshal.SizeOf(caps)) == 0 ? caps.szPname : "MIDI In " + i);
            }
            return list;
        }

        public static List<string> Outputs()
        {
            var list = new List<string>();
            int n = Native.midiOutGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new Native.MIDIOUTCAPS();
                list.Add(Native.midiOutGetDevCapsW((UIntPtr)i, ref caps, Marshal.SizeOf(caps)) == 0 ? caps.szPname : "MIDI Out " + i);
            }
            return list;
        }
    }

    public sealed class MidiInput : IDisposable
    {
        IntPtr handle;
        readonly Native.MidiInProc proc; // gardé en champ pour que le GC ne collecte pas le callback
        readonly Action<int> sink;

        public MidiInput(int index, Action<int> sink)
        {
            this.sink = sink;
            proc = Callback;
            int r = Native.midiInOpen(out handle, index, proc, IntPtr.Zero, Native.CALLBACK_FUNCTION);
            if (r != 0) throw new InvalidOperationException("midiInOpen a échoué (" + r + ")");
            Native.midiInStart(handle);
        }

        void Callback(IntPtr h, int msg, IntPtr inst, IntPtr p1, IntPtr p2)
        {
            // On ne fait rien de lourd ici : le message est juste mis en file pour le worker.
            if (msg == Native.MIM_DATA) sink((int)p1.ToInt64());
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            Native.midiInStop(handle);
            Native.midiInReset(handle);
            Native.midiInClose(handle);
            handle = IntPtr.Zero;
        }
    }

    public sealed class MidiOutput : IDisposable
    {
        IntPtr handle;

        public MidiOutput(int index)
        {
            int r = Native.midiOutOpen(out handle, index, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0) throw new InvalidOperationException("midiOutOpen a échoué (" + r + ")");
        }

        public void Send(int status, int d1, int d2)
        {
            if (handle != IntPtr.Zero)
                Native.midiOutShortMsg(handle, (uint)((status & 0xFF) | ((d1 & 0x7F) << 8) | ((d2 & 0x7F) << 16)));
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            Native.midiOutReset(handle);
            Native.midiOutClose(handle);
            handle = IntPtr.Zero;
        }
    }
}
