using System;
using System.Collections.Generic;
using AssistiveTouch.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace AssistiveTouch.Pages;

public sealed partial class GeneralPage : Page
{
    private const string StartupKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupName = "AssistiveTouch";
    private const string StartupTaskId = "AssistiveTouchStartup";

    private readonly string _cfgPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AssistiveTouch", "config.json");

    // 显示名 ↔ 枚举值映射
    private static readonly (string Label, BackdropType Value)[] BackdropItems =
    {
        ("无（默认）",  BackdropType.None),
        ("亚克力",      BackdropType.Acrylic),
        ("云母",        BackdropType.Mica),
        ("云母 Alt",    BackdropType.MicaAlt),
    };

    private static readonly (string Label, ThemeMode Value)[] ThemeItems =
    {
        ("跟随系统", ThemeMode.System),
        ("浅色",     ThemeMode.Light),
        ("深色",     ThemeMode.Dark),
    };

    private static readonly (string Label, ButtonBackdropType Value)[] ButtonBackdropItems =
    {
        ("纯色半透明",  ButtonBackdropType.None),
        ("亚克力",      ButtonBackdropType.Acrylic),
        ("云母",        ButtonBackdropType.Mica),
        ("云母 Alt",    ButtonBackdropType.MicaAlt),
    };

    public GeneralPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // About version
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        AboutVersionText.Text = ver != null
            ? $"AssisTouch  v{ver.Major}.{ver.Minor}.{ver.Build}"
            : "AssisTouch  v1.1";

        // Backdrop
        BackdropCombo.ItemsSource   = Array.ConvertAll(BackdropItems, x => x.Label);
        BackdropCombo.SelectedIndex = Array.FindIndex(BackdropItems, x => x.Value == App.Config.Config.BackdropType);
        BackdropCombo.SelectionChanged += BackdropCombo_SelectionChanged;

        // Theme
        ThemeCombo.ItemsSource   = Array.ConvertAll(ThemeItems, x => x.Label);
        ThemeCombo.SelectedIndex = Array.FindIndex(ThemeItems, x => x.Value == App.Config.Config.ThemeMode);
        ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;

        // Size
        SizeSlider.Value = App.Config.Config.ButtonSize;
        SizeLabel.Text   = $"{App.Config.Config.ButtonSize} px";
        SizeSlider.ValueChanged += SizeSlider_ValueChanged;

        // Button backdrop
        ButtonBackdropCombo.ItemsSource   = Array.ConvertAll(ButtonBackdropItems, x => x.Label);
        ButtonBackdropCombo.SelectedIndex = Array.FindIndex(ButtonBackdropItems, x => x.Value == App.Config.Config.ButtonBackdrop);
        ButtonBackdropCombo.SelectionChanged += ButtonBackdropCombo_SelectionChanged;

        // Poll
        PollCombo.ItemsSource  = new[] { "200 ms", "500 ms", "1000 ms", "2000 ms" };
        PollCombo.SelectedItem = "500 ms";

        // Startup — async init, run fire-and-forget
        _ = InitStartupToggleAsync();

