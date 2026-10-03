<#
.SYNOPSIS
    Drives the running Gambit app for development checks (UI Automation + targeted board clicks).
.EXAMPLE
    ./tools/ui.ps1 list                      # dump named elements
    ./tools/ui.ps1 invoke -Name "Play"       # invoke/select an element by name (no mouse)
    ./tools/ui.ps1 type -Name "Your name" -Text "Sam"  # set a text box (no keyboard)
    ./tools/ui.ps1 board -Moves "e2e4,g1f3"  # click squares on the board (mouse; window must be in front)
#>
param(
    [Parameter(Position = 0)][ValidateSet('list', 'invoke', 'click', 'board', 'type')][string]$Action = 'list',
    [string]$Name,
    [string]$Text,
    [string]$AutomationId,
    [string]$Moves,
    [switch]$Flipped,
    [int]$Depth = 12,
    [int]$DelayMs = 350,
    [int]$ProcessId = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('GambitUi.Native' -as [type])) {
    Add-Type -Namespace GambitUi -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
'@
}
[GambitUi.Native]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

. (Join-Path $PSScriptRoot 'gambit-window.ps1')
$proc = Get-GambitProcess $ProcessId
$hwnd = $proc.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$TreeScope = [System.Windows.Automation.TreeScope]

function Find-Element {
    $all = $root.FindAll($TreeScope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($el in $all) {
        $cur = $el.Current
        if ($AutomationId -and $cur.AutomationId -eq $AutomationId) { return $el }
        if ($Name -and $cur.Name -eq $Name -and -not $cur.IsOffscreen) { return $el }
    }
    throw "Element not found (Name='$Name' AutomationId='$AutomationId')."
}

function Ensure-Foreground {
    [GambitUi.Native]::ShowWindow($hwnd, 9) | Out-Null
    [GambitUi.Native]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 250
    if ([GambitUi.Native]::GetForegroundWindow() -ne $hwnd) { throw 'Gambit is not the foreground window; refusing to click.' }
}

function Click-At([double]$x, [double]$y) {
    [GambitUi.Native]::SetCursorPos([int]$x, [int]$y) | Out-Null
    Start-Sleep -Milliseconds 60
    [GambitUi.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero) # left down
    Start-Sleep -Milliseconds 40
    [GambitUi.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero) # left up
}

switch ($Action) {
    'list' {
        $all = $root.FindAll($TreeScope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($el in $all) {
            $c = $el.Current
            if ($c.Name -or $c.AutomationId) {
                "{0,-22} {1,-28} {2}" -f $c.ControlType.ProgrammaticName.Replace('ControlType.', ''), $c.AutomationId, $c.Name
            }
        }
    }
    'invoke' {
        $el = Find-Element
        $patterns = $el.GetSupportedPatterns()
        if ($patterns -contains [System.Windows.Automation.InvokePattern]::Pattern) {
            $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        } elseif ($patterns -contains [System.Windows.Automation.SelectionItemPattern]::Pattern) {
            $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        } elseif ($patterns -contains [System.Windows.Automation.TogglePattern]::Pattern) {
            $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        } else {
            throw "Element '$Name' supports no invoke/select/toggle pattern."
        }
        "invoked $($el.Current.Name)"
    }
    'type' {
        $el = Find-Element
        $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
        "set $($el.Current.Name) to '$Text'"
    }
    'click' {
        $el = Find-Element
        Ensure-Foreground
        $r = $el.Current.BoundingRectangle
        Click-At ($r.X + $r.Width / 2) ($r.Y + $r.Height / 2)
        "clicked $($el.Current.Name)"
    }
    'board' {
        # Plays moves by invoking the board's per-square automation elements (no mouse, no focus needed).
        $all = $root.FindAll($TreeScope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $squares = @{}
        foreach ($el in $all) { $id = $el.Current.AutomationId; if ($id -like 'sq-*') { $squares[$id] = $el } }
        if ($squares.Count -eq 0) { throw 'No board squares found (is a board on screen?).' }
        foreach ($mv in ($Moves -split ',')) {
            $mv = $mv.Trim()
            foreach ($s in @($mv.Substring(0, 2), $mv.Substring(2, 2))) {
                $squares["sq-$s"].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                Start-Sleep -Milliseconds 120
            }
            Start-Sleep -Milliseconds $DelayMs
            "played $mv"
        }
    }}
