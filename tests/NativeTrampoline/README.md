# Native trampoline publication regression

Build the maintained source and run the generated trampoline regression with a
.NET 10 SDK:

```powershell
dotnet build MonoMod.RuntimeDetour/MonoMod.RuntimeDetour.csproj -c Release -f net5.0 -p:Version=22.7.31.1 -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false
dotnet run --project tests/NativeTrampoline/NativeTrampoline.csproj -c Release
```

To test an independently built DLL, pass
`-p:MonoModBinaryDirectory=<absolute-build-directory>` to `dotnet run`.
The directory must contain `MonoMod.RuntimeDetour.dll` and `MonoMod.Utils.dll`.

The test invokes the real `NativeDetour.GenerateTrampoline` with IL copying
disabled. A tracking native platform models separate instruction/data views and
executable permissions at the original-call boundary. It verifies repeated calls,
return values, the original exception, and hook publication after both normal and
exceptional exits. The unmodified implementation fails before calling published
original code. This host test does not replace an ARM device test of the native
function proxy and cache-maintenance implementation.
