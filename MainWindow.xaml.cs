using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using QuotaHub.Models;
using QuotaHub.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using WinRT.Interop;

namespace QuotaHub
{
    public sealed partial class MainWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly QuotaService _quotaService;
        private readonly QuotaCacheService _quotaCacheService;
        private readonly HotkeyService _hotkeyService;
        private AppSettings _settings = new();
        private bool _refreshing;
        private bool _saving;
        private bool _showingSettings;
        private readonly DispatcherQueueTimer _autoRefreshTimer;
        private readonly DispatcherQueueTimer _displayTimer;
        private readonly DispatcherQueueTimer _cooldownTimer;
        private DateTimeOffset _lastRefreshAttempt = DateTimeOffset.MinValue;
        private DateTimeOffset _lastManualRefreshFinish = DateTimeOffset.MinValue;
        private IReadOnlyList<QuotaSnapshot>? _lastSnapshots;
        private DateTimeOffset _lastCheckTime;
        private long _spinStartTimestamp;
        private bool _isSpinning;
        private bool _coolingDown;

        private static readonly TimeSpan ManualCooldown = TimeSpan.FromSeconds(5);
        private TimeSpan _autoRefreshInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan DisplayInterval = TimeSpan.FromSeconds(15);

        public MainWindow(SettingsService settingsService, QuotaService quotaService, QuotaCacheService quotaCacheService, HotkeyService hotkeyService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _quotaService = quotaService;
            _quotaCacheService = quotaCacheService;
            _hotkeyService = hotkeyService;
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
            _cooldownTimer = DispatcherQueue.CreateTimer();
            _cooldownTimer.IsRepeating = false;
            _cooldownTimer.Interval = ManualCooldown;
            _cooldownTimer.Tick += OnCooldownTick;
            UpdateEdgeStroke();
            RootGrid.ActualThemeChanged += OnActualThemeChanged;
        }

        private const int PanelEntranceDurationMs = 167;
        private const int PanelExitDurationMs = 167;
        private const int PanelWidth = 380;
        private const int PanelHeight = 560;
        private const int PanelMargin = 12;
        private const int DefaultTaskbarThickness = 48;
        private const int PanelParkedExtraPx = 4;
        private int _animationVersion;

