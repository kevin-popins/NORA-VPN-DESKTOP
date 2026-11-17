param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\dist\NORA VPN Portable"),
    [string]$ZipPath = "",
    [string]$AppVersion = '',
    [string]$FileVersion = '',
    [switch]$NoRestore,
    [ValidateSet('Full', 'Isolated')]
    [string]$VerificationMode = 'Full',
    [string]$ManagedTestDll = '',
    [string]$ProofDirectory = '',
    [string]$ApprovedCoreBundle = '',
    [string]$ApprovedCoreBundleSha256 = '758fc43fbf02efb43706ca1e0f0beee3026e1c3c97a8869a6e5a141f826c7c96'
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot 'portable-verification.ps1')
$identity = Get-NoraBuildIdentity $projectRoot $AppVersion $FileVersion ''
$target = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $projectRoot $OutputDirectory }))
if (Test-Path -LiteralPath $target) {
    throw "Refusing to overwrite an existing portable folder: $target"
}
if ($ZipPath) {
    $archive = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($ZipPath)) { $ZipPath } else { Join-Path $projectRoot $ZipPath }))
    if (Test-Path -LiteralPath $archive) { throw "Refusing to overwrite an existing archive: $archive" }
    if ($archive.StartsWith($target.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The Portable archive must be outside the payload folder.' }
}

$project = Join-Path $projectRoot "src\NoraVPN\nvp.csproj"
if ($VerificationMode -eq 'Isolated') {
    # Isolated packaging uses existing restored dependencies; it never restores
    # or fetches a core/dependency, and verification cannot fall back to an EXE.
    $privateProof = Assert-NoraPrivateProofDirectory $ProofDirectory $projectRoot $target
    if (-not $ManagedTestDll -or -not (Test-Path -LiteralPath $ManagedTestDll -PathType Leaf)) { throw 'Isolated packaging requires ManagedTestDll.' }
    Assert-NoraFileVersion (Get-NoraFileIdentity $ManagedTestDll) $identity
    if (-not $ApprovedCoreBundle -or -not (Test-Path -LiteralPath $ApprovedCoreBundle -PathType Leaf)) { throw 'Isolated packaging requires an existing approved core ZIP.' }
    if ((Get-FileHash -LiteralPath $ApprovedCoreBundle -Algorithm SHA256).Hash -ne $ApprovedCoreBundleSha256) { throw 'Approved core bundle digest mismatch.' }
}
if (-not $NoRestore -and $VerificationMode -eq 'Full') {
    dotnet restore $project
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$offlineEnvironment = @{}
if ($VerificationMode -eq 'Isolated') { $offlineEnvironment = Set-NoraOfflineBuildEnvironment }
try {
    dotnet publish $project -c Release -f net8.0-windows -r win-x64 --self-contained true --no-restore -o $target "-p:Version=$($identity.AppVersion)" "-p:FileVersion=$($identity.FileVersion)" "-p:InformationalVersion=$($identity.InformationalVersion)" "-p:SourceRevisionId=$($identity.Commit)" '-p:IncludeSourceRevisionInInformationalVersion=false'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally { Restore-NoraBuildEnvironment $offlineEnvironment }

$verification = & (Join-Path $PSScriptRoot "test-portable.ps1") -PortableDirectory $target -VerificationMode $VerificationMode -ManagedTestDll $ManagedTestDll -ProofDirectory $ProofDirectory -ExpectedAppVersion $identity.AppVersion -ExpectedFileVersion $identity.FileVersion -ExpectedCommit $identity.Commit -ApprovedCoreBundle $ApprovedCoreBundle -ApprovedCoreBundleSha256 $ApprovedCoreBundleSha256
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($ZipPath) {
    Compress-Archive -LiteralPath $target -DestinationPath $archive -CompressionLevel Optimal
    Write-Host "Created $archive"
}

if ($VerificationMode -eq 'Isolated') {
    $manifest = [ordered]@{
        Schema = 'nora-portable-package-evidence-v1'; CreatedUtc = [DateTime]::UtcNow.ToString('O')
        Build = $identity; VerificationMode = $VerificationMode; RestoreInvoked = $false
        Publish = [ordered]@{ Configuration = 'Release'; Framework = 'net8.0-windows'; Runtime = 'win-x64'; SelfContained = $true; NoRestore = $true; InformationalVersionIncludesCommit = $true; WorkloadAdvertisingDisabled = $true }
        PortableDirectory = $target; Executable = Get-NoraFileIdentity (Join-Path $target 'NoraVPN.exe')
        VerificationManifest = $verification.ManifestPath; Archive = $null
    }
    if ($ZipPath) { $manifest.Archive = Get-NoraFileIdentity $archive }
    $evidence = Join-Path (Split-Path -Parent $verification.ManifestPath) 'portable-package.json'
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidence -Encoding UTF8
    Write-Host "Private portable package manifest: $evidence"
}
