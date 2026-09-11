namespace SiteShield.Models;

public sealed class AppState
{
    public List<string> AllowedDomains { get; set; } = new();
    public bool ProtectionEnabled { get; set; }
}
