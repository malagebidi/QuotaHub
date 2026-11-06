using System;
using System.Runtime.InteropServices;

namespace QuotaHub.Services
{
    internal sealed class TrayIconService : IDisposable
    {
        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_GUID = 0x00000020;

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        private const uint LR_DEFAULTSIZE = 0x00000040;

        private const uint WM_APP = 0x8000;
        private const uint CallbackMessageId = WM_APP + 100;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_NULL = 0x0000;

        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;

        private const uint TPM_LEFTALIGN = 0x0000;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_BOTTOMALIGN = 0x0020;
        private const uint TPM_RETURNCMD = 0x0100;

        private const nuint MenuShowId = 1;
        private const nuint MenuExitId = 2;

        private const string WindowClassName = "QuotaHubTrayMessageWindow";
        private static readonly nint HWND_MESSAGE = new(-3);

        // Stable identity so Windows remembers the tray icon settings.
        private static readonly Guid TrayIconGuid = new("bac46dca-183e-4f36-92d6-780fe029306b");

        private const int MaxTipLength = 127;

        public event Action? LeftClick;
        public event Action? ShowRequested;
        public event Action? ExitRequested;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate nint WndProcDelegate(nint hWnd, uint uMsg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSW
        {
            public uint style;
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public uint cbSize;
            public nint hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public nint hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public nint hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint LoadImageW(nint hInstance, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(nint hIcon);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint GetModuleHandleW(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint DefWindowProcW(nint hWnd, uint uMsg, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool UnregisterClassW(string lpClassName, nint hInstance);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyMenu(nint hMenu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

        [DllImport("user32.dll")]
        private static extern bool PostMessageW(nint hWnd, uint uMsg, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(nint hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(nint hWnd);

        [DllImport("user32.dll")]
        private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private NOTIFYICONDATAW _data;
        private WndProcDelegate? _wndProc;
        private nint _hInstance;
        private nint _messageHwnd;
        private nint _iconHandle;
        private bool _classRegistered;
        private bool _added;
        private bool _disposed;

        public void Start(string iconPath, string tip)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TrayIconService));
            }

            if (_added)
            {
                return;
            }

            _hInstance = GetModuleHandleW(null);
            if (_hInstance == nint.Zero)
            {
                throw new InvalidOperationException("Failed to get module handle.");
            }

            _wndProc = OnWindowMessage;
            WNDCLASSW windowClass = new()
            {
                lpfnWndProc = _wndProc,
                hInstance = _hInstance,
                lpszClassName = WindowClassName,
            };

            if (RegisterClassW(ref windowClass) == 0)
            {
                throw new InvalidOperationException("Failed to register tray message window.");
            }

            _classRegistered = true;

            _messageHwnd = CreateWindowExW(0, WindowClassName, string.Empty, 0, 0, 0, 0, 0, HWND_MESSAGE, nint.Zero, _hInstance, nint.Zero);
            if (_messageHwnd == nint.Zero)
            {
                CleanupMessageWindow();
                throw new InvalidOperationException("Failed to create tray message window.");
            }

            _iconHandle = LoadImageW(nint.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (_iconHandle == nint.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                CleanupMessageWindow();
                throw new InvalidOperationException($"Failed to load tray icon. Error: {error}");
            }

            if (tip.Length > MaxTipLength)
            {
                tip = tip.Substring(0, MaxTipLength);
            }

            _data = new NOTIFYICONDATAW
            {
                hWnd = _messageHwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID,
                uCallbackMessage = CallbackMessageId,
                hIcon = _iconHandle,
                szTip = tip,
                szInfo = string.Empty,
                szInfoTitle = string.Empty,
                guidItem = TrayIconGuid,
            };
            _data.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>();

            if (!Shell_NotifyIconW(NIM_ADD, ref _data))
            {
                int error = Marshal.GetLastWin32Error();
                CleanupMessageWindow();
                DestroyIcon(_iconHandle);
                _iconHandle = nint.Zero;
                throw new InvalidOperationException($"Failed to add tray icon. Error: {error}");
            }

            _added = true;
        }

        public void Stop()
        {
            if (_added)
            {
                Shell_NotifyIconW(NIM_DELETE, ref _data);
                _added = false;
            }

            CleanupMessageWindow();

            if (_iconHandle != nint.Zero)
            {
                DestroyIcon(_iconHandle);
                _iconHandle = nint.Zero;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }

        private const int SW_RESTORE = 9;

        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const long WS_BORDER = 0x00800000L;
        private const long WS_DLGFRAME = 0x00400000L;
        private const long WS_THICKFRAME = 0x00040000L;
        private const long WS_EX_APPWINDOW = 0x00040000L;
        private const long WS_EX_TOOLWINDOW = 0x00000080L;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;

        /// <summary>
        /// Gets the rasterization scale for the monitor hosting the window.
        /// </summary>
        public static double GetWindowScaleFactor(nint hwnd)
        {
            if (hwnd == nint.Zero)
            {
                return 1.0;
            }

            uint dpi = GetDpiForWindow(hwnd);
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }

        /// <summary>
        /// Gets the cursor position in physical pixels.
        /// </summary>
        public static bool TryGetCursorPoint(out int x, out int y)
        {
            if (!GetCursorPos(out POINT position))
            {
                x = 0;
                y = 0;
                return false;
            }

            x = position.x;
            y = position.y;
            return true;
        }

        /// <summary>
        /// Gets the rasterization scale for the monitor nearest to a point.
        /// </summary>
        public static double GetScaleForPoint(int x, int y)
        {
            POINT point = new() { x = x, y = y };
            nint monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
            if (monitor == nint.Zero)
            {
                return 1.0;
            }

            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) != 0 || dpiX == 0)
            {
                return 1.0;
            }

            return dpiX / 96.0;
        }

        /// <summary>
        /// Strips the Win32 border and caption frame bits so DWM draws no border
        /// line around the panel. The XAML edge stroke and drop shadow stay.
        /// </summary>
        public static void RemoveWindowFrame(nint hwnd)
        {
            if (hwnd == nint.Zero)
            {
                return;
            }

            nint style = GetWindowLongPtrW(hwnd, GWL_STYLE);
            long updated = style.ToInt64() & ~(WS_BORDER | WS_DLGFRAME | WS_THICKFRAME);
            SetWindowLongPtrW(hwnd, GWL_STYLE, new nint(updated));
            SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Removes the taskbar button for the quota panel.
        /// The panel remains reachable through the tray icon.
        /// </summary>
        public static void HideFromTaskbar(nint hwnd)
        {
            if (hwnd == nint.Zero)
            {
                return;
            }

            nint exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
            long updated = exStyle.ToInt64() | WS_EX_TOOLWINDOW;
            updated &= ~WS_EX_APPWINDOW;
            SetWindowLongPtrW(hwnd, GWL_EXSTYLE, new nint(updated));
            SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Restores the window when minimized and moves it to the foreground.
        /// WinUI Activate alone is rejected while another app (for example the
        /// taskbar) owns the foreground, so the tray click path needs this.
        /// </summary>
        public static void EnsureVisibleAndForeground(nint hwnd)
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }

            SetForegroundWindow(hwnd);
        }

        private nint OnWindowMessage(nint hWnd, uint uMsg, nint wParam, nint lParam)
        {
            if (uMsg == CallbackMessageId)
            {
                uint mouseMessage = unchecked((uint)lParam.ToInt64());
                if (mouseMessage == WM_LBUTTONUP)
                {
                    LeftClick?.Invoke();
                }
                else if (mouseMessage == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                }

                return 0;
            }

            return DefWindowProcW(hWnd, uMsg, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            nint menu = CreatePopupMenu();
            if (menu == nint.Zero)
            {
                return;
            }

            try
            {
                AppendMenuW(menu, MF_STRING, MenuShowId, "Open QuotaHub");
                AppendMenuW(menu, MF_SEPARATOR, 0, null);
                AppendMenuW(menu, MF_STRING, MenuExitId, "Exit");

                if (!GetCursorPos(out POINT position))
                {
                    return;
                }

                SetForegroundWindow(_messageHwnd);
                uint selected = TrackPopupMenu(menu, TPM_LEFTALIGN | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_RETURNCMD, position.x, position.y, 0, _messageHwnd, nint.Zero);
                PostMessageW(_messageHwnd, WM_NULL, nint.Zero, nint.Zero);

                if (selected == (uint)MenuShowId)
                {
                    ShowRequested?.Invoke();
                }
                else if (selected == (uint)MenuExitId)
                {
                    ExitRequested?.Invoke();
                }
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        private void CleanupMessageWindow()
        {
            if (_messageHwnd != nint.Zero)
            {
                DestroyWindow(_messageHwnd);
                _messageHwnd = nint.Zero;
            }

            if (_classRegistered)
            {
                UnregisterClassW(WindowClassName, _hInstance);
                _classRegistered = false;
            }
        }
    }
}
