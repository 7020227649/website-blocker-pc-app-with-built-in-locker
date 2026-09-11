using Microsoft.Win32;
using System.Text.Json;

namespace SiteShield.Services;

public sealed class BrowserPolicy
{
    private readonly Dictionary<string, Dictionary<string, (RegistryValueKind Kind, object? Value)>> _backup = new(StringComparer.OrdinalIgnoreCase);
    private const string BackupPathName = "browser-policy-backup.json";
    private string BackupPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiteShield", BackupPathName);

    public void Enable()
    {
        LoadBackup();
        SetChromePolicies();
        SetEdgePolicies();
        SaveBackup();
        SystemProxy.SetLocalProxy();
    }

    public void Disable()
    {
        RestoreKey(@"SOFTWARE\Policies\Google\Chrome");
        RestoreKey(@"SOFTWARE\Policies\Microsoft\Edge");
        SystemProxy.Restore();
    }

    private void SetChromePolicies()
    {
        const string path = @"SOFTWARE\Policies\Google\Chrome";
        Remember(path, "ProxySettings");
        Remember(path, "DnsOverHttpsMode");
        Remember(path, "QuicAllowed");
        using var key = Registry.LocalMachine.CreateSubKey(path, true)!;
        var proxy = JsonSerializer.Serialize(new
        {
            ProxyMode = "fixed_servers",
            ProxyServer = "http://127.0.0.1:8888",
            ProxyBypassList = "<local>"
        });
        key.SetValue("ProxySettings", proxy, RegistryValueKind.String);
        key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
        key.SetValue("QuicAllowed", 0, RegistryValueKind.DWord);
    }

    private void SetEdgePolicies()
    {
        const string path = @"SOFTWARE\Policies\Microsoft\Edge";
        Remember(path, "ProxySettings");
        Remember(path, "DnsOverHttpsMode");
        Remember(path, "QuicAllowed");
        using var key = Registry.LocalMachine.CreateSubKey(path, true)!;
        var proxy = JsonSerializer.Serialize(new
        {
            ProxyMode = "fixed_servers",
            ProxyServer = "http://127.0.0.1:8888",
            ProxyBypassList = "<local>"
        });
        key.SetValue("ProxySettings", proxy, RegistryValueKind.String);
        key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
        key.SetValue("QuicAllowed", 0, RegistryValueKind.DWord);
    }

    private void Remember(string keyPath, string valueName)
    {
        if (!_backup.TryGetValue(keyPath, out var values))
        {
            values = new Dictionary<string, (RegistryValueKind, object?)>(StringComparer.OrdinalIgnoreCase);
            _backup[keyPath] = values;
        }

        if (values.ContainsKey(valueName)) return;
        using var key = Registry.LocalMachine.OpenSubKey(keyPath, false);
        if (key is null)
        {
            values[valueName] = (RegistryValueKind.None, null);
            return;
        }
        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null)
        {
            values[valueName] = (RegistryValueKind.None, null);
            return;
        }
        values[valueName] = (key.GetValueKind(valueName), value);
    }

    private void RestoreKey(string keyPath)
    {
        if (!_backup.TryGetValue(keyPath, out var values)) return;
        using var key = Registry.LocalMachine.CreateSubKey(keyPath, true)!;
        foreach (var pair in values)
        {
            if (pair.Value.Kind == RegistryValueKind.None)
                key.DeleteValue(pair.Key, false);
            else if (pair.Value.Kind == RegistryValueKind.DWord && pair.Value.Value is string text && int.TryParse(text, out var number))
                key.SetValue(pair.Key, number, RegistryValueKind.DWord);
            else
                key.SetValue(pair.Key, pair.Value.Value, pair.Value.Kind);
        }
    }

    private void LoadBackup()
    {
        try
        {
            if (!File.Exists(BackupPath)) return;
            var dto = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, BackupValue>>>(File.ReadAllText(BackupPath));
            if (dto is null) return;
            foreach (var key in dto)
            {
                var values = new Dictionary<string, (RegistryValueKind, object?)>(StringComparer.OrdinalIgnoreCase);
                foreach (var value in key.Value)
                {
                    object? restored = value.Value.Kind == RegistryValueKind.DWord ? value.Value.StringValue : value.Value.StringValue;
                    values[value.Key] = (value.Value.Kind, restored);
                }
                _backup[key.Key] = values;
            }
        }
        catch { }
    }

    private void SaveBackup()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
            var dto = new Dictionary<string, Dictionary<string, BackupValue>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _backup)
            {
                var values = new Dictionary<string, BackupValue>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in key.Value)
                    values[pair.Key] = new BackupValue(pair.Value.Kind, pair.Value.Value?.ToString());
                dto[key.Key] = values;
            }
            File.WriteAllText(BackupPath, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private sealed record BackupValue(RegistryValueKind Kind, string? StringValue);
}

public static class SystemProxy
{
    private static readonly string BackupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SiteShield", "system-proxy-backup.json");

    public static void SetLocalProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true)!;
            if (!File.Exists(BackupPath))
            {
                var backup = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ProxyEnable"] = key.GetValue("ProxyEnable", 0),
                    ["ProxyServer"] = key.GetValue("ProxyServer", null),
                    ["ProxyOverride"] = key.GetValue("ProxyOverride", null)
                };
                Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
                File.WriteAllText(BackupPath, JsonSerializer.Serialize(backup));
            }
            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", "127.0.0.1:8888", RegistryValueKind.String);
            key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
            NotifyWinInet();
        }
        catch (UnauthorizedAccessException) { throw; }
    }

    public static void Restore()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true)!;
            if (File.Exists(BackupPath))
            {
                var backup = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(BackupPath));
                if (backup is not null)
                {
                    key.SetValue("ProxyEnable", backup.TryGetValue("ProxyEnable", out var enabled) ? enabled.GetInt32() : 0, RegistryValueKind.DWord);
                    RestoreString(key, backup, "ProxyServer");
                    RestoreString(key, backup, "ProxyOverride");
                }
                File.Delete(BackupPath);
            }
            else
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.DeleteValue("ProxyServer", false);
                key.DeleteValue("ProxyOverride", false);
            }
            NotifyWinInet();
        }
        catch { }
    }

    private static void RestoreString(RegistryKey key, Dictionary<string, JsonElement> backup, string name)
    {
        if (backup.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Null)
            key.SetValue(name, value.GetString() ?? string.Empty, RegistryValueKind.String);
        else
            key.DeleteValue(name, false);
    }

    private static void NotifyWinInet()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }

    [System.Runtime.InteropServices.DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}
