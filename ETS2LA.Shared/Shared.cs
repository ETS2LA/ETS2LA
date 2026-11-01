using System.Numerics;

using ETS2LA.Logging;
using static ETS2LA.Translations.T;

namespace ETS2LA.Shared;

/// <summary>
///  The EventBus is used by plugins to communicate with each other.
/// </summary>
/// <remarks>
///  Plugins can subscribe to topics and publish data to topics. When data is published to a topic,
///  all subscribers to that topic will receive the data.
/// </remarks>
public interface IEventBus
{
    /// <summary>
    ///  Subscribe to an event topic with a handler. Please call this function in `OnEnable` and do not call it in `Init`. <br/>
    ///  Remember to call `Unsubscribe` in `OnDisable` and `Shutdown` as well!
    /// </summary>
    /// <typeparam name="T">The type of the expected response.</typeparam>
    /// <param name="topic">The topic string, usually PluginName.SomeTopic</param>
    /// <param name="handler">The handler to call when this event fires.</param>
    void Subscribe<T>(string topic, Action<T> handler);
    /// <summary>
    ///  Unsubscribe from an event topic.
    /// </summary>
    /// <typeparam name="T">The type of the expected response.</typeparam>
    /// <param name="topic">The topic string, usually PluginName.SomeTopic</param>
    /// <param name="handler">The handler to unsubscribe from this topic.</param>
    void Unsubscribe<T>(string topic, Action<T> handler);
    /// <summary>
    ///  Publish an event to a topic. All subscribers to this topic will receive the data.
    /// </summary>
    /// <typeparam name="T">The type of the data being published.</typeparam>
    /// <param name="topic">The topic string, usually PluginName.SomeTopic</param>
    /// <param name="data">The data to publish.</param>
    void Publish<T>(string topic, T data);
}

/// <summary>
///  The base interface for all ETS2LA plugins.
/// </summary>
public interface IPlugin
{
    /// <summary>
    ///  This plugin's information such as it's name, description, author, etc.
    /// </summary>
    PluginInformation Info { get; }
    /// <summary>
    ///  Whether this plugin is currently running. Set by the backend, do not edit.
    /// </summary>
    bool _IsRunning { get; set; }
    /// <summary>
    ///  This function is ran once when the plugin is loaded, use it to setup any static data. <br/>
    ///  **NOTE**: Do not access the EventBus here, use `OnEnable` for that!
    /// </summary>
    void Init();
    /// <summary>
    ///  This function is ran when the plugin is enabled. Use it to subscribe to events. <br/>
    ///  Remember to unsubscribe in `OnDisable` and `Shutdown` as well!
    /// </summary>
    void OnEnable();
    /// <summary>
    ///  The main tick function. Equivalent to `Update()` in Unity and `Tick()` in Unreal Engine. <br/>
    ///  This function is ran at the rate defined by `TickRate`, as long as the CPU allows.
    /// </summary>
    void Tick();
    /// <summary>
    ///  This function is ran when the plugin is disabled. Use it to unsubscribe from events
    ///  and stop any running tasks. 
    /// </summary>
    void OnDisable();
    /// <summary>
    ///  This function is ran when the plugin is unloaded, usually at app shutdown. 
    ///  Use it to cleanup any resources.
    /// </summary>
    void Shutdown();
}

/// <summary>
///  The base class for all ETS2LA plugins.
/// </summary>
public abstract class Plugin : IPlugin
{
    public abstract PluginInformation Info { get; }
    public bool _IsRunning { get; set; } = false;
    /// <summary>
    ///  The tick rate of this plugin in ticks per second. Default is 20.0f TPS. <br/>
    ///  Please do not use a tickrate higher than you need, that will just wait CPU time.
    /// </summary>
    public virtual float TickRate => 20.0f;

    public virtual void Init() { }

    public virtual void OnEnable()
    {
        _IsRunning = true;
        Task.Run(RunningThread);
    }

    /// <summary>
    ///  The main running thread of this plugin. Handles ticking at the defined TickRate.
    /// </summary>
    protected void RunningThread()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double interval = 1000.0 / TickRate; // ms per tick
        double next = sw.Elapsed.TotalMilliseconds;

