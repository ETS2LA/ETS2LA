using System.Collections.Concurrent;
using ETS2LA.Logging;
using ETS2LA.Settings;
using static ETS2LA.Translations.T;
using ETS2LA.Audio.Generator;

using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Providers;
using SoundFlow.Structs;

using System;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic;

namespace ETS2LA.Audio;

public class AudioHandler
{
    private static readonly Lazy<AudioHandler> _instance = new(() => new AudioHandler());
    public static AudioHandler Current => _instance.Value;

    private readonly ConcurrentQueue<AudioJob> _queue = new();
    private CancellationTokenSource? _currentCts;
    private bool _isRunning = true;

    private SettingsHandler _settingsHandler;
    private AudioSettings _settings;

    private MiniAudioEngine engine;
    private AudioFormat format = AudioFormat.DvdHq;
    private AudioPlaybackDevice? outputDevice;

    private AudioHandler()
    {
        engine = new MiniAudioEngine();
        engine.UpdateAudioDevicesInfo();
        var defaultDevice = engine.PlaybackDevices.FirstOrDefault(d => d.IsDefault);
        outputDevice = engine.InitializePlaybackDevice(defaultDevice, format);

        _settingsHandler = new SettingsHandler();
        _settings = _settingsHandler.Load<AudioSettings>("AudioSettings.json");
        _settingsHandler.RegisterListener<AudioSettings>("AudioSettings.json", OnSettingsChanged);
        Task.Run(ProcessAudioQueue);
    }

    /// <summary>
    ///  Set the current audio volume.
    /// </summary>
    /// <param name="volume">from 0.0f to 1.0f</param>
    public void SetVolume(float volume)
    {
        _settings.Volume = volume;
    }

    /// <summary>
    ///  Get the current audio volume.
    /// </summary>
    public float GetVolume()
    {
        return _settings.Volume;
    }

    /// <summary>
    ///  Get the queue length (check if audio is currently playing).
    /// </summary>
    public int GetQueueLength()
    {
        return _queue.Count;
    }

    /// <summary>
    ///  Queues a default audio beep for playback. You can use Queue() to play
    ///  custom audio.
    /// </summary>
    /// <param name="audio">Audio Type</param>
    /// <param name="loopCount">How many times to loop the audio</param>
    /// <param name="loopCondition">A function that returns a bool to determine if the audio should loop. If this is provided, loopCount is ignored.</param>
    /// <param name="overrideCurrent">Whether to override the currently playing audio (and queue
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void Play(DefaultAudio audio, int loopCount = 1, Func<bool>? loopCondition = null, bool overrideCurrent = false)
    {
        string filename = AppContext.BaseDirectory;
        switch (audio)
        {
            case DefaultAudio.Engage:
                filename += "Assets/Sounds/engage.wav"; break;
            case DefaultAudio.Disengage:
                filename += "Assets/Sounds/disengage.wav"; break;
            case DefaultAudio.Refuse:
                filename += "Assets/Sounds/refuse.wav"; break;
            case DefaultAudio.PreAlert:
                filename += "Assets/Sounds/pre_alert.wav"; break;
            case DefaultAudio.Warning:
                filename += "Assets/Sounds/warning.wav"; break;
            case DefaultAudio.Critical:
                filename += "Assets/Sounds/critical.wav"; break;
            case DefaultAudio.SoftWarning:
                filename += "Assets/Sounds/soft_warning.wav"; break;
            case DefaultAudio.SoftCritical:
                filename += "Assets/Sounds/soft_critical.wav"; break;
            default:
                throw new ArgumentOutOfRangeException(nameof(audio), audio, null);
        }

        if (overrideCurrent)
        {
            while (_queue.TryDequeue(out AudioJob? x)) { }
            
            _currentCts?.Cancel();
        }

        _queue.Enqueue(new AudioJob(AudioJobType.File, filename, null, loopCondition, loopCount));
    }

    // Internal file to catch legacy paths (engage.mp3, disengage.mp3, etc.) and redirect them 
    // to the new generated beeps. This will be removed once most plugins use the beeps instead.
    // TODO: Remove this function in the future.
    private bool CatchLegacyPaths(string filepath, int loopCount = 1)
    {
        if (filepath.EndsWith("engage.mp3", StringComparison.OrdinalIgnoreCase))
        {
            Play(DefaultAudio.Engage, loopCount);
            return true;
        }
        else if (filepath.EndsWith("disengage.mp3", StringComparison.OrdinalIgnoreCase))
        {
            Play(DefaultAudio.Disengage, loopCount);
            return true;
        }
        return false;
    }

