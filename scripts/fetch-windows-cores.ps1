param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\cores")
)

$ErrorActionPreference = "Stop"
$headers = @{ "User-Agent" = "NORA-VPN-build" }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/XTLS/Xray-core/releases/latest"
$asset = $release.assets | Where-Object name -eq "Xray-windows-64.zip" | Select-Object -First 1
if (-not $asset) {
    throw "The latest Xray release does not contain Xray-windows-64.zip"
}

$work = Join-Path $env:TEMP "nora-xray-core"
$zip = "$work.zip"
if (Test-Path $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
}
Invoke-WebRequest -Headers $headers -Uri $asset.browser_download_url -OutFile $zip
Expand-Archive -LiteralPath $zip -DestinationPath $work -Force
Copy-Item -LiteralPath (Join-Path $work "xray.exe") -Destination (Join-Path $OutputDirectory "xray.exe") -Force

Write-Host "Installed Xray $($release.tag_name) into $OutputDirectory"

$singRelease = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/SagerNet/sing-box/releases/latest"
$singAsset = $singRelease.assets |
    Where-Object { $_.name -match '^sing-box-.*-windows-amd64\.zip$' } |
    Select-Object -First 1
if (-not $singAsset) {
    throw "The latest sing-box release does not contain a Windows amd64 ZIP"
}

$singWork = Join-Path $env:TEMP "nora-sing-box-core"
$singZip = Join-Path $env:TEMP "nora-sing-box-core.zip"
if (Test-Path $singWork) {
    Remove-Item -LiteralPath $singWork -Recurse -Force
}
Invoke-WebRequest -Headers $headers -Uri $singAsset.browser_download_url -OutFile $singZip
if ($singAsset.digest) {
    $actualDigest = "sha256:" + (Get-FileHash -LiteralPath $singZip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualDigest -ne $singAsset.digest.ToLowerInvariant()) {
        throw "sing-box SHA-256 checksum verification failed"
    }
}
Expand-Archive -LiteralPath $singZip -DestinationPath $singWork -Force
$singExe = Get-ChildItem -LiteralPath $singWork -Recurse -Filter "sing-box.exe" | Select-Object -First 1
if (-not $singExe) {
    throw "sing-box.exe was not found in the verified release archive"
}
Copy-Item -LiteralPath $singExe.FullName -Destination (Join-Path $OutputDirectory "sing-box.exe") -Force

Write-Host "Installed sing-box $($singRelease.tag_name) into $OutputDirectory"

$awgRelease = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/amnezia-vpn/amneziawg-windows-client/releases/latest"
$awgAsset = $awgRelease.assets |
    Where-Object { $_.name -match '^amneziawg-amd64-[0-9].*\.msi$' -and $_.name -notmatch 'windows7' } |
    Select-Object -First 1
if (-not $awgAsset) {
    throw "The latest AmneziaWG release does not contain an amd64 MSI"
}

$awgMsi = Join-Path $env:TEMP "nora-amneziawg-core.msi"
$awgWork = Join-Path $env:TEMP "nora-amneziawg-core"
if (Test-Path $awgWork) {
    $resolvedAwgWork = (Resolve-Path -LiteralPath $awgWork).Path
    if ($resolvedAwgWork -ne $awgWork) {
        throw "Unexpected AmneziaWG extraction path: $resolvedAwgWork"
    }
    Remove-Item -LiteralPath $resolvedAwgWork -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $awgWork | Out-Null
Invoke-WebRequest -Headers $headers -Uri $awgAsset.browser_download_url -OutFile $awgMsi
$msi = Start-Process msiexec.exe -ArgumentList @('/a', $awgMsi, '/qn', "TARGETDIR=$awgWork") -WindowStyle Hidden -Wait -PassThru
if ($msi.ExitCode -ne 0) {
    throw "AmneziaWG MSI extraction failed with exit code $($msi.ExitCode)"
}
$awgBin = Join-Path $awgWork "AmneziaWG"
foreach ($name in @("amneziawg.exe", "awg.exe", "wintun.dll")) {
    Copy-Item -LiteralPath (Join-Path $awgBin $name) -Destination (Join-Path $OutputDirectory $name) -Force
}

Write-Host "Installed AmneziaWG $($awgRelease.tag_name) into $OutputDirectory"
