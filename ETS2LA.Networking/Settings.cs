using ETS2LA.Settings;

using ETS2LA.Networking.Users;

namespace ETS2LA.Networking.Settings;

[Serializable]
public class NetworkingSettings
{
    [NonSerialized]
    private static readonly Lazy<NetworkingSettings> _instance = new(() => new NetworkingSettings(loadSettings: true));
    public static NetworkingSettings Current => _instance.Value;
    /// ---
    
    public User CurrentUser { get; set; } = new User();
    public ApiServer? CurrentApiServer { get; set; } = null;
    public bool AutomaticallySaveSettingsToCloud { get; set; } = false;

    /// ---
    [NonSerialized]
    private SettingsHandler? _settingsHandler;

    public NetworkingSettings(bool loadSettings = false)
    {
        if (loadSettings)
        {
            _settingsHandler = new SettingsHandler();
            var loadedSettings = _settingsHandler.Load<NetworkingSettings>("NetworkingSettings.json");
            if (loadedSettings != null)
            {
                CurrentUser = loadedSettings.CurrentUser ?? new User();
                CurrentApiServer = loadedSettings.CurrentApiServer;
                AutomaticallySaveSettingsToCloud = loadedSettings.AutomaticallySaveSettingsToCloud;
            }

            if (CurrentUser.Id == Guid.Empty)
            {
                CurrentUser.Id = Guid.NewGuid();
                Save();
            }

            _settingsHandler.RegisterListener<NetworkingSettings>("NetworkingSettings.json", OnSettingsChanged);
        }
    }

    public NetworkingSettings() { }

    public void Save()
    {
        _settingsHandler?.Save<NetworkingSettings>("NetworkingSettings.json", this);
    }

    public void OnSettingsChanged(NetworkingSettings newSettings)
    {
        CurrentUser = newSettings.CurrentUser;
        CurrentApiServer = newSettings.CurrentApiServer;
        AutomaticallySaveSettingsToCloud = newSettings.AutomaticallySaveSettingsToCloud;
    }
}