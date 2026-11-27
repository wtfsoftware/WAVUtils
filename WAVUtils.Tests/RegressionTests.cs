using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using static WAVUtils.Tests.WaveFixture;

namespace WAVUtils.Tests;

[TestClass]
public sealed class RegressionTests
{
    [TestMethod]
    public async Task EqualLengthPcm16RoundTripPreservesEverySample()
    {
        using var f = new WaveFixture();
        byte[] a = Pcm16(100, 200, 300, 400), b = Pcm16(500, 600, 700, 800);
        string merged = await MergeCommand.Run([f.Write("a.wav", a), f.Write("b.wav", b)], outputPath: f.PathOf("merged.wav"));
        CollectionAssert.AreEqual(Pcm16(100, 500, 200, 600, 300, 700, 400, 800), Chunks(merged)["data"]);
        string[] split = await SplitCommand.Run(merged, outputDirectory: f.PathOf("out"));
        CollectionAssert.AreEqual(a, Chunks(split[0])["data"]);
        CollectionAssert.AreEqual(b, Chunks(split[1])["data"]);
    }

    [TestMethod]
    public async Task Finding30ShortTrackKeepsOriginalLength()
    {
        using var f = new WaveFixture();
        byte[] original = Pcm16(100, 200);
        string merged = f.Merged(new() { Index = 0, FileName = "a.wav", Channels = 1, DataLength = original.Length },
            new() { Index = 1, FileName = "b.wav", Channels = 1, DataLength = 8 });
        string[] split = await SplitCommand.Run(merged, outputDirectory: f.PathOf("out"));
        CollectionAssert.AreEqual(original, Chunks(split[0])["data"]);
    }

