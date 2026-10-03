<#
.SYNOPSIS
    Build / test / run helper for Gambit.
.EXAMPLE
    ./build.ps1            # build everything (Debug)
    ./build.ps1 test       # run unit tests
    ./build.ps1 run        # build and launch the app
    ./build.ps1 run -DataDir artifacts/test-profile   # launch with a throwaway profile (your real data untouched)
    ./build.ps1 publish    # self-contained Release build in ./artifacts/<rid>
    ./build.ps1 perft      # quick move-generator speed check
    ./build.ps1 server     # run the online play server on port 5080 (all network interfaces)
    ./build.ps1 install    # install for this user (%LOCALAPPDATA%\Programs\Gambit + Start menu shortcut); re-run to update
    ./build.ps1 uninstall  # remove that install (your profile and games in %LOCALAPPDATA%\Gambit are kept)
#>
param(
    [ValidateSet('build', 'test', 'run', 'publish', 'perft', 'clean', 'server', 'install', 'uninstall')]
    [string]$Command = 'build',
    [ValidateSet('Debug', 'Release', '')]
    [string]$Configuration = '',
    [string]$Runtime = '',
    [string]$InstallDir = '',
    [string]$DataDir = '',
    [switch]$NoShortcut
)

$ErrorActionPreference = 'Stop'
# 'run' and 'publish' default to Release (the engine is ~4x faster optimized); 'build'/'test' to Debug.
if (-not $Configuration) { $Configuration = if ($Command -in 'run', 'publish') { 'Release' } else { 'Debug' } }
$root = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# Locate the .NET SDK (machine-wide install first, then per-user).
$dotnet = @("$env:ProgramFiles\dotnet\dotnet.exe", "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dotnet) { throw '.NET SDK not found. Install the .NET 10 SDK (Arm64).' }

if (-not $Runtime) {
    $Runtime = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
}
$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }

$solution = Join-Path $root 'Gambit.slnx'
$app = Join-Path $root 'src\Gambit.App\Gambit.App.csproj'
$tests = Join-Path $root 'tests\Gambit.Tests\Gambit.Tests.csproj'
if (-not $InstallDir) { $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Gambit' }
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Gambit.lnk'

function Publish-App([string]$out) {
    Restore $app @("-p:Platform=$platform", "-r", $Runtime)
    Invoke-Dotnet @('publish', $app, '-c', 'Release', "-p:Platform=$platform", '-r', $Runtime, '--self-contained', 'true', '--no-restore', '-o', $out, '-nologo')
}

# Only ever delete a folder that holds a Gambit install (never an unrelated directory).
function Assert-GambitFolder([string]$dir) {
    if ((Test-Path $dir) -and -not (Test-Path (Join-Path $dir 'Gambit.exe'))) {
        throw "$dir exists but doesn't contain Gambit.exe; refusing to touch it. Pick another -InstallDir."
    }
}

function Assert-NotRunningFrom([string]$dir) {
    $running = Get-Process Gambit -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw "Close Gambit first (it is running from $dir)." }
}

function Invoke-Dotnet([string[]]$arguments) {
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

# NuGet restore with retries: one api.nuget.org CDN edge is intermittently unreachable from this
# network; packages already downloaded are cached, so each retry makes progress.
function Restore([string]$project, [string[]]$extra = @()) {
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        & $dotnet restore $project @extra --verbosity quiet
        if ($LASTEXITCODE -eq 0) { return }
        Write-Warning "Restore attempt $attempt failed; retrying..."
        Start-Sleep -Seconds 3
    }
    throw "NuGet restore failed for $project"
}

switch ($Command) {
    'clean' {
        Get-ChildItem $root -Recurse -Directory -Include bin, obj |
            Where-Object { $_.FullName -notmatch '\\\.git\\' } |
            Remove-Item -Recurse -Force
        Write-Host 'Cleaned bin/obj folders.'
    }
    'build' {
        Restore $solution @("-p:Platform=$platform")
        Invoke-Dotnet @('build', $solution, '-c', $Configuration, "-p:Platform=$platform", '--no-restore', '-nologo', '-clp:ErrorsOnly;Summary')
    }
    'test' {
        Restore $tests
        Invoke-Dotnet @('test', $tests, '-c', $Configuration, '--no-restore', '-nologo')
    }
    'perft' {
        Restore $tests
        Invoke-Dotnet @('test', $tests, '-c', 'Release', '--no-restore', '-nologo', '--filter', 'FullyQualifiedName~PerftTests')
    }
    'run' {
        Restore $app @("-p:Platform=$platform", "-r", $Runtime)
        Invoke-Dotnet @('build', $app, '-c', $Configuration, "-p:Platform=$platform", '-r', $Runtime, '--no-restore', '-nologo', '-clp:ErrorsOnly;Summary')
        $exe = Get-ChildItem (Join-Path $root "src\Gambit.App\bin\$platform\$Configuration") -Recurse -Filter 'Gambit.exe' |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $exe) { throw 'Gambit.exe not found after build.' }
        if ($DataDir) {
            # The app reads GAMBIT_DATA_DIR at startup (Services/JsonStore.cs); child processes inherit it.
            $env:GAMBIT_DATA_DIR = (New-Item -ItemType Directory -Force -Path $DataDir).FullName
            Write-Host "Using data folder $env:GAMBIT_DATA_DIR"
        }
        Write-Host "Launching $($exe.FullName)"
        Start-Process $exe.FullName
    }
    'publish' {
        $out = Join-Path $root "artifacts\$Runtime"
        Publish-App $out
        Write-Host "Published to $out"
    }
    'install' {
        # Per-user install: no admin rights, no certificate, no MSIX. Re-running updates it.
        Assert-GambitFolder $InstallDir
        Assert-NotRunningFrom $InstallDir
        $out = Join-Path $root "artifacts\$Runtime"
        Publish-App $out
        if (Test-Path $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
        Copy-Item -LiteralPath $out -Destination $InstallDir -Recurse
        $exe = Join-Path $InstallDir 'Gambit.exe'
        if (-not $NoShortcut) {
            $shell = New-Object -ComObject WScript.Shell
            $link = $shell.CreateShortcut($shortcut)
            $link.TargetPath = $exe
            $link.WorkingDirectory = $InstallDir
            $link.IconLocation = "$exe,0"
            $link.Description = 'Gambit - chess for Windows'
            $link.Save()
            Write-Host "Start menu shortcut: $shortcut"
        }
        Write-Host "Installed Gambit to $InstallDir"
    }
    'uninstall' {
        Assert-GambitFolder $InstallDir
        Assert-NotRunningFrom $InstallDir
        if (Test-Path $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
        if (-not $NoShortcut -and (Test-Path $shortcut)) { Remove-Item -LiteralPath $shortcut -Force }
        Write-Host "Removed $InstallDir (your data in $env:LOCALAPPDATA\Gambit is kept)."
    }
    'server' {
        $server = Join-Path $root 'src\Gambit.Server\Gambit.Server.csproj'
        Restore $server
        Write-Host 'Gambit server on http://0.0.0.0:5080 (Ctrl+C to stop). Friends on your network connect to http://<this-PC-IP>:5080'
        Invoke-Dotnet @('run', '--project', $server, '-c', 'Release', '--no-restore', '--urls', 'http://0.0.0.0:5080')
    }
}
