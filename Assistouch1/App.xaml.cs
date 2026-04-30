using System;
using AssistiveTouch.Services;
using Microsoft.UI.Xaml;

namespace AssistiveTouch;

public partial class App : Application
{
    public static ConfigService Config  { get; } = new();
    public static ForegroundWindowService FgWatcher { get; } = new();
    public static RuleEngine  Rules    { get; } = new(Config);
    public static ActionExecutor Executor { get; } = new();

    private FloatingButtonWindow? _floatWin;
    private SettingsWindow?       _settingsWin;
    private TrayIconService?      _tray;
    private TrayMenuWindow?       _trayMenu;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        CrashLogger.Init(this);

        try
        {
            _floatWin = new FloatingButtonWindow();
            _floatWin.Activate();

            _tray = new TrayIconService();
            _tray.MenuRequested += ShowTrayMenu;
        }
        catch (Exception ex)
        {
            CrashLogger.Write(ex, "App.OnLaunched");
            throw;
        }
    }

    private void ShowTrayMenu()
    {
        // If already open, close it (toggle behaviour)
        if (_trayMenu != null)
        {
            _trayMenu.Close();
            return;
        }
        _trayMenu = new TrayMenuWindow();
        _trayMenu.SettingsRequested += OpenSettings;
        _trayMenu.ExitRequested += () =>
        {
            _tray?.Dispose();
            _floatWin?.Close();
            Environment.Exit(0);
        };
        _trayMenu.Closed += (_, _) => _trayMenu = null;
        _trayMenu.ShowAtCursor();
    }

    public void OpenSettings()
    {
        try
        {
            if (_settingsWin?.AppWindow != null)
            {
                _settingsWin.Activate();
                return;
            }
            _settingsWin = new SettingsWindow();
            _settingsWin.Closed += (_, _) => _settingsWin = null;
            _settingsWin.Activate();
        }
        catch (Exception ex)
        {
            CrashLogger.Write(ex, "App.OpenSettings");
        }
    }

    public IntPtr SettingsWindowHandle =>
        _settingsWin != null ? WinRT.Interop.WindowNative.GetWindowHandle(_settingsWin) : IntPtr.Zero;

    /// <summary>Lets inner pages navigate between tabs.</summary>
    public Microsoft.UI.Xaml.Controls.NavigationView? SettingsNav => _settingsWin?.SettingsNav;

    public void ApplyWindowSettings() => _settingsWin?.ApplyBackdropAndTheme();

    public void FloatingButtonApplyBackdrop() => _floatWin?.ApplyBackdrop();
    public void FloatingButtonApplySize()     => _floatWin?.ApplyButtonSize();

    public static App Instance => (App)Current;
}
