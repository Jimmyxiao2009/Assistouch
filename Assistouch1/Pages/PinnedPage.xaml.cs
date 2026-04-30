using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AssistiveTouch.Models;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace AssistiveTouch.Pages;

public sealed partial class PinnedPage : Page
{
    private List<ActionItem> Items => App.Config.Config.PinnedActions;
    private ActionItem? _dragging;

    public PinnedPage()
    {
        InitializeComponent();
        RebuildItems();
    }

    private void AddBtn_Click(object sender, RoutedEventArgs e) => _ = AddAsync();
    private async Task AddAsync()
    {
        var item = await ShowActionDialog(null, XamlRoot);
        if (item != null) { Items.Add(item); RebuildItems(); App.Config.Save(); }
    }

    // ── Imperative list with drag-handle reorder ─────────────
    private void RebuildItems()
    {
        ItemPanel.Children.Clear();
        foreach (var a in Items)
        {
            var captured = a; // capture for closures

            // ── Card ──────────────────────────────────────────
            var card = new Border
            {
                Background      = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush     = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(0, 10, 14, 10),
                AllowDrop       = true,
            };

            // Drop-target feedback
            card.DragEnter += (_, e) =>
            {
                if (_dragging != null && _dragging != captured)
                {
                    e.AcceptedOperation = DataPackageOperation.Move;
                    card.Opacity = 0.55;
                }
            };
            card.DragLeave += (_, _) => card.Opacity = 1.0;
            card.Drop += (_, _) =>
            {
                card.Opacity = 1.0;
                if (_dragging == null || _dragging == captured) return;
                int from = Items.IndexOf(_dragging);
                int to   = Items.IndexOf(captured);
                if (from < 0 || to < 0) return;
                Items.RemoveAt(from);
                Items.Insert(to, _dragging);
                _dragging = null;
                RebuildItems();
                App.Config.Save();
            };

            // ── Row ───────────────────────────────────────────
            var row = new Grid();
            // ☰ grip | icon gap | icon | label gap | label | buttons
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });           // grip
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });           // gap
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });              // icon badge
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });           // gap
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // label
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });              // buttons

            // ☰ Drag grip
            var grip = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment   = VerticalAlignment.Stretch,
                CanDrag             = true,
                Child = new FontIcon
                {
                    Glyph      = "\uE784",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize   = 14,
                    Opacity    = 0.35,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                }
            };
            grip.DragStarting += (_, e) =>
            {
                _dragging = captured;
                e.Data.RequestedOperation = DataPackageOperation.Move;
                // Put a placeholder so AcceptedOperation can be set
                e.Data.SetText(captured.Id.ToString());
            };

            // Icon badge
            var iconBadge = new Border
            {
                Width        = 36,
                Height       = 36,
                CornerRadius = new CornerRadius(8),
                Background   = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                Child = new FontIcon
                {
                    Glyph      = captured.Icon,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize   = 16,
                    Foreground = new SolidColorBrush(Colors.White),
                }
            };

            // Label + type
            var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            labelStack.Children.Add(new TextBlock
            {
                Text  = captured.Label,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            labelStack.Children.Add(new TextBlock
            {
                Text    = ActionTypeName(captured.Type),
                Style   = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Opacity = 0.55,
            });

            // Edit / Delete buttons
            var editBtn = new Button
            {
                Content = "编辑",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            };
            editBtn.Click += (_, _) => _ = EditItemAsync(captured);

            var delBtn = new Button
            {
                Width   = 32,
                Height  = 32,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new FontIcon
                {
                    Glyph      = "\uE74D",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize   = 12,
                },
            };
            delBtn.Click += (_, _) => { Items.Remove(captured); RebuildItems(); App.Config.Save(); };

            var btnBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 4,
            };
            btnBar.Children.Add(editBtn);
            btnBar.Children.Add(delBtn);

            Grid.SetColumn(grip,      0);
            Grid.SetColumn(iconBadge, 2);
            Grid.SetColumn(labelStack,4);
            Grid.SetColumn(btnBar,    5);
            row.Children.Add(grip);
            row.Children.Add(iconBadge);
            row.Children.Add(labelStack);
            row.Children.Add(btnBar);

            card.Child = row;
            ItemPanel.Children.Add(card);
        }
    }

    private async Task EditItemAsync(ActionItem existing)
    {
        var edited = await ShowActionDialog(existing, XamlRoot);
        if (edited == null) return;
        var idx = Items.IndexOf(existing);
        if (idx >= 0) Items[idx] = edited;
        RebuildItems();
        App.Config.Save();
    }

    private static string ActionTypeName(ActionType t) => t switch
    {
        ActionType.Hotkey        => "快捷键",
        ActionType.SimulateClick => "模拟点击",
        ActionType.Script        => "脚本",
        ActionType.ShowDesktop   => "显示桌面",
        ActionType.OpenUrl       => "打开网址/路径",
        ActionType.LaunchApp     => "启动程序",
        ActionType.KillProcess   => "结束进程",
        ActionType.MediaControl  => "媒体/音量",
        ActionType.SendText      => "输入文字",
        ActionType.LockScreen    => "锁定屏幕",
        _                        => t.ToString(),
    };

    // ── Shared action dialog ───────────────────────────────────
    public static async Task<ActionItem?> ShowActionDialog(ActionItem? existing, XamlRoot? xamlRoot)
    {
        var item = existing != null
            ? new ActionItem
            {
                Id=existing.Id, Label=existing.Label, Icon=existing.Icon,
                Type=existing.Type, HotkeyString=existing.HotkeyString,
                ClickTarget=existing.ClickTarget, ScriptPath=existing.ScriptPath,
                ScriptInline=existing.ScriptInline, ScriptSilent=existing.ScriptSilent,
                ScriptShell=existing.ScriptShell, UrlOrPath=existing.UrlOrPath,
                LaunchArgs=existing.LaunchArgs, TargetProcess=existing.TargetProcess,
                MediaCmd=existing.MediaCmd, TextToSend=existing.TextToSend
            }
            : new ActionItem();

        var panel = new StackPanel { Spacing = 12, Width = 380 };
        var labelBox = new TextBox { Header = "显示名称", Text = item.Label, PlaceholderText = "例如：退出放映" };

        var typeCombo = new ComboBox
        {
            Header = "操作类型",
            ItemsSource = new[]
            {
                "SimulateClick — 模拟点击",
                "Hotkey — 快捷键",
                "Script — 脚本",
                "ShowDesktop — 显示桌面",
                "OpenUrl — 打开网址/路径",
                "LaunchApp — 启动程序",
                "KillProcess — 结束进程",
                "MediaControl — 媒体/音量",
                "SendText — 输入文字",
                "LockScreen — 锁定屏幕",
            },
            SelectedIndex = (int)item.Type
        };

        var paramStack = new StackPanel { Spacing = 8 };

        void RebuildParams(ActionType t)
        {
            paramStack.Children.Clear();
            switch (t)
            {
                case ActionType.Hotkey:
                {
                    bool isRecording = false;
                    var hk = new TextBox
                    {
                        Header = "快捷键", Text = item.HotkeyString ?? "",
                        PlaceholderText = "Ctrl+Alt+F5"
                    };
                    var modPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed
                    };
                    var btnCtrl  = new ToggleButton { Content = "Ctrl"  };
                    var btnAlt   = new ToggleButton { Content = "Alt"   };
                    var btnShift = new ToggleButton { Content = "Shift" };
                    var btnWin   = new ToggleButton { Content = "Win"   };
                    modPanel.Children.Add(new TextBlock
                    {
                        Text = "修饰键：", VerticalAlignment = VerticalAlignment.Center,
                        Opacity = 0.65, Margin = new Thickness(0, 0, 2, 0)
                    });
                    modPanel.Children.Add(btnCtrl);
                    modPanel.Children.Add(btnAlt);
                    modPanel.Children.Add(btnShift);
                    modPanel.Children.Add(btnWin);
                    var recSwitch = new ToggleSwitch
                    {
                        OffContent = "手动输入", OnContent = "按下主键...",
                        IsOn = false, Margin = new Thickness(0, 4, 0, 0)
                    };
                    void StopRecording(bool restoreText)
                    {
                        hk.IsReadOnly = false; hk.PlaceholderText = "Ctrl+Alt+F5";
                        modPanel.Visibility = Visibility.Collapsed;
                        btnCtrl.IsChecked = btnAlt.IsChecked =
                        btnShift.IsChecked = btnWin.IsChecked = false;
                        isRecording = false; recSwitch.IsOn = false;
                        if (restoreText && string.IsNullOrEmpty(hk.Text))
                            hk.Text = item.HotkeyString ?? "";
                    }
                    // 在录制模式下阻止任何文字输入到 TextBox（避免主键字母在 KeyDown 前被插入）
                    hk.BeforeTextChanging += (_, bce) => { if (isRecording) bce.Cancel = true; };
                    hk.KeyDown += (_, ke) =>
                    {
                        if (!isRecording) return;
                        ke.Handled = true;
                        var key = ke.Key;
                        if (key is VirtualKey.Control or VirtualKey.LeftControl  or VirtualKey.RightControl
                                or VirtualKey.Shift   or VirtualKey.LeftShift    or VirtualKey.RightShift
                                or VirtualKey.Menu    or VirtualKey.LeftMenu     or VirtualKey.RightMenu
                                or VirtualKey.LeftWindows or VirtualKey.RightWindows) return;
                        if (key == VirtualKey.Escape) { hk.Text = item.HotkeyString ?? ""; StopRecording(false); return; }
                        bool ctrl  = btnCtrl.IsChecked  == true ||
                            (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
                        bool alt   = btnAlt.IsChecked   == true ||
                            (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)    & CoreVirtualKeyStates.Down) != 0;
                        bool shift = btnShift.IsChecked == true ||
                            (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)   & CoreVirtualKeyStates.Down) != 0;
                        bool win   = btnWin.IsChecked   == true ||
                            (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftWindows)  & CoreVirtualKeyStates.Down) != 0 ||
                            (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightWindows) & CoreVirtualKeyStates.Down) != 0;
                        var parts = new List<string>();
                        if (ctrl)  parts.Add("Ctrl");
                        if (alt)   parts.Add("Alt");
                        if (shift) parts.Add("Shift");
                        if (win)   parts.Add("Win");
                        parts.Add(VkToHotkeyName(key));
                        item.HotkeyString = string.Join("+", parts);
                        hk.Text = item.HotkeyString;
                        StopRecording(false);
                    };
                    recSwitch.Toggled += (_, _) =>
                    {
                        isRecording = recSwitch.IsOn; hk.IsReadOnly = isRecording;
                        modPanel.Visibility = isRecording ? Visibility.Visible : Visibility.Collapsed;
                        if (isRecording)
                        {
                            btnCtrl.IsChecked = btnAlt.IsChecked =
                            btnShift.IsChecked = btnWin.IsChecked = false;
                            hk.PlaceholderText = "按下主键..."; hk.Text = "";
                            hk.Focus(FocusState.Programmatic);
                        }
                        else
                        {
                            hk.PlaceholderText = "Ctrl+Alt+F5";
                            if (string.IsNullOrEmpty(hk.Text)) hk.Text = item.HotkeyString ?? "";
                        }
                    };
                    hk.TextChanged += (_, _) => { if (!isRecording) item.HotkeyString = hk.Text; };
                    paramStack.Children.Add(hk);
                    paramStack.Children.Add(modPanel);
                    paramStack.Children.Add(recSwitch);
                    paramStack.Children.Add(new TextBlock
                    {
                        Text = "特殊键名：Win · Ctrl · Alt · Shift · F1–F12 · Enter · Escape · Space · Tab · Backspace · Delete · Left · Right · Up · Down · Home · End · PageUp · PageDown · PrintScreen · CapsLock · Insert",
                        FontSize = 11, Opacity = 0.45, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                    break;
                }
                case ActionType.SimulateClick:
                    var cl = new TextBox { Header = "坐标", Text = item.ClickTarget ?? "", PlaceholderText = "960,540 或 rel:100,200" };
                    cl.TextChanged += (_, _) => item.ClickTarget = cl.Text;
                    paramStack.Children.Add(cl);
                    paramStack.Children.Add(new TextBlock { Text = "绝对坐标：x,y；相对前台窗口左上角：rel:dx,dy", FontSize=12, Opacity=0.5 });
                    break;
                case ActionType.Script:
                    var shellCombo = new ComboBox { Header = "解释器", ItemsSource = new[]{"cmd","powershell","python"}, SelectedItem = item.ScriptShell };
                    shellCombo.SelectionChanged += (_, _) => item.ScriptShell = shellCombo.SelectedItem as string ?? "cmd";
                    var scriptPath = new TextBox { Header = "脚本路径（留空用内联）", Text = item.ScriptPath ?? "" };
                    scriptPath.TextChanged += (_, _) => item.ScriptPath = scriptPath.Text;
                    var scriptInline = new TextBox { Header = "内联脚本", Text = item.ScriptInline ?? "", AcceptsReturn=true, TextWrapping=TextWrapping.Wrap, MinHeight=80 };
                    scriptInline.TextChanged += (_, _) => item.ScriptInline = scriptInline.Text;
                    var silent = new ToggleSwitch { Header = "静默执行（隐藏窗口）", IsOn = item.ScriptSilent };
                    silent.Toggled += (_, _) => item.ScriptSilent = silent.IsOn;
                    paramStack.Children.Add(shellCombo); paramStack.Children.Add(scriptPath);
                    paramStack.Children.Add(scriptInline); paramStack.Children.Add(silent);
                    break;
                case ActionType.OpenUrl:
                    var url = new TextBox { Header = "网址 / 文件路径 / 文件夹路径", Text = item.UrlOrPath ?? "", PlaceholderText = "https://example.com 或 C:\\..." };
                    url.TextChanged += (_, _) => item.UrlOrPath = url.Text;
                    paramStack.Children.Add(url);
                    break;
                case ActionType.LaunchApp:
                    var appPath = new TextBox { Header = "程序路径", Text = item.UrlOrPath ?? "", PlaceholderText = "C:\\Windows\\notepad.exe" };
                    appPath.TextChanged += (_, _) => item.UrlOrPath = appPath.Text;
                    var appArgs = new TextBox { Header = "启动参数（可选）", Text = item.LaunchArgs ?? "" };
                    appArgs.TextChanged += (_, _) => item.LaunchArgs = appArgs.Text;
                    paramStack.Children.Add(appPath); paramStack.Children.Add(appArgs);
                    break;
                case ActionType.KillProcess:
                    var proc = new TextBox { Header = "进程名或 PID", Text = item.TargetProcess ?? "", PlaceholderText = "notepad 或 1234" };
                    proc.TextChanged += (_, _) => item.TargetProcess = proc.Text;
                    paramStack.Children.Add(proc);
                    paramStack.Children.Add(new TextBlock { Text = "支持不含 .exe 的进程名（如 notepad）或精确 PID。", FontSize=12, Opacity=0.5 });
                    break;
                case ActionType.MediaControl:
                    var mediaCombo = new ComboBox
                    {
                        Header = "媒体指令",
                        ItemsSource = new[]
                        {
                            "PlayPause — 播放/暂停", "Next — 下一首", "Prev — 上一首", "Stop — 停止",
                            "VolumeUp — 音量+", "VolumeDown — 音量-", "Mute — 静音切换",
                        },
                        SelectedIndex = (int)item.MediaCmd
                    };
                    mediaCombo.SelectionChanged += (_, _) => item.MediaCmd = (MediaCommand)mediaCombo.SelectedIndex;
                    paramStack.Children.Add(mediaCombo);
                    break;
                case ActionType.SendText:
                    var txt = new TextBox { Header = "要输入的文字", Text = item.TextToSend ?? "", AcceptsReturn=true, TextWrapping=TextWrapping.Wrap, MinHeight=60, PlaceholderText = "支持任意 Unicode 文字" };
                    txt.TextChanged += (_, _) => item.TextToSend = txt.Text;
                    paramStack.Children.Add(txt);
                    paramStack.Children.Add(new TextBlock { Text = "执行后会将文字发送到当前焦点控件（相当于打字）。", FontSize=12, Opacity=0.5 });
                    break;
            }
        }

        typeCombo.SelectionChanged += (_, _) => { item.Type = (ActionType)typeCombo.SelectedIndex; RebuildParams(item.Type); };
        RebuildParams(item.Type);
        panel.Children.Add(labelBox);
        panel.Children.Add(typeCombo);
        panel.Children.Add(paramStack);

        var dlg = new ContentDialog
        {
            Title = existing == null ? "添加操作" : "编辑操作",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = "确定", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = xamlRoot
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;
        item.Label = labelBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(item.Label)) return null;
        return item;
    }

    private static string VkToHotkeyName(VirtualKey key) => key switch
    {
        VirtualKey.Escape      => "Escape",   VirtualKey.Enter    => "Enter",
        VirtualKey.Space       => "Space",    VirtualKey.Back     => "Backspace",
        VirtualKey.Delete      => "Delete",   VirtualKey.Left     => "Left",
        VirtualKey.Up          => "Up",       VirtualKey.Right    => "Right",
        VirtualKey.Down        => "Down",     VirtualKey.Home     => "Home",
        VirtualKey.End         => "End",      VirtualKey.PageUp   => "PageUp",
        VirtualKey.PageDown    => "PageDown", VirtualKey.Tab      => "Tab",
        VirtualKey.Insert      => "Insert",   VirtualKey.Snapshot => "PrintScreen",
        VirtualKey.CapitalLock => "CapsLock",
        VirtualKey.F1  => "F1",  VirtualKey.F2  => "F2",  VirtualKey.F3  => "F3",
        VirtualKey.F4  => "F4",  VirtualKey.F5  => "F5",  VirtualKey.F6  => "F6",
        VirtualKey.F7  => "F7",  VirtualKey.F8  => "F8",  VirtualKey.F9  => "F9",
        VirtualKey.F10 => "F10", VirtualKey.F11 => "F11", VirtualKey.F12 => "F12",
        VirtualKey.Number0 => "0", VirtualKey.Number1 => "1", VirtualKey.Number2 => "2",
        VirtualKey.Number3 => "3", VirtualKey.Number4 => "4", VirtualKey.Number5 => "5",
        VirtualKey.Number6 => "6", VirtualKey.Number7 => "7", VirtualKey.Number8 => "8",
        VirtualKey.Number9 => "9",
        >= VirtualKey.A and <= VirtualKey.Z => key.ToString(),
        _ => key.ToString()
    };
}
