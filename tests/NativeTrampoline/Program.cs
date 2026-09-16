using System;
using System.Reflection;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;

public static class Program {
    private static readonly TrackingNativePlatform Platform = new TrackingNativePlatform();
    private static readonly Exception OriginalFailure = new Exception("original failure");
    private static int OriginalCalls;

    public static int Original(int value) {
        OriginalCalls++;
        // Model ARM's separate data/instruction views at the actual emitted call boundary.
        if (Platform.Hooked || !Platform.Published || !Platform.Executable)
            throw new InvalidOperationException("Original code was called before executable permissions and instruction-cache publication.");
        if (value < 0)
            throw OriginalFailure;
        return value + 7;
    }

    public static int Main() {
        IDetourNativePlatform previous = DetourHelper.Native;
        _ = DetourHelper.Runtime;
        DetourHelper.Native = Platform;
        try {
            var config = new NativeDetourConfig { SkipILCopy = true };
            using (var detour = new NativeDetour(typeof(Program).GetMethod(nameof(Original)),
                Platform.Target, new IntPtr(0x2000), ref config)) {
                var trampoline = detour.GenerateTrampoline<Func<int, int>>();
                for (int i = 0; i < 3; i++) {
                    if (trampoline(i) != i + 7)
                        throw new Exception("Original return value changed.");
                    AssertReapplied();
                }
                Console.WriteLine("PASS: repeated trampoline calls publish restored and reapplied code");
                try {
                    trampoline(-1);
                    throw new Exception("Original exception was swallowed.");
                } catch (Exception exception) when (ReferenceEquals(exception, OriginalFailure)) {
                    AssertReapplied();
                }
                if (OriginalCalls != 4)
                    throw new Exception("Unexpected original call count.");
                Console.WriteLine("PASS: exceptional trampoline exit reapplies and publishes hook");
            }
            return 0;
        } catch (Exception exception) {
            Console.Error.WriteLine("FAIL: " + exception);
            return 1;
        } finally {
            DetourHelper.Native = previous;
        }
    }

    private static void AssertReapplied() {
        if (!Platform.Hooked || !Platform.Published || !Platform.Executable)
            throw new Exception("Reapplied hook was not executable and instruction-cache published.");
    }

    private sealed class TrackingNativePlatform : IDetourNativePlatform {
        public readonly IntPtr Target = new IntPtr(0x1000);
        public bool Hooked;
        public bool Published;
        public bool Executable = true;
        public NativeDetourData Create(IntPtr from, IntPtr to, byte? type = null) =>
            new NativeDetourData { Method = from, Target = to, Size = 16, Type = 4 };
        public void Free(NativeDetourData data) { }
        public void Apply(NativeDetourData data) {
            AssertWritable();
            Hooked = true;
            Published = false;
        }
        public void Copy(IntPtr src, IntPtr dst, byte type) {
            if (dst == Target) {
                AssertWritable();
                Hooked = false;
                Published = false;
            }
        }
        public void MakeWritable(IntPtr src, uint size) { Executable = false; }
        public void MakeExecutable(IntPtr src, uint size) { Executable = true; }
        public void MakeReadWriteExecutable(IntPtr src, uint size) { Executable = true; }
        public void FlushICache(IntPtr src, uint size) {
            if (src != Target || size != 16)
                throw new Exception("Wrong code publication range.");
            if (!Executable)
                throw new Exception("Code publication occurred before executable permissions were restored.");
            Published = true;
        }
        private void AssertWritable() {
            if (Executable)
                throw new Exception("Code was written without making the target writable.");
        }
        public IntPtr MemAlloc(uint size) => Marshal.AllocHGlobal((int)size);
        public void MemFree(IntPtr ptr) => Marshal.FreeHGlobal(ptr);
    }
}
