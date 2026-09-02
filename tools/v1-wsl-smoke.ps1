param(
    [string]$Distro = "Ubuntu"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root ".artifacts\wsl-smoke-linux-x64"
$remote = "/tmp/terminal-linux-agent-$PID"

try {
    & wsl.exe -d $Distro -- true
    if ($LASTEXITCODE -ne 0) { throw "WSL distro '$Distro' is not available." }

    Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
    & dotnet publish (Join-Path $root "src\Terminal.LinuxAgent\Terminal.LinuxAgent.csproj") `
        --configuration Release `
        --runtime linux-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        --output $publish
    if ($LASTEXITCODE -ne 0) { throw "Linux agent publish failed." }

    $agent = Join-Path $publish "Terminal.LinuxAgent"
    $wslSource = (& wsl.exe -d $Distro -- wslpath -u $agent).Trim()
    if (-not $wslSource) { throw "Could not translate the Linux agent path into WSL." }

    & wsl.exe -d $Distro -- cp -- $wslSource $remote
    if ($LASTEXITCODE -ne 0) { throw "Could not stage the Linux agent inside WSL." }
    & wsl.exe -d $Distro -- chmod 700 $remote
    if ($LASTEXITCODE -ne 0) { throw "Could not mark the Linux agent executable." }

    $env:TERMINAL_RUN_WSL_E2E = "1"
    $env:TERMINAL_WSL_DISTRO = $Distro
    $env:TERMINAL_WSL_AGENT = $remote

    & dotnet test (Join-Path $root "tests\Terminal.Windows.Tests\Terminal.Windows.Tests.csproj") `
        --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Real WSL transport smoke test failed." }
}
finally {
    Remove-Item Env:TERMINAL_RUN_WSL_E2E -ErrorAction SilentlyContinue
    Remove-Item Env:TERMINAL_WSL_DISTRO -ErrorAction SilentlyContinue
    Remove-Item Env:TERMINAL_WSL_AGENT -ErrorAction SilentlyContinue
    & wsl.exe -d $Distro -- rm -f -- $remote 2>$null
}