    [TestMethod]
    public async Task Finding29ExistingOutputIsNotOverwritten()
    {
        using var f = new WaveFixture();
        string merged = f.Merged(new() { Index = 0, FileName = "a.wav", Channels = 1, DataLength = 4 },
            new() { Index = 1, FileName = "b.wav", Channels = 1, DataLength = 4 });
        string output = f.PathOf("out");
        Directory.CreateDirectory(output);
        string sentinel = Path.Combine(output, "a.wav");
        byte[] original = Encoding.ASCII.GetBytes("existing file must survive");
        File.WriteAllBytes(sentinel, original);
        try { await SplitCommand.Run(merged, outputDirectory: output); }
        catch (IOException) { }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(sentinel));
        Assert.IsFalse(File.Exists(Path.Combine(output, "b.wav")), "Refuse the whole batch before output creation.");
    }

    [TestMethod]
    [DataRow("../escape.wav")]
    [DataRow("..\\escape.wav")]
    [DataRow("absolute")]
    [DataRow("safe.wav:stream")]
    public async Task Finding29UntrustedNamesCannotEscape(string name)
    {
        using var f = new WaveFixture();
        string sentinel = f.PathOf("escape.wav");
        if (name == "absolute") name = sentinel;
        byte[] original = Encoding.ASCII.GetBytes("outside destination");
        File.WriteAllBytes(sentinel, original);
        string merged = f.Merged(new() { Index = 0, FileName = name, Channels = 1, DataLength = 4 },
            new() { Index = 1, FileName = "safe.wav", Channels = 1, DataLength = 4 });
        bool refused = false;
        try { await SplitCommand.Run(merged, outputDirectory: f.PathOf("out")); }
        catch (ArgumentException) { refused = true; }
        catch (InvalidDataException) { refused = true; }
        Assert.IsTrue(refused, "Unsafe PMAL name must be rejected.");
        CollectionAssert.AreEqual(original, File.ReadAllBytes(sentinel));
    }

    [TestMethod]
    public async Task Finding31PmalAfterCueIsFound()
    {
        using var f = new WaveFixture();
        byte[] cue = new byte[28];
        BitConverter.GetBytes(1).CopyTo(cue, 0);
        "data"u8.CopyTo(cue.AsSpan(12));
        TrackMetadata[] tracks = [new() { Index = 0, FileName = "a.wav", Channels = 1, DataLength = 4, Chunks = [new() { ChunkId = "cue ", Data = cue }] },
            new() { Index = 1, FileName = "b.wav", Channels = 1, DataLength = 4 }];
        string merged = f.Write("merged.wav", Pcm16(100, 300, 200, 400), channels: 2,
            chunks: [new() { ChunkId = "cue ", Data = cue }, new() { ChunkId = "pmal", Data = JsonSerializer.SerializeToUtf8Bytes(tracks) }]);
        Assert.IsTrue(Chunks(merged).ContainsKey("cue "));
        string[] split = await SplitCommand.Run(merged, outputDirectory: f.PathOf("out"));
        CollectionAssert.AreEqual(cue, Chunks(split[0])["cue "]);
    }

    [TestMethod]
    public async Task Finding32SplitRiffSizeIncludesMetadata()
    {
        using var f = new WaveFixture();
        string merged = await MergeCommand.Run([f.Write("a.wav", Pcm16(100, 200), chunks: [new() { ChunkId = "bext", Data = Bext() }]), f.Write("b.wav", Pcm16(300, 400))], outputPath: f.PathOf("merged.wav"));
        string[] split = await SplitCommand.Run(merged, outputDirectory: f.PathOf("out"));
        Assert.IsTrue(Chunks(split[0]).ContainsKey("bext"));
        AssertRiffSize(split[0]);
    }

    [TestMethod]
    public async Task Finding33MixedPcmAndFloatAreRejectedBeforeWriting()
    {
        using var f = new WaveFixture();
        string output = f.PathOf("merged.wav");
        bool refused = false;
        try { await MergeCommand.Run([f.Write("pcm.wav", BitConverter.GetBytes(1 << 30), bits: 32), f.Write("float.wav", BitConverter.GetBytes(0.5f), bits: 32, encoding: 3)], outputPath: output); }
        catch (ArgumentException) { refused = true; }
        Assert.IsTrue(refused);
        Assert.IsFalse(File.Exists(output));
    }

    [TestMethod]
    public async Task Finding33UnsignedPcmPaddingIs128()
    {
        using var f = new WaveFixture();
        string merged = await MergeCommand.Run([f.Write("a.wav", [128, 129], bits: 8), f.Write("bb.wav", [128, 128, 128, 128], bits: 8)], outputPath: f.PathOf("merged.wav"));
        CollectionAssert.AreEqual(new byte[] { 128, 128, 129, 128, 128, 128, 128, 128 }, Chunks(merged)["data"]);
    }

    [TestMethod]
    public void Finding34BextSerializeUsesEbuOffsets()
    {
        byte[] bytes = new BextChunkData { OriginationTime = "01:00:00", TimeReference = 172800000, Version = 1 }.Serialize();
        Assert.AreEqual(172800000L, BitConverter.ToInt64(bytes, 338));
        Assert.AreEqual((short)1, BitConverter.ToInt16(bytes, 346));
    }

    [TestMethod]
    public void Finding34StandardVersionAndUmidArePreserved()
    {
        byte[] bytes = Bext();
        var parsed = BextChunkData.Parse(bytes)!;
        Assert.AreEqual((short)1, parsed.Version);
        CollectionAssert.AreEqual(bytes.AsSpan(348, 64).ToArray(), parsed.Umid);
        CollectionAssert.AreEqual(bytes, parsed.Serialize());
    }

    [TestMethod]
    public void Finding35GeneratedIxmlIsXml()
    {
        MethodInfo method = typeof(MergeCommand).GetMethod("SetIxmlTimecode", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tc = new LtcDecoder.Timecode(Hours: 1, Minutes: 2, Seconds: 3, Frames: 4, DropFrame: false, Fps: 24, SampleOffset: 0);
        string xml = (string)method.Invoke(null, [null, tc])!;
        Assert.AreEqual("BWF_XML", XDocument.Parse(xml).Root!.Name.LocalName);
    }

    [TestMethod]
    public void Finding36StandardInfoIsRecognized()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.ASCII, leaveOpen: true);
        writer.Write("INFO"u8);
        WriteChunk(writer, "INAM", "Title\0"u8.ToArray());
        var parsed = WavChunkParsers.ExtractTypedMetadata([new() { ChunkId = "LIST", Data = memory.ToArray() }]);
        Assert.AreEqual("Title", parsed.Info?.Tags.GetValueOrDefault("INAM"));
    }

    [TestMethod]
    public void Finding37TruncatedChunkIsRejected()
    {
        using var f = new WaveFixture();
        string path = f.Write("broken.wav", Pcm16(100, 200), chunks: [new() { ChunkId = "JUNK", Data = [1, 2] }]);
        byte[] bytes = File.ReadAllBytes(path);
        BitConverter.GetBytes(100).CopyTo(bytes, bytes.Length - 6);
        File.WriteAllBytes(path, bytes);
        bool refused = false;
        try { WavMetadataChunk.ReadWavChunks(path); }
        catch (InvalidDataException) { refused = true; }
        catch (EndOfStreamException) { refused = true; }
        Assert.IsTrue(refused);
    }

    [TestMethod]
    public async Task Finding38OddDataHasPadding()
    {
        using var f = new WaveFixture();
        string merged = await MergeCommand.Run([f.Write("mono.wav", [128], bits: 8), f.Write("stereo.wav", [128, 128], channels: 2, bits: 8)], outputPath: f.PathOf("merged.wav"));
        Assert.IsTrue(Chunks(merged).ContainsKey("pmal"));
        AssertRiffSize(merged);
    }

    [TestMethod]
    public async Task Finding38OddPmalHasPadding()
    {
        using var f = new WaveFixture();
        string a = f.Write("a.wav", new byte[8]);
        bool exercised = false;
        foreach (string name in new[] { "b.wav", "bb.wav" })
        {
            string merged = await MergeCommand.Run([a, f.Write(name, new byte[8])], outputPath: f.PathOf("merged-" + name));
            if ((Chunks(merged)["pmal"].Length & 1) == 0) continue;
            exercised = true;
            Assert.AreEqual(0L, new FileInfo(merged).Length & 1);
        }
        Assert.IsTrue(exercised, "Exercise an odd PMAL body.");
    }

    [TestMethod]
    public void Finding40DonorShiftDoesNotAccumulate()
    {
        using var f = new WaveFixture();
        string path = f.Write("target.wav", new byte[8], chunks: [new() { ChunkId = "bext", Data = Bext() }]);
        var tc = new LtcDecoder.Timecode(Hours: 2, Minutes: 0, Seconds: 0, Frames: 0, DropFrame: false, Fps: 24, SampleOffset: 0);
        var donor = (Tc: tc, Decoded: 345600000L, BaseOld: 172800000L, Rate: 48000);
        MethodInfo patch = typeof(TcCommand).GetMethod("PatchOne", BindingFlags.NonPublic | BindingFlags.Static)!;
        patch.Invoke(null, [path, donor]);
        byte[] first = File.ReadAllBytes(path);
        patch.Invoke(null, [path, donor]);
        CollectionAssert.AreEqual(first, File.ReadAllBytes(path));
    }
}
