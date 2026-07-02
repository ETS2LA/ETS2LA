using ETS2LA.Logging;
using ETS2LA.Shared;
using ETS2LA.Game.Data;
using ETS2LA.Game.Utils;
using ETS2LA.Notifications;

using TruckLib.HashFs;
using TruckLib.ScsMap;
using TruckLib;

using System.Text;

namespace ETS2LA.Game;

public enum GameType
{
    EuroTruckSimulator2,
    AmericanTruckSimulator
}

public class Mod
{
    public required string Path { get; set; }
    public required bool Load { get; set; }
    public int Priority { get; set; } = 0;
}

public class Installation
{
    public required GameType Type { get; set; }
    public required string Path { get; set; }
    public required string DocumentsPath { get; set; }
    public required string ExecutablePath { get; set; }

    public string Version { get; set; } = "Undetermined";

    public bool IsParsed { get; set; } = false;
    public bool IsParsing { get; set; } = false;
    
    public List<string> FileExclusions = new List<string>
    {
        "dlc_winter.scs",
    };

    public event Action? OnDataParsed;
    public event Action? OnDataNotParsed;
    public event Action? OnParsingStarted;

    private AssetLoader? assetLoader = null;
    private MapData? map = null;
    private List<Mod>? selectedMods = null;

    public MapData? GetMapData()
    {
        return map;
    }

    public IFileSystem? GetFileSystem()
    {
        return assetLoader;
    }

    public List<string> GetLogFileContents()
    {
        var logFileLocation = System.IO.Path.Combine(DocumentsPath, "game.log.txt");
        if (!File.Exists(logFileLocation))
            return new List<string>();

        try
        {
            // This is painful in C# (the game is holding the file so normal read doesn't work)
            // Not sure why that was not an issue in python?
            using (var fileStream = new FileStream(logFileLocation, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] buffer = new byte[fileStream.Length];
                int bytesRead = fileStream.Read(buffer, 0, buffer.Length);
                string content = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                return content.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
            }
        }
        catch (IOException ex)
        {
            Logger.Warn($"Failed to read log file at '{logFileLocation}': {ex.Message}");
            return new List<string>();
        }
    }

    public int GetPriority(string modName)
    {
        if(modName.StartsWith("promods"))
        {
            if (modName.Contains("def"))
                return 1;
            if (modName.Contains("map"))
                return 2;
            if (modName.Contains("assets"))
                return 3;
            return 4; // media and models
        }
        if(modName.StartsWith("eaa"))
        {
            if (modName.Contains("semeuropa"))
                return 5;
            if (modName.Contains("base_share"))
                return 7;
            return 6; // base
        }
        return 100;
    }

    private void GetBaseContent(List<string> scsFiles)
    {
        foreach (string file in Directory.GetFiles(Path, "*.scs"))
        {
            if (FileExclusions.Contains(System.IO.Path.GetFileName(file)))
                continue;
            scsFiles.Add(file);
        }
    }

    private string GetMapFilepath()
    {
        var maps = assetLoader?.GetFiles("/map/") ?? new List<string>();
        if (Type == GameType.EuroTruckSimulator2)
        {
            // Discover modded maps. For example with EAA
            // - /map/europe.external.sii
            // - /map/europe.mbd
            // - /map/mapaeaa.climate.sii
            // - /map/mapaeaa.mbd
            maps.Remove("/map/europe.mbd");
            foreach (var map in maps)
            {
                if (map.EndsWith(".mbd") && !DataSettings.Current.ForceBaseMapName)
                    return map;
            }

            return "/map/europe.mbd";
        }
        else
        {
            maps.Remove("/map/usa.mbd");
            foreach (var map in maps)
            {
                if (map.EndsWith(".mbd") && !DataSettings.Current.ForceBaseMapName)
                    return map;
            }

            return "/map/usa.mbd";
        }
    }