        while (_IsRunning)
        {
            // Update interval in case TickRate changed
            interval = 1000.0 / TickRate;

            next += interval;
            try { Tick(); }
            catch (Exception ex)
            {
                Logger.Error(_("Error in plugin {0} Tick: {1}", Info.Name, ex.Message));
            }

            if (next < sw.Elapsed.TotalMilliseconds)
                next = sw.Elapsed.TotalMilliseconds;
            
            double remaining = next - sw.Elapsed.TotalMilliseconds;

            // Use Thread.Sleep for the bigger part of the sleep.
            // This is not accurate, but saves CPU.
            if (remaining > 1.0)
                System.Threading.Thread.Sleep((int)(remaining - 1));

            // Busy-wait the last bit for better accuracy. This uses some CPU,
            // but ensures we get a stable tickrate.
            while (_IsRunning && sw.Elapsed.TotalMilliseconds < next)
                System.Threading.Thread.SpinWait(10);
        }
    }

    public virtual void Tick() { }

    public virtual void OnDisable()
    {
        _IsRunning = false;
    }
    public virtual void Shutdown()
    {
        _IsRunning = false;
    }
}

/// <summary>
///  The base interface for all ETS2LA library plugins.
/// </summary>
public interface ILibraryPlugin
{
    PluginInformation Info { get; }
}

/// <summary>
///  The base class for all ETS2LA library plugins. This is just a convenience class that implements the ILibraryPlugin interface.
/// </summary>
public abstract class LibraryPlugin : ILibraryPlugin
{
    public abstract PluginInformation Info { get; }
}


/// <summary>
///  Represents the supported version range of ETS2LA for a plugin.
/// </summary>
public class PluginVersionSupport
{
    public string? UpperBound { get; set; } = string.Empty;
    public string? LowerBound { get; set; } = string.Empty;
}

/// <summary>
///  Represents a dependency on another plugin. This is used to ensure that the 
///  required plugins are loaded and enabled before enabling this plugin.
/// </summary>
[Serializable]
public class PluginDependency
{
    public string Id { get; set; } = string.Empty;

    // TODO: Use PluginVersionSupport on the service so we can replace this
    public string? Version { get; set; } = string.Empty;
}

/// <summary>
///  Represents the basic information about a plugin.
/// </summary>
public class PluginInformation
{
    // Required
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }

    public required string Version { get; set; }
    
    /// <summary>
    ///  This string supports simple wildcard patterns to indicate which version of ETS2LA you support.
    ///  For example <br/>
    ///  "*" -> any <br/>
    ///  "1.*" -> any 1.x version <br/>
    ///  "<=1.2.3" -> any version up to and including 1.2.3 <br/>
    /// </summary>
    [Obsolete("Use AppVersionSupport instead. This property will be removed in a future version.")]
    public string SupportedETS2LA { get; set; } = "*";

    /// <summary>
    ///  This property allows you to specify the supported version range of ETS2LA for your plugin.
    ///  Usually you set a *lower bound* here, and only update the *upper bound* on the website when you
    ///  release a new version of your plugin.
    /// </summary>
    public PluginVersionSupport AppVersionSupport { get; set; } = new PluginVersionSupport();
    
    /// <summary>
    ///  This is a URL to an icon. It's displayed as square thumbnails.
    /// </summary>
    public string Icon { get; set; } = "";
    
    /// <summary>
    ///  Select the plugins this one depends on. The backend will ensure these plugins are loaded and
    ///  enabled before enabling this plugin. <br/>If any of the required plugins are not found, this plugin will
    ///  not be loaded at all. Use another plugin's Id to reference it.
    /// </summary>
    [Obsolete("Use DependsOn instead. This property will be removed in a future version.")]
    public List<string> Dependencies { get; set; } = new List<string>();

    /// <summary>
    ///  Select the plugins this one depends on. The backend will ensure these plugins are loaded and
    ///  enabled before enabling this plugin. <br/>If any of the required plugins are not found,
    ///  this plugin will not be loaded at all.
    /// </summary>
    public List<PluginDependency> DependsOn { get; set; } = new List<PluginDependency>();

    /// <summary>
    ///  Use commas to separate multiple authors.
    /// </summary>
    public string AuthorName { get; set; } = "";

    /// <summary>
    ///  Use commas to separate multiple author websites.
    /// </summary>
    public string AuthorWebsite { get; set; } = "";

    /// <summary>
    ///  Use commas to separate multiple author icons.
    /// </summary>
    public string AuthorIcon { get; set; } = "";

    /// <summary>
    ///  Used to change the "Adjustments" button name in the plugin manager. Note that you will have to handle
    ///  translations on your own if you change this. It is recommended to keep the default value in most situations.
    /// </summary>
    public string PluginPageButtonName { get; set; } = "Adjustments";

    /// <summary>
    ///  If true, the plugin manager will hide the enable/disable button for this plugin. This might be useful for plugins that
    ///  are purely UI or event based. Note that your plugin will always show as enabled if this is set to true.
    /// </summary>
    public bool HideEnableDisableButton { get; set; } = false;

    /// <summary>
    ///  This is a list of tags that can be used to categorize plugins. For example, "UI", "Gameplay", "Telemetry", etc.
    /// </summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
}

