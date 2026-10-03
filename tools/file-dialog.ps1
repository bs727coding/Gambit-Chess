<#
.SYNOPSIS
    Fills in a Windows Save/Open file dialog opened by the test-profile Gambit window, without the
    mouse or keyboard focus.
.DESCRIPTION
    Finds the dialog through UI Automation (raw tree), "types" the path into the file name box with
    posted WM_CHAR messages (the dialog ignores WM_SETTEXT) and presses OK with WM_COMMAND.
    ALWAYS pass a full path inside artifacts/ or a temp folder: the dialog starts in the user's
    Documents folder (OneDrive), where a bare file name would be saved.
.EXAMPLE
    ./tools/ui.ps1 invoke -Name "Back up progress"; ./tools/file-dialog.ps1 -Path "$PWD\artifacts\backup.zip"
.EXAMPLE
    ./tools/file-dialog.ps1 -Cancel
#>
param([string]$Path, [int]$TimeoutSec = 15, [switch]$Cancel, [int]$ProcessId = 0)

$ErrorActionPreference = 'Stop'
if (-not $Cancel -and -not [System.IO.Path]::IsPathRooted($Path)) { throw 'Pass a full path (a bare name would be saved in Documents).' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('GambitDialog.Native' -as [type])) {
    Add-Type -Namespace GambitDialog -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, int flags, int timeout, out IntPtr result);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
'@
}
. (Join-Path $PSScriptRoot 'gambit-window.ps1')
$AE = [System.Windows.Automation.AutomationElement]
$walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
$proc = Get-GambitProcess $ProcessId
$main = $AE::FromHandle($proc.MainWindowHandle)

function Children($el) { $c = $walker.GetFirstChild($el); while ($c) { $c; $c = $walker.GetNextSibling($c) } }
function Find-Raw($el, [scriptblock]$match, [int]$depth = 0) {
    if ($depth -gt 8) { return $null }
    foreach ($child in Children $el) {
        if (& $match $child.Current) { return $child }
        # Skip the file list and the folder tree: big, and never what we want.
        if ($child.Current.ClassName -notin @('UIItemsView', 'SysTreeView32')) {
            $found = Find-Raw $child $match ($depth + 1)
            if ($found) { return $found }
        }
    }
    return $null
}

$dialog = $null
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while (-not $dialog -and (Get-Date) -lt $deadline) {
    $dialog = Children $main | Where-Object { $_.Current.ClassName -eq '#32770' } | Select-Object -First 1
    if (-not $dialog) { Start-Sleep -Milliseconds 300 }
}
if (-not $dialog) { throw 'No file dialog appeared.' }
$hDlg = [IntPtr]$dialog.Current.NativeWindowHandle
$ok = Children $dialog | Where-Object { $_.Current.ClassName -eq 'Button' -and $_.Current.AutomationId -eq '1' } | Select-Object -First 1

$WM_COMMAND = 0x0111; $WM_CHAR = 0x0102; $EM_SETSEL = 0x00B1
if ($Cancel) {
    [void][GambitDialog.Native]::PostMessage($hDlg, $WM_COMMAND, [IntPtr]2, [IntPtr]::Zero) # IDCANCEL
    "cancelled $($dialog.Current.Name)"
    return
}

# Save dialogs: an Edit with id 1001. Open dialogs: a combo box (1148) with an Edit inside.
$combo = Find-Raw $dialog { param($c) $c.AutomationId -eq '1148' }
$edit = if ($combo) { Find-Raw $combo { param($c) $c.ClassName -eq 'Edit' } } else { Find-Raw $dialog { param($c) $c.ClassName -eq 'Edit' -and $c.AutomationId -eq '1001' } }
if (-not $edit) { throw 'No file name box found.' }
$hEdit = [IntPtr]$edit.Current.NativeWindowHandle
[IntPtr]$r = [IntPtr]::Zero
[void][GambitDialog.Native]::SendMessageTimeout($hEdit, $EM_SETSEL, [IntPtr]0, [IntPtr](-1), 2, 3000, [ref]$r)
foreach ($ch in $Path.ToCharArray()) { [void][GambitDialog.Native]::PostMessage($hEdit, $WM_CHAR, [IntPtr][int]$ch, [IntPtr]1) }
Start-Sleep -Milliseconds 500
[void][GambitDialog.Native]::PostMessage($hDlg, $WM_COMMAND, [IntPtr]1, [IntPtr]$ok.Current.NativeWindowHandle) # IDOK from the button
"$($dialog.Current.Name): $Path"
