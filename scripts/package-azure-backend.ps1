param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = ".artifacts/azure-backend",
    [string]$MobileApkPath,
    [string]$PackageName
)

$ErrorActionPreference = 'Stop'

function Resolve-WorkspacePath {
    param([string]$PathValue)

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

function Find-LatestMobileApk {
    param([string]$RepoRoot)

    $searchRoots = @(
        (Join-Path $RepoRoot "apps/mobile-app/bin/Release"),
        (Join-Path $RepoRoot "apps/mobile-app/bin/Debug")
    )

    $candidates = @()
    foreach ($root in $searchRoots) {
        if (Test-Path -LiteralPath $root) {
            $candidates += Get-ChildItem -LiteralPath $root -Recurse -Filter "*.apk" -File |
                Where-Object {
                    $_.FullName -like "*net*-android*" -and
                    $_.Name -notlike "*.idsig"
                }
        }
    }

    return $candidates |
        Sort-Object `
            @{ Expression = { $_.FullName -like "*\Release\*" }; Descending = $true }, `
            @{ Expression = { $_.Name -like "*Signed.apk" }; Descending = $true }, `
            @{ Expression = { $_.FullName -like "*\publish\*" }; Descending = $true }, `
            @{ Expression = { $_.LastWriteTimeUtc }; Descending = $true } |
        Select-Object -First 1
}

function Test-ApkHasDex {
    param([string]$ApkPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ApkPath)
    try {
        return ($zip.Entries | Where-Object { $_.FullName -like "classes*.dex" } | Select-Object -First 1) -ne $null
    }
    finally {
        $zip.Dispose()
    }
}

function Remove-PublishFileIfExists {
    param([string]$PathValue)

    if (Test-Path -LiteralPath $PathValue) {
        Remove-Item -LiteralPath $PathValue -Force
    }
}

function Copy-ApkFile {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    $destinationDirectory = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

    if ([System.IO.Path]::GetFullPath($SourcePath) -ne [System.IO.Path]::GetFullPath($DestinationPath)) {
        Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
    }
}

$repoRoot = Resolve-WorkspacePath "."
$backendProject = Join-Path $repoRoot "apps/backend-api/VinhKhanh.BackendApi.csproj"
$backendWwwrootDownloads = Join-Path $repoRoot "apps/backend-api/wwwroot/downloads"
$downloadApkPath = Join-Path $backendWwwrootDownloads "vinh-khanh-food-guide/tour.apk"
$legacyGuideDownloadApkPath = Join-Path $backendWwwrootDownloads "vinh-khanh-food-guide.apk"
$legacyTourDownloadApkPath = Join-Path $backendWwwrootDownloads "vinh-khanh-food-tour.apk"
$outputRootPath = Resolve-WorkspacePath $OutputRoot
$publishPath = Join-Path $outputRootPath "publish"

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = "vinh-khanh-backend-{0}.zip" -f (Get-Date -Format "yyyyMMddHHmmss")
}

$packagePath = Join-Path $outputRootPath $PackageName

if ([string]::IsNullOrWhiteSpace($MobileApkPath)) {
    $latestApk = Find-LatestMobileApk $repoRoot
    if ($null -eq $latestApk) {
        throw "Khong tim thay APK mobile trong apps/mobile-app/bin. Hay build mobile APK truoc khi package backend."
    }

    $MobileApkPath = $latestApk.FullName
}
else {
    $MobileApkPath = Resolve-WorkspacePath $MobileApkPath
}

if (-not (Test-Path -LiteralPath $MobileApkPath)) {
    throw "Khong tim thay APK mobile tai '$MobileApkPath'."
}

if (-not (Test-ApkHasDex $MobileApkPath)) {
    throw "APK '$MobileApkPath' khong co classes.dex. Hay clean/rebuild mobile truoc khi package."
}

Copy-ApkFile -SourcePath $MobileApkPath -DestinationPath $downloadApkPath
Copy-ApkFile -SourcePath $MobileApkPath -DestinationPath $legacyGuideDownloadApkPath
Copy-ApkFile -SourcePath $MobileApkPath -DestinationPath $legacyTourDownloadApkPath

if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:APPDATA = Join-Path $env:DOTNET_CLI_HOME "AppData/Roaming"
$env:NUGET_PACKAGES = Join-Path $env:DOTNET_CLI_HOME ".nuget/packages"

dotnet publish $backendProject `
    -c $Configuration `
    -o $publishPath `
    --no-restore `
    -p:UseAppHost=false

Remove-PublishFileIfExists (Join-Path $publishPath "appsettings.Development.example.json")
Get-ChildItem -LiteralPath $publishPath -Filter "appsettings.local-backup-*.json" -File -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

$nestedPublishPath = Join-Path $publishPath "publish"
if (Test-Path -LiteralPath $nestedPublishPath) {
    Remove-Item -LiteralPath $nestedPublishPath -Recurse -Force
}

$publishedApkPath = Join-Path $publishPath "wwwroot/downloads/vinh-khanh-food-guide/tour.apk"
if (-not (Test-Path -LiteralPath $publishedApkPath)) {
    throw "Publish output thieu wwwroot/downloads/vinh-khanh-food-guide/tour.apk."
}

if (-not (Test-ApkHasDex $publishedApkPath)) {
    throw "Published APK khong co classes.dex."
}

$publishedAppSettingsPath = Join-Path $publishPath "appsettings.json"
if (Test-Path -LiteralPath $publishedAppSettingsPath) {
    $settings = Get-Content -LiteralPath $publishedAppSettingsPath -Raw | ConvertFrom-Json
    if ($settings.DatabaseInitialization.AllowCreateDatabase -or
        $settings.DatabaseInitialization.AllowSeedDatabase -or
        $settings.DatabaseInitialization.AllowSchemaUpdates) {
        throw "DatabaseInitialization trong appsettings publish dang bat. Khong an toan de deploy len DB hien tai."
    }
}

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $packagePath -Force

$audioFileCount = 0
$audioRoot = Join-Path $publishPath "wwwroot/storage/audio/pois"
if (Test-Path -LiteralPath $audioRoot) {
    $audioFileCount = (Get-ChildItem -LiteralPath $audioRoot -Recurse -File | Measure-Object).Count
}

Write-Output "Azure backend package ready."
Write-Output "Publish path: $publishPath"
Write-Output "Zip package: $packagePath"
Write-Output "Mobile APK: $publishedApkPath"
Write-Output "Tracked QR download endpoint: /downloads/vinh-khanh-food-guide/tour.apk"
Write-Output "QR diagnostics endpoint: /api/public/diagnostics/qr-scan-count"
Write-Output "POI audio files: $audioFileCount"
