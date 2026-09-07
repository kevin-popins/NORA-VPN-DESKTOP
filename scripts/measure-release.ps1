param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,
    [Parameter(Mandatory = $true)]
    [string]$ReportPath
)

# Read-only ZIP audit. Write the report outside shipping assets (for example artifacts/).
# expanded_payload is the sum of uncompressed file lengths, not installed disk blocks,
# installer/uninstaller overhead, or the extracted single-file runtime cache.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath = (Resolve-Path -LiteralPath $ZipPath).Path
$reportFile = [System.IO.Path]::GetFullPath($ReportPath)
if ([string]::Equals($archivePath, $reportFile, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The report must not overwrite the source ZIP."
}

function Get-ContentGroup([string]$name) {
    if ($name -match '(^|/)assets/locations/') { return "backgrounds" }
    if ($name -match '(^|/)assets/') { return "other_assets" }
    if ($name -match '(^|/)cores/') { return "cores" }
    if ($name -match '\.(exe|dll|deps\.json|runtimeconfig\.json)$') { return "app_runtime" }
    return "other"
}

$groups = [ordered]@{}
foreach ($groupName in @("backgrounds", "other_assets", "app_runtime", "cores", "other")) {
    $groups[$groupName] = [ordered]@{ entries = 0; compressed_bytes = [long]0; expanded_payload_bytes = [long]0 }
}
$files = [System.Collections.Generic.List[object]]::new()
$hashGroups = @{}
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
$hasher = [System.Security.Cryptography.SHA256]::Create()
$directoryEntries = 0
try {
    foreach ($entry in ($archive.Entries | Sort-Object FullName)) {
        $entryName = $entry.FullName.Replace('\', '/')
        if ($entryName.EndsWith('/')) { $directoryEntries++; continue }
        $group = Get-ContentGroup $entryName
        $groups[$group].entries++
        $groups[$group].compressed_bytes += $entry.CompressedLength
        $groups[$group].expanded_payload_bytes += $entry.Length
        $stream = $entry.Open()
        try {
            # ComputeHash reads the entry stream incrementally; no full-entry buffers.
            $sha256 = [System.BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
        }
        finally { $stream.Dispose() }
        $row = [pscustomobject][ordered]@{ name = $entryName; group = $group; compressed_bytes = $entry.CompressedLength;
            expanded_payload_bytes = $entry.Length; sha256 = $sha256 }
        $files.Add($row)
        if (-not $hashGroups.ContainsKey($sha256)) {
            $hashGroups[$sha256] = [System.Collections.Generic.List[object]]::new()
        }
        $hashGroups[$sha256].Add($row)
    }
}
finally { $hasher.Dispose(); $archive.Dispose() }

$duplicates = @(
    foreach ($hash in ($hashGroups.Keys | Sort-Object)) {
        $matches = $hashGroups[$hash]
        if ($matches.Count -lt 2) { continue }
        $compressed = [long]($matches | Measure-Object compressed_bytes -Sum).Sum
        $smallestCompressed = [long]($matches | Measure-Object compressed_bytes -Minimum).Minimum
        [pscustomobject][ordered]@{ sha256 = $hash; copies = $matches.Count; names = @($matches | ForEach-Object { $_.name });
            bytes_per_copy = $matches[0].expanded_payload_bytes;
            redundant_expanded_payload_bytes = $matches[0].expanded_payload_bytes * ($matches.Count - 1);
            redundant_compressed_bytes = $compressed - $smallestCompressed }
    }
)
$compressedTotal = [long]($files | Measure-Object compressed_bytes -Sum).Sum
$expandedTotal = [long]($files | Measure-Object expanded_payload_bytes -Sum).Sum
$downloadBytes = (Get-Item -LiteralPath $archivePath).Length
$report = [ordered]@{
    schema_version = 1
    archive_name = [System.IO.Path]::GetFileName($archivePath)
    archive_sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    download_bytes = $downloadBytes
    compressed_entries_bytes = $compressedTotal
    zip_structure_bytes = $downloadBytes - $compressedTotal
    expanded_payload_bytes = $expandedTotal
    file_entries = $files.Count
    directory_entries = $directoryEntries
    groups = $groups
    duplicate_content_groups = $duplicates
    redundant_expanded_payload_bytes = [long]($duplicates | Measure-Object redundant_expanded_payload_bytes -Sum).Sum
    redundant_compressed_bytes = [long]($duplicates | Measure-Object redundant_compressed_bytes -Sum).Sum
    entries = @($files.ToArray())
}
$reportDirectory = [System.IO.Path]::GetDirectoryName($reportFile)
[System.IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
[System.IO.File]::WriteAllText($reportFile, (($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))
Write-Host "Release audit: $($files.Count) files; download $downloadBytes bytes; expanded payload $expandedTotal bytes."
$groups | ConvertTo-Json -Depth 3
