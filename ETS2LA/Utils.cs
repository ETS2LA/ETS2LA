using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using ETS2LA.Logging;
using ETS2LA.Telemetry;

using System.Runtime.InteropServices;

namespace ETS2LA;

static class Utils
{
    /// <summary>
    ///  Handles a full app crash exception. We'll display a popup to the user
    ///  and log the error to OpenTelemetry if possible.
    /// </summary>
    /// <param name="ex"></param>
    public static void HandleFatalException(Exception? ex, TracerProvider? tracerProvider = null, MeterProvider? meterProvider = null)
    {
        if (ex == null) return;

        if (ex is AggregateException aggregate)
            ex = aggregate.Flatten().InnerExceptions.FirstOrDefault() ?? ex;

        string errorMessage = $"ETS2LA has encountered a fatal error.\n\n" +
                              $"Error: {ex.Message}\n\n" +
                              $"Stack Trace:\n{ex.StackTrace}";

        // This logs to OpenTelemetry. The log won't go through if the user has telemetry disabled though...
        try
        {
            if (ex.Message != "ETS2LA is already running")
                AppAnalytics.LogEvent("app.crash", new Dictionary<string, string>
                {
                    { "exception.type", ex.GetType().ToString() },
                    { "exception.message", ex.Message },
                    { "exception.stacktrace", ex.StackTrace ?? "" }
                });
        } catch {}
        
        # if WINDOWS
            try { NativeMethods.MessageBox(IntPtr.Zero, errorMessage, "ETS2LA", 0x10); }
            catch { }
        # else
            // zenity is a standard linux utility, at least that's what gemini told me...
            try { System.Diagnostics.Process.Start("zenity", $"--error --title=\"ETS2LA\" --text=\"{errorMessage.Replace("\"", "\\\"")}\""); }
            catch { }
        # endif

        try { Logger.Error(errorMessage); }
        catch { }

        LogFileWriter.Current.Save();

        // Environment.Exit skips disposal so we flush manually.
        try { meterProvider?.ForceFlush(5000); } catch { }
        try { tracerProvider?.ForceFlush(5000); } catch { }

        // Force terminate
        Environment.Exit(1);
    }

    public static bool DoesETS2LAProcessExist()
    {
        var processName = "ets2la";
        var processes = System.Diagnostics.Process.GetProcessesByName(processName);
        return processes.Length > 1;
    }

    public static string[] WaitForRestartParentProcess(string[] args)
    {
        const string argumentPrefix = "--restart-parent-process-id=";
        string? restartArgument = args.FirstOrDefault(argument => argument.StartsWith(argumentPrefix, StringComparison.Ordinal));

        if (restartArgument == null)
            return args;

        string processIdValue = restartArgument[argumentPrefix.Length..];
        if (int.TryParse(processIdValue, out int processId) && processId != Environment.ProcessId)
        {
            try
            {
                // Avoid triggering the single-instance check during a restart.
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                process.WaitForExit();
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }

        // Keep the internal restart argument away from the UI.
        return args.Where(argument => !argument.StartsWith(argumentPrefix, StringComparison.Ordinal)).ToArray();
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int MessageBox(IntPtr hWnd, String text, String caption, uint type);
}
