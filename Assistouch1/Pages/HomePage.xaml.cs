using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssistiveTouch.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = ver != null
            ? $"版本 {ver.Major}.{ver.Minor}.{ver.Build}"
            : "版本 1.1";
    }

    // Navigate to sibling pages via the parent NavigationView
    private void GoPinnedBtn_Click(object sender, RoutedEventArgs e)   => NavigateTo("Pinned");
    private void GoRulesBtn_Click(object sender, RoutedEventArgs e)    => NavigateTo("Rules");
    private void GoWidgetsBtn_Click(object sender, RoutedEventArgs e)  => NavigateTo("Widgets");
    private void GoGeneralBtn_Click(object sender, RoutedEventArgs e)  => NavigateTo("General");

    private static void NavigateTo(string tag)
    {
        if (App.Instance.SettingsNav is not { } nav) return;
        foreach (var obj in nav.MenuItems)
        {
            if (obj is NavigationViewItem item && item.Tag as string == tag)
            {
                nav.SelectedItem = item;
                break;
            }
        }
    }
}
