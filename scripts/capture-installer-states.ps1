param(
    [Parameter(Mandatory = $true)]
    [string]$VisualQaInstallerPath,
    [string]$OutputDirectory = "",
    [string[]]$States = @('welcome', 'license', 'notices', 'folder', 'shortcut', 'ready', 'progress', 'success', 'update', 'running', 'uninstall'),
    [switch]$AttachExisting
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NoraWindowCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetLastActivePopup(IntPtr hwnd);
}
"@

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$installer = (Resolve-Path -LiteralPath $VisualQaInstallerPath).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root "docs\assets\screenshots\installer"
}
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($output) | Out-Null
$qaInstallDir = Join-Path $env:LOCALAPPDATA "NORA VPN Installer Visual QA"

function Wait-MainWindow([DateTime]$startedAt, [int]$timeoutSeconds = 20) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        Start-Sleep -Milliseconds 200
        $candidate = Get-Process | Where-Object {
            $_.MainWindowHandle -ne [IntPtr]::Zero -and
            ($_.MainWindowTitle -like '*NORA VPN*' -or $_.ProcessName -like '_unins*' -or
                $_.ProcessName -like 'NORA-VPN-Setup-*') -and
            $_.ProcessName -ne 'NoraVPN' -and
            $_.StartTime.ToUniversalTime() -ge $startedAt.AddSeconds(-1)
        } | Sort-Object StartTime -Descending | Select-Object -First 1
        if ($candidate) { return $candidate }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for the Visual QA installer window."
}

function Save-Window([IntPtr]$handle, [string]$path) {
    $rect = [NoraWindowCapture+RECT]::new()
    if (-not [NoraWindowCapture]::GetWindowRect($handle, [ref]$rect)) {
        throw "GetWindowRect failed."
    }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -lt 300 -or $height -lt 120) { throw "Unexpected installer window size: ${width}x${height}." }
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $hdc = $graphics.GetHdc()
            try {
                if (-not [NoraWindowCapture]::PrintWindow($handle, $hdc, 2)) {
                    throw "PrintWindow failed."
                }
            }
            finally { $graphics.ReleaseHdc($hdc) }
        }
        finally { $graphics.Dispose() }
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}

function Capture-State([string]$state, [int]$waitSeconds) {
    $qaPage = if ($state -eq 'update') { 'welcome' } else { $state }
    $displayInstallDir = if ($state -in @('progress', 'success', 'running')) { $qaInstallDir } else { 'C:\Program Files\NORA VPN' }
    $arguments = @(
        '/LANG=russian',
        "/QAPAGE=$qaPage",
        '/NORESTART',
        "/DIR=`"$displayInstallDir`""
    )
    $runningMutex = if ($state -eq 'running') { [Threading.Mutex]::new($true, 'Local\NORA.VPN.Desktop.SingleInstance.v1') } else { $null }
    $startedAt = [DateTime]::UtcNow
    $process = Start-Process -FilePath $installer -ArgumentList $arguments -PassThru
    $windowProcess = $null
    try {
        $windowProcess = Wait-MainWindow $startedAt
        Start-Sleep -Seconds $waitSeconds
        $windowProcess.Refresh()
        if ($windowProcess.HasExited) { throw "Visual QA installer exited before $state was captured." }
        $handle = $windowProcess.MainWindowHandle
        if ($state -eq 'running') {
            $popup = [NoraWindowCapture]::GetLastActivePopup($handle)
            if ($popup -ne [IntPtr]::Zero -and $popup -ne $handle) { $handle = $popup }
        }
        $path = Join-Path $output ("installer-" + $state + ".png")
        Save-Window $handle $path
        Write-Host "CAPTURED: $path"
    }
    finally {
        if ($windowProcess -and -not $windowProcess.HasExited) {
            $null = $windowProcess.CloseMainWindow()
            if (-not $windowProcess.WaitForExit(3000)) { $windowProcess.Kill($true) }
        }
        if (-not $process.HasExited) {
            if (-not $process.WaitForExit(3000)) { $process.Kill($true) }
        }
        if ($runningMutex) { $runningMutex.Dispose() }
        Start-Sleep -Seconds 2
    }
}

if ($AttachExisting) {
    if ($States.Count -ne 1) { throw "AttachExisting requires exactly one output state name." }
    $windowProcess = Wait-MainWindow ([DateTime]::UtcNow.AddHours(-1))
    $path = Join-Path $output ("installer-" + $States[0] + ".png")
    Save-Window $windowProcess.MainWindowHandle $path
    Write-Host "CAPTURED EXISTING WINDOW: $path"
    exit 0
}

foreach ($state in $States) {
    if ($state -notin @('welcome', 'license', 'notices', 'folder', 'shortcut', 'ready', 'progress', 'success', 'update', 'running', 'uninstall')) {
        throw "Unsupported installer visual state: $state"
    }
    if ($state -eq 'uninstall') {
        $uninstaller = Join-Path $qaInstallDir 'unins000.exe'
        if (-not (Test-Path -LiteralPath $uninstaller)) { throw "Visual QA uninstaller is missing: $uninstaller" }
        $startedAt = [DateTime]::UtcNow
        $process = Start-Process -FilePath $uninstaller -PassThru
        try {
            $windowProcess = Wait-MainWindow $startedAt
            Start-Sleep -Seconds 1
            $handle = [NoraWindowCapture]::GetLastActivePopup($windowProcess.MainWindowHandle)
            if ($handle -eq [IntPtr]::Zero) { $handle = $windowProcess.MainWindowHandle }
            $path = Join-Path $output 'installer-uninstall.png'
            Save-Window $handle $path
            Write-Host "CAPTURED: $path"
        }
        finally {
            if ($windowProcess -and -not $windowProcess.HasExited) { $windowProcess.Kill($true) }
            if (-not $process.HasExited) { $process.Kill($true) }
        }
        continue
    }
    $waitSeconds = if ($state -eq 'success') { 60 } elseif ($state -eq 'progress') { 2 } else { 1 }
    Capture-State $state $waitSeconds
}

Write-Host "INSTALLER VISUAL STATE CAPTURE COMPLETE: $output"
