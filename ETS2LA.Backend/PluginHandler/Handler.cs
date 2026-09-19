using ETS2LA.Shared;
using ETS2LA.Logging;
using ETS2LA.Backend.Events;
using ETS2LA.Backend.Plugins;
using ETS2LA.Notifications;
using static ETS2LA.Translations.T;

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

    public IEnumerable<Assembly> PluginAssemblies => 
        _pluginLoadContexts.Values
            .SelectMany(ctx => ctx.Assemblies)
            .Distinct();

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
            Logger.Info(_("Created plugin directory: [gray]{0}[/]", path));
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
            Logger.Error(_("Failed to discover Dlls: {0}", ex.Message));
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
                Logger.Warn(_("Unknown manifest type: {0}", type));
                return Array.Empty<string>();
        }
    }

    public void LoadLibraries()
    {
        string[] libraryFiles = DiscoverManualDlls("Libraries");
        Logger.Info(_n("Discovered {0} manually installed library", "Discovered {0} manually installed libraries.", libraryFiles.Length, libraryFiles.Length));
        libraryFiles = libraryFiles.Concat(DiscoverManifestDlls(PluginType.Library)).ToArray();
        Logger.Info(_n("Discovered {0} library in total", "Discovered {0} libraries in total.", libraryFiles.Length, libraryFiles.Length));

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
                    if (LoadedLibraryPlugins.Any(p => p.Info.Id == libraryPlugin.Info.Id))
                    {
                        Logger.Warn(_("Library plugin with ID [gray]{0}[/] is already loaded, skipping duplicate.", libraryPlugin.Info.Id));
                        continue;
                    }

                    LoadedLibraryPlugins.Add(libraryPlugin);
                    Logger.Info(_("Loaded library plugin: [gray]{0}[/] from [gray]{1}[/].", type.FullName ?? "Unknown", filename));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(_("Failed to load library plugin from [gray]{0}[/]: {1}", filename, ex));
            }
        }
    }

    public void LoadPlugins()
    {
        loading = true;
        string[] pluginFiles = DiscoverManualDlls("Plugins");
        Logger.Info(_n("Discovered {0} manually installed plugin.", "Discovered {0} manually installed plugins.", pluginFiles.Length, pluginFiles.Length));
        pluginFiles = pluginFiles.Concat(DiscoverManifestDlls(PluginType.Plugin)).ToArray();
        Logger.Info(_n("Discovered {0} plugins in total.", "Discovered {0} plugins in total.", pluginFiles.Length, pluginFiles.Length));

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
                    if (LoadedPlugins.Any(p => p.Info.Id == plugin.Info.Id))
                    {
                        Logger.Warn(_("Plugin with ID [gray]{0}[/] is already loaded, skipping duplicate.", type.FullName ?? "Unknown"));
                        continue;
                    }
                    
                    plugin.Init();
                    LoadedPlugins.Add(plugin);
                    _pluginLoadContexts[plugin] = loadContext;
                    
                    Logger.Info(_("Loaded plugin: [gray]{0}[/] from [gray]{1}[/].", type.FullName ?? "Unknown", filename));
                }
            }
            catch (Exception ex)
            {
                // stacktrace + inner exceptions
                // (basically we get the full exception info, inside the assembly context)
                if (ex is System.Reflection.ReflectionTypeLoadException rtle)
                {
                    Logger.Error(_("Failed to load plugin from [gray]{0}[/]: {1}", filename, rtle));
                    foreach (var le in rtle.LoaderExceptions)
                    {
                        Logger.Error(_("LoaderException: {0}", le?.ToString() ?? "null"));
                    }
                }
                else if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
                {
                    Logger.Error(_("Failed to load plugin from [gray]{0}[/]: {1}", filename, tie.InnerException));
                    Logger.Error(_("{0}", tie.InnerException.ToString()));
                }
                else
                {
                    Logger.Error(_("Failed to load plugin from [gray]{0}[/]: {1}", filename, ex));
                }
            }
        }

        NotificationHandler.Current.SendNotification(new Notification
        {
            Id = "Backend.PluginHandler.Loading",
            Title = _("Finished loading plugins"),
            Content = _n("Loaded {0} plugin from the Plugins folder.", "Loaded {0} plugins from the Plugins folder.", LoadedPlugins.Count, LoadedPlugins.Count),
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
                Logger.Error(_("Failed to shutdown plugin {0}: {1}", plugin.GetType().FullName ?? "Unknown", ex.Message));
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
                Logger.Error(_("Failed to unload plugin load context: {0}", ex.Message));
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
            Logger.Debug(_("Failed to clean plugin shadow directory [gray]{0}[/], it will be removed on the next startup: {1}", shadowDirectory, ex.Message));
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
                Logger.Debug(_("Skipped stale plugin shadow directory [gray]{0}[/]: {1}", directory, ex.Message));
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
            Logger.Warn(_("Tried to enable {0}, but it was not found among loaded plugins.", pluginId ?? "Unknown"));
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
                        Title = _("{0}", plugin.Info.Name),
                        Content = _("Missing dependency: {0}", dependencyId),
                        Level = NotificationLevel.Danger
                    });
                    Logger.Warn(_("Cannot enable plugin {0} because dependency {1} was not found.", plugin.Info.Name, dependencyId));
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
                        Title = _("{0}", plugin.Info.Name),
                        Content = _("Failed to enable dependency: {0}", dependency.Info.Name),
                        Level = NotificationLevel.Danger
                    });
                    Logger.Warn(_("Cannot enable plugin {0} because dependency {1} failed to enable.", plugin.Info.Name, dependency.Info.Name));
                    return false;
                }
            }
        }

        try
        {
            plugin.OnEnable();

            Logger.Info(_("Enabled plugin: [bold]{0}[/]", plugin.Info.Id));
            PluginEnabled?.Invoke(plugin);
            NotificationHandler.Current.SendNotification(new Notification
            {
                Id = $"Backend.PluginHandler.PluginEnabled.{plugin.Info.Id}",
                Title = _("{0}", plugin.Info.Name),
                Content = _("The plugin was enabled successfully."),
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
                Logger.Error(_("Failed enable {0}: {1}", plugin.GetType().FullName ?? "Unknown", rtle));
                foreach (var le in rtle.LoaderExceptions)
                {
                    Logger.Error(_("LoaderException: {0}", le?.ToString() ?? "null"));
                }
            }
            else if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
            {
                Logger.Error(_("Failed enable {0}: {1}", plugin.GetType().FullName ?? "Unknown", tie.InnerException));
                Logger.Error(_("Inner Exception: {0}", tie.InnerException.ToString()));
            }
            else
            {
                Logger.Error(_("Failed enable {0}: {1}", plugin.GetType().FullName ?? "Unknown", ex));
            }
            return false;
        }
    }

    public bool DisablePlugin(IPlugin? plugin = null, string? pluginId = null)
    {
        plugin ??= GetPluginById(pluginId!);
        if (plugin == null)
        {
            Logger.Warn(_("Tried to disable {0}, but it was not found among loaded plugins.", pluginId ?? "Unknown"));
            return false;
        }

        try
        {
            plugin.OnDisable();
            Logger.Info(_("Disabled plugin: [bold]{0}[/]", plugin.Info.Name));
            PluginDisabled?.Invoke(plugin);
            Events.Events.Current.Publish<string>($"ETS2LA.Backend.Disabled", plugin.Info.Id);
            Events.Events.Current.Publish($"ETS2LA.Backend.Disabled.{plugin.Info.Id}", EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(_("Failed to disable {0}: {1}", plugin.GetType().FullName ?? "Unknown", ex.Message));
            return false;
        }
    }
}