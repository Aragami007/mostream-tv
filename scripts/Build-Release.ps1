param(
    [string]$Version = "0.6.0"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$stagingRoot = Join-Path $artifactsRoot "staging\MostreamTV.Host"
$installerStaging = Join-Path $artifactsRoot "staging\Installer"
$archivePath = Join-Path $artifactsRoot "MostreamTV.Host-$Version-windows-x64.zip"
$installerOutPath = Join-Path $artifactsRoot "MostreamTV-Setup-$Version-windows-x64.exe"
$vendorArchive = Join-Path $repositoryRoot "vendor\Syncplay_1.7.6_Portable.zip"

if (-not (Test-Path -LiteralPath $vendorArchive)) {
    throw "Missing required Syncplay archive: $vendorArchive"
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
foreach ($dir in @($stagingRoot, $installerStaging)) {
    if (Test-Path -LiteralPath $dir) {
        $resolved = [IO.Path]::GetFullPath($dir)
        if (-not $resolved.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clear an unsafe staging path."
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

# Publish self-contained win-x64 build so new users do not need .NET 8 installed separately.
dotnet publish (Join-Path $repositoryRoot "MostreamTV.Host\MostreamTV.Host.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:Version=$Version `
    --output $stagingRoot

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (MostreamTV.Host) failed with exit code $LASTEXITCODE"
}

Get-ChildItem -Path $stagingRoot -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

$runtimeDirectory = Join-Path $stagingRoot "runtime"
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
Copy-Item -LiteralPath $vendorArchive -Destination (Join-Path $runtimeDirectory (Split-Path $vendorArchive -Leaf)) -Force

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $archivePath -CompressionLevel Optimal

# Package single-file Windows installer wrapping the self-contained payload ZIP.
dotnet publish (Join-Path $repositoryRoot "MostreamTV.Installer\MostreamTV.Installer.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    "-p:PayloadZipPath=$archivePath" `
    --output $installerStaging

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (MostreamTV.Installer) failed with exit code $LASTEXITCODE"
}

$publishedInstaller = Join-Path $installerStaging "MostreamTV.Installer.exe"
if (-not (Test-Path -LiteralPath $publishedInstaller)) {
    throw "Missing published installer executable: $publishedInstaller"
}
Copy-Item -LiteralPath $publishedInstaller -Destination $installerOutPath -Force

$zipHash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
$exeHash = Get-FileHash -LiteralPath $installerOutPath -Algorithm SHA256
Write-Host "Created $archivePath"
Write-Host "SHA256 $($zipHash.Hash)"
Write-Host "Created $installerOutPath"
Write-Host "SHA256 $($exeHash.Hash)"
