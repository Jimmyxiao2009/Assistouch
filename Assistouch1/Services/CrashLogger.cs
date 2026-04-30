using System;
using System.IO;
using System.Text;
using Microsoft.UI.Xaml;

namespace AssistiveTouch.Services;

/// <summary>
/// Writes crash and swallowed-exception records to
///   %LocalAppData%\AssistiveTouch\crash.log
/// Call Init() once at app startup; call Write(ex) from every catch block.
/// </summary>
public static class CrashLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AssistiveTouch", "crash.log");

    private static readonly object _lock = new();

    // ── Init ──────────────────────────────────────────────────────────────

    public static void Init(Application app)
    {
        // WinUI unhandled exception (UI thread)
        app.UnhandledException += (_, e) =>
        {
            e.Handled = true; // prevent process termination so we can log first
            Write(e.Exception, "WinUI.UnhandledException");
            // Re-crash deliberately after logging so the OS error report fires.
            // Comment out the next line if you prefer the app to stay alive.
            // Environment.FailFast("Unhandled WinUI exception — see crash.log");
        };

        // Background Task / async void exceptions
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Write(e.Exception, "TaskScheduler.UnobservedTaskException");
        };

        // CLR-level (native interop, static ctors, etc.)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Write(ex, $"AppDomain.UnhandledException (isTerminating={e.IsTerminating})");
        };

        Write(null, "App started — crash logger initialized");
    }

    // ── Write ─────────────────────────────────────────────────────────────

    public static void Write(Exception? ex, string? context = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("────────────────────────────────────────");
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]");
            if (context is not null)
                sb.AppendLine($"Context : {context}");

            if (ex is null)
            {
                sb.AppendLine("(no exception)");
            }
            else
            {
                AppendException(sb, ex, 0);
            }

            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // If logging itself fails, silently swallow — never let the logger crash the app.
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void AppendException(StringBuilder sb, Exception ex, int depth)
    {
        string indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}Type    : {ex.GetType().FullName}");
        sb.AppendLine($"{indent}Message : {ex.Message}");
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            sb.AppendLine($"{indent}Stack   :");
            foreach (var line in ex.StackTrace.Split('\n'))
                sb.AppendLine($"{indent}  {line.TrimEnd()}");
        }

        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
            {
                sb.AppendLine($"{indent}  [InnerException]");
                AppendException(sb, inner, depth + 1);
            }
        }
        else if (ex.InnerException is not null)
        {
            sb.AppendLine($"{indent}  [InnerException]");
            AppendException(sb, ex.InnerException, depth + 1);
        }
    }
}
