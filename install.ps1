$ErrorActionPreference = 'Stop'
$Repo = 'https://github.com/7020227649/website-blocker-pc-app-with-built-in-locker.git'
$InstallDir = Join-Path $env:LOCALAPPDATA 'SiteShield'
$RepoDir = Join-Path $env:TEMP 'SiteShield-source'

Write-Host '=== SiteShield one-click installer ===' -ForegroundColor Cyan
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  Write-Host 'Requesting Administrator permission...' -ForegroundColor Yellow
  $args = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
  Start-Process powershell.exe -Verb RunAs -ArgumentList $args
  exit
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
  Write-Host 'Git is required. Install Git for Windows, then run the command again.' -ForegroundColor Red
  exit 1
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Host '.NET 8 SDK is required. Install it, then run the command again.' -ForegroundColor Red
  exit 1
}

if (Test-Path $RepoDir) { Remove-Item $RepoDir -Recurse -Force }
git clone --depth 1 $Repo $RepoDir
if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
New-Item -ItemType Directory -Path $InstallDir | Out-Null

dotnet publish (Join-Path $RepoDir 'SiteShield.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $InstallDir

$Exe = Join-Path $InstallDir 'SiteShield.exe'
if (-not (Test-Path $Exe)) { throw 'Build completed without producing SiteShield.exe' }

$Shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SiteShield.lnk'
$Shell = New-Object -ComObject WScript.Shell
$Link = $Shell.CreateShortcut($Shortcut)
$Link.TargetPath = $Exe
$Link.WorkingDirectory = $InstallDir
$Link.Description = 'SiteShield website allow-list firewall'
$Link.Save()

Write-Host ''
Write-Host 'SiteShield installed successfully.' -ForegroundColor Green
Write-Host "Installed to: $InstallDir"
Write-Host 'Desktop shortcut created.'
Start-Process $Exe
