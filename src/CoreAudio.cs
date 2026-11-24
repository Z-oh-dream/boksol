using System;
using System.Runtime.InteropServices;

namespace Boksol
{
    internal sealed class CoreAudio : IDisposable
    {
        private readonly IntPtr handle;
        private readonly Slot[] queue;
        private readonly int headerSize = Marshal.SizeOf(typeof(Header));
        private int cursor;

        public CoreAudio()
        {
            var format = new WaveFormat { Format = 1, Channels = 2, Rate = 48000,
                BytesPerSecond = 192000, Block = 4, Bits = 16 };
            if (waveOutOpen(out handle, UInt32.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            {
                handle = IntPtr.Zero;
                return;
            }
            queue = new Slot[8];
            for (int i = 0; i < queue.Length; i++)
                queue[i] = new Slot { Header = Marshal.AllocHGlobal(headerSize), Data = Marshal.AllocHGlobal(8192) };
        }

        public void Write(short[] data, int count)
        {
            if (handle == IntPtr.Zero || count < 2 || count > 4096) return;
            var slot = queue[cursor];
            if (slot.Prepared)
            {
                var existing = Marshal.PtrToStructure<Header>(slot.Header);
                if ((existing.Flags & 1) == 0 || waveOutUnprepareHeader(handle, slot.Header, headerSize) != 0) return;
                slot.Prepared = false;
            }
            int even = count & ~1;
            Marshal.Copy(data, 0, slot.Data, even);
            Marshal.StructureToPtr(new Header { Data = slot.Data, Length = (uint)(even * 2) }, slot.Header, false);
            if (waveOutPrepareHeader(handle, slot.Header, headerSize) != 0) return;
            slot.Prepared = true;
            if (waveOutWrite(handle, slot.Header, headerSize) != 0 &&
                waveOutUnprepareHeader(handle, slot.Header, headerSize) == 0) slot.Prepared = false;
            cursor = (cursor + 1) % queue.Length;
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            waveOutReset(handle);
            foreach (var slot in queue)
            {
                if (slot.Prepared) waveOutUnprepareHeader(handle, slot.Header, headerSize);
                Marshal.FreeHGlobal(slot.Data);
                Marshal.FreeHGlobal(slot.Header);
            }
            waveOutClose(handle);
        }

        private sealed class Slot { public IntPtr Header, Data; public bool Prepared; }
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormat { public ushort Format, Channels; public uint Rate, BytesPerSecond; public ushort Block, Bits, Extra; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Header { public IntPtr Data; public uint Length, Recorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }
        [DllImport("winmm")] private static extern int waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm")] private static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm")] private static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm")] private static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm")] private static extern int waveOutReset(IntPtr handle);
        [DllImport("winmm")] private static extern int waveOutClose(IntPtr handle);
    }
}
