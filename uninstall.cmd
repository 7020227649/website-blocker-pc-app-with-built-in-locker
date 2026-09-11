@echo off
setlocal

:: SiteShield One-Click Uninstaller
:: Run this CMD file as Administrator.

net session >nul 2>&1
if not %errorlevel%==0 (
    echo Requesting Administrator permission...
    powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo ========================================
echo        SiteShield One-Click Uninstaller
echo ========================================
echo.

echo Stopping SiteShield...
taskkill /F /IM SiteShield.exe >nul 2>&1

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
"$ErrorActionPreference='SilentlyContinue'; ^
$install=Join-Path $env:LOCALAPPDATA 'SiteShield'; ^
$desktop=[Environment]::GetFolderPath('Desktop'); ^
$hosts=Join-Path $env:WINDIR 'System32\drivers\etc\hosts'; ^
$proxyKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'; ^
$backup=Join-Path $install 'system-proxy-backup.json'; ^
$policyBackup=Join-Path $install 'browser-policy-backup.json'; ^

# Remove all SiteShield firewall rules, including rules created for discovered browsers.
Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like 'SiteShield Browser Block *' -or $_.Name -like 'SiteShield Browser Block *' } | Remove-NetFirewallRule -ErrorAction SilentlyContinue; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Chrome' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Msedge' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Opera' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Launcher' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Brave' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Firefox' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Vivaldi' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Chromium' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Arc' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Librewolf' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Waterfox' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Floorp' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Zen' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block Thorium' >nul 2>&1; ^
netsh advfirewall firewall delete rule name='SiteShield Browser Block AvastSecureBrowser' >nul 2>&1; ^

# Remove only the SiteShield-managed hosts section; preserve the user's other hosts entries.
if (Test-Path $hosts) { ^
  $text=[IO.File]::ReadAllText($hosts); ^
  $clean=[regex]::Replace($text,'(?ms)^# SITESHIELD START.*?# SITESHIELD END\r?\n?',''); ^
  [IO.File]::WriteAllText($hosts,$clean.TrimEnd()+[Environment]::NewLine); ^
}; ^

# Restore the exact proxy settings saved by SiteShield, if available.
if (Test-Path $backup) { ^
  $b=Get-Content $backup -Raw | ConvertFrom-Json; ^
  Set-ItemProperty $proxyKey -Name ProxyEnable -Value ([int]$b.ProxyEnable); ^
  if ($null -ne $b.ProxyServer) { Set-ItemProperty $proxyKey -Name ProxyServer -Value ([string]$b.ProxyServer) } else { Remove-ItemProperty $proxyKey -Name ProxyServer -ErrorAction SilentlyContinue }; ^
  if ($null -ne $b.ProxyOverride) { Set-ItemProperty $proxyKey -Name ProxyOverride -Value ([string]$b.ProxyOverride) } else { Remove-ItemProperty $proxyKey -Name ProxyOverride -ErrorAction SilentlyContinue }; ^
} else { ^
  Set-ItemProperty $proxyKey -Name ProxyEnable -Value 0; ^
  Remove-ItemProperty $proxyKey -Name ProxyServer -ErrorAction SilentlyContinue; ^
  Remove-ItemProperty $proxyKey -Name ProxyOverride -ErrorAction SilentlyContinue; ^
}; ^

# Restore browser policy values from SiteShield's backup, then remove empty SiteShield policy keys.
if (Test-Path $policyBackup) { ^
  $b=Get-Content $policyBackup -Raw | ConvertFrom-Json; ^
  foreach ($kp in $b.PSObject.Properties) { ^
    $key='HKLM:\'+$kp.Name; New-Item $key -Force | Out-Null; ^
    foreach ($vp in $kp.Value.PSObject.Properties) { ^
      $e=$vp.Value; ^
      if ([int]$e.Kind -eq 0) { Remove-ItemProperty $key -Name $vp.Name -ErrorAction SilentlyContinue } ^
      elseif ([int]$e.Kind -eq 4) { $n=0; [int]::TryParse([string]$e.StringValue,[ref]$n) | Out-Null; Set-ItemProperty $key -Name $vp.Name -Value $n -Type DWord } ^
      else { Set-ItemProperty $key -Name $vp.Name -Value ([string]$e.StringValue) -Type String } ^
    } ^
  } ^
}; ^

foreach ($key in @('SOFTWARE\Policies\Google\Chrome','SOFTWARE\Policies\Microsoft\Edge','SOFTWARE\Policies\BraveSoftware\Brave','SOFTWARE\Policies\Mozilla\Firefox\Proxy','SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS')) { ^
  $p='HKLM:\'+$key; ^
  if (Test-Path $p) { $k=Get-Item $p; if ($k.GetValueNames().Count -eq 0 -and $k.GetSubKeyNames().Count -eq 0) { Remove-Item $p -Force } } ^
}; ^

# Remove SiteShield local files and shortcut.
Remove-Item (Join-Path $desktop 'SiteShield.lnk') -Force -ErrorAction SilentlyContinue; ^
Remove-Item $install -Recurse -Force -ErrorAction SilentlyContinue; ^
Remove-Item (Join-Path $env:TEMP 'SiteShield-source') -Recurse -Force -ErrorAction SilentlyContinue; ^
Remove-Item (Join-Path $env:TEMP 'SiteShield-install.ps1') -Force -ErrorAction SilentlyContinue; ^
Remove-Item (Join-Path $env:TEMP 'SiteShield-install.log') -Force -ErrorAction SilentlyContinue; ^
Start-Process ipconfig.exe -ArgumentList '/flushdns' -WindowStyle Hidden -Wait; ^
Add-Type @' ^
using System; using System.Runtime.InteropServices; public static class W { [DllImport(\"wininet.dll\",SetLastError=true)] public static extern bool InternetSetOption(IntPtr h,int o,IntPtr p,int l); } ^
'@; ^
[W]::InternetSetOption([IntPtr]::Zero,39,[IntPtr]::Zero,0) | Out-Null; ^
[W]::InternetSetOption([IntPtr]::Zero,37,[IntPtr]::Zero,0) | Out-Null"

echo.
echo SiteShield cleanup completed.
echo Hosts blocks, SiteShield firewall rules, browser policies, proxy settings,
echo local state, backups, shortcut and installation files were removed/restored.
echo.
pause
endlocal
