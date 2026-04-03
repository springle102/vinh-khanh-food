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

$primaryBounds = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$knownHandles = @{}

while ($true) {
    $processes = Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue

    foreach ($process in $processes) {
        if ($process.MainWindowHandle -eq 0) {
            continue
        }

        $handle = [IntPtr]$process.MainWindowHandle
        $rect = New-Object RECT
        [Win32]::GetWindowRect($handle, [ref]$rect) | Out-Null

        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top
        if ($width -le 0 -or $height -le 0) {
            continue
        }

        $isOutside =
            $rect.Top -lt $primaryBounds.Top -or
            $rect.Left -lt $primaryBounds.Left -or
            $rect.Right -gt $primaryBounds.Right -or
            $rect.Bottom -gt $primaryBounds.Bottom

        if (-not $isOutside) {
            $knownHandles[$process.Id] = $true
            continue
        }

        $targetWidth = [Math]::Min($width, $primaryBounds.Width - 40)
        $targetHeight = [Math]::Min($height, $primaryBounds.Height - 40)
        $targetX = $primaryBounds.Left + 40
        $targetY = $primaryBounds.Top + 40

        [Win32]::ShowWindowAsync($handle, 9) | Out-Null
        [Win32]::SetWindowPos($handle, [IntPtr]::Zero, $targetX, $targetY, $targetWidth, $targetHeight, 0x0040) | Out-Null
        [Win32]::SetForegroundWindow($handle) | Out-Null
        $knownHandles[$process.Id] = $true
    }

    foreach ($pid in @($knownHandles.Keys)) {
        if (-not (Get-Process -Id $pid -ErrorAction SilentlyContinue)) {
            $knownHandles.Remove($pid)
        }
    }

    Start-Sleep -Milliseconds 800
}
