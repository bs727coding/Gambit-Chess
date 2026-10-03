<#
.SYNOPSIS
    Dot-sourced by the tools/ scripts: finds the Gambit window they may drive or capture.
.DESCRIPTION
    By default only a test-profile window ("Gambit (test profile)", started with
    ./build.ps1 run -Configuration Debug -DataDir artifacts/qa-profile). The user plays Gambit on
    this PC too, and their window must never be driven by accident; pass -ProcessId to target a
    specific window deliberately.
#>
function Get-GambitProcess([int]$ProcessId = 0) {
    if ($ProcessId) {
        $p = Get-Process -Id $ProcessId -ErrorAction Stop
        if ($p.MainWindowHandle -eq 0) { throw "Process $ProcessId has no window." }
        return $p
    }
    $test = @(Get-Process Gambit -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -like '*(test profile)*' })
    if ($test.Count -eq 0) {
        throw 'No Gambit test-profile window. Start one with ./build.ps1 run -Configuration Debug -DataDir artifacts/qa-profile, or pass -ProcessId to target another window deliberately.'
    }
    if ($test.Count -gt 1) { throw "Several test-profile windows (PIDs $($test.Id -join ', ')); pass -ProcessId." }
    return $test[0]
}
