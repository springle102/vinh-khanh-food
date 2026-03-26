[CmdletBinding()]
param(
    [string]$Device,
    [string]$Connect,
    [switch]$Restore,
    [switch]$ListDevices
)

$ErrorActionPreference = "Stop"

if ($null -ne $Device -and $Device.ToString().Trim() -eq "1") {
    $Device = $null
}

function Get-AdbPath {
    $defaultPath = "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe"
    if (Test-Path $defaultPath) {
        return $defaultPath
    }

    $command = Get-Command adb -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "Khong tim thay adb. Hay cai Android platform-tools hoac cap nhat PATH."
}

function Get-ConnectedDeviceIds {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AdbPath
    )

    $lines = & $AdbPath devices
    if ($LASTEXITCODE -ne 0) {
        throw "Khong the doc danh sach thiet bi adb."
    }

    return @(
        $lines |
            Select-Object -Skip 1 |
            Where-Object { $_ -match "\sdevice$" } |
            ForEach-Object { ($_ -split "\s+")[0] }
    )
}

$repoRoot = Split-Path -Parent $PSCommandPath
$mobileProject = Join-Path $repoRoot "apps\mobile-app\VinhKhanh.MobileApp.csproj"
$dotnetHome = Join-Path $repoRoot ".dotnet-home"
$appDataPath = Join-Path $dotnetHome "AppData\Roaming"
$nugetPackagesPath = Join-Path $dotnetHome ".nuget\packages"
$androidSettingsPath = Join-Path $repoRoot ".android-settings"
$adbPath = Get-AdbPath

if (-not (Test-Path $mobileProject)) {
    throw "Khong tim thay mobile project: $mobileProject"
}

New-Item -ItemType Directory -Force -Path $dotnetHome | Out-Null
New-Item -ItemType Directory -Force -Path $appDataPath | Out-Null
New-Item -ItemType Directory -Force -Path $nugetPackagesPath | Out-Null
New-Item -ItemType Directory -Force -Path $androidSettingsPath | Out-Null

if ($Connect) {
    Write-Host "Dang ket noi adb toi $Connect ..." -ForegroundColor Cyan
    & $adbPath connect $Connect
}

if ($ListDevices) {
    & $adbPath devices -l
    exit 0
}

$deviceIds = Get-ConnectedDeviceIds -AdbPath $adbPath

if ($Device -eq "1" -and $deviceIds.Count -eq 1) {
    $Device = $null
}

if (-not $Device) {
    if ($deviceIds.Count -eq 0) {
        throw "Khong co thiet bi Android nao dang online. Neu dang dung Wi-Fi debug, hay chay: adb connect <ip:port>"
    }

    if ($deviceIds.Count -gt 1) {
        throw "Co nhieu hon 1 thiet bi dang ket noi. Hay chay lai voi -Device <device-id>."
    }

    $Device = $deviceIds[0]
}

if ($deviceIds -notcontains $Device) {
    throw "Khong tim thay thiet bi '$Device' trong adb devices."
}

$env:ANDROID_SERIAL = $Device
$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:APPDATA = $appDataPath
$env:NUGET_PACKAGES = $nugetPackagesPath

$arguments = @(
    "build",
    $mobileProject,
    "-t:Run",
    "-f", "net10.0-android",
    "-p:AppSettingsDirectory=$androidSettingsPath"
)

if (-not $Restore) {
    $arguments += "--no-restore"
}

Write-Host "Dang chay app Android tren thiet bi: $Device" -ForegroundColor Green
Write-Host ("dotnet " + ($arguments -join " ")) -ForegroundColor DarkGray

Push-Location $repoRoot
try {
    & dotnet @arguments
}
finally {
    Pop-Location
}
