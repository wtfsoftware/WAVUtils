using NAudio.Wave;
using System.Runtime.InteropServices;

namespace WAVUtils.Core;

/// <summary>
/// Utility for creating wave formats with specific channel count.
/// </summary>
public static class WaveFormatExtensions
{
    public static WaveFormat CreateMono(WaveFormat original)
    {
        int sampleRate = original.SampleRate;
        int bitsPerSample = original.BitsPerSample;
        int byteRate = sampleRate * bitsPerSample / 8;
        int blockAlign = bitsPerSample / 8;

        return WaveFormat.CreateCustomFormat(
            WaveFormatEncoding.Pcm,
            sampleRate, 1, byteRate, blockAlign, bitsPerSample);
    }

    public static WaveFormat CreateMultiChannel(WaveFormat original, int channels)
    {
        int sampleRate = original.SampleRate;
        int bitsPerSample = original.BitsPerSample;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;

        return WaveFormat.CreateCustomFormat(
            WaveFormatEncoding.Pcm,
            sampleRate, channels, byteRate, blockAlign, bitsPerSample);
    }
}