// Utility Classes
public class Vector3Double
{
    public double X;
    public double Y;
    public double Z;

    public Vector3Double(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public override string ToString()
    {
        return $"({X}, {Y}, {Z})";
    }

    public static Vector3Double Zero => new Vector3Double(0, 0, 0);
    public static Vector3Double One => new Vector3Double(1, 1, 1);

    public double[] ToArray()
    {
        return new double[] { X, Y, Z };
    }

    public Vector3 ToVector3()
    {
        return new Vector3((float)X, (float)Y, (float)Z);
    }

    public static Vector3Double operator -(Vector3Double a, Vector3Double b)
    {
        return new Vector3Double(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    public static Vector3Double operator +(Vector3Double a, Vector3Double b)
    {
        return new Vector3Double(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    public static Vector3Double operator *(Vector3Double a, double b)
    {
        return new Vector3Double(a.X * b, a.Y * b, a.Z * b);
    }

    public static Vector3Double operator /(Vector3Double a, double b)
    {
        return new Vector3Double(a.X / b, a.Y / b, a.Z / b);
    }
}

// TODO: Create ETS2LA.Calculation for filters like these.
public class KalmanFilter
{
    private float _q;
    private float _r;
    private float _p;
    private float _x;
    private bool _isInitialized;

    /// <summary>
    ///  A (1D) Kalman Filter for smoothing values with lots of noise. In ETS2LA that is usually location data, speed data,
    ///  or other data that relates to dynamically changing values.
    /// </summary>
    /// <param name="q">Process noise. Low values mean stability. High values mean quick changes.</param>
    /// <param name="r">Measurement noise. High values indicate a noisy sensor. The filter will use it's internal model more.</param>
    /// <param name="p">Initial estimation error. Usually no need to change.</param>
    public KalmanFilter(float q = 0.01f, float r = 0.1f, float p = 1.0f)
    {
        _q = q;
        _r = r;
        _p = p;
        _isInitialized = false;
    }

    public float Update(float measurement)
    {
        // snap directly to the initial value so we 
        // don't slowly drift from 0
        if (!_isInitialized)
        {
            _x = measurement;
            _isInitialized = true;
            return _x;
        }

        _p = _p + _q;
        float k = _p / (_p + _r);
        _x = _x + k * (measurement - _x);
        _p = (1.0f - k) * _p;
        return _x;
    }

    public void Reset()
    {
        _isInitialized = false;
    }
}

// These are naive implementations for 2D and 3D filters. There might be some better
// implementations, but I'm not good enough at math to figure out which are / aren't good.
// This seems to work well enough...

public class KalmanFilter2D
{
    private KalmanFilter _filterX;
    private KalmanFilter _filterY;

    public KalmanFilter2D(float q = 0.01f, float r = 0.1f, float p = 1.0f)
    {
        _filterX = new KalmanFilter(q, r, p);
        _filterY = new KalmanFilter(q, r, p);
    }

    public Vector2 Update(Vector2 measurement)
    {
        float x = _filterX.Update(measurement.X);
        float y = _filterY.Update(measurement.Y);
        return new Vector2(x, y);
    }

    public void Reset()
    {
        _filterX.Reset();
        _filterY.Reset();
    }
}

public class KalmanFilter3D
{
    private KalmanFilter _filterX;
    private KalmanFilter _filterY;
    private KalmanFilter _filterZ;

    public KalmanFilter3D(float q = 0.01f, float r = 0.1f, float p = 1.0f)
    {
        _filterX = new KalmanFilter(q, r, p);
        _filterY = new KalmanFilter(q, r, p);
        _filterZ = new KalmanFilter(q, r, p);
    }

    public Vector3 Update(Vector3 measurement)
    {
        float x = _filterX.Update(measurement.X);
        float y = _filterY.Update(measurement.Y);
        float z = _filterZ.Update(measurement.Z);
        return new Vector3(x, y, z);
    }

    public void Reset()
    {
        _filterX.Reset();
        _filterY.Reset();
        _filterZ.Reset();
    }
}

public readonly struct Optional<T>
{
    public T Value { get; }
    public bool HasValue { get; }

    public Optional(T value)
    {
        Value = value;
        HasValue = true;
    }

    public static Optional<T> Empty => default;

    public static implicit operator Optional<T>(T value) => new(value);

    public T GetValueOrDefault(T defaultValue = default!) => HasValue ? Value : defaultValue;

    public override string ToString() => HasValue ? Value?.ToString() ?? "null" : "<Empty>";
}