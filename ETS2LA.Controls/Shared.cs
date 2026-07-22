using ETS2LA.Settings;
using ETS2LA.Controls.Defaults;
using System.Runtime.Serialization;

namespace ETS2LA.Controls;

public interface IControlsBackend
{
    private static IControlsBackend? _instance;
    /// <summary>
    ///  Use .Current to access the registered controls backend instance.
    ///  This is shared between all plugins.
    /// </summary>
    public static IControlsBackend Current => _instance ?? throw new Exception("No controls backend registered! Make sure to register one in your plugin's OnLoad method.");

    /// <summary>
    ///  This event is fired when a control is added to the ControlsBackend.
    /// </summary>
    public event EventHandler<ControlAddedEventArgs> ControlAdded;
    /// <summary>
    ///  This event is fired when a control is removed from the ControlsBackend.
    /// </summary>
    public event EventHandler<ControlRemovedEventArgs> ControlRemoved;

    /// <summary>
    ///  Register a new control definition to the ControlsBackend. <br/>
    ///  After registering you can use ControlsBackend.On to listen for changes to this control.
    /// </summary>
    /// <param name="definition">The control definition to register.</param>
    public void RegisterControl(ControlDefinition definition);

    /// <summary>
    ///  Listen for changes to a registered control. Please note that the control must be <br/>
    ///  registered first using ControlsBackend.RegisterControl, each time the app is launched (use init()).
    /// </summary>
    /// <param name="controlId">The ID of the control to listen to (ControlDefinition.Id).</param>
    /// <param name="callback">The callback to invoke when the control's value changes.</param>
    public void On(string controlId, EventHandler<ControlChangeEventArgs> callback);

    /// <summary>
    ///  Stop listening for changes to a registered control.
    /// </summary>
    /// <param name="controlId">The ID of the control to stop listening to (ControlDefinition.Id).</param>
    /// <param name="callback">The callback to remove.</param>
    public void UnregisterListener(string controlId, EventHandler<ControlChangeEventArgs> callback);

    /// <summary>
    ///  Unregister a control from the ControlsBackend. This will also save its current state. <br/>
    ///  **NOTE**: This will remove the control from the settings!
    /// </summary>
    /// <param name="controlId">The ID of the control to unregister (ControlDefinition.Id).</param>
    public void UnregisterControl(string controlId);

    /// <summary>
    ///  Update the bindings for a registered control. This should only ever be called from <br/>
    ///  the settings menu, however plugins can technically use this as well.
    /// </summary>
    /// <param name="controlId">The ID of the control to update (ControlDefinition.Id).</param>
    /// <param name="deviceId">The ID of the device to bind the control to.</param>
    /// <param name="controlKey">The key or axis to bind the control to.</param>
    public void UpdateControlBindings(string controlId, string deviceId, string controlKey);

    /// <summary>
    ///  Update the axis behavior for a registered control.
    /// </summary>
    /// <param name="controlId">The ID of the control to update (ControlDefinition.Id).</param>
    /// <param name="axisType">The new axis behavior to set.</param>
    public void UpdateAxisBehavior(string controlId, AxisType behavior);
    
    /// <summary>
    ///  Get a list of all registered controls.
    /// </summary>
    /// <returns>A list of all registered control instances.</returns>
    public List<ControlInstance> GetRegisteredControls();

    /// <summary>
    ///  Return a device's information based on it's ID.
    /// </summary>
    /// <param name="deviceId">The device ID to lookup.</param>
    /// <returns>The device information, or null if not found.</returns>
    public InputDeviceInfo? GetInputDeviceInfoById(string deviceId);

    /// <summary>
    ///  Waits for an input from any connected device within the specified timeout.
    /// </summary>
    /// <param name="timeoutSeconds">How long to wait for input, in seconds.</param>
    /// <returns>
    ///  A tuple containing the device ID and control ID of the detected input.
    ///  If no input is detected within the timeout, returns ("", "").
    /// </returns>
    public (string, string) WaitForInput(float timeoutSeconds);
    
    /// <summary>
    ///  Shutdown the ControlsBackend, saving all control states to their files. <br/>
    ///  **NEVER** call this inside of plugins!
    /// </summary>
    public void Shutdown();
}

public enum DeviceType
{
    Keyboard,
    Gamepad,
    Wheel
}

public class InputDeviceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DeviceType Type { get; init; }
}

/// <summary>
///  Represents a control definition for plugins.
///  Register these in ControlsBackend.Current and use .On to listen for changes.
/// </summary>
public class ControlDefinition
{
    /// <summary>
    ///  This Control's unique identifier. Do not change after releasing
    ///  your plugin!
    /// </summary>
    public required string Id;
    /// <summary>
    ///  The display name of this control, will be shown to the user.
    /// </summary>
    public required string Name;
    /// <summary>
    ///  The description of this control, will be shown to the user.
    /// </summary>
    public required string Description;
    /// <summary>
    ///  A default keybind for this control. <br/>
    ///  **NOTE**: You can only default bind to the keyboard!
    /// </summary>
    public string DefaultKeybind = "";
    /// <summary>
    ///  Whether this control outputs a float value (e.g. for axes) 
    ///  or a boolean value (e.g. for buttons). <br/>
    ///  **NOTE**: Updates are only triggered on value change for booleans!
    /// </summary>
    public ControlType Type { get; set; } = ControlType.Boolean;
}

