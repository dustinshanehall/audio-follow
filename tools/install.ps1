# Installs audio-follow to %LOCALAPPDATA%\audio-follow, keeps any existing config.json,
# and starts it. The app registers itself to start at login (config: startWithWindows).
#   pwsh tools\install.ps1              install or update
#   pwsh tools\install.ps1 -Uninstall   stop, remove the startup entry and the folder
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $env:LOCALAPPDATA 'audio-follow'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process audio-follow -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if ($Uninstall) {
    Remove-ItemProperty -Path $runKey -Name 'audio-follow' -ErrorAction SilentlyContinue
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    "Uninstalled."
    return
}

$savedConfig = $null
$configPath = Join-Path $dest 'config.json'
if (Test-Path $configPath) { $savedConfig = Get-Content $configPath -Raw }

dotnet publish (Join-Path $root 'audio-follow.csproj') -c Release -o $dest --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if ($savedConfig) { Set-Content $configPath $savedConfig -NoNewline }

Start-Process (Join-Path $dest 'audio-follow.exe') -WorkingDirectory $dest
"Installed to $dest and started."
