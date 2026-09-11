using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SiteShield.Services;

public sealed class BrowserFirewall
{
    private const string Prefix = "SiteShield Browser Block";
    private static readonly string[] BrowserNames =
    {
        "chrome.exe",
        "msedge.exe",
        "opera.exe",
        "launcher.exe",
        "brave.exe",
        "firefox.exe",
        "vivaldi.exe",
        "chromium.exe",
        "arc.exe",
        "librewolf.exe",
        "waterfox.exe",
        "floorp.exe",
        "zen.exe",
        "thorium.exe",
        "avastsecurebrowser.exe"
    };

    private static readonly string[] UninstallRoots =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    public void Enable()
    {
        Disable();
        foreach (var exe in DiscoverBrowsers())
        {
            var ruleName = GetRuleName(exe);
            RunNetsh($"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block program=\"{exe}\" enable=yes profile=any");
        }
    }

    public void Disable()
    {
        var names = BrowserNames
            .Select(GetRuleNameForFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
            RunNetsh($"advfirewall firewall delete rule name=\"{name}\"", allowMissingRule: true);
    }

    private static IEnumerable<string> DiscoverBrowsers()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var path in GetKnownPaths(root))
                TryAdd(candidates, path);
        }

        foreach (var installLocation in DiscoverInstallLocations())
        {
            foreach (var path in GetKnownPaths(installLocation))
                TryAdd(candidates, path);

            foreach (var exe in BrowserNames)
                TryAdd(candidates, Path.Combine(installLocation, exe));
        }

        return candidates;
    }

    private static IEnumerable<string> DiscoverInstallLocations()
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var rootPath in UninstallRoots)
            {
                try
                {
                    using var root = hive.OpenSubKey(rootPath, false);
                    if (root is null) continue;

                    foreach (var subKeyName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var app = root.OpenSubKey(subKeyName, false);
                            var displayName = app?.GetValue("DisplayName") as string;
                            var installLocation = app?.GetValue("InstallLocation") as string;
                            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installLocation))
                                continue;

                            if (!LooksLikeBrowser(displayName)) continue;
                            var fullPath = Path.GetFullPath(installLocation.Trim());
                            if (Directory.Exists(fullPath)) locations.Add(fullPath);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        return locations;
    }

    private static bool LooksLikeBrowser(string displayName)
    {
        var value = displayName.ToLowerInvariant();
        return value.Contains("chrome") ||
               value.Contains("edge") ||
               value.Contains("opera") ||
               value.Contains("brave") ||
               value.Contains("firefox") ||
               value.Contains("vivaldi") ||
               value.Contains("chromium") ||
               value.Contains("arc") ||
               value.Contains("librewolf") ||
               value.Contains("waterfox") ||
               value.Contains("floorp") ||
               value.Contains("zen browser") ||
               value.Contains("thorium") ||
               value.Contains("avast secure browser");
    }

    private static void TryAdd(HashSet<string> candidates, string path)
    {
        try
        {
            if (File.Exists(path)) candidates.Add(Path.GetFullPath(path));
        }
        catch { }
    }

    private static IEnumerable<string> GetKnownPaths(string root)
    {
        yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
        yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
        yield return Path.Combine(root, "BraveSoftware", "Brave-Browser", "Application", "brave.exe");
        yield return Path.Combine(root, "Opera", "launcher.exe");
        yield return Path.Combine(root, "Opera", "opera.exe");
        yield return Path.Combine(root, "Programs", "Opera", "launcher.exe");
        yield return Path.Combine(root, "Programs", "Opera", "opera.exe");
        yield return Path.Combine(root, "Mozilla Firefox", "firefox.exe");
        yield return Path.Combine(root, "Vivaldi", "Application", "vivaldi.exe");
        yield return Path.Combine(root, "Chromium", "Application", "chromium.exe");
        yield return Path.Combine(root, "Arc", "Arc.exe");
        yield return Path.Combine(root, "LibreWolf", "librewolf.exe");
        yield return Path.Combine(root, "Waterfox", "waterfox.exe");
        yield return Path.Combine(root, "Floorp", "floorp.exe");
        yield return Path.Combine(root, "Zen Browser", "zen.exe");
        yield return Path.Combine(root, "Thorium", "thorium.exe");
        yield return Path.Combine(root, "AVAST Software", "Browser", "Application", "AvastSecureBrowser.exe");
    }

    private static string GetRuleName(string executablePath) =>
        $"{Prefix} {Path.GetFileNameWithoutExtension(executablePath)}";

    private static string GetRuleNameForFileName(string fileName) =>
        $"{Prefix} {Path.GetFileNameWithoutExtension(fileName)}";

    private static void RunNetsh(string arguments, bool allowMissingRule = false)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        });

        if (process is null)
            throw new InvalidOperationException("Could not start Windows Firewall configuration.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(10000))
        {
            try { process.Kill(true); } catch { }
            throw new InvalidOperationException("Windows Firewall configuration timed out.");
        }

        if (process.ExitCode != 0)
        {
            var output = $"{stdout}\n{stderr}";
            if (allowMissingRule && output.Contains("No rules match", StringComparison.OrdinalIgnoreCase))
                return;

            throw new InvalidOperationException($"Windows Firewall operation failed: {arguments}");
        }
    }
}
