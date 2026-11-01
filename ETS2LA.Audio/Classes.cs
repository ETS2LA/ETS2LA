using SoundFlow.Providers;

namespace ETS2LA.Audio;

public enum DefaultAudio
{
    Engage,
    Disengage,
    Refuse,
    PreAlert,
    Warning,
    Critical,
    SoftWarning,
    SoftCritical,
}

enum AudioJobType
{
    File,
    Generated
}

class AudioJob
{
    public AudioJobType Type { get; }

    public string? Filename { get; }
    public RawDataProvider? Data { get; }
    public Func<bool>? LoopCondition { get; }
    public int LoopCount { get; } = 0;

    public AudioJob(AudioJobType type, string filename = "", RawDataProvider? data = null, Func<bool>? condition = null, int loopCount = 0)
    {
        Type = type;
        Filename = filename;
        Data = data;
        LoopCondition = condition;
        LoopCount = loopCount;
    }
}

[Serializable]
public class AudioSettings
{
    public float Volume = 0.5f;
}