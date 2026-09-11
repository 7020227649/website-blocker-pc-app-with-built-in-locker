@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$u='https://raw.githubusercontent.com/7020227649/website-blocker-pc-app-with-built-in-locker/main/install.ps1'; $p=Join-Path $env:TEMP 'SiteShield-install.ps1'; Invoke-WebRequest -UseBasicParsing $u -OutFile $p; powershell.exe -NoProfile -ExecutionPolicy Bypass -File $p"
if errorlevel 1 pause
