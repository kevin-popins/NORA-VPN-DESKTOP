param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,
    [Parameter(Mandatory = $true)]
    [string]$EuropeFlag
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$target = Join-Path $projectRoot "assets\flags"
$source = Resolve-Path $SourceDirectory

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source "*.png") -Destination $target -Force
Copy-Item -LiteralPath $EuropeFlag -Destination (Join-Path $target "eu.png") -Force

$count = (Get-ChildItem -LiteralPath $target -Filter *.png).Count
if ($count -lt 250 -or -not (Test-Path (Join-Path $target "eu.png"))) {
    throw "Flag import incomplete: $count files; EU flag is required."
}

Write-Host "COUNTRY FLAG IMPORT PASS: $count local PNG flags available."
