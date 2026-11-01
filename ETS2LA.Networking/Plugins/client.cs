using ETS2LA.Networking.Users;
using ETS2LA.Networking.Settings;
using ETS2LA.Backend;
using ETS2LA.Backend.Events;
using ETS2LA.Backend.Plugins;
using ETS2LA.Notifications;
using static ETS2LA.Translations.T;
using ETS2LA.Logging;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Reflection;

namespace ETS2LA.Networking.Plugins;

public class PluginApiClient
{
    public List<NetworkPlugin> AvailablePlugins { get; private set; } = new List<NetworkPlugin>();
    const string CurrentApiVersion = "v2";

    JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private void Log(string message, NotificationLevel level = NotificationLevel.Information)
    {
        switch (level)
        {
            case NotificationLevel.Information:
                Logger.Info(message);
                break;
            case NotificationLevel.Warning:
                Logger.Warn(message);
                break;
            case NotificationLevel.Danger:
                Logger.Error(message);
                break;
            case NotificationLevel.Success:
                Logger.Success(message);
                break;
            default:
                Logger.Info(message);
                break;
        }

        NotificationHandler.Current.SendNotification(new Notification
        {
            Id = Guid.NewGuid().ToString(),
            Title = _("Plugin Installer"),
            Content = message,
            Level = level
        });
    }

    public async Task FetchAvailablePluginsAsync()
    {
        HttpResponseMessage? response = null;
        try
        {
            var apiServer = NetworkingSettings.Current.CurrentApiServer;
            if (apiServer == null)
            {
                throw new InvalidOperationException("CurrentApiServer is not set.");
            }

            using var httpClient = new HttpClient();
            response = await httpClient.GetAsync($"{apiServer.Value.BaseUrl}/plugins?version={CurrentApiVersion}");
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            AvailablePlugins = JsonSerializer.Deserialize<List<NetworkPlugin>>(jsonResponse, jsonOptions) ?? new List<NetworkPlugin>();

            Log(_n("Fetched {0} plugin from {1}", "Fetched {0} plugins from {1}", AvailablePlugins.Count, AvailablePlugins.Count, apiServer.Value.BaseUrl));
        }
        catch (Exception ex)
        {
            Log(_("Failed to fetch available plugins. Please check your internet connection.") + $" {ex.Message}", NotificationLevel.Danger);
            if (response != null)
            {
                Logger.Warn($"Content: {await response.Content.ReadAsStringAsync()}");
            }
        }
    }

    public NetworkPluginVersion? GetPluginUpdate(string pluginId)
    {
        var plugin = AvailablePlugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null)
        {
            Log(_("Plugin with ID {0} not found in available plugins.", pluginId), NotificationLevel.Warning);
            return null;
        }

        InstalledPlugin? installedPlugin = InstalledPluginManifest.Current.InstalledPlugins.FirstOrDefault(p => p.Id == pluginId);
        if (!installedPlugin.HasValue || string.IsNullOrEmpty(installedPlugin.Value.Version))
        {
            return null;
        }

        var appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        OperatingSystem currentOS = Environment.OSVersion.Platform != PlatformID.Unix ? OperatingSystem.Windows : OperatingSystem.Linux;
        var latestVersion = plugin.GetLatestCompatibleVersion(appVersion, currentOS);

        if (latestVersion == null || string.IsNullOrEmpty(latestVersion.Version))
        {
            Log(_("No valid versions found for plugin with ID {0}.", pluginId), NotificationLevel.Warning);
            return null;
        }

