using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;

using ETS2LA.Backend.Events;
using ETS2LA.UI.Views;
using ETS2LA.UI.Services;
using ETS2LA.UI.Notifications;
using ETS2LA.Notifications;
using ETS2LA.UI.Settings;

using Huskui.Avalonia.Models;
using Huskui.Avalonia.Controls;

namespace ETS2LA.UI;

// TODO: Documentation, cleanup code!
public partial class MainWindow : AppWindow
{
    public static MainWindow? Instance { get; private set; }
    
    public enum PageKind
    {
        Dashboard,
        Visualization,
        Manager,
        Catalogue,
        Performance,
        Wiki,
        Roadmap,
        Settings
    }

    private readonly List<Button> navButtons = new();
    private readonly PluginManagerService pluginService;
    private readonly DashboardView dashboardView = new();
    private readonly WikiView wikiView = new();
    private readonly ManagerView managerView;
    private readonly CatalogueView catalogueView;
    private readonly SettingsView settingsView;
    public static event EventHandler? WindowOpened;

    public MainWindow()
    {
        Instance = this;
        CanResize = true;
        ExtendClientAreaToDecorationsHint = true;
        InitializeComponent();

        // Linux distros don't add their own window borders. To match windows' appearance
        // we need to add those ourselves.
        # if LINUX
            MainBorder.BorderThickness = new Avalonia.Thickness(1);
            MainBorder.CornerRadius = new Avalonia.CornerRadius(4);
            MainBorder.ClipToBounds = true;
            DragCorner.IsVisible = true; // Linux systems don't support BorderOnly resizing
                                         // so we need to add our own drag corner.
        # endif

        VersionText.Text = $"v{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)}";
        UINotificationHandler.Current.SetWindow(this);

        pluginService = new PluginManagerService();
        managerView = new ManagerView(pluginService);
        catalogueView = new CatalogueView();
        settingsView = new SettingsView();
        navButtons.AddRange(new[]
        {
            DashboardButton, VisualizationButton, ManagerButton, CatalogueButton,
            PerformanceButton, WikiButton, RoadmapButton, SettingsButton
        });

        UpdateTitlebarButtonVisibility();
        SetSelected(DashboardButton);
        ShowPage(PageKind.Dashboard);

        UISettings settings = UISettingsHandler.Current.GetSettings();
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        Position = new Avalonia.PixelPoint(settings.WindowX, settings.WindowY);

        Opened += (s, e) => Events.Current.Publish("ETS2LA.UI.WindowOpened", e);
        Opened += (s, e) => WindowOpened?.Invoke(this, e);
    }

