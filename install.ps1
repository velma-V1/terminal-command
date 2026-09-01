[CmdletBinding()]
param(
    [string]$Source = $PSScriptRoot,
    [string]$InstallRoot = "",
    [string]$Version = "0.1.0",
    [switch]$NoShortcut
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version must be semantic version text such as 0.1.0."
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is unavailable; pass -InstallRoot explicitly."
    }
    $InstallRoot = Join-Path $env:LOCALAPPDATA "TerminalCommand"
}

$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$ReleaseDir = Join-Path (Join-Path $InstallRoot "releases") $Version
$Marker = Join-Path $InstallRoot ".terminal-command-install"
$CurrentFile = Join-Path $InstallRoot "current.txt"
$Launcher = Join-Path $InstallRoot "launch.cmd"
$createdRelease = $false

if (Test-Path $ReleaseDir) {
    throw "Release already exists: $ReleaseDir. Uninstall it or install a different version."
}

try {
    New-Item -ItemType Directory -Force -Path (Split-Path $ReleaseDir -Parent) | Out-Null
    New-Item -ItemType Directory -Path $ReleaseDir | Out-Null
    $createdRelease = $true

    $PythonCommand = Get-Command python -ErrorAction Stop
    & $PythonCommand.Source -m venv $ReleaseDir
    if ($LASTEXITCODE -ne 0) { throw "Failed to create release virtual environment." }

    $ReleasePython = Join-Path $ReleaseDir "Scripts\python.exe"
    $ReleaseExe = Join-Path $ReleaseDir "Scripts\terminal-command.exe"
    if (-not (Test-Path $ReleasePython)) { throw "Release Python executable was not created." }

    & $ReleasePython -m pip install --upgrade pip
    if ($LASTEXITCODE -ne 0) { throw "Failed to initialize pip." }
    & $ReleasePython -m pip install $Source
    if ($LASTEXITCODE -ne 0) { throw "Failed to install Terminal Command from $Source." }
    if (-not (Test-Path $ReleaseExe)) { throw "terminal-command executable was not installed." }

    $InstalledVersion = (& $ReleasePython -c "import importlib.metadata; print(importlib.metadata.version('terminal-command'))").Trim()
    if ($LASTEXITCODE -ne 0 -or $InstalledVersion -ne $Version) {
        throw "Installed package version '$InstalledVersion' does not match requested version '$Version'."
    }

    & $ReleaseExe --doctor
    if ($LASTEXITCODE -ne 0) { throw "Terminal Command doctor check failed; current release was not changed." }

    New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
    Set-Content -Path $Marker -Value "terminal-command install root" -Encoding UTF8

    $LauncherContent = @'
@echo off
setlocal
set "TERMINAL_COMMAND_INSTALL_ROOT=%~dp0"
if not exist "%~dp0current.txt" (
  echo Terminal Command has no active release.
  exit /b 1
)
set /p TC_VERSION=<"%~dp0current.txt"
if not defined TC_VERSION (
  echo Terminal Command current release pointer is empty.
  exit /b 1
)
set "TC_EXE=%~dp0releases\%TC_VERSION%\Scripts\terminal-command.exe"
if not exist "%TC_EXE%" (
  echo Terminal Command release %TC_VERSION% is missing.
  exit /b 1
)
"%TC_EXE%" %*
'@
    Set-Content -Path $Launcher -Value $LauncherContent -Encoding ASCII

    $TempCurrent = Join-Path $InstallRoot ("current.{0}.tmp" -f $PID)
    Set-Content -Path $TempCurrent -Value $Version -Encoding ASCII
    Move-Item -Force -Path $TempCurrent -Destination $CurrentFile

    if (-not $NoShortcut) {
        $Desktop = [Environment]::GetFolderPath("Desktop")
        if (-not [string]::IsNullOrWhiteSpace($Desktop)) {
            $ShortcutPath = Join-Path $Desktop "Terminal Command.lnk"
            $Shell = New-Object -ComObject WScript.Shell
            $Shortcut = $Shell.CreateShortcut($ShortcutPath)
            $Shortcut.TargetPath = $Launcher
            $Shortcut.WorkingDirectory = [Environment]::GetFolderPath("UserProfile")
            $Shortcut.IconLocation = $ReleaseExe
            $Shortcut.Save()
        }
    }

    Write-Host "Terminal Command $Version installed successfully."
    Write-Host "Install root: $InstallRoot"
    Write-Host "Launch: $Launcher"
}
catch {
    if ($createdRelease -and (Test-Path $ReleaseDir)) {
        Remove-Item -Recurse -Force $ReleaseDir -ErrorAction SilentlyContinue
    }
    throw
}