        public bool IsPanelVisible { get; private set; }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdateEdgeStroke();
        }

        private void UpdateEdgeStroke()
        {
            bool isDark = RootGrid.ActualTheme == ElementTheme.Dark;
            RootGrid.BorderBrush = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(0xFF, 0x2E, 0x2E, 0x2E)
                : Windows.UI.Color.FromArgb(0xFF, 0xE5, 0xE5, 0xE5));
        }

        private async void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            await HideAnimatedAsync();
        }

        public async Task ShowAnimatedAsync()
        {
            if (IsPanelVisible)
            {
                return;
            }

            int version = ++_animationVersion;
            IsPanelVisible = true;
            UpdateButtonStates();
            var layout = GetCursorLayout();
            PointInt32 finalPosition = GetPanelPosition(layout);
            AppWindow.Resize(GetPhysicalSize(layout));
            AppWindow.Move(GetParkedPosition(layout));
            AppWindow.Show();
            await SlideWindowToAsync(finalPosition, PanelEntranceDurationMs, version, easeOut: true);
        }

        public async Task HideAnimatedAsync()
        {
            if (!IsPanelVisible)
            {
                return;
            }

            int version = ++_animationVersion;
            var layout = GetWindowLayout();
            await SlideWindowToAsync(GetParkedPosition(layout), PanelExitDurationMs, version, easeOut: false);
            if (version != _animationVersion)
            {
                return;
            }

            IsPanelVisible = false;
        }

        public void CancelPendingHide()
        {
            _animationVersion++;
            UpdateButtonStates();
            AppWindow.Move(GetPanelPosition(GetWindowLayout()));
        }

        private PointInt32 GetPanelPosition((RectInt32 WorkArea, RectInt32 OuterBounds, double Scale, TaskbarEdge Edge) layout)
        {
            int width = (int)Math.Round(PanelWidth * layout.Scale);
            int height = (int)Math.Round(PanelHeight * layout.Scale);
            int margin = (int)Math.Round(PanelMargin * layout.Scale);
            int defaultThickness = (int)Math.Round(DefaultTaskbarThickness * layout.Scale);
            RectInt32 outer = layout.OuterBounds;
            RectInt32 workArea = layout.WorkArea;

            return layout.Edge switch
            {
                TaskbarEdge.Top => new PointInt32(
                    outer.X + outer.Width - width - margin,
                    outer.Y + Math.Max(workArea.Y - outer.Y, defaultThickness) + margin),

                TaskbarEdge.Left => new PointInt32(
                    outer.X + Math.Max(workArea.X - outer.X, defaultThickness) + margin,
                    outer.Y + outer.Height - height - margin),

                TaskbarEdge.Right => new PointInt32(
                    outer.X + outer.Width - Math.Max((outer.X + outer.Width) - (workArea.X + workArea.Width), defaultThickness) - width - margin,
                    outer.Y + outer.Height - height - margin),

                TaskbarEdge.Bottom or _ => new PointInt32(
                    outer.X + outer.Width - width - margin,
                    outer.Y + outer.Height - Math.Max((outer.Y + outer.Height) - (workArea.Y + workArea.Height), defaultThickness) - height - margin),
            };
        }

        private PointInt32 GetParkedPosition((RectInt32 WorkArea, RectInt32 OuterBounds, double Scale, TaskbarEdge Edge) layout)
        {
            PointInt32 panel = GetPanelPosition(layout);
            int width = (int)Math.Round(PanelWidth * layout.Scale);
            int height = (int)Math.Round(PanelHeight * layout.Scale);
            RectInt32 outer = layout.OuterBounds;

            return layout.Edge switch
            {
                TaskbarEdge.Top => new PointInt32(
                    panel.X,
                    outer.Y - height - PanelParkedExtraPx),
                TaskbarEdge.Left => new PointInt32(
                    outer.X - width - PanelParkedExtraPx,
                    panel.Y),
                TaskbarEdge.Right => new PointInt32(
                    outer.X + outer.Width + PanelParkedExtraPx,
                    panel.Y),
                TaskbarEdge.Bottom or _ => new PointInt32(
                    panel.X,
                    outer.Y + outer.Height + PanelParkedExtraPx),
            };
        }

        public SizeInt32 GetPhysicalSize()
        {
            return GetPhysicalSize(GetCursorLayout());
        }

        private SizeInt32 GetPhysicalSize((RectInt32 WorkArea, RectInt32 OuterBounds, double Scale, TaskbarEdge Edge) layout)
        {
            return new SizeInt32(
                (int)Math.Round(PanelWidth * layout.Scale),
                (int)Math.Round(PanelHeight * layout.Scale));
        }

        private (RectInt32 WorkArea, RectInt32 OuterBounds, double Scale, TaskbarEdge Edge) GetCursorLayout()
        {
            if (TrayIconService.TryGetCursorPoint(out int x, out int y))
            {
                DisplayArea display = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Primary);
                TaskbarEdge edge = DetectTaskbarEdge(display.WorkArea, display.OuterBounds, x, y);
                return (display.WorkArea, display.OuterBounds, TrayIconService.GetScaleForPoint(x, y), edge);
            }

            return GetWindowLayout();
        }

        private (RectInt32 WorkArea, RectInt32 OuterBounds, double Scale, TaskbarEdge Edge) GetWindowLayout()
        {
            DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            TaskbarEdge edge = DetectTaskbarEdge(display.WorkArea, display.OuterBounds);
            return (display.WorkArea, display.OuterBounds, TrayIconService.GetWindowScaleFactor(WindowNative.GetWindowHandle(this)), edge);
        }

        private static TaskbarEdge DetectTaskbarEdge(RectInt32 workArea, RectInt32 outer, int cursorX = int.MinValue, int cursorY = int.MinValue)
        {
            int leftDiff = workArea.X - outer.X;
            int topDiff = workArea.Y - outer.Y;
            int rightDiff = (outer.X + outer.Width) - (workArea.X + workArea.Width);
            int bottomDiff = (outer.Y + outer.Height) - (workArea.Y + workArea.Height);

            int maxDiff = Math.Max(Math.Max(leftDiff, topDiff), Math.Max(rightDiff, bottomDiff));
            if (maxDiff > 10)
            {
                if (topDiff == maxDiff) return TaskbarEdge.Top;
                if (leftDiff == maxDiff) return TaskbarEdge.Left;
                if (rightDiff == maxDiff) return TaskbarEdge.Right;
                return TaskbarEdge.Bottom;
            }

            if (TrayIconService.TryGetTaskbarEdge(out TaskbarEdge edge))
            {
                return edge;
            }

            if (cursorX != int.MinValue && cursorY != int.MinValue)
            {
                int distLeft = cursorX - outer.X;
                int distTop = cursorY - outer.Y;
                int distRight = (outer.X + outer.Width) - cursorX;
                int distBottom = (outer.Y + outer.Height) - cursorY;

                int minDist = Math.Min(Math.Min(distLeft, distTop), Math.Min(distRight, distBottom));
                if (minDist == distTop) return TaskbarEdge.Top;
                if (minDist == distLeft) return TaskbarEdge.Left;
                if (minDist == distRight) return TaskbarEdge.Right;
                return TaskbarEdge.Bottom;
            }

            return TaskbarEdge.Bottom;
        }

        private async Task SlideWindowToAsync(PointInt32 to, int durationMs, int version, bool easeOut)
        {
            PointInt32 from = AppWindow.Position;
            long start = Stopwatch.GetTimestamp();
            while (true)
            {
                if (version != _animationVersion)
                {
                    return;
                }

                double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                double progress = Math.Min(1.0, elapsedMs / durationMs);
                double eased = easeOut
                    ? 1.0 - (1.0 - progress) * (1.0 - progress) * (1.0 - progress)
                    : progress * progress * progress;
                int x = from.X + (int)Math.Round((to.X - from.X) * eased);
                int y = from.Y + (int)Math.Round((to.Y - from.Y) * eased);
                AppWindow.Move(new PointInt32(x, y));
                if (progress >= 1.0)
                {
                    break;
                }

                await Task.Delay(8);
            }
        }

        private async void OnFirstActivated(object sender, WindowActivatedEventArgs e)
        {
            Activated -= OnFirstActivated;
            _settings = await _settingsService.LoadAsync();
            OpenRouterKeyBox.Password = _settings.OpenRouterApiKey ?? string.Empty;
            DeepSeekKeyBox.Password = _settings.DeepSeekApiKey ?? string.Empty;
            SelectAutoRefreshItem(_settings.AutoRefreshMinutes);
            SelectShortcutItem(_settings.Shortcut);
            ApplyIntervalSettings();
            _hotkeyService.Register(_settings.Shortcut);

            IReadOnlyList<QuotaSnapshot> cached = await _quotaCacheService.LoadAsync();
            if (cached.Count > 0)
            {
                _lastSnapshots = cached;
                _lastCheckTime = cached.Max(static s => s.UpdatedAt);
                RenderSnapshots(cached);
                UpdateStatusBarText();
            }

            await RefreshAsync(bypassCooldown: true, animate: false);
        }

        private async void OnRefreshClicked(object sender, RoutedEventArgs e)
        {
            await RefreshAsync(animate: true);
        }

        private async void OnAutoRefreshTick(object sender, object e)
        {
            await RefreshAsync(isAuto: true, animate: false);
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            _autoRefreshTimer.Stop();
            _displayTimer.Stop();
            _cooldownTimer.Stop();
        }

        private void OnCooldownTick(DispatcherQueueTimer sender, object args)
        {
            _cooldownTimer.Stop();
            _coolingDown = false;
            UpdateButtonStates();
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            _showingSettings = !_showingSettings;
            QuotaScroll.Visibility = _showingSettings ? Visibility.Collapsed : Visibility.Visible;
            SettingsScroll.Visibility = _showingSettings ? Visibility.Visible : Visibility.Collapsed;
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
                _settings.Shortcut = ReadShortcut();
                await _settingsService.SaveAsync(_settings);
                SelectAutoRefreshItem(_settings.AutoRefreshMinutes);
                SelectShortcutItem(_settings.Shortcut);
                ApplyIntervalSettings();

                bool registered = _hotkeyService.Register(_settings.Shortcut);
                if (!registered && !string.Equals(_settings.Shortcut, "None", StringComparison.OrdinalIgnoreCase))
                {
                    SettingsStatus.Text = $"Saved at {DateTime.Now:HH:mm:ss} (shortcut conflict).";
                }
                else
                {
                    SettingsStatus.Text = $"Saved at {DateTime.Now:HH:mm:ss}.";
                }
            }
            finally
            {
                _saving = false;
                UpdateButtonStates();
            }

            await RefreshAsync(bypassCooldown: true, animate: false);
        }

        private async Task RefreshAsync(CancellationToken cancellationToken = default, bool bypassCooldown = false, bool isAuto = false, bool animate = false)
        {
            if (_refreshing || _saving)
            {
                return;
            }

            if (!bypassCooldown)
            {
                TimeSpan required = isAuto ? _autoRefreshInterval : ManualCooldown;
                DateTimeOffset referenceTime = isAuto ? _lastRefreshAttempt : _lastManualRefreshFinish;
                TimeSpan since = DateTimeOffset.Now - referenceTime;
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
            if (animate)
            {
                StartRefreshSpin();
            }
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
                try
                {
                    if (animate)
                    {
                        await StopRefreshSpinAsync();
                    }
                }
                finally
                {
                    _refreshing = false;
                    if (animate)
                    {
                        _coolingDown = true;
                        _lastManualRefreshFinish = DateTimeOffset.Now;
                        _cooldownTimer.Stop();
                        _cooldownTimer.Start();
                    }
                    UpdateButtonStates();
                }
            }
        }

        private void UpdateButtonStates()
        {
            if (_coolingDown && (DateTimeOffset.Now - _lastManualRefreshFinish >= ManualCooldown))
            {
                _coolingDown = false;
                _cooldownTimer.Stop();
            }

            RefreshButton.IsEnabled = !_showingSettings && !_refreshing && !_saving && !_coolingDown;
            SaveButton.IsEnabled = !_saving && !_refreshing;
        }

        private void StartRefreshSpin()
        {
            _isSpinning = true;
            _spinStartTimestamp = Stopwatch.GetTimestamp();

            Visual visual = ElementCompositionPreview.GetElementVisual(RefreshIconContainer);
            visual.CenterPoint = new Vector3(8f, 8f, 0f);
            Compositor compositor = visual.Compositor;
            LinearEasingFunction linear = compositor.CreateLinearEasingFunction();
            ScalarKeyFrameAnimation spin = compositor.CreateScalarKeyFrameAnimation();
            spin.InsertKeyFrame(0f, 0f, linear);
            spin.InsertKeyFrame(1f, 360f, linear);
            spin.Duration = TimeSpan.FromSeconds(1);
            spin.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("RotationAngleInDegrees", spin);
        }

        private async Task StopRefreshSpinAsync()
        {
            if (!_isSpinning)
            {
                return;
            }

            _isSpinning = false;
            double elapsedSeconds = (Stopwatch.GetTimestamp() - _spinStartTimestamp) / (double)Stopwatch.Frequency;
            float currentAngle = (float)((elapsedSeconds * 360.0) % 360.0);
            float remainingAngle = 360f - currentAngle;
            float targetAngle = remainingAngle < 90f ? 720f : 360f;
            float delta = targetAngle - currentAngle;
            int durationMs = (int)Math.Clamp(delta / 360f * 400f, 200f, 400f);

            Visual visual = ElementCompositionPreview.GetElementVisual(RefreshIconContainer);
            Compositor compositor = visual.Compositor;
            CompositionEasingFunction easeOut = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.1f, 0.9f),
                new Vector2(0.2f, 1f));

            ScalarKeyFrameAnimation settle = compositor.CreateScalarKeyFrameAnimation();
            settle.InsertKeyFrame(0f, currentAngle);
            settle.InsertKeyFrame(1f, targetAngle, easeOut);
            settle.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation("RotationAngleInDegrees", settle);

            await Task.Delay(durationMs);

            visual.StopAnimation("RotationAngleInDegrees");
            visual.RotationAngleInDegrees = 0f;
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

        private void SelectShortcutItem(string shortcut)
        {
            int selected = 0;
            bool found = false;

            for (int i = 0; i < ShortcutBox.Items.Count; i++)
            {
                if (ShortcutBox.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), shortcut, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                    found = true;
                    break;
                }
            }

            if (!found && !string.IsNullOrWhiteSpace(shortcut))
            {
                ComboBoxItem customItem = new()
                {
                    Content = shortcut,
                    Tag = shortcut,
                };
                ShortcutBox.Items.Add(customItem);
                selected = ShortcutBox.Items.Count - 1;
            }

            ShortcutBox.SelectedIndex = selected;
        }

        private string ReadShortcut()
        {
            if (ShortcutBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                return item.Tag.ToString() ?? "Alt+Q";
            }

            return "Alt+Q";
        }

        private void OnDisplayTick(object sender, object e)
        {
            UpdateButtonStates();
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
            return value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "?";
        }

        private static string? NormalizeKey(string password)
        {
            return string.IsNullOrWhiteSpace(password) ? null : password.Trim();
        }
    }
}
