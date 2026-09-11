$ErrorActionPreference = 'Stop'
$Repo = 'https://github.com/7020227649/website-blocker-pc-app-with-built-in-locker.git'
$InstallDir = Join-Path $env:LOCALAPPDATA 'SiteShield'
$RepoDir = Join-Path $env:TEMP 'SiteShield-source'
$LogFile = Join-Path $env:TEMP 'SiteShield-install.log'

$IsAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $IsAdmin) {
  Write-Host '=== SiteShield one-click installer ===' -ForegroundColor Cyan
  Write-Host 'Requesting Administrator permission...' -ForegroundColor Yellow
  $argList = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$PSCommandPath)
  try {
    $child = Start-Process -FilePath powershell.exe -Verb RunAs -ArgumentList $argList -Wait -PassThru -WindowStyle Normal
    Write-Host "Elevated installer exited with code $($child.ExitCode)."
    if ($child.ExitCode -ne 0) {
      Write-Host "Installation failed. Check $LogFile for details." -ForegroundColor Red
      exit $child.ExitCode
    }
    Write-Host 'Installation completed.' -ForegroundColor Green
    exit 0
  }
  catch {
    Write-Host "Could not start the elevated installer: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
  }
}

Start-Transcript -Path $LogFile -Append | Out-Null
try {
  Write-Host '=== SiteShield one-click installer ===' -ForegroundColor Cyan
  Write-Host 'Administrator permission confirmed.' -ForegroundColor Green

  function Refresh-Path {
    $machine = [System.Environment]::GetEnvironmentVariable('Path','Machine')
    $user = [System.Environment]::GetEnvironmentVariable('Path','User')
    $env:Path = $machine + ';' + $user
  }

  function Install-WingetPackage([string]$Id, [string]$Name) {
    Write-Host "Installing $Name automatically..." -ForegroundColor Yellow
    if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) {
      throw 'Windows Package Manager (winget) is not available. Please install/update Microsoft App Installer and run the same command again.'
    }
    & winget.exe install --id $Id --exact --source winget --accept-source-agreements --accept-package-agreements --silent
    if ($LASTEXITCODE -ne 0) { throw "winget failed while installing $Name (exit code $LASTEXITCODE)." }
    Refresh-Path
  }

  if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { Install-WingetPackage 'Git.Git' 'Git for Windows' }
  if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) { Install-WingetPackage 'Microsoft.DotNet.SDK.8' '.NET 8 SDK' }
  if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { throw 'Git installation did not complete successfully.' }
  if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) { throw '.NET 8 SDK installation did not complete successfully.' }

  Write-Host 'Stopping any previous SiteShield process...' -ForegroundColor Yellow
  Get-Process -Name SiteShield -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

  $hostsPath = Join-Path $env:WINDIR 'System32\drivers\etc\hosts'
  if (Test-Path $hostsPath) {
    Write-Host 'Removing legacy SiteShield hosts entries...' -ForegroundColor Yellow
    $hostsText = Get-Content -Path $hostsPath -Raw
    $cleanHosts = [regex]::Replace($hostsText, '(?ms)^# SITESHIELD START.*?# SITESHIELD END\r?\n?', '')
    if ($cleanHosts -ne $hostsText) { Set-Content -Path $hostsPath -Value $cleanHosts -NoNewline }
  }

  if (Test-Path $RepoDir) { Remove-Item $RepoDir -Recurse -Force }
  Write-Host 'Cloning SiteShield...' -ForegroundColor Yellow
  & git.exe clone --depth 1 $Repo $RepoDir
  if ($LASTEXITCODE -ne 0) { throw 'Git clone failed.' }

  if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
  New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

  Write-Host 'Building SiteShield for Windows x64...' -ForegroundColor Yellow
  & dotnet.exe publish (Join-Path $RepoDir 'SiteShield.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $InstallDir
  if ($LASTEXITCODE -ne 0) { throw 'SiteShield build failed.' }

  $Exe = Join-Path $InstallDir 'SiteShield.exe'
  if (-not (Test-Path $Exe)) { throw 'Build completed without producing SiteShield.exe.' }

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
}
catch {
  Write-Host ''
  Write-Host 'INSTALLATION FAILED' -ForegroundColor Red
  Write-Host $_.Exception.Message -ForegroundColor Red
  Write-Host "Full installer log: $LogFile" -ForegroundColor Yellow
  exit 1
}
finally {
  Stop-Transcript | Out-Null
}

Write-Host ''
Read-Host 'Press Enter to close this installer window'
