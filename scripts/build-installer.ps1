param(
    [string]$AppVersion = '',
    [string]$InstallerVersion = '',
    [string]$BuildId = "",
    [string]$OutputDirectory = "",
    [switch]$NoRestore,
    [string]$CompilerPath = '',
    [ValidateSet('Full', 'Isolated')]
    [string]$VerificationMode = 'Full',
    [string]$ManagedTestDll = '',
    [string]$ProofDirectory = '',
    [string]$ApprovedCoreBundle = '',
    [string]$ApprovedCoreBundleSha256 = '758fc43fbf02efb43706ca1e0f0beee3026e1c3c97a8869a6e5a141f826c7c96'
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "src\NoraVPN\nvp.csproj"
. (Join-Path $PSScriptRoot 'portable-verification.ps1')
$identity = Get-NoraBuildIdentity $root $AppVersion $InstallerVersion ''
$AppVersion = $identity.AppVersion
$InstallerVersion = $identity.FileVersion

if (-not $BuildId) {
    $BuildId = (git -C $root rev-parse --short=7 HEAD).Trim()
    if (git -C $root status --short) { $BuildId += "-dev" }
}
if ($BuildId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$') { throw 'BuildId must be a simple artifact suffix, not a path.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root "dist" }
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = Join-Path $root "artifacts\installer"
$staging = Join-Path $artifactRoot ("payload-" + $BuildId)
$visuals = Join-Path $artifactRoot ("visuals-" + $BuildId)
# Older clients require this exact public asset name to discover an update.
$expectedSetup = Join-Path $output "NORA-VPN-Setup-v$AppVersion-win-x64.exe"

if ($VerificationMode -eq 'Isolated') {
    $privateProof = Assert-NoraPrivateProofDirectory $ProofDirectory $root $staging
    if (-not $ManagedTestDll -or -not (Test-Path -LiteralPath $ManagedTestDll -PathType Leaf)) { throw 'Isolated installer packaging requires ManagedTestDll.' }
    Assert-NoraFileVersion (Get-NoraFileIdentity $ManagedTestDll) $identity
    if (-not $ApprovedCoreBundle -or -not (Test-Path -LiteralPath $ApprovedCoreBundle -PathType Leaf)) { throw 'Isolated installer packaging requires an approved existing core ZIP.' }
    if ((Get-FileHash -LiteralPath $ApprovedCoreBundle -Algorithm SHA256).Hash -ne $ApprovedCoreBundleSha256) { throw 'Approved core bundle digest mismatch.' }
}

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

if (-not $NoRestore -and $VerificationMode -eq 'Full') {
    dotnet restore $project
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$offlineEnvironment = @{}
if ($VerificationMode -eq 'Isolated') { $offlineEnvironment = Set-NoraOfflineBuildEnvironment }
try {
    dotnet publish $project -c Release -f net8.0-windows -r win-x64 --self-contained true --no-restore -o $staging "-p:Version=$AppVersion" "-p:FileVersion=$InstallerVersion" "-p:InformationalVersion=$($identity.InformationalVersion)" "-p:SourceRevisionId=$($identity.Commit)" '-p:IncludeSourceRevisionInInformationalVersion=false'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally { Restore-NoraBuildEnvironment $offlineEnvironment }

# Fail before Inno compiles if its requested public version differs from the
# actual application payload. Defaults come from csproj, overrides reach publish.
$compiledExecutable = Get-NoraFileIdentity (Join-Path $staging 'NoraVPN.exe')
Assert-NoraFileVersion $compiledExecutable $identity
$verification = & (Join-Path $PSScriptRoot "test-portable.ps1") -PortableDirectory $staging -VerificationMode $VerificationMode -ManagedTestDll $ManagedTestDll -ProofDirectory $ProofDirectory -ExpectedAppVersion $AppVersion -ExpectedFileVersion $InstallerVersion -ExpectedCommit $identity.Commit -ApprovedCoreBundle $ApprovedCoreBundle -ApprovedCoreBundleSha256 $ApprovedCoreBundleSha256
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "generate-installer-assets.ps1") -OutputDirectory $visuals
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = if ($CompilerPath) {
    (Resolve-Path -LiteralPath $CompilerPath -ErrorAction Stop).Path
} else {
    $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
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
$setupIdentity = Get-NoraFileIdentity $expectedSetup
$setupVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($expectedSetup)
$setupNumericVersion = '{0}.{1}.{2}.{3}' -f $setupVersion.FileMajorPart, $setupVersion.FileMinorPart, $setupVersion.FileBuildPart, $setupVersion.FilePrivatePart
if ($setupNumericVersion -ne $InstallerVersion -or $setupVersion.ProductVersion.Trim() -ne $AppVersion) {
    throw 'The compiled installer numeric/text version does not match the verified application payload.'
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
if ($VerificationMode -eq 'Isolated') {
    $evidence = Join-Path (Split-Path -Parent $verification.ManifestPath) 'installer-package.json'
    [ordered]@{
        Schema = 'nora-installer-package-evidence-v1'; CreatedUtc = [DateTime]::UtcNow.ToString('O')
        Build = $identity; BuildId = $BuildId; VerificationMode = $VerificationMode; RestoreInvoked = $false
        Publish = [ordered]@{ Configuration = 'Release'; Framework = 'net8.0-windows'; Runtime = 'win-x64'; SelfContained = $true; NoRestore = $true; InformationalVersionIncludesCommit = $true; WorkloadAdvertisingDisabled = $true }
        Payload = $compiledExecutable; Installer = $setupIdentity; InstallerNumericVersion = $setupNumericVersion; VerificationManifest = $verification.ManifestPath
        InnoCompiler = Get-NoraFileIdentity $iscc; InstallerScriptSha256 = (Get-FileHash -LiteralPath $script -Algorithm SHA256).Hash.ToLowerInvariant()
        CanonicalIconPixelsMatched = $true; SetupExecuted = $false; InstalledAppAccessed = $false
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidence -Encoding UTF8
    Write-Host "Private installer package manifest: $evidence"
}
