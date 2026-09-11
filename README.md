# SiteShield

Windows website allow-list firewall for Chrome, Edge, Opera, and other applications using the Windows hosts layer.

## One-click CMD installation

Open **Command Prompt as Administrator** and run:

```cmd
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "iwr 'https://raw.githubusercontent.com/7020227649/website-blocker-pc-app-with-built-in-locker/main/install.ps1' -OutFile $env:TEMP\SiteShield-install.ps1; powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:TEMP\SiteShield-install.ps1"
```

The installer clones the repository, publishes the self-contained x64 app, creates a Desktop shortcut, and launches SiteShield.

The app requires Administrator permission because Windows system network configuration is modified.

## One-click CMD uninstallation

To completely remove SiteShield and undo its system changes, open **Command Prompt as Administrator** and run:

```cmd
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "iwr 'https://raw.githubusercontent.com/7020227649/website-blocker-pc-app-with-built-in-locker/main/uninstall.cmd' -OutFile $env:TEMP\SiteShield-uninstall.cmd; cmd.exe /c $env:TEMP\SiteShield-uninstall.cmd"
```

Or download/run **`uninstall.cmd`** from this repository as Administrator.

The one-click uninstaller:

- Stops SiteShield.
- Removes all SiteShield browser firewall rules.
- Removes the SiteShield-managed hosts-file blocking section.
- Restores the Windows proxy settings saved by SiteShield, or disables the SiteShield proxy if no backup exists.
- Restores browser policy values saved by SiteShield and removes empty SiteShield policy keys.
- Flushes DNS and refreshes WinINet proxy settings.
- Removes the Desktop shortcut.
- Removes `%LOCALAPPDATA%\SiteShield` and SiteShield temporary installer/source files.

**Important:** The uninstaller removes SiteShield's changes while preserving unrelated entries in the Windows hosts file and restoring previously backed-up proxy/browser policy values where available.

## Manual build

Requires Windows and .NET 8 SDK:

```powershell
dotnet publish SiteShield.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```
