using System;
using System.Reflection;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour.Platforms;

internal static class Program {
    private static readonly IntPtr MethodDesc = new IntPtr(0x12345600);
    private static readonly IntPtr Body = new IntPtr(0x23456700);
    private static readonly MethodInfo ReadPrecode = typeof(DetourRuntimeNETPlatform).GetMethod(
        "TryReadArm64PagePrecode", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new Exception("ARM64 page precode support is missing.");

    private static void Main() {
        foreach (int page in new[] { 4096, 8192, 16384, 32768, 65536 }) {
            IntPtr code = Marshal.AllocHGlobal(page + 24);
            try {
                WriteFixup(code, page);
                Marshal.WriteIntPtr(code, page, Body);
                Marshal.WriteIntPtr(code, page + 8, MethodDesc);
                AssertRead(code, true, Body, true);
                Marshal.WriteIntPtr(code, page, new IntPtr(code.ToInt64() + 8));
                AssertRead(code, true, new IntPtr(code.ToInt64() + 8), true);
                Marshal.WriteIntPtr(code, page, IntPtr.Zero);
                AssertRead(code, false);
                Marshal.WriteIntPtr(code, page, Body);
                Marshal.WriteIntPtr(code, page + 8, Body);
                AssertRead(code, false);
                Marshal.WriteIntPtr(code, page + 8, MethodDesc);
                Marshal.WriteInt32(code, 8, 0);
                AssertRead(code, false);

                uint load = 0x5800000Au | (uint)(((page + 8) / 4) << 5);
                Write(code, 0, load);
                Write(code, 4, load - 0x60u + 2u);
                Write(code, 8, 0xD61F0140u);
                Marshal.WriteIntPtr(code, page, MethodDesc);
                Marshal.WriteIntPtr(code, page + 8, Body);
                foreach (int type in new[] { 3, 5, 6, 7, 8, 10 }) {
                    Marshal.WriteIntPtr(code, page + 16, new IntPtr(type));
                    AssertRead(code, type == 3, Body, false);
                }
                // An installed detour has a short embedded literal, not a data page.
                Write(code, 0, 0x5800004Fu);
                Write(code, 4, 0xD61F01E0u);
                AssertRead(code, false);
            } finally {
                Marshal.FreeHGlobal(code);
            }
        }
        Console.WriteLine("ARM64_PRECODE_PASS page sizes, ownership, unprepared/null targets, adapters and detours");
        var platform = new PinRaceProbe.PausingPlatform();
        JitNotificationProbe.Run(platform);
        PinRaceProbe.Run(platform);
        PublicationProbe.Run();
        MethodFlagsProbe.Run();
    }

    private static void WriteFixup(IntPtr code, int page) {
        uint load = 0x5800000Bu | (uint)((page / 4) << 5);
        Write(code, 0, load);
        Write(code, 4, 0xD61F0160u);
        Write(code, 8, 0xD50339BFu);
        Write(code, 12, load - 0x20u + 1u);
        Write(code, 16, load);
        Write(code, 20, 0xD61F0160u);
    }

    private static void Write(IntPtr pointer, int offset, uint value) =>
        Marshal.WriteInt32(pointer, offset, unchecked((int)value));

    private static void AssertRead(IntPtr code, bool expected, IntPtr target = default, bool fixup = false) {
        object[] args = { code, MethodDesc, IntPtr.Zero, false };
        bool actual = (bool)ReadPrecode.Invoke(null, args);
        if (actual != expected || (actual && ((IntPtr)args[2] != target || (bool)args[3] != fixup)))
            throw new Exception("Incorrect ARM64 precode recognition or target.");
    }
}
