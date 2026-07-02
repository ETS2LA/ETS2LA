using ETS2LA.Controls;
using ETS2LA.Controls.Defaults;
using ETS2LA.Backend.Events;
using ETS2LA.Game.Telemetry;
using ETS2LA.Settings.Global;
using ETS2LA.Game;
using ETS2LA.Logging;
using ETS2LA.Notifications;

namespace ETS2LA.State;

public enum SteeringAssists
{
    None,
    LaneKeep,
    Full
}

public enum LongitudinalAssists
{
    None,
    EmergencyBraking,
    AdaptiveCruiseControl
}

/// <summary>
///  This state contains the most important ETS2LA variables. Most plugins
///  will use it to follow the user's preferences and read the game data.
/// </summary>
public class ApplicationState
{
    private static readonly Lazy<ApplicationState> _instance = new(() => new ApplicationState());
    public static ApplicationState Current => _instance.Value;
    private volatile bool shutdown = false;

    public ApplicationState()
    {

        Events.Current.Subscribe<GameTelemetryData>(GameTelemetry.Current.EventString, HandleTelemetryUpdate);
        Events.Current.Subscribe<float>("TelemetryEvents.SpeedLimitChanged", HandleSpeedLimitChanged);

        ControlsBackend.Current.On(DefaultControls.SET.Id, HandleSet);
        ControlsBackend.Current.On(DefaultControls.Increase.Id, HandleIncrease);
        ControlsBackend.Current.On(DefaultControls.Decrease.Id, HandleDecrease);
        ControlsBackend.Current.On(DefaultControls.Assist.Id, HandleAssist);

        assistanceSettings = AssistanceSettings.Current;

        StateSettingsHandler.Current.OnSettingsChanged += HandleSettingsChanged;
        HandleSettingsChanged(StateSettingsHandler.Current.GetSettings());
    }

    private void HandleSettingsChanged(StateSettings newStateSettings)
    {
        stateSettings = newStateSettings;
        DisplayUnits = newStateSettings.DisplayUnits;
    }

    private void HandleTelemetryUpdate(GameTelemetryData data)
    {
        latestTelemetryData = data;
        if (data.sdkActive)
        {
            IsGameRunning = true;
            RunningGameType = data.scsValues.game == "ETS2" ? GameType.EuroTruckSimulator2
                                                            : GameType.AmericanTruckSimulator;
            RunningGameVersion = data.scsValues.versionMajor.ToString() + "."
                               + data.scsValues.versionMinor.ToString();

            if (RunningGame != null && RunningGame.Type != RunningGameType)
            {
                Logger.Info($"Detected a switch to {RunningGameType}, waiting for its game data to be parsed.");
                Installation oldGame = RunningGame;
                RunningGame = null;
                // Unload in the background so the forced GC doesn't
                // stall the telemetry thread.
                Task.Run(oldGame.ClearParsedData);
            }

            if (RunningGame == null && (parsingTask == null || parsingTask.IsCompleted))
                // This function will run until game data is successfully parsed.
                parsingTask = WaitForParseSuccessful();
        }
        else
        {
            IsGameRunning = false;
        }
    }

    public void Shutdown()
    {
        shutdown = true;
    }
    


    // MARK: Self-Driving Related
    // NOTE: This class is organized by *category* and not variable/function type.
    //       This makes the most sense to avoid having lots of variables back to back
    //       far from the relevant functions. It is slightly unconventional though.
    //       Follow the marks :+1:



    /// <summary>
    ///  Defines the level of steering assistance the user wants.
    ///  Full is assumed to be everything plugins can provide, however plugins that can only
    ///  provide Lane Keeping, should be disabled when the user selects a higher
    ///  level.
    /// </summary>
    public SteeringAssists DesiredSteeringLevel { get; set; } = SteeringAssists.Full;
    /// <summary>
    ///  This value will be set to true if the user has temporarily paused the steering assist,
    ///  e.g. by braking. Once the user resumes assists this value will be set to false again.
    /// </summary>
    public bool PauseSteeringAssist { get; set; } = true;

    /// <summary>
    ///  Defines the level of longitudinal assistance the user wants. It is assumed that lower levels
    ///  are included in higher levels, e.g. Emergency Braking should still be active even if the user
    ///  desires Adaptive Cruise Control.
    /// </summary>
    public LongitudinalAssists DesiredLongitudinalLevel { get; set; } = LongitudinalAssists.AdaptiveCruiseControl;
    /// <summary>
    ///  This value will be set to true if the user has temporarily paused the longitudinal assist,
    ///  e.g. by braking. Once the user resumes assists this value will be set to false again.
    /// </summary>
    public bool PauseLongitudinalAssist { get; set; } = true;
    /// <summary>
    ///  This value will be used by the longitudinal assist to determine the target speed. This value does
    ///  not take into account any environmental factors. That will either be provided by plugins, or the
    ///  user will have to take care of it themselves. <br/><br/>
    ///  **This value is in scientific units, that is m/s.**
    /// </summary>
    public float DesiredSpeed { get; set; } = 0.0f;

