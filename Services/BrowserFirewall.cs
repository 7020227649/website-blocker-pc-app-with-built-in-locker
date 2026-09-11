using System.Diagnostics;
using System.Text;

namespace SiteShield.Services;

public sealed class BrowserFirewall
{
    private const string Prefix = "SiteShield Browser Block";
    private static readonly string[] BrowserNames = { "chrome.exe", "msedge.exe", "opera.exe", "launcher.exe", "brave.exe", "firefox.exe" };

    public void Enable()
    {
        Disable();
        foreach (var exe in DiscoverBrowsers())
        {
            RunNetsh($"advfirewall firewall add rule name=\"{Prefix} {Path.GetFileNameWithoutExtension(exe)}\" dir=out action=block program=\"{exe}\" enable=yes profile=any");
        }
    }

    public void Disable()
    {
        foreach (var name in BrowserNames.Select(x => Path.GetFileNameWithoutExtension(x)))
            RunNetsh($"advfirewall firewall delete rule name=\"{Prefix} {name}\"");
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
                if (File.Exists(path)) candidates.Add(path);
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
        yield return Path.Combine(root, "Mozilla Firefox", "firefox.exe");
    }

    private static void RunNetsh(string arguments)
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
        process?.WaitForExit(10000);
        if (process is not null && process.ExitCode != 0)
            throw new InvalidOperationException($"Windows Firewall operation failed: {arguments}");
    }
}
