using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuotaHub.Services
{
    public sealed class HotkeyService : IDisposable
    {
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        private const uint WM_HOTKEY = 0x0312;
        private const int HotkeyId = 9001;
        private const string WindowClassName = "QuotaHubHotkeyMessageWindow";
        private static readonly nint HWND_MESSAGE = new(-3);

        public event Action? HotkeyPressed;

        public bool IsRegistered { get; private set; }
        public string? CurrentHotkey { get; private set; }

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(nint hWnd, int id);

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

        private WndProcDelegate? _wndProc;
        private nint _hInstance;
        private nint _messageHwnd;
        private bool _classRegistered;
        private bool _disposed;

        public bool Register(string? hotkeyString)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString) ||
                string.Equals(hotkeyString, "None", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hotkeyString, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                Unregister();
                CurrentHotkey = "None";
                return true;
            }

            if (!TryParse(hotkeyString, out uint modifiers, out uint vk))
            {
                Debug.WriteLine($"Invalid hotkey string: '{hotkeyString}'");
                return false;
            }

            bool success = Register(modifiers, vk);
            if (success)
            {
                CurrentHotkey = hotkeyString;
            }

            return success;
        }

        public bool Register(uint modifiers = MOD_ALT | MOD_NOREPEAT, uint vk = (uint)'Q')
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HotkeyService));
            }

            EnsureMessageWindow();

            if (IsRegistered)
            {
                Unregister();
            }

            bool success = RegisterHotKey(_messageHwnd, HotkeyId, modifiers, vk);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"Failed to register global hotkey (modifiers=0x{modifiers:X}, vk=0x{vk:X}). Error code: {error}");
                IsRegistered = false;
                return false;
            }

            IsRegistered = true;
            return true;
        }

        public void Unregister()
        {
            if (IsRegistered && _messageHwnd != nint.Zero)
            {
                UnregisterHotKey(_messageHwnd, HotkeyId);
                IsRegistered = false;
            }
        }

        public static bool TryParse(string? text, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;

            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(text, "None", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            uint mods = MOD_NOREPEAT;
            uint key = 0;

            foreach (string part in parts)
            {
                string p = part.Trim();
                if (string.Equals(p, "Alt", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= MOD_ALT;
                }
                else if (string.Equals(p, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(p, "Control", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= MOD_CONTROL;
                }
                else if (string.Equals(p, "Shift", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= MOD_SHIFT;
                }
                else if (string.Equals(p, "Win", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(p, "Windows", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= MOD_WIN;
                }
                else if (string.Equals(p, "Space", StringComparison.OrdinalIgnoreCase))
                {
                    key = 0x20;
                }
                else if (p.Length == 1 && char.IsLetterOrDigit(p[0]))
                {
                    key = char.ToUpperInvariant(p[0]);
                }
                else if (p.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(p.Substring(1), out int fNum) && fNum >= 1 && fNum <= 12)
                {
                    key = (uint)(0x70 + (fNum - 1));
                }
                else
                {
                    return false;
                }
            }

            if (key == 0 || (mods & (MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_WIN)) == 0)
            {
                return false;
            }

            modifiers = mods;
            vk = key;
            return true;
        }

        private void EnsureMessageWindow()
        {
            if (_messageHwnd != nint.Zero)
            {
                return;
            }

            _hInstance = GetModuleHandleW(null);
            if (_hInstance == nint.Zero)
            {
                throw new InvalidOperationException("Failed to get module handle for hotkey window.");
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
                int error = Marshal.GetLastWin32Error();
                // 1410 = ERROR_CLASS_ALREADY_EXISTS
                if (error != 1410)
                {
                    throw new InvalidOperationException($"Failed to register hotkey window class. Error: {error}");
                }
            }

            _classRegistered = true;

            _messageHwnd = CreateWindowExW(0, WindowClassName, string.Empty, 0, 0, 0, 0, 0, HWND_MESSAGE, nint.Zero, _hInstance, nint.Zero);
            if (_messageHwnd == nint.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                CleanupMessageWindow();
                throw new InvalidOperationException($"Failed to create hotkey message window. Error: {error}");
            }
        }

        private nint OnWindowMessage(nint hWnd, uint uMsg, nint wParam, nint lParam)
        {
            if (uMsg == WM_HOTKEY)
            {
                if (wParam.ToInt32() == HotkeyId)
                {
                    HotkeyPressed?.Invoke();
                    return 0;
                }
            }

            return DefWindowProcW(hWnd, uMsg, wParam, lParam);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Unregister();
                CleanupMessageWindow();
                _disposed = true;
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
