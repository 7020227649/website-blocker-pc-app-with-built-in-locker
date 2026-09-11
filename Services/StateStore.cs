using System.Text.Json;
using SiteShield.Models;

namespace SiteShield.Services;

public sealed class StateStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SiteShield", "state.json");

    public AppState Load()
    {
        try
        {
            if (!File.Exists(_path)) return Default();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppState>(json) ?? Default();
        }
        catch
        {
            return Default();
        }
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static AppState Default() => new()
    {
        AllowedDomains = new List<string> { "google.com", "canva.com" }
    };
}
