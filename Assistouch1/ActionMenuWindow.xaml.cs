using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AssistiveTouch.Models;
using AssistiveTouch.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;

namespace AssistiveTouch;

public sealed partial class ActionMenuWindow : Window
{
    public event Action? SettingsRequested;

    private int _contentHeight = 0;   // logical px, grows during BuildMenu
    private const int MenuWidth = 320; // logical px

    // Foreground window captured before this window activates
    private readonly ForegroundWindowInfo? _capturedFg;
    private readonly IntPtr _hwnd;

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int  GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern int  SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("gdi32.dll")]  private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("dwmapi.dll")] private static extern int  DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref int value, uint size);

    private static readonly IntPtr HWND_TOPMOST  = new(-1);
    private const int  GWL_STYLE                 = -16;
    private const int  GWL_EXSTYLE               = -20;
    private const int  WS_CAPTION                = 0x00C00000;
    private const int  WS_THICKFRAME             = 0x00040000;
    private const int  WS_EX_TOOLWINDOW          = 0x00000080;
    private const uint SWP_NOMOVE                = 0x0002;
    private const uint SWP_NOSIZE                = 0x0001;
    private const uint SWP_NOACTIVATE            = 0x0010;
    private const uint SWP_FRAMECHANGED          = 0x0020;
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const uint DWMWA_BORDER_COLOR            = 34;
    private const uint DWMWA_CAPTION_COLOR           = 35;
    private const uint DWMWA_COLOR_NONE              = 0xFFFFFFFE;

    public ActionMenuWindow(ForegroundWindowInfo? capturedFg = null)
    {
        _capturedFg = capturedFg;

        InitializeComponent();

        // ① Apply theme immediately after InitializeComponent so the very first
        //    rendered frame is already dark — eliminates the white flash.
        ApplyThemeToContent();

        // OverlappedPresenter.Create() instead of CreateForToolWindow —
        // CreateForToolWindow silently blocks SystemBackdrop.
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable   = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);

        // Hide from taskbar/alt-tab via WS_EX_TOOLWINDOW (same as FloatingButtonWindow)
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Remove Win32 caption/frame styles so no system border is rendered
        int style = GetWindowLong(_hwnd, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME);
        SetWindowLong(_hwnd, GWL_STYLE, style);

        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        // ② Tell DWM to use dark non-client area and hide any remaining border colour.
        ApplyDwmDarkMode();

        BuildMenu();
        ResizeToContent();

        // SystemBackdrop must be set after first activation
        Activated += OnFirstActivated;
    }

    private bool _backdropApplied = false;
    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        if (!_backdropApplied)
        {
            _backdropApplied = true;
            ApplyBackdropFromConfig();
        }
        if (e.WindowActivationState == WindowActivationState.Deactivated)
            DispatcherQueue.TryEnqueue(Close);
    }

    // ── Positioning ───────────────────────────────────────────────────────

    public void ShowNear(RectInt32 anchorRect)
    {
        var wa = DisplayArea.Primary.WorkArea;
        int w  = (int)AppWindow.Size.Width;
        int h  = (int)AppWindow.Size.Height;
        const int gap = 12;

        var candidates = new[]
        {
            ClampToWorkArea(new PointInt32(anchorRect.X + anchorRect.Width / 2 - w / 2, anchorRect.Y - h - gap), wa, w, h), // above
            ClampToWorkArea(new PointInt32(anchorRect.X + anchorRect.Width / 2 - w / 2, anchorRect.Y + anchorRect.Height + gap), wa, w, h), // below
            ClampToWorkArea(new PointInt32(anchorRect.X + anchorRect.Width + gap, anchorRect.Y + anchorRect.Height / 2 - h / 2), wa, w, h), // right
            ClampToWorkArea(new PointInt32(anchorRect.X - w - gap, anchorRect.Y + anchorRect.Height / 2 - h / 2), wa, w, h), // left
        };

        PointInt32 best = candidates[0];
        int bestOverlap = int.MaxValue;

        foreach (var candidate in candidates)
        {
            int overlap = GetOverlapArea(candidate, w, h, anchorRect);
            if (overlap == 0)
            {
                best = candidate;
                bestOverlap = 0;
                break;
            }

            if (overlap < bestOverlap)
            {
                best = candidate;
                bestOverlap = overlap;
            }
        }

        AppWindow.Move(best);
        Activate();
    }

    private static PointInt32 ClampToWorkArea(PointInt32 point, RectInt32 workArea, int width, int height)
    {
        int maxX = Math.Max(workArea.X, workArea.X + workArea.Width - width);
        int maxY = Math.Max(workArea.Y, workArea.Y + workArea.Height - height);
        int x = Math.Clamp(point.X, workArea.X, maxX);
        int y = Math.Clamp(point.Y, workArea.Y, maxY);
        return new PointInt32(x, y);
    }

    private static int GetOverlapArea(PointInt32 menuPos, int menuWidth, int menuHeight, RectInt32 anchorRect)
    {
        int overlapLeft = Math.Max(menuPos.X, anchorRect.X);
        int overlapTop = Math.Max(menuPos.Y, anchorRect.Y);
        int overlapRight = Math.Min(menuPos.X + menuWidth, anchorRect.X + anchorRect.Width);
        int overlapBottom = Math.Min(menuPos.Y + menuHeight, anchorRect.Y + anchorRect.Height);

        int overlapWidth = Math.Max(0, overlapRight - overlapLeft);
        int overlapHeight = Math.Max(0, overlapBottom - overlapTop);
        return overlapWidth * overlapHeight;
    }

    // ── Resize helper ─────────────────────────────────────────────────────

    private void ResizeToContent()
    {
        uint dpi    = GetDpiForWindow(_hwnd);
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;
        int physW   = (int)Math.Round(MenuWidth * scale);
        int physH   = (int)Math.Round(Math.Clamp(_contentHeight + 8, 80, 880) * scale);
        AppWindow.Resize(new SizeInt32(physW, physH));
        ApplyWindowRegion(physW, physH);
    }

    // Same 2.5-logical-px inset as FloatingButtonWindow — clips out the DWM white border.
    // CornerRadius matches the XAML MenuCard CornerRadius="8".
    private void ApplyWindowRegion(int physW, int physH)
    {
        uint dpi = GetDpiForWindow(_hwnd);
        int pad  = (int)Math.Round(2.5 * (dpi > 0 ? dpi : 96u) / 96.0,
                                   MidpointRounding.AwayFromZero);
        int r    = (int)Math.Round(8.0 * (dpi > 0 ? dpi : 96u) / 96.0) * 2;
        var rgn  = CreateRoundRectRgn(pad, pad, physW - pad + 1, physH - pad + 1, r, r);
        SetWindowRgn(_hwnd, rgn, true);
    }

    // ── Menu construction ─────────────────────────────────────────────────

    private void BuildMenu()
    {
        // Use the fg snapshot taken at pointer-down (before this window stole focus).
        // Fall back to a fresh snapshot only if none was passed in.
        var fg = _capturedFg ?? App.FgWatcher.CaptureContext();
        bool any = false;

        // Recommended — always expanded, no collapse
        var recommended = App.Rules.GetRecommendedActions(fg);
        if (recommended.Count > 0)
        {
            AddSectionLabel("推荐操作");
            foreach (var a in recommended)
            {
                var cap = a;
                AddMenuItem(cap, () => ExecuteActionAndClose(cap, fg));
            }
            any = true;
        }

        // Pinned — always expanded, no collapse
        var pinned = App.Config.Config.PinnedActions;
        if (pinned.Count > 0)
        {
            if (any) AddSeparator();
            AddSectionLabel("固定");
            foreach (var a in pinned)
            {
                var cap = a;
                AddMenuItem(cap, () => ExecuteActionAndClose(cap, fg));
            }
            any = true;
        }

        // Widgets — collapsible, state persisted per widget id
        var widgets = App.Config.Config.Widgets;
        if (widgets.Count > 0)
        {
            if (any) AddSeparator();
            AddSectionLabel("小工具");
            foreach (var widget in widgets)
                AddWidgetGroup(widget);
        }

        // Settings footer
        AddSeparator();
        AddSettingsItem();
        AddBottomInset();
    }

    // ── Widget group (collapsible) ────────────────────────────────────────

    private void AddWidgetGroup(WidgetItem widget)
    {
        var collapsed = App.Config.Config.WidgetCollapsed;
        bool isCollapsed = !collapsed.TryGetValue(widget.Id, out var c) ? true : c;
        // default: collapsed = true (first open shows widget headers closed)

        // Header row: icon + label + chevron
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconHost = new Grid { Width = 36, Height = 32 };
        iconHost.Children.Add(new FontIcon
        {
            Glyph               = widget.Icon,
            FontFamily          = new FontFamily("Segoe MDL2 Assets"),
            FontSize            = 14,
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var label = new TextBlock
        {
            Text              = widget.Label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 4, 0),
        };

        var chevron = new TextBlock
        {
            Text              = isCollapsed ? "\uE76C" : "\uE70D",
            FontFamily        = new FontFamily("Segoe MDL2 Assets"),
            FontSize          = 10,
            Opacity           = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 10, 0),
        };

        Grid.SetColumn(iconHost, 0);
        Grid.SetColumn(label,    1);
        Grid.SetColumn(chevron,  2);
        headerRow.Children.Add(iconHost);
        headerRow.Children.Add(label);
        headerRow.Children.Add(chevron);

        var headerBtn = new Button
        {
            Content                    = headerRow,
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment   = VerticalAlignment.Center,
            Height                     = 32,
            Padding                    = new Thickness(0),
            Style                      = (Style)Application.Current.Resources["ContextMenuItemStyle"],
        };

        // Child actions panel
        var actionsPanel = new StackPanel
        {
            Spacing = 0,
            Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible,
        };
        int actionsLogicalH = widget.Actions.Count * 32;

        foreach (var a in widget.Actions)
        {
            var cap = a;
            AddMenuItemTo(actionsPanel, cap, () => ExecuteActionAndClose(cap, _capturedFg ?? App.FgWatcher.CaptureContext()), indent: 14);
        }

        // Track how much height the expanded children add
        if (!isCollapsed)
            _contentHeight += actionsLogicalH;

        headerBtn.Click += (_, _) =>
        {
            try
            {
                isCollapsed = !isCollapsed;

                // Persist
                App.Config.Config.WidgetCollapsed[widget.Id] = isCollapsed;
                App.Config.Save();

                actionsPanel.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                chevron.Text = isCollapsed ? "\uE76C" : "\uE70D";

                // Adjust window height
                _contentHeight += isCollapsed ? -actionsLogicalH : actionsLogicalH;
                ResizeToContent();
            }
            catch (Exception ex) { Services.CrashLogger.Write(ex, "ActionMenuWindow.WidgetGroup.Toggle"); }
        };

        RootPanel.Children.Add(headerBtn);
        RootPanel.Children.Add(actionsPanel);
        _contentHeight += 32; // header row
    }

    // ── Static helpers ────────────────────────────────────────────────────

    private void AddSectionLabel(string label)
    {
        var grid = new Grid { Margin = new Thickness(8, 6, 8, 2), Height = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        var leftLine  = new Rectangle { Height = 1, Opacity = 0.35, Fill = fill, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var text      = new TextBlock  { Text = label, FontSize = 11, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var rightLine = new Rectangle { Height = 1, Opacity = 0.35, Fill = fill, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };

        Grid.SetColumn(leftLine,  0);
        Grid.SetColumn(text,      1);
        Grid.SetColumn(rightLine, 2);
        grid.Children.Add(leftLine);
        grid.Children.Add(text);
        grid.Children.Add(rightLine);

        RootPanel.Children.Add(grid);
        _contentHeight += 26;
    }

    private void AddMenuItem(ActionItem item, Action onClick) =>
        AddMenuItemTo(RootPanel, item, onClick, indent: 0);

    private void AddMenuItemTo(Panel target, ActionItem item, Action onClick, double indent)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };

        var iconHost = new Grid { Width = 36 + indent, Height = 32 };
        iconHost.Children.Add(new FontIcon
        {
            Glyph = item.Icon,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        sp.Children.Add(iconHost);
        sp.Children.Add(new TextBlock
        {
            Text = item.Label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });

        var btn = new Button
        {
            Content = sp,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = 32,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["ContextMenuItemStyle"],
        };
        btn.Click += (_, _) =>
        {
            try { onClick(); }
            catch (Exception ex) { Services.CrashLogger.Write(ex, $"ActionMenuWindow.MenuItem.Click label={item.Label}"); }
        };
        target.Children.Add(btn);

        // Only count height when adding to root panel directly
        if (ReferenceEquals(target, RootPanel))
            _contentHeight += 32;
    }

    private void AddSeparator()
    {
        RootPanel.Children.Add(new Rectangle
        {
            Height = 1,
            Margin = new Thickness(0, 3, 0, 3),
            Fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
        });
        _contentHeight += 7;
    }

    private async void ExecuteActionAndClose(ActionItem action, ForegroundWindowInfo? foreground)
    {
        Close();

        // Give the popup a beat to disappear before restoring focus / injecting input.
        await Task.Delay(50);

        try
        {
            App.Executor.Execute(action, foreground);
        }
        catch (Exception ex)
        {
            Services.CrashLogger.Write(ex, $"ActionMenuWindow.ExecuteAction label={action.Label}");
        }
    }

    private void AddSettingsItem()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        var iconHost = new Grid { Width = 36, Height = 32 };
        iconHost.Children.Add(new FontIcon
        {
            Glyph = "\uE713",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        sp.Children.Add(iconHost);
        sp.Children.Add(new TextBlock
        {
            Text = "设置…",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });

        var btn = new Button
        {
            Content = sp,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = 32,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["ContextMenuItemStyle"],
        };
        btn.Click += (_, _) =>
        {
            try { SettingsRequested?.Invoke(); Close(); }
            catch (Exception ex) { Services.CrashLogger.Write(ex, "ActionMenuWindow.Settings.Click"); }
        };
        RootPanel.Children.Add(btn);
        _contentHeight += 32;
    }

    private void AddBottomInset(int height = 10)
    {
        RootPanel.Children.Add(new Rectangle
        {
            Height = height,
            Opacity = 0,
            IsHitTestVisible = false,
        });
        _contentHeight += height;
    }

    private void ApplyBackdropFromConfig()
    {
        SystemBackdrop = App.Config.Config.ButtonBackdrop switch
        {
            ButtonBackdropType.Acrylic => new DesktopAcrylicBackdrop(),
            ButtonBackdropType.Mica    => new MicaBackdrop(),
            ButtonBackdropType.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            _                          => null,
        };

        ApplyThemeToContent();
        ApplyFallbackBackground();
    }

    /// <summary>
    /// When no system backdrop is set the window client area is fully opaque
    /// (white in light mode, near-black in dark mode).  Apply a visible
    /// semi-transparent brush to MenuCard so the menu still looks good.
    /// When a real backdrop is active, keep MenuCard transparent so Acrylic/Mica shows through.
    /// </summary>
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
                    ? Windows.UI.Color.FromArgb(0xE0, 0x28, 0x28, 0x28) // dark: near-black 88%
                    : Windows.UI.Color.FromArgb(0xE8, 0xF9, 0xF9, 0xF9)); // light: near-white 91%
        }
        else
        {
            card.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
    }

    // Sets RequestedTheme on the XAML content tree.
    // Called early in constructor (pre-render) and again after backdrop is ready.
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

    // Tells DWM whether to render the non-client area in dark mode,
    // and hides any remaining border/caption colour.
    private void ApplyDwmDarkMode()
    {
        bool isDark = App.Config.Config.ThemeMode == ThemeMode.Dark;
        int value   = isDark ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));

        int noBorder = unchecked((int)DWMWA_COLOR_NONE);
        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR,  ref noBorder, sizeof(int));
        DwmSetWindowAttribute(_hwnd, DWMWA_CAPTION_COLOR, ref noBorder, sizeof(int));
    }
}
