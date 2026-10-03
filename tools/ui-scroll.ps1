<#
.SYNOPSIS
    Scrolls the Gambit window's main scrollable area via UI Automation (no mouse, no focus needed).
.EXAMPLE
    ./tools/ui-scroll.ps1 -Percent 100   # bottom
    ./tools/ui-scroll.ps1 -Percent 0     # top
#>
param([double]$Percent = 100)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$proc = Get-Process Gambit -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true)
$best = $null
$bestArea = 0
foreach ($el in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
    $sp = $el.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if (-not $sp.Current.VerticallyScrollable) { continue }
    $r = $el.Current.BoundingRectangle
    if ($r.Width * $r.Height -gt $bestArea) { $best = $sp; $bestArea = $r.Width * $r.Height }
}
if (-not $best) { 'nothing to scroll'; return }
$best.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $Percent)
"scrolled to $Percent%"
