param(
    [string]$Framework = "net10.0-android",
    [string]$ProjectPath = "",
    [string]$PackageId = "com.vinhkhanh.foodguide.mobile",
    [int]$DebounceMilliseconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $PSScriptRoot "..\apps\mobile-app\VinhKhanh.MobileApp.csproj"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedProjectPath = (Resolve-Path $ProjectPath).Path
$projectDirectory = Split-Path -Path $resolvedProjectPath -Parent

$dotnetHome = Join-Path $repoRoot ".dotnet-home"
$appDataDirectory = Join-Path $dotnetHome "AppData\Roaming"
$localAppDataDirectory = Join-Path $dotnetHome "AppData\Local"
$nugetPackagesDirectory = Join-Path $dotnetHome ".nuget\packages"
$nugetHttpCacheDirectory = Join-Path $dotnetHome ".nuget\v3-cache"
$androidSettingsDirectory = Join-Path $repoRoot ".android-settings"

foreach ($directory in @($dotnetHome, $appDataDirectory, $localAppDataDirectory, $nugetPackagesDirectory, $nugetHttpCacheDirectory, $androidSettingsDirectory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"
$env:APPDATA = $appDataDirectory
$env:LOCALAPPDATA = $localAppDataDirectory
$env:NUGET_PACKAGES = $nugetPackagesDirectory
$env:NUGET_HTTP_CACHE_PATH = $nugetHttpCacheDirectory

function Resolve-AndroidSdkRoot {
    $candidates = @()

    foreach ($sdkRoot in @($env:ANDROID_SDK_ROOT, $env:ANDROID_HOME)) {
        if (-not [string]::IsNullOrWhiteSpace($sdkRoot)) {
            $candidates += $sdkRoot
        }
    }

    $candidates += @(
        "C:\Program Files (x86)\Android\android-sdk",
        "C:\Users\ADMIN\AppData\Local\Android\Sdk",
        "C:\Users\ADMIN\AppData\Local\Android\sdk"
    )

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path (Join-Path $candidate "platform-tools\adb.exe")) {
            return $candidate
        }
    }

    throw "Khong tim thay Android SDK hop le. Hay cai Android SDK truoc khi chay."
}

$resolvedAndroidSdkRoot = Resolve-AndroidSdkRoot
$env:ANDROID_SDK_ROOT = $resolvedAndroidSdkRoot
$env:ANDROID_HOME = $resolvedAndroidSdkRoot

function Resolve-AdbPath {
    $candidates = @()

    foreach ($sdkRoot in @($env:ANDROID_SDK_ROOT, $env:ANDROID_HOME)) {
        if (-not [string]::IsNullOrWhiteSpace($sdkRoot)) {
            $candidates += (Join-Path $sdkRoot "platform-tools\adb.exe")
        }
    }

    $candidates += @(
        "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe",
        "C:\Users\ADMIN\AppData\Local\Android\Sdk\platform-tools\adb.exe",
        "C:\Users\ADMIN\AppData\Local\Android\sdk\platform-tools\adb.exe",
        "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe"
    )

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Khong tim thay adb.exe. Hay mo Android SDK hoac cai dat platform-tools truoc khi chay."
}

function Get-ConnectedDevice {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AdbPath
    )

    $output = & $AdbPath devices
    if ($LASTEXITCODE -ne 0) {
        throw "Khong doc duoc danh sach thiet bi Android tu adb."
    }

    $devices = foreach ($line in ($output | Select-Object -Skip 1)) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $parts = ($line -split "\s+") | Where-Object { $_ }
        if ($parts.Count -lt 2 -or $parts[1] -ne "device") {
            continue
        }

        [pscustomobject]@{
            Serial = $parts[0]
            IsEmulator = $parts[0] -like "emulator-*"
        }
    }

    $selectedDevice = $devices |
        Sort-Object @{ Expression = "IsEmulator"; Descending = $true }, @{ Expression = "Serial"; Descending = $false } |
        Select-Object -First 1

    if ($null -eq $selectedDevice) {
        throw "Chua co emulator Android dang online. Hay mo may ao truoc, sau do chay lai script nay."
    }

    return $selectedDevice
}

function Resolve-LaunchActivity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AdbPath,
        [Parameter(Mandatory = $true)]
        [string]$DeviceSerial,
        [Parameter(Mandatory = $true)]
        [string]$AndroidPackageId
    )

    $output = & $AdbPath -s $DeviceSerial shell cmd package resolve-activity --brief $AndroidPackageId
    if ($LASTEXITCODE -ne 0) {
        throw "Khong xac dinh duoc launcher activity cua app Android."
    }

    $launchActivity = $output |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -like "$AndroidPackageId/*" } |
        Select-Object -Last 1

    if ([string]::IsNullOrWhiteSpace($launchActivity)) {
        throw "Khong tim thay launcher activity hop le cua app Android."
    }

    return $launchActivity
}

