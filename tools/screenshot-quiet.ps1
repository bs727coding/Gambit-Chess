<#
.SYNOPSIS
    Screenshots the Gambit window without disturbing the user. A window that is on screen is
    captured in place with PrintWindow (never moved, resized or reordered: the user may be using
    it). A minimized window is restored without activation behind the user's windows, captured,
    then minimized again.
#>
param(
    [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'gambit-shot.png'),
    [int]$Width = 1500,
    [int]$Height = 950,
    [int]$ProcessId = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('GambitOff.Win' -as [type])) {
    Add-Type -Namespace GambitOff -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
[DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
[DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[StructLayout(LayoutKind.Sequential)] public struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT ptMin, ptMax; public RECT rcNormal; }
'@
}
[GambitOff.Win]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
. (Join-Path $PSScriptRoot 'gambit-window.ps1')
$proc = Get-GambitProcess $ProcessId
$h = $proc.MainWindowHandle
$wasMinimized = [GambitOff.Win]::IsIconic($h)

$wp = New-Object GambitOff.Win+WINDOWPLACEMENT
$wp.length = [Runtime.InteropServices.Marshal]::SizeOf($wp)
[GambitOff.Win]::GetWindowPlacement($h, [ref]$wp) | Out-Null
$saved = $wp

if ($wasMinimized) {
    # Restore without activating (SW_SHOWNOACTIVATE = 4), then send to the bottom of the z-order.
    $wp.showCmd = 4
    $wp.rcNormal.Left = 40; $wp.rcNormal.Top = 40
    $wp.rcNormal.Right = 40 + $Width; $wp.rcNormal.Bottom = 40 + $Height
    [GambitOff.Win]::SetWindowPlacement($h, [ref]$wp) | Out-Null
    [GambitOff.Win]::SetWindowPos($h, [IntPtr]1, 0, 0, 0, 0, 0x0013) | Out-Null  # HWND_BOTTOM, NOSIZE|NOMOVE|NOACTIVATE
    Start-Sleep -Milliseconds 900
}

$rect = New-Object GambitOff.Win+RECT
[GambitOff.Win]::GetWindowRect($h, [ref]$rect) | Out-Null
$Width = $rect.Right - $rect.Left; $Height = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $Width, $Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[GambitOff.Win]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# Put a minimized window back the way it was (an on-screen window was never touched).
if ($wasMinimized) {
    $saved.showCmd = 7  # SW_SHOWMINNOACTIVE
    [GambitOff.Win]::SetWindowPlacement($h, [ref]$saved) | Out-Null
}
"$Out ($Width x $Height)"