public enum ControlType
{
    /// <summary>
    ///  A floating point value, e.g. for axes.
    /// </summary>
    Float,
    /// <summary>
    ///  A boolean value, e.g. for buttons.
    /// </summary>
    Boolean
}

public enum AxisType
{
    Normal,   // 0.0 to 1.0 (e.g., Throttle)
    Centered, // -1.0 to 1.0 (e.g., Steering)
    Inverted, // 1.0 to 0.0 (e.g., some Pedals)
    SplitNeg, // 1.0 to 0.0 for the first half of a combined axis (usually Z for xbox-like controllers)
    SplitPos  // 0.0 to 1.0 for the second half of a combined axis (usually Z for xbox-like controllers)
}

/// <summary>
///  This event is fired when a control is added to the ControlsBackend.
/// </summary>
public class ControlAddedEventArgs : EventArgs
{
    public ControlInstance Control;
    public ControlAddedEventArgs(ControlInstance control)
    {
        Control = control;
    }
}

/// <summary>
///  This event is fired when a control's value changes. i.e. button pressed or axis moved.
/// </summary>
public class ControlChangeEventArgs : EventArgs
{
    public object NewValue;
    public ControlChangeEventArgs(object newValue)
    {
        NewValue = newValue;
    }
}

/// <summary>
///  This event is fired when a control is removed from the ControlsBackend.
/// </summary>
public class ControlRemovedEventArgs : EventArgs
{
    public ControlInstance Control;
    public ControlRemovedEventArgs(ControlInstance control)
    {
        Control = control;
    }
}

/// <summary>
///  Represents an internal instance of a control binding. Usually shouldn't be 
///  used directly, and instead through ControlsBackend's functions.
/// </summary>
[Serializable]
public class ControlInstance : ISerializable
{
    /// <summary>
    ///  The ID of the device this control is bound to.
    /// </summary>
    public string DeviceId = "";
    /// <summary>
    ///  The ID of the control on the device this is bound to. Button/key/axis where:
    ///  - Buttons start with "B" (e.g. "B0", "B1", ...)
    ///  - Keys are the key name (e.g. "Space", "A", "F1", ...) 
    ///    since they are only used when DeviceId is "keyboard"
    ///  - Axes don't start with anything (e.g. "X", "Y", "Z", "RotationX", "RotationY", ...)
    /// </summary>
    public object ControlId = "";
    /// <summary>
    ///  The definition this control instance is based on.
    /// </summary>
    public required ControlDefinition Definition;
    /// <summary>
    ///  The behavior of this axis, only relevant if bound to an axis.
    /// </summary>
    public AxisType AxisBehavior = AxisType.Normal;
    /// <summary>
    ///  Event fired when this control's value changes.
    /// </summary>
    private event EventHandler<ControlChangeEventArgs>? OnChange;
    private object? _lastValue;

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue(nameof(DeviceId), DeviceId);
        info.AddValue(nameof(ControlId), ControlId);
    }

    public bool IsBound()
    {
        return !string.IsNullOrEmpty(DeviceId) && !string.IsNullOrEmpty(ControlId.ToString());
    }

    public void SaveToFile(SettingsHandler settingsHandler, string rootPath)
    {
        var fileName = Path.Combine(rootPath, $"{Definition.Id}.json");
        settingsHandler.Save(fileName, this);
    }

    public void LoadFromFile(SettingsHandler settingsHandler, string rootPath)
    {
        var fileName = Path.Combine(rootPath, $"{Definition.Id}.json");
        var loaded = settingsHandler.Load<ControlInstance>(fileName);
        DeviceId = loaded.DeviceId;
        ControlId = loaded.ControlId;
        AxisBehavior = loaded.AxisBehavior;
    }

    public void RegisterCallback(EventHandler<ControlChangeEventArgs> callback)
    {
        if (OnChange != null && OnChange.GetInvocationList().Contains(callback))
            return;
            
        OnChange += callback;
    }

    public void UnregisterCallback(EventHandler<ControlChangeEventArgs> callback)
    {
        if (OnChange != null && OnChange.GetInvocationList().Contains(callback))
            OnChange -= callback;
    }

    public void UpdateState(object newValue)
    {
        if (Definition.Type == ControlType.Boolean)
        {
            bool boolValue;
            if (newValue.GetType() == typeof(float))
                boolValue = (float)newValue != 0.0f;
            else
                boolValue = (bool)newValue;

            if (_lastValue != null && _lastValue.Equals(boolValue))
                return;

            OnChange?.Invoke(this, new ControlChangeEventArgs(boolValue));
            _lastValue = boolValue;
        }
        else
        {
            float floatValue;
            if (newValue.GetType() == typeof(bool))
                floatValue = (bool)newValue ? 1.0f : 0.0f;
            else
                floatValue = (float)newValue;

            if (_lastValue != null && _lastValue.Equals(floatValue))
                return;

            OnChange?.Invoke(this, new ControlChangeEventArgs(floatValue));
            _lastValue = floatValue;
        }
    }
}
