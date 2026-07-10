using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using ETS2LA.Logging;
using ETS2LA.Settings.Global;

namespace ETS2LA.Telemetry;

public static class OTelAttributes
{
    public static Dictionary<string, object> GetAttributes()
    {
        return new Dictionary<string, object>
        {}.Concat(HardwareInfo.GetSystemSpecs())
          .Concat(AnonymousUser.GetUserProperties())
          .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}

public static class HardwareInfo
{
    public static Dictionary<string, object> GetSystemSpecs()
    {
        return new Dictionary<string, object>
        {
            { "os.type", RuntimeInformation.OSDescription },
            { "os.architecture", RuntimeInformation.OSArchitecture.ToString() },
            { "process.runtime.version", RuntimeInformation.FrameworkDescription },
            
            { "device.cpu.cores", Environment.ProcessorCount },
            { "device.ram.gb", Math.Round((double)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 / 1024, 1) },
        };
    }
}

public static class AnonymousUser
{
    public static Dictionary<string, object> GetUserProperties()
    {
        string userId = UserSettings.Current.UserId;
        return new Dictionary<string, object>
        {
            { "user.id", userId }
        };
    }
}

public static class AppAnalytics
{
    private static readonly Meter AnalyticsMeter = new Meter("ETS2LA.Analytics");
    public static Activity? SessionActivity { get; private set; }

    private static readonly Counter<long> HeartbeatCounter =
        AnalyticsMeter.CreateCounter<long>("app.heartbeat", description: "Keeps track of uptime ticks (so that we can track active users over time).");

    private static readonly Counter<long> EventCounter =
        AnalyticsMeter.CreateCounter<long>("app.event", description: "Counts the number of events triggered.");


    public static void Pulse()
    {
        HeartbeatCounter.Add(1);
    }

    public static void LogEvent(string eventName, Dictionary<string, string>? customTags = null)
    {
        var allTags = OTelAttributes.GetAttributes()
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        allTags.Add("event.name", eventName);
        if (customTags != null)
        {
            foreach (var tag in customTags)
            {
                allTags[tag.Key] = tag.Value;
            }
        }

        var tagsArray = allTags.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value)).ToArray();
        EventCounter.Add(1, tagsArray);
    }
}