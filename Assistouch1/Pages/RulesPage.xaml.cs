using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AssistiveTouch.Models;
using AssistiveTouch.Win32;
using MatchType = AssistiveTouch.Models.MatchType;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AssistiveTouch.Pages;

public sealed partial class RulesPage : Page
{
    private List<AppRule> Rules => App.Config.Config.Rules;
    private AppRule? _selected;

    // ── card style helpers ─────────────────────────────────────
    private static Border MakeCard(UIElement content, Thickness? margin = null)
    {
        var b = new Border
        {
            Background      = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush     = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(16, 14, 16, 14),
            Margin          = margin ?? new Thickness(0),
            Child           = content
        };
        return b;
    }

    private static Border MakeDivider() => new Border
    {
        Height     = 1,
        Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
        Margin     = new Thickness(-16, 0, -16, 0)
    };

    private static TextBlock SectionHeader(string text) => new TextBlock
    {
        Text   = text,
        Style  = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        Margin = new Thickness(2, 0, 0, 0)
    };

    // ── Page ──────────────────────────────────────────────────
    public RulesPage()
    {
        InitializeComponent();
        RefreshList();
    }

    private void RefreshList()
    {
        RuleList.ItemsSource = null;
        RuleList.ItemsSource = Rules;
    }

    private void AddRuleBtn_Click(object sender, RoutedEventArgs e)
    {
        var rule = new AppRule { Name = "新规则" };
        Rules.Add(rule);
        App.Config.Save();
        RefreshList();
        RuleList.SelectedItem = rule;
    }