    /// <summary>
    ///  Queue an audio file for playback.
    /// </summary>
    /// <param name="filepath">Filepath of the audio file to play.</param>
    /// <param name="overrideCurrent">Whether playing this audio should override the currently playing audio (and queue).</param>
    /// <param name="loopCount">How many times this file should be played.</param>
    public void Queue(string filepath, bool overrideCurrent = false, int loopCount = 1)
    {
        if (!File.Exists(filepath))
        {
            Logger.Warn(_("File not found: {0}", filepath));
            return;
        }

        if (overrideCurrent)
        {
            while (_queue.TryDequeue(out AudioJob? x)) { }
            
            _currentCts?.Cancel();
        }

        if (CatchLegacyPaths(filepath, loopCount))
        {
            Logger.Warn(_("Legacy audio path detected: {0}", filepath));
            return;
        }

        _queue.Enqueue(new AudioJob(AudioJobType.File, filepath, null, null, loopCount));
    }

    /// <summary>
    ///  Queue an audio file for playback with a loop condition.
    /// </summary>
    /// <param name="filepath">The filepath of the audio file to play.</param>
    /// <param name="loopCondition">The condition under which the audio should loop. (function with bool return)</param>
    public void Queue(string filepath, Func<bool> loopCondition)
    {
         _queue.Enqueue(new AudioJob(AudioJobType.File, filepath, null, loopCondition, 1));
    }

    /// <summary>
    ///  Queue a generated audio provider for playback with a loop condition.
    ///  This is useful for generated audio like beeps.
    /// </summary>
    /// <param name="data">The generated audio data to play.</param>
    /// <param name="loopCondition">The condition under which the audio should loop. (function with bool return)</param>
    public void Queue(RawDataProvider data, Func<bool> loopCondition)
    {
        _queue.Enqueue(new AudioJob(AudioJobType.Generated, "", data, loopCondition, 1));
    }

    private async Task ProcessAudioQueue()
    {
        while (_isRunning)
        {
            if (!_queue.TryDequeue(out var job))
            {
                await Task.Delay(50);
                continue;
            }

            _currentCts = new CancellationTokenSource();
            try 
            {
                if (job.LoopCondition != null)
                {
                    while (job.LoopCondition.Invoke() && !_currentCts.Token.IsCancellationRequested)
                    {
                        await PlaySound(job, _currentCts.Token);
                    }
                }
                else
                {
                    for (int i = 0; i < job.LoopCount; i++)
                    {
                        if (_currentCts.Token.IsCancellationRequested) break;
                        await PlaySound(job, _currentCts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info(_("Audio playback canceled"));
            }
            catch (Exception ex)
            {
                Logger.Error(_("Error playing audio file {0}: {1}", job.Filename ?? "(Raw Data)", ex.Message));
            }
            finally 
            {
                _currentCts.Dispose();
                _currentCts = null;
            }
        }
    }

    private async Task PlaySound(AudioJob job, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        if (outputDevice == null)
        {
            Logger.Error(_("No audio output device available"));
            return;
        }

        ISoundDataProvider dataProvider;
        if (job.Type == AudioJobType.File && !string.IsNullOrEmpty(job.Filename) && File.Exists(job.Filename))
        {
            using var stream = File.OpenRead(job.Filename);
            dataProvider = new AssetDataProvider(engine, format, stream);
        }
        else if (job.Type == AudioJobType.Generated && job.Data != null)
        {
            Logger.Info("Playing generated audio job");
            Logger.Info($"Generated audio job length: {job.Data.Length} samples");
            dataProvider = job.Data;
        }
        else
        {
            Logger.Error(_("Invalid audio job: {0}, does the file exist?", job.Filename ?? "(Raw Data)"));
            return;
        }

        using var player = new SoundPlayer(engine, format, dataProvider);
        outputDevice.MasterMixer.AddComponent(player);
        try
        {
            outputDevice.Start();

            player.Volume = _settings.Volume;
            player.Play();

            while (!token.IsCancellationRequested && player.State == PlaybackState.Playing)
            {
                await Task.Delay(100, token);
            }
        }
        finally
        {
            outputDevice.Stop();
            outputDevice.MasterMixer.RemoveComponent(player);
        }
    }

    private void OnSettingsChanged(AudioSettings audioSettings)
    {
        _settings = audioSettings;
    }

    public void Shutdown()
    {
        _isRunning = false;
        _currentCts?.Cancel();
        _settingsHandler.Save<AudioSettings>("AudioSettings.json", _settings);
    }
}
