<#
.SYNOPSIS
    Captures the Gambit window to a PNG (used for visual checks during development).
.EXAMPLE
    ./tools/screenshot.ps1 -Out shot.png
    ./tools/screenshot.ps1 -Width 1400 -Height 900   # resize the window first
#>
param(
    [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'gambit-shot.png'),
    [string]$ProcessName = 'Gambit',
    [int]$Width = 0,
    [int]$Height = 0,
    [ValidateSet('Print', 'Screen')]
    [string]$Mode = 'Print'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('GambitShot.Win' -as [type])) {
    Add-Type -Namespace GambitShot -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT rect, int size);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@
}

[GambitShot.Win]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null  # per-monitor v2
$proc = Get-Process $ProcessName -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { throw "No visible window for process '$ProcessName'." }
$h = $proc.MainWindowHandle

if ($Width -gt 0 -and $Height -gt 0) {
    [GambitShot.Win]::ShowWindow($h, 9) | Out-Null   # restore
    [GambitShot.Win]::MoveWindow($h, 60, 60, $Width, $Height, $true) | Out-Null
    Start-Sleep -Milliseconds 600
}

$rect = New-Object GambitShot.Win+RECT
[GambitShot.Win]::GetWindowRect($h, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$hgt = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)

if ($Mode -eq 'Print') {
    $hdc = $g.GetHdc()
    [GambitShot.Win]::PrintWindow($h, $hdc, 2) | Out-Null  # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc)
} else {
    [GambitShot.Win]::ShowWindow($h, 9) | Out-Null
    [GambitShot.Win]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 400
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
}

$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"$Out ($w x $hgt)"
