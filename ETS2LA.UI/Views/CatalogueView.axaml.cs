using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.Media;
using ETS2LA.UI.Services;
using ETS2LA.Shared;
using ETS2LA.Networking;
using ETS2LA.Networking.Plugins;
using ETS2LA.Logging;
using System.Reflection;
using ETS2LA.Backend.Plugins;

namespace ETS2LA.UI.Views;

public partial class CatalogueView : UserControl, INotifyPropertyChanged
{
    // This list is listened by the UI to show available plugins.
    public ObservableCollection<NetworkPluginItem> Plugins { get; } = new();
    public ObservableCollection<NetworkPluginItem> FilteredPlugins { get; } = new();

    public bool HasPlugins => Plugins.Count > 0;
    public int PluginColumns => (int)(Math.Floor(Bounds.Width / 1000) + 1);
    public bool NeedsRestart {get; set;} = false;

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value) return;
            _searchQuery = value;
            OnPropertyChanged();
            FilterPlugins();
        }
    }

    public CatalogueView()
    {
        UpdatePluginList();
        InitializeComponent();
        DataContext = this;
        
        SizeChanged += (_, _) => OnPropertyChanged(nameof(PluginColumns));
    }

    private void OnRestartClick(object? sender, RoutedEventArgs e)
    {
        Logger.Info("Restarting ETS2LA...");
        using Process currentProcess = Process.GetCurrentProcess();
        var startInfo = new ProcessStartInfo
        {
            FileName = currentProcess.MainModule?.FileName,
            UseShellExecute = true
        };
        // Let the new instance wait for this process to exit.
        startInfo.ArgumentList.Add($"--restart-parent-process-id={currentProcess.Id}");
        Process.Start(startInfo);
        Environment.Exit(0);
    }

    private void RefreshCatalogueClick(object? sender, RoutedEventArgs e)
    {
        UpdatePluginList();
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return; // avoid toggling when pressing buttons
        if (sender is Huskui.Avalonia.Controls.Card { Tag: NetworkPluginItem item })
        {
            if (item.IsInstalled)
            {
                if (NetworkingClient.Current.Plugins.PluginHasUpdateAvailable(item.Id))
                {
                    Logger.Info($"Updating plugin {item.Name} ({item.Id})");
                    if (NetworkingClient.Current.Plugins.UpdatePlugin(item.Id))
                    {
                        item.SetInstalled(true);
                    }
                }
                else
                {
                    Logger.Info($"Uninstalling plugin {item.Name} ({item.Id})");
                    if (NetworkingClient.Current.Plugins.UninstallPlugin(item.Id))
                    {
                        item.SetInstalled(false);
                    }
                }
            }
            else
            {
                Logger.Info($"Installing plugin {item.Name} ({item.Id})");
                if (NetworkingClient.Current.Plugins.InstallPlugin(item.Id))
                {
                    item.SetInstalled(true);
                }
            }

        }
        
        NeedsRestart = true;
        OnPropertyChanged(nameof(NeedsRestart));
        // Might've installed other plugins as well.
        UpdatePluginList();
    }

    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Huskui.Avalonia.Controls.Card { Tag: NetworkPluginItem item })
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                if (item.IsInstalled)
                {

                    Logger.Info($"Uninstalling plugin {item.Name} ({item.Id})");
                    if (NetworkingClient.Current.Plugins.UninstallPlugin(item.Id))
                    {
                        item.SetInstalled(false);
                    }
                }
                else
                {
                    Logger.Info($"Installing plugin {item.Name} ({item.Id})");
                    if (NetworkingClient.Current.Plugins.InstallPlugin(item.Id))
                    {
                        item.SetInstalled(true);
                    }
                }
            }
        }
    }

    private void UpdatePluginList()
    {
        Plugins.Clear();
        List<NetworkPlugin> plugins = NetworkingClient.Current.Plugins.AvailablePlugins;
        foreach (var plugin in plugins)
        {
            Plugins.Add(new NetworkPluginItem(plugin));
            if (InstalledPluginManifest.Current.InstalledPlugins.Exists(p => p.Id == plugin.Id))
            {
                Plugins[^1].SetInstalled(true);
            }
        }

        FilterPlugins();
        OnPropertyChanged(nameof(HasPlugins));
    }

    private void FilterPlugins()
    {
        string query = SearchQuery.Trim().ToLowerInvariant();
        FilteredPlugins.Clear();
        foreach (var plugin in Plugins)
        {
            if (string.IsNullOrEmpty(query) || 
                plugin.Name.ToLowerInvariant().Contains(query) || 
                plugin.Description.ToLowerInvariant().Contains(query) || 
                plugin.Author.ToLowerInvariant().Contains(query))
            {
                FilteredPlugins.Add(plugin);
            }
        }
        OnPropertyChanged(nameof(FilteredPlugins));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class NetworkPluginItem : INotifyPropertyChanged
{
    private readonly NetworkPlugin _instance;
    private readonly NetworkPluginVersion? _latestVersion;
    private bool _isInstalled;

    private string CurrentRelease = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    public string Id => _instance.Id;
    public string Name => _instance.Name;
    public string AutomationName => GetAutomationText();
    public string Description => _instance.Description;
    public string Version => _latestVersion?.Version ?? "N/A";
    public string SupportedVersion => _latestVersion?.AppVersion ?? "N/A";
    public string Author => _instance.Author;
    public string WebsiteUrl => _instance.WebsiteUrl;
    public string DependenciesCount => _latestVersion?.Dependencies.Count.ToString() ?? "0";
    public string DependenciesTooltip => _latestVersion?.Dependencies.Count == 0 ? "No dependencies" : "Dependencies:\n" + string.Join("\n", _latestVersion?.Dependencies ?? new List<string>());
    public string Initials => BuildInitials(Name);
    public bool IsPlugin => _instance.Tags.Contains(NetworkPluginTags.Plugin);
    public bool IsLibrary => _instance.Tags.Contains(NetworkPluginTags.Library);
    public bool NeedsUpdate => NetworkingClient.Current.Plugins.PluginHasUpdateAvailable(Id);

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled == value) return;
            _isInstalled = value;
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(AutomationName));
        }
    }

    public NetworkPluginItem(NetworkPlugin instance)
    {
        _instance = instance;
        _latestVersion = _instance.GetLatestCompatibleVersion(
            CurrentRelease, 
            Environment.OSVersion.Platform != PlatformID.Unix ? Networking.Plugins.OperatingSystem.Windows 
                                                              : Networking.Plugins.OperatingSystem.Linux
        );
    }

    public void SetInstalled(bool installed)
    {
        IsInstalled = installed;
        OnPropertyChanged(nameof(IsInstalled));
    }

    private static string BuildInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return parts[0].Length >= 2 ? parts[0].Substring(0, 2).ToUpperInvariant() : parts[0].ToUpperInvariant();
        return string.Concat(parts.Take(2).Select(p => p[0])).ToUpperInvariant();
    }

    private string GetAutomationText()
    {
        string text = _isInstalled ? "Installed plugin card," : "Uninstalled plugin card,";
        text += $" {Name}";
        text += ", button";
        return text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
