<#
.SYNOPSIS
    Runs PowerShell code outside the Claude desktop app's container and prints its output.
.DESCRIPTION
    Everything Claude Code starts on this PC inherits the Claude app's MSIX file-system
    virtualization: in its view of %LOCALAPPDATA% and %APPDATA%, files those processes created live
    in %LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache and hide the real ones (a stale
    private copy of the Gambit profile once did). A process created through WMI runs outside the
    container and sees what the user's own apps see. Use this to read the user's real profile;
    change it only with the user's OK.
.EXAMPLE
    ./tools/run-outside.ps1 'Get-ChildItem $env:LOCALAPPDATA\Gambit -Recurse | Select-Object FullName, Length, LastWriteTime'
#>
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Code,
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
# The script and its output live in artifacts\ (under Documents, which isn't virtualized), so both sides see them.
$dir = (New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot '..\artifacts\outside')).FullName
$script = Join-Path $dir 'run.ps1'
$out = Join-Path $dir 'run.txt'
$utf8 = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($script, "& {`r`n$Code`r`n} *>&1 | Out-File -LiteralPath '$out' -Encoding utf8 -Width 400`r`n", $utf8)
[System.IO.File]::WriteAllText($out, '', $utf8)

$started = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$script`""
}
if ($started.ReturnValue -ne 0) { throw "Couldn't start the outside process (WMI returned $($started.ReturnValue))." }
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (Get-Process -Id $started.ProcessId -ErrorAction SilentlyContinue) {
    if ((Get-Date) -gt $deadline) { throw "Timed out after $TimeoutSeconds s (process $($started.ProcessId) is still running)." }
    Start-Sleep -Milliseconds 300
}
Get-Content -LiteralPath $out -Encoding utf8
