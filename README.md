# SiteShield for Windows

A Windows desktop allow-list website blocker for Chromium browsers including Chrome, Edge, and Opera.

## Features
- Blocks web access by default at the Windows hosts layer.
- Maintains an allow list such as `google.com` and `canva.com`.
- Add and remove domains from a desktop UI.
- Applies changes with Administrator privileges.
- Flushes the Windows DNS cache after changes.
- Includes a GitHub Actions workflow that builds a self-contained Windows x64 executable package.

## Important limitation
This version uses the Windows `hosts` file, so protection is system-wide instead of being tied to one browser. It is not a kernel-level firewall driver. Existing browser connections may need to be closed/reopened after a protection change.

## Build locally
Install the .NET 8 SDK on Windows and run:

```powershell
dotnet publish SiteShield.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The build output contains `SiteShield.exe`.

## GitHub build
The workflow at `.github/workflows/build-windows.yml` runs on pushes to `main` and manual dispatch. Download the `SiteShield-windows-x64` artifact from the Actions run.
