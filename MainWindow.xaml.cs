using System.Windows;
using SiteShield.Models;
using SiteShield.Services;

namespace SiteShield;

public partial class MainWindow : Window
{
    private readonly StateStore _store = new();
    private readonly HostsBlocker _blocker = new();
    private AppState _state = new();

    public MainWindow()
    {
        InitializeComponent();
        _state = _store.Load();
        RefreshUi();
    }

    private void RefreshUi()
    {
        DomainsList.ItemsSource = null;
        DomainsList.ItemsSource = _state.AllowedDomains.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        StatusText.Text = _state.ProtectionEnabled ? "Protected — unlisted websites are blocked" : "Protection is off";
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
        if (!_state.AllowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase)) _state.AllowedDomains.Add(domain);
        DomainBox.Clear();
        _store.Save(_state);
        RefreshUi();
        if (_state.ProtectionEnabled) ApplyProtection();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DomainsList.SelectedItem is not string domain) return;
        _state.AllowedDomains.RemoveAll(x => string.Equals(x, domain, StringComparison.OrdinalIgnoreCase));
        _store.Save(_state);
        RefreshUi();
        if (_state.ProtectionEnabled) ApplyProtection();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e) => ApplyProtection();

    private void ApplyProtection()
    {
        try
        {
            _blocker.Apply(_state.AllowedDomains);
            _state.ProtectionEnabled = true;
            _store.Save(_state);
            RefreshUi();
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("SiteShield needs Administrator permission to modify Windows network settings. Please restart the app as Administrator.", "Administrator permission required", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not apply protection.\n\n{ex.Message}", "SiteShield", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DisableButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _blocker.Disable();
            _state.ProtectionEnabled = false;
            _store.Save(_state);
            RefreshUi();
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("Administrator permission is required to change the Windows hosts file.", "Administrator permission required", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string Normalize(string value)
    {
        var domain = value.Trim().ToLowerInvariant().Replace("https://", "").Replace("http://", "").Split('/')[0].Trim('.');
        if (domain.StartsWith("www.")) domain = domain[4..];
        return domain;
    }
}
