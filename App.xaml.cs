using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.Windows.AppLifecycle;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;
using QuotaHub.Services;
using System;
using System.IO;
using Windows.ApplicationModel;
using Windows.Graphics;
using WinRT.Interop;

namespace QuotaHub
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private TrayIconService? _trayIcon;
        private HotkeyService? _hotkeyService;
        private bool _isExiting;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            AppActivationArguments activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            AppInstance mainInstance = AppInstance.FindOrRegisterForKey("QuotaHubMain");
            if (!mainInstance.IsCurrent)
            {
                await mainInstance.RedirectActivationToAsync(activationArgs);
                Exit();
                return;
            }

            mainInstance.Activated -= OnAppInstanceActivated;
            mainInstance.Activated += OnAppInstanceActivated;

            if (_window != null)
            {
                ShowPanel();
                return;
            }

            SettingsService settingsService = new();
            QuotaService quotaService = new();
            QuotaCacheService quotaCacheService = new();
            _hotkeyService = new HotkeyService();
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _window = new MainWindow(settingsService, quotaService, quotaCacheService, _hotkeyService);
            SizeInt32 initialSize = new SizeInt32(380, 560);
            if (_window is MainWindow bootPanel)
            {
                initialSize = bootPanel.GetPhysicalSize();
            }

            _window.AppWindow.Resize(initialSize);
            string iconPath = AppPaths.GetIconPath();
            _window.AppWindow.SetIcon(iconPath);
            TrayIconService.HideFromTaskbar(WindowNative.GetWindowHandle(_window));
            TrayIconService.RemoveWindowFrame(WindowNative.GetWindowHandle(_window));
            if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }
            _trayIcon = new TrayIconService();
            _trayIcon.Start(iconPath, "QuotaHub");
            _trayIcon.LeftClick += OnTrayLeftClick;
            _trayIcon.ShowRequested += OnTrayShowRequested;
            _trayIcon.ExitRequested += OnTrayExitRequested;

            _window.AppWindow.Closing += OnWindowClosing;
            _window.Activated += OnWindowActivated;
            _window.Closed += OnWindowClosed;
            if (_window is MainWindow launchPanel)
            {
                _ = launchPanel.ShowAnimatedAsync();
            }

            _window.Activate();
        }

        private void OnAppInstanceActivated(object? sender, AppActivationArguments args)
        {
            if (_window == null)
            {
                return;
            }

            _window.DispatcherQueue.TryEnqueue(ShowPanel);
        }

        private void OnTrayLeftClick()
        {
            TogglePanel();
        }

        private void OnHotkeyPressed()
        {
            TogglePanel();
        }

        private async void TogglePanel()
        {
            if (_window is MainWindow panel)
            {
                if (panel.IsPanelVisible)
                {
                    await panel.HideAnimatedAsync();
                }
                else
                {
                    ShowPanel();
                }
            }
        }

        private void OnTrayShowRequested()
        {
            ShowPanel();
        }

        private void ShowPanel()
        {
            if (_window is MainWindow panel)
            {
                if (!panel.IsPanelVisible)
                {
                    _ = panel.ShowAnimatedAsync();
                }
                else
                {
                    panel.CancelPendingHide();
                    _window.AppWindow.Show();
                }

                nint hwnd = WindowNative.GetWindowHandle(_window);
                TrayIconService.EnsureVisibleAndForeground(hwnd);
                _window.Activate();
            }
        }

        private void OnTrayExitRequested()
        {
            _isExiting = true;
            _hotkeyService?.Dispose();
            _hotkeyService = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
            Exit();
        }

        private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isExiting)
            {
                return;
            }

            args.Cancel = true;
            if (_window is MainWindow closingPanel)
            {
                _ = closingPanel.HideAnimatedAsync();
            }
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                if (_window is MainWindow deactivatedPanel)
                {
                    _ = deactivatedPanel.HideAnimatedAsync();
                }
            }
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _hotkeyService?.Dispose();
            _hotkeyService = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
    }
}
