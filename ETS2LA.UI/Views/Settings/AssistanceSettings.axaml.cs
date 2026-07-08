using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ETS2LA.Settings.Global;
using ETS2LA.Overlay;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using ETS2LA.State;
using Avalonia.Automation;
using Avalonia.Automation.Peers;

namespace ETS2LA.UI.Views.Settings;

public partial class AssistanceSettingsPage : UserControl, INotifyPropertyChanged
{
    public ObservableCollection<TabStripItemHandler> AccelerationOptions { get; } = new();
    public ObservableCollection<TabStripItemHandler> SteeringSensitivityOptions { get; } = new();
    public ObservableCollection<TabStripItemHandler> FollowingDistanceOptions { get; } = new();
    public ObservableCollection<TabStripItemHandler> SetSpeedBehaviourOptions { get; } = new();
    public ObservableCollection<TabStripItemHandler> SpeedLimitWarningOptions { get; } = new();
    public ObservableCollection<TabStripItemHandler> CollisionAvoidanceOptions { get; } = new();

    public bool SeparateCruiseAndSteering
    {
        get => AssistanceSettings.Current.SeparateCruiseAndSteering;
        set
        {
            if (AssistanceSettings.Current.SeparateCruiseAndSteering != value)
            {
                AssistanceSettings.Current.SeparateCruiseAndSteering = value;
                AssistanceSettings.Current.Save();
            }
        }
    }

    public bool PauseWhenUnstable
    {
        get => AssistanceSettings.Current.PauseWhenUnstable;
        set
        {
            if (AssistanceSettings.Current.PauseWhenUnstable != value)
            {
                AssistanceSettings.Current.PauseWhenUnstable = value;
                AssistanceSettings.Current.Save();
            }
        }
    }