    private void OnTitlebarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnDragCornerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginResizeDrag(WindowEdge.SouthEast, e);
    }

    private void OnStayOnTopClick(object? sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        StayOnTopIcon.Value = Topmost ? "mdi-picture-in-picture-bottom-right" : "mdi-picture-in-picture-bottom-right-outline";
        if (Topmost) StayOnTopIcon.Classes.Add("Highlight");
        else StayOnTopIcon.Classes.Remove("Highlight");
        
        NotificationHandler.Current.SendNotification(new Notification
        {
            Id = "MainWindow.StayOnTopChanged",
            Title = "Stay On Top",
            Content = Topmost ? "Enabled" : "Disabled",
            CloseAfter = 2.0f,
            Level = Topmost ? NotificationLevel.Success : NotificationLevel.Danger
        });
    }

    private void OnTransparencyClick(object? sender, RoutedEventArgs e)
    {
        this.Opacity = this.Opacity == 1.0 ? 0.8 : 1.0;
        TransparencyIcon.Value = this.Opacity == 1.0 ? "fa-circle" : "fa-circle-half-stroke";
        if(this.Opacity == 1.0) TransparencyIcon.Classes.Remove("Highlight");
        else TransparencyIcon.Classes.Add("Highlight");
        
        NotificationHandler.Current.SendNotification(new Notification
        {
            Id = "MainWindow.TransparencyChanged",
            Title = "Transparency",
            Content = this.Opacity < 1.0 ? "Enabled" : "Disabled",
            CloseAfter = 2.0f,
            Level = this.Opacity < 1.0 ? NotificationLevel.Success : NotificationLevel.Danger
        });
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaxRestoreClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeRestoreIcon.Value = WindowState == WindowState.Maximized ? "fa-window-restore" : "fa-window-maximize";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        NotificationHandler.Current.SendNotification(new Notification
        {
            Id = "MainWindow.Shutdown",
            Title = "ETS2LA",
            Content = "Shutting down application & backend...",
            CloseAfter = 20.0f
        });
        pluginService.Shutdown();
        UINotificationHandler.Current.Shutdown();

        UISettings settings = UISettingsHandler.Current.GetSettings();
        settings.WindowWidth = (int)Width;
        settings.WindowHeight = (int)Height;
        settings.WindowX = Position.X;
        settings.WindowY = Position.Y;
        UISettingsHandler.Current.Save();

        Close();
    }

    private void UpdateTitlebarButtonVisibility()
    {
        if (MainSplitView.IsPaneOpen)
        {
            ToggleSidebarIcon.Value = "fa-right-to-bracket";
            ToggleSidebarIcon.RenderTransform = new RotateTransform(180);
            TitlebarDividerLeft.IsVisible = false;
            TitlebarDividerRight.IsVisible = false;
            ManagerButtonTitlebar.IsVisible = false;
            VisualizationButtonTitlebar.IsVisible = false;
            SettingsButtonTitlebar.IsVisible = false;
        }
        else
        {
            ToggleSidebarIcon.Value = "fa-right-from-bracket";
            ToggleSidebarIcon.RenderTransform = new RotateTransform(0);
            TitlebarDividerLeft.IsVisible = true;
            TitlebarDividerRight.IsVisible = true;
            ManagerButtonTitlebar.IsVisible = true;
            VisualizationButtonTitlebar.IsVisible = true;
            SettingsButtonTitlebar.IsVisible = true;
        }
    }

    private void TogglePane(object? sender, RoutedEventArgs e)
    {
        MainSplitView.IsPaneOpen = !MainSplitView.IsPaneOpen;
        ContentBorder.CornerRadius = MainSplitView.IsPaneOpen ? new Avalonia.CornerRadius(12, 0, 0, 0) : new Avalonia.CornerRadius(0);
        ContentBorder.BorderThickness = MainSplitView.IsPaneOpen ? new Avalonia.Thickness(1,1,0,0) : new Avalonia.Thickness(0,1,0,0);
        UpdateTitlebarButtonVisibility();
    }

    private UserControl ClosePaneAndOpen(UserControl page)
    {
        MainSplitView.IsPaneOpen = false;
        ContentBorder.CornerRadius = new Avalonia.CornerRadius(0);
        ContentBorder.BorderThickness = new Avalonia.Thickness(0,1,0,0);
        UpdateTitlebarButtonVisibility();
        return page;
    }

    private void ShowPage(PageKind page)
    {
        Events.Current.Publish<string>("ETS2LA.UI.SwitchedPage", page.ToString());
        Events.Current.Publish<EventArgs>($"ETS2LA.UI.SwitchedPage.{page.ToString()}", EventArgs.Empty);
        ContentHost.Content = page switch
        {
            PageKind.Dashboard => dashboardView,
            PageKind.Manager => managerView,
            PageKind.Visualization => CreatePlaceholder("Sorry", "This page is being remade and isn't available in this version. It will return in a future update."),
            PageKind.Catalogue => catalogueView,
            PageKind.Performance => CreatePlaceholder("Performance", "This page hasn't been implemented yet, you can monitor performance using external tools."),
            PageKind.Wiki => wikiView,
            PageKind.Roadmap => CreatePlaceholder("Roadmap", "Please take a look at our public roadmap on GitHub. Navigate to the repository and click on the Projects tab at the top."),
            PageKind.Settings => settingsView,
            _ => dashboardView
        };
    }

    private Control CreatePlaceholder(string title, string body)
    {
        return new Border {
            Padding = new Avalonia.Thickness(20),
            Child = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                        new TextBlock { Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                    }
                }
            }
        };
    }

    private void SetSelected(Button active)
    {
        foreach (var button in navButtons)
        {
            button.Classes.Remove("Selected");
        }
        active.Classes.Add("Selected");
    }

    private void OnDashboardClick(object? sender, RoutedEventArgs e)
    {
        SetSelected(DashboardButton);
        ShowPage(PageKind.Dashboard);
    }

    private void OnVisualizationClick(object? sender, RoutedEventArgs e)
    {
        SetSelected(VisualizationButton);
        ShowPage(PageKind.Visualization);
    }

    private void OnManagerClick(object? sender, RoutedEventArgs e)
    {
        SetSelected(ManagerButton);
        ShowPage(PageKind.Manager);
    }

    private void OnCatalogueClick(object? sender, RoutedEventArgs e)
    {
        SetSelected(CatalogueButton);
        ShowPage(PageKind.Catalogue);
    }

    private void OnPerformanceClick(object? sender, RoutedEventArgs e)
    {
        SetSelected(PerformanceButton);
        ShowPage(PageKind.Performance);
    }

    private void OnWikiClick(object? sender, RoutedEventArgs e)
    {
        SetSelected(WikiButton);
        ShowPage(PageKind.Wiki);
    }

    private void OnRoadmapClick(object? sender, RoutedEventArgs e)
    {
        SetSelected(RoadmapButton);
        ShowPage(PageKind.Roadmap);
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        SetSelected(SettingsButton);
        ShowPage(PageKind.Settings);
    }

    public void NavigateToPage( PageKind page)
    {
        switch (page)
        {
            case PageKind.Dashboard:
                ShowPage(PageKind.Dashboard);
                SetSelected(DashboardButton);
                break;
            case PageKind.Visualization:
                ShowPage(PageKind.Visualization);
                SetSelected(VisualizationButton);
                break;
            case PageKind.Manager:
                ShowPage(PageKind.Manager);
                SetSelected(ManagerButton);
                break;
            case PageKind.Catalogue:
                ShowPage(PageKind.Catalogue);
                SetSelected(CatalogueButton);
                break;
            case PageKind.Performance:
                ShowPage(PageKind.Performance);
                SetSelected(PerformanceButton);
                break;
            case PageKind.Wiki:
                ShowPage(PageKind.Wiki);
                SetSelected(WikiButton);
                break;
            case PageKind.Roadmap:
                ShowPage(PageKind.Roadmap);
                SetSelected(RoadmapButton);
                break;
            case PageKind.Settings:
                ShowPage(PageKind.Settings);
                SetSelected(SettingsButton);
                break;
        }
    }
}
