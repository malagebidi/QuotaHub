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
            _window = new MainWindow(settingsService, quotaService, quotaCacheService);
            _window.AppWindow.Resize(new SizeInt32(380, 560));
            string iconPath = AppPaths.GetIconPath();
            _window.AppWindow.SetIcon(iconPath);

            _trayIcon = new TrayIconService();
            _trayIcon.Start(iconPath, "QuotaHub");
            _trayIcon.LeftClick += OnTrayLeftClick;
            _trayIcon.ShowRequested += OnTrayShowRequested;
            _trayIcon.ExitRequested += OnTrayExitRequested;

            _window.AppWindow.Closing += OnWindowClosing;
            _window.Activated += OnWindowActivated;
            _window.Closed += OnWindowClosed;
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
            if (_window == null)
            {
                return;
            }

            nint hwnd = WindowNative.GetWindowHandle(_window);
            if (_window.AppWindow.IsVisible && !TrayIconService.IsMinimized(hwnd))
            {
                _window.AppWindow.Hide();
            }
            else
            {
                ShowPanel();
            }
        }

        private void OnTrayShowRequested()
        {
            ShowPanel();
        }

        private void ShowPanel()
        {
            if (_window == null)
            {
                return;
            }

            nint hwnd = WindowNative.GetWindowHandle(_window);
            _window.AppWindow.Show();
            TrayIconService.EnsureVisibleAndForeground(hwnd);
            _window.Activate();
        }

        private void OnTrayExitRequested()
        {
            _isExiting = true;
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
            _window?.AppWindow.Hide();
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                _window?.AppWindow.Hide();
            }
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
    }
}
