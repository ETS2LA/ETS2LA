using ETS2LA.Backend.Events;

using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace ETS2LA.Game.Output;

public class GameOutput
{
    private static readonly Lazy<GameOutput> _instance = new(() => new GameOutput());
    public static GameOutput Current => _instance.Value;
    public string EventString = "ETS2LA.Game.Output.ControlEvent";

    public Dictionary<string, ControlChannel> Channels = new Dictionary<string, ControlChannel>();

    // Example:
    // "steering" : [(0.5, 1.0), (0.2, 0.5)]
    // Where the first value is the weight and the second value is the control value.
    // The channel itself doesn't matter, just their weights.
    private Dictionary<string, List<Tuple<float, float>>> curFrameFloats = new Dictionary<string, List<Tuple<float, float>>>();
    private float TickRate = 1f / 60f;

    private Stopwatch SinceTriedMemoryAccess = new Stopwatch();
    private bool MemoryAccessAvailable => legacyAccessor != null && modernAccessor != null;
    private bool IsReset = false;

    // Legacy uses a virtual controller provided through the SCSControls plugin. This
    // system works the same as SteamInput for example.

    string legacyMapName = "Local\\SCSControls";
    string legacyMapNameLinux = "/dev/shm/SCS/SCSControls";
    int legacyMapSize = 0;
    Dictionary<string, int> legacyShmOffsets = new Dictionary<string, int>();
    MemoryMappedFile? legacyMmf = null;
    MemoryMappedViewAccessor? legacyAccessor = null;

    // Modern uses ETS2LAPlugin to write directly to the game's memory. This does
    // not however work on all devices, and it doesn't provide such an extensive list
    // of controls as the legacy system does.

    string modernMapName = "Local\\ETS2LAPluginInput";
    string modernMapNameLinux = "/dev/shm/ETS2LAPluginInput";
    int modernMapSize = 26;
    MemoryMappedFile? modernMmf = null;
    MemoryMappedViewAccessor? modernAccessor = null;

    public GameOutput()
    {
        // Calculate the memory offsets for each of the variables
        // for legacy mode.
        int boolSize = sizeof(bool);
        int floatSize = sizeof(float);

        int offset = 0;
        foreach (var field in typeof(ControlVariables).GetFields())
        {
            legacyShmOffsets[field.Name] = offset;
            if (field.FieldType == typeof(bool?))
                offset += boolSize;
            else if (field.FieldType == typeof(float?))
                offset += floatSize;
        }
        legacyMapSize = offset;

        SinceTriedMemoryAccess.Start();
        TryOpenMemory();
        // Then we start listening to events and spin up the update thread.
        Events.Current.Subscribe<ControlEvent>(EventString, OnControlEvent);
        Task.Run(Tick);
    }

    private void TryOpenMemory()
    {
        if (MemoryAccessAvailable)
            return;

        float secondsSinceLastTry = (float)SinceTriedMemoryAccess.Elapsed.TotalSeconds;
        if (secondsSinceLastTry < 5f)
            return;


        try
        {
            #if WINDOWS
                legacyMmf = MemoryMappedFile.OpenExisting(legacyMapName);
                modernMmf = MemoryMappedFile.OpenExisting(modernMapName);
            # else
                legacyMmf = MemoryMappedFile.CreateFromFile(legacyMapNameLinux);
                modernMmf = MemoryMappedFile.CreateFromFile(modernMapNameLinux);
            # endif

            legacyAccessor = legacyMmf.CreateViewAccessor(0, legacyMapSize, MemoryMappedFileAccess.Write);
            modernAccessor = modernMmf.CreateViewAccessor(0, modernMapSize, MemoryMappedFileAccess.ReadWrite);
        } catch(Exception ex)
        {
            // Logging.Logger.Error("Failed to open memory: " + ex.Message);
            legacyAccessor = null;
            modernAccessor = null;
        }

        Logging.Logger.Debug(MemoryAccessAvailable ? "Successfully opened memory for output." 
                                                  : "Memory not available for output.");
        SinceTriedMemoryAccess.Restart();
    }

