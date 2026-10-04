<#
.SYNOPSIS
    Build / test / run helper for Gambit.
.EXAMPLE
    ./build.ps1            # build everything (Debug)
    ./build.ps1 test       # run unit tests
    ./build.ps1 run        # build and launch the app
    ./build.ps1 run -DataDir artifacts/test-profile   # launch with a throwaway profile (your real data untouched)
    ./build.ps1 publish    # self-contained Release build in ./artifacts/<rid>
    ./build.ps1 package -ServerUrl https://my-gambit.fly.dev   # installers + update packages for Arm64 and x64 PCs in ./artifacts/releases (docs/RELEASING.md)
    ./build.ps1 perft      # quick move-generator speed check
    ./build.ps1 server     # run the online play server on port 5080 (all network interfaces)
    ./build.ps1 server-admin users   # an admin command for that server (invite, users, ban, backup, help, ...)
    ./build.ps1 install    # install for this user (%LOCALAPPDATA%\Programs\Gambit + Start menu shortcut); re-run to update
    ./build.ps1 uninstall  # remove that install (your profile and games in %LOCALAPPDATA%\Gambit are kept)
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'test', 'run', 'publish', 'package', 'perft', 'clean', 'server', 'server-admin', 'install', 'uninstall')]
    [string]$Command = 'build',
    [ValidateSet('Debug', 'Release', '')]
    [string]$Configuration = '',
    [string]$Runtime = '',
    [string]$InstallDir = '',
    [string]$DataDir = '',
    # package/publish: the online server the app is made for (it updates from there; new profiles connect to it)
    [string]$ServerUrl = '',
    [switch]$NoShortcut,
    # server-admin: the command and its arguments, e.g. ./build.ps1 server-admin invite --uses 3
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdminArgs = @()
)

$ErrorActionPreference = 'Stop'
# 'run' and 'publish' default to Release (the engine is ~4x faster optimized); 'build'/'test' to Debug.
if (-not $Configuration) { $Configuration = if ($Command -in 'run', 'publish', 'package') { 'Release' } else { 'Debug' } }
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
    $publish = @('publish', $app, '-c', 'Release', "-p:Platform=$platform", '-r', $Runtime, '--self-contained', 'true', '--no-restore', '-o', $out, '-nologo')
    if ($ServerUrl) { $publish += "-p:GambitServerUrl=$($ServerUrl.TrimEnd('/'))" }
    Invoke-Dotnet $publish
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
    'package' {
        # Velopack installers, portable zips and update packages for both kinds of Windows PC, in
        # artifacts\releases. Copy that folder's files to the server's data folder (releases\) to offer
        # downloads (/download) and updates: docs/RELEASING.md.
        $version = (Select-Xml -Path (Join-Path $root 'Directory.Build.props') -XPath '//Version').Node.InnerText
        $releases = Join-Path $root 'artifacts\releases'
        if (-not $ServerUrl) {
            Write-Warning 'No -ServerUrl: installed copies will look for updates on the server set on their Online page (default http://localhost:5080).'
        }
        Invoke-Dotnet @('tool', 'restore')
        foreach ($rid in 'win-arm64', 'win-x64') {
            $Runtime = $rid
            $platform = if ($rid -eq 'win-arm64') { 'ARM64' } else { 'x64' }
            $out = Join-Path $root "artifacts\publish\$rid"
            if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
            Publish-App $out
            # The package id must differ from the data folder's name (Gambit): uninstalling deletes %LOCALAPPDATA%\<id>.
            $pack = @('vpk', 'pack', '--packId', 'GambitChess', '--packVersion', $version, '--packDir', $out, '--mainExe', 'Gambit.exe',
                '--packTitle', 'Gambit', '--packAuthors', 'Gambit', '--icon', (Join-Path $root 'src\Gambit.App\Assets\Gambit.ico'),
                '--channel', $rid, '--runtime', $rid, '--outputDir', $releases)
            # Code signing (optional): GAMBIT_SIGN_PARAMS holds signtool arguments, e.g. '/a /fd sha256 /tr http://timestamp.digicert.com /td sha256'.
            if ($env:GAMBIT_SIGN_PARAMS) { $pack += @('--signParams', $env:GAMBIT_SIGN_PARAMS) }
            # Or Azure Trusted Signing: GAMBIT_TRUSTED_SIGNING holds the path of its metadata.json.
            if ($env:GAMBIT_TRUSTED_SIGNING) { $pack += @('--azureTrustedSignFile', $env:GAMBIT_TRUSTED_SIGNING) }
            Invoke-Dotnet $pack
        }
        Write-Host "Gambit $version packaged in $releases"
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
        # Accounts, ratings and games live outside bin/ ('clean' deletes bin/).
        if (-not $env:GAMBIT_DATA) { $env:GAMBIT_DATA = Join-Path $env:LOCALAPPDATA 'Gambit Server' }
        Write-Host "Gambit server on http://0.0.0.0:5080 (Ctrl+C to stop), data in $env:GAMBIT_DATA."
        Write-Host 'Friends on your network connect to http://<this-PC-IP>:5080'
        Invoke-Dotnet @('run', '--project', $server, '-c', 'Release', '--no-restore', '--urls', 'http://0.0.0.0:5080')
    }
    'server-admin' {
        $server = Join-Path $root 'src\Gambit.Server\Gambit.Server.csproj'
        if (-not $env:GAMBIT_DATA) { $env:GAMBIT_DATA = Join-Path $env:LOCALAPPDATA 'Gambit Server' }
        $adminCommand = if ($AdminArgs.Count -gt 0) { $AdminArgs } else { @('help') }
        Invoke-Dotnet (@('run', '--project', $server, '-c', 'Release', '--') + $adminCommand)
    }
}
