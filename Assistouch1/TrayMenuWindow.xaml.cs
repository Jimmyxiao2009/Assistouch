using System;
using System.Runtime.InteropServices;
using AssistiveTouch.Models;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;

namespace AssistiveTouch;

/// <summary>
/// WinUI-style tray popup: 「打开设置」+ 「退出」。
/// Shown on left-click of the tray icon; dismissed when deactivated.
/// </summary>
public sealed partial class TrayMenuWindow : Window
{
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    private readonly IntPtr _hwnd;
    private int _contentHeight;
    private const int MenuWidth = 200; // logical px

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int  GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern int  SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("gdi32.dll")]  private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("dwmapi.dll")] private static extern int  DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref int value, uint size);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const int  GWL_STYLE      = -16;
    private const int  GWL_EXSTYLE    = -20;
    private const int  WS_CAPTION     = 0x00C00000;
    private const int  WS_THICKFRAME  = 0x00040000;
    private const int  WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const uint DWMWA_BORDER_COLOR   = 34;
    private const uint DWMWA_CAPTION_COLOR  = 35;
    private const uint DWMWA_COLOR_NONE     = 0xFFFFFFFE;

    public TrayMenuWindow()
    {
        InitializeComponent();

        ApplyThemeToContent();

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable   = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        int style = GetWindowLong(_hwnd, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME);
        SetWindowLong(_hwnd, GWL_STYLE, style);

        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        ApplyDwmAttributes();
        BuildMenu();
        ResizeToContent();

        Activated += OnActivated;
    }

    // ── Show above tray icon ───────────────────────────────────
    public void ShowAtCursor()
    {
        GetCursorPos(out var pt);
        var wa = DisplayArea.Primary.WorkArea;

        uint dpi   = GetDpiForWindow(_hwnd);
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;
        int physW  = AppWindow.Size.Width;
        int physH  = AppWindow.Size.Height;

        // Position above the cursor, offset left so it doesn't cover the icon
        int x = pt.X - physW / 2;
        int y = pt.Y - physH - (int)(8 * scale);

        // Clamp to work area
        x = Math.Clamp(x, wa.X, wa.X + wa.Width  - physW);
        y = Math.Clamp(y, wa.Y, wa.Y + wa.Height - physH);

        AppWindow.Move(new PointInt32(x, y));
        Activate();
    }

    private bool _backdropApplied;
    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (!_backdropApplied)
        {
            _backdropApplied = true;
            ApplyBackdrop();
            ApplyFallbackBackground();
        }
        if (e.WindowActivationState == WindowActivationState.Deactivated)
            DispatcherQueue.TryEnqueue(Close);
    }

    // ── Menu items ─────────────────────────────────────────────
    private void BuildMenu()
    {
        // App name header (non-clickable label)
        AddSectionLabel("AssisTouch");

        AddItem("\uE713", "打开设置", () =>
        {
            Close();
            SettingsRequested?.Invoke();
        });

        AddSeparator();

        AddItem("\uE711", "退出", () =>
        {
            Close();
            ExitRequested?.Invoke();
        });

        // Bottom inset
        RootPanel.Children.Add(new Rectangle { Height = 4, Opacity = 0, IsHitTestVisible = false });
        _contentHeight += 4;
    }

    private void AddSectionLabel(string text)
    {
        var fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        var grid = new Grid { Margin = new Thickness(8, 6, 8, 2), Height = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftLine  = new Rectangle { Height = 1, Opacity = 0.35, Fill = fill, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var label     = new TextBlock  { Text = text, FontSize = 11, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };
        var rightLine = new Rectangle { Height = 1, Opacity = 0.35, Fill = fill, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };

        Grid.SetColumn(leftLine,  0);
        Grid.SetColumn(label,     1);
        Grid.SetColumn(rightLine, 2);
        grid.Children.Add(leftLine);
        grid.Children.Add(label);
        grid.Children.Add(rightLine);

        RootPanel.Children.Add(grid);
        _contentHeight += 26;
    }

    private void AddItem(string glyph, string label, Action onClick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };

        var iconHost = new Grid { Width = 36, Height = 32 };
        iconHost.Children.Add(new FontIcon
        {
            Glyph               = glyph,
            FontFamily          = new FontFamily("Segoe MDL2 Assets"),
            FontSize            = 14,
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        row.Children.Add(iconHost);
        row.Children.Add(new TextBlock
        {
            Text              = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 12, 0),
        });

        var btn = new Button
        {
            Content                    = row,
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment   = VerticalAlignment.Center,
            Height                     = 32,
            Padding                    = new Thickness(0),
            Style                      = (Style)Application.Current.Resources["ContextMenuItemStyle"],
        };
        btn.Click += (_, _) =>
        {
            try { onClick(); }
            catch (Exception ex) { Services.CrashLogger.Write(ex, $"TrayMenuWindow.{label}"); }
        };

        RootPanel.Children.Add(btn);
        _contentHeight += 32;
    }

    private void AddSeparator()
    {
        RootPanel.Children.Add(new Rectangle
        {
            Height = 1,
            Margin = new Thickness(0, 3, 0, 3),
            Fill   = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
        });
        _contentHeight += 7;
    }

    // ── Sizing ─────────────────────────────────────────────────
    private void ResizeToContent()
    {
        uint dpi    = GetDpiForWindow(_hwnd);
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;
        int physW   = (int)Math.Round(MenuWidth * scale);
        int physH   = (int)Math.Round((_contentHeight + 8) * scale);
        AppWindow.Resize(new SizeInt32(physW, physH));

        int pad = (int)Math.Round(2.5 * (dpi > 0 ? dpi : 96u) / 96.0, MidpointRounding.AwayFromZero);
        int r   = (int)Math.Round(8.0 * (dpi > 0 ? dpi : 96u) / 96.0) * 2;
        var rgn = CreateRoundRectRgn(pad, pad, physW - pad + 1, physH - pad + 1, r, r);
        SetWindowRgn(_hwnd, rgn, true);
    }

    // ── Appearance ─────────────────────────────────────────────
    private void ApplyBackdrop()
    {
        SystemBackdrop = App.Config.Config.ButtonBackdrop switch
        {
            ButtonBackdropType.Acrylic => new DesktopAcrylicBackdrop(),
            ButtonBackdropType.Mica    => new MicaBackdrop(),
            ButtonBackdropType.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            _                          => null,
        };
    }

    private void ApplyFallbackBackground()
    {
        if (MenuCard is not Border card) return;
        if (App.Config.Config.ButtonBackdrop == ButtonBackdropType.None)
        {
            bool isDark = App.Config.Config.ThemeMode == ThemeMode.Dark ||
                          (App.Config.Config.ThemeMode == ThemeMode.System &&
                           Application.Current.RequestedTheme == ApplicationTheme.Dark);
            card.Background = new SolidColorBrush(
                isDark
                    ? Windows.UI.Color.FromArgb(0xE0, 0x28, 0x28, 0x28)
                    : Windows.UI.Color.FromArgb(0xE8, 0xF9, 0xF9, 0xF9));
        }
        else
        {
            card.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
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

    private void ApplyDwmAttributes()
    {
        bool isDark = App.Config.Config.ThemeMode == ThemeMode.Dark;
        int dark    = isDark ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int noBorder = unchecked((int)DWMWA_COLOR_NONE);
        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR,  ref noBorder, sizeof(int));
        DwmSetWindowAttribute(_hwnd, DWMWA_CAPTION_COLOR, ref noBorder, sizeof(int));
    }
}
