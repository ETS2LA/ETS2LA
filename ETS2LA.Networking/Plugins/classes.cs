// These classes HAVE to be matching with the classes in the backend.
// Do not change them in any way, they're handled by the ETS2LA team!
using ETS2LA.Logging;
using ETS2LA.Networking.Settings;
using ETS2LA.Shared;

namespace ETS2LA.Networking.Plugins;

[Serializable]
public enum Region
{
    Global,
    MainlandChina
}

[Serializable]
public enum OperatingSystem
{
    Windows,
    Linux,
    MacOS
}

[Serializable]
public enum NetworkPluginTags
{
    Plugin,
    Library,
    AIAssisted,
    OpenSource,
    ClosedSource,
    VibeCoded
}

[Serializable]
public class NetworkPlugin
{
    // Plugin ID, in the format of "author.pluginname.somethingelseifneeded"
    public string Id { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty; // has to match the db username for the author
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;

    public List<NetworkPluginVersion> Versions { get; set; } = new();
    public List<NetworkPluginTags> Tags { get; set; } = new();

    public NetworkPluginVersion? GetLatestCompatibleVersion(string appVersion, OperatingSystem os)
    {
        var compatibleVersions = Versions
            .Where(v => IsCompatible(Version.Parse(appVersion), v.AppVersion))
            .Where(v => v.SupportedOperatingSystems.Contains(os))
            .OrderByDescending(v => Version.TryParse(v.Version, out var parsed) ? parsed : new Version(0, 0, 0))
            .ToList();
        return compatibleVersions.FirstOrDefault();
    }

    private bool IsCompatible(Version currentVersion, PluginVersionSupport support)
    {
        if (!string.IsNullOrEmpty(support.LowerBound))
        {
            // if starts with >=, remove that
            var lowerBound = support.LowerBound.StartsWith(">=") ? support.LowerBound[2..] : support.LowerBound;
            if (Version.TryParse(lowerBound, out var lowerBoundVersion))
            {
                if (currentVersion < lowerBoundVersion)
                    return false;
            }
            else { } // missing
        }

        if (!string.IsNullOrEmpty(support.UpperBound))
        {
            // if starts with <=, remove that
            var upperBound = support.UpperBound.StartsWith("<=") ? support.UpperBound[2..] : support.UpperBound;
            if (Version.TryParse(upperBound, out var upperBoundVersion))
            {
                if (currentVersion > upperBoundVersion)
                    return false;
            }
            else { } // missing
        }

        return true;
    }

    public bool IsCompatible(NetworkPluginVersion version, string appVersion, OperatingSystem os)
    {
        return IsCompatible(Version.Parse(appVersion), version.AppVersion) 
            && version.SupportedOperatingSystems.Contains(os) 
            && version.SupportedRegions.Contains(
                NetworkingSettings.Current?.CurrentApiServer?.Name == "China" ? Region.MainlandChina : Region.Global
            );
    }
}

[Serializable]
public class PluginVersionSupport
{
    public string? UpperBound { get; set; } = string.Empty;
    public string? LowerBound { get; set; } = string.Empty;
}

[Serializable]
public class NetworkPluginVersion
{
    public string Version { get; set; } = string.Empty;
    public PluginVersionSupport AppVersion { get; set; } = new();

    public string Changelog { get; set; } = string.Empty;
    public string DllPath { get; set; } = string.Empty;
    
    public List<PluginDependency> Dependencies { get; set; } = new();
    public List<OperatingSystem> SupportedOperatingSystems { get; set; } = new();
    public List<Region> SupportedRegions { get; set; } = new();
    public Dictionary<Region, string> DownloadUrl { get; set; } = new();
}