using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AssistiveTouch.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AssistiveTouch.Pages;

/// <summary>
/// Left: widget group list. Right: actions inside selected widget.
/// </summary>
public sealed partial class WidgetsPage : Page
{
    private List<WidgetItem> Widgets => App.Config.Config.Widgets;
    private WidgetItem? _selected;

    // ── Card helpers ───────────────────────────────────────────
    private static Border MakeCard(UIElement content)
    {
        return new Border
        {
            Background      = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush     = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(16, 14, 16, 14),
            Child           = content
        };
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
    public WidgetsPage()
    {
        InitializeComponent();
        RefreshList();
    }

    private async void AddWidgetBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = await AskName("新建小工具组", "输入分组名称");
        if (name == null) return;
        var w = new WidgetItem { Label = name };
        Widgets.Add(w);
        App.Config.Save();
        RefreshList();
        WidgetList.SelectedItem = w;
    }

    private void WidgetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = WidgetList.SelectedItem as WidgetItem;
        ShowDetail(_selected);
    }

    private void RefreshList()
    {
        WidgetList.ItemsSource = null;
        WidgetList.ItemsSource = Widgets;
    }

    // ── Detail ────────────────────────────────────────────────
    private void ShowDetail(WidgetItem? widget)
    {
        DetailPanel.Children.Clear();

        if (widget == null)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text    = "← 从左侧选择一个分组",
                Opacity = 0.45,
                Style   = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                Margin  = new Thickness(2, 8, 0, 0)
            });
            return;
        }

        // ── Group settings card ─────────────────────────────────
        DetailPanel.Children.Add(SectionHeader("分组设置"));

        var settingsStack = new StackPanel { Spacing = 0 };

        var nameBox = new TextBox
        {
            Header              = "分组名称",
            Text                = widget.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        nameBox.TextChanged += (_, _) => { widget.Label = nameBox.Text; RefreshList(); App.Config.Save(); };

        var iconBox = new TextBox
        {
            Header          = "图标 Glyph（Segoe MDL2 Assets 字符）",
            Text            = widget.Icon,
            PlaceholderText = "\uE713",
            MaxLength       = 2,
            Margin          = new Thickness(0, 12, 0, 0)
        };
        iconBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(iconBox.Text))
            { widget.Icon = iconBox.Text; RefreshList(); App.Config.Save(); }
        };

        var delBtn = new HyperlinkButton
        {
            Content    = "删除此分组",
            Foreground = new SolidColorBrush(Colors.OrangeRed),
            Margin     = new Thickness(0, 8, 0, 0)
        };
        delBtn.Click += (_, _) =>
        {
            Widgets.Remove(widget);
            App.Config.Save();
            RefreshList();
            ShowDetail(null);
        };

        settingsStack.Children.Add(nameBox);
        settingsStack.Children.Add(iconBox);
        settingsStack.Children.Add(delBtn);
        DetailPanel.Children.Add(MakeCard(settingsStack));

        // ── Actions card ────────────────────────────────────────
        DetailPanel.Children.Add(SectionHeader("分组内操作"));

        var actCard = new StackPanel { Spacing = 0 };
        void RebuildActions()
        {
            actCard.Children.Clear();
            bool first = true;
            for (int i = 0; i < widget.Actions.Count; i++)
            {
                var a   = widget.Actions[i];
                var idx = i;

                if (!first) actCard.Children.Add(MakeDivider());
                first = false;

                var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Icon badge
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

                // Label + type
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

                // Reorder buttons
                var upBtn   = new Button { Content = "↑", Width = 28, Padding = new Thickness(0) };
                var downBtn = new Button { Content = "↓", Width = 28, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0) };
                upBtn.IsEnabled   = idx > 0;
                downBtn.IsEnabled = idx < widget.Actions.Count - 1;
                upBtn.Click   += (_, _) => { widget.Actions.RemoveAt(idx); widget.Actions.Insert(idx - 1, a); App.Config.Save(); RebuildActions(); };
                downBtn.Click += (_, _) => { widget.Actions.RemoveAt(idx); widget.Actions.Insert(idx + 1, a); App.Config.Save(); RebuildActions(); };

                var editBtn = new Button
                {
                    Content = "编辑",
                    Margin  = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                editBtn.Click += async (_, _) =>
                {
                    var edited = await PinnedPage.ShowActionDialog(a, XamlRoot);
                    if (edited != null) { widget.Actions[idx] = edited; App.Config.Save(); RebuildActions(); }
                };

                var rmBtn = new Button
                {
                    Width   = 32,
                    Height  = 32,
                    Padding = new Thickness(0),
                    Margin  = new Thickness(4, 0, 0, 0),
                    Content = new FontIcon { Glyph = "\uE74D", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12 },
                    VerticalAlignment = VerticalAlignment.Center
                };
                rmBtn.Click += (_, _) => { widget.Actions.RemoveAt(idx); App.Config.Save(); RebuildActions(); };

                Grid.SetColumn(iconBorder,  0);
                Grid.SetColumn(labelStack,  2);
                Grid.SetColumn(upBtn,       3);
                Grid.SetColumn(downBtn,     4);
                Grid.SetColumn(editBtn,     5);
                Grid.SetColumn(rmBtn,       6);
                row.Children.Add(iconBorder);
                row.Children.Add(labelStack);
                row.Children.Add(upBtn);
                row.Children.Add(downBtn);
                row.Children.Add(editBtn);
                row.Children.Add(rmBtn);
                actCard.Children.Add(row);
            }

            if (!first) actCard.Children.Add(MakeDivider());
            var addActBtn = new HyperlinkButton
            {
                Content = "+ 添加操作到此分组",
                Margin  = new Thickness(0, 4, 0, 0)
            };
            addActBtn.Click += async (_, _) =>
            {
                var newAct = await PinnedPage.ShowActionDialog(null, XamlRoot);
                if (newAct != null) { widget.Actions.Add(newAct); App.Config.Save(); RebuildActions(); }
            };
            actCard.Children.Add(addActBtn);
        }
        RebuildActions();
        DetailPanel.Children.Add(MakeCard(actCard));
    }

    private async Task<string?> AskName(string title, string placeholder)
    {
        var tb = new TextBox { PlaceholderText = placeholder };
        var dlg = new ContentDialog
        {
            Title             = title,
            Content           = tb,
            PrimaryButtonText = "确定",
            CloseButtonText   = "取消",
            DefaultButton     = ContentDialogButton.Primary,
            XamlRoot          = XamlRoot
        };
        var r = await dlg.ShowAsync();
        return r == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tb.Text)
            ? tb.Text.Trim()
            : null;
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
}
