@echo off
setlocal
set "SCRIPT=%TEMP%\SiteShield-install.ps1"
set "URL=https://raw.githubusercontent.com/7020227649/website-blocker-pc-app-with-built-in-locker/main/install.ps1"

echo === SiteShield one-click installer ===
echo Downloading installer...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing '%URL%' -OutFile '%SCRIPT%'"
if errorlevel 1 (
  echo Failed to download installer.
  pause
  exit /b 1
)

echo Starting Administrator installer console...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell.exe -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','%SCRIPT%')"
if errorlevel 1 (
  echo Failed to start Administrator installer.
  pause
  exit /b 1
)

echo The Administrator installer window is now open and will remain visible.
endlocal
