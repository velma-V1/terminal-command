[CmdletBinding()]
param(
    [string]$InstallRoot = "",
    [switch]$NoShortcut,
    [switch]$RemoveState
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is unavailable; pass -InstallRoot explicitly."
    }
    $InstallRoot = Join-Path $env:LOCALAPPDATA "TerminalCommand"
}

$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$Marker = Join-Path $InstallRoot ".terminal-command-install"

if (Test-Path $InstallRoot) {
    if (-not (Test-Path $Marker)) {
        throw "Refusing to remove unrecognized directory: $InstallRoot"
    }
    Remove-Item -Recurse -Force $InstallRoot
    Write-Host "Removed Terminal Command install root: $InstallRoot"
}
else {
    Write-Host "Terminal Command install root is already absent: $InstallRoot"
}

if (-not $NoShortcut) {
    $Desktop = [Environment]::GetFolderPath("Desktop")
    if (-not [string]::IsNullOrWhiteSpace($Desktop)) {
        $ShortcutPath = Join-Path $Desktop "Terminal Command.lnk"
        if (Test-Path $ShortcutPath) {
            Remove-Item -Force $ShortcutPath
            Write-Host "Removed desktop shortcut."
        }
    }
}

if ($RemoveState) {
    $StatePath = Join-Path ([Environment]::GetFolderPath("UserProfile")) ".terminal-command"
    if (Test-Path $StatePath) {
        Remove-Item -Recurse -Force $StatePath
        Write-Host "Removed user state: $StatePath"
    }
}
else {
    Write-Host "User state/history was preserved. Use -RemoveState to delete it explicitly."
}
