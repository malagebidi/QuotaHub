using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using QuotaHub.Models;
using QuotaHub.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuotaHub
{
    public sealed partial class MainWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly QuotaService _quotaService;
        private readonly QuotaCacheService _quotaCacheService;
        private AppSettings _settings = new();
        private bool _refreshing;
        private bool _saving;
        private bool _showingSettings;
        private readonly DispatcherQueueTimer _autoRefreshTimer;
        private readonly DispatcherQueueTimer _displayTimer;
        private DateTimeOffset _lastRefreshAttempt = DateTimeOffset.MinValue;
        private IReadOnlyList<QuotaSnapshot>? _lastSnapshots;
        private DateTimeOffset _lastCheckTime;

        private static readonly TimeSpan ManualCooldown = TimeSpan.FromSeconds(30);
        private TimeSpan _autoRefreshInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan DisplayInterval = TimeSpan.FromSeconds(15);

        public MainWindow(SettingsService settingsService, QuotaService quotaService, QuotaCacheService quotaCacheService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _quotaService = quotaService;
            _quotaCacheService = quotaCacheService;
            Activated += OnFirstActivated;
            _autoRefreshTimer = DispatcherQueue.CreateTimer();
            _autoRefreshTimer.Interval = _autoRefreshInterval;
            _autoRefreshTimer.Tick += OnAutoRefreshTick;
            Closed += OnClosed;
            _autoRefreshTimer.Start();
            _displayTimer = DispatcherQueue.CreateTimer();
            _displayTimer.Interval = DisplayInterval;
            _displayTimer.Tick += OnDisplayTick;
            _displayTimer.Start();
        }

        private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            AppWindow.Hide();
            args.Handled = true;
        }

        private async void OnFirstActivated(object sender, WindowActivatedEventArgs e)
        {
            Activated -= OnFirstActivated;
            _settings = await _settingsService.LoadAsync();
            OpenRouterKeyBox.Password = _settings.OpenRouterApiKey ?? string.Empty;
            DeepSeekKeyBox.Password = _settings.DeepSeekApiKey ?? string.Empty;
            SelectAutoRefreshItem(_settings.AutoRefreshMinutes);
            ApplyIntervalSettings();

            IReadOnlyList<QuotaSnapshot> cached = await _quotaCacheService.LoadAsync();
            if (cached.Count > 0)
            {
                _lastSnapshots = cached;
                _lastCheckTime = cached.Max(static s => s.UpdatedAt);
                RenderSnapshots(cached);
                UpdateStatusBarText();
            }

            await RefreshAsync(bypassCooldown: true);
        }

        private async void OnRefreshClicked(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async void OnAutoRefreshTick(object sender, object e)
        {
            await RefreshAsync(isAuto: true);
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            _autoRefreshTimer.Stop();
            _displayTimer.Stop();
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            _showingSettings = !_showingSettings;
            QuotaScroll.Visibility = _showingSettings ? Visibility.Collapsed : Visibility.Visible;
            SettingsPanel.Visibility = _showingSettings ? Visibility.Visible : Visibility.Collapsed;
            SettingsButton.Content = new FontIcon { FontSize = 12, Glyph = _showingSettings ? "\uE72B" : "\uE713" };
            ToolTipService.SetToolTip(SettingsButton, _showingSettings ? "Back to quotas" : "Settings");
            UpdateButtonStates();
        }

        private async void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            if (_saving || _refreshing)
            {
                return;
            }

            _saving = true;
            UpdateButtonStates();
            try
            {
                _settings.OpenRouterApiKey = NormalizeKey(OpenRouterKeyBox.Password);
                _settings.DeepSeekApiKey = NormalizeKey(DeepSeekKeyBox.Password);
                _settings.AutoRefreshMinutes = ReadAutoRefreshMinutes();
                await _settingsService.SaveAsync(_settings);
                SelectAutoRefreshItem(_settings.AutoRefreshMinutes);
                ApplyIntervalSettings();
                SettingsStatus.Text = $"Saved at {DateTime.Now:HH:mm:ss}.";
            }
            finally
            {
                _saving = false;
                UpdateButtonStates();
            }

            await RefreshAsync(bypassCooldown: true);
        }

        private async Task RefreshAsync(CancellationToken cancellationToken = default, bool bypassCooldown = false, bool isAuto = false)
        {
            if (_refreshing || _saving)
            {
                return;
            }

            if (!bypassCooldown)
            {
                TimeSpan required = isAuto ? _autoRefreshInterval : ManualCooldown;
                TimeSpan since = DateTimeOffset.Now - _lastRefreshAttempt;
                if (since < required)
                {
                    if (!isAuto)
                    {
                        int waitSeconds = (int)Math.Ceiling((required - since).TotalSeconds);
                        StatusBar.Text = $"Refreshed recently. Please wait {waitSeconds}s.";
                    }

                    return;
                }
            }

            _refreshing = true;
            _lastRefreshAttempt = DateTimeOffset.Now;
            UpdateButtonStates();
            StatusBar.Text = "Refreshing...";
            try
            {
                IReadOnlyList<QuotaSnapshot> snapshots = await _quotaService.RefreshAsync(_settings, cancellationToken);
                _lastSnapshots = snapshots;
                _lastCheckTime = DateTimeOffset.Now;
                RenderSnapshots(snapshots);
                UpdateStatusBarText();
                await _quotaCacheService.SaveAsync(snapshots);
            }
            catch (OperationCanceledException)
            {
                StatusBar.Text = "Refresh canceled.";
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                _refreshing = false;
                UpdateButtonStates();
            }
        }

        private void UpdateButtonStates()
        {
            RefreshButton.IsEnabled = !_showingSettings && !_refreshing && !_saving;
            SaveButton.IsEnabled = !_saving && !_refreshing;
        }

        private void ApplyIntervalSettings()
        {
            _autoRefreshInterval = TimeSpan.FromMinutes(_settings.AutoRefreshMinutes);
            _autoRefreshTimer.Interval = _autoRefreshInterval;
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Start();
        }

        private void SelectAutoRefreshItem(int minutes)
        {
            int selected = 1;
            for (int i = 0; i < AutoRefreshBox.Items.Count; i++)
            {
                if (AutoRefreshBox.Items[i] is ComboBoxItem item &&
                    int.TryParse(item.Tag?.ToString(), out int value) &&
                    value == minutes)
                {
                    selected = i;
                    break;
                }
            }

            AutoRefreshBox.SelectedIndex = selected;
        }

        private int ReadAutoRefreshMinutes()
        {
            if (AutoRefreshBox.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out int value))
            {
                return Math.Clamp(value, 1, 120);
            }

            return 5;
        }

        private void OnDisplayTick(object sender, object e)
        {
            if (_lastSnapshots == null || _refreshing)
            {
                return;
            }

            RenderSnapshots(_lastSnapshots);
            UpdateStatusBarText();
        }

        private void UpdateStatusBarText()
        {
            if (_lastSnapshots == null)
            {
                return;
            }

            int providerCount = _lastSnapshots.Select(static s => s.ProviderId).Distinct().Count();
            int failedProviders = _lastSnapshots.GroupBy(static s => s.ProviderId).Count(static g => g.Any(static s => s.ErrorMessage != null));
            string age = FormatRelative(_lastCheckTime);
            StatusBar.Text = failedProviders == 0
                ? $"Updated {providerCount} providers {age}."
                : $"{failedProviders} of {providerCount} providers failed {age}.";
        }

        private static string FormatRelative(DateTimeOffset time)
        {
            TimeSpan age = DateTimeOffset.Now - time;
            if (age.TotalSeconds < 10)
            {
                return "just now";
            }

            if (age.TotalSeconds < 60)
            {
                return $"{(int)age.TotalSeconds}s ago";
            }

            if (age.TotalMinutes < 60)
            {
                int minutes = (int)age.TotalMinutes;
                return minutes == 1 ? "1 min ago" : $"{minutes} min ago";
            }

            return $"at {time.LocalDateTime:HH:mm:ss}";
        }

        private void RenderSnapshots(IReadOnlyList<QuotaSnapshot> snapshots)
        {
            QuotaList.Children.Clear();
            foreach (IGrouping<string, QuotaSnapshot> group in snapshots.GroupBy(static s => s.ProviderId))
            {
                QuotaList.Children.Add(BuildCard(group.ToList()));
            }
        }

        private static Border BuildCard(IReadOnlyList<QuotaSnapshot> snapshots)
        {
            QuotaSnapshot first = snapshots[0];
            StackPanel body = new() { Spacing = 2 };
            body.Children.Add(new TextBlock
            {
                Text = first.DisplayName,
                FontWeight = FontWeights.SemiBold,
            });
            foreach (QuotaSnapshot snapshot in snapshots)
            {
                body.Children.Add(new TextBlock
                {
                    Text = FormatDetail(snapshot),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            bool hasError = snapshots.Any(static s => s.ErrorMessage != null);
            DateTimeOffset latest = snapshots.Max(static s => s.UpdatedAt);
            TextBlock timestamp = new()
            {
                Text = $"Updated {FormatRelative(latest)}.",
                FontSize = 12,
                Foreground = new SolidColorBrush(hasError ? Colors.IndianRed : Colors.Gray),
            };
            ToolTipService.SetToolTip(timestamp, latest.LocalDateTime.ToString("HH:mm:ss"));
            body.Children.Add(timestamp);
            return new Border
            {
                Child = body,
                Padding = new Thickness(10),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Colors.Gray),
            };
        }

        private static string FormatDetail(QuotaSnapshot snapshot)
        {
            if (snapshot.ErrorMessage != null)
            {
                return snapshot.ErrorMessage;
            }

            if (snapshot.Remaining != null)
            {
                return $"Remaining {FormatNumber(snapshot.Remaining)} {snapshot.Unit}";
            }

            if (snapshot.Total != null)
            {
                return $"Used {FormatNumber(snapshot.Used)} / {FormatNumber(snapshot.Total)} {snapshot.Unit}";
            }

            if (snapshot.Used != null)
            {
                return $"Used {FormatNumber(snapshot.Used)} {snapshot.Unit}";
            }

            return "No data.";
        }

        private static string FormatNumber(double? value)
        {
            return value?.ToString("0.####") ?? "?";
        }

        private static string? NormalizeKey(string password)
        {
            return string.IsNullOrWhiteSpace(password) ? null : password.Trim();
        }
    }
}
