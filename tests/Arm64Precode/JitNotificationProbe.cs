using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using MonoMod.RuntimeDetour.Platforms;

internal static class JitNotificationProbe {
    private delegate int Compile(IntPtr jit, IntPtr info, IntPtr method, uint flags, out IntPtr entry, out uint size);
    private static int result;
    private static bool nested;
    private static Compile callback;
    private static int Target(int value) => value + 1;

    public static void Run(DetourRuntimeNET110Platform platform) {
        MethodInfo target = typeof(JitNotificationProbe).GetMethod(nameof(Target), BindingFlags.NonPublic | BindingFlags.Static);
        FieldInfo original = typeof(DetourRuntimeNET110Platform).GetField("original", BindingFlags.NonPublic | BindingFlags.Instance);
        original.SetValue(platform, Delegate.CreateDelegate(original.FieldType,
            typeof(JitNotificationProbe).GetMethod(nameof(Original), BindingFlags.NonPublic | BindingFlags.Static)));
        var compile = (Compile)Delegate.CreateDelegate(typeof(Compile), platform,
            typeof(DetourRuntimeNET110Platform).GetMethod("Compile", BindingFlags.NonPublic | BindingFlags.Instance));
        callback = compile;
        IntPtr request = Marshal.AllocHGlobal(IntPtr.Size);
        int notifications = 0;
        int successfulNotifications = 0;
        bool correctIdentity = false;
        FieldInfo writer = typeof(DetourRuntimeNET110Platform).Assembly.GetType("MonoMod.MMDbgLog")
            .GetField("Writer", BindingFlags.Public | BindingFlags.Static);
        object previousWriter = writer.GetValue(null);
        platform.OnMethodCompiled += (method, entry, size) => {
            if (!method.Equals(target) || entry != new IntPtr(0x23456700) || size != 32)
                throw new Exception("Incorrect JIT notification identity.");
            notifications++;
            Marshal.SetLastPInvokeError(0xBB);
            throw new InvalidOperationException("notification-fixture");
        };
        platform.OnMethodCompiled += (method, entry, size) => {
            correctIdentity = method.Equals(target) && entry == new IntPtr(0x23456700) && size == 32;
            successfulNotifications++;
        };
        try {
            platform.Pin(target);
            Marshal.WriteIntPtr(request, target.MethodHandle.Value);
            writer.SetValue(null, new ThrowingWriter());
            // Warm marshalling and callback helpers before measuring thread error state.
            compile(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _, out _);
            Marshal.SetLastPInvokeError(0xAA);
            if (compile(new IntPtr(1), IntPtr.Zero, request, 0, out IntPtr entry, out uint size) != 0 ||
                entry != new IntPtr(0x23456700) || size != 32 || notifications != 1 ||
                successfulNotifications != 1 || !correctIdentity ||
                Marshal.GetLastPInvokeError() != 0xAA)
                throw new Exception("JIT forwarding, exception containment or P/Invoke error preservation failed.");

            result = 1;
            if (compile(new IntPtr(1), IntPtr.Zero, request, 0, out _, out _) != 1 || notifications != 1)
                throw new Exception("Failed JIT compilation produced a notification.");
            result = 0;
            nested = true;
            compile(new IntPtr(1), IntPtr.Zero, request, 0, out _, out _);
            if (notifications != 2 || successfulNotifications != 2)
                throw new Exception("Nested compilation duplicated or lost the outer notification.");
            platform.Unpin(target);
            compile(new IntPtr(1), IntPtr.Zero, request, 0, out _, out _);
            if (notifications != 2)
                throw new Exception("Unpinned method produced a notification.");
        } finally {
            writer.SetValue(null, previousWriter);
            if (platform.GetPin(target).Count != 0)
                platform.Unpin(target);
            Marshal.FreeHGlobal(request);
            callback = null;
        }
        Console.WriteLine("JIT_NOTIFICATION_PASS forwarding, pinned identity, error state, reentrancy and observer/log failure containment");
    }

    private static int Original(IntPtr jit, IntPtr info, IntPtr method, uint flags, out IntPtr entry, out uint size) {
        if (nested) {
            nested = false;
            callback(jit, info, method, flags, out _, out _);
        }
        Marshal.SetLastPInvokeError(0xCC);
        entry = new IntPtr(0x23456700);
        size = 32;
        return result;
    }

    private sealed class ThrowingWriter : TextWriter {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string value) => throw new IOException("log-fixture");
    }
}
