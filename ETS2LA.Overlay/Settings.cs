using ETS2LA.Settings;

namespace ETS2LA.Overlay;

[Serializable]
public class OverlaySettings
{
    public bool LimitFramerate = true;
    public int MaxFramerate = 30;
    public bool SupportMultipleViewports = false;

    // AR
    public bool RenderAR = true;
    public bool SimplifiedGraphics = false;
    public bool DontRenderWhenPaused = true;
    public float MaxARDistance = 150.0f;
}

public class OverlaySettingsHandler
{
    private static readonly Lazy<OverlaySettingsHandler> _instance = new(() => new OverlaySettingsHandler());
    public static OverlaySettingsHandler Current => _instance.Value;

    private SettingsHandler _settingsHandler;
    private OverlaySettings _settings;

    public event Action<OverlaySettings>? OnSettingsUpdated;

    public OverlaySettingsHandler()
    {
        _settingsHandler = new SettingsHandler();
        _settings = _settingsHandler.Load<OverlaySettings>("OverlaySettings.json");
        _settingsHandler.RegisterListener<OverlaySettings>("OverlaySettings.json", OnSettingsChanged);
    }

    public void Save()
    {
        _settingsHandler.Save<OverlaySettings>("OverlaySettings.json", _settings);
    }

    public OverlaySettings GetSettings()
    {
        return _settings;
    }

    private void OnSettingsChanged(OverlaySettings overlaySettings)
    {
        _settings = overlaySettings;
        OnSettingsUpdated?.Invoke(_settings);
    }
}