    /// <summary>
    ///  This is an internal class for parsing map data.
    ///  You should instead call .Parse for user-facing functionality.
    /// </summary>
    /// <returns>Boolean state whether parsing was successfull.</returns>
    private bool ParseActual()
    {
        var logs = GetLogFileContents();
        if(!DataUtils.IsRunningBasedOnLogs(logs))
        {
            Logger.Warn($"Installation at '{Path}' is not currently running, waiting for load of profile.");
            return false;
        }

        List<string> modFiles = DataUtils.FindModsFromLogs(logs);
        List<string> scsFiles = new List<string>();
        GetBaseContent(scsFiles);

        List<IFileSystem> hashFsReaders = scsFiles
            .Select(file => HashFsReader.Open(file) as IFileSystem)
            .ToList();

        int modCount = modFiles.Count;
        List<Task> tasks = new List<Task>();
        foreach (string modFile in modFiles)
        {
            Logger.Info($"Adding mod: {modFile}");
            tasks.Add(Task.Run(() => DataUtils.UnpackMod(modFile, hashFsReaders)));
        }

        while (!Task.WhenAll(tasks).IsCompleted)
        {
            int completed = tasks.Count(t => t.IsCompleted);
            NotificationHandler.Current.SendNotification(new Notification
            {
                Id = "ETS2LA.Game.Parsing",
                Title = "Unpacking Mods",
                Content = $"This might take a while... ({completed}/{modCount})",
                IsProgressIndeterminate = false,
                Progress = completed / (float)modCount * 100f,
                CloseAfter = 0
            });
            Thread.Sleep(500);
        }

        // We load the mods first, only then we load
        // the base .scs data.
        hashFsReaders.Reverse();
        assetLoader = new AssetLoader(hashFsReaders.ToArray());
        
        map = new MapData();
        var filepath = GetMapFilepath();
        
        Logger.Info($"Loading map data from '{filepath}'");
        try { map.Read(filepath, assetLoader); }
        catch (Exception ex)
        {
            Logger.Error($"Error loading map data from '{filepath}': {ex.Message}");
            NotificationHandler.Current.SendNotification(new Notification
            {
                Id = "ETS2LA.Game.ErrorParsing",
                Title = "Error Loading Map Data",
                Content = $"An error occurred while loading map data: {ex.Message}",
                IsProgressIndeterminate = false,
                Level = NotificationLevel.Danger,
                CloseAfter = 10
            });
            map = null;
            return false;
        }

        return true;
    }

    /// <summary>
    ///  Parse this installation's map data. Note that this will fail if
    ///  the installation is currently not running, or ETS2LA can't determine
    ///  this installation's log file location.
    /// </summary>
    /// <returns>Boolean state of success.</returns>
    public bool Parse()
    {
        if (IsParsed)
        {
            Logger.Warn($"Installation at '{Path}' has already been parsed.");
            return true;
        }

        IsParsing = true;
        OnParsingStarted?.Invoke();
        Logger.Info($"Parsing installation at '{Path}' (version: {Version})");
        NotificationHandler.Current.SendNotification(new Notification
        {
            Id = "ETS2LA.Game.Parsing",
            Title = "Parsing Map Data",
            Content = "Initializing...",
            IsProgressIndeterminate = true,
            CloseAfter = 0
        });
        
        var success = ParseActual();

        if (map == null || assetLoader == null || !success)
        {
            Logger.Warn($"Failed to load map for installation at '{Path}'");
            IsParsing = false;
            OnDataNotParsed?.Invoke();
            NotificationHandler.Current.CloseNotification("ETS2LA.Game.Parsing");
            return false;
        }

        int prefabs = map.MapItems.Where(x => x.Value is Prefab).ToList().Count;
        int roads = map.MapItems.Where(x => x.Value is Road).ToList().Count;
        int nodes = map.Nodes.Count;

        if (prefabs == 0 && roads == 0 && nodes == 0)
        {
            Logger.Warn($"No map data found for installation at '{Path}'. Is the installation valid?");
            IsParsing = false;
            OnDataNotParsed?.Invoke();
            NotificationHandler.Current.CloseNotification("ETS2LA.Game.Parsing");
            return false;
        }

        Logger.Success($"Finished parsing installation at '{Path}'");
        Logger.Success($"Found {prefabs} prefabs, {roads} roads and {nodes} nodes.");

        IsParsed = true;
        IsParsing = false;
        OnDataParsed?.Invoke();
        NotificationHandler.Current.CloseNotification("ETS2LA.Game.Parsing");
        NotificationHandler.Current.SendNotification(new Notification
        {
            Id = "ETS2LA.Game.Parsing.Complete",
            Title = "Map Data Parsed",
            Content = $"Found {prefabs} prefabs, {roads} roads and {nodes} nodes.",
            IsProgressIndeterminate = false,
            CloseAfter = 5
        });

        return true;
    }

