# CoreCLR compatibility source

This branch is based on upstream commit
`34fa90162b0bc2a3317e9bd846a60bcbbe5c8205` on upstream master. Its source dependency changes
include the `MonoMod.Common` submodule revision documented in
[`MonoMod.Common/PATCHES.md`](MonoMod.Common/PATCHES.md).

Consumers must initialize the submodule and build from source. Do not reproduce
the compatibility fix as a post-build change to `MonoMod.Utils.dll`.
The public fork resolves `MonoMod.Common` from `LemonLoaderX/MonoMod.Common` so
the recorded gitlink always has a reproducible source repository.

## Native trampoline code publication

The generated undo-call-redo trampoline now makes the target writable before
restoring or reapplying its instructions, then makes it executable and flushes
the instruction cache before proceeding. Previously this generated path skipped
the publication steps already used by `NativeDetour.Apply` and `Undo`.
On systems with separate data and instruction caches, stale jump instructions
could interpret the restored function prologue as a destination address, causing
a native crash. Reapplication in the generated `finally` block uses the same
publication sequence, including when the original method throws.

The regression and build commands are in
[`tests/NativeTrampoline/README.md`](tests/NativeTrampoline/README.md).
This preserves the existing undo-call-redo design; it does not make concurrent
execution or concurrent changes to the same native function safe.

## ARM64 CoreCLR method entrypoints

The source fork's page-precode and .NET 11 JIT adaptations are documented in
[`MonoMod.Common/PATCHES.md`](MonoMod.Common/PATCHES.md). Validate template decoding
with `dotnet run --project tests/Arm64Precode/Arm64Precode.csproj -c Release` after
building RuntimeDetour as above. The fixtures cover 4/8/16/32/64 KiB offsets,
unprepared and null targets, wrong MethodDesc ownership, adapter rejection and
installed detours. They do not execute ARM instructions or qualify JIT callbacks;
The host fixture also checks JIT callback forwarding, pinned identity, error-state
preservation, nested compilation, failing subscribers/writers and final-Unpin
publication races using a synthetic compiler. It does not write .NET 11 MethodDesc
flags on its .NET 10 host. A separate MethodFlagsProbe calls the production atomic
helper against synthetic aligned storage while another thread updates runtime flags
and the adjacent slot. The factory's process-lifetime hook owner prevents
repeat selection from installing another callback or losing managed delegate roots.
Loader's Android smoke Mod exercises actual method patching and recompilation.

Android consumers first run `scripts/build-android-exception-helper.ps1` with
`AndroidNdkRoot` and `OutputPath`, then pass that output as
`-p:NativeExceptionHelperPath=<helper.so>` when building RuntimeDetour. The helper
is embedded in the assembly; it is not an additional installed runtime input.
The script targets API26 ARM64 and 16 KiB ELF alignment. Other builds can omit
the input, but cannot install this POSIX ARM64 JIT callback without it. The device
smoke includes missing-method compilation and invalid-IL rejection as well as
successful patch/unpatch, because native exceptions must traverse the hook.
The same helper carries the known-GUID native ICorJitInfo forwarding table and
allocation capture. The host PublicationProbe simulates RW-to-RX publication and
checks both surviving patch bytes and restoration of the original code on Undo.

Run `scripts/test-android-exception-helper.ps1 -DeviceSerial <serial>` with the
pinned Android NDK and SDK to check zero initialization of fresh thread exception
slots and retention of existing state. It uses dirty freed allocations and does
not require an installed application; run on native ARM64 and native-bridge devices.
