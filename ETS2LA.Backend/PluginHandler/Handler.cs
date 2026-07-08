using ETS2LA.Shared;
using ETS2LA.Logging;
using ETS2LA.Backend.Events;
using ETS2LA.Backend.Plugins;
using ETS2LA.Notifications;
using System.Runtime.Loader;
using System.Reflection;

namespace ETS2LA.Backend;

// The class instance for this lives in PluginBackend
public class PluginHandler
{
    // These are files in the plugins folder that the backend will
    // exclude when trying to load.
    private readonly List<string> _exclusions = new()
    {
        "Microsoft.*",
        "System.*",
        "SharpDX.*",
        "ETS2LA.*",
    };

    public readonly List<IPlugin> LoadedPlugins = new();
    public readonly List<ILibraryPlugin> LoadedLibraryPlugins = new();

    // Check PluginLoadContext.cs for why we need to keep track of them here.
    // TLDR: To be able to reload assemblies without restarting ETS2LA.
    private readonly Dictionary<IPlugin, AssemblyLoadContext> _pluginLoadContexts = new();
    // This also applies to shadow directories. Using shadow copies also means that it's
    // possible to detect .dll changes automatically in the future, meaning hot reloading
    // of plugins without requiring a direct reload action from the user.
    // TODO: Implement hot reloading of plugins.
    private readonly Dictionary<AssemblyLoadContext, string> _contextShadowDirectories = new();
    
    public Action<IPlugin>? PluginEnabled;
    public Action<IPlugin>? PluginDisabled;
    public bool loading = false;

    # if WINDOWS
        // On Windows we just use the already existing AppData folder that
        // velopack creates during installs. (the current root, so no changes needed)
        public string PluginRootPath = "";
    # else
        // This is ~/.local/share/ETS2LA/Plugins
        public string PluginRootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETS2LA");
    # endif

    private static readonly string ShadowRootPath = Path.Combine(Path.GetTempPath(), "ETS2LA", "PluginShadow");

    public PluginHandler()
    {
        CleanupStaleShadowDirectories();
    }

    public string[] DiscoverManualDlls(string path)
    {
        path = Path.Combine(PluginRootPath, path);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            Logger.Info($"Created plugin directory: [gray]{path}[/]");
        }

