using System;
using System.Runtime.InteropServices;
using AssistiveTouch.Win32;

namespace AssistiveTouch.Services;

/// <summary>
/// Win32 system-tray icon.
/// Left-click  → raises <see cref="MenuRequested"/> (shown as WinUI popup).
/// Right-click → ignored.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    public event Action? MenuRequested;

    private IntPtr _hwnd;
    private NativeMethods.WndProcDelegate? _wndProc; // keep alive to prevent GC
    private bool _iconAdded;

    public TrayIconService()
    {
        CreateMessageWindow();
        AddTrayIcon();
    }

    // ── Hidden message window ─────────────────────────────────
    private void CreateMessageWindow()
    {
        const string className = "AssisTouchTrayWnd";
        _wndProc = WndProc;

        var hInst = NativeMethods.GetModuleHandle(null);
        var wc = new NativeMethods.WNDCLASS
        {
            lpfnWndProc   = _wndProc,
            hInstance     = hInst,
            lpszClassName = className
        };
        NativeMethods.RegisterClass(ref wc);

        _hwnd = NativeMethods.CreateWindowEx(
            0, className, "TrayHost",
            0, 0, 0, 0, 0,
            new IntPtr(-3), // HWND_MESSAGE
            IntPtr.Zero, hInst, IntPtr.Zero);
    }

    // ── Tray icon ─────────────────────────────────────────────
    private static IntPtr LoadAppIcon()
    {
        // Primary: extract the first icon embedded in the running exe.
        // <ApplicationIcon> compiles the .ico into the exe's Win32 resources,
        // so this works identically in packaged (MSIX) and unpackaged contexts.
        string exePath = System.Diagnostics.Process.GetCurrentProcess()
                             .MainModule?.FileName ?? string.Empty;
        if (!string.IsNullOrEmpty(exePath))
        {
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            if (ExtractIconEx(exePath, 0, large, small, 1) > 0 && large[0] != IntPtr.Zero)
                return large[0];
        }

        // Fallback: loose .ico file (unpackaged / dev run without embedded icon).
        string iconPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
            return LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0,
                             LR_LOADFROMFILE | LR_DEFAULTSIZE);

        // Last resort: OS default application icon.
        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    private void AddTrayIcon()
    {
        var hIcon = LoadAppIcon();

        var nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize           = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd             = _hwnd,
            uID              = 1,
            uFlags           = NativeMethods.NIF_ICON | NativeMethods.NIF_MESSAGE | NativeMethods.NIF_TIP,
            uCallbackMessage = NativeMethods.WM_TRAYICON,
            hIcon            = hIcon,
            szTip            = "AssisTouch",
            szInfo           = string.Empty,
            szInfoTitle      = string.Empty
        };
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref nid);
        _iconAdded = true;
    }

    private void RemoveTrayIcon()
    {
        if (!_iconAdded) return;
        var nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize      = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd        = _hwnd,
            uID         = 1,
            szTip       = string.Empty,
            szInfo      = string.Empty,
            szInfoTitle = string.Empty
        };
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref nid);
        _iconAdded = false;
    }

    // ── Window procedure ──────────────────────────────────────
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_TRAYICON)
        {
            uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == NativeMethods.WM_LBUTTONUP)
                MenuRequested?.Invoke();
            // WM_RBUTTONUP → intentionally ignored
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // ── Cleanup ───────────────────────────────────────────────
    public void Dispose()
    {
        RemoveTrayIcon();
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType,
        int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex,
        IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

    private const uint IMAGE_ICON      = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE  = 0x0040;
}
