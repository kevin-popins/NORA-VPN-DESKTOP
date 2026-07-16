param(
    [Parameter(Mandatory = $true)]
    [string]$PortableDirectory
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $PortableDirectory).Path
$required = @(
    "NoraVPN.exe",
    "assets\logo.png",
    "assets\logo.ico",
    "assets\logo-taskbar.ico",
    "assets\logo.svg",
    "assets\flags\eu.png",
    "assets\emoji\1f680.png",
    "wintun.dll",
    "cores\xray.exe",
    "cores\sing-box.exe",
    "cores\amneziawg.exe",
    "cores\awg.exe",
    "cores\wintun.dll",
    "assets\router-with-screen-svgrepo-com.svg",
    "assets\locations\Discord1.png",
    "assets\locations\Discord2.png",
    "assets\locations\Discord3.png",
    "assets\locations\Discord4.png"
)

$missing = $required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $root $_)) }
if ($missing.Count -gt 0) {
    throw "Portable layout is incomplete: $($missing -join ', ')"
}

function Get-IcoFrameSizes([string]$path) {
    $iconBytes = [System.IO.File]::ReadAllBytes($path)
    if ($iconBytes.Length -lt 22 -or [System.BitConverter]::ToUInt16($iconBytes, 2) -ne 1) {
        throw "Invalid Windows icon resource: $path"
    }
    $frameCount = [System.BitConverter]::ToUInt16($iconBytes, 4)
    $frameSizes = for ($index = 0; $index -lt $frameCount; $index++) {
        $entry = 6 + 16 * $index
        if ($entry + 1 -ge $iconBytes.Length) {
            throw "Truncated Windows icon directory: $path"
        }
        $size = [int]$iconBytes[$entry]
        if ($size -eq 0) { 256 } else { $size }
    }
    return [PSCustomObject]@{ Count = $frameCount; Sizes = $frameSizes }
}

$brandIcon = Get-IcoFrameSizes (Join-Path $root "assets\logo.ico")
$taskbarIcon = Get-IcoFrameSizes (Join-Path $root "assets\logo-taskbar.ico")
$missingBrandFrames = @(16, 24, 32, 48, 256) | Where-Object { $_ -notin $brandIcon.Sizes }
if ($missingBrandFrames.Count -gt 0) {
    throw "Portable title/tray logo.ico is missing expected frames: $($missingBrandFrames -join ', ')"
}
$missingTaskbarFrames = @(16, 20, 24, 32, 48, 256) | Where-Object { $_ -notin $taskbarIcon.Sizes }
if ($missingTaskbarFrames.Count -gt 0) {
    throw "Portable taskbar icon is missing expected frames: $($missingTaskbarFrames -join ', ')"
}

$flagCount = (Get-ChildItem -LiteralPath (Join-Path $root "assets\flags") -Filter *.png -ErrorAction Stop).Count
if ($flagCount -lt 250) {
    throw "Portable flag set is incomplete: expected at least 250 PNG flags, found $flagCount."
}

$emojiCount = (Get-ChildItem -LiteralPath (Join-Path $root "assets\emoji") -Filter *.png -ErrorAction Stop).Count
if ($emojiCount -lt 3600) {
    throw "Portable emoji set is incomplete: expected at least 3600 PNG emoji, found $emojiCount."
}

& (Join-Path $root "cores\xray.exe") version | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "xray.exe version check failed with exit code $LASTEXITCODE"
}

& (Join-Path $root "cores\sing-box.exe") version | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "sing-box.exe version check failed with exit code $LASTEXITCODE"
}

$isAdministrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdministrator) {
    $discordSelfTest = Start-Process -FilePath (Join-Path $root "NoraVPN.exe") -ArgumentList @("discord-routing-selftest") -WorkingDirectory $root -WindowStyle Hidden -Wait -PassThru
    if ($discordSelfTest.ExitCode -ne 0) {
        throw "Discord routing self-test failed with exit code $($discordSelfTest.ExitCode)"
    }
}
else {
    $buildDll = Join-Path $PSScriptRoot "..\src\NoraVPN\bin\Release\net8.0-windows\win-x64\NoraVPN.dll"
    if (-not (Test-Path -LiteralPath $buildDll)) {
        throw "Non-elevated portable verification requires the Release build output: $buildDll"
    }
    dotnet $buildDll discord-routing-selftest
    if ($LASTEXITCODE -ne 0) {
        throw "Discord routing self-test failed from Release build output with exit code $LASTEXITCODE"
    }
    Write-Warning "Portable NoraVPN.exe requires elevation; routing logic was verified from the matching Release build without opening a UAC prompt."
}

Write-Host "PORTABLE CORE CHECK PASS: all required assets, $($brandIcon.Count) title/tray frames, $($taskbarIcon.Count) taskbar frames, $flagCount country flags, $emojiCount color emoji, VPN cores, and Discord Mode routing are present and executable."
