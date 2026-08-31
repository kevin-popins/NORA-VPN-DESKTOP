param(
    [string]$AppVersion = "0.1.0-pre.7-rc.1",
    [string]$InstallerVersion = "0.1.0.7",
    [string]$BuildId = "",
    [string]$OutputDirectory = "",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "src\NoraVPN\nvp.csproj"

if (-not $BuildId) {
    $BuildId = (git -C $root rev-parse --short=7 HEAD).Trim()
    if (git -C $root status --short) { $BuildId += "-dev" }
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root "dist" }
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = Join-Path $root "artifacts\installer"
$staging = Join-Path $artifactRoot ("payload-" + $BuildId)
$visuals = Join-Path $artifactRoot ("visuals-" + $BuildId)
$expectedSetup = Join-Path $output "NORA-VPN-Setup-$AppVersion-$BuildId-win-x64.exe"

foreach ($path in @($staging, $visuals)) {
    if (Test-Path -LiteralPath $path) {
        throw "Refusing to overwrite an existing installer staging path: $path"
    }
}
if (Test-Path -LiteralPath $expectedSetup) {
    throw "Refusing to overwrite an existing installer: $expectedSetup"
}
[System.IO.Directory]::CreateDirectory($staging) | Out-Null
[System.IO.Directory]::CreateDirectory($visuals) | Out-Null
[System.IO.Directory]::CreateDirectory($output) | Out-Null

if (-not $NoRestore) {
    dotnet restore $project
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

dotnet publish $project -c Release -f net8.0-windows -r win-x64 --self-contained true --no-restore -o $staging
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "test-portable.ps1") -PortableDirectory $staging
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "generate-installer-assets.ps1") -OutputDirectory $visuals
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6.7+ was not found. Install JRSoftware.InnoSetup and retry."
}

$script = Join-Path $root "installer\NoraVPN.iss"
$background = Join-Path $visuals "nora-installer-background.png"
$logo = Join-Path $visuals "nora-installer-logo.png"
& $iscc "/Qp" "/DPayloadDir=$staging" "/DOutputDir=$output" "/DAppVersion=$AppVersion" "/DInstallerVersion=$InstallerVersion" "/DBuildId=$BuildId" "/DInstallerBackground=$background" "/DInstallerLogo=$logo" $script
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not (Test-Path -LiteralPath $expectedSetup)) {
    throw "Inno Setup completed but the expected artifact is missing: $expectedSetup"
}

# Guard the canonical NORA identity on every public artifact. The setup
# compiler may re-encode ICO resources, so compare rendered 32x32 pixels rather
# than container bytes.
Add-Type -AssemblyName System.Drawing
function Get-IconPixels([string]$path) {
    $icon = [System.Drawing.Icon]::ExtractAssociatedIcon($path)
    if (-not $icon) { throw "No associated icon could be extracted from $path" }
    try {
        $bitmap = $icon.ToBitmap()
        try {
            $scaled = [System.Drawing.Bitmap]::new($bitmap, [System.Drawing.Size]::new(32, 32))
            try {
                $pixels = [System.Collections.Generic.List[int]]::new(32 * 32)
                for ($y = 0; $y -lt 32; $y++) {
                    for ($x = 0; $x -lt 32; $x++) { $pixels.Add($scaled.GetPixel($x, $y).ToArgb()) }
                }
                return $pixels.ToArray()
            }
            finally { $scaled.Dispose() }
        }
        finally { $bitmap.Dispose() }
    }
    finally { $icon.Dispose() }
}

$approvedIcon = Join-Path $root "assets\logo.ico"
$approvedPixels = Get-IconPixels $approvedIcon
$setupPixels = Get-IconPixels $expectedSetup
for ($index = 0; $index -lt $approvedPixels.Length; $index++) {
    if ($approvedPixels[$index] -ne $setupPixels[$index]) {
        throw "Installer icon invariant failed: setup does not render the canonical NORA icon."
    }
}

Write-Host "NORA INSTALLER READY: $expectedSetup"
