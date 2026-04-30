using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AssistiveTouch.Models;
using MatchType = AssistiveTouch.Models.MatchType;  // disambiguate from System.IO.MatchType

#pragma warning disable CA1416  // Windows-only APIs — this project targets Windows exclusively

namespace AssistiveTouch.Services;

public class ConfigService
{
    private static readonly object SaveLock = new();
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AssistiveTouch", "config.json");

    // Bump this whenever new default rules are added so existing users get them merged.
    private const int CurrentConfigVersion = 3;

    public AppConfig Config { get; private set; } = new();

    public ConfigService() => Load();

    public void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                Config = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig) ?? new AppConfig();
                MigrateIfNeeded();
            }
            else
            {
                Config = CreateDefaults();
                Config.ConfigVersion = CurrentConfigVersion;
            }
        }
        catch (Exception ex) { CrashLogger.Write(ex, "ConfigService.Load"); Config = CreateDefaults(); }
    }

    public void Save()
    {
        try
        {
            lock (SaveLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config, AppJsonContext.Default.AppConfig));
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write(ex, "ConfigService.Save");
        }
    }

    // ── Migration ─────────────────────────────────────────────────────────

    private void MigrateIfNeeded()
    {
        if (Config.ConfigVersion >= CurrentConfigVersion) return;

        // Merge any default rules whose Name isn't already present.
        // Rules the user deliberately deleted stay deleted (we only add, never overwrite).
        var existing = new HashSet<string>(Config.Rules.Select(r => r.Name),
                                           StringComparer.OrdinalIgnoreCase);
        foreach (var rule in CreateDefaults().Rules)
            if (!existing.Contains(rule.Name))
                Config.Rules.Add(rule);

        Config.ConfigVersion = CurrentConfigVersion;
        Save();
    }

    // ── Defaults ──────────────────────────────────────────────────────────

    private static AppConfig CreateDefaults() => new AppConfig
    {
        ConfigVersion = CurrentConfigVersion,
        PinnedActions = new List<ActionItem>
        {
            new ActionItem { Label = "返回桌面",  Icon = "\uE8A1", Type = ActionType.ShowDesktop },
            new ActionItem { Label = "任务视图",  Icon = "\uE7C4", Type = ActionType.Hotkey, HotkeyString = "Win+Tab" },
            new ActionItem { Label = "截图",      Icon = "\uE722", Type = ActionType.Hotkey, HotkeyString = "Win+Shift+S" },
            new ActionItem { Label = "快速批注",  Icon = "\uE932", Type = ActionType.OpenUrl, UrlOrPath = "ms-screensketch:" },
        },
        Rules = CreateDefaultRules(),
        Widgets = new List<WidgetItem>
        {
            new WidgetItem
            {
                Label = "系统控制",
                Icon = "\uE713",
                Actions = new List<ActionItem>
                {
                    new ActionItem { Label = "锁屏",     Icon = "\uE72E", Type = ActionType.Hotkey, HotkeyString = "Win+L" },
                    new ActionItem { Label = "通知中心", Icon = "\uE91C", Type = ActionType.Hotkey, HotkeyString = "Win+A" },
                    new ActionItem { Label = "设置",     Icon = "\uE713", Type = ActionType.Hotkey, HotkeyString = "Win+I" },
                    new ActionItem { Label = "任务管理器", Icon = "\uE9D9", Type = ActionType.Hotkey, HotkeyString = "Ctrl+Shift+Escape" },
                }
            }
        }
    };

    private static List<AppRule> CreateDefaultRules() => new()
    {
        // ── Microsoft PowerPoint ──────────────────────────────────────────
        new AppRule
        {
            Name = "PowerPoint 放映",
            Matchers = new List<RuleMatcher> { new() { Type = MatchType.ProcessName, Value = "POWERPNT.EXE" } },
            RecommendedActions = new List<ActionItem>
            {
                new() { Label = "退出放映",  Icon = "\uE711", Type = ActionType.Hotkey, HotkeyString = "Escape" },
                new() { Label = "下一页",    Icon = "\uE76C", Type = ActionType.Hotkey, HotkeyString = "Right" },
                new() { Label = "上一页",    Icon = "\uE76B", Type = ActionType.Hotkey, HotkeyString = "Left" },
                new() { Label = "快速批注",  Icon = "\uE932", Type = ActionType.Hotkey, HotkeyString = "Ctrl+P" },
                new() { Label = "橡皮擦",    Icon = "\uED60", Type = ActionType.Hotkey, HotkeyString = "Ctrl+E" },
                new() { Label = "黑屏",      Icon = "\uE7FC", Type = ActionType.Hotkey, HotkeyString = "B" },
                new() { Label = "白屏",      Icon = "\uEB9F", Type = ActionType.Hotkey, HotkeyString = "W" },
                new() { Label = "强退放映",  Icon = "\uE711", Type = ActionType.Hotkey, HotkeyString = "Alt+F4" },
            }
        },

        // ── WPS 演示 ──────────────────────────────────────────────────────
        new AppRule
        {
            Name = "WPS 演示",
            Matchers = new List<RuleMatcher> { new() { Type = MatchType.ProcessName, Value = "wpp.exe" } },
            RecommendedActions = new List<ActionItem>
            {
                new() { Label = "开始放映",  Icon = "\uE714", Type = ActionType.Hotkey, HotkeyString = "F5" },
                new() { Label = "退出放映",  Icon = "\uE711", Type = ActionType.Hotkey, HotkeyString = "Escape" },
                new() { Label = "强退放映",  Icon = "\uE711", Type = ActionType.Hotkey, HotkeyString = "Alt+F4" },
                new() { Label = "下一页",    Icon = "\uE76C", Type = ActionType.Hotkey, HotkeyString = "Right" },
                new() { Label = "上一页",    Icon = "\uE76B", Type = ActionType.Hotkey, HotkeyString = "Left" },
                new() { Label = "快速批注",  Icon = "\uE932", Type = ActionType.Hotkey, HotkeyString = "Ctrl+P" },
                new() { Label = "橡皮擦",    Icon = "\uED60", Type = ActionType.Hotkey, HotkeyString = "Ctrl+E" },
                new() { Label = "保存",      Icon = "\uE74E", Type = ActionType.Hotkey, HotkeyString = "Ctrl+S" },
            }
        },

        // ── WPS 文字 ──────────────────────────────────────────────────────
        new AppRule
        {
            Name = "WPS 文字",
            Matchers = new List<RuleMatcher> { new() { Type = MatchType.ProcessName, Value = "wps.exe" } },
            RecommendedActions = new List<ActionItem>
            {
                new() { Label = "保存",      Icon = "\uE74E", Type = ActionType.Hotkey, HotkeyString = "Ctrl+S" },
                new() { Label = "撤销",      Icon = "\uE7A7", Type = ActionType.Hotkey, HotkeyString = "Ctrl+Z" },
                new() { Label = "查找",      Icon = "\uE721", Type = ActionType.Hotkey, HotkeyString = "Ctrl+F" },
                new() { Label = "快速批注",  Icon = "\uE932", Type = ActionType.OpenUrl, UrlOrPath = "ms-screensketch:" },
            }
        },

        // ── WPS 表格 ──────────────────────────────────────────────────────
        new AppRule
        {
            Name = "WPS 表格",
            Matchers = new List<RuleMatcher> { new() { Type = MatchType.ProcessName, Value = "et.exe" } },
            RecommendedActions = new List<ActionItem>
            {
                new() { Label = "保存",      Icon = "\uE74E", Type = ActionType.Hotkey, HotkeyString = "Ctrl+S" },
                new() { Label = "撤销",      Icon = "\uE7A7", Type = ActionType.Hotkey, HotkeyString = "Ctrl+Z" },
                new() { Label = "查找",      Icon = "\uE721", Type = ActionType.Hotkey, HotkeyString = "Ctrl+F" },
                new() { Label = "快速批注",  Icon = "\uE932", Type = ActionType.OpenUrl, UrlOrPath = "ms-screensketch:" },
            }
        },

        // ── 浏览器（Chrome / Edge / Firefox / Brave / 360）───────────────
        new AppRule
        {
            Name = "浏览器",
            Matchers = new List<RuleMatcher>
            {
                new() { Type = MatchType.ProcessNameRegex,
                        Value = @"^(chrome|msedge|firefox|brave|360se|360chrome)\.exe$" }
            },
            RecommendedActions = new List<ActionItem>
            {
                new() { Label = "新建标签页", Icon = "\uE710", Type = ActionType.Hotkey, HotkeyString = "Ctrl+T" },
                new() { Label = "关闭标签页", Icon = "\uE711", Type = ActionType.Hotkey, HotkeyString = "Ctrl+W" },
                new() { Label = "刷新",       Icon = "\uE72C", Type = ActionType.Hotkey, HotkeyString = "F5" },
                new() { Label = "全屏",       Icon = "\uE740", Type = ActionType.Hotkey, HotkeyString = "F11" },
                new() { Label = "快速批注",   Icon = "\uE932", Type = ActionType.OpenUrl, UrlOrPath = "ms-screensketch:" },
            }
        },

        // ── 希沃白板 5（EasiNote5.exe）────────────────────────────────────
        new AppRule
        {
            Name = "希沃白板",
            Matchers = new List<RuleMatcher>
            {
                new() { Type = MatchType.ProcessNameRegex, Value = @"^EasiNote\d*\.exe$" }
            },
            RecommendedActions = new List<ActionItem>
            {
                new() { Label = "快速批注",   Icon = "\uE932", Type = ActionType.OpenUrl, UrlOrPath = "ms-screensketch:" },
                new() { Label = "截图",       Icon = "\uE722", Type = ActionType.Hotkey, HotkeyString = "Win+Shift+S" },
                new() { Label = "撤销",       Icon = "\uE7A7", Type = ActionType.Hotkey, HotkeyString = "Ctrl+Z" },
            }
        },

        // ── 鸿合白板（HiteBoard / HiteTouchBoard）────────────────────────
        new AppRule
        {
            Name = "鸿合白板",
            Matchers = new List<RuleMatcher>
            {
                new() { Type = MatchType.ProcessNameRegex,
                        Value = @"^(HiteBoard|HiteTouchBoard|HiBoard|HiteView)\.exe$" }
            },
            RecommendedActions = new List<ActionItem>
            {
                new() { Label = "快速批注",   Icon = "\uE932", Type = ActionType.OpenUrl, UrlOrPath = "ms-screensketch:" },
                new() { Label = "截图",       Icon = "\uE722", Type = ActionType.Hotkey, HotkeyString = "Win+Shift+S" },
                new() { Label = "撤销",       Icon = "\uE7A7", Type = ActionType.Hotkey, HotkeyString = "Ctrl+Z" },
            }
        },
    };
}
