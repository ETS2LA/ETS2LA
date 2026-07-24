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
using ETS2LA.Logging;

namespace ETS2LA.UI.Views;

public partial class ManagerView : UserControl, INotifyPropertyChanged
{  
    // This list is listened by the UI to show available plugins.
    public ObservableCollection<PluginItem> Plugins { get; } = new();
    public ObservableCollection<PluginItem> FilteredPlugins { get; } = new();
    private readonly PluginManagerService _pluginService;
    public bool HasPlugins => Plugins.Count > 0;
    public int PluginColumns => (int)(Math.Floor(Bounds.Width / 1000) + 1);

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

    public ManagerView(PluginManagerService service)
    {
        _pluginService = service;

        if (!service.backend.IsLoaded) service.backend.OnBackendLoaded += (s, e) => UpdatePluginList();
        else UpdatePluginList();

        InitializeComponent();
        DataContext = this;
        
        SizeChanged += (_, _) => OnPropertyChanged(nameof(PluginColumns));
    }

    private void TogglePluginClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: PluginItem item })
        {
            item.Toggle();
        }
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return; // avoid toggling when pressing buttons
        if (sender is Huskui.Avalonia.Controls.Card { Tag: PluginItem item })
        {
            item.Toggle();
        }
    }

    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Huskui.Avalonia.Controls.Card { Tag: PluginItem item })
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                item.Toggle();
            }
        }
    }

    private void UpdatePluginList()
    {
        Plugins.Clear();
        List<IPlugin> plugins = _pluginService.GetPlugins();
        foreach (var plugin in plugins)
        {
            Plugins.Add(new PluginItem(plugin, _pluginService));
            _pluginService.backend.PluginHandler?.PluginEnabled += (enabledPlugin) =>
            {
                if (enabledPlugin == plugin)
                {
                    var item = Plugins.FirstOrDefault(pi => pi.Id == plugin.Info.Id);
                    item?.Update();
                }
            };
            _pluginService.backend.PluginHandler?.PluginDisabled += (disabledPlugin) =>
            {
                if (disabledPlugin == plugin)
                {
                    var item = Plugins.FirstOrDefault(pi => pi.Id == plugin.Info.Id);
                    item?.Update();
                }
            };
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

    private void OnUnloadButtonClick(object? sender, RoutedEventArgs e)
    {
        _pluginService.UnloadPlugins();
        UpdatePluginList();
    }

    private void OnReloadButtonClick(object? sender, RoutedEventArgs e)
    {
        _pluginService.ReloadPlugins();
        UpdatePluginList();
    }

    private void OnOpenFolderButtonClick(object? sender, RoutedEventArgs e)
    {
        string location = _pluginService.backend.PluginHandler?.PluginRootPath ?? string.Empty;
        if (string.IsNullOrEmpty(location))
        {
            location = Directory.GetCurrentDirectory() + "/Plugins";
        }

        # if WINDOWS
        location = location.Replace("/", "\\");
        Process.Start("explorer.exe", location);
        # elif LINUX
        Process.Start("xdg-open", location);
        # else
        Process.Start("open", location);
        #endif
    }

    private void OpenCataloguePage(object? sender, RoutedEventArgs e)
    {
        var mainWindow = MainWindow.Instance;
        if (mainWindow != null)
        {
            mainWindow.NavigateToPage(MainWindow.PageKind.Catalogue);
        }
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

public class PluginItem : INotifyPropertyChanged
{
    private readonly PluginManagerService _service;
    private readonly IPlugin _instance;
    private bool _isEnabled;

    public string Id => _instance.Info.Id;
    public string Name => _instance.Info.Name;
    public string AutomationName => GetAutomationText();
    public string Description => _instance.Info.Description;
    public string Version => _instance.Info.Version;
    public string IconUrl => _instance.Info.Icon;
    public string SupportedVersion => _instance.Info.SupportedETS2LA;
    public string Author => _instance.Info.AuthorName;
    public string AuthorLink => _instance.Info.AuthorWebsite;
    public string DependenciesCount => _instance.Info.Dependencies.Count.ToString();
    public string DependenciesTooltip => _instance.Info.Dependencies.Count == 0 ? "No dependencies" : "Dependencies:\n" + string.Join("\n", _instance.Info.Dependencies);
    public string Initials => BuildInitials(Name);

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(AutomationName));
        }
    }

    public PluginItem(IPlugin instance, PluginManagerService service)
    {
        _instance = instance;
        _service = service;
        _isEnabled = _instance._IsRunning;
        Update();
    }

    public void Update()
    {
        IsEnabled = _instance._IsRunning;
    }

    public void Toggle()
    {
        var target = !IsEnabled;
        _service.SetEnabled(_instance, target);
        Update();
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
        string text = _isEnabled ? "Enabled plugin card," : "Disabled plugin card,";
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