    public string SpeedControlDisplay => $"{StateSettingsHandler.Current.GetSettings().SpeedControlStepSize:F0} {UnitConversions.GetUnitAbbreviation(
                                            UnitType.Speed, 
                                            StateSettingsHandler.Current.GetSettings().DisplayUnits
                                        )}";

    public string SpeedControlAutomationName => $"Speed control step size, slider, {SpeedControlStepSize}";

    public int SpeedControlStepSize
    {
        get => (int)StateSettingsHandler.Current.GetSettings().SpeedControlStepSize;
        set
        {
            if (StateSettingsHandler.Current.GetSettings().SpeedControlStepSize != (int)value)
            {
                StateSettingsHandler.Current.GetSettings().SpeedControlStepSize = (int)value;
                StateSettingsHandler.Current.Save();
            }
            OnPropertyChanged(nameof(SpeedControlStepSize));
            OnPropertyChanged(nameof(SpeedControlDisplay));
            OnPropertyChanged(nameof(SpeedControlAutomationName));
        }
    }

    public string SnapTo10UnitsDisplay => $"Snap to 10 {UnitConversions.GetUnitAbbreviation(
                                                UnitType.Speed, 
                                                StateSettingsHandler.Current.GetSettings().DisplayUnits
                                            )}";

    public string SnapTo10UnitsAutomationName => $"Snap ACC to 10 units, toggle, {(SnapTo10Units ? "enabled" : "disabled")}";

    public bool SnapTo10Units
    {
        get => StateSettingsHandler.Current.GetSettings().SnapTo10s;
        set
        {
            if (StateSettingsHandler.Current.GetSettings().SnapTo10s != value)
            {
                StateSettingsHandler.Current.GetSettings().SnapTo10s = value;
                StateSettingsHandler.Current.Save();
            }
            OnPropertyChanged(nameof(SnapTo10Units));
            OnPropertyChanged(nameof(SnapTo10UnitsDisplay));
            OnPropertyChanged(nameof(SnapTo10UnitsAutomationName));
        }
    }

    public bool IgnoreTrafficRules
    {
        get => AssistanceSettings.Current.IgnoreTrafficRules;
        set
        {
            if (AssistanceSettings.Current.IgnoreTrafficRules != value)
            {
                AssistanceSettings.Current.IgnoreTrafficRules = value;
                AssistanceSettings.Current.Save();
            }
            OnPropertyChanged(nameof(IgnoreTrafficRules));
        }
    }

    public string MaximumSpeedDisplay => AssistanceSettings.Current.MaximumSpeed > 0 ? $"{AssistanceSettings.Current.MaximumSpeed:F0} {UnitConversions.GetUnitAbbreviation(UnitType.Speed, StateSettingsHandler.Current.GetSettings().DisplayUnits)}" : "No Limit";
    public float MaximumSpeed
    {
        get => AssistanceSettings.Current.MaximumSpeed;
        set
        {
            if (AssistanceSettings.Current.MaximumSpeed != value)
            {
                AssistanceSettings.Current.MaximumSpeed = value;
                AssistanceSettings.Current.Save();
            }
            OnPropertyChanged(nameof(MaximumSpeed));
            OnPropertyChanged(nameof(MaximumSpeedDisplay));
        }
    }

    public int SelectedAccelerationOption { get; set; }
    public int SelectedSteeringSensitivityOption { get; set; }
    public int SelectedFollowingDistanceOption { get; set; }
    public int SelectedSetSpeedBehaviourOption { get; set; }
    public int SelectedSpeedLimitWarningOption { get; set; }
    public int SelectedCollisionAvoidanceOption { get; set; }

    public AssistanceSettingsPage()
    {
        LoadAccelerationOptions();
        LoadSteeringSensitivityOptions();
        LoadFollowingDistanceOptions();
        LoadSetSpeedBehaviourOptions();
        LoadSpeedLimitWarningOptions();
        LoadCollisionAvoidanceOptions();
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
    }

    private void LoadAccelerationOptions()
    {
        SelectedAccelerationOption = (int)AssistanceSettings.Current.AccelerationResponse;
        foreach (AccelerationResponseOption option in Enum.GetValues(typeof(AccelerationResponseOption)))
        {
            AccelerationOptions.Add(new TabStripItemHandler(option.ToString()));
        }
    }

    private void LoadSteeringSensitivityOptions()
    {
        SelectedSteeringSensitivityOption = (int)AssistanceSettings.Current.SteeringSensitivity;
        foreach (SteeringSensitivityOption option in Enum.GetValues(typeof(SteeringSensitivityOption)))
        {
            SteeringSensitivityOptions.Add(new TabStripItemHandler(option.ToString()));
        }
    }

    private void LoadFollowingDistanceOptions()
    {
        SelectedFollowingDistanceOption = (int)AssistanceSettings.Current.FollowingDistance;
        foreach (FollowingDistanceOption option in Enum.GetValues(typeof(FollowingDistanceOption)))
        {
            FollowingDistanceOptions.Add(new TabStripItemHandler(option.ToString()));
        }
    }

    private void LoadSetSpeedBehaviourOptions()
    {
        SelectedSetSpeedBehaviourOption = (int)AssistanceSettings.Current.SetSpeedBehaviourOption;
        foreach (SetSpeedBehaviour option in Enum.GetValues(typeof(SetSpeedBehaviour)))
        {
            SetSpeedBehaviourOptions.Add(new TabStripItemHandler(option.ToString()));
        }
    }

    private void LoadSpeedLimitWarningOptions()
    {
        SelectedSpeedLimitWarningOption = (int)AssistanceSettings.Current.SpeedLimitWarningOption;
        foreach (SpeedLimitWarning option in Enum.GetValues(typeof(SpeedLimitWarning)))
        {
            SpeedLimitWarningOptions.Add(new TabStripItemHandler(option.ToString()));
        }
    }

    private void LoadCollisionAvoidanceOptions()
    {
        SelectedCollisionAvoidanceOption = (int)AssistanceSettings.Current.CollisionAvoidanceOption;
        foreach (CollisionAvoidance option in Enum.GetValues(typeof(CollisionAvoidance)))
        {
            CollisionAvoidanceOptions.Add(new TabStripItemHandler(option.ToString()));
        }
    }

    private void OnAccelerationOptionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedAccelerationOption >= 0)
        {
            AssistanceSettings.Current.AccelerationResponse = (AccelerationResponseOption)SelectedAccelerationOption;
            AssistanceSettings.Current.Save();
        }
    }

    private void OnSteeringSensitivityChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedSteeringSensitivityOption >= 0)
        {
            AssistanceSettings.Current.SteeringSensitivity = (SteeringSensitivityOption)SelectedSteeringSensitivityOption;
            AssistanceSettings.Current.Save();
        }
    }

    private void OnFollowingDistanceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedFollowingDistanceOption >= 0)
        {
            AssistanceSettings.Current.FollowingDistance = (FollowingDistanceOption)SelectedFollowingDistanceOption;
            AssistanceSettings.Current.Save();
        }
    }

    private void OnSetSpeedBehaviourChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedSetSpeedBehaviourOption >= 0)
        {
            AssistanceSettings.Current.SetSpeedBehaviourOption = (SetSpeedBehaviour)SelectedSetSpeedBehaviourOption;
            AssistanceSettings.Current.Save();
        }
    }

    private void OnSpeedLimitWarningChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedSpeedLimitWarningOption >= 0)
        {
            AssistanceSettings.Current.SpeedLimitWarningOption = (SpeedLimitWarning)SelectedSpeedLimitWarningOption;
            AssistanceSettings.Current.Save();
        }
    }

    private void OnCollisionAvoidanceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedCollisionAvoidanceOption >= 0)
        {
            AssistanceSettings.Current.CollisionAvoidanceOption = (CollisionAvoidance)SelectedCollisionAvoidanceOption;
            AssistanceSettings.Current.Save();
        }
    }

    private void OnSeparateCruiseAndSteeringClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SeparateCruiseAndSteering = !SeparateCruiseAndSteering;
        OnPropertyChanged(nameof(SeparateCruiseAndSteering));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class TabStripItemHandler: INotifyPropertyChanged
{
    public string Item { get; }
    public string Header => GetFormattedName();
    public string AutomationName => GetAutomationText();
    public bool IsDisabled { get; set; } = false;

    public TabStripItemHandler(string option, bool? isDisabled = null)
    {
        Item = option;
        IsDisabled = isDisabled ?? false;
    }

    private string GetFormattedName()
    {
        // Add a space before each capital letter (except the first)
        // e.g., "AccelerationResponse" -> "Acceleration Response"
        var formatted = System.Text.RegularExpressions.Regex.Replace(Item, "(\\B[A-Z])", " $1");
        return formatted;
    }

    private string GetAutomationText()
    {
        string text = $"Tab strip item {Header}, button";
        return text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}