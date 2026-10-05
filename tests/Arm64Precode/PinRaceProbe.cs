using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MonoMod.RuntimeDetour.Platforms;

internal static class PinRaceProbe {
    private static readonly MethodInfo TargetMethod = typeof(PinRaceProbe).GetMethod(nameof(Target), BindingFlags.NonPublic | BindingFlags.Static);
    private static int Target(int value) => value + 1;

    public static void Run(PausingPlatform platform) {
        var index = (Dictionary<IntPtr, MethodBase>)typeof(DetourRuntimeNET110Platform)
            .GetField("methods", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(platform);
        int originalCount = index.Count;
        Task pin = Task.Run(() => {
            platform.PinThreadId = Thread.CurrentThread.ManagedThreadId;
            platform.Pin(TargetMethod);
        });
        Task unpin = null;
        try {
            if (!platform.PinReady.Wait(TimeSpan.FromSeconds(5)))
                throw new Exception("Pin did not reach the controlled race boundary.");
            unpin = Task.Run(() => platform.Unpin(TargetMethod));
            if (!SpinWait.SpinUntil(() => platform.GetPin(TargetMethod).Count == 0, TimeSpan.FromSeconds(5)))
                throw new Exception("Unpin did not remove the base pin while publication was pending.");
        } finally {
            platform.ContinuePin.Set();
            if (!pin.Wait(TimeSpan.FromSeconds(5)) || (unpin != null && !unpin.Wait(TimeSpan.FromSeconds(5))))
                throw new Exception("Pin/Unpin race did not finish.");
            platform.PinReady.Dispose();
            platform.ContinuePin.Dispose();
        }
        if (platform.GetPin(TargetMethod).Count != 0 || index.Count != originalCount || index.ContainsKey(IntPtr.Zero))
            throw new Exception("Concurrent final Unpin left a stale JIT method index.");
        Console.WriteLine("PIN_RACE_PASS final Unpin during index publication leaves no stale entry");
    }

    internal sealed class PausingPlatform : DetourRuntimeNET110Platform {
        internal int PinThreadId;
        internal readonly ManualResetEventSlim PinReady = new ManualResetEventSlim();
        internal readonly ManualResetEventSlim ContinuePin = new ManualResetEventSlim();

        // Host fixtures run on .NET 10 x64 and do not qualify .NET 11 MethodDesc writes.
        protected override void DisableInlining(MethodBase method, RuntimeMethodHandle handle) { }

        public override MethodPinInfo GetPin(MethodBase method) {
            if (Thread.CurrentThread.ManagedThreadId == PinThreadId && method.Equals(TargetMethod)) {
                PinReady.Set();
                if (!ContinuePin.Wait(TimeSpan.FromSeconds(5)))
                    throw new Exception("Pin publication was not released.");
            }
            return base.GetPin(method);
        }
    }
}
