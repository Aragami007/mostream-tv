param(
    [string]$Version = "0.6.2"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$stagingRoot = Join-Path $artifactsRoot "staging\MostreamTV.Host"
$installerStaging = Join-Path $artifactsRoot "staging\Installer"
$syncplayStaging = Join-Path $artifactsRoot "staging\SyncplayHeadless"
$libsyncStaging = Join-Path $artifactsRoot "staging\LibsyncUnpacked"
$archivePath = Join-Path $artifactsRoot "MostreamTV.Host-$Version-windows-x64.zip"
$installerOutPath = Join-Path $artifactsRoot "MostreamTV-Setup-$Version-windows-x64.exe"
$vendorArchive = Join-Path $repositoryRoot "vendor\Syncplay_1.7.6_Portable.zip"

if (-not (Test-Path -LiteralPath $vendorArchive)) {
    throw "Missing required Syncplay archive: $vendorArchive"
}

function New-HeadlessSyncplayArchive {
    param(
        [string]$SourceDir,
        [string]$LibsyncDir,
        [string]$DestinationZip,
        [System.IO.Compression.CompressionLevel]$Compression
    )

    $libZip = Join-Path $SourceDir "lib\libsync.zip"
    if (Test-Path -LiteralPath $libZip) {
        Remove-Item -LiteralPath $libZip -Force
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($LibsyncDir, $libZip, $Compression, $false)

    if (Test-Path -LiteralPath $DestinationZip) {
        Remove-Item -LiteralPath $DestinationZip -Force
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($SourceDir, $DestinationZip, $Compression, $false)
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
foreach ($dir in @($stagingRoot, $installerStaging, $syncplayStaging, $libsyncStaging)) {
    if (Test-Path -LiteralPath $dir) {
        $resolved = [IO.Path]::GetFullPath($dir)
        if (-not $resolved.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clear an unsafe staging path."
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

# Build stripped headless Syncplay 1.7.6 runtime (Mostream TV runs syncplayServer.exe and SyncplayConsole.exe --no-gui).
[IO.Compression.ZipFile]::ExtractToDirectory($vendorArchive, $syncplayStaging)
foreach ($relPath in @(
    "Syncplay.exe",
    "platforms",
    "styles",
    "lib\sqlite3.dll",
    "lib\_sqlite3.pyd"
)) {
    $full = Join-Path $syncplayStaging $relPath
    if (Test-Path -LiteralPath $full) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}
foreach ($pattern in @("PySide2*", "Qt5*", "shiboken2*", "pyside2*")) {
    Get-ChildItem -Path (Join-Path $syncplayStaging "lib") -Filter $pattern -ErrorAction SilentlyContinue | Remove-Item -Force
}
Get-ChildItem -Path (Join-Path $syncplayStaging "resources") -Include "*.png", "*.mng", "*.ico" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

$originalLibZip = Join-Path $syncplayStaging "lib\libsync.zip"
[IO.Compression.ZipFile]::ExtractToDirectory($originalLibZip, $libsyncStaging)
foreach ($drop in @(
    "PySide2",
    "shiboken2",
    "setuptools",
    "wheel",
    "distutils",
    "lib2to3",
    "test",
    "curses",
    "_distutils_hack",
    "sqlite3",
    "syncplay\ui\gui.pyc",
    "syncplay\ui\GuiConfiguration.pyc",
    "syncplay\vendor\Qt.pyc",
    "syncplay\vendor\qt5reactor.pyc",
    "syncplay\vendor\darkdetect"
)) {
    $dropPath = Join-Path $libsyncStaging $drop
    if (Test-Path -LiteralPath $dropPath) {
        Remove-Item -LiteralPath $dropPath -Recurse -Force
    }
}
Get-ChildItem -Path $libsyncStaging -Directory -Filter "*.dist-info" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

$headlessCompressedZip = Join-Path $artifactsRoot "staging\Syncplay_1.7.6_Headless.zip"
$headlessStoreZip = Join-Path $artifactsRoot "staging\Syncplay_1.7.6_Headless_Store.zip"
New-HeadlessSyncplayArchive -SourceDir $syncplayStaging -LibsyncDir $libsyncStaging -DestinationZip $headlessCompressedZip -Compression ([IO.Compression.CompressionLevel]::SmallestSize)
New-HeadlessSyncplayArchive -SourceDir $syncplayStaging -LibsyncDir $libsyncStaging -DestinationZip $headlessStoreZip -Compression ([IO.Compression.CompressionLevel]::NoCompression)

# Publish trimmed, WPF-free self-contained win-x64 build so new users do not need .NET 8 installed separately.
dotnet publish (Join-Path $repositoryRoot "MostreamTV.Host\MostreamTV.Host.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:ExcludeSyncplayVendorZip=true `
    --output $stagingRoot

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (MostreamTV.Host) failed with exit code $LASTEXITCODE"
}

Get-ChildItem -Path $stagingRoot -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

$runtimeDirectory = Join-Path $stagingRoot "runtime"
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
$stagedSyncplayZip = Join-Path $runtimeDirectory (Split-Path $vendorArchive -Leaf)
Copy-Item -LiteralPath $headlessCompressedZip -Destination $stagedSyncplayZip -Force

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
[IO.Compression.ZipFile]::CreateFromDirectory($stagingRoot, $archivePath, [IO.Compression.CompressionLevel]::SmallestSize, $false)

# Build lightweight .NET Framework 4.8 installer stub (built into Windows 10/11) and append LZX-compressed payload.
dotnet publish (Join-Path $repositoryRoot "MostreamTV.Installer\MostreamTV.Installer.csproj") `
    --configuration Release `
    -p:Version=$Version `
    --output $installerStaging

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (MostreamTV.Installer) failed with exit code $LASTEXITCODE"
}

$publishedInstaller = Join-Path $installerStaging "MostreamTV.Installer.exe"
if (-not (Test-Path -LiteralPath $publishedInstaller)) {
    throw "Missing published installer executable: $publishedInstaller"
}

# Re-stage uncompressed Syncplay inside uncompressed payload.zip so outer LZX CAB compresses across all binaries solidly.
Copy-Item -LiteralPath $headlessStoreZip -Destination $stagedSyncplayZip -Force
$storePayloadZip = Join-Path $installerStaging "payload.zip"
if (Test-Path -LiteralPath $storePayloadZip) {
    Remove-Item -LiteralPath $storePayloadZip -Force
}
[IO.Compression.ZipFile]::CreateFromDirectory($stagingRoot, $storePayloadZip, [IO.Compression.CompressionLevel]::NoCompression, $false)

$makecabExe = Get-Command "makecab.exe" -ErrorAction SilentlyContinue
if ($makecabExe) {
    $cabDdfPath = Join-Path $installerStaging "payload.ddf"
    $cabOutPath = Join-Path $installerStaging "payload.cab"
    @"
.OPTION EXPLICIT
.Set InfFileName=NUL
.Set RptFileName=NUL
.Set CabinetNameTemplate=payload.cab
.Set DiskDirectory1=$installerStaging
.Set Cabinet=on
.Set Compress=on
.Set CompressionType=LZX
.Set CompressionMemory=21
.Set MaxDiskSize=0
.Set FolderSizeThreshold=200000000
"$storePayloadZip" "payload"
"@ | Set-Content -LiteralPath $cabDdfPath -Encoding ASCII

    & $makecabExe.Source /F $cabDdfPath | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $cabOutPath)) {
        throw "makecab.exe failed with exit code $LASTEXITCODE"
    }

    $payloadPath = $cabOutPath
    $payloadMagic = "MSTRCAB1"
}
else {
    $optZipPath = Join-Path $installerStaging "payload-opt.zip"
    if (Test-Path -LiteralPath $optZipPath) {
        Remove-Item -LiteralPath $optZipPath -Force
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($stagingRoot, $optZipPath, [IO.Compression.CompressionLevel]::SmallestSize, $false)
    $payloadPath = $optZipPath
    $payloadMagic = "MSTRZIP1"
}

Copy-Item -LiteralPath $publishedInstaller -Destination $installerOutPath -Force
$payloadLength = (Get-Item -LiteralPath $payloadPath).Length
$outStream = [IO.File]::Open($installerOutPath, [IO.FileMode]::Append, [IO.FileAccess]::Write)
try {
    $inStream = [IO.File]::OpenRead($payloadPath)
    try {
        $inStream.CopyTo($outStream)
    }
    finally {
        $inStream.Dispose()
    }
    $lenBytes = [BitConverter]::GetBytes([Int64]$payloadLength)
    $magicBytes = [Text.Encoding]::ASCII.GetBytes($payloadMagic)
    $outStream.Write($lenBytes, 0, $lenBytes.Length)
    $outStream.Write($magicBytes, 0, $magicBytes.Length)
}
finally {
    $outStream.Dispose()
}

# Restore compressed Syncplay zip inside stagingRoot for inspection consistency and tidy large installer intermediates.
Copy-Item -LiteralPath $headlessCompressedZip -Destination $stagedSyncplayZip -Force
foreach ($tempArtifact in @($storePayloadZip, (Join-Path $installerStaging "payload.cab"), (Join-Path $installerStaging "payload.ddf"), (Join-Path $installerStaging "payload-opt.zip"), $headlessCompressedZip, $headlessStoreZip)) {
    if (Test-Path -LiteralPath $tempArtifact) {
        Remove-Item -LiteralPath $tempArtifact -Force
    }
}

$zipInfo = Get-Item -LiteralPath $archivePath
$exeInfo = Get-Item -LiteralPath $installerOutPath
$zipHash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
$exeHash = Get-FileHash -LiteralPath $installerOutPath -Algorithm SHA256
Write-Host ("Created {0} ({1:N2} MB)" -f $archivePath, ($zipInfo.Length / 1MB))
Write-Host "SHA256 $($zipHash.Hash)"
Write-Host ("Created {0} ({1:N2} MB)" -f $installerOutPath, ($exeInfo.Length / 1MB))
Write-Host "SHA256 $($exeHash.Hash)"
