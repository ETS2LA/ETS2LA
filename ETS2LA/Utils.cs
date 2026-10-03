using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using ETS2LA.Logging;
using ETS2LA.Telemetry;
using ETS2LA.Translations;
using ETS2LA.State;
using ETS2LA.Backend.Plugins;
using ETS2LA.Settings.Global;

using System.Globalization;
using System.Runtime.InteropServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Localization;

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

    public static void HandleContinueClose(string message)
    {
        # if WINDOWS
            try {
                int result = NativeMethods.MessageBox(IntPtr.Zero, message, "ETS2LA", 0x4 | 0x20); // MB_YESNO | MB_ICONQUESTION
                if (result == 6) // ID_YES
                    return;
            } catch { }
        # else
            try
            {
                int exitCode = -1;
                try {
                    using var proc = System.Diagnostics.Process.Start("zenity", $"--question --title=\"ETS2LA\" --text=\"{message.Replace("\"", "\\\"")}\"");
                    if (proc == null) return;
                    
                    proc?.WaitForExit();
                    exitCode = proc?.ExitCode ?? -1;
                } catch { }

                if (exitCode == 0) // zenity returns 0 for "Yes"
                    return;
            }
            catch { }
        # endif

        Environment.Exit(0);
    }

    public static bool IsRunningAsRoot()
    {
        #if WINDOWS
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        #else
            return Environment.UserName == "root" || Environment.UserName == "admin";
        #endif
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
                process.WaitForExit(10000);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DoesETS2LAProcessExist() && DateTime.UtcNow < deadline)
            Thread.Sleep(50);

        // Keep the internal restart argument away from the UI.
        return args.Where(argument => !argument.StartsWith(argumentPrefix, StringComparison.Ordinal)).ToArray();
    }

    public static void InitializeTranslations()
    {
        var services = new ServiceCollection();
        var installedPlugins = InstalledPluginManifest.Current.InstalledPlugins;

        IHostEnvironment hostEnvironment = new HostingEnvironment
        {
            ContentRootPath = AppContext.BaseDirectory,
            ContentRootFileProvider = new PhysicalFileProvider(AppContext.BaseDirectory),
            EnvironmentName = Environments.Production,
            ApplicationName = "ETS2LA"
        };
        services.AddSingleton(hostEnvironment);

        services.AddLogging();
        services.AddMemoryCache();
        
        services.AddPortableObjectLocalization(options =>
        {
            options.ResourcesPath = "Localization";
        });

        IEnumerable<string> pluginPaths = installedPlugins.Select(p => Path.GetDirectoryName(p.DllPath)).Where(p => p != null).Select(p => p!);
        services.AddSingleton<ILocalizationFileLocationProvider>(new MultiPoFileProvider(pluginPaths));

        IServiceProvider serviceProvider = services.BuildServiceProvider();
        T.Initialize(serviceProvider);

        var stateSettings = StateSettingsHandler.Current.GetSettings();
        var language = stateSettings.DisplayLanguage;
        string cultureCode = string.IsNullOrWhiteSpace(language?.Code) ? "en" : language.Code;
        Logger.Info($"Setting application culture to: {cultureCode}");
        
        var culture = new CultureInfo(cultureCode);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        UserSettings.Current.DisplayLanguage = cultureCode;
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int MessageBox(IntPtr hWnd, String text, String caption, uint type);
}

public class MultiPoFileProvider : ILocalizationFileLocationProvider
{
    private readonly List<IFileProvider> _fileProviders;

    public MultiPoFileProvider(IEnumerable<string> pluginPaths)
    {
        _fileProviders = new List<IFileProvider>
        {
            // This is ETS2LA's base (so bin/net10.0 in build and the root of the app in publish)
            new PhysicalFileProvider(AppContext.BaseDirectory)
        };

        foreach (var path in pluginPaths)
        {
            if (Directory.Exists(path))
            {
                // And this points to the plugin's root directory
                # if LINUX
                    var usedPath = path.EndsWith("/") ? path : path + "/";
                # else
                    var usedPath = path.EndsWith("\\") ? path : path + "\\";
                    usedPath = Path.Combine(AppContext.BaseDirectory, usedPath);
                # endif

                Logger.Info($"Added Localization provider {usedPath}");
                _fileProviders.Add(new PhysicalFileProvider(usedPath));
            }
        }
    }

    public IEnumerable<IFileInfo> GetLocations(string cultureName)
    {
        var fileInfos = new List<IFileInfo>();

        foreach (var provider in _fileProviders)
        {
            var fileInfo = provider.GetFileInfo(Path.Combine("Localization", $"{cultureName}.po"));
            Logger.Info($"Checking for localization file: {fileInfo.PhysicalPath}");
            if (fileInfo.Exists)
            {
                fileInfos.Add(fileInfo);
            }
        }

        return fileInfos;
    }
}