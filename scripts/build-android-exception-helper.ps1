#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AndroidNdkRoot,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hostTag = if ($IsWindows) { 'windows-x86_64' } else { 'linux-x86_64' }
$suffix = if ($IsWindows) { '.exe' } else { '' }
$compiler = Join-Path $AndroidNdkRoot "toolchains/llvm/prebuilt/$hostTag/bin/clang$suffix"
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Android NDK clang is missing.' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath))
& $compiler --target=aarch64-linux-android26 -shared -fPIC -fuse-ld=lld `
    '-Wl,--eh-frame-hdr,-z,now,-z,noexecstack,-z,max-page-size=16384,--no-undefined' `
    '-Wl,--exclude-libs,ALL' '-Wl,--build-id=sha1' -s `
    (Join-Path $root 'MonoMod.Common/RuntimeDetour/Native/exception-helper-arm64.S') -o $OutputPath
if ($LASTEXITCODE) { throw "Native exception helper build failed: $LASTEXITCODE" }
