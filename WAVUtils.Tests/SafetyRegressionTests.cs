using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using static WAVUtils.Tests.WaveFixture;

namespace WAVUtils.Tests;

[TestClass]
public sealed class SafetyRegressionTests
{
    [TestMethod]
    public async Task Finding44MergeNeverOverwritesADestinationOrInput()
    {
        using var f = new WaveFixture();
        string a = f.Write("a.wav", Pcm16(1, 2)), b = f.Write("b.wav", Pcm16(3, 4));
        foreach (string output in new[] { a, f.Write("existing.wav", Pcm16(5, 6)) })
        {
            byte[] before = File.ReadAllBytes(output);
            await Assert.ThrowsAsync<IOException>(() => MergeCommand.Run([a, b], outputPath: output));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(output));
        }
    }

    [TestMethod]
    public async Task Finding44RejectedMergeReleasesEveryInput()
    {
        using var f = new WaveFixture();
        string a = f.Write("a.wav", Pcm16(1, 2)), b = f.Write("b.wav", [128], bits: 8);
        await Assert.ThrowsAsync<ArgumentException>(() => MergeCommand.Run([a, b], outputPath: f.PathOf("out.wav")));
        using var unlocked = new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.IsFalse(File.Exists(f.PathOf("out.wav")));
    }

    [TestMethod]
    [DataRow("negative-length")]
    [DataRow("unaligned-length")]
    [DataRow("length-beyond-audio")]
    [DataRow("zero-channels")]
    [DataRow("wrong-channel-sum")]
    [DataRow("duplicate-index")]
    [DataRow("duplicate-name")]
    [DataRow("null-track")]
    public async Task Finding37InvalidPmalIsRejectedBeforeOutputs(string scenario)
    {
        using var f = new WaveFixture();
        TrackMetadata[] tracks = [new() { Index = 0, FileName = "a.wav", Channels = 1, DataLength = 4 },
            new() { Index = 1, FileName = "b.wav", Channels = 1, DataLength = 4 }];
        switch (scenario)
        {
            case "negative-length": tracks[0].DataLength = -2; break;
            case "unaligned-length": tracks[0].DataLength = 3; break;
            case "length-beyond-audio": tracks[0].DataLength = 100; break;
            case "zero-channels": tracks[0].Channels = 0; break;
            case "wrong-channel-sum": tracks[1].Channels = 2; break;
            case "duplicate-index": tracks[1].Index = 0; break;
            case "duplicate-name": tracks[1].FileName = "A.WAV"; break;
            case "null-track": tracks[1] = null!; break;
        }
        string merged = f.Merged(tracks), output = f.PathOf("out");
        await Assert.ThrowsAsync<InvalidDataException>(() => SplitCommand.Run(merged, outputDirectory: output));
        Assert.IsFalse(Directory.Exists(output), "Validation precedes output creation.");
    }

    [TestMethod]
    public async Task Finding36SplitKeepsRawBextAndUnsupportedListSubtype()
    {
        using var f = new WaveFixture();
        byte[] bext = [.. Bext(), .. "odd\r\n"u8];
        bext[412] = 17;
        byte[] list = "UNKNunknown payload"u8.ToArray();
        TrackMetadata[] tracks = [new() { Index = 0, FileName = "a.wav", Channels = 1, DataLength = 4,
            Bext = BextChunkData.Parse(bext), Info = new() { Tags = new() { ["INAM"] = "fallback" } },
            Chunks = [new() { ChunkId = "bext", Data = bext }, new() { ChunkId = "LIST", Data = list }] },
            new() { Index = 1, FileName = "b.wav", Channels = 1, DataLength = 4 }];
        string merged = f.Write("merged.wav", Pcm16(1, 3, 2, 4), channels: 2,
            chunks: [new() { ChunkId = "pmal", Data = JsonSerializer.SerializeToUtf8Bytes(tracks) }]);
        string[] split = await SplitCommand.Run(merged, outputDirectory: f.PathOf("out"));
        var chunks = Chunks(split[0]);
        CollectionAssert.AreEqual(bext, chunks["bext"]);
        CollectionAssert.AreEqual(list, chunks["LIST"]);
    }

    [TestMethod]
    public void Finding37RiffSizeAndPaddingAreValidated()
    {
        using var f = new WaveFixture();
        string path = f.Write("bad.wav", Pcm16(1, 2));
        byte[] bytes = File.ReadAllBytes(path);
        BitConverter.GetBytes(4).CopyTo(bytes, 4);
        File.WriteAllBytes(path, bytes);
        Assert.ThrowsExactly<InvalidDataException>(() => WavMetadataChunk.ReadWavChunks(path));
    }

    [TestMethod]
    public void Finding38StandalonePmalWriteAddsAlignmentByte()
    {
        using var stream = new MemoryStream();
        bool exercised = false;
        foreach (string name in new[] { "a.wav", "aa.wav" })
        {
            stream.SetLength(0);
            stream.Position = 0;
            WavMetadataChunk.Write(stream, [new() { FileName = name, Channels = 1, DataLength = 2 }]);
            uint length = BitConverter.ToUInt32(stream.ToArray(), 4);
            if ((length & 1) == 0) continue;
            exercised = true;
            Assert.AreEqual(8L + length + 1, stream.Length);
            Assert.AreEqual((byte)0, stream.ToArray()[^1]);
        }
        Assert.IsTrue(exercised);
    }
}
