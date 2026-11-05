using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
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
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            SettingsService settingsService = new();
            QuotaService quotaService = new();
            _window = new MainWindow(settingsService, quotaService);
            _window.AppWindow.Resize(new SizeInt32(380, 560));

            string iconPath = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "TrayIcon.ico");

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
