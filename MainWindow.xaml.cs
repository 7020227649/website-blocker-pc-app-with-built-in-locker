using System.Windows;
using SiteShield.Models;
using SiteShield.Services;

namespace SiteShield;

public partial class MainWindow : Window
{
    private readonly StateStore _store = new();
    private readonly LocalWebProxy _proxy = new();
    private readonly BrowserPolicy _browserPolicy = new();
    private readonly BrowserFirewall _browserFirewall = new();
    private AppState _state = new();

    public MainWindow()
    {
        InitializeComponent();
        _state = _store.Load();
        RefreshUi();

        if (_state.ProtectionEnabled)
        {
            try
            {
                StartProtection();
            }
            catch
            {
                StopProtectionBestEffort();
                _state.ProtectionEnabled = false;
                _store.Save(_state);
                RefreshUi();
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopProtectionBestEffort();
        _state.ProtectionEnabled = false;
        try { _store.Save(_state); } catch { }
        base.OnClosed(e);
    }

    private void RefreshUi()
    {
        DomainsList.ItemsSource = null;
        DomainsList.ItemsSource = _state.AllowedDomains
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        StatusText.Text = _state.ProtectionEnabled
            ? "Protected — browser traffic is forced through the allow-list"
            : "Protection is off";
        ApplyButton.IsEnabled = !_state.ProtectionEnabled;
        DisableButton.IsEnabled = _state.ProtectionEnabled;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var domain = Normalize(DomainBox.Text);
        if (string.IsNullOrWhiteSpace(domain) || !domain.Contains('.'))
        {
            MessageBox.Show("Enter a valid domain such as google.com.", "SiteShield", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_state.AllowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            _state.AllowedDomains.Add(domain);
        DomainBox.Clear();
        _store.Save(_state);

        if (_state.ProtectionEnabled)
        {
            try { _proxy.UpdateAllowedDomains(_state.AllowedDomains); }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not update protection.\n\n{ex.Message}", "SiteShield", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        RefreshUi();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DomainsList.SelectedItem is not string domain) return;
        _state.AllowedDomains.RemoveAll(x => string.Equals(x, domain, StringComparison.OrdinalIgnoreCase));
        _store.Save(_state);
        if (_state.ProtectionEnabled) _proxy.UpdateAllowedDomains(_state.AllowedDomains);
        RefreshUi();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var installedBrowserRules = StartProtection();
            _state.ProtectionEnabled = true;
            _store.Save(_state);
            RefreshUi();

            var browserMessage = installedBrowserRules > 0
                ? $"{installedBrowserRules} browser firewall rule(s) installed."
                : "No supported browser installation was detected. Browser traffic cannot be enforced until a supported browser is installed.";

            MessageBox.Show(
                $"Protection is active. {browserMessage}\n\nClose and reopen your browser so existing connections are forced through SiteShield.",
                "SiteShield",
                MessageBoxButton.OK,
                installedBrowserRules > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (UnauthorizedAccessException)
        {
            StopProtectionBestEffort();
            MessageBox.Show("Administrator permission is required to enforce Windows firewall and proxy settings.", "Administrator permission required", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            StopProtectionBestEffort();
            MessageBox.Show($"Could not apply protection.\n\n{ex.Message}", "SiteShield", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private int StartProtection()
    {
        _proxy.Start(_state.AllowedDomains);
        try
        {
            if (!_proxy.IsRunning)
                throw new InvalidOperationException("SiteShield local proxy failed to start.");

            _browserPolicy.Enable();
            try
            {
                return _browserFirewall.Enable();
            }
            catch
            {
                _browserPolicy.Disable();
                throw;
            }
        }
        catch
        {
            _proxy.Stop();
            throw;
        }
    }

    private void StopProtectionBestEffort()
    {
        try { _browserFirewall.Disable(); } catch { }
        try { _browserPolicy.Disable(); } catch { }
        try { _proxy.Stop(); } catch { }
    }

    private void DisableButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StopProtectionBestEffort();
            _state.ProtectionEnabled = false;
            _store.Save(_state);
            RefreshUi();
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("Administrator permission is required to remove Windows firewall and browser policies.", "Administrator permission required", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not disable protection.\n\n{ex.Message}", "SiteShield", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string Normalize(string value)
    {
        var domain = value.Trim().ToLowerInvariant().Replace("https://", "").Replace("http://", "").Split('/')[0].Trim('.');
        if (domain.StartsWith("www.")) domain = domain[4..];
        return domain;
    }
}
