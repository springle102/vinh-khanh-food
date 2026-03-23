[CmdletBinding()]
param(
    [int]$BackendPort = 5080,
    [string]$WindowsFramework = "net10.0-windows10.0.19041.0",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Convert-ToEncodedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandText
    )

    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($CommandText))
}

function New-WindowCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WindowTitle,
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$DotnetHome,
        [Parameter(Mandatory = $true)]
        [string]$AppDataPath,
        [Parameter(Mandatory = $true)]
        [string]$NugetPackagesPath,
        [Parameter(Mandatory = $true)]
        [string]$InnerCommand
    )

    return @"
`$ErrorActionPreference = 'Stop'
try {
    if (`$Host -and `$Host.UI -and `$Host.UI.RawUI) {
        `$Host.UI.RawUI.WindowTitle = '$WindowTitle'
    }
} catch {}

`$env:DOTNET_CLI_HOME = '$DotnetHome'
`$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
`$env:APPDATA = '$AppDataPath'
`$env:NUGET_PACKAGES = '$NugetPackagesPath'

Set-Location '$RepoRoot'
$InnerCommand
"@
}

function Start-PowerShellWindow {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WindowTitle,
        [Parameter(Mandatory = $true)]
        [string]$CommandText,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [switch]$DryRunMode
    )

    if ($DryRunMode) {
        Write-Host "----- $WindowTitle -----" -ForegroundColor Yellow
        Write-Host $CommandText
        return
    }

    $encoded = Convert-ToEncodedCommand -CommandText $CommandText
    Start-Process -FilePath "powershell.exe" `
        -WorkingDirectory $WorkingDirectory `
        -ArgumentList @("-NoExit", "-ExecutionPolicy", "Bypass", "-EncodedCommand", $encoded) | Out-Null
}

function Test-BackendReady {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BootstrapUrl,
        [int]$TimeoutSeconds = 2
    )

    try {
        Invoke-WebRequest -UseBasicParsing -Uri $BootstrapUrl -TimeoutSec $TimeoutSeconds | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Wait-BackendReady {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BootstrapUrl,
        [int]$MaxWaitSeconds = 25
    )

    $deadline = (Get-Date).AddSeconds($MaxWaitSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-BackendReady -BootstrapUrl $BootstrapUrl -TimeoutSeconds 2) {
            return $true
        }

        Start-Sleep -Seconds 1
    }

    return $false
}

$repoRoot = Split-Path -Parent $PSCommandPath
$dotnetHome = Join-Path $repoRoot ".dotnet-home"
$appDataPath = Join-Path $dotnetHome "AppData\Roaming"
$nugetPackagesPath = Join-Path $dotnetHome ".nuget\packages"

$backendProject = Join-Path $repoRoot "apps\backend-api\VinhKhanh.BackendApi.csproj"
$mobileProject = Join-Path $repoRoot "apps\mobile-app\VinhKhanh.MobileApp.csproj"
$bootstrapUrl = "http://localhost:$BackendPort/api/v1/bootstrap"

if (-not (Test-Path $backendProject)) {
    throw "Khong tim thay backend project: $backendProject"
}

if (-not (Test-Path $mobileProject)) {
    throw "Khong tim thay mobile project: $mobileProject"
}

New-Item -ItemType Directory -Force -Path $dotnetHome | Out-Null
New-Item -ItemType Directory -Force -Path $appDataPath | Out-Null
New-Item -ItemType Directory -Force -Path $nugetPackagesPath | Out-Null

$backendCommand = New-WindowCommand `
    -WindowTitle "Vinh Khanh Backend API" `
    -RepoRoot $repoRoot `
    -DotnetHome $dotnetHome `
    -AppDataPath $appDataPath `
    -NugetPackagesPath $nugetPackagesPath `
    -InnerCommand @"
Write-Host 'Starting backend API on http://localhost:$BackendPort ...' -ForegroundColor Cyan
dotnet run --project '$backendProject'
"@

$mobileCommand = New-WindowCommand `
    -WindowTitle "Vinh Khanh Mobile App" `
    -RepoRoot $repoRoot `
    -DotnetHome $dotnetHome `
    -AppDataPath $appDataPath `
    -NugetPackagesPath $nugetPackagesPath `
    -InnerCommand @"
Write-Host 'Starting .NET MAUI Windows app ...' -ForegroundColor Cyan
dotnet run --project '$mobileProject' -f '$WindowsFramework'
"@

if (Test-BackendReady -BootstrapUrl $bootstrapUrl -TimeoutSeconds 2) {
    Write-Host "Backend da san sang tai $bootstrapUrl" -ForegroundColor Green
}
else {
    Write-Host "Dang mo backend..." -ForegroundColor Cyan
    Start-PowerShellWindow -WindowTitle "Vinh Khanh Backend API" -CommandText $backendCommand -WorkingDirectory $repoRoot -DryRunMode:$DryRun

    if (-not $DryRun) {
        if (Wait-BackendReady -BootstrapUrl $bootstrapUrl -MaxWaitSeconds 25) {
            Write-Host "Backend da san sang." -ForegroundColor Green
        }
        else {
            Write-Warning "Backend chua phan hoi sau 25 giay. Van tiep tuc mo app mobile."
        }
    }
}

Write-Host "Dang mo app mobile..." -ForegroundColor Cyan
Start-PowerShellWindow -WindowTitle "Vinh Khanh Mobile App" -CommandText $mobileCommand -WorkingDirectory $repoRoot -DryRunMode:$DryRun

if ($DryRun) {
    Write-Host "Dry run hoan tat. Khong co cua so nao duoc mo." -ForegroundColor Yellow
}
else {
    Write-Host "Da gui lenh mo backend va app mobile." -ForegroundColor Green
    Write-Host "Neu can, chay: .\run-mobile.ps1" -ForegroundColor DarkGray
}
