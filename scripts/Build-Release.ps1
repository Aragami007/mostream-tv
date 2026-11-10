param(
    [string]$Version = "0.5.1"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$stagingRoot = Join-Path $artifactsRoot "staging\MoStreamTV.Host"
$archivePath = Join-Path $artifactsRoot "MoStreamTV.Host-$Version-windows.zip"
$vendorArchive = Join-Path $repositoryRoot "vendor\Syncplay_1.7.6_Portable.zip"

if (-not (Test-Path -LiteralPath $vendorArchive)) {
    throw "Missing required Syncplay archive: $vendorArchive"
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
if (Test-Path -LiteralPath $stagingRoot) {
    $resolvedStaging = [IO.Path]::GetFullPath($stagingRoot)
    if (-not $resolvedStaging.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear an unsafe staging path."
    }
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}

dotnet publish (Join-Path $repositoryRoot "MoStreamTV.Host\MoStreamTV.Host.csproj") `
    --configuration Release `
    --output $stagingRoot

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$runtimeDirectory = Join-Path $stagingRoot "runtime"
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
Copy-Item -LiteralPath $vendorArchive -Destination (Join-Path $runtimeDirectory (Split-Path $vendorArchive -Leaf))

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $archivePath -CompressionLevel Optimal

$hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
Write-Host "Created $archivePath"
Write-Host "SHA256 $($hash.Hash)"