    /// <summary>
    ///  This value will determine the currently used units for any values shown in the UI. This value
    ///  is automatically changed by ETS2LA, either when the user sets it in the settings, or when we
    ///  detect a change in the game's units. This unit should determine the units used everywhere, e.g.
    ///  the units used when increasing and decreasing the target speed. (+-1 mph/kph/ms) <br/><br/>
    ///  **Use UnitConversions.FromScientificUnits and UnitConversions.ToScientificUnits to convert values to and from the current display units.**
    /// </summary>
    public Units DisplayUnits { get; set; } = Units.Metric;

    // Internal value to keep track of the latest telemetry we received.
    private GameTelemetryData latestTelemetryData = new();
    private AssistanceSettings assistanceSettings;
    private StateSettings stateSettings;
    private float lastSpeedLimit = 0f;

    // The functions below are for handling control events.
    // If determining what they do is hard via code, then take a look at the 
    // example at https://docs.ets2la.com/docs/Rewrite/UserInput#how-to-listen-to-registered-controls

    private void RoundToNearestUnit()
    {
        switch (DisplayUnits)
        {
            case Units.Metric:
                DesiredSpeed = (float)(Math.Round(DesiredSpeed * 3.6) / 3.6); // Round to nearest km/h
                break;
            case Units.Imperial:
                DesiredSpeed = (float)(Math.Round(DesiredSpeed * 2.237) / 2.237); // Round to nearest mph
                break;
            case Units.Scientific:
                DesiredSpeed = (float)Math.Round(DesiredSpeed); // Round to nearest m/s
                break;
        }

        if (DesiredSpeed < 0)
            DesiredSpeed = 0;
    }
    
    private float SnapTo10s(float increase)
    {
        if (!stateSettings.SnapTo10s)
            return DesiredSpeed + increase;

        float currentSpeedInDisplayUnits = UnitConversions.FromScientificUnits(UnitType.Speed, DesiredSpeed, DisplayUnits);
        float newSpeedInDisplayUnits = currentSpeedInDisplayUnits + UnitConversions.FromScientificUnits(UnitType.Speed, increase, DisplayUnits);
        // When increasing by 2 from 37 we go:
        // 37 -> 39 -> 40 -> 42 -> 44
        float currentSpeed10s = (float)(Math.Floor((currentSpeedInDisplayUnits + 0.1f) / 10) * 10);
        float newSpeed10s = (float)(Math.Floor((newSpeedInDisplayUnits + 0.1f) / 10) * 10);
        
        if (currentSpeed10s != newSpeed10s && newSpeed10s > currentSpeed10s)
        {
            return UnitConversions.ToScientificUnits(UnitType.Speed, newSpeed10s, DisplayUnits);
        }
        else
        {
            return DesiredSpeed + increase;
        }
    }

    private void HandleSpeedLimitChanged(float newSpeedLimit)
    {
        if (DesiredSpeed == 0)
            return;

        float offset = 0;
        if (lastSpeedLimit != 0)
            offset = DesiredSpeed - lastSpeedLimit; 

        if (newSpeedLimit == 0)
            newSpeedLimit = UnitConversions.ToScientificUnits(UnitType.Speed, 30, Units.Metric);

        lastSpeedLimit = newSpeedLimit;
        DesiredSpeed = newSpeedLimit + offset;
        RoundToNearestUnit();
        NotificationHandler.Current.SendNotification(new Notification
        {
            Id = "ApplicationState.SpeedLimitChanged",
            Title = "Speed limit changed",
            Content = $"New limit {UnitConversions.FromScientificUnits(UnitType.Speed, newSpeedLimit, DisplayUnits):0} {UnitConversions.GetUnitAbbreviation(UnitType.Speed, DisplayUnits)}"
        });
    }

    private void HandleSet(object sender, ControlChangeEventArgs e)
    {
        bool b = (bool)e.NewValue;
        if(b == true) return; // key down event

        if (PauseLongitudinalAssist)
        {
            PauseLongitudinalAssist = false;
            PauseSteeringAssist = false;
            if (assistanceSettings.SetSpeedBehaviourOption == SetSpeedBehaviour.CurrentSpeed)
                DesiredSpeed = latestTelemetryData.truckFloat.speed;
            else if (assistanceSettings.SetSpeedBehaviourOption == SetSpeedBehaviour.SpeedLimit)
                DesiredSpeed = latestTelemetryData.truckFloat.speedLimit != 0 ?
                               latestTelemetryData.truckFloat.speedLimit :
                               UnitConversions.ToScientificUnits(UnitType.Speed, 30, Units.Metric);

            Events.Current.Publish<EventArgs>("ETS2LA.State.AssistsUnpaused", new EventArgs());
            Events.Current.Publish<bool>("ETS2LA.State.SteeringPaused", PauseSteeringAssist);
            Events.Current.Publish<bool>("ETS2LA.State.LongitudinalPaused", PauseLongitudinalAssist);
            RoundToNearestUnit();
        }
        else
        {
            PauseLongitudinalAssist = true;
            PauseSteeringAssist = true;

            Events.Current.Publish<EventArgs>("ETS2LA.State.AssistsPaused", new EventArgs());
            Events.Current.Publish<bool>("ETS2LA.State.SteeringPaused", PauseSteeringAssist);
            Events.Current.Publish<bool>("ETS2LA.State.LongitudinalPaused", PauseLongitudinalAssist);
        }
    }

