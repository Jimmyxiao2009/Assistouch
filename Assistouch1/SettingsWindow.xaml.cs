using System;
using AssistiveTouch.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace AssistiveTouch;

public sealed partial class SettingsWindow : Window
{
    /// <summary>Exposed so HomePage.cs can call nav.SelectedItem.</summary>
    public NavigationView SettingsNav => NavView;

    public SettingsWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1080, 720));
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        Title = "AssisTouch · 设置";
        ApplyBackdropAndTheme();

        // Ensure the window is visible on the primary display area,
        // in case the OS restored a stale off-screen position from a previous session.
        var wa = DisplayArea.Primary.WorkArea;
        int x = wa.X + (wa.Width  - AppWindow.Size.Width)  / 2;
        int y = wa.Y + (wa.Height - AppWindow.Size.Height) / 2;
        AppWindow.Move(new PointInt32(x, y));

        Activated += SettingsWindow_Activated;
    }

    private void SettingsWindow_Activated(object? sender, WindowActivatedEventArgs e)
    {
        Activated -= SettingsWindow_Activated;
        DispatcherQueue.TryEnqueue(() =>
        {
            ContentFrame.Navigate(typeof(Pages.HomePage));
            NavView.SelectedItem = NavView.MenuItems[0];
        });
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is not NavigationViewItem item) return;
        Type? page = (item.Tag as string) switch
        {
            "Home"    => typeof(Pages.HomePage),
            "Pinned"  => typeof(Pages.PinnedPage),
            "Rules"   => typeof(Pages.RulesPage),
            "Widgets" => typeof(Pages.WidgetsPage),
            "General" => typeof(Pages.GeneralPage),
            _ => null
        };
        if (page != null && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }

    public void ApplyBackdropAndTheme()
    {
        // Backdrop
        SystemBackdrop = App.Config.Config.BackdropType switch
        {
            BackdropType.Acrylic => new DesktopAcrylicBackdrop(),
            BackdropType.Mica    => new MicaBackdrop(),
            BackdropType.MicaAlt => new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
            _                    => null
        };

        // Theme
        var theme = App.Config.Config.ThemeMode switch
        {
            ThemeMode.Light => ElementTheme.Light,
            ThemeMode.Dark  => ElementTheme.Dark,
            _               => ElementTheme.Default
        };
        if (Content is FrameworkElement fe)
            fe.RequestedTheme = theme;
    }
}
