namespace ETS2LA.Settings.Global;

public enum AccelerationResponseOption
{
    Slow,
    Normal,
    Fast
}

public enum SteeringSensitivityOption
{
    Slow,
    Normal,
    Fast
}

public enum FollowingDistanceOption
{
    Near,
    Normal,
    Far
}

public enum SetSpeedBehaviour
{
    SpeedLimit,
    CurrentSpeed
}

public enum SpeedLimitWarning
{
    Off,
    Visual,
    Chime
}

public enum CollisionAvoidance
{
    Off,
    Late,
    Medium,
    Early
}

[Serializable]
public class AssistanceSettings
{
    [NonSerialized]
    private static readonly Lazy<AssistanceSettings> _instance = new(() => new AssistanceSettings(loadSettings: true));
    public static AssistanceSettings Current => _instance.Value;

    public bool SeparateCruiseAndSteering { get; set; } = false;
    public bool PauseWhenUnstable { get; set; } = true;
    public AccelerationResponseOption AccelerationResponse { get; set; } = AccelerationResponseOption.Normal;
    public SteeringSensitivityOption SteeringSensitivity { get; set; } = SteeringSensitivityOption.Normal;
    public FollowingDistanceOption FollowingDistance { get; set; } = FollowingDistanceOption.Normal;
    public SetSpeedBehaviour SetSpeedBehaviourOption { get; set; } = SetSpeedBehaviour.SpeedLimit;
    public SpeedLimitWarning SpeedLimitWarningOption { get; set; } = SpeedLimitWarning.Visual;
    public CollisionAvoidance CollisionAvoidanceOption { get; set; } = CollisionAvoidance.Early;
    public float MaximumSpeed { get; set; } = 0f; // 0 = no limit
    public bool IgnoreTrafficRules { get; set; } = false;

    [NonSerialized]
    private SettingsHandler? _settingsHandler;

    public AssistanceSettings(bool loadSettings = false)
    {
        if (loadSettings)
        {
            _settingsHandler = new SettingsHandler();
            var loadedSettings = _settingsHandler.Load<AssistanceSettings>("AssistanceSettings.json");
            if (loadedSettings != null)
            {
                SeparateCruiseAndSteering = loadedSettings.SeparateCruiseAndSteering;
                PauseWhenUnstable = loadedSettings.PauseWhenUnstable;
                AccelerationResponse = loadedSettings.AccelerationResponse;
                SteeringSensitivity = loadedSettings.SteeringSensitivity;
                FollowingDistance = loadedSettings.FollowingDistance;
                SetSpeedBehaviourOption = loadedSettings.SetSpeedBehaviourOption;
                SpeedLimitWarningOption = loadedSettings.SpeedLimitWarningOption;
                CollisionAvoidanceOption = loadedSettings.CollisionAvoidanceOption;
                MaximumSpeed = loadedSettings.MaximumSpeed;
                IgnoreTrafficRules = loadedSettings.IgnoreTrafficRules;
            }
            _settingsHandler.RegisterListener<AssistanceSettings>("AssistanceSettings.json", OnSettingsChanged);
        }
    }

    public AssistanceSettings() { }

    public void Save()
    {
        _settingsHandler?.Save<AssistanceSettings>("AssistanceSettings.json", this);
    }

    public void OnSettingsChanged(AssistanceSettings newSettings)
    {
        SeparateCruiseAndSteering = newSettings.SeparateCruiseAndSteering;
        PauseWhenUnstable = newSettings.PauseWhenUnstable;
        AccelerationResponse = newSettings.AccelerationResponse;
        SteeringSensitivity = newSettings.SteeringSensitivity;
        FollowingDistance = newSettings.FollowingDistance;
        SetSpeedBehaviourOption = newSettings.SetSpeedBehaviourOption;
        SpeedLimitWarningOption = newSettings.SpeedLimitWarningOption;
        CollisionAvoidanceOption = newSettings.CollisionAvoidanceOption;
        MaximumSpeed = newSettings.MaximumSpeed;
        IgnoreTrafficRules = newSettings.IgnoreTrafficRules;
    }
}