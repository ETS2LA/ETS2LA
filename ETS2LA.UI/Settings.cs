using ETS2LA.Settings;

namespace ETS2LA.UI;

[Serializable]
public class WindowSettings
{
    public int X = 0;
    public int Y = 0;
    public int Width = 1280;
    public int Height = 720;

    public int Zoom = 100;
    public bool StayOnTop = true;

    public int OnboardingStep = 0;
    public bool HasCompletedOnboarding = false;

    [NonSerialized]
    private static readonly Lazy<WindowSettings> _instance = new(() => new WindowSettings(loadSettings: true));
    public static WindowSettings Current => _instance.Value;

    [NonSerialized]
    private SettingsHandler? _settingsHandler;

    public WindowSettings(bool loadSettings = false)
    {
        if (loadSettings)
        {
            _settingsHandler = new SettingsHandler();
            var loadedSettings = _settingsHandler.Load<WindowSettings>("WindowSettings.json");
            if (loadedSettings != null)
            {
                X = loadedSettings.X;
                Y = loadedSettings.Y;
                if (X < 0 || Y < 0)
                {
                    // Reset, clearly wrong
                    X = 0;
                    Y = 0;
                }
                Width = loadedSettings.Width;
                Height = loadedSettings.Height;
                Zoom = loadedSettings.Zoom;
                StayOnTop = loadedSettings.StayOnTop;
                OnboardingStep = loadedSettings.OnboardingStep;
                HasCompletedOnboarding = loadedSettings.HasCompletedOnboarding;
            }
            _settingsHandler.RegisterListener<WindowSettings>("WindowSettings.json", OnSettingsChanged);
        }
    }

    public WindowSettings() { }

    public void Save()
    {
        _settingsHandler?.Save<WindowSettings>("WindowSettings.json", this);
    }

    public void OnSettingsChanged(WindowSettings newSettings)
    {
        X = newSettings.X;
        Y = newSettings.Y;
        Width = newSettings.Width;
        Height = newSettings.Height;
        Zoom = newSettings.Zoom;
        StayOnTop = newSettings.StayOnTop;
        OnboardingStep = newSettings.OnboardingStep;
        HasCompletedOnboarding = newSettings.HasCompletedOnboarding;
    }
}