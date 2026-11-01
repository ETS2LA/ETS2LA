using Hexa.NET.ImGui;
using ETS2LA.Logging;
using static ETS2LA.Translations.T;
using System.Numerics;

namespace ETS2LA.Overlay.Window;

class ConsoleWindow : InternalWindow
{
    private List<Tuple<string, string>> _consoleMessages = new();
    private int _logLength = 10;

    public ConsoleWindow()
    {
        Definition = new WindowDefinition
        {
            Title = _("Console"),
            Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize,
            X = 10,
            Y = 10,
            Alpha = 0.5f,
            #if !DEBUG
            Open = false
            #endif
        };

        Render = () =>
        {
            for(int i = _logLength; i > 0; i--)
            {
                var color = new Vector4(1f, 1f, 1f, 1f);
                string level, message;
                level = _consoleMessages.ElementAtOrDefault(_consoleMessages.Count - i)?.Item1 ?? "";
                message = _consoleMessages.ElementAtOrDefault(_consoleMessages.Count - i)?.Item2 ?? "";
                if (string.IsNullOrEmpty(level) || string.IsNullOrEmpty(message)) { continue; }

                if (level == "ERR") { color = new Vector4(1f, 0.5f, 0.5f, 1f); }
                else if (level == "WRN") { color = new Vector4(1f, 1f, 0.5f, 1f); }
                else if (level == "INF") { color = new Vector4(0.5f, 0.5f, 1f, 1f); }
                else if (level == "OKK") { color = new Vector4(0.5f, 1f, 0.5f, 1f); }
                ImGui.TextColored(color, $"[{level}]");
                ImGui.SameLine();
                ImGui.Text(message);
            }
        };

        RenderContextMenu = () =>
        {
            if (ImGui.MenuItem(_("Clear Console")))
            {
                _consoleMessages.Clear();
            }
            if (ImGui.MenuItem(_("Show More Logs")))
            {
                _logLength += 2;
            }
            if (ImGui.MenuItem(_("Show Fewer Logs")))
            {
                _logLength = Math.Max(2, _logLength - 2);
            }
        };

        Logger.OnLog += (log) => {
            _consoleMessages.Add(log);
            if (_consoleMessages.Count > 100) { _consoleMessages.RemoveAt(0); }
        };
    }
}