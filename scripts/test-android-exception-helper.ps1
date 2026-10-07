#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$AndroidNdkRoot = $env:ANDROID_NDK_ROOT,
    [string]$AndroidSdkRoot = $env:ANDROID_SDK_ROOT,
    [Parameter(Mandatory)][string]$DeviceSerial
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $root 'artifacts/native-exception-test'
[void][IO.Directory]::CreateDirectory($output)
$helper = Join-Path $output 'helper.so'
& "$PSScriptRoot/build-android-exception-helper.ps1" -AndroidNdkRoot $AndroidNdkRoot -OutputPath $helper
$hostTag = if ($IsWindows) { 'windows-x86_64' } else { 'linux-x86_64' }
$suffix = if ($IsWindows) { '.exe' } else { '' }
$compiler = Join-Path $AndroidNdkRoot "toolchains/llvm/prebuilt/$hostTag/bin/clang$suffix"
$test = Join-Path $output 'exception-slot-test'
& $compiler --target=aarch64-linux-android26 -O2 -Wall -Wextra -Werror `
    (Join-Path $root 'tests/NativeException/exception-slot-test.c') -ldl -pthread -o $test
if ($LASTEXITCODE) { throw 'Exception slot fixture build failed.' }
$adb = Join-Path $AndroidSdkRoot "platform-tools/adb$suffix"
$remote = "/data/local/tmp/monomod-exception-test-$([Guid]::NewGuid().ToString('N'))"
function Invoke-Adb([string[]]$Arguments) {
    & $adb -s $DeviceSerial @Arguments
    if ($LASTEXITCODE) { throw "ADB $($Arguments[0]) failed: $LASTEXITCODE" }
}
try {
    Invoke-Adb @('shell', 'mkdir', $remote)
    Invoke-Adb @('push', $helper, "$remote/helper.so")
    Invoke-Adb @('push', $test, "$remote/test")
    Invoke-Adb @('shell', 'chmod', '755', "$remote/test")
    Invoke-Adb @('shell', "$remote/test", "$remote/helper.so")
} finally {
    Invoke-Adb @('shell', 'rm', '-f', "$remote/test", "$remote/helper.so")
    Invoke-Adb @('shell', 'rmdir', $remote)
}
