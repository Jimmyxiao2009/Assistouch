using System;
using System.Runtime.InteropServices;
using System.Text;
using AssistiveTouch.Models;

namespace AssistiveTouch.Win32;

internal static partial class NativeMethods
{
    // ── Window ─────────────────────────────────────────────────
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // ── Message / window class ────────────────────────────────
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ── Tray popup menu ───────────────────────────────────────
    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] public static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags,
        int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);

    // ── Shell_NotifyIcon ──────────────────────────────────────
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA pnid);

    // ── Process ───────────────────────────────────────────────
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint dwAccess, bool bInherit, uint dwPid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr hObject);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags,
        StringBuilder lpExeName, ref uint lpdwSize);

    // ── Input simulation ──────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] public static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);

    // ── Constants ─────────────────────────────────────────────
    public static readonly IntPtr HWND_TOPMOST   = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public const uint SWP_NOMOVE     = 0x0002;
    public const uint SWP_NOSIZE     = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOZORDER   = 0x0004;

    public const int GWL_EXSTYLE       = -20;
    public const int GWL_STYLE         = -16;
    public const int WS_CAPTION        = 0x00C00000;   // title bar + thin border
    public const int WS_THICKFRAME     = 0x00040000;   // resizable border
    public const int WS_EX_LAYERED     = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW  = 0x00000080;
    public const int WS_EX_NOACTIVATE  = 0x08000000;
    public const uint SWP_FRAMECHANGED = 0x0020;       // recalculate non-client area

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public const int INPUT_MOUSE    = 0;
    public const int INPUT_KEYBOARD = 1;

    public const uint KEYEVENTF_KEYUP       = 0x0002;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_UNICODE     = 0x0004;

    // Media / volume virtual keys
    public const ushort VK_MEDIA_NEXT_TRACK  = 0xB0;
    public const ushort VK_MEDIA_PREV_TRACK  = 0xB1;
    public const ushort VK_MEDIA_STOP        = 0xB2;
    public const ushort VK_MEDIA_PLAY_PAUSE  = 0xB3;
    public const ushort VK_VOLUME_MUTE       = 0xAD;
    public const ushort VK_VOLUME_DOWN       = 0xAE;
    public const ushort VK_VOLUME_UP         = 0xAF;

    public const uint MOUSEEVENTF_LEFTDOWN  = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP    = 0x0004;
    public const uint MOUSEEVENTF_MOVE      = 0x0001;
    public const uint MOUSEEVENTF_ABSOLUTE  = 0x8000;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000; // coordinates span the entire virtual desktop

    // Shell_NotifyIcon
    public const uint NIM_ADD    = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON    = 0x00000002;
    public const uint NIF_TIP     = 0x00000004;
    public const uint WM_TRAYICON = 0x8001;

    // Menu
    public const uint MF_STRING    = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_RETURNCMD  = 0x0100;
    public const uint TPM_RIGHTBUTTON = 0x0002;

    public const int TRAY_CMD_SETTINGS = 1001;
    public const int TRAY_CMD_EXIT     = 1002;

    public const uint WM_RBUTTONUP     = 0x0205;
    public const uint WM_LBUTTONUP     = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;

    // ── Structs ───────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASS
    {
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ── DWM blur (works regardless of window activation state) ──
    [DllImport("user32.dll")] private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);

    private enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }

    private enum AccentState
    {
        ACCENT_DISABLED                   = 0,
        ACCENT_ENABLE_GRADIENT            = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND          = 3,  // regular blur (Aero-style)
        ACCENT_ENABLE_ACRYLICBLURBEHIND   = 4,  // acrylic
        ACCENT_INVALID_STATE              = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;  // AABBGGRR
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttribData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    /// <summary>
    /// Applies or removes DWM blur/acrylic on <paramref name="hwnd"/> via
    /// SetWindowCompositionAttribute — works even when the window is not the foreground window.
    /// </summary>
    public static unsafe void ApplyDwmBlur(IntPtr hwnd, ButtonBackdropType backdrop)
    {
        var accent = new AccentPolicy();
        switch (backdrop)
        {
            case ButtonBackdropType.Acrylic:
                accent.AccentState  = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
                accent.AccentFlags  = 2;
                accent.GradientColor = 0x88303030; // stronger tint so acrylic is visible
                break;
            case ButtonBackdropType.Mica:
            case ButtonBackdropType.MicaAlt:
                // Mica is not available via SWCA; fall back to plain blur
                accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
                accent.AccentFlags = 2;
                break;
            default:
                accent.AccentState = AccentState.ACCENT_DISABLED;
                break;
        }

        var data = new WindowCompositionAttribData
        {
            Attribute  = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            Data       = (IntPtr)(&accent),
            SizeOfData = sizeof(AccentPolicy),
        };
        SetWindowCompositionAttribute(hwnd, ref data);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
