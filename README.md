# SiteShield

Windows website allow-list firewall for Chrome, Edge, Opera, and other applications using the Windows hosts layer.

## One-click CMD installation

Open **Command Prompt as Administrator** and run:

```cmd
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "iwr 'https://raw.githubusercontent.com/7020227649/website-blocker-pc-app-with-built-in-locker/main/install.ps1' -OutFile $env:TEMP\SiteShield-install.ps1; powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:TEMP\SiteShield-install.ps1"
```

The installer clones the repository, publishes the self-contained x64 app, creates a Desktop shortcut, and launches SiteShield.

The app requires Administrator permission because Windows system network configuration is modified.

## Manual build

Requires Windows and .NET 8 SDK:

```powershell
dotnet publish SiteShield.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```