    public void OnControlEvent(ControlEvent controlEvent)
    {
        string channel = controlEvent.ChannelDefinition.Id;
        if (!Channels.ContainsKey(channel))
        {
            Channels[channel] = new ControlChannel{
                Definition = controlEvent.ChannelDefinition,
                Properties = controlEvent.Properties,
                Variables = controlEvent.Variables
            };
        }
        else if (controlEvent.Variables == null || controlEvent.Properties == null)
        {
            Channels.Remove(channel);
        }
        else
        {
            Channels[channel].Properties = controlEvent.Properties;
            Channels[channel].Variables = controlEvent.Variables;
            Channels[channel].LastUpdate.Restart();
            Channels[channel].BoolsProcessed = false;
        }
    }

    private void ResetOutputs()
    {        
        if (!MemoryAccessAvailable)
            return;

        if (IsReset)
            return;
        
        if (legacyAccessor != null)
        {
            foreach (var field in typeof(ControlVariables).GetFields())
            {
                if (field.FieldType == typeof(float?))
                {
                    WriteFloat(legacyAccessor!, legacyShmOffsets[field.Name], 0);
                }
                else if (field.FieldType == typeof(bool?))
                {
                    WriteBool(legacyAccessor!, legacyShmOffsets[field.Name], false);
                }
            }
            legacyAccessor.Flush();
        }

        if(modernAccessor != null)
        {
            WriteFloat(modernAccessor, 0, 0);
            WriteBool(modernAccessor, 4, false);
            WriteDouble(modernAccessor, 5, 0);
            WriteFloat(modernAccessor, 13, 0);
            WriteBool(modernAccessor, 17, false);
            WriteDouble(modernAccessor, 18, 0);
            
            modernAccessor.Flush();
        }

        IsReset = true;
        Logging.Logger.Debug("Reset outputs to default values.");
    }

    private void ToggleBool(MemoryMappedViewAccessor accessor, int offset)
    {
        WriteBool(accessor, offset, true);
        Thread.Sleep(50);
        WriteBool(accessor, offset, false);
    }

    private void WriteBool(MemoryMappedViewAccessor accessor, int offset, bool value)
    {
        accessor.Write(offset, value);
    }

    private void WriteFloat(MemoryMappedViewAccessor accessor, int offset, float value)
    {
        accessor.Write(offset, value);
    }

    private void WriteDouble(MemoryMappedViewAccessor accessor, int offset, double value)
    {
        accessor.Write(offset, value);
    }

    private void ProcessChannel(ControlChannel channel)
    {
        var boolType = channel.Properties.BooleanType;
        var weight = channel.Properties.Weight;

        foreach (var prop in typeof(ControlVariables).GetFields())
        {
            var value = prop.GetValue(channel.Variables);
            if (value != null)
            {
                if (prop.FieldType == typeof(bool?) && !channel.BoolsProcessed)
                {
                    bool boolValue = (bool)value;
                    if(boolValue && boolType == ControlBooleanType.TrueToToggle)
                        Task.Run(() => ToggleBool(legacyAccessor!, legacyShmOffsets[prop.Name]));
                    else
                        WriteBool(legacyAccessor!, legacyShmOffsets[prop.Name], boolValue);
                }

                if (prop.FieldType == typeof(float?))
                {
                    float floatValue = (float)value;

                    string propName = prop.Name;
                    if (propName == "aforward" || propName == "abackward")
                        propName = "acceleration";

                    if (!curFrameFloats.ContainsKey(propName))
                        curFrameFloats[propName] = new List<Tuple<float, float>>();

                    curFrameFloats[propName].Add(new Tuple<float, float>(weight, floatValue));
                }
            }
        }

        channel.BoolsProcessed = true;
    }

