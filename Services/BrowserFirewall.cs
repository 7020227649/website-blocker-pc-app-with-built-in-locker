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

    public int Enable()
    {
        Disable();
        var installed = 0;
        foreach (var exe in DiscoverBrowsers())
        {
            var ruleName = GetRuleName(exe);
            RunNetsh($"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block program=\"{exe}\" enable=yes profile=any");
            installed++;
        }
        return installed;
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
            {
                try
                {
                    if (File.Exists(path)) candidates.Add(path);
                }
                catch { }
            }
        }

        foreach (var installPath in DiscoverInstallRegistryPaths())
        {
            foreach (var path in GetKnownPaths(installPath))
            {
                try
                {
                    if (File.Exists(path)) candidates.Add(path);
                }
                catch { }
            }
        }

        return candidates;
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

    private static IEnumerable<string> DiscoverInstallRegistryPaths()
    {
        var registryRoots = new[]
        {
            Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        foreach (var root in registryRoots)
        {
            if (root is null) continue;
            using (root)
            {
                foreach (var name in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(name, false);
                    var displayName = key?.GetValue("DisplayName") as string ?? string.Empty;
                    if (!IsKnownBrowserProduct(displayName)) continue;
                    var installLocation = key?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                        yield return installLocation;
                }
            }
        }
    }

    private static bool IsKnownBrowserProduct(string displayName)
    {
        return displayName.Contains("Google Chrome", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Brave", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Opera", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Mozilla Firefox", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Chromium", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Arc", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("LibreWolf", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Waterfox", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Floorp", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Zen Browser", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Thorium", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Avast Secure Browser", StringComparison.OrdinalIgnoreCase);
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
