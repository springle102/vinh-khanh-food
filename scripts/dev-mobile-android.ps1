param(
    [string]$Framework = "net10.0-android",
    [string]$Configuration = "Debug",
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

    throw "Could not find a valid Android SDK installation."
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
        "C:\Users\ADMIN\AppData\Local\Android\sdk\platform-tools\adb.exe"
    )

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Could not find adb.exe. Please install Android platform-tools first."
}

function Get-ConnectedDevice {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AdbPath
    )

    $output = & $AdbPath devices
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read Android devices from adb."
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
        throw "No Android emulator is online yet."
    }

    return $selectedDevice
}

function Try-GetConnectedDevice {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AdbPath
    )

    try {
        return Get-ConnectedDevice -AdbPath $AdbPath
    }
    catch {
        return $null
    }
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
        throw "Unable to resolve the launcher activity for the Android app."
    }

    $launchActivity = $output |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -like "$AndroidPackageId/*" } |
        Select-Object -Last 1

    if ([string]::IsNullOrWhiteSpace($launchActivity)) {
        throw "Could not find a valid launcher activity for the Android app."
    }

    return $launchActivity
}

function Test-PackageInstalled {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AdbPath,
        [Parameter(Mandatory = $true)]
        [string]$DeviceSerial,
        [Parameter(Mandatory = $true)]
        [string]$AndroidPackageId
    )

    & $AdbPath -s $DeviceSerial shell pm path $AndroidPackageId | Out-Null
    return $LASTEXITCODE -eq 0
}

function Resolve-AndroidIntermediateRootDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile,
        [Parameter(Mandatory = $true)]
        [string]$TargetFramework,
        [Parameter(Mandatory = $true)]
        [string]$BuildConfiguration
    )

    $projectDir = Split-Path -Path $ProjectFile -Parent
    return (Join-Path $projectDir ("obj\{0}\{1}" -f $BuildConfiguration, $TargetFramework))
}

function Resolve-AndroidIntermediateOutputDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile,
        [Parameter(Mandatory = $true)]
        [string]$TargetFramework,
        [Parameter(Mandatory = $true)]
        [string]$BuildConfiguration
    )

    return (Join-Path (Resolve-AndroidIntermediateRootDirectory -ProjectFile $ProjectFile -TargetFramework $TargetFramework -BuildConfiguration $BuildConfiguration) "android")
}

function Get-LatestWriteTimeUtc {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Paths
    )

    $items = foreach ($path in $Paths) {
        if (Test-Path $path) {
            Get-Item $path
        }
    }

    if ($null -eq $items -or @($items).Count -eq 0) {
        return $null
    }

    return (@($items) | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
}

function Test-AndroidPackagingCacheStale {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile,
        [Parameter(Mandatory = $true)]
        [string]$TargetFramework,
        [Parameter(Mandatory = $true)]
        [string]$BuildConfiguration
    )

    $projectDir = Split-Path -Path $ProjectFile -Parent
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectFile)
    $intermediateRoot = Resolve-AndroidIntermediateRootDirectory -ProjectFile $ProjectFile -TargetFramework $TargetFramework -BuildConfiguration $BuildConfiguration
    $androidOutputDirectory = Resolve-AndroidIntermediateOutputDirectory -ProjectFile $ProjectFile -TargetFramework $TargetFramework -BuildConfiguration $BuildConfiguration
    $managedAssemblyPath = Join-Path $intermediateRoot "$projectName.dll"
    $androidManifestPath = Join-Path $androidOutputDirectory "AndroidManifest.xml"
    $primaryDexPath = Join-Path $androidOutputDirectory "bin\classes.dex"
    $secondaryDexPath = Join-Path $androidOutputDirectory "bin\classes2.dex"

    if (-not (Test-Path $primaryDexPath)) {
        return $false
    }

    $referenceWriteTimeUtc = Get-LatestWriteTimeUtc -Paths @(
        $ProjectFile,
        (Join-Path $projectDir "Platforms\Android\AndroidManifest.xml"),
        (Join-Path $projectDir "Platforms\Android\MainActivity.cs"),
        (Join-Path $projectDir "Platforms\Android\MainApplication.cs"),
        $managedAssemblyPath
    )

    $packagingWriteTimeUtc = Get-LatestWriteTimeUtc -Paths @(
        $androidManifestPath,
        $primaryDexPath,
        $secondaryDexPath
    )

    if ($null -eq $referenceWriteTimeUtc -or $null -eq $packagingWriteTimeUtc) {
        return $false
    }

    return $referenceWriteTimeUtc -gt $packagingWriteTimeUtc
}