    /// <summary>
    ///  Throw away this installation's parsed map data so the GC can reclaim it.
    ///  Called when the user switches games, we don't want to keep both
    ///  games' data in memory at the same time.
    /// </summary>
    public void ClearParsedData()
    {
        if (!IsParsed)
            return;

        Logger.Info($"Unloading parsed map data for installation at '{Path}'");
        map = null;
        assetLoader = null;
        IsParsed = false;

        // Same trick as in the PluginHandler, force the GC to actually
        // release everything now instead of whenever it feels like it.
        // This also runs the finalizers that close the .scs file handles.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public bool IsSDKInstalled(string version)
    {
        string sdkPath;
        # if WINDOWS
            if (Type == GameType.EuroTruckSimulator2)
                sdkPath = System.IO.Path.Combine(ExecutablePath.Replace("eurotrucks2.exe", ""), "plugins", "ets2la_" + version);
            else
                sdkPath = System.IO.Path.Combine(ExecutablePath.Replace("amtrucks.exe", ""), "plugins", "ets2la_" + version);
        # else
            if (Type == GameType.EuroTruckSimulator2)
                sdkPath = System.IO.Path.Combine(ExecutablePath.Replace("eurotrucks2", ""), "plugins", "ets2la_" + version);
            else                
                sdkPath = System.IO.Path.Combine(ExecutablePath.Replace("amtrucks", ""), "plugins", "ets2la_" + version);
        # endif

        if (File.Exists(sdkPath))
            return true;
        else
            return false;
    }

    public bool InstallSDK(string version)
    {
        string SDKSourcePath;
        # if WINDOWS
            SDKSourcePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SDKs", version, "Windows");
        # else
            SDKSourcePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SDKs", version, "Linux");
        # endif

        string SDKDestinationPath;
        # if WINDOWS
            if (Type == GameType.EuroTruckSimulator2)
                SDKDestinationPath = System.IO.Path.Combine(ExecutablePath.Replace("eurotrucks2.exe", ""), "plugins");
            else
                SDKDestinationPath = System.IO.Path.Combine(ExecutablePath.Replace("amtrucks.exe", ""), "plugins");
        # else
            if (Type == GameType.EuroTruckSimulator2)
                SDKDestinationPath = System.IO.Path.Combine(ExecutablePath.Replace("eurotrucks2", ""), "plugins");
            else                
                SDKDestinationPath = System.IO.Path.Combine(ExecutablePath.Replace("amtrucks", ""), "plugins");
        # endif

        try
        {
            Directory.CreateDirectory(SDKDestinationPath);
            foreach (string newPath in Directory.GetFiles(SDKSourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(SDKSourcePath, SDKDestinationPath), true);
                Logger.Info($"Copied '{newPath}' to '{newPath.Replace(SDKSourcePath, SDKDestinationPath)}'");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to install SDK from '{SDKSourcePath}' to '{SDKDestinationPath}': {ex.Message}");
            return false;
        }
    }

    public bool UninstallSDK(string version)
    {
        string SDKSourcePath;
        # if WINDOWS
            SDKSourcePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SDKs", version, "Windows");
        # else
            SDKSourcePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SDKs", version, "Linux");
        # endif

        string SDKDestinationPath;
        # if WINDOWS
            if (Type == GameType.EuroTruckSimulator2)
                SDKDestinationPath = System.IO.Path.Combine(ExecutablePath.Replace("eurotrucks2.exe", ""), "plugins");
            else
                SDKDestinationPath = System.IO.Path.Combine(ExecutablePath.Replace("amtrucks.exe", ""), "plugins");
        # else
            if (Type == GameType.EuroTruckSimulator2)
                SDKDestinationPath = System.IO.Path.Combine(ExecutablePath.Replace("eurotrucks2", ""), "plugins");
            else                
                SDKDestinationPath = System.IO.Path.Combine(ExecutablePath.Replace("amtrucks", ""), "plugins");
        # endif

        try
        {
            Directory.CreateDirectory(SDKDestinationPath);
            foreach (string newPath in Directory.GetFiles(SDKSourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Delete(newPath.Replace(SDKSourcePath, SDKDestinationPath));
                Logger.Info($"Deleted '{newPath.Replace(SDKSourcePath, SDKDestinationPath)}'");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to uninstall SDK from '{SDKSourcePath}' to '{SDKDestinationPath}': {ex.Message}");
            return false;
        }
    }
}