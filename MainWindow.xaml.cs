using Microsoft.UI;
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
        private AppSettings _settings = new();
        private bool _busy;
        private bool _showingSettings;

        public MainWindow(SettingsService settingsService, QuotaService quotaService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _quotaService = quotaService;
            Activated += OnFirstActivated;
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
            await RefreshAsync();
        }

        private async void OnRefreshClicked(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            _showingSettings = !_showingSettings;
            QuotaScroll.Visibility = _showingSettings ? Visibility.Collapsed : Visibility.Visible;
            SettingsPanel.Visibility = _showingSettings ? Visibility.Visible : Visibility.Collapsed;
            SettingsButton.Content = _showingSettings ? "Quota" : "Settings";
            RefreshButton.IsEnabled = !_showingSettings && !_busy;
        }

        private async void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            SaveButton.IsEnabled = false;
            try
            {
                _settings.OpenRouterApiKey = NormalizeKey(OpenRouterKeyBox.Password);
                _settings.DeepSeekApiKey = NormalizeKey(DeepSeekKeyBox.Password);
                await _settingsService.SaveAsync(_settings);
                SettingsStatus.Text = $"Saved at {DateTime.Now:HH:mm:ss}.";
                await RefreshAsync();
            }
            finally
            {
                _busy = false;
                SaveButton.IsEnabled = true;
            }
        }

        private async Task RefreshAsync()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            RefreshButton.IsEnabled = false;
            StatusBar.Text = "Refreshing...";
            try
            {
                IReadOnlyList<QuotaSnapshot> snapshots = await _quotaService.RefreshAsync(_settings, CancellationToken.None);
                RenderSnapshots(snapshots);
                int failed = snapshots.Count(s => s.ErrorMessage != null);
                StatusBar.Text = failed == 0
                    ? $"Updated {snapshots.Count} providers at {DateTime.Now:HH:mm:ss}."
                    : $"{failed} of {snapshots.Count} providers failed. Updated at {DateTime.Now:HH:mm:ss}.";
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                _busy = false;
                RefreshButton.IsEnabled = !_showingSettings;
            }
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
            body.Children.Add(new TextBlock
            {
                Text = $"Updated {first.UpdatedAt.LocalDateTime:HH:mm:ss}.",
                FontSize = 12,
                Foreground = new SolidColorBrush(hasError ? Colors.IndianRed : Colors.Gray),
            });
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
