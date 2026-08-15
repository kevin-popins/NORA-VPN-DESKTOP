param(
    [Parameter(Mandatory = $true)]
    [string]$BaseInstallerPath,
    [Parameter(Mandatory = $true)]
    [string]$UpgradeInstallerPath,
    [string]$ResultPath = "C:\NORA-Installer-QA-result.json",
    [switch]$VisualQa
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$marker = if ($VisualQa) {
    Join-Path $repoRoot "artifacts\installer\NORA-INSTALLER-VISUAL-QA.marker"
}
else {
    "C:\NORA-INSTALLER-QA-SANDBOX.marker"
}
if (-not (Test-Path -LiteralPath $marker)) {
    throw "Installer lifecycle QA is destructive and may run only inside the dedicated Windows Sandbox."
}

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $VisualQa -and -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Installer lifecycle QA requires an elevated Windows Sandbox session."
}

$baseInstaller = (Resolve-Path -LiteralPath $BaseInstallerPath).Path
$upgradeInstaller = (Resolve-Path -LiteralPath $UpgradeInstallerPath).Path
if ($VisualQa) {
    $installDir = Join-Path $env:LOCALAPPDATA "NORA VPN Installer Lifecycle QA"
    $dataRoot = Join-Path $env:LOCALAPPDATA "NORA VPN Installer Data QA"
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) "NORA VPN.lnk"
    $startShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\NORA VPN\NORA VPN.lnk"
    $uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8A238707-9865-442C-8862-A39D3ED80518}_is1"
}
else {
    $installDir = "C:\Program Files\NORA VPN"
    $dataRoot = Join-Path $env:LOCALAPPDATA "NORA VPN"
    $desktopShortcut = "C:\Users\Public\Desktop\NORA VPN.lnk"
    $startShortcut = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\NORA VPN\NORA VPN.lnk"
    $uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7CBB6718-2B47-4EFC-9DE7-726C494E611A}_is1"
}

function Run-Setup([string]$path, [string[]]$arguments) {
    $process = Start-Process -FilePath $path -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Installer command failed with exit code $($process.ExitCode): $path $($arguments -join ' ')" }
}

function Add-Sentinel([string]$relative, [byte[]]$bytes) {
    $path = Join-Path $dataRoot $relative
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    [System.IO.File]::WriteAllBytes($path, $bytes)
}

function Snapshot-Sentinels {
    $result = [ordered]@{}
    Get-ChildItem -LiteralPath $dataRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($dataRoot, $_.FullName)
        $result[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    return $result
}

function Assert-Sentinels([System.Collections.IDictionary]$expected, [string]$stage) {
    $actual = Snapshot-Sentinels
    foreach ($entry in $expected.GetEnumerator()) {
        if (-not $actual.Contains($entry.Key) -or $actual[$entry.Key] -ne $entry.Value) {
            throw "User data changed during ${stage}: $($entry.Key)"
        }
    }
}

function Assert-Installed([string]$stage) {
    foreach ($path in @((Join-Path $installDir "NoraVPN.exe"), $desktopShortcut, $startShortcut)) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing installed artifact during ${stage}: $path" }
    }
    if (-not (Test-Path -LiteralPath $uninstallKey)) { throw "Missing uninstall registry entry during $stage." }
}

if (Test-Path -LiteralPath $dataRoot) { throw "Sandbox data root is not clean: $dataRoot" }
if (Test-Path -LiteralPath $uninstallKey) { throw "Sandbox already contains a NORA installation." }

[System.IO.Directory]::CreateDirectory($dataRoot) | Out-Null
Add-Sentinel "premium.dat" ([Security.Cryptography.RandomNumberGenerator]::GetBytes(521))
Add-Sentinel "appearance.json" ([Text.Encoding]::UTF8.GetBytes('{"slideshow":true,"sentinel":"premium-appearance"}'))
Add-Sentinel "subscriptions\provider\subscription.json" ([Text.Encoding]::UTF8.GetBytes('{"sentinel":"subscription-order-and-servers"}'))
Add-Sentinel "profiles\krot\client-profile.json" ([Text.Encoding]::UTF8.GetBytes('{"sentinel":"krot-profile"}'))
Add-Sentinel "external-profiles\fixture.json" ([Text.Encoding]::UTF8.GetBytes('{"sentinel":"external-profile"}'))
Add-Sentinel "state.json" ([Text.Encoding]::UTF8.GetBytes('{"ActiveProfilePath":"C:\\fixture\\selected.json"}'))
Add-Sentinel "ui-preferences.json" ([Text.Encoding]::UTF8.GetBytes('{"CloseToTrayNoticeDismissed":true}'))
Add-Sentinel "discord-mode.json" ([Text.Encoding]::UTF8.GetBytes('{"Enabled":true}'))
Add-Sentinel "managed-secrets.json" ([Text.Encoding]::UTF8.GetBytes('{"sentinel":"ssh-secret"}'))
$expected = Snapshot-Sentinels

$installArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS="desktopicon"', "/DIR=`"$installDir`"")
Run-Setup $baseInstaller $installArgs
Assert-Installed "clean install"
Assert-Sentinels $expected "clean install"

$legacyProfile = Join-Path $installDir "client-profile.json"
[System.IO.File]::WriteAllText($legacyProfile, '{"sentinel":"legacy-portable-profile"}')
$legacyHash = (Get-FileHash -LiteralPath $legacyProfile -Algorithm SHA256).Hash

Run-Setup $upgradeInstaller $installArgs
Assert-Installed "upgrade"
Assert-Sentinels $expected "upgrade"
if ((Get-FileHash -LiteralPath $legacyProfile -Algorithm SHA256).Hash -ne $legacyHash) { throw "Legacy client-profile.json changed during upgrade." }

Run-Setup $upgradeInstaller $installArgs
Assert-Installed "repair"
Assert-Sentinels $expected "repair"

$uninstaller = Get-ChildItem -LiteralPath $installDir -Filter "unins*.exe" | Select-Object -First 1
if (-not $uninstaller) { throw "Uninstaller was not created." }
Run-Setup $uninstaller.FullName @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
Assert-Sentinels $expected "default uninstall"
if (-not (Test-Path -LiteralPath $legacyProfile)) { throw "Default uninstall removed the user-owned legacy profile." }

Run-Setup $upgradeInstaller $installArgs
Assert-Installed "reinstall"
Assert-Sentinels $expected "reinstall"

$uninstaller = Get-ChildItem -LiteralPath $installDir -Filter "unins*.exe" | Select-Object -First 1
Run-Setup $uninstaller.FullName @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/PURGEUSERDATA')
if (Test-Path -LiteralPath $dataRoot) { throw "Explicit purge uninstall did not delete the exact user data root." }

$result = [ordered]@{
    passed = $true
    cleanInstall = $true
    upgrade = $true
    repair = $true
    premiumPreserved = $true
    subscriptionsPreserved = $true
    selectedServerPreserved = $true
    preferencesPreserved = $true
    defaultUninstallKeptData = $true
    explicitPurgeDeletedData = $true
    legacyPortableProfilePreserved = $true
}
[System.IO.File]::WriteAllText($ResultPath, ($result | ConvertTo-Json -Depth 4))
Write-Host "INSTALLER LIFECYCLE QA PASS: clean install, upgrade, repair, Premium/subscription/settings preservation, keep-data uninstall, explicit purge"
