using System;
using System.Runtime.InteropServices;
using AssistiveTouch.Models;
using AssistiveTouch.Win32;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace AssistiveTouch;

public sealed partial class FloatingButtonWindow : Window
{
    // ── Win32 ─────────────────────────────────────────────────
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern int  GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int  SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int  SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("gdi32.dll")]  static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("dwmapi.dll")] static extern int  DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref int value, uint size);

    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOMOVE       = 0x0002;
    const uint SWP_NOSIZE       = 0x0001;
    const uint SWP_NOACTIVATE   = 0x0010;
    const uint SWP_NOZORDER     = 0x0004;
    const uint SWP_FRAMECHANGED = 0x0020;
    const int  GWL_STYLE        = -16;
    const int  GWL_EXSTYLE      = -20;
    const int  WS_CAPTION       = 0x00C00000;
    const int  WS_THICKFRAME    = 0x00040000;
    const int  WS_EX_TOOLWINDOW = 0x00000080;

    // DWM
    const uint DWMWA_USE_IMMERSIVE_DARK_MODE  = 20;
    const uint DWMWA_BORDER_COLOR             = 34;
    const uint DWMWA_CAPTION_COLOR            = 35;
    const uint DWMWA_COLOR_NONE               = 0xFFFFFFFE;

    // ── State ─────────────────────────────────────────────────
    private readonly IntPtr _hwnd;
    private int _size; // logical px

    private bool _ptrDown;
    private bool _dragging;
    private int  _dragOffX, _dragOffY;
    private int  _pressCursorX, _pressCursorY;
    private bool _menuWasOpenOnPress;
    private bool _suppressMenuOnRelease;
    private DateTimeOffset _lastMenuClosedAt = DateTimeOffset.MinValue;
    private const int DragThreshold = 6;
    private static readonly TimeSpan MenuReopenGuard = TimeSpan.FromMilliseconds(150);

    private ActionMenuWindow? _menu;
    private Services.ForegroundWindowInfo? _pendingFg;

    // ── Constructor ───────────────────────────────────────────
    public FloatingButtonWindow()
    {
        InitializeComponent();

        ApplyThemeToContent();

        _size = App.Config.Config.ButtonSize;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop  = true;
        presenter.IsResizable    = false;
        presenter.IsMinimizable  = false;
        presenter.IsMaximizable  = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);

        // Strip Win32 caption/frame
        int style = GetWindowLong(_hwnd, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME);
        SetWindowLong(_hwnd, GWL_STYLE, style);

        // Hide from taskbar/alt-tab, stay topmost — commit frame change FIRST
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        // Apply DWM attributes AFTER frame change
        ApplyDwmAttributes();

        // Resize to square AFTER all style/frame changes are committed
        int sz = ToPhysical(_size);
        AppWindow.Resize(new SizeInt32(sz, sz));

        // Clip HWND to rounded rect — eliminates any DWM border outside the region
        ApplyWindowRegion(sz);

        RestorePosition();
        Closed += (_, _) => App.Config.Save();

        Activated += OnFirstActivated;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        ApplyBackdrop();
    }

    // ── DWM ───────────────────────────────────────────────────
    private void ApplyDwmAttributes()
    {
        bool isDark = App.Config.Config.ThemeMode == ThemeMode.Dark;
        int darkVal = isDark ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));

        int noBorder = unchecked((int)DWMWA_COLOR_NONE);
        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR,  ref noBorder, sizeof(int));
        DwmSetWindowAttribute(_hwnd, DWMWA_CAPTION_COLOR, ref noBorder, sizeof(int));
    }

    private void ApplyWindowRegion(int physSz)
    {
        int r   = ToPhysical(8) * 2;
        // 2.5 logical px → physical, rounded away from zero
        uint dpi = GetDpiForWindow(_hwnd);
        int pad  = (int)Math.Round(2.5 * (dpi > 0 ? dpi : 96u) / 96.0,
                                   MidpointRounding.AwayFromZero);
        var rgn  = CreateRoundRectRgn(pad, pad, physSz - pad + 1, physSz - pad + 1, r, r);
        SetWindowRgn(_hwnd, rgn, true);
    }

    // ── Backdrop ──────────────────────────────────────────────
    public void ApplyBackdrop()
    {
        SystemBackdrop = App.Config.Config.ButtonBackdrop switch
        {
            ButtonBackdropType.Acrylic => new DesktopAcrylicBackdrop(),
            ButtonBackdropType.Mica    => new MicaBackdrop(),
            ButtonBackdropType.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            _                          => null,
        };

        ApplyThemeToContent();
        ApplyDwmAttributes();
        ApplyFallbackBackground();
    }

    /// <summary>
    /// When no system backdrop is used the HWND client area is fully opaque
    /// (white in light mode, near-black in dark mode).  Apply a visible
    /// semi-transparent brush to ButtonCard so the button still looks good.
    /// When a real backdrop is active, keep ButtonCard transparent so the
    /// Acrylic / Mica shows through.
    /// </summary>
    private void ApplyFallbackBackground()
    {
        if (ButtonCard is not Border card) return;

        if (App.Config.Config.ButtonBackdrop == ButtonBackdropType.None)
        {
            bool isDark = App.Config.Config.ThemeMode == ThemeMode.Dark ||
                          (App.Config.Config.ThemeMode == ThemeMode.System &&
                           Application.Current.RequestedTheme == ApplicationTheme.Dark);

            // 60 % opacity so the dots are still visible but the button has shape
            card.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                isDark
                    ? Windows.UI.Color.FromArgb(0x99, 0x30, 0x30, 0x30)   // dark: #99303030
                    : Windows.UI.Color.FromArgb(0x99, 0xF3, 0xF3, 0xF3)); // light: #99F3F3F3
        }
        else
        {
            card.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0, 0, 0, 0)); // fully transparent
        }
    }

    private void ApplyThemeToContent()
    {
        if (Content is not FrameworkElement fe) return;
        fe.RequestedTheme = App.Config.Config.ThemeMode switch
        {
            ThemeMode.Light => ElementTheme.Light,
            ThemeMode.Dark  => ElementTheme.Dark,
            _               => ElementTheme.Default,
        };
    }

    // ── Resize ────────────────────────────────────────────────
    public void ApplyButtonSize()
    {
        _size = App.Config.Config.ButtonSize;
        int sz = ToPhysical(_size);
        AppWindow.Resize(new SizeInt32(sz, sz));
        ApplyWindowRegion(sz);
        ApplyBackdrop();
    }

    private int ToPhysical(int logical)
    {
        uint dpi = GetDpiForWindow(_hwnd);
        return (int)Math.Round(logical * (dpi > 0 ? dpi : 96u) / 96.0);
    }

    private int ToPhysical(double logical)
    {
        uint dpi = GetDpiForWindow(_hwnd);
        return (int)Math.Round(logical * (dpi > 0 ? dpi : 96u) / 96.0);
    }

    /// <summary>
    /// 获取指针的绝对屏幕物理坐标。
    /// 使用 Win32 GetCursorPos() 而非 WinUI 的 GetCurrentPoint()，
    /// 因为拖动过程中窗口在持续移动，WinUI 返回的是相对当前窗口的 DIP 坐标，
    /// 累加窗口位置会因帧间位置变化产生抖动。GetCursorPos 始终返回绝对屏幕物理坐标。
    /// </summary>
    private static (int x, int y) GetPointerPhysPos(PointerRoutedEventArgs _)
    {
        NativeMethods.GetCursorPos(out var pt);
        return (pt.X, pt.Y);
    }

    // ── Position ──────────────────────────────────────────────
    private void RestorePosition()
    {
        var wa   = DisplayArea.Primary.WorkArea;
        int phys = ToPhysical(_size);
        int x    = (int)(App.Config.Config.ButtonX * wa.Width);
        int y    = (int)(App.Config.Config.ButtonY * wa.Height);
        x = Math.Clamp(x, 0, wa.Width  - phys);
        y = Math.Clamp(y, 0, wa.Height - phys);
        AppWindow.Move(new PointInt32(x, y));
    }

    private void SavePosition()
    {
        var wa = DisplayArea.Primary.WorkArea;
        App.Config.Config.ButtonX = (double)AppWindow.Position.X / wa.Width;
        App.Config.Config.ButtonY = (double)AppWindow.Position.Y / wa.Height;
        App.Config.Save();
    }

    // ── Pointer events ────────────────────────────────────────
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pendingFg = App.FgWatcher.CaptureContext();

        RootGrid.CapturePointer(e.Pointer);
        _menuWasOpenOnPress = _menu != null || WasMenuClosedRecently();
        if (_menu != null) _menu.Close();

        _ptrDown               = true;
        _dragging              = false;
        _suppressMenuOnRelease = false;
        var (px, py) = GetPointerPhysPos(e);
        _pressCursorX = px;
        _pressCursorY = py;
        _dragOffX     = px - AppWindow.Position.X;
        _dragOffY     = py - AppWindow.Position.Y;
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_ptrDown) return;
        var (px, py) = GetPointerPhysPos(e);
        if (!_dragging && HasMovedPastDragThreshold(px, py))
        {
            _dragging              = true;
            _suppressMenuOnRelease = true;
        }
        if (_dragging)
        {
            var wa   = DisplayArea.Primary.WorkArea;
            int phys = ToPhysical(_size);
            int wx   = Math.Clamp(px - _dragOffX, wa.X, wa.X + wa.Width  - phys);
            int wy   = Math.Clamp(py - _dragOffY, wa.Y, wa.Y + wa.Height - phys);
            SetWindowPos(_hwnd, IntPtr.Zero, wx, wy, 0, 0,
                SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
        }
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_ptrDown) return;

        RootGrid.ReleasePointerCaptures();
        var (px, py) = GetPointerPhysPos(e);
        bool wasDrag         = _dragging || HasMovedPastDragThreshold(px, py) || _suppressMenuOnRelease;
        bool shouldOnlyClose = _menuWasOpenOnPress;

        if (wasDrag)
        {
            var wa   = DisplayArea.Primary.WorkArea;
            int phys = ToPhysical(_size);
            int wx   = Math.Clamp(px - _dragOffX, wa.X, wa.X + wa.Width  - phys);
            int wy   = Math.Clamp(py - _dragOffY, wa.Y, wa.Y + wa.Height - phys);
            SetWindowPos(_hwnd, IntPtr.Zero, wx, wy, 0, 0,
                SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
        }

        _ptrDown               = false;
        _dragging              = false;
        _menuWasOpenOnPress    = false;
        _suppressMenuOnRelease = false;

        if      (wasDrag)        SavePosition();
        else if (shouldOnlyClose){ }
        else                     ShowMenu();
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_ptrDown) return;
        var (px, py) = GetPointerPhysPos(e);
        if (_dragging || HasMovedPastDragThreshold(px, py))
        {
            var wa   = DisplayArea.Primary.WorkArea;
            int phys = ToPhysical(_size);
            int wx   = Math.Clamp(px - _dragOffX, wa.X, wa.X + wa.Width  - phys);
            int wy   = Math.Clamp(py - _dragOffY, wa.Y, wa.Y + wa.Height - phys);
            SetWindowPos(_hwnd, IntPtr.Zero, wx, wy, 0, 0,
                SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
            SavePosition();
            _suppressMenuOnRelease = true;
        }
        _ptrDown            = false;
        _dragging           = false;
        _menuWasOpenOnPress = false;
    }

    private bool HasMovedPastDragThreshold(int x, int y) =>
        Math.Abs(x - _pressCursorX) > DragThreshold ||
        Math.Abs(y - _pressCursorY) > DragThreshold;

    private bool WasMenuClosedRecently() =>
        DateTimeOffset.UtcNow - _lastMenuClosedAt <= MenuReopenGuard;

    // ── Show action menu ──────────────────────────────────────
    private void ShowMenu()
    {
        if (_menu != null) { _menu.Close(); return; }
        _menu = new ActionMenuWindow(_pendingFg ?? App.FgWatcher.CaptureContext());
        _menu.SettingsRequested += App.Instance.OpenSettings;
        _menu.Closed += (_, _) =>
        {
            _lastMenuClosedAt = DateTimeOffset.UtcNow;
            _menu = null;
        };
        _menu.ShowNear(new RectInt32(
            AppWindow.Position.X,
            AppWindow.Position.Y,
            AppWindow.Size.Width,
            AppWindow.Size.Height));
    }
}
