using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssistiveTouch.Win32;

namespace AssistiveTouch.Services;

/// <summary>
/// Polls the foreground window at a configurable interval and raises an event when it changes.
/// </summary>
public class ForegroundWindowInfo
{
    public IntPtr Hwnd { get; init; }
    public uint Pid { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string ExeFullPath { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
}

public class ForegroundWindowService : IDisposable
{
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private ForegroundWindowInfo? _last;
    private ForegroundWindowInfo? _lastExternal;

    public event Action<ForegroundWindowInfo>? ForegroundChanged;

    public ForegroundWindowInfo? Current => _last;
    public ForegroundWindowInfo? CurrentExternal => _lastExternal;

    public ForegroundWindowService(int pollMs = 500)
    {
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(pollMs));
        _ = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (await _timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var info = Snapshot();
                if (!IsAssistouchWindow(info))
                    _lastExternal = info;

                if (_last == null || info.Hwnd != _last.Hwnd || info.WindowTitle != _last.WindowTitle)
                {
                    _last = info;
                    ForegroundChanged?.Invoke(info);
                }
            }
            catch { /* swallow – app may be shutting down */ }
        }
    }

    public static ForegroundWindowInfo Snapshot()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);

        string processName = string.Empty;
        string exePath = string.Empty;

        var hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess != IntPtr.Zero)
        {
            try
            {
                uint size = 1024;
                var sb = new StringBuilder((int)size);
                if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    exePath = sb.ToString();
                    processName = Path.GetFileName(exePath);
                }
            }
            finally { NativeMethods.CloseHandle(hProcess); }
        }

        var titleSb = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, titleSb, titleSb.Capacity);

        return new ForegroundWindowInfo
        {
            Hwnd = hwnd,
            Pid = pid,
            ProcessName = processName,
            ExeFullPath = exePath,
            WindowTitle = titleSb.ToString()
        };
    }

    public ForegroundWindowInfo CaptureContext()
    {
        var info = Snapshot();
        return IsAssistouchWindow(info) && _lastExternal is not null
            ? _lastExternal
            : info;
    }

    private static bool IsAssistouchWindow(ForegroundWindowInfo info) =>
        info.Pid == (uint)Environment.ProcessId;

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _timer.Dispose();
    }
}
