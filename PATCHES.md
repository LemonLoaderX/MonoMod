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
flags on its .NET 10 host. The factory's process-lifetime hook owner prevents
repeat selection from installing another callback or losing managed delegate roots.
Loader's Android smoke Mod exercises actual method patching and recompilation.
