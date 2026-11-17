# Artifact identity and process-environment helpers. Dot-source this file;
# it never starts an application or extracts a bundled file to disk.
function Set-NoraOfflineBuildEnvironment {
    $previous = @{}
    foreach ($name in @('DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE')) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $(if ($name -eq 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE') { 'true' } else { '1' }), 'Process')
    }
    return $previous
}

function Restore-NoraBuildEnvironment($Previous) {
    foreach ($name in $Previous.Keys) { [Environment]::SetEnvironmentVariable($name, $Previous[$name], 'Process') }
}

function Get-NoraBuildIdentity([string]$ProjectRoot, [string]$AppVersion, [string]$FileVersion, [string]$Commit) {
    [xml]$projectXml = Get-Content -LiteralPath (Join-Path $ProjectRoot 'src\NoraVPN\nvp.csproj') -Raw
    if (-not $AppVersion) { $AppVersion = [string](@($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]) }
    if (-not $FileVersion) { $FileVersion = [string](@($projectXml.Project.PropertyGroup.FileVersion | Where-Object { $_ })[0]) }
    if (-not $Commit) {
        $Commit = (& git -C $ProjectRoot rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read the source commit.' }
    }
    if ($AppVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or $FileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'A semantic app version, four-part file version and full source commit are required.'
    }
    [PSCustomObject]@{ AppVersion = $AppVersion; FileVersion = $FileVersion; Commit = $Commit.ToLowerInvariant(); InformationalVersion = "$AppVersion+$($Commit.ToLowerInvariant())" }
}

function Get-NoraFileIdentity([string]$Path) {
    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolved)
    $sourceCommit = $null
    if ($version.ProductVersion -match '\+([0-9a-fA-F]{40})$') { $sourceCommit = $Matches[1].ToLowerInvariant() }
    [PSCustomObject]@{
        Path = $resolved; Bytes = (Get-Item -LiteralPath $resolved).Length
        Sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
        FileVersion = $version.FileVersion; InformationalVersion = $version.ProductVersion; SourceCommit = $sourceCommit
    }
}

function Assert-NoraFileVersion($Identity, $Expected) {
    if ($Identity.FileVersion -ne $Expected.FileVersion -or $Identity.InformationalVersion -ne $Expected.InformationalVersion) {
        throw "Artifact version/commit mismatch for $($Identity.Path): actual $($Identity.FileVersion) / $($Identity.InformationalVersion); expected $($Expected.FileVersion) / $($Expected.InformationalVersion)."
    }
}

function Assert-NoraPrivateProofDirectory([string]$ProofDirectory, [string]$ProjectRoot, [string]$PortableRoot) {
    if (-not $ProofDirectory) { throw 'Isolated verification requires an explicit private ProofDirectory.' }
    $proof = [IO.Path]::GetFullPath($ProofDirectory).TrimEnd('\', '/')
    foreach ($excluded in @($ProjectRoot, $PortableRoot)) {
        $boundary = [IO.Path]::GetFullPath($excluded).TrimEnd('\', '/')
        if ($proof.Equals($boundary, [StringComparison]::OrdinalIgnoreCase) -or $proof.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Verification evidence and synthetic state must stay outside public source and shipping payloads.'
        }
    }
    # Refuse junction/symlink ancestors rather than treating Resolve-Path as a
    # guarantee about their final filesystem target.
    $ancestor = [IO.DirectoryInfo]::new($proof)
    while ($null -ne $ancestor) {
        if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A private proof directory may not use a reparse-point ancestor.' }
        $ancestor = $ancestor.Parent
    }
    [IO.Directory]::CreateDirectory($proof) | Out-Null
    $resolved = (Resolve-Path -LiteralPath $proof).Path.TrimEnd('\', '/')
    foreach ($excluded in @($ProjectRoot, $PortableRoot)) {
        $boundary = [IO.Path]::GetFullPath($excluded).TrimEnd('\', '/')
        if ($resolved.Equals($boundary, [StringComparison]::OrdinalIgnoreCase) -or $resolved.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The resolved proof directory is not private.' }
    }
    return $resolved
}

function Get-NoraIcoFrames([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 22 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0 -or [BitConverter]::ToUInt16($bytes, 2) -ne 1) { throw "Invalid ICO: $Path" }
    $count = [BitConverter]::ToUInt16($bytes, 4)
    $sizes = @()
    for ($i = 0; $i -lt $count; $i++) {
        $entry = 6 + 16 * $i
        if ($entry + 16 -gt $bytes.Length) { throw "Truncated ICO directory: $Path" }
        $width = [int]$bytes[$entry]; if ($width -eq 0) { $width = 256 }
        $height = [int]$bytes[$entry + 1]; if ($height -eq 0) { $height = 256 }
        $length = [BitConverter]::ToUInt32($bytes, $entry + 8)
        $offset = [BitConverter]::ToUInt32($bytes, $entry + 12)
        if ($width -ne $height -or $length -eq 0 -or [long]$offset + $length -gt $bytes.Length) { throw "Invalid ICO frame: $Path" }
        $sizes += $width
    }
    [PSCustomObject]@{ Count = $count; Sizes = $sizes }
}

function Get-NoraStaticAssetEvidence([string]$ArtifactRoot, [string]$ProjectRoot) {
    $iconPath = Join-Path $ArtifactRoot 'assets\logo.ico'
    $iconHash = (Get-FileHash -LiteralPath $iconPath -Algorithm SHA256).Hash
    if ($iconHash -ne '4097349A024503524D06D932F4FCBDAE1593BE16643369B337655704262B7CBD') { throw 'The artifact does not contain the canonical NORA Windows ICO.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $ArtifactRoot 'assets') -Filter 'logo*.ico' -File | Where-Object Name -ne 'logo.ico').Count -gt 0) { throw 'Extra Windows brand ICO found.' }
    $frames = Get-NoraIcoFrames $iconPath
    foreach ($size in @(16, 24, 32, 48, 256)) { if ($size -notin $frames.Sizes) { throw "Canonical ICO frame $size is missing." } }
    $flags = @(Get-ChildItem -LiteralPath (Join-Path $ArtifactRoot 'assets\flags') -Filter '*.png' -File)
    $expectedFlagCount = @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'assets\flags') -Filter '*.png' -File).Count
    if ($flags.Count -lt 250 -or $flags.Count -ne $expectedFlagCount) { throw 'Country flag count differs from the canonical shipping set.' }
    foreach ($flag in @('aq', 'bm', 'va', 'eu')) {
        if ((Get-FileHash -LiteralPath (Join-Path $ArtifactRoot "assets\flags\$flag.png") -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $ProjectRoot "assets\flags\$flag.png") -Algorithm SHA256).Hash) { throw "Flag $flag differs from the canonical asset." }
    }
    $emojiCount = @(Get-ChildItem -LiteralPath (Join-Path $ArtifactRoot 'assets\emoji') -Filter '*.png' -File).Count
    if ($emojiCount -lt 3600) { throw 'The shipping emoji set is incomplete.' }
    [xml]$projectXml = Get-Content -LiteralPath (Join-Path $ProjectRoot 'src\NoraVPN\nvp.csproj') -Raw
    $include = @($projectXml.Project.ItemGroup.Content | Where-Object { $_.Include -like '*assets\locations\*' })
    if ($include.Count -ne 1 -or $include[0].Include.Contains('*')) { throw 'Location shipping assets must use one explicit allow-list.' }
    $entries = @($include[0].Include.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($entries.Count -ne 80 -or @($entries | Sort-Object -Unique).Count -ne 80) { throw 'The location allow-list must contain exactly 80 distinct shipping files.' }
    $actual = @(Get-ChildItem -LiteralPath (Join-Path $ArtifactRoot 'assets\locations') -File)
    if ($actual.Count -ne 80) { throw 'The artifact location folder must contain exactly the 80 approved files.' }
    $locations = foreach ($entry in $entries) {
        $name = [IO.Path]::GetFileName($entry)
        $source = [IO.Path]::GetFullPath((Join-Path (Join-Path $ProjectRoot 'src\NoraVPN') $entry))
        $canonicalLocationRoot = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'assets\locations')).TrimEnd('\') + '\'
        if (-not $source.StartsWith($canonicalLocationRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Location include escapes the canonical asset directory.' }
        $artifact = Join-Path $ArtifactRoot "assets\locations\$name"
        $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()) { throw "Shipping location asset differs: $name" }
        [PSCustomObject]@{ Name = $name; Bytes = (Get-Item -LiteralPath $artifact).Length; Sha256 = $hash }
    }
    [PSCustomObject]@{ IconSha256 = $iconHash.ToLowerInvariant(); IconFrames = $frames; FlagCount = $flags.Count; EmojiCount = $emojiCount; LocationCount = $locations.Count; Locations = $locations }
}

function Read-NoraBundleString([IO.BinaryReader]$Reader) {
    $length = 0; $shift = 0
    do {
        $part = $Reader.ReadByte()
        if ($shift -ge 28 -and $part -gt 7) { throw 'Invalid bundle string length.' }
        $length = $length -bor (($part -band 127) -shl $shift)
        $shift += 7
    } while (($part -band 128) -ne 0)
    if ($length -lt 0 -or $length -gt 4096) { throw 'Oversized bundle string.' }
    $bytes = $Reader.ReadBytes($length)
    if ($bytes.Length -ne $length) { throw 'Truncated bundle string.' }
    return [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
}

function Get-NoraBundledAssemblyEvidence([string]$Executable, [string]$AssemblyName = 'NoraVPN.dll') {
    # Official .NET 8 host format, pinned to bundle v6. Read only: no extraction or loading.
    # dotnet/runtime v8.0.0: HostWriter.cs, Bundle/Manifest.cs, FileEntry.cs, Bundler.cs.
    $signature = [byte[]]@(0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae)
    $file = [IO.File]::OpenRead($Executable)
    $reader = [IO.BinaryReader]::new($file, [Text.UTF8Encoding]::new($false, $true), $true)
    try {
        # The marker belongs to the native apphost, before the embedded files.
        $prefix = $reader.ReadBytes([int][Math]::Min(16MB, $file.Length))
        $marker = -1
        for ($i = 8; $i -le $prefix.Length - $signature.Length; $i++) {
            if ($prefix[$i] -ne $signature[0]) { continue }
            $signatureMatches = $true
            for ($j = 1; $j -lt $signature.Length; $j++) { if ($prefix[$i + $j] -ne $signature[$j]) { $signatureMatches = $false; break } }
            if ($signatureMatches) { $marker = $i; break }
        }
        if ($marker -lt 8) { throw 'The .NET single-file bundle marker was not found in the native apphost.' }
        $header = [BitConverter]::ToInt64($prefix, $marker - 8)
        if ($header -le $marker -or $header -ge $file.Length - 12) { throw 'Invalid bundle manifest offset.' }
        $file.Position = $header
        $major = $reader.ReadUInt32(); $minor = $reader.ReadUInt32(); $count = $reader.ReadInt32()
        if ($major -ne 6 -or $minor -ne 0 -or $count -le 0 -or $count -gt 10000) { throw 'Unsupported or malformed bundle manifest; expected official .NET bundle v6.0.' }
        $bundleId = Read-NoraBundleString $reader
        # v2+ deps/runtimeconfig offsets and sizes, then UInt64 flags.
        foreach ($unused in 1..5) { [void]$reader.ReadInt64() }
        $selected = @()
        for ($i = 0; $i -lt $count; $i++) {
            $offset = $reader.ReadInt64(); $size = $reader.ReadInt64(); $compressed = $reader.ReadInt64(); $type = $reader.ReadByte()
            $name = Read-NoraBundleString $reader
            $stored = if ($compressed -gt 0) { $compressed } else { $size }
            if ($offset -lt 0 -or $size -lt 0 -or $compressed -lt 0 -or $offset -gt $header -or $stored -gt $header - $offset) { throw 'Bundle entry points outside its embedded payload.' }
            if ($name -ceq $AssemblyName) { $selected += [PSCustomObject]@{ Offset = $offset; Size = $size; CompressedSize = $compressed; StoredSize = $stored; Type = $type } }
        }
        if ($selected.Count -ne 1 -or $selected[0].Type -ne 1 -or $selected[0].Size -le 0 -or $selected[0].Size -gt 256MB -or $selected[0].StoredSize -gt 256MB) { throw 'The bundle must contain exactly one bounded managed application assembly.' }
        $entry = $selected[0]
        $file.Position = $entry.Offset
        $data = $reader.ReadBytes([int]$entry.StoredSize)
        if ($data.Length -ne $entry.StoredSize) { throw 'Truncated bundled application assembly.' }
        $memory = [IO.MemoryStream]::new($data, $false)
        $payload = $memory
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            if ($entry.CompressedSize -gt 0) { $payload = [IO.Compression.DeflateStream]::new($memory, [IO.Compression.CompressionMode]::Decompress) }
            $buffer = [byte[]]::new(65536); $decoded = 0L
            while (($read = $payload.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $decoded += $read
                if ($decoded -gt $entry.Size) { throw 'Bundled application exceeds its declared decoded size.' }
                [void]$sha.TransformBlock($buffer, 0, $read, $buffer, 0)
            }
            if ($decoded -ne $entry.Size) { throw 'Bundled application decoded size mismatch.' }
            [void]$sha.TransformFinalBlock([byte[]]::new(0), 0, 0)
            $hash = [BitConverter]::ToString($sha.Hash).Replace('-', '').ToLowerInvariant()
        }
        finally { if ($payload -ne $memory) { $payload.Dispose() }; $memory.Dispose(); $sha.Dispose() }
        [PSCustomObject]@{ BundleVersion = "$major.$minor"; BundleId = $bundleId; HeaderOffset = $header; Entry = $AssemblyName; Offset = $entry.Offset; Bytes = $entry.Size; CompressedBytes = $entry.CompressedSize; Sha256 = $hash; ExtractedOrExecuted = $false }
    }
    finally { $reader.Dispose(); $file.Dispose() }
}

function Get-NoraApprovedCoreEvidence([string]$ArtifactRoot, [string]$ApprovedCoreBundle, [string]$ApprovedSha256) {
    if (-not $ApprovedCoreBundle -or $ApprovedSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Isolated verification requires an existing approved Portable ZIP and its approved SHA256.' }
    $zipIdentity = Get-NoraFileIdentity $ApprovedCoreBundle
    if ($zipIdentity.Sha256 -ne $ApprovedSha256.ToLowerInvariant()) { throw 'The approved core ZIP digest does not match the authorized baseline.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($zipIdentity.Path)
    try {
        $baseline = @{}
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($entry.Name.Length -eq 0) { continue }
            if ($name -match '^(?:[^/]+/)?(cores/.+|wintun\.dll)$') {
                $relative = $Matches[1]
                if ($baseline.ContainsKey($relative)) { throw 'Duplicate core entry in approved ZIP.' }
                $baseline[$relative] = $entry
            }
        }
        $files = @(Get-ChildItem -LiteralPath (Join-Path $ArtifactRoot 'cores') -File -Recurse) + @(Get-Item -LiteralPath (Join-Path $ArtifactRoot 'wintun.dll'))
        if ($files.Count -ne $baseline.Count -or $baseline.Count -lt 6) { throw 'Staged core files differ from the approved ZIP file set.' }
        $rows = foreach ($item in $files) {
            $relative = $item.FullName.Substring($ArtifactRoot.TrimEnd('\', '/').Length + 1).Replace('\', '/')
            if (-not $baseline.ContainsKey($relative)) { throw "Unapproved staged core file: $relative" }
            $stream = $baseline[$relative].Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $expected = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $stream.Dispose(); $sha.Dispose() }
            $actual = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $expected -or $item.Length -ne $baseline[$relative].Length) { throw "Staged core differs from the approved release: $relative" }
            [PSCustomObject]@{ RelativePath = $relative; Bytes = $item.Length; Sha256 = $actual; BaselineEntry = $baseline[$relative].FullName }
        }
        [PSCustomObject]@{ ApprovedZip = $zipIdentity.Path; ApprovedZipSha256 = $zipIdentity.Sha256; Files = $rows; ProcessesInvoked = @() }
    }
    finally { $zip.Dispose() }
}
