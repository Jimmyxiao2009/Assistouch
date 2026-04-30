using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AssistiveTouch.Models;

// ── Backdrop / Theme ──────────────────────────────────────────
public enum BackdropType     { None, Acrylic, Mica, MicaAlt }
public enum ThemeMode        { System, Light, Dark }
public enum ButtonBackdropType { None, Acrylic, Mica, MicaAlt }

// ── Action type ────────────────────────────────────────────────
public enum ActionType
{
    SimulateClick,   // 模拟鼠标点击
    Hotkey,          // 发送快捷键
    Script,          // 执行脚本
    ShowDesktop,     // 显示桌面（内置）
    OpenUrl,         // 打开网址 / 文件 / 文件夹
    LaunchApp,       // 启动程序
    KillProcess,     // 结束进程
    MediaControl,    // 媒体 / 音量控制
    SendText,        // 发送文本输入
    LockScreen,      // 锁定屏幕
    Custom           // 保留
}

public enum MediaCommand
{
    PlayPause,
    Next,
    Prev,
    Stop,
    VolumeUp,
    VolumeDown,
    Mute
}

public enum MatchType
{
    ProcessName,
    ExeName,
    Pid,
    WindowTitleRegex,
    ProcessNameRegex
}

// ── Single executable action ───────────────────────────────────
public class ActionItem
{
    public string Id            { get; set; } = Guid.NewGuid().ToString();
    public string Label         { get; set; } = string.Empty;
    public string Icon          { get; set; } = "\uE8A5";
    public ActionType Type      { get; set; } = ActionType.Hotkey;

    // Hotkey
    public string? HotkeyString { get; set; }

    // SimulateClick
    public string? ClickTarget  { get; set; }

    // Script
    public string? ScriptPath   { get; set; }
    public string? ScriptInline { get; set; }
    public bool    ScriptSilent { get; set; } = true;
    public string  ScriptShell  { get; set; } = "cmd";

    // OpenUrl / LaunchApp
    public string? UrlOrPath    { get; set; }
    public string? LaunchArgs   { get; set; }

    // KillProcess
    public string? TargetProcess { get; set; }

    // MediaControl
    public MediaCommand MediaCmd { get; set; } = MediaCommand.PlayPause;

    // SendText
    public string? TextToSend   { get; set; }

    /// <summary>Display-friendly Chinese name for the action type (used in list bindings).</summary>
    [JsonIgnore]
    public string TypeDisplayName => Type switch
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
        _                        => Type.ToString(),
    };
}

// ── Rule ──────────────────────────────────────────────────────
public class AppRule
{
    public string Id        { get; set; } = Guid.NewGuid().ToString();
    public string Name      { get; set; } = string.Empty;
    public bool   Enabled   { get; set; } = true;
    public List<RuleMatcher> Matchers          { get; set; } = new();
    public List<ActionItem>  RecommendedActions { get; set; } = new();
}

public class RuleMatcher
{
    public MatchType Type  { get; set; } = MatchType.ProcessName;
    public string    Value { get; set; } = string.Empty;
}

// ── Widget ────────────────────────────────────────────────────
public class WidgetItem
{
    public string Id            { get; set; } = Guid.NewGuid().ToString();
    public string Label         { get; set; } = string.Empty;
    public string Icon          { get; set; } = "\uE74C";
    public List<ActionItem> Actions { get; set; } = new();
}

// ── Config root ───────────────────────────────────────────────
public class AppConfig
{
    public List<AppRule>    Rules         { get; set; } = new();
    public List<ActionItem> PinnedActions { get; set; } = new();
    public List<WidgetItem> Widgets       { get; set; } = new();

    public double ButtonX       { get; set; } = 0.92;
    public double ButtonY       { get; set; } = 0.5;
    public int    ButtonSize    { get; set; } = 52;
    public bool   StartWithWindows { get; set; } = false;

    public ButtonBackdropType ButtonBackdrop { get; set; } = ButtonBackdropType.Acrylic;

    public BackdropType BackdropType { get; set; } = BackdropType.Mica;
    public ThemeMode    ThemeMode    { get; set; } = ThemeMode.System;

    // Persists which widget groups are collapsed in the action menu.
    // Key = WidgetItem.Id, Value = true means collapsed.
    public Dictionary<string, bool> WidgetCollapsed { get; set; } = new();

    // Bumped whenever CreateDefaultRules adds new entries, so existing
    // users get them merged in automatically on next launch.
    public int ConfigVersion { get; set; } = 0;
}

// ── Source-generated JSON context (trim-safe) ──────────────────────────────
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(List<AppRule>))]
[JsonSerializable(typeof(List<ActionItem>))]
[JsonSerializable(typeof(List<WidgetItem>))]
[JsonSerializable(typeof(List<RuleMatcher>))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
internal partial class AppJsonContext : JsonSerializerContext { }
