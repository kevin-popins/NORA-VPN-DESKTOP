param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\dist\NORA VPN Portable"),
    [string]$ZipPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$target = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
if (Test-Path -LiteralPath $target) {
    throw "Refusing to overwrite an existing portable folder: $target"
}

$project = Join-Path $projectRoot "src\NoraVPN\nvp.csproj"
dotnet restore $project
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet publish $project -c Release -f net8.0-windows -r win-x64 --self-contained true --no-restore -o $target
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& (Join-Path $PSScriptRoot "test-portable.ps1") -PortableDirectory $target
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($ZipPath) {
    $archive = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $ZipPath))
    if (Test-Path -LiteralPath $archive) {
        throw "Refusing to overwrite an existing archive: $archive"
    }
    Compress-Archive -LiteralPath $target -DestinationPath $archive -CompressionLevel Optimal
    Write-Host "Created $archive"
}