    private void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = RuleList.SelectedItem as AppRule;
        ShowDetail(_selected);
    }

    // ── Detail ────────────────────────────────────────────────
    private void ShowDetail(AppRule? rule)
    {
        DetailPanel.Children.Clear();

        if (rule == null)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text    = "← 从左侧选择一条规则",
                Opacity = 0.45,
                Style   = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                Margin  = new Thickness(2, 8, 0, 0)
            });
            return;
        }

        // ── Rule name card ──────────────────────────────────────
        DetailPanel.Children.Add(SectionHeader("规则设置"));

        var cardStack = new StackPanel { Spacing = 0 };

        // Name row
        var nameBox = new TextBox
        {
            Header          = "规则名称",
            Text            = rule.Name,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        nameBox.TextChanged += (_, _) => rule.Name = nameBox.Text;
        nameBox.LostFocus   += (_, _) => { RefreshList(); App.Config.Save(); };
        cardStack.Children.Add(nameBox);

        cardStack.Children.Add(new Border { Height = 12 }); // breathing room

        // Enable / delete row
        var enableToggle = new ToggleSwitch
        {
            Header     = "启用此规则",
            IsOn       = rule.Enabled,
            OnContent  = "",
            OffContent = ""
        };
        enableToggle.Toggled += (_, _) => { rule.Enabled = enableToggle.IsOn; App.Config.Save(); };

        var delBtn = new HyperlinkButton
        {
            Content    = "删除此规则",
            Foreground = new SolidColorBrush(Colors.OrangeRed),
            Margin     = new Thickness(0, 4, 0, 0)
        };
        delBtn.Click += (_, _) =>
        {
            Rules.Remove(rule);
            App.Config.Save();
            RefreshList();
            DetailPanel.Children.Clear();
            ShowDetail(null);
        };

        cardStack.Children.Add(enableToggle);
        cardStack.Children.Add(delBtn);
        DetailPanel.Children.Add(MakeCard(cardStack));

        // ── Matchers ────────────────────────────────────────────
        DetailPanel.Children.Add(SectionHeader("匹配条件"));

        // ── 拖拽识别窗口 ────────────────────────────────────────
        bool isPicking = false;
        Microsoft.UI.Dispatching.DispatcherQueueTimer? pickTimer = null;

        var pickerStatus = new TextBlock
        {
            Text              = "按住拖到目标窗口，松开自动填充进程名",
            Style             = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity           = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping      = TextWrapping.Wrap,
            Margin            = new Thickness(10, 0, 0, 0)
        };

        var dragHandle = new Border
        {
            Width        = 36,
            Height       = 36,
            CornerRadius = new CornerRadius(6),
            Background   = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Child        = new FontIcon
            {
                Glyph      = "\uE8FA",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize   = 16,
                Foreground = new SolidColorBrush(Colors.White)
            }
        };

        var pickerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 2, 0, 4)
        };
        pickerRow.Children.Add(dragHandle);
        pickerRow.Children.Add(pickerStatus);

        DetailPanel.Children.Add(pickerRow);

        // matcherCard 必须在 PointerReleased 事件注册之前声明，
        // 否则 C# 定性赋值分析会认为它在 RebuildMatchers() 中可能未赋值。
        var matcherCard = new StackPanel { Spacing = 0 };

        dragHandle.PointerPressed += (_, pe) =>
        {
            dragHandle.CapturePointer(pe.Pointer);
            isPicking            = true;
            pickerStatus.Opacity = 1.0;
            pickerStatus.Text    = "正在识别... 拖到目标应用窗口后松开";

            pickTimer           = DispatcherQueue.CreateTimer();
            pickTimer.Interval  = TimeSpan.FromMilliseconds(80);
            pickTimer.IsRepeating = true;
            pickTimer.Tick     += (_, _) =>
            {
                if (!isPicking) return;
                NativeMethods.GetCursorPos(out var cur);
                var hwnd = NativeMethods.WindowFromPoint(cur);
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0 || pid == (uint)System.Environment.ProcessId)
                {
                    pickerStatus.Text = "移到其他应用窗口上...";
                    return;
                }
                var (procName, _, title) = GetWindowInfo(hwnd);
                pickerStatus.Text = $"📌 {procName}   {title}";
            };
            pickTimer.Start();
        };

        dragHandle.PointerReleased += (_, _) =>
        {
            if (!isPicking) return;
            isPicking = false;
            pickTimer?.Stop();
            dragHandle.ReleasePointerCaptures();

            NativeMethods.GetCursorPos(out var cur);
            var hwnd = NativeMethods.WindowFromPoint(cur);
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);

            if (pid == 0 || pid == (uint)System.Environment.ProcessId)
            {
                pickerStatus.Text    = "未识别到其他应用窗口，请重试";
                pickerStatus.Opacity = 0.55;
                return;
            }

            var (procName, _, _) = GetWindowInfo(hwnd);
            rule.Matchers.Clear();
            if (!string.IsNullOrEmpty(procName))
                rule.Matchers.Add(new RuleMatcher { Type = MatchType.ProcessName, Value = procName });
            App.Config.Save();
            RebuildMatchers();

            pickerStatus.Text    = $"✅ 已识别：{procName}";
            pickerStatus.Opacity = 0.75;
        };

        dragHandle.PointerCaptureLost += (_, _) =>
        {
            isPicking = false;
            pickTimer?.Stop();
        };

        void RebuildMatchers()
        {
            matcherCard.Children.Clear();
            bool first = true;
            foreach (var m in rule.Matchers.ToList())
            {
                if (!first) matcherCard.Children.Add(MakeDivider());
                first = false;

                var innerRow = new Grid { Margin = new Thickness(0, 8, 0, 8) };
                innerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                innerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                innerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                innerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var typeCombo = new ComboBox
                {
                    ItemsSource = new[]
                    {
                        "进程名",
                        "EXE 文件名",
                        "进程 PID",
                        "窗口标题（正则）",
                        "进程名（正则）",
                    },
                    SelectedIndex = (int)m.Type,
                    VerticalAlignment = VerticalAlignment.Center
                };
                typeCombo.SelectionChanged += (_, _) => { m.Type = (MatchType)typeCombo.SelectedIndex; App.Config.Save(); };

                var valBox = new TextBox
                {
                    Text            = m.Value,
                    PlaceholderText = "进程名 / 路径 / PID / 正则",
                    VerticalAlignment = VerticalAlignment.Center
                };
                valBox.TextChanged += (_, _) => { m.Value = valBox.Text; App.Config.Save(); };

                var rmBtn = new Button
                {
                    Width   = 32,
                    Height  = 32,
                    Padding = new Thickness(0),
                    Margin  = new Thickness(6, 0, 0, 0),
                    Content = new FontIcon
                    {
                        Glyph      = "\uE74D",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize   = 12
                    },
                    VerticalAlignment = VerticalAlignment.Center
                };
                var cap = m;
                rmBtn.Click += (_, _) => { rule.Matchers.Remove(cap); App.Config.Save(); RebuildMatchers(); };

                Grid.SetColumn(typeCombo, 0);
                Grid.SetColumn(valBox,    2);
                Grid.SetColumn(rmBtn,     3);
                innerRow.Children.Add(typeCombo);
                innerRow.Children.Add(valBox);
                innerRow.Children.Add(rmBtn);
                matcherCard.Children.Add(innerRow);
            }

            // Add matcher button at bottom
            if (!first) matcherCard.Children.Add(MakeDivider());
            var addMatcherBtn = new HyperlinkButton
            {
                Content = "+ 添加匹配条件",
                Margin  = new Thickness(0, 4, 0, 0)
            };
            addMatcherBtn.Click += (_, _) =>
            {
                rule.Matchers.Add(new RuleMatcher());
                App.Config.Save();
                RebuildMatchers();
            };
            matcherCard.Children.Add(addMatcherBtn);
        }
        RebuildMatchers();
        DetailPanel.Children.Add(MakeCard(matcherCard));

        // ── Recommended actions ─────────────────────────────────
        DetailPanel.Children.Add(SectionHeader("推荐操作"));

        var actCard = new StackPanel { Spacing = 0 };
        void RebuildActions()
        {
            actCard.Children.Clear();
            bool first = true;
            foreach (var a in rule.RecommendedActions.ToList())
            {
                if (!first) actCard.Children.Add(MakeDivider());
                first = false;

                var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var iconBorder = new Border
                {
                    Width        = 32,
                    Height       = 32,
                    CornerRadius = new CornerRadius(6),
                    Background   = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    Child        = new FontIcon
                    {
                        Glyph      = a.Icon,
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize   = 14,
                        Foreground = new SolidColorBrush(Colors.White)
                    }
                };

                var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                labelStack.Children.Add(new TextBlock
                {
                    Text  = a.Label,
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
                });
                labelStack.Children.Add(new TextBlock
                {
                    Text    = ActionTypeName(a.Type),
                    Style   = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.55
                });

                var editBtn = new Button
                {
                    Content = "编辑",
                    Margin  = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var rmBtn = new Button
                {
                    Width   = 32,
                    Height  = 32,
                    Padding = new Thickness(0),
                    Margin  = new Thickness(4, 0, 0, 0),
                    Content = new FontIcon
                    {
                        Glyph      = "\uE74D",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize   = 12
                    },
                    VerticalAlignment = VerticalAlignment.Center
                };

                var capA = a;
                editBtn.Click += (_, _) => _ = EditActionAsync(capA, rule, RebuildActions);
                rmBtn.Click   += (_, _) => { rule.RecommendedActions.Remove(capA); App.Config.Save(); RebuildActions(); };

                Grid.SetColumn(iconBorder,  0);
                Grid.SetColumn(labelStack,  2);
                Grid.SetColumn(editBtn,     3);
                Grid.SetColumn(rmBtn,       4);
                row.Children.Add(iconBorder);
                row.Children.Add(labelStack);
                row.Children.Add(editBtn);
                row.Children.Add(rmBtn);
                actCard.Children.Add(row);
            }

            if (!first) actCard.Children.Add(MakeDivider());
            var addActBtn = new HyperlinkButton
            {
                Content = "+ 添加推荐操作",
                Margin  = new Thickness(0, 4, 0, 0)
            };
            addActBtn.Click += (_, _) => _ = AddActionAsync(rule, RebuildActions);
            actCard.Children.Add(addActBtn);
        }
        RebuildActions();
        DetailPanel.Children.Add(MakeCard(actCard));

        // ── Test button ─────────────────────────────────────────
        var testBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left
        };
        testBtn.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 6,
            Children    =
            {
                new FontIcon { Glyph = "\uE773", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13 },
                new TextBlock { Text = "测试：对当前前台窗口匹配" }
            }
        };
        testBtn.Click += (_, _) =>
        {
            // CaptureContext() 与悬浮菜单逻辑一致：
            // 当前前台是 Assistouch 自身时，自动返回上一个外部窗口。
            var fg    = App.FgWatcher.CaptureContext();
            bool hit  = App.Rules.GetRecommendedActions(fg).Any();
            var dlg   = new ContentDialog
            {
                Title  = "匹配结果",
                Content = new TextBlock
                {
                    Text         = $"前台进程：{fg.ProcessName}\n窗口标题：{fg.WindowTitle}\n\n" +
                                   (hit ? "✅ 该规则命中！" : "❌ 未命中"),
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = "关闭",
                XamlRoot        = XamlRoot
            };
            _ = dlg.ShowAsync();
        };
        DetailPanel.Children.Add(testBtn);
    }

    private async Task EditActionAsync(ActionItem a, AppRule rule, Action rebuild)
    {
        var edited = await PinnedPage.ShowActionDialog(a, XamlRoot);
        if (edited != null)
        {
            var idx = rule.RecommendedActions.IndexOf(a);
            if (idx >= 0) rule.RecommendedActions[idx] = edited;
            App.Config.Save();
            rebuild();
        }
    }

    private async Task AddActionAsync(AppRule rule, Action rebuild)
    {
        var newAct = await PinnedPage.ShowActionDialog(null, XamlRoot);
        if (newAct != null)
        {
            rule.RecommendedActions.Add(newAct);
            App.Config.Save();
            rebuild();
        }
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

    /// <summary>
    /// 通过窗口句柄获取进程名、EXE 路径和窗口标题。
    /// 供拖拽识别功能使用。
    /// </summary>
    private static (string processName, string exePath, string windowTitle) GetWindowInfo(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        var titleSb = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, titleSb, 512);

        string processName = "", exePath = "";
        var hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc != IntPtr.Zero)
        {
            try
            {
                uint size = 1024;
                var sb = new StringBuilder((int)size);
                if (NativeMethods.QueryFullProcessImageName(hProc, 0, sb, ref size))
                {
                    exePath     = sb.ToString();
                    processName = System.IO.Path.GetFileName(exePath);
                }
            }
            finally { NativeMethods.CloseHandle(hProc); }
        }
        return (processName, exePath, titleSb.ToString());
    }
}