        return new Version(latestVersion.Version) > new Version(installedPlugin.Value.Version) ? latestVersion : null;
    }

    public bool InstallPlugin(string pluginId, string? version = null)
    {
        var plugin = AvailablePlugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null)
        {
            Log(_("Plugin with ID {0} not found.", pluginId), NotificationLevel.Warning);
            return false;   
        }

        var appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        OperatingSystem currentOS = Environment.OSVersion.Platform != PlatformID.Unix ? OperatingSystem.Windows : OperatingSystem.Linux;

        NetworkPluginVersion targetVersion;
        if (string.IsNullOrEmpty(version))
        {
            var latestVersion = plugin.GetLatestCompatibleVersion(appVersion, currentOS);
            if (latestVersion == null)
            {
                Log(_("No valid versions found for plugin with ID {0}.", pluginId), NotificationLevel.Warning);
                return false;
            }
            targetVersion = latestVersion;
        }
        else
        {
            if (plugin.Versions == null || plugin.Versions.Count == 0)
            {
                Log(_("No versions available for plugin with ID {0}.", pluginId), NotificationLevel.Warning);
                return false;
            }
            
            targetVersion = plugin.Versions.FirstOrDefault(v => v.Version == version);
            if (targetVersion == null)
            {
                Log(_("Version {0} not found for plugin with ID {1}.", version, pluginId), NotificationLevel.Warning);
                return false;
            }
            if (!plugin.IsCompatible(targetVersion, appVersion, currentOS))
            {
                Log(_("Version {0} of plugin with ID {1} is not compatible with the current application version or operating system.", version, pluginId), NotificationLevel.Warning);
                return false;
            }
        }

        // Downloading is done from whatever region the user is in
        Region currentRegion = NetworkingSettings.Current.CurrentApiServer?.Name == "China" ? Region.MainlandChina : Region.Global;
        string downloadUrl = targetVersion.DownloadUrl.FirstOrDefault(d => d.Key == Region.Global).Value;
        if (currentRegion == Region.MainlandChina)
            downloadUrl = downloadUrl.Replace("ets2la.com", "ets2la.cn");

        if (string.IsNullOrEmpty(downloadUrl))
        {
            Log(_("No download URL found for plugin with ID {0} in region {1}.", pluginId, currentRegion), NotificationLevel.Warning);
            return false;
        }

        if (targetVersion.Dependencies.Count > 0)
        {
            bool allDependenciesInstalled = true;
            foreach (var dependency in targetVersion.Dependencies)
            {
                // TODO: Check if the dependency is also of the correct version here! For now not necessary...
                if (!InstalledPluginManifest.Current.InstalledPlugins.Any(p => p.Id == dependency.Id))
                {
                    if (!InstallPlugin(dependency.Id, string.IsNullOrEmpty(dependency.Version) ? null : dependency.Version))
                    {
                        Log(_("Failed to install dependency {0} for plugin {1}.", dependency.Id, pluginId), NotificationLevel.Warning);
                        allDependenciesInstalled = false;
                    }
                }
            }
            if (!allDependenciesInstalled)
            {
                Log(_("Not all dependencies for plugin {0} are installed.", pluginId), NotificationLevel.Warning);
                return false;
            }
        }

        string tempFilePath = Path.GetTempFileName();
        using (var httpClient = new HttpClient())
        {
            var downloadTask = httpClient.GetAsync(downloadUrl);
            downloadTask.Wait();
            var downloadResponse = downloadTask.Result;
            if (!downloadResponse.IsSuccessStatusCode)
            {
                Log(_("Failed to download plugin with ID {0} from {1}. Status code: {2}", pluginId, downloadUrl, downloadResponse.StatusCode), NotificationLevel.Warning);
                return false;
            }
            using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var copyTask = downloadResponse.Content.CopyToAsync(fs);
                copyTask.Wait();
            }
        }

        // And the output path is determined by the PluginBackend's PluginRootPath.
        // On windows that's set to none so it's in /Plugins or /Libraries.
        string location = PluginBackend.Current.PluginHandler?.PluginRootPath ?? string.Empty;

        string type = plugin.Tags.Contains(NetworkPluginTags.Plugin) ? "Plugin" : "Library";
        string folder = type == "Plugin" ? "Plugins" : "Libraries";
        string outputPath = Path.Combine(location, folder, plugin.Id);
        Directory.CreateDirectory(outputPath);

        System.IO.Compression.ZipFile.ExtractToDirectory(tempFilePath, outputPath, true);
        File.Delete(tempFilePath);

        // Finally we just have to register this plugin in the InstalledPluginManifest.
        InstalledPluginManifest.Current.InstalledPlugins.Add(new InstalledPlugin
        {
            Id = plugin.Id,
            Version = targetVersion.Version,
            Dependencies = targetVersion.Dependencies,
            DllPath = Path.Combine(outputPath, targetVersion.DllPath),
            Type = type == "Plugin" ? PluginType.Plugin : PluginType.Library
        });
        InstalledPluginManifest.Current.Save();

        Events.Current.Publish<string>("ETS2LA.Plugins.Installed", pluginId);
        Events.Current.Publish<EventArgs>($"ETS2LA.Plugins.Installed.{pluginId}", EventArgs.Empty);
        Log(_("Successfully installed plugin {0} ({1}, {2})", plugin.Name, plugin.Id, targetVersion.Version), NotificationLevel.Success);
        return true;
    }

    public bool UpdatePlugin(string pluginId)
    {
        if (GetPluginUpdate(pluginId) == null)
        {
            Log(_("No update available for plugin with ID {0}.", pluginId), NotificationLevel.Information);
            return false;
        }

        // Uninstall the current version first.
        if (!UninstallPlugin(pluginId, overrideDependencyCheck: true))
        {
            Log(_("Failed to uninstall current version of plugin with ID {0}.", pluginId), NotificationLevel.Warning);
            return false;
        }

        // Then install the latest version.
        if (!InstallPlugin(pluginId))
        {
            Log(_("Failed to install latest version of plugin with ID {0}.", pluginId), NotificationLevel.Warning);
            return false;
        }

        Events.Current.Publish<string>("ETS2LA.Plugins.Updated", pluginId);
        Events.Current.Publish<EventArgs>($"ETS2LA.Plugins.Updated.{pluginId}", EventArgs.Empty);
        Log(_("Successfully updated plugin with ID {0}.", pluginId), NotificationLevel.Success);
        return true;
    }

    public bool UninstallPlugin(string pluginId, bool overrideDependencyCheck = false)
    {
        InstalledPlugin? installedPlugin = InstalledPluginManifest.Current.InstalledPlugins.FirstOrDefault(p => p.Id == pluginId);
        if (installedPlugin == null)
        {
            Log(_("Installed plugin with ID {0} not found.", pluginId), NotificationLevel.Warning);
            return false;
        }

        if (!overrideDependencyCheck)
        {
            // Scan for other plugins that depend on this one.
            var dependentPlugins = InstalledPluginManifest.Current.InstalledPlugins
                .Where(p => p.Dependencies.Any(d => d.Id == pluginId))
                .ToList();
            if (dependentPlugins.Any())
            {
                string dependentPluginIds = string.Join(", ", dependentPlugins.Select(p => p.Id));
                Log(_("Cannot uninstall plugin with ID {0} because the following installed plugins depend on it: {1}", pluginId, dependentPluginIds), NotificationLevel.Warning);
                return false;
            }
        }
        
        // Remove the plugin's files from the filesystem.
        string pluginPath = Path.Combine(
            PluginBackend.Current.PluginHandler?.PluginRootPath ?? string.Empty, 
            installedPlugin.Value.Type == PluginType.Plugin ? "Plugins" 
                                                            : "Libraries", 
            installedPlugin.Value.Id
        );

        if (Directory.Exists(pluginPath)) Directory.Delete(pluginPath, true);
        else
        {
            Log(_("Apparent plugin directory {0} does not exist.", pluginPath), NotificationLevel.Warning);
            return false;
        }

        // And then we remove it from the InstalledPluginManifest.
        InstalledPluginManifest.Current.InstalledPlugins.Remove(installedPlugin.Value);
        InstalledPluginManifest.Current.Save();

        Events.Current.Publish<string>("ETS2LA.Plugins.Uninstalled", pluginId);
        Events.Current.Publish<EventArgs>($"ETS2LA.Plugins.Uninstalled.{pluginId}", EventArgs.Empty);
        Log(_("Successfully uninstalled plugin with ID {0}.", pluginId), NotificationLevel.Success);
        return true;
    }
}