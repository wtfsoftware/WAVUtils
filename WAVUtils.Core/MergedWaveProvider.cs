using NAudio.Wave;

namespace WAVUtils.Core;

/// <summary>
/// Provides interleaved multi-channel PCM data from multiple mono sources.
/// Pads shorter sources with silence.
/// </summary>
public class MergedWaveProvider : IWaveProvider
{
    private readonly WaveFileReader[] _sources;
    private readonly int _channels;
    private readonly int _bytesPerSample;
    private bool _ended;

    public MergedWaveProvider(string[] inputFiles)
    {
        _sources = new WaveFileReader[inputFiles.Length];
        _channels = inputFiles.Length;

        for (int i = 0; i < inputFiles.Length; i++)
        {
            _sources[i] = new WaveFileReader(inputFiles[i]);
            if (_sources[i].WaveFormat.Channels != 1)
                throw new ArgumentException(string.Format(LocalizationManager.GetResource("ProviderNotMono"), inputFiles[i]));
        }

        int bytesPerSample = _sources[0].WaveFormat.BitsPerSample / 8;
        _bytesPerSample = _channels * bytesPerSample;
    }

    public WaveFormat WaveFormat
    {
        get
        {
            var fmt = _sources[0].WaveFormat;
            return WaveFormat.CreateCustomFormat(
                fmt.Encoding,
                fmt.SampleRate,
                _channels,
                fmt.SampleRate * _channels * fmt.BitsPerSample / 8,
                _channels * fmt.BitsPerSample / 8,
                fmt.BitsPerSample);
        }
    }

    public int Read(Span<byte> buffer)
    {
        if (_ended) return 0;

        int bytesPerSample = _sources[0].WaveFormat.BitsPerSample / 8;
        int framesToRead = buffer.Length / _bytesPerSample;
        int bytesRead = 0;
        byte[] sampleBuf = new byte[bytesPerSample];

        for (int frame = 0; frame < framesToRead; frame++)
        {
            bool anyMore = false;
            for (int ch = 0; ch < _channels; ch++)
            {
                int read = _sources[ch].Read(sampleBuf);
                if (read > 0) anyMore = true;

                int dstIdx = frame * _bytesPerSample + ch * bytesPerSample;
                if (read > 0)
                {
                    sampleBuf.AsSpan(0, read).CopyTo(buffer[dstIdx..]);
                }
            }
            bytesRead += _bytesPerSample;

            if (!anyMore)
            {
                _ended = true;
                break;
            }
        }

        return bytesRead;
    }
}
