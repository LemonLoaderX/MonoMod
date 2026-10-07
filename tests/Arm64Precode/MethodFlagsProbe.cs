using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using MonoMod.RuntimeDetour.Platforms;

internal static class MethodFlagsProbe {
    internal static unsafe void Run() {
        var setNotInline = (Action<IntPtr>)Delegate.CreateDelegate(typeof(Action<IntPtr>),
            typeof(DetourRuntimeNET110Platform).GetMethod("SetNotInline", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new Exception("Atomic MethodDesc flag update is missing."));
        IntPtr descriptor = Marshal.AllocHGlobal(16);
        using var barrier = new Barrier(3);
        const int rounds = 10000;
        int flagMask = BitConverter.IsLittleEndian ? 0x20000000 : 0x2000;
        int otherFlag = BitConverter.IsLittleEndian ? 0x40000000 : 0x4000;
        int slotMask = BitConverter.IsLittleEndian ? 0x80 : 0x00800000;
        bool preserved = true;
        var detour = new Thread(() => {
            for (int i = 0; i < rounds; i++) {
                barrier.SignalAndWait();
                setNotInline(descriptor);
                barrier.SignalAndWait();
            }
        });
        var runtime = new Thread(() => {
            for (int i = 0; i < rounds; i++) {
                barrier.SignalAndWait();
                Interlocked.Or(ref *(int*)((byte*)descriptor + 4), otherFlag | slotMask);
                barrier.SignalAndWait();
            }
        });
        try {
            Marshal.WriteInt32(descriptor, 0, 0x12345678);
            Marshal.WriteInt32(descriptor, 8, 0x76543210);
            detour.Start();
            runtime.Start();
            for (int i = 0; i < rounds; i++) {
                Marshal.WriteInt32(descriptor, 4, 0x01010101);
                barrier.SignalAndWait();
                barrier.SignalAndWait();
                preserved &= Marshal.ReadInt32(descriptor, 4) == (0x01010101 | flagMask | otherFlag | slotMask);
            }
            detour.Join();
            runtime.Join();
            if (!preserved || Marshal.ReadInt32(descriptor, 0) != 0x12345678 ||
                Marshal.ReadInt32(descriptor, 8) != 0x76543210)
                throw new Exception("MethodDesc update lost concurrent flags or changed adjacent storage.");
            Console.WriteLine("METHOD_FLAGS_PASS concurrent runtime flags, adjacent slot and surrounding fields");
        } finally {
            Marshal.FreeHGlobal(descriptor);
        }
    }
}