function Reset-AndroidPackagingCache {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile,
        [Parameter(Mandatory = $true)]
        [string]$TargetFramework,
        [Parameter(Mandatory = $true)]
        [string]$BuildConfiguration
    )

    $intermediateRoot = Resolve-AndroidIntermediateRootDirectory -ProjectFile $ProjectFile -TargetFramework $TargetFramework -BuildConfiguration $BuildConfiguration
    if (-not (Test-Path $intermediateRoot)) {
        return
    }

    $resolvedIntermediateRoot = (Resolve-Path $intermediateRoot).Path
    $targets = @(
        (Join-Path $resolvedIntermediateRoot "android"),
        (Join-Path $resolvedIntermediateRoot "stamp")
    )

    Write-Host "[deploy] Android packaging cache is stale. Rebuilding Android intermediates..." -ForegroundColor Yellow

    foreach ($target in $targets) {
        if (-not (Test-Path $target)) {
            continue
        }

        $resolvedTarget = (Resolve-Path $target).Path
        if (-not $resolvedTarget.StartsWith($resolvedIntermediateRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to delete a path outside the Android intermediate directory: $resolvedTarget"
        }

        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

function Wait-ForPackageInstalled {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AdbPath,
        [Parameter(Mandatory = $true)]
        [string]$DeviceSerial,
        [Parameter(Mandatory = $true)]
        [string]$AndroidPackageId,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-PackageInstalled -AdbPath $AdbPath -DeviceSerial $DeviceSerial -AndroidPackageId $AndroidPackageId) {
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "The Android package $AndroidPackageId was not visible on $DeviceSerial after installation."
}

function Wait-ForLaunchActivity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AdbPath,
        [Parameter(Mandatory = $true)]
        [string]$DeviceSerial,
        [Parameter(Mandatory = $true)]
        [string]$AndroidPackageId,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            return Resolve-LaunchActivity -AdbPath $AdbPath -DeviceSerial $DeviceSerial -AndroidPackageId $AndroidPackageId
        }
        catch {
            $lastError = $_
            Start-Sleep -Milliseconds 250
        }
    }

    if ($null -ne $lastError) {
        throw $lastError
    }

    throw "Could not resolve the launcher activity for $AndroidPackageId on $DeviceSerial."
}