    public void Tick()
    {
        Stopwatch tickTimer = Stopwatch.StartNew();
        int consecutiveErrors = 0;
        while(true)
        {
            try
            {
                float timeLeft = TickRate - (float)tickTimer.Elapsed.TotalSeconds;
                if (timeLeft > 0)
                {   
                    Thread.Sleep((int)(timeLeft * 1000));
                    continue;
                }

                // These || need to be added to silence warnings...
                // If someone knows how to make the compiler understand that MemoryAccessAvailable ensures
                // that the accessors are not null, then please tell me.
                if (!MemoryAccessAvailable || legacyAccessor == null || modernAccessor == null)
                {
                    TryOpenMemory();
                    tickTimer.Restart();
                    continue;
                }

                if(Channels.Count == 0)
                {
                    if (!IsReset)
                        ResetOutputs();
                    
                    tickTimer.Restart();
                    continue;
                }

                IsReset = false;

                // Iterate over a snapshot to avoid collection-modified-during-enumeration crashes
                var channelSnapshot = Channels.Values.ToList();
                foreach (var channel in channelSnapshot)
                {
                    if (channel.Properties == null || channel.Variables == null)
                        continue;

                    if (channel.LastUpdate.Elapsed.TotalSeconds > channel.Definition.Timeout)
                    {
                        Logging.Logger.Debug($"Channel {channel.Definition.Id} timed out, removing.");
                        Channels.Remove(channel.Definition.Id);
                        continue;
                    }

                    ProcessChannel(channel);
                }

                double time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                foreach (var kvp in curFrameFloats)
                {
                    string propName = kvp.Key;
                    List<Tuple<float, float>> values = kvp.Value;

                    if (values.Count == 0) continue;

                    float totalWeight = values.Sum(v => v.Item1);
                    if (totalWeight <= 0) continue;
                    float weightedValue = values.Sum(v => v.Item1 * v.Item2) / totalWeight;
                    weightedValue = Math.Clamp(weightedValue, -1f, 1f);

                    if(propName == "steering")
                    {
                        WriteFloat(modernAccessor, 0, weightedValue);
                        WriteBool(modernAccessor, 4, weightedValue != 0.0f);
                        WriteDouble(modernAccessor, 5, time);
                        WriteFloat(legacyAccessor, legacyShmOffsets[propName], -weightedValue);
                    }
                    else if (propName == "acceleration")
                    {
                        WriteFloat(modernAccessor, 13, weightedValue);
                        WriteBool(modernAccessor, 17, weightedValue != 0.0f);
                        WriteDouble(modernAccessor, 18, time);
                        if (weightedValue < 0)
                        {
                            WriteFloat(legacyAccessor, legacyShmOffsets["abackward"], -weightedValue);
                            WriteFloat(legacyAccessor, legacyShmOffsets["aforward"], 0);
                        }
                        else
                        {
                            WriteFloat(legacyAccessor, legacyShmOffsets["aforward"], weightedValue);
                            WriteFloat(legacyAccessor, legacyShmOffsets["abackward"], 0);   
                        }
                    }
                    else
                    {
                        WriteFloat(legacyAccessor!, legacyShmOffsets[propName], weightedValue);
                    }
                }

                modernAccessor.Flush();
                legacyAccessor.Flush();

                curFrameFloats.Clear();
                consecutiveErrors = 0;
                tickTimer.Restart();
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                Logging.Logger.Error($"Error in GameOutput tick loop (error #{consecutiveErrors}): {ex.Message}");
                
                // If we keep failing, the memory access is probably broken — reset accessors
                if (consecutiveErrors >= 5)
                {
                    Logging.Logger.Warn("Too many consecutive errors in GameOutput tick. Resetting memory accessors.");
                    legacyAccessor?.Dispose();
                    modernAccessor?.Dispose();
                    legacyAccessor = null;
                    modernAccessor = null;
                    legacyMmf?.Dispose();
                    modernMmf?.Dispose();
                    legacyMmf = null;
                    modernMmf = null;
                    consecutiveErrors = 0;
                    SinceTriedMemoryAccess.Restart();
                }
                
                tickTimer.Restart();
                Thread.Sleep(100);
            }
        }

    }
}