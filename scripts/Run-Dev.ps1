<#
.SYNOPSIS
    Builds the current branch as a Belie dev build for QA and runs it on a copy of your data.

.DESCRIPTION
    The dev build lives in %LOCALAPPDATA%\Belie-dev\app and reads and writes only
    %LOCALAPPDATA%\Belie-dev\data (through BELIE_DATA_DIR), so your real profiles, widgets
    and settings are never touched. It shows a "Dev build" badge, its tray icon says
    "Belie (dev build)", and it never registers itself to start with Windows.

    Only one Belie can own the desktop at a time, so the installed Belie is closed politely
    first and started again (minimized) once you quit the dev build from its tray icon.

    The desktop wallpaper itself is shared by Windows: the dev build may change it while it
    runs, and your installed Belie puts its own back when it restarts.

.PARAMETER FreshData
    Throw away the dev build's data and copy your current data again.

.PARAMETER NoBuild
    Reuse the last dev build instead of building the current branch.

.PARAMETER Remove
    Delete the dev build and its data, then stop.

.EXAMPLE
    .\scripts\Run-Dev.ps1
.EXAMPLE
    .\scripts\Run-Dev.ps1 -FreshData
.EXAMPLE
    .\scripts\Run-Dev.ps1 -Remove
#>
[CmdletBinding()]
param([switch]$FreshData, [switch]$NoBuild, [switch]$Remove)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path $env:LOCALAPPDATA 'Belie-dev'
$app = Join-Path $root 'app'
$data = Join-Path $root 'data'
$realData = Join-Path $env:APPDATA 'Belie'
$installedFolder = Join-Path $env:LOCALAPPDATA 'Belie'
$installed = Join-Path $installedFolder 'Belie.exe'

function Get-BelieProcesses([string]$folder) {
    Get-Process Belie -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($folder, [StringComparison]::OrdinalIgnoreCase) }
}

function Wait-Gone([string]$folder, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-BelieProcesses $folder) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    return -not (Get-BelieProcesses $folder)
}

# The top bar and desktop widgets run in their own host, which outlives the app by design.
# Each host listens for a stop signal named after its settings file (see TopBarProcess.MutexName).
function Stop-Host([string]$prefix, [string]$file) {
    $bytes = [Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($file).ToUpperInvariant())
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).Substring(0, 16)
    $signal = $null
    if ([Threading.EventWaitHandle]::TryOpenExisting("Local\Belie.$prefix.$hash.Stop", [ref]$signal)) {
        try { [void]$signal.Set() } finally { $signal.Dispose() }
    }
}

if ($Remove) {
    if (Get-BelieProcesses $app) { throw 'The dev build is still running. Quit it from its tray icon first.' }
    if (Test-Path $root) { Remove-Item $root -Recurse -Force }
    Write-Host "Removed the dev build and its data ($root)."
    return
}

# 1. Build the branch into its own folder.
if (-not $NoBuild -or -not (Test-Path (Join-Path $app 'Belie.exe'))) {
    $branch = (git -C $repo branch --show-current)
    $commit = (git -C $repo rev-parse --short HEAD)
    Write-Host "Building '$branch' ($commit) into $app ..."
    dotnet publish (Join-Path $repo 'WallpaperProfiles.csproj') -c Release -o $app --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

# 2. Give the dev build its own copy of your data (logs stay behind).
if ($FreshData -and (Test-Path $data)) { Remove-Item $data -Recurse -Force }
if (-not (Test-Path $data)) {
    Write-Host "Copying your data into $data ..."
    New-Item -ItemType Directory -Force $data | Out-Null
    if (Test-Path $realData) {
        robocopy $realData $data /E /XD logs /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Copying your data failed (robocopy exit $LASTEXITCODE)." }
    }
}

# 3. Close the installed Belie politely so the dev build can own the desktop.
$wasRunning = [bool](Get-BelieProcesses $installedFolder)
try {
    if ($wasRunning) {
        Write-Host 'Closing your installed Belie for now ...'
        & $installed --exit
        Start-Sleep -Seconds 2
        Stop-Host 'TopBar' (Join-Path $realData 'topbar.json')
        Stop-Host 'Canvas' (Join-Path $realData 'desktop-canvas.json')
        if (-not (Wait-Gone $installedFolder 25)) {
            throw 'Your installed Belie did not close completely, so the dev build was not started.'
        }
    }

    # 4. Run the dev build and wait until you quit it.
    Write-Host 'Starting the dev build. Quit it from its tray icon (Exit) when you are done.'
    $env:BELIE_DATA_DIR = $data
    $dev = Start-Process (Join-Path $app 'Belie.exe') -PassThru
    $dev.WaitForExit()
    # The dev build's own top bar and widgets wind down the same way.
    Start-Sleep -Seconds 2
    Stop-Host 'TopBar' (Join-Path $data 'topbar.json')
    Stop-Host 'Canvas' (Join-Path $data 'desktop-canvas.json')
    Wait-Gone $app 25 | Out-Null
}
finally {
    Remove-Item Env:BELIE_DATA_DIR -ErrorAction SilentlyContinue
    # 5. Put your real Belie back.
    if ($wasRunning -and (Test-Path $installed)) {
        Write-Host 'Restarting your installed Belie ...'
        Start-Process $installed -ArgumentList '--minimized'
    }
}
Write-Host 'Dev build closed. Its data stays in' $data '(use -FreshData to start over).'
exit 0 # robocopy reports success as 1, which would otherwise look like a failure.
