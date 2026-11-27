using System.Buffers.Binary;
using System.Numerics;
using NAudio.Wave;

namespace WAVUtils.Core;

// NAudio writes the audio and RF64 envelope; this format retains the original fmt bytes.
internal sealed class WavAudioFormat : WaveFormat
{
    internal byte[] Body { get; }
    internal WaveFormatEncoding SampleEncoding { get; }
    internal int ValidBits { get; }

    internal WavAudioFormat(byte[] body)
    {
        if (body.Length < 16 || body.Length == 17 || body.Length > 32785 ||
            (body.Length >= 18 && BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(16)) != body.Length - 18))
            throw new InvalidDataException("Invalid fmt chunk size.");
        Body = body;
        waveFormatTag = (WaveFormatEncoding)BinaryPrimitives.ReadUInt16LittleEndian(body);
        channels = BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(2));
        sampleRate = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4));
        averageBytesPerSecond = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(8));
        blockAlign = BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(12));
        bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(14));
        extraSize = body.Length >= 18 ? (short)(body.Length - 18) : (short)0;
        SampleEncoding = waveFormatTag;
        ValidBits = bitsPerSample;
        if (waveFormatTag == WaveFormatEncoding.Extensible)
        {
            if (body.Length < 40) throw new InvalidDataException("Truncated extensible fmt chunk.");
            var subtype = new Guid(body.AsSpan(24, 16));
            SampleEncoding = subtype == AudioMediaSubtypes.MEDIASUBTYPE_PCM ? WaveFormatEncoding.Pcm
                : subtype == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT ? WaveFormatEncoding.IeeeFloat
                : throw new ArgumentException("Only PCM and IEEE float audio are supported.");
            ValidBits = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(18));
            uint mask = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(20));
            if (mask != 0 && BitOperations.PopCount(mask) != channels)
                throw new InvalidDataException("Channel mask does not match the channel count.");
        }
        if (SampleEncoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat) ||
            (SampleEncoding == WaveFormatEncoding.Pcm && bitsPerSample is not (8 or 16 or 24 or 32)) ||
            (SampleEncoding == WaveFormatEncoding.IeeeFloat && bitsPerSample is not (32 or 64)))
            throw new ArgumentException("Only PCM8/16/24/32 and IEEE float32/64 audio are supported.");
        if (channels <= 0 || sampleRate <= 0 || ValidBits <= 0 || ValidBits > bitsPerSample ||
            (SampleEncoding == WaveFormatEncoding.IeeeFloat && ValidBits != bitsPerSample) ||
            blockAlign <= 0 || blockAlign != channels * (bitsPerSample / 8) ||
            averageBytesPerSecond != (long)sampleRate * blockAlign)
            throw new InvalidDataException("Invalid audio format fields.");
    }

    public override void Serialize(BinaryWriter writer)
    {
        writer.Write(Body.Length);
        writer.Write(Body);
        // shortcut: NAudio 3.3.0 doesn't align fmt; remove this pad when the writer handles it.
        if ((Body.Length & 1) != 0) writer.Write((byte)0);
    }

    internal static WavAudioFormat FromWaveFormat(WaveFormat format)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        format.Serialize(writer);
        return new WavAudioFormat(stream.ToArray()[4..]);
    }

    internal WavAudioFormat WithChannels(int channelCount)
    {
        long alignment = (long)channelCount * (BitsPerSample / 8);
        if (channelCount <= 0 || channelCount > short.MaxValue || alignment > short.MaxValue ||
            alignment * SampleRate > int.MaxValue)
            throw new ArgumentException("Combined channel count exceeds the WAV format limits.");
        WaveFormat format = Encoding == WaveFormatEncoding.Extensible || channelCount > 2 || BitsPerSample > 16
            ? new WaveFormatExtensible(rate: SampleRate, bits: BitsPerSample, channels: channelCount,
                subFormat: SampleEncoding == WaveFormatEncoding.Pcm ? AudioMediaSubtypes.MEDIASUBTYPE_PCM : AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT,
                validBitsPerSample: ValidBits, channelMask: 0)
            : CreateCustomFormat(tag: SampleEncoding, sampleRate: SampleRate, channels: channelCount,
                averageBytesPerSecond: (int)(alignment * SampleRate), blockAlign: (int)alignment, bitsPerSample: BitsPerSample);
        return FromWaveFormat(format);
    }

    internal bool IsCompatibleWith(WavAudioFormat other) =>
        SampleRate == other.SampleRate && BitsPerSample == other.BitsPerSample &&
        SampleEncoding == other.SampleEncoding && ValidBits == other.ValidBits;

    internal void ValidateDataLength(long length)
    {
        if (length < 0 || length % BlockAlign != 0)
            throw new InvalidDataException("Audio data does not contain whole sample frames.");
    }
}