function Invoke-Deploy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AdbPath,
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile,
        [Parameter(Mandatory = $true)]
        [string]$TargetFramework,
        [Parameter(Mandatory = $true)]
        [string]$DeviceSerial,
        [Parameter(Mandatory = $true)]
        [string]$AndroidPackageId
    )

    Write-Host ""
    Write-Host "[deploy] Building and installing to $DeviceSerial..." -ForegroundColor Cyan

    $dotnetArguments = @(
        "build",
        $ProjectFile,
        "-f", $TargetFramework,
        "-t:Install",
        "--no-restore",
        "-p:Device=$DeviceSerial",
        "-p:AppSettingsDirectory=$androidSettingsDirectory\",
        "-p:NuGetAudit=false"
    )

    & dotnet @dotnetArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Build/Install that bai. Sua loi build roi script se tiep tuc theo doi."
    }

    $launchActivity = Resolve-LaunchActivity -AdbPath $AdbPath -DeviceSerial $DeviceSerial -AndroidPackageId $AndroidPackageId

    Write-Host "[deploy] Launching $launchActivity on $DeviceSerial..." -ForegroundColor Cyan
    & $AdbPath -s $DeviceSerial shell am start -W -n $launchActivity | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Da cai app xong nhung khong mo duoc app tren emulator."
    }

    Write-Host "[watch] App da duoc cap nhat len emulator." -ForegroundColor Green
}

$adbPath = Resolve-AdbPath
$device = Get-ConnectedDevice -AdbPath $adbPath

Write-Host "[watch] Dang theo doi thay doi trong $projectDirectory" -ForegroundColor Yellow
Write-Host "[watch] Emulator duoc chon: $($device.Serial)" -ForegroundColor Yellow
Write-Host "[watch] Android SDK duoc chon: $resolvedAndroidSdkRoot" -ForegroundColor Yellow

$watchState = [hashtable]::Synchronized(@{
    Dirty = $false
    LastChange = [datetime]::MinValue
    LastPath = ""
})

$eventNames = @(
    "VKMobileAndroidChanged",
    "VKMobileAndroidCreated",
    "VKMobileAndroidDeleted",
    "VKMobileAndroidRenamed"
)

$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $projectDirectory
$watcher.IncludeSubdirectories = $true
$watcher.NotifyFilter = [System.IO.NotifyFilters]"FileName, LastWrite, DirectoryName"
$watcher.EnableRaisingEvents = $true

$messageData = @{
    Extensions = @(".cs", ".csproj", ".xaml", ".json", ".html", ".css", ".js", ".svg", ".xml", ".png", ".jpg", ".jpeg", ".webp", ".ttf", ".otf")
    State = $watchState
}

$action = {
    $data = $Event.MessageData
    $state = $data.State
    $path = $Event.SourceEventArgs.FullPath

    if ([string]::IsNullOrWhiteSpace($path)) {
        return
    }

    if ($path -match "\\(bin|obj)\\") {
        return
    }

    $extension = [System.IO.Path]::GetExtension($path)
    if ([string]::IsNullOrWhiteSpace($extension)) {
        return
    }

    if ($data.Extensions -notcontains $extension.ToLowerInvariant()) {
        return
    }

    $state.Dirty = $true
    $state.LastChange = Get-Date
    $state.LastPath = $path
}

Register-ObjectEvent -InputObject $watcher -EventName Changed -SourceIdentifier $eventNames[0] -MessageData $messageData -Action $action | Out-Null
Register-ObjectEvent -InputObject $watcher -EventName Created -SourceIdentifier $eventNames[1] -MessageData $messageData -Action $action | Out-Null
Register-ObjectEvent -InputObject $watcher -EventName Deleted -SourceIdentifier $eventNames[2] -MessageData $messageData -Action $action | Out-Null
Register-ObjectEvent -InputObject $watcher -EventName Renamed -SourceIdentifier $eventNames[3] -MessageData $messageData -Action $action | Out-Null

try {
    Invoke-Deploy -AdbPath $adbPath -ProjectFile $resolvedProjectPath -TargetFramework $Framework -DeviceSerial $device.Serial -AndroidPackageId $PackageId
    Write-Host "[watch] Dang cho thay doi tiep theo. Nhan Ctrl+C de dung." -ForegroundColor Yellow

    while ($true) {
        if ($watchState.Dirty) {
            $elapsed = (Get-Date) - $watchState.LastChange
            if ($elapsed.TotalMilliseconds -ge $DebounceMilliseconds) {
                $watchState.Dirty = $false
                Write-Host ""
                Write-Host "[watch] Phat hien thay doi: $($watchState.LastPath)" -ForegroundColor Yellow

                try {
                    Invoke-Deploy -AdbPath $adbPath -ProjectFile $resolvedProjectPath -TargetFramework $Framework -DeviceSerial $device.Serial -AndroidPackageId $PackageId
                }
                catch {
                    Write-Warning $_
                    Write-Host "[watch] Script van tiep tuc theo doi de ban sua tiep." -ForegroundColor Yellow
                }
            }
        }

        Start-Sleep -Milliseconds 250
    }
}
finally {
    $watcher.EnableRaisingEvents = $false
    $watcher.Dispose()

    foreach ($eventName in $eventNames) {
        Unregister-Event -SourceIdentifier $eventName -ErrorAction SilentlyContinue
    }

    Get-Job |
        Where-Object { $_.Name -like "VKMobileAndroid*" } |
        Remove-Job -Force -ErrorAction SilentlyContinue
}
