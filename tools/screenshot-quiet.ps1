<#
.SYNOPSIS
    Screenshots the Gambit window without disturbing the user: the window is restored without
    activation, sent to the bottom of the z-order (behind the user's windows), captured with
    PrintWindow, then put back (re-minimized if it was minimized).
#>
param(
    [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'gambit-shot.png'),
    [int]$Width = 1500,
    [int]$Height = 950
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
$proc = Get-Process Gambit -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$h = $proc.MainWindowHandle
$wasMinimized = [GambitOff.Win]::IsIconic($h)

$wp = New-Object GambitOff.Win+WINDOWPLACEMENT
$wp.length = [Runtime.InteropServices.Marshal]::SizeOf($wp)
[GambitOff.Win]::GetWindowPlacement($h, [ref]$wp) | Out-Null
$saved = $wp

# Restore without activating (SW_SHOWNOACTIVATE = 4), then send to the bottom of the z-order.
$wp.showCmd = 4
$wp.rcNormal.Left = 40; $wp.rcNormal.Top = 40
$wp.rcNormal.Right = 40 + $Width; $wp.rcNormal.Bottom = 40 + $Height
[GambitOff.Win]::SetWindowPlacement($h, [ref]$wp) | Out-Null
[GambitOff.Win]::SetWindowPos($h, [IntPtr]1, 0, 0, 0, 0, 0x0013) | Out-Null  # HWND_BOTTOM, NOSIZE|NOMOVE|NOACTIVATE
Start-Sleep -Milliseconds 900

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

# Put things back the way they were.
if ($wasMinimized) { $saved.showCmd = 7 }  # SW_SHOWMINNOACTIVE
[GambitOff.Win]::SetWindowPlacement($h, [ref]$saved) | Out-Null
"$Out ($Width x $Height)"
