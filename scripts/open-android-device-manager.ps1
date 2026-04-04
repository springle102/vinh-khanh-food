Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms

Add-Type @'
using System;
using System.Runtime.InteropServices;

public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

public static class Win32
{
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@

function Resolve-AndroidDeviceManagerPath {
    $candidateMap = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    $visualStudioRoots = @(
        "C:\Program Files\Microsoft Visual Studio",
        "C:\Program Files (x86)\Microsoft Visual Studio"
    )

    foreach ($root in $visualStudioRoots) {
        if (-not (Test-Path $root)) {
            continue
        }

        Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object {
                Get-ChildItem -Path $_.FullName -Directory -ErrorAction SilentlyContinue |
                    ForEach-Object {
                        $candidate = Join-Path $_.FullName "Common7\IDE\Extensions\Microsoft\Maui\AndroidDeviceManager\AndroidDevices.exe"
                        if (Test-Path $candidate) {
                            [void]$candidateMap.Add($candidate)
                        }
                    }
            }
    }

    $vsWherePath = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vsWherePath) {
        $installRoots = & $vsWherePath -products * -prerelease -requires Microsoft.Component.Maui -property installationPath 2>$null
        foreach ($installRoot in $installRoots) {
            if ([string]::IsNullOrWhiteSpace($installRoot)) {
                continue
            }

            $candidate = Join-Path $installRoot.Trim() "Common7\IDE\Extensions\Microsoft\Maui\AndroidDeviceManager\AndroidDevices.exe"
            if (Test-Path $candidate) {
                [void]$candidateMap.Add($candidate)
            }
        }
    }

    $candidates = @($candidateMap.ToArray() | Sort-Object -Descending)
    if ($candidates.Count -gt 0) {
        return $candidates[0]
    }

    throw "Khong tim thay Android Device Manager. Hay mo Visual Studio Installer va cai workload .NET MAUI hoac Android SDK tools."
}

function Get-OrStart-AndroidDeviceManager {
    $existing = Get-Process -Name AndroidDevices -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $existing) {
        return $existing
    }

    $path = Resolve-AndroidDeviceManagerPath
    return Start-Process -FilePath $path -PassThru
}

function Wait-ForMainWindow {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Android Device Manager da dong truoc khi hien cua so."
        }

        if ($Process.MainWindowHandle -ne 0) {
            return [IntPtr]$Process.MainWindowHandle
        }

        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)

    throw "Android Device Manager khong hien cua so trong thoi gian cho."
}

function Move-WindowIntoView {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr]$Handle
    )

    $screenBounds = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $rect = New-Object RECT
    [Win32]::GetWindowRect($Handle, [ref]$rect) | Out-Null

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    $isInvalidSize = $width -lt 600 -or $height -lt 400
    $isOutside =
        $rect.Top -lt $screenBounds.Top -or
        $rect.Left -lt $screenBounds.Left -or
        $rect.Right -gt $screenBounds.Right -or
        $rect.Bottom -gt $screenBounds.Bottom

    if ($isInvalidSize -or $isOutside) {
        $targetWidth = [Math]::Min([Math]::Max($screenBounds.Width - 120, 900), $screenBounds.Width)
        $targetHeight = [Math]::Min([Math]::Max($screenBounds.Height - 140, 650), $screenBounds.Height)
        $targetX = $screenBounds.Left + 60
        $targetY = $screenBounds.Top + 60

        [Win32]::ShowWindowAsync($Handle, 9) | Out-Null
        [Win32]::SetWindowPos($Handle, [IntPtr]::Zero, $targetX, $targetY, $targetWidth, $targetHeight, 0x0040) | Out-Null
    }

    [Win32]::ShowWindowAsync($Handle, 9) | Out-Null
    [Win32]::SetForegroundWindow($Handle) | Out-Null
}

$process = Get-OrStart-AndroidDeviceManager
$windowHandle = Wait-ForMainWindow -Process $process
Move-WindowIntoView -Handle $windowHandle

Write-Host "Android Device Manager da duoc mo va dua ve man hinh." -ForegroundColor Green
