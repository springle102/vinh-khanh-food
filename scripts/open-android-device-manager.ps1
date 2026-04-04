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
    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\Maui\AndroidDeviceManager\AndroidDevices.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\Extensions\Microsoft\Maui\AndroidDeviceManager\AndroidDevices.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\Extensions\Microsoft\Maui\AndroidDeviceManager\AndroidDevices.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Community\Common7\IDE\Extensions\Microsoft\Maui\AndroidDeviceManager\AndroidDevices.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Professional\Common7\IDE\Extensions\Microsoft\Maui\AndroidDeviceManager\AndroidDevices.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Enterprise\Common7\IDE\Extensions\Microsoft\Maui\AndroidDeviceManager\AndroidDevices.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Khong tim thay AndroidDevices.exe trong Visual Studio."
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
