param(
    [Parameter(Mandatory = $true)]
    [string]$PortableDirectory,
    [ValidateSet('Full', 'Isolated')]
    [string]$VerificationMode = 'Full',
    [string]$ManagedTestDll = '',
    [string]$ProofDirectory = '',
    [string]$ExpectedAppVersion = '',
    [string]$ExpectedFileVersion = '',
    [string]$ExpectedCommit = '',
    [string]$ApprovedCoreBundle = '',
    [string]$ApprovedCoreBundleSha256 = '758fc43fbf02efb43706ca1e0f0beee3026e1c3c97a8869a6e5a141f826c7c96'
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'portable-verification.ps1')
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
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

# Static checks apply to both modes. No payload executable has run here.
$expected = Get-NoraBuildIdentity $projectRoot $ExpectedAppVersion $ExpectedFileVersion $ExpectedCommit
$stagedExecutable = Get-NoraFileIdentity (Join-Path $root 'NoraVPN.exe')
Assert-NoraFileVersion $stagedExecutable $expected
$assetEvidence = Get-NoraStaticAssetEvidence $root $projectRoot

if ($VerificationMode -eq 'Isolated') {
    # Deliberately return before the legacy full checks below (core version,
    # elevated EXE and full-shell ui-icon commands are forbidden in this mode).
    $proofRoot = Assert-NoraPrivateProofDirectory $ProofDirectory $projectRoot $root
    $run = Join-Path $proofRoot ('portable-verification-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($run) | Out-Null
    $manifestPath = Join-Path $run 'verification.json'
    $commands = @('import-input-selftest', 'deep-link-selftest', 'subscription-notice-selftest',
        'subscription-reliability-selftest', 'subscription-refresh-selftest', 'subscription-metadata-selftest',
        'protected-display-selftest', 'purchase-link-selftest', 'display-region-selftest',
        'subscription-order-selftest', 'country-flag-selftest', 'location-artwork-selftest')
    $results = [Collections.Generic.List[object]]::new()
    $manifest = [ordered]@{
        Schema = 'nora-isolated-portable-verification-v1'; Status = 'Running'; StartedUtc = [DateTime]::UtcNow.ToString('O')
        ProofDirectory = $run; PortableDirectory = $root; Expected = $expected; StagedExecutable = $stagedExecutable
        Assets = $assetEvidence; Commands = $results; Failure = $null
        Settings = [ordered]@{ VerificationMode = 'Isolated'; ExecutableLaunched = $false; CoreLaunched = $false; UacUsed = $false; InstalledAppAccessed = $false; ProductionShellCreated = $false; NetworkTestsEnabled = $false; ValidateCoreSyntax = $false; RuntimeMotionExecuted = $false; TestsSerialized = $true }
        VerificationScripts = @('test-portable.ps1', 'portable-verification.ps1') | ForEach-Object { Get-NoraFileIdentity (Join-Path $PSScriptRoot $_) }
    }
    $previousEnvironment = @{}
    try {
        if (-not $ManagedTestDll -or -not (Test-Path -LiteralPath $ManagedTestDll -PathType Leaf)) { throw 'Isolated verification requires an explicitly supplied matching ManagedTestDll; no fallback is allowed.' }
        $managed = Get-NoraFileIdentity $ManagedTestDll
        if ([IO.Path]::GetFileName($managed.Path) -cne 'NoraVPN.dll') { throw 'The managed test entrypoint must be NoraVPN.dll.' }
        Assert-NoraFileVersion $managed $expected
        $managedDirectory = Split-Path -Parent $managed.Path
        foreach ($name in @('NoraVPN.runtimeconfig.json', 'NoraVPN.deps.json')) {
            if (-not (Test-Path -LiteralPath (Join-Path $managedDirectory $name) -PathType Leaf)) { throw "Missing managed runtime companion: $name" }
        }
        $manifest.ManagedTestAssembly = $managed
        $manifest.ManagedRuntimeFiles = @('NoraVPN.runtimeconfig.json', 'NoraVPN.deps.json') | ForEach-Object { Get-NoraFileIdentity (Join-Path $managedDirectory $_) }
        $manifest.ManagedAssets = Get-NoraStaticAssetEvidence $managedDirectory $projectRoot
        $manifest.Bundle = Get-NoraBundledAssemblyEvidence $stagedExecutable.Path
        if ($manifest.Bundle.Sha256 -ne $managed.Sha256 -or $manifest.Bundle.Bytes -ne $managed.Bytes) { throw 'The staged single-file embedded DLL is not byte-identical to the managed DLL selected for tests.' }
        $manifest.ApprovedCores = Get-NoraApprovedCoreEvidence $root $ApprovedCoreBundle $ApprovedCoreBundleSha256
        $manifest.WorkingTreeDirty = [bool](& git -C $projectRoot status --porcelain)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot record source working-tree status.' }
        $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $manifest.DotnetHost = Get-NoraFileIdentity $dotnet
        $environment = @{
            NORA_DATA_ROOT = (Join-Path $run 'synthetic-state'); NORA_SELFTEST_ROOT = (Join-Path $run 'temporary-fixtures')
            TEMP = (Join-Path $run 'temporary-fixtures'); TMP = (Join-Path $run 'temporary-fixtures')
            DOTNET_CLI_HOME = (Join-Path $run 'dotnet-home'); DOTNET_CLI_TELEMETRY_OPTOUT = '1'
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'; DOTNET_NOLOGO = '1'; DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
            DOTNET_STARTUP_HOOKS = $null; DOTNET_ADDITIONAL_DEPS = $null; CORECLR_ENABLE_PROFILING = '0'; COR_ENABLE_PROFILING = '0'
        }
        foreach ($directory in @($environment.NORA_DATA_ROOT, $environment.NORA_SELFTEST_ROOT, $environment.DOTNET_CLI_HOME)) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
        foreach ($name in $environment.Keys) {
            $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
        }
        $manifest.Environment = $environment
        Push-Location -LiteralPath $root
        try {
            foreach ($command in $commands) {
                $log = Join-Path $run ($command + '.log')
                $started = [DateTime]::UtcNow
                & $dotnet $managed.Path $command *> $log
                $exitCode = $LASTEXITCODE
                $results.Add([PSCustomObject]@{ Command = $command; ExitCode = $exitCode; StartedUtc = $started.ToString('O'); DurationMs = [Math]::Round(([DateTime]::UtcNow - $started).TotalMilliseconds); Log = $log; LogSha256 = (Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash.ToLowerInvariant() })
                if ($exitCode -ne 0) { throw "Isolated $command failed with exit code $exitCode. See private proof log." }
                Write-Host "ISOLATED PASS: $command"
            }
        }
        finally { Pop-Location }
        if ((Get-FileHash -LiteralPath $managed.Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $managed.Sha256 -or
            (Get-FileHash -LiteralPath $stagedExecutable.Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $stagedExecutable.Sha256) { throw 'An application artifact changed while verification was running.' }
        $manifest.Status = 'Passed'
    }
    catch { $manifest.Status = 'Failed'; $manifest.Failure = $_.Exception.Message; throw }
    finally {
        foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
        $manifest.CompletedUtc = [DateTime]::UtcNow.ToString('O')
        $manifest | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        Write-Host "Private portable verification manifest: $manifestPath"
    }
    Write-Host 'ISOLATED PORTABLE PASS: static shipping identity, embedded/tested DLL equality, approved core hashes, and safe managed commands.'
    [PSCustomObject]@{ VerificationMode = 'Isolated'; Status = $manifest.Status; ManifestPath = $manifestPath }
    return
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