        CfgPathText.Text = $"配置文件：{_cfgPath}";
        OpenCfgBtn.Click += OpenCfgBtn_Click;
        ExportBtn.Click  += ExportBtn_Click;
        ImportBtn.Click  += ImportBtn_Click;
    }

    private void BackdropCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = BackdropCombo.SelectedIndex;
        if (idx < 0) return;
        App.Config.Config.BackdropType = BackdropItems[idx].Value;
        App.Config.Save();
        App.Instance.ApplyWindowSettings();
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = ThemeCombo.SelectedIndex;
        if (idx < 0) return;
        App.Config.Config.ThemeMode = ThemeItems[idx].Value;
        App.Config.Save();
        App.Instance.ApplyWindowSettings();
        App.Instance.FloatingButtonApplyBackdrop(); // propagate theme to floating button
    }

    private void SizeSlider_ValueChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int v = (int)e.NewValue;
        App.Config.Config.ButtonSize = v;
        SizeLabel.Text = $"{v} px";
        App.Config.Save();
        App.Instance.FloatingButtonApplySize();
    }

    private void ButtonBackdropCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = ButtonBackdropCombo.SelectedIndex;
        if (idx < 0) return;
        App.Config.Config.ButtonBackdrop = ButtonBackdropItems[idx].Value;
        App.Config.Save();
        App.Instance.FloatingButtonApplyBackdrop();
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e) =>
        _ = SetStartupAsync(StartupToggle.IsOn);

    private void OpenCfgBtn_Click(object sender, RoutedEventArgs e)
    {
        var dir = System.IO.Path.GetDirectoryName(_cfgPath);
        if (dir != null && System.IO.Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    private async void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON 配置", new List<string> { ".json" });
        picker.SuggestedFileName = "assistouch_config";
        WinRT.Interop.InitializeWithWindow.Initialize(picker, GetHwnd());
        var file = await picker.PickSaveFileAsync();
        if (file != null)
            System.IO.File.Copy(_cfgPath, file.Path, overwrite: true);
    }

    private async void ImportBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, GetHwnd());
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        var confirm = new ContentDialog
        {
            Title = "导入配置", Content = "这将覆盖当前所有配置，确定吗？",
            PrimaryButtonText = "确定", CloseButtonText = "取消", XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        System.IO.File.Copy(file.Path, _cfgPath, overwrite: true);
        App.Config.Load();
        var done = new ContentDialog
        {
            Title = "导入完成", Content = "配置已导入，部分更改需重启后生效。",
            CloseButtonText = "好", XamlRoot = XamlRoot
        };
        await done.ShowAsync();
    }

    // ── Startup helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the app is running as an MSIX package (has package identity).
    /// </summary>
    private static bool IsPackaged()
    {
        try { var _ = Package.Current; return true; }
        catch { return false; }
    }

    private async System.Threading.Tasks.Task InitStartupToggleAsync()
    {
        bool enabled;
        if (IsPackaged())
        {
            // Use the MSIX StartupTask API — this is the correct approach for packaged apps.
            // The registry path points to the package install directory which changes on update.
            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                enabled = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            catch { enabled = false; }
        }
        else
        {
            enabled = IsStartupEnabledRegistry();
        }

        // Update toggle without triggering Toggled
        StartupToggle.Toggled -= StartupToggle_Toggled;
        StartupToggle.IsOn = enabled;
        StartupToggle.Toggled += StartupToggle_Toggled;
    }

    private async System.Threading.Tasks.Task SetStartupAsync(bool enable)
    {
        if (IsPackaged())
        {
            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                if (enable)
                {
                    var newState = await task.RequestEnableAsync();
                    // If the user denied, reflect the real state back in the toggle
                    bool actualEnabled = newState is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                    if (actualEnabled != enable)
                    {
                        StartupToggle.Toggled -= StartupToggle_Toggled;
                        StartupToggle.IsOn = actualEnabled;
                        StartupToggle.Toggled += StartupToggle_Toggled;
                    }
                    App.Config.Config.StartWithWindows = actualEnabled;
                }
                else
                {
                    task.Disable();
                    App.Config.Config.StartWithWindows = false;
                }
                App.Config.Save();
            }
            catch { }
        }
        else
        {
            SetStartupRegistry(enable);
        }
    }

    private static bool IsStartupEnabledRegistry()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(StartupKey);
              return key?.GetValue(StartupName) != null; }
        catch { return false; }
    }

    private static void SetStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupKey, writable: true);
            if (key == null) return;
            if (enable)
                key.SetValue(StartupName, $"\"{System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName}\"");
            else
                key.DeleteValue(StartupName, throwOnMissingValue: false);
            App.Config.Config.StartWithWindows = enable;
            App.Config.Save();
        }
        catch { }
    }

    private IntPtr GetHwnd() => App.Instance.SettingsWindowHandle;
}
