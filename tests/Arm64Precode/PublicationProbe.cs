using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.Platforms;

internal static class PublicationProbe {
    public static void Run() {
        var previous = DetourHelper.Native;
        var platform = new DetourNativeARMPlatform { ShouldFlushICache = false };
        IntPtr executable = Marshal.AllocHGlobal(64);
        IntPtr writable = Marshal.AllocHGlobal(64);
        byte[] original = Enumerable.Range(0, 64).Select(i => (byte)(i + 16)).ToArray();
        try {
            DetourHelper.Native = platform;
            Marshal.Copy(new byte[64], 0, executable, 64);
            Marshal.Copy(original, 0, writable, 64);
            using (var detour = new NativeDetour(executable, new IntPtr(0x12345600),
                new NativeDetourConfig { ManualApply = true, SkipILCopy = true })) {
                typeof(NativeDetour).GetMethod("ChangeSource", BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(IntPtr), typeof(IntPtr) }, null)
                    .Invoke(detour, new object[] { executable, writable });
                if (Marshal.ReadInt32(executable) != 0 || detour.Data.Method != executable)
                    throw new Exception("The pending detour wrote RX memory or retained an RW source address.");
                var published = new byte[64];
                Marshal.Copy(writable, published, 0, 64);
                Marshal.Copy(published, 0, executable, 64);
                if (Marshal.ReadInt32(executable) != unchecked((int)0x5800004F) ||
                    Marshal.ReadIntPtr(executable, 8) != new IntPtr(0x12345600))
                    throw new Exception("The detour did not survive CoreCLR's RW-to-RX publication.");
                detour.Undo();
                var restored = new byte[16];
                Marshal.Copy(executable, restored, 0, restored.Length);
                if (!restored.SequenceEqual(original.Take(restored.Length)))
                    throw new Exception("Unpatch restored unpublished RX bytes instead of the original code.");
            }
        } finally {
            DetourHelper.Native = previous;
            Marshal.FreeHGlobal(executable);
            Marshal.FreeHGlobal(writable);
        }
        Console.WriteLine("PUBLICATION_PASS RW patch survives RX publication; executable identity and original-code Undo preserved");
    }
}