function Invoke-InstallBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile,
        [Parameter(Mandatory = $true)]
        [string]$TargetFramework,
        [Parameter(Mandatory = $true)]
        [string]$BuildConfiguration,
        [Parameter(Mandatory = $true)]
        [string]$DeviceSerial
    )

    if (Test-AndroidPackagingCacheStale -ProjectFile $ProjectFile -TargetFramework $TargetFramework -BuildConfiguration $BuildConfiguration) {
        Reset-AndroidPackagingCache -ProjectFile $ProjectFile -TargetFramework $TargetFramework -BuildConfiguration $BuildConfiguration
    }

    Write-Host "[deploy] Building and installing with .NET Android tooling..." -ForegroundColor Cyan

    $dotnetArguments = @(
        "build",
        $ProjectFile,
        "-c", $BuildConfiguration,
        "-f", $TargetFramework,
        "-t:Install",
        "--no-restore",
        "-p:Device=$DeviceSerial",
        "-p:AppSettingsDirectory=$androidSettingsDirectory\",
        "-p:NuGetAudit=false",
        "-p:EmbedAssembliesIntoApk=true",
        "-p:AndroidUseSharedRuntime=false"
    )

    & dotnet @dotnetArguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Build/install failed. Fix the build issue and the watcher will try again."
    }
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
        [string]$BuildConfiguration,
        [Parameter(Mandatory = $true)]
        [string]$DeviceSerial,
        [Parameter(Mandatory = $true)]
        [string]$AndroidPackageId
    )

    Write-Host ""
    Write-Host "[deploy] Deploying to $DeviceSerial..." -ForegroundColor Cyan

    Invoke-InstallBuild -ProjectFile $ProjectFile -TargetFramework $TargetFramework -BuildConfiguration $BuildConfiguration -DeviceSerial $DeviceSerial
    Wait-ForPackageInstalled -AdbPath $AdbPath -DeviceSerial $DeviceSerial -AndroidPackageId $AndroidPackageId
    $launchActivity = Wait-ForLaunchActivity -AdbPath $AdbPath -DeviceSerial $DeviceSerial -AndroidPackageId $AndroidPackageId

    Write-Host "[deploy] Restarting $AndroidPackageId on $DeviceSerial..." -ForegroundColor Cyan
    & $AdbPath -s $DeviceSerial shell am force-stop $AndroidPackageId | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The app was installed, but the previous Android process could not be stopped."
    }

    Write-Host "[deploy] Launching $launchActivity on $DeviceSerial..." -ForegroundColor Cyan
    & $AdbPath -s $DeviceSerial shell am start -S -W -n $launchActivity | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The app was installed, but it could not be launched on the emulator."
    }

    Write-Host "[watch] The Android app has been updated on the emulator." -ForegroundColor Green
}

$adbPath = Resolve-AdbPath

Write-Host "[watch] Watching for changes in $projectDirectory" -ForegroundColor Yellow
Write-Host "[watch] Android SDK: $resolvedAndroidSdkRoot" -ForegroundColor Yellow
Write-Host "[watch] If no emulator is online yet, the watcher will keep waiting and deploy automatically when one appears." -ForegroundColor Yellow

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
    $activeDeviceSerial = ""
    $hasPrintedReadyMessage = $false
    $hasPrintedWaitingMessage = $false
    $pendingDeploy = $true

    while ($true) {
        $device = Try-GetConnectedDevice -AdbPath $adbPath
        if ($null -eq $device) {
            if (-not $hasPrintedWaitingMessage) {
                Write-Host "[watch] No Android emulator is online yet. Waiting..." -ForegroundColor Yellow
                $hasPrintedWaitingMessage = $true
            }

            $activeDeviceSerial = ""
            Start-Sleep -Milliseconds 1000
            continue
        }

        if ($hasPrintedWaitingMessage -or $activeDeviceSerial -ne $device.Serial) {
            $activeDeviceSerial = $device.Serial
            $pendingDeploy = $true
            $hasPrintedWaitingMessage = $false
            $hasPrintedReadyMessage = $false
            Write-Host "[watch] Emulator selected: $activeDeviceSerial" -ForegroundColor Yellow
        }

        if ($watchState.Dirty) {
            $elapsed = (Get-Date) - $watchState.LastChange
            if ($elapsed.TotalMilliseconds -lt $DebounceMilliseconds) {
                Start-Sleep -Milliseconds 250
                continue
            }

            $watchState.Dirty = $false
            $pendingDeploy = $true
            Write-Host ""
            Write-Host "[watch] Detected change: $($watchState.LastPath)" -ForegroundColor Yellow
        }

        if ($pendingDeploy -and -not [string]::IsNullOrWhiteSpace($activeDeviceSerial)) {
            try {
                Invoke-Deploy -AdbPath $adbPath -ProjectFile $resolvedProjectPath -TargetFramework $Framework -BuildConfiguration $Configuration -DeviceSerial $activeDeviceSerial -AndroidPackageId $PackageId
                $pendingDeploy = $false

                if (-not $hasPrintedReadyMessage) {
                    Write-Host "[watch] Waiting for the next change. Press Ctrl+C to stop." -ForegroundColor Yellow
                    $hasPrintedReadyMessage = $true
                }
            }
            catch {
                Write-Warning $_
                Write-Host "[watch] The watcher will keep running and retry deployment." -ForegroundColor Yellow
                Start-Sleep -Milliseconds 1500
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
