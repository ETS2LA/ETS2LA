
using SoundFlow.Providers;

namespace ETS2LA.Audio.Generator;

// This was a failed experiment for making new sounds. You're free to use
// this for your own purposes. We don't use it in the base ETS2LA though.
public static class AudioGenerator
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    /// <summary>
    ///  Generates a decaying harmonic beep. This function is a direct port of OpenPilot's function here:
    ///  https://github.com/commaai/openpilot/blob/master/openpilot/selfdrive/assets/sounds/make_beeps.py
    /// </summary>
    /// <param name="freq">Frequency of the tone in Hz.</param>
    /// <param name="durationSeconds">Duration of the tone in seconds.</param>
    /// <returns>Stereo float samples (F32, 48 kHz), normalized to [-1, 1].</returns>
    public static float[] HarmonicBeep(float freq, float durationSeconds)
    {
        int nTotal = (int)(SampleRate * durationSeconds);
        var samples = new float[nTotal * Channels];

        // exp(-x / 5.5e3)
        const float decay = 5.5e3f;
        float phaseIncrement = 2f * MathF.PI * freq / SampleRate;

        for (int i = 0; i < nTotal; i++)
        {
            float signal = MathF.Sin(phaseIncrement * i);
            float envelope = MathF.Exp(-i / decay);
            float value = signal * envelope; // normalized

            int offset = i * Channels;
            for (int ch = 0; ch < Channels; ch++)
                samples[offset + ch] = value;
        }

        return samples;
    }

    /// <summary>
    ///  This function can be used to directly generate a SoundFlow provider
    ///  for harmonics beeps.
    /// </summary>
    public static RawDataProvider HarmonicBeepProvider(float freq, float durationSeconds)
    {
        return new RawDataProvider(HarmonicBeep(freq, durationSeconds), SampleRate);
    }
}