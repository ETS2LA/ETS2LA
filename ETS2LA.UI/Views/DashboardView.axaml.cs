using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;
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

    private void OpenLink(string url)
    {
        # if LINUX
            // Linux doesn't support Process.Start with UseShellExecute, so we need to use xdg-open.
            new Process
            {
                StartInfo = new ProcessStartInfo("xdg-open", url)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            }.Start();
        # else
            // Windows and macOS can use the default method.
            new Process
            {
                StartInfo = new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                }
            }.Start();
        # endif
    }

    public void OpenGitHub(object? sender, RoutedEventArgs e)
    {
        OpenLink("https://ets2la.com/repo");
    }

    public void OpenDiscord(object? sender, RoutedEventArgs e)
    {
        OpenLink("https://ets2la.com/discord");
    }

    public void OpenDocumentation(object? sender, RoutedEventArgs e)
    {
        OpenLink("https://docs.ets2la.com/docs/Rewrite/Introduction");
    }

    public void OpenDonate(object? sender, RoutedEventArgs e)
    {
        OpenLink("https://ets2la.com/donate");
    }
}
