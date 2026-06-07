using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ETS2LA.Logging;

namespace ETS2LA.UI.Views;

public partial class DashboardView : UserControl
{

    public string CurrentRelease { get; set; } = "Unknown";
    public int UsersOnline { get; set; } = 123;
    public int UsersOver24h { get; set; } = 456;

    public DashboardView()
    {
        CurrentRelease = $"v{Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)}";
        InitializeComponent();
        DataContext = this;
    }

    private void OpenDiscord(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://discord.gg/ets2la") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open Discord link: {ex.Message}");
        }
    }

    private void OpenGitHub(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/ETS2LA/Euro-Truck-Simulator-2-Lane-Assist") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open GitHub link: {ex.Message}");
        }
    }

    private void OpenKoFi(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://ko-fi.com/ets2la") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open Ko-Fi link: {ex.Message}");
        }
    }
}
