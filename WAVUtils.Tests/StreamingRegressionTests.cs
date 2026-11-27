using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using static WAVUtils.Tests.WaveFixture;

namespace WAVUtils.Tests;

[TestClass]
public sealed class StreamingRegressionTests
{
    [TestMethod]
    [DoNotParallelize]
    public async Task Finding41MergeUsesBoundedBuffersAndReportsProcessedAudio()
    {
        using var f = new WaveFixture();
        string a = f.Write("a.wav", new byte[1024 * 1024]), b = f.Write("b.wav", new byte[1024 * 1024]);
        var updates = new List<double>();
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        string merged = await MergeCommand.Run([a, b], outputPath: f.PathOf("merged.wav"),
            progress: (value, _) => updates.Add(value));
        allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        Assert.IsTrue(allocated < 16 * 1024 * 1024, $"Allocated {allocated} bytes for two small tracks.");
        Assert.IsTrue(updates.Count > 5, "Report progress inside the audio loop, not only at phase boundaries.");
        Assert.AreEqual(1.0, updates[^1], 1e-9);
        Assert.IsTrue(updates.Zip(updates.Skip(1), (a, b) => b >= a).All(value => value));
        Assert.AreEqual(2 * 1024 * 1024, Chunks(merged)["data"].Length);
    }

    [TestMethod]
    public void Finding41Rf64MetadataUsesDs64Lengths()
    {
        using var f = new WaveFixture();
        string path = f.PathOf("small-rf64.wav");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RF64"u8);
            writer.Write(uint.MaxValue);
            writer.Write("WAVEds64"u8);
            writer.Write(28);
            writer.Write(0L);
            writer.Write(4L);
            writer.Write(2L);
            writer.Write(0);
            WriteChunk(writer, "fmt ", [1, 0, 1, 0, 128, 187, 0, 0, 0, 119, 1, 0, 2, 0, 16, 0]);
            writer.Write("data"u8);
            writer.Write(uint.MaxValue);
            writer.Write(Pcm16(1, 2));
            WriteChunk(writer, "bext", Bext());
            stream.Position = 20;
            writer.Write(stream.Length - 8);
        }
        WavChunksResult result = WavMetadataChunk.ReadWavChunks(path);
        Assert.AreEqual(172800000L, result.TypedMetadata.Bext!.TimeReference);
        Assert.IsFalse(result.Chunks.Any(chunk => chunk.ChunkId == "ds64"), "Structural chunks are not track metadata.");
    }

    [TestMethod]
    public void Finding42CoreCommandsExposeCooperativeCancellation()
    {
        foreach (Type command in new[] { typeof(MergeCommand), typeof(SplitCommand), typeof(TcCommand) })
            Assert.IsTrue(command.GetMethod("Run")!.GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken)),
                $"{command.Name} must accept a cancellation token.");
    }
}
