using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using static WAVUtils.Tests.WaveFixture;

namespace WAVUtils.Tests;

[TestClass]
public sealed class LtcRegressionTests
{
    internal const long Reference = 354441900;

    [TestMethod]
    public void IndependentLtcWireVectorPinsTheAudioStartReference()
    {
        var tc = LtcDecoder.Decode(Signal(), sampleRate: 48000);
        Assert.IsNotNull(tc);
        Assert.AreEqual(2, tc.Value.Hours);
        Assert.AreEqual(3, tc.Value.Minutes);
        Assert.AreEqual(4, tc.Value.Seconds);
        Assert.AreEqual(24.0, tc.Value.Fps, 0.005);
        Assert.IsTrue(Math.Abs(LtcDecoder.ToSampleReference(tc.Value, sampleRate: 48000) - Reference) <= 2);
    }

    [TestMethod]
    public async Task Finding43MergeDecodesLtcWithTheLastInputsOwnFormat()
    {
        using var f = new WaveFixture();
        string first = f.Write("first.wav", Pcm16(0, 0), channels: 2);
        string ltc = f.Write("ltc.wav", Audio());
        string path = await MergeCommand.Run([first, ltc], f.PathOf("merged.wav"));
        long reference = BinaryPrimitives.ReadInt64LittleEndian(Chunks(path)["bext"].AsSpan(338));
        Assert.IsTrue(Math.Abs(reference - Reference) <= 2);
    }

    [TestMethod]
    public void Finding43OppositePolarityChannelsAreDecodedSeparately()
    {
        using var f = new WaveFixture();
        string path = f.Write("stereo-ltc.wav", Audio(channels: 2), channels: 2);
        using var reader = File.OpenRead(path);
        var tc = MergeCommand.DecodeLtcFromReader(reader, WavMetadataChunk.ReadFile(reader));
        Assert.IsNotNull(tc);
        Assert.IsTrue(Math.Abs(LtcDecoder.ToSampleReference(tc.Value, sampleRate: 48000) - Reference) <= 2);
    }

    internal static byte[] Audio(int channels = 1)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (float value in Signal())
            for (int channel = 0; channel < channels; channel++)
                writer.Write((short)(value * (channel == 0 ? 16000 : -16000)));
        return stream.ToArray();
    }

    internal static float[] Signal()
    {
        // Independent wire bytes: 02:03:04:05..08 at 24 fps; parity at bit 27,
        // forward sync FC BF. No production BCD or frame-building code is reused.
        byte[][] frames = [[5, 0, 4, 8, 3, 0, 2, 0, 252, 191], [6, 0, 4, 8, 3, 0, 2, 0, 252, 191],
            [7, 0, 4, 0, 3, 0, 2, 0, 252, 191], [8, 0, 4, 0, 3, 0, 2, 0, 252, 191]];
        var samples = new float[100 + frames.Length * 2000];
        float level = 0.5f;
        int position = 100;
        foreach (byte[] frame in frames)
            for (int bit = 0; bit < 80; bit++)
            {
                level = -level;
                bool one = (frame[bit / 8] & (1 << (bit % 8))) != 0;
                for (int sample = 0; sample < 25; sample++)
                {
                    if (one && sample == 13) level = -level;
                    samples[position++] = level;
                }
            }
        return samples;
    }
}
