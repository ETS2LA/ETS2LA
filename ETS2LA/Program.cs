using Velopack;
using Velopack.Locators;

using ETS2LA.Tutorials;
using ETS2LA.Overlay;
using ETS2LA.Backend;
using ETS2LA.Game.Telemetry;
using ETS2LA.State;
using ETS2LA.Logging;
using ETS2LA.Settings.Global;
using ETS2LA.Telemetry;
using ETS2LA.Networking;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;

using System.Runtime.InteropServices;

namespace ETS2LA;

internal static class Program
{
    /// <summary>
    ///  Main entrypoint for ETS2LA.
    /// </summary>
    static void Main(string[] args)
    {
        // This handles the main thread crashing (Avalonia)
        // Nothing else *should* run on the main thread.
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            HandleFatalException(e.ExceptionObject as Exception);
        };

        // This is for unobserved exceptions, i.e. plugins and other Task.Run() calls etc..
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            e.SetObserved(); // Prevents an immediate crash, we'll handle termination in HandleFatalException instead.

            // Avalonia's IBus integration on linux throws these from DBus calls
            // that nothing awaits (like when closing the window). They are harmless, can be ignored.
            if (e.Exception.Flatten().InnerExceptions.All(ex => ex is Tmds.DBus.Protocol.DBusExceptionBase))
            {
                Logger.Warn($"Ignored DBus exception: {e.Exception.InnerException?.Message}");
                return;
            }

            HandleFatalException(e.Exception);
        };

        // Velopack is the installer / update manager
        // Please don't move this, Velopack has to be initialized before anything else,
        // otherwise we might end up with weird bugs.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            #if DEBUG
            .SetLocator(new TestVelopackLocator(
                appId: "ETS2LA",
                version: "1.0.0",
                packagesDir: "./Releases/Portable"
            ))
            #endif
            .Run();

        string currentVersion = VelopackLocator.Current?.CurrentlyInstalledVersion?.ToString()
                             ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) 
                             ?? "unknown"; 

        // For OTel (OpenTelemetry)
        var appResource = ResourceBuilder.CreateDefault()
            .AddService("ETS2LA", serviceVersion: currentVersion)
            .AddAttributes(OTelAttributes.GetAttributes());

        // These get automatically removed because of using var
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(appResource)
            .AddSource("ETS2LA.*")
            .AddOtlpExporter(options =>
            {
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                options.Endpoint = UserSettings.Current.IsTelemetryEnabled ? new Uri("https://otel.ets2la.com/v1/traces") : new Uri("http://localhost:4318/v1/traces");
            })
            .Build();
        
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(appResource)
            .AddMeter("ETS2LA.*")
            .AddOtlpExporter(options =>
            {
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                options.Endpoint = UserSettings.Current.IsTelemetryEnabled ? new Uri("https://otel.ets2la.com/v1/metrics") : new Uri("http://localhost:4318/v1/metrics");
            })
            .Build();

        bool shutdown = false;
        var AnalyticsThread = Task.Factory.StartNew(() =>
        {
            AppAnalytics.StartSession();
            while (!shutdown)
            {
                AppAnalytics.Pulse();
                Thread.Sleep(TimeSpan.FromMinutes(1));
            }
            AppAnalytics.StopSession();
        }, TaskCreationOptions.LongRunning);

        var BackendThread = Task.Run(() =>
        {
            // These initialize global instances, if there's a more "official" way to
            // do this then please make a PR for that.
            var ar = OverlayHandler.Current;
            var backend = PluginBackend.Current;
            var telemetry = GameTelemetry.Current;
            var state = ApplicationState.Current;
            var tutorials = TutorialHandler.Current;
            var networking = NetworkingClient.Current;
        });

        # if LINUX
            string? useWayland = Environment.GetEnvironmentVariable("GLFW_USE_WAYLAND");
            if (useWayland == null || useWayland == "0" || useWayland == "")
            {
                // This is to prevent GLFW from trying to use wayland. If wayland is still required
                // then setting GLFW_USE_WAYLAND=1 should work fine.
                Environment.SetEnvironmentVariable("GLFW_USE_WAYLAND", "0");
                Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", "x11");
            }
        # endif

        // Gotta wait for the UI thread to close (i.e. user closed the window)
        // and then tell the backend to shutdown too.
        UI.Program.Main(args);

        shutdown = true;
        PluginBackend.Current.Shutdown();
        OverlayHandler.Current.Shutdown();
        GameTelemetry.Current.Shutdown();
        ApplicationState.Current.Shutdown();
        TutorialHandler.Current.Shutdown();

        LogFileWriter.Current.Save();
    }

    /// <summary>
    ///  Handles a full app crash exception. We'll display a popup to the user
    ///  and log the error to OpenTelemetry if possible.
    /// </summary>
    /// <param name="ex"></param>
    private static void HandleFatalException(Exception? ex)
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
        // Force terminate
        Environment.Exit(1);
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int MessageBox(IntPtr hWnd, String text, String caption, uint type);
}