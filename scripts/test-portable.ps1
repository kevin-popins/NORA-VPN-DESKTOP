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
    "assets\logo.svg",
    "assets\flags\eu.png",
    "assets\flags\aq.png",
    "assets\emoji\1f680.png",
    "assets\fonts\NotoSansBamum-Regular.ttf",
    "assets\fonts\OFL.txt",
    "wintun.dll",
    "cores\xray.exe",
    "cores\sing-box.exe",
    "cores\amneziawg.exe",
    "cores\awg.exe",
    "cores\wintun.dll",
    "assets\router-with-screen-svgrepo-com.svg",
    "assets\locations\AQ1.jpg",
    "assets\locations\AQ2.jpg",
    "assets\locations\AQ3.jpg",
    "assets\locations\AQ4.jpg",
    "assets\locations\AQ5.jpg",
    "assets\locations\earth.png",
    "assets\locations\moon.png",
    "assets\locations\universe.jpg",
    "assets\locations\welocmescreengirl.jpg",
    "assets\locations\Discord1.jpg",
    "assets\locations\Discord2.jpg",
    "assets\locations\Discord3.jpg",
    "assets\locations\Discord4.jpg",
    "assets\locations\Germany4.jpg",
    "assets\locations\Germany5.jpg",
    "assets\locations\Netherlands4.jpg",
    "assets\locations\Netherlands5.jpg",
    "assets\locations\Poland1.jpg",
    "assets\locations\Poland2.jpg",
    "assets\locations\Poland3.jpg",
    "assets\locations\Latvia1.jpg",
    "assets\locations\Latvia2.jpg",
    "assets\locations\Latvia3.jpg",
    "assets\locations\Sweden1.jpg",
    "assets\locations\Sweden2.jpg",
    "assets\locations\Sweden3.jpg",
    "assets\locations\Bulgaria1.jpg",
    "assets\locations\Bulgaria2.jpg",
    "assets\locations\Bulgaria3.jpg",
    "assets\locations\India1.jpg",
    "assets\locations\India2.jpg",
    "assets\locations\India3.jpg",
    "assets\locations\KZ1.jpg",
    "assets\locations\KZ2.jpg",
    "assets\locations\KZ3.jpg",
    "assets\locations\TR1.jpg",
    "assets\locations\TR2.jpg",
    "assets\locations\TR3.jpg",
    "assets\locations\Ukraine1.jpg",
    "assets\locations\RUALL.jpg",
    "assets\locations\EUALL.jpg"
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

$brandIconPath = Join-Path $root "assets\logo.ico"
$brandIcon = Get-IcoFrameSizes $brandIconPath
$expectedBrandIconHash = "4097349A024503524D06D932F4FCBDAE1593BE16643369B337655704262B7CBD"
$actualBrandIconHash = (Get-FileHash -LiteralPath $brandIconPath -Algorithm SHA256).Hash
if ($actualBrandIconHash -ne $expectedBrandIconHash) {
    throw "Portable logo.ico is not the canonical NORA Windows icon: $actualBrandIconHash"
}
if (@(Get-ChildItem -LiteralPath (Join-Path $root "assets") -Filter "logo*.ico" -File | Where-Object Name -ne "logo.ico").Count -gt 0) {
    throw "Only the canonical assets\logo.ico may be packaged for Windows surfaces."
}
$missingBrandFrames = @(16, 24, 32, 48, 256) | Where-Object { $_ -notin $brandIcon.Sizes }
if ($missingBrandFrames.Count -gt 0) {
    throw "Portable title/tray logo.ico is missing expected frames: $($missingBrandFrames -join ', ')"
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
    foreach ($selfTestName in @("subscription-transport-selftest", "subscription-order-selftest", "roblox-routing-selftest", "subscription-refresh-selftest", "deep-link-selftest", "update-selftest", "dns-bootstrap-selftest", "country-flag-selftest", "display-region-selftest", "emoji-selftest", "location-artwork-selftest", "ui-preferences-selftest", "appearance-availability-selftest", "ui-icon-selftest", "discord-routing-selftest", "xray-tun-recovery-selftest")) {
        $selfTest = Start-Process -FilePath (Join-Path $root "NoraVPN.exe") -ArgumentList @($selfTestName) -WorkingDirectory $root -WindowStyle Hidden -Wait -PassThru
        if ($selfTest.ExitCode -ne 0) {
            throw "$selfTestName failed with exit code $($selfTest.ExitCode)"
        }
    }
}
else {
    $buildDll = Join-Path $PSScriptRoot "..\src\NoraVPN\bin\Release\net8.0-windows\win-x64\NoraVPN.dll"
    if (-not (Test-Path -LiteralPath $buildDll)) {
        throw "Non-elevated portable verification requires the Release build output: $buildDll"
    }
    foreach ($selfTestName in @("subscription-transport-selftest", "subscription-order-selftest", "roblox-routing-selftest", "subscription-refresh-selftest", "deep-link-selftest", "update-selftest", "dns-bootstrap-selftest", "country-flag-selftest", "display-region-selftest", "emoji-selftest", "location-artwork-selftest", "ui-preferences-selftest", "appearance-availability-selftest", "ui-icon-selftest", "discord-routing-selftest", "xray-tun-recovery-selftest")) {
        dotnet $buildDll $selfTestName
        if ($LASTEXITCODE -ne 0) {
            throw "$selfTestName failed from Release build output with exit code $LASTEXITCODE"
        }
    }
    Write-Warning "Portable NoraVPN.exe requires elevation; subscription and routing logic were verified from the matching Release build without opening a UAC prompt."
}

Write-Host "PORTABLE CORE CHECK PASS: canonical NORA icon on every Windows surface, $($brandIcon.Count) icon frames, $flagCount country flags, $emojiCount color emoji, VPN cores, subscription refresh, and Discord Mode routing are present and executable."
