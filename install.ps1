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

function Install-WingetPackage([string]$Id, [string]$Name) {
  Write-Host "Installing $Name automatically..." -ForegroundColor Yellow
  if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) {
    throw 'Windows Package Manager (winget) is not available. Please update App Installer from Microsoft Store and run the same command again.'
  }
  winget install --id $Id --exact --source winget --accept-source-agreements --accept-package-agreements --silent
}

if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
  Install-WingetPackage 'Git.Git' 'Git for Windows'
  $env:Path = [System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')
}

if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
  Install-WingetPackage 'Microsoft.DotNet.SDK.8' '.NET 8 SDK'
  $env:Path = [System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')
}

if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { throw 'Git installation did not complete successfully.' }
if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) { throw '.NET 8 SDK installation did not complete successfully.' }

if (Test-Path $RepoDir) { Remove-Item $RepoDir -Recurse -Force }
Write-Host 'Cloning SiteShield...' -ForegroundColor Yellow
git clone --depth 1 $Repo $RepoDir
if ($LASTEXITCODE -ne 0) { throw 'Git clone failed.' }

if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
New-Item -ItemType Directory -Path $InstallDir | Out-Null

Write-Host 'Building SiteShield for Windows x64...' -ForegroundColor Yellow
dotnet publish (Join-Path $RepoDir 'SiteShield.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $InstallDir
if ($LASTEXITCODE -ne 0) { throw 'SiteShield build failed.' }

$Exe = Join-Path $InstallDir 'SiteShield.exe'
if (-not (Test-Path $Exe)) { throw 'Build completed without producing SiteShield.exe' }

$Shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SiteShield.lnk'
$Shell = New-Object -ComObject WScript.Shell
$Link = $Shell.CreateShortcut($Shortcut)
$Link.TargetPath = $Exe
$Link.WorkingDirectory = $InstallDir
$Link.Description = 'SiteShield website allow-list firewall'
$Link.Save()

Remove-Item $RepoDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'SiteShield installed successfully.' -ForegroundColor Green
Write-Host "Installed to: $InstallDir"
Write-Host 'Desktop shortcut created.'
Write-Host 'Launching SiteShield...' -ForegroundColor Cyan
Start-Process $Exe