        try
        {
            var pluginFiles = Directory.GetFiles(path, "*.dll");

            // Exclude anything in _exclusions.
            pluginFiles = pluginFiles.Where(file =>
            {
                var fileName = Path.GetFileName(file);
                return !_exclusions.Any(pattern => 
                    System.Text.RegularExpressions.Regex.IsMatch(fileName, 
                        "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$"
                    ));
            }).ToArray();

            return pluginFiles;
        } catch (Exception ex)
        {
            Logger.Error($"Failed to discover Dlls: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    public string[] DiscoverManifestDlls(PluginType type)
    {
        switch (type)
        {
            case PluginType.Plugin:
                return InstalledPluginManifest.Current.InstalledPlugins
                    .Where(p => p.Type == PluginType.Plugin)
                    .Select(p => p.DllPath)
                    .ToArray();
            case PluginType.Library:
                return InstalledPluginManifest.Current.InstalledPlugins
                    .Where(p => p.Type == PluginType.Library)
                    .Select(p => p.DllPath)
                    .ToArray();
            default:
                Logger.Warn($"Unknown manifest type: {type}");
                return Array.Empty<string>();
        }
    }

    public void LoadLibraries()
    {
        string[] libraryFiles = DiscoverManualDlls("Libraries");
        Logger.Info($"Discovered {libraryFiles.Length} manually installed libraries.");
        libraryFiles = libraryFiles.Concat(DiscoverManifestDlls(PluginType.Library)).ToArray();
        Logger.Info($"Discovered {libraryFiles.Length} libraries in total.");

        foreach (string filename in libraryFiles)
        {
            try
            {
                var absolutePath = Path.GetFullPath(filename);
                var shadowPath = CreateShadowCopy(absolutePath);

                var assembly = Assembly.LoadFrom(shadowPath);
                var libraryTypes = assembly.GetTypes()
                    .Where(t => typeof(ILibraryPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in libraryTypes)
                {
                    var libraryPlugin = (ILibraryPlugin)Activator.CreateInstance(type)!;
                    LoadedLibraryPlugins.Add(libraryPlugin);
                    Logger.Info($"Loaded library plugin: [gray]{type.FullName}[/] from [gray]{filename}[/].");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load library plugin from [gray]{filename}[/]: {ex}");
            }
        }
    }

    public void LoadPlugins()
    {
        loading = true;
        string[] pluginFiles = DiscoverManualDlls("Plugins");
        Logger.Info($"Discovered {pluginFiles.Length} manually installed plugins.");
        pluginFiles = pluginFiles.Concat(DiscoverManifestDlls(PluginType.Plugin)).ToArray();
        Logger.Info($"Discovered {pluginFiles.Length} plugins in total.");

        foreach (string filename in pluginFiles)
        {

            Thread.Sleep(100); // Slight delay to avoid overwhelming the system
                                // and to allow other processes / logging to run smoothly.
            try
            {
                // This is so ugly...
                // Please if anyone does have a better idea then help me :sob:
                var absolutePath = Path.GetFullPath(filename);
                var shadowPath = CreateShadowCopy(absolutePath);
                var pluginDirectory = Path.GetDirectoryName(absolutePath) ?? Directory.GetCurrentDirectory();
                
                var loadContext = new PluginLoadContext(shadowPath, pluginDirectory);
                _contextShadowDirectories[loadContext] = Path.GetDirectoryName(shadowPath)!;

                var assembly = loadContext.LoadFromAssemblyPath(shadowPath);
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                // There can be multiple plugins in one assembly.
                foreach (var type in pluginTypes)
                {
                    var plugin = (IPlugin)Activator.CreateInstance(type)!;
                    plugin.Init();

                    LoadedPlugins.Add(plugin);
                    _pluginLoadContexts[plugin] = loadContext;
                    
                    Logger.Info($"Loaded plugin: [gray]{type.FullName}[/] from [gray]{filename}[/].");
                }
            }
            catch (Exception ex)
            {
                // stacktrace + inner exceptions
                // (basically we get the full exception info, inside the assembly context)
                if (ex is System.Reflection.ReflectionTypeLoadException rtle)
                {
                    Logger.Error($"Failed to load plugin from [gray]{filename}[/]: {rtle}");
                    foreach (var le in rtle.LoaderExceptions)
                    {
                        Logger.Error(le?.ToString() ?? "LoaderException: null");
                    }
                }
                else if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
                {
                    Logger.Error($"Failed to load plugin from [gray]{filename}[/]: {tie.InnerException}");
                    Logger.Error(tie.InnerException.ToString());
                }
                else
                {
                    Logger.Error($"Failed to load plugin from [gray]{filename}[/]: {ex}");
                }
            }
        }

        NotificationHandler.Current.SendNotification(new Notification
        {
            Id = "Backend.PluginHandler.Loading",
            Title = $"Finished loading plugins",
            Content = $"Loaded {LoadedPlugins.Count} plugins from the Plugins folder.",
            CloseAfter = 3,
            Level = NotificationLevel.Success
        });
        loading = false;
    }

    public void UnloadPlugins()
    {
        loading = true;
        // Keeps track of the load contexts of plugins
        // we've unloaded, these will be unloaded later.
        var loadContexts = new HashSet<AssemblyLoadContext>();

        foreach (var plugin in LoadedPlugins)
        {
            try
            {
                if(plugin._IsRunning)
                    plugin.OnDisable();
                    
                plugin.Shutdown();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to shutdown plugin {plugin.GetType().FullName}: {ex.Message}");
            }

            if (_pluginLoadContexts.TryGetValue(plugin, out var loadContext))
            {
                loadContexts.Add(loadContext);
            }
        }

        LoadedPlugins.Clear();
        _pluginLoadContexts.Clear();

        foreach (var loadContext in loadContexts)
        {
            try
            {
                loadContext.Unload();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to unload plugin load context: {ex.Message}");
            }
        }

        // We wait just a bit to force GC to cleanup the old 
        // assemblies, otherwise they might still be around for 
        // the next cycle, meaning a call of UnloadPlugins -> LoadPlugins 
        // without delay might not update the .dlls as expected.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        foreach (var loadContext in loadContexts)
        {
            // On Windows this fails while assemblies are still mapped,
            // leftovers are swept on the next startup instead.
            CleanupShadowDirectory(loadContext);
        }

        loading = false;
    }

    private static string CreateShadowCopy(string sourceAssemblyPath)
    {
        var pluginName = Path.GetFileNameWithoutExtension(sourceAssemblyPath);
        var shadowDirectory = Path.Combine(ShadowRootPath, pluginName + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shadowDirectory);

        var destinationAssemblyPath = Path.Combine(shadowDirectory, Path.GetFileName(sourceAssemblyPath));
        File.Copy(sourceAssemblyPath, destinationAssemblyPath, overwrite: true);

        return destinationAssemblyPath;
    }

    private void CleanupShadowDirectory(AssemblyLoadContext context)
    {
        if (!_contextShadowDirectories.TryGetValue(context, out var shadowDirectory))
        {
            return;
        }

        _contextShadowDirectories.Remove(context);

        try
        {
            if (Directory.Exists(shadowDirectory))
            {
                Directory.Delete(shadowDirectory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to clean plugin shadow directory [gray]{shadowDirectory}[/], it will be removed on the next startup: {ex.Message}");
        }
    }

    // Removes shadow directories left behind by previous runs.
    private static void CleanupStaleShadowDirectories()
    {
        if (!Directory.Exists(ShadowRootPath))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(ShadowRootPath))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Skipped stale plugin shadow directory [gray]{directory}[/]: {ex.Message}");
            }
        }
    }

    private IPlugin? GetPluginById(string pluginId)
    {
        return LoadedPlugins.FirstOrDefault(p => p.Info.Id == pluginId);
    }

    private ILibraryPlugin? GetLibraryPluginById(string pluginId)
    {
        return LoadedLibraryPlugins.FirstOrDefault(p => p.Info.Id == pluginId);
    }

    public bool EnablePlugin(IPlugin? plugin = null, string? pluginId = null)
    {
        plugin ??= GetPluginById(pluginId!);
        if (plugin == null)
        {
            Logger.Warn($"Tried to enable {pluginId}, but it was not found among loaded plugins.");
            return false;
        }

        var dependencies = plugin.Info.Dependencies;
        foreach (var dependencyId in dependencies)
        {
            var dependency = GetPluginById(dependencyId);
            if (dependency == null) {
                if (GetLibraryPluginById(dependencyId) == null)
                {
                    NotificationHandler.Current.SendNotification(new Notification
                    {
                        Id = $"Backend.PluginHandler.MissingDependency.{plugin.Info.Id}",
                        Title = $"{plugin.Info.Name}",
                        Content = $"Missing dependency: {dependencyId}",
                        Level = NotificationLevel.Danger
                    });
                    Logger.Warn($"Cannot enable plugin {plugin.Info.Name} because dependency {dependencyId} was not found.");
                    return false;
                }
            }
            if (dependency != null && !dependency._IsRunning)
            {                    
                var success = EnablePlugin(dependency);
                if (!success)                    {
                    NotificationHandler.Current.SendNotification(new Notification
                    {
                        Id = $"Backend.PluginHandler.FailedDependency.{dependency.Info.Id}",
                        Title = $"{plugin.Info.Name}",
                        Content = $"Failed to enable dependency: {dependency.Info.Name}",
                        Level = NotificationLevel.Danger
                    });
                    Logger.Warn($"Cannot enable plugin {plugin.Info.Name} because dependency {dependency.Info.Name} failed to enable.");
                    return false;
                }
            }
        }

        try
        {
            plugin.OnEnable();

            Logger.Info($"Enabled plugin: [bold]{plugin.Info.Id}[/]");
            PluginEnabled?.Invoke(plugin);
            NotificationHandler.Current.SendNotification(new Notification
            {
                Id = $"Backend.PluginHandler.PluginEnabled.{plugin.Info.Id}",
                Title = $"{plugin.Info.Name}",
                Content = $"The plugin was enabled successfully.",
                Level = NotificationLevel.Success,
                CloseAfter = 3
            });
            Events.Events.Current.Publish<string>($"ETS2LA.Backend.Enabled", plugin.Info.Id);
            Events.Events.Current.Publish($"ETS2LA.Backend.Enabled.{plugin.Info.Id}", EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            // stacktrace + inner exceptions
            // (basically we get the full exception info, inside the assembly context)
            if (ex is System.Reflection.ReflectionTypeLoadException rtle)
            {
                Logger.Error($"Failed enable {plugin.GetType().FullName}: {rtle}");
                foreach (var le in rtle.LoaderExceptions)
                {
                    Logger.Error(le?.ToString() ?? "LoaderException: null");
                }
            }
            else if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
            {
                Logger.Error($"Failed enable {plugin.GetType().FullName}: {tie.InnerException}");
                Logger.Error(tie.InnerException.ToString());
            }
            else
            {
                Logger.Error($"Failed enable {plugin.GetType().FullName}: {ex}");
            }
            return false;
        }
    }

    public bool DisablePlugin(IPlugin? plugin = null, string? pluginId = null)
    {
        plugin ??= GetPluginById(pluginId!);
        if (plugin == null)
        {
            Logger.Warn($"Tried to disable {pluginId}, but it was not found among loaded plugins.");
            return false;
        }

        try
        {
            plugin.OnDisable();
            Logger.Info($"Disabled plugin: [bold]{plugin.Info.Name}[/]");
            PluginDisabled?.Invoke(plugin);
            Events.Events.Current.Publish<string>($"ETS2LA.Backend.Disabled", plugin.Info.Id);
            Events.Events.Current.Publish($"ETS2LA.Backend.Disabled.{plugin.Info.Id}", EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to disable {plugin.GetType().FullName}: {ex.Message}");
            return false;
        }
    }
}