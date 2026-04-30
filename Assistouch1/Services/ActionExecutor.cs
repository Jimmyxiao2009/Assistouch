using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using AssistiveTouch.Models;
using AssistiveTouch.Win32;

[ComImport, Guid("D8F015C0-C278-11CE-A49E-444553540000"),
 InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
file interface IShellApplication
{
    [DispId(0x60020006)] void ToggleDesktop();
    [DispId(0x60020007)] void MinimizeAll();
    [DispId(0x60020008)] void UndoMinimizeAll();
}

namespace AssistiveTouch.Services
{
    public class ActionExecutor
    {
        private static readonly Dictionary<string, ushort> VkMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Escape"] = 0x1B, ["Enter"] = 0x0D, ["Space"] = 0x20,
            ["Tab"] = 0x09, ["Backspace"] = 0x08, ["Delete"] = 0x2E,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
            ["PrintScreen"] = 0x2C, ["Insert"] = 0x2D, ["CapsLock"] = 0x14,
            ["F1"]  = 0x70, ["F2"]  = 0x71, ["F3"]  = 0x72, ["F4"]  = 0x73,
            ["F5"]  = 0x74, ["F6"]  = 0x75, ["F7"]  = 0x76, ["F8"]  = 0x77,
            ["F9"]  = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
            ["Ctrl"] = 0x11, ["Alt"] = 0x12, ["Shift"] = 0x10, ["Win"] = 0x5B,
            // A-Z
            ["A"]=0x41,["B"]=0x42,["C"]=0x43,["D"]=0x44,["E"]=0x45,
            ["F"]=0x46,["G"]=0x47,["H"]=0x48,["I"]=0x49,["J"]=0x4A,
            ["K"]=0x4B,["L"]=0x4C,["M"]=0x4D,["N"]=0x4E,["O"]=0x4F,
            ["P"]=0x50,["Q"]=0x51,["R"]=0x52,["S"]=0x53,["T"]=0x54,
            ["U"]=0x55,["V"]=0x56,["W"]=0x57,["X"]=0x58,["Y"]=0x59,["Z"]=0x5A,
            // 0-9
            ["0"]=0x30,["1"]=0x31,["2"]=0x32,["3"]=0x33,["4"]=0x34,
            ["5"]=0x35,["6"]=0x36,["7"]=0x37,["8"]=0x38,["9"]=0x39,
        };

        public void Execute(ActionItem action, ForegroundWindowInfo? foreground = null)
        {
            switch (action.Type)
            {
                case ActionType.Hotkey:
                    RestoreForegroundWindow(foreground);
                    if (action.HotkeyString is not null) SendHotkey(action.HotkeyString);
                    break;
                case ActionType.SimulateClick:
                    RestoreForegroundWindow(foreground);
                    if (action.ClickTarget is not null) SimulateClick(action.ClickTarget, foreground);
                    break;
                case ActionType.Script:
                    RunScript(action);
                    break;
                case ActionType.ShowDesktop:
                    ShowDesktop();
                    break;
                case ActionType.OpenUrl:
                    if (!string.IsNullOrWhiteSpace(action.UrlOrPath))
                        OpenUrl(action.UrlOrPath);
                    break;
                case ActionType.LaunchApp:
                    if (!string.IsNullOrWhiteSpace(action.UrlOrPath))
                        LaunchApp(action.UrlOrPath, action.LaunchArgs);
                    break;
                case ActionType.KillProcess:
                    if (!string.IsNullOrWhiteSpace(action.TargetProcess))
                        KillProcess(action.TargetProcess);
                    break;
                case ActionType.MediaControl:
                    SendMediaKey(action.MediaCmd);
                    break;
                case ActionType.SendText:
                    RestoreForegroundWindow(foreground);
                    if (!string.IsNullOrEmpty(action.TextToSend))
                        SendText(action.TextToSend);
                    break;
                case ActionType.LockScreen:
                    LockWorkStationStatic();
                    break;
            }
        }

        // ── OS-intercepted shortcut override map ──────────────────
        // These combos are blocked by Windows at the kernel/shell level and cannot
        // be sent via SendInput.  Each entry maps the normalised combo string to an
        // action that achieves the same effect through an alternate API.
        private static readonly Dictionary<string, Action> _blockedOverrides =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // Win+L  – Secure Attention Sequence: lock screen
            ["Win+L"] = () => LockWorkStationStatic(),

            // Win+D  – Show/hide desktop (toggle).  SendInput Win+D is swallowed by
            //          the shell; MinimizeAll() is more reliable than ToggleDesktop()
            //          especially when a full-screen app (e.g. slideshow) is in foreground.
            ["Win+D"] = () =>
            {
                try
                {
                    var shell = (IShellApplication)Activator.CreateInstance(
                        Type.GetTypeFromProgID("Shell.Application")!)!;
                    shell.MinimizeAll();
                }
                catch { }
            },

            // Win+E  – Open File Explorer
            ["Win+E"] = () =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); }
                catch { }
            },

            // Win+I  – Open Windows Settings
            ["Win+I"] = () =>
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }); }
                catch { }
            },

            // Win+A  – Action Center / Quick Settings (no public API; best-effort SendInput)
            // (left as SendInput because there is no better alternative)

            // Ctrl+Alt+Del – SAS: impossible from userspace, silently ignore
            ["Ctrl+Alt+Delete"] = () => { },
            ["Ctrl+Alt+Del"]    = () => { },
        };

        [DllImport("user32.dll", EntryPoint = "LockWorkStation")]
        private static extern bool LockWorkStation_();
        private static void LockWorkStationStatic()
        {
            // Try the direct P/Invoke first; fall back to Rundll32 if it fails
            // (the P/Invoke may be blocked by UAC/MSIX policy on some configurations).
            if (!LockWorkStation_())
            {
                try
                {
                    Process.Start(new ProcessStartInfo("Rundll32.exe", "user32.dll,LockWorkStation")
                    {
                        UseShellExecute = false,
                        CreateNoWindow  = true
                    });
                }
                catch { }
            }
        }

        // ── Hotkey ────────────────────────────────────────────────
        public void SendHotkey(string combo)
        {
            // Normalise: remove spaces, then check the override map.
            var normalised = combo.Replace(" ", "");
            if (_blockedOverrides.TryGetValue(normalised, out var overrideAction))
            {
                overrideAction();
                return;
            }

            var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var vks = new List<ushort>();
            foreach (var p in parts)
            {
                if (VkMap.TryGetValue(p, out var vk))
                    vks.Add(vk);
                else if (p.Length == 1)
                    vks.Add((ushort)(NativeMethods.VkKeyScan(p[0]) & 0xFF));
            }
            if (vks.Count == 0) return;
            var inputs = new NativeMethods.INPUT[vks.Count * 2];
            for (int i = 0; i < vks.Count; i++)
                inputs[i] = MakeKeyDown(vks[i]);
            for (int i = 0; i < vks.Count; i++)
                inputs[vks.Count + i] = MakeKeyUp(vks[vks.Count - 1 - i]);
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        // ── Media / volume keys ───────────────────────────────────
        public void SendMediaKey(MediaCommand cmd)
        {
            ushort vk = cmd switch
            {
                MediaCommand.PlayPause => NativeMethods.VK_MEDIA_PLAY_PAUSE,
                MediaCommand.Next      => NativeMethods.VK_MEDIA_NEXT_TRACK,
                MediaCommand.Prev      => NativeMethods.VK_MEDIA_PREV_TRACK,
                MediaCommand.Stop      => NativeMethods.VK_MEDIA_STOP,
                MediaCommand.VolumeUp  => NativeMethods.VK_VOLUME_UP,
                MediaCommand.VolumeDown=> NativeMethods.VK_VOLUME_DOWN,
                MediaCommand.Mute      => NativeMethods.VK_VOLUME_MUTE,
                _ => 0
            };
            if (vk == 0) return;
            var inputs = new[] { MakeKeyDown(vk, extended: true), MakeKeyUp(vk, extended: true) };
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        // ── Send text (Unicode) ───────────────────────────────────
        public void SendText(string text)
        {
            var inputs = new NativeMethods.INPUT[text.Length * 2];
            for (int i = 0; i < text.Length; i++)
            {
                ushort c = text[i];
                inputs[i * 2]     = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD,
                    Data = new() { ki = new() { wScan = c, dwFlags = NativeMethods.KEYEVENTF_UNICODE } } };
                inputs[i * 2 + 1] = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD,
                    Data = new() { ki = new() { wScan = c, dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP } } };
            }
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        // ── Open URL / file / folder ──────────────────────────────
        public void OpenUrl(string url)
        {
            // ms-screensketch: is the Windows 11 Snipping Tool protocol.
            // On Windows 10 it does not exist; fall back to SnippingTool.exe.
            if (url.Equals("ms-screensketch:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("ms-screensketch:") { UseShellExecute = true });
                    return;
                }
                catch
                {
                    // Win10 fallback: launch classic Snipping Tool
                    try
                    {
                        Process.Start(new ProcessStartInfo("SnippingTool.exe") { UseShellExecute = true });
                    }
                    catch { }
                    return;
                }
            }
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        // ── Launch application ────────────────────────────────────
        public void LaunchApp(string path, string? args)
        {
            var psi = new ProcessStartInfo(path) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args;
            Process.Start(psi);
        }

        // ── Kill process ──────────────────────────────────────────
        public void KillProcess(string nameOrPid)
        {
            if (int.TryParse(nameOrPid, out int pid))
            {
                try { Process.GetProcessById(pid).Kill(); } catch { }
            }
            else
            {
                string name = nameOrPid.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                foreach (var p in Process.GetProcessesByName(name))
                    try { p.Kill(); } catch { }
            }
        }

        // ── Simulate click ────────────────────────────────────────
        public void SimulateClick(string target, ForegroundWindowInfo? foreground)
        {
            int x, y;
            if (target.StartsWith("rel:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = target[4..].Split(',');
                if (parts.Length < 2) return;
                int dx = int.Parse(parts[0].Trim()), dy = int.Parse(parts[1].Trim());
                foreground ??= ForegroundWindowService.Snapshot();
                NativeMethods.GetWindowRect(foreground.Hwnd, out var rect);
                x = rect.Left + dx; y = rect.Top + dy;
            }
            else
            {
                var parts = target.Split(',');
                if (parts.Length < 2) return;
                x = int.Parse(parts[0].Trim()); y = int.Parse(parts[1].Trim());
            }
            int screenW = NativeMethods.GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
            int screenH = NativeMethods.GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
            int originX = NativeMethods.GetSystemMetrics(76); // SM_XVIRTUALSCREEN
            int originY = NativeMethods.GetSystemMetrics(77); // SM_YVIRTUALSCREEN
            // Normalise to [0, 65535] across the full virtual screen (all monitors)
            int nx = (x - originX) * 65535 / (screenW > 0 ? screenW : 1);
            int ny = (y - originY) * 65535 / (screenH > 0 ? screenH : 1);
            var inputs = new NativeMethods.INPUT[]
            {
                new() { type=NativeMethods.INPUT_MOUSE, Data=new(){ mi=new(){
                    dx=nx, dy=ny,
                    dwFlags=NativeMethods.MOUSEEVENTF_MOVE|NativeMethods.MOUSEEVENTF_ABSOLUTE|NativeMethods.MOUSEEVENTF_VIRTUALDESK}}},
                new() { type=NativeMethods.INPUT_MOUSE, Data=new(){ mi=new(){
                    dwFlags=NativeMethods.MOUSEEVENTF_LEFTDOWN|NativeMethods.MOUSEEVENTF_ABSOLUTE|NativeMethods.MOUSEEVENTF_VIRTUALDESK,
                    dx=nx, dy=ny}}},
                new() { type=NativeMethods.INPUT_MOUSE, Data=new(){ mi=new(){
                    dwFlags=NativeMethods.MOUSEEVENTF_LEFTUP|NativeMethods.MOUSEEVENTF_ABSOLUTE|NativeMethods.MOUSEEVENTF_VIRTUALDESK,
                    dx=nx, dy=ny}}}
            };
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        // ── Script ────────────────────────────────────────────────
        public void RunScript(ActionItem action)
        {
            string shell = action.ScriptShell ?? "cmd";
            string content = action.ScriptInline ?? string.Empty;
            string exe, args;
            switch (shell.ToLowerInvariant())
            {
                case "powershell":
                    exe = "powershell.exe";
                    args = string.IsNullOrWhiteSpace(action.ScriptPath)
                        ? $"-NoProfile -Command \"{EscapeArg(content)}\""
                        : $"-NoProfile -File \"{action.ScriptPath}\"";
                    break;
                case "python":
                    exe = "python.exe";
                    args = string.IsNullOrWhiteSpace(action.ScriptPath)
                        ? $"-c \"{EscapeArg(content)}\""
                        : $"\"{action.ScriptPath}\"";
                    break;
                default:
                    exe = "cmd.exe";
                    args = string.IsNullOrWhiteSpace(action.ScriptPath)
                        ? $"/c \"{EscapeArg(content)}\""
                        : $"/c \"{action.ScriptPath}\"";
                    break;
            }
            Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = action.ScriptSilent,
                WindowStyle = action.ScriptSilent ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
            });
        }

        public void ShowDesktop()
        {
            try
            {
                var shell = (IShellApplication)Activator.CreateInstance(
                    Type.GetTypeFromProgID("Shell.Application")!)!;
                shell.MinimizeAll();
            }
            catch { }
        }

        private static void RestoreForegroundWindow(ForegroundWindowInfo? foreground)
        {
            if (foreground is null || foreground.Hwnd == IntPtr.Zero) return;
            if (foreground.Pid == (uint)Environment.ProcessId) return;

            if (NativeMethods.SetForegroundWindow(foreground.Hwnd))
            {
                // Give the target window enough time to receive focus before we inject input.
                // 50 ms is needed for full-screen apps (e.g. PowerPoint slideshow) that take
                // longer to accept focus than regular windowed applications.
                Thread.Sleep(50);
            }
        }

        // ── Helpers ───────────────────────────────────────────────
        private static NativeMethods.INPUT MakeKeyDown(ushort vk, bool extended = false) =>
            new() { type = NativeMethods.INPUT_KEYBOARD, Data = new() { ki = new()
            { wVk = vk, dwFlags = extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0 } } };

        private static NativeMethods.INPUT MakeKeyUp(ushort vk, bool extended = false) =>
            new() { type = NativeMethods.INPUT_KEYBOARD, Data = new() { ki = new()
            { wVk = vk, dwFlags = NativeMethods.KEYEVENTF_KEYUP | (extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0) } } };

        private static string EscapeArg(string s) => s.Replace("\"", "\\\"");
    }
}