    private void HandleIncrease(object sender, ControlChangeEventArgs e)
    {
        bool b = (bool)e.NewValue;
        if(b == true) return; // key down event

        // Resume after pause
        if (PauseLongitudinalAssist)
        {
            PauseLongitudinalAssist = false;
            // Reset speed if it's too low compared to current speed
            // to avoid an "AEB" like event.
            if (latestTelemetryData.truckFloat.speed > DesiredSpeed + 5 / 3.6f)
            {
                DesiredSpeed = latestTelemetryData.truckFloat.speed;
            }
            return;
        }

        // We're driving at 40kph with no limit (Desired = 0)
        // -> Press Increase
        // -> AEB due to speed now being set to 1kph
        // -> WTF
        if (Math.Abs(DesiredSpeed) < 0.01f)
        {
            DesiredSpeed = latestTelemetryData.truckFloat.speed;
            return;
        }

        switch (DisplayUnits)
        {
            case Units.Metric:
                float increaseMetric = UnitConversions.ToScientificUnits(UnitType.Speed, stateSettings.SpeedControlStepSize, Units.Metric);
                DesiredSpeed = SnapTo10s(increaseMetric);
                break;
            case Units.Imperial:
                float increaseImperial = UnitConversions.ToScientificUnits(UnitType.Speed, stateSettings.SpeedControlStepSize, Units.Imperial);
                DesiredSpeed = SnapTo10s(increaseImperial);
                break;
            case Units.Scientific:
                DesiredSpeed += stateSettings.SpeedControlStepSize;
                break;
        }

        RoundToNearestUnit();
    }

    private void HandleDecrease(object sender, ControlChangeEventArgs e)
    {
        bool b = (bool)e.NewValue;
        if(b == true) return; // key down event

        if (PauseLongitudinalAssist)
        {
            PauseLongitudinalAssist = false;
            // Reset speed if it's too low compared to current speed
            // to avoid an "AEB" like event.
            if (latestTelemetryData.truckFloat.speed > DesiredSpeed + 5 / 3.6f)
            {
                DesiredSpeed = latestTelemetryData.truckFloat.speed;
            }
            return;
        }

        if (Math.Abs(DesiredSpeed) < 0.01f)
        {
            DesiredSpeed = latestTelemetryData.truckFloat.speed;
            return;
        }

        switch (DisplayUnits)
        {
            case Units.Metric:
                float decreaseMetric = UnitConversions.ToScientificUnits(UnitType.Speed, stateSettings.SpeedControlStepSize, Units.Metric);
                DesiredSpeed = SnapTo10s(-decreaseMetric);
                break;
            case Units.Imperial:
                float decreaseImperial = UnitConversions.ToScientificUnits(UnitType.Speed, stateSettings.SpeedControlStepSize, Units.Imperial);
                DesiredSpeed = SnapTo10s(-decreaseImperial);
                break;
            case Units.Scientific:
                DesiredSpeed -= stateSettings.SpeedControlStepSize;
                break;
        }

        RoundToNearestUnit();
    }

    private void HandleAssist(object sender, ControlChangeEventArgs e)
    {
        bool b = (bool)e.NewValue;
        if(b == true) return; // key down event

        if (PauseSteeringAssist)
        {
            PauseSteeringAssist = false;
            Events.Current.Publish<bool>("ETS2LA.State.SteeringPaused", PauseSteeringAssist);
        }
        else
        {
            DesiredSteeringLevel++;
            if (DesiredSteeringLevel > SteeringAssists.Full)
            {
                DesiredSteeringLevel = SteeringAssists.None;
            }

            Events.Current.Publish<SteeringAssists>("ETS2LA.State.SteeringLevel", DesiredSteeringLevel);
            Events.Current.Publish<EventArgs>($"ETS2LA.State.SteeringLevel.{DesiredSteeringLevel}", new EventArgs());
        }
    }



    // MARK: Map Data Related



    public bool IsGameRunning { get; set; } = false;
    public GameType? RunningGameType { get; set; }
    public string? RunningGameVersion { get; set; }
    private Task? parsingTask;
    public Installation? RunningGame { get; set; }

    private async Task WaitForParseSuccessful()
    {
        while (!shutdown)
        {
            foreach(Installation install in GameHandler.Current.Installations)
            {
                if (RunningGame != null)
                    break;

                if(install.Type == RunningGameType)
                {
                    bool success = await Task.Run(() => install.Parse());
                    // The user might have switched games while we were parsing,
                    // in that case throw the now stale data away.
                    if (success && install.Type == RunningGameType)
                        RunningGame = install;
                    else if (success)
                        install.ClearParsedData();
                }
            }

            if (RunningGame != null && RunningGame.Type == RunningGameType)
                break;

            await Task.Delay(5000);
        }
    }
}
