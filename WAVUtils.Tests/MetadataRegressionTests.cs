using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using static WAVUtils.Tests.WaveFixture;

namespace WAVUtils.Tests;

[TestClass]
public sealed class MetadataRegressionTests
{
    [TestMethod]
    public void Finding34BextRoundTripKeepsLoudnessReservedAndOddHistory()
    {
        byte[] original = [.. Bext(), .. "A=PCM\r\n"u8];
        original[412] = 123;
        original[601] = 45;
        CollectionAssert.AreEqual(original, BextChunkData.Parse(original)!.Serialize());
    }

    [TestMethod]
    public void Finding34MergeStampOnlyChangesTheSixteenTimeBytes()
    {
        using var f = new WaveFixture();
        byte[] original = Bext();
        original[412] = 123;
        var tracks = new[] { new TrackMetadata { Bext = BextChunkData.Parse(original), Chunks = [new() { ChunkId = "bext", Data = original }] } };
        string source = f.Write("source.wav", Pcm16(100), chunks: [new() { ChunkId = "bext", Data = original }]);
        var tc = new LtcDecoder.Timecode(Hours: 2, Minutes: 0, Seconds: 0, Frames: 0, DropFrame: false, Fps: 24, SampleOffset: 0);
        MethodInfo method = typeof(MergeCommand).GetMethod("BuildMergedBext", BindingFlags.NonPublic | BindingFlags.Static)!;
        byte[] stamped = (byte[])method.Invoke(null, [tracks, new[] { source }, tc, 48000])!;
        byte[] expected = (byte[])original.Clone();
        "02:00:00"u8.CopyTo(expected.AsSpan(330));
        BitConverter.GetBytes(345600000L).CopyTo(expected, 338);
        CollectionAssert.AreEqual(expected, stamped);
    }

    [TestMethod]
    public void Finding35IxmlUsesSpeedAndKeepsSessionText()
    {
        string xml = StampIxml("<BWF_XML><SESSION>session name</SESSION><SCENE>scene &amp; take</SCENE><SPEED><NOTE>keep</NOTE><TIMECODE_RATE>25/1</TIMECODE_RATE></SPEED></BWF_XML>");
        XElement root = XDocument.Parse(xml).Root!;
        Assert.AreEqual("session name", root.Element("SESSION")!.Value);
        Assert.AreEqual("scene & take", root.Element("SCENE")!.Value);
        Assert.AreEqual("keep", root.Element("SPEED")!.Element("NOTE")!.Value);
        Assert.AreEqual("24/1", root.Element("SPEED")!.Element("TIMECODE_RATE")!.Value);
        Assert.AreEqual("NDF", root.Element("SPEED")!.Element("TIMECODE_FLAG")!.Value);
        Assert.IsNull(root.Element("SESSION")!.Element("TIMECODE"));
    }

    [TestMethod]
    public void Finding35IxmlRefusesDtdAndBrokenXml()
    {
        foreach (string content in new[] { "<!DOCTYPE BWF_XML [<!ENTITY x 'expanded'>]><BWF_XML><SCENE>&x;</SCENE></BWF_XML>", "<BWF_XML><SCENE>" })
        {
            var exception = Assert.ThrowsExactly<TargetInvocationException>(() => StampIxml(content));
            Assert.IsInstanceOfType<XmlException>(exception.InnerException);
        }
    }

    [TestMethod]
    public void Finding36InfoWritesUppercaseIdentifiersAndNullTerminatedText()
    {
        byte[] bytes = new InfoChunkData { Tags = new() { ["INAM"] = "Title" } }.Serialize();
        Assert.AreEqual("INFO", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.AreEqual(6U, BitConverter.ToUInt32(bytes, 8));
        CollectionAssert.AreEqual("Title\0"u8.ToArray(), bytes.AsSpan(12, 6).ToArray());
    }

    [TestMethod]
    public void Finding37InfoRejectsTruncatedSubchunk()
    {
        byte[] bytes = [.. "INFOINAM"u8, 100, 0, 0, 0, 65, 0];
        Assert.ThrowsExactly<InvalidDataException>(() => InfoChunkData.Parse(bytes));
    }

    [TestMethod]
    [DataRow("INFO", "list-type")]
    [DataRow("INFO", "fourcc")]
    [DataRow("INFO", "nonzero-padding")]
    [DataRow("adtl", "oversized-body")]
    [DataRow("adtl", "missing-padding")]
    [DataRow("adtl", "short-cue-id")]
    public void Finding60NestedChunksRejectMalformedHeadersAndBodies(string listType, string defect)
    {
        using var f = new WaveFixture();
        byte[] body = listType == "INFO" ? [.. "INFOINAM"u8, 3, 0, 0, 0, 72, 105, 0, 0]
            : [.. "adtllabl"u8, 7, 0, 0, 0, 3, 0, 0, 0, 72, 105, 0, 0];
        string Write(string name, byte[] data) => f.Write(name, Pcm16(1), chunks: [new() { ChunkId = "LIST", Data = data }]);
        CollectionAssert.AreEqual(body, Chunks(Write("valid.wav", body))["LIST"]);
        _ = WavMetadataChunk.ReadWavChunks(f.PathOf("valid.wav"));

        byte[] malformed = body.ToArray();
        switch (defect)
        {
            case "list-type": malformed[0] = 0x80; break;
            case "fourcc": malformed[4] = 0x80; break;
            case "nonzero-padding": malformed[^1] = 1; break;
            case "oversized-body": malformed.AsSpan(8, 4).Fill(0xff); break;
            case "missing-padding": malformed = malformed[..^1]; break;
            case "short-cue-id": malformed = malformed[..14]; malformed[8] = 2; break;
            default: throw new AssertFailedException(defect);
        }
        string path = Write("malformed.wav", malformed);
        Assert.IsFalse(body.AsSpan().SequenceEqual(malformed));
        CollectionAssert.AreEqual(malformed, Chunks(path)["LIST"]);
        Assert.ThrowsExactly<InvalidDataException>(() => WavMetadataChunk.ReadWavChunks(path));
    }

    [TestMethod]
    public async Task Finding60StandardAdtlIsTypedAndRestoredWithoutDuplicatingIt()
    {
        using var f = new WaveFixture();
        byte[] list = [.. "adtllabl"u8, 7, 0, 0, 0, 3, 0, 0, 0, 72, 105, 0, 0,
            .. "note"u8, 6, 0, 0, 0, 3, 0, 0, 0, 78, 0, .. "abcd"u8, 0, 0, 0, 0];
        string a = f.Write("a.wav", Pcm16(1), chunks: [new() { ChunkId = "LIST", Data = list }]);
        string b = f.Write("b.wav", Pcm16(2));
        var typed = WavMetadataChunk.ReadWavChunks(a).TypedMetadata.Adtl;
        Assert.IsNotNull(typed);
        Assert.AreEqual(3, typed.Labels.Single().Id);
        Assert.AreEqual("Hi", typed.Labels.Single().Text);
        Assert.AreEqual("N", typed.Notes.Single().Text);
        Assert.AreEqual("Hi", AdtlChunkData.Parse(list[4..])!.Labels.Single().Text, "Legacy bare adtl remains readable.");

        string merged = await MergeCommand.Run([a, b], outputPath: f.PathOf("merged.wav"));
        string[] restored = await SplitCommand.Run(merged, outputDirectory: f.PathOf("restored"));
        foreach (string path in new[] { merged, restored[0] })
        {
            var chunk = WavMetadataChunk.ReadWavChunks(path).Chunks.Single(chunk => chunk.ChunkId is "LIST" or "adtl");
            Assert.AreEqual("LIST", chunk.ChunkId);
            CollectionAssert.AreEqual(list, chunk.Data);
        }
    }

    [TestMethod]
    public async Task Finding60SplitRejectsMalformedStoredListBeforeCreatingOutputs()
    {
        using var f = new WaveFixture();
        byte[] list = [.. "adtllabl"u8, 3, 0, 0, 0, 7, 0, 0, 0];
        string input = f.Merged(new() { Index = 0, FileName = "a.wav", Channels = 1, DataLength = 4 },
            new() { Index = 1, FileName = "b.wav", Channels = 1, DataLength = 4, Chunks = [new() { ChunkId = "LIST", Data = list }] });
        var stored = System.Text.Json.JsonSerializer.Deserialize<TrackMetadata[]>(Chunks(input)["pmal"])!;
        CollectionAssert.AreEqual(list, stored[1].Chunks.Single().Data);
        string directory = f.PathOf("out");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => SplitCommand.Run(input, outputDirectory: directory));
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public async Task Finding60TypedOnlyPmalWritesStandardAdtlWithTerminatedText()
    {
        using var f = new WaveFixture();
        string input = f.Merged(new() { Index = 0, FileName = "a.wav", Channels = 1, DataLength = 4,
            Adtl = new() { Labels = [new() { Id = 3, Text = "Hi" }], Notes = [new() { Id = 3, Text = "N" }] } },
            new() { Index = 1, FileName = "b.wav", Channels = 1, DataLength = 4 });
        string[] restored = await SplitCommand.Run(input, outputDirectory: f.PathOf("out"));
        var chunks = Chunks(restored[0]);
        Assert.IsFalse(chunks.ContainsKey("adtl"));
        byte[] expected = [.. "adtllabl"u8, 7, 0, 0, 0, 3, 0, 0, 0, 72, 105, 0, 0,
            .. "note"u8, 6, 0, 0, 0, 3, 0, 0, 0, 78, 0];
        CollectionAssert.AreEqual(expected, chunks["LIST"]);
    }

    private static string StampIxml(string content)
    {
        MethodInfo method = typeof(MergeCommand).GetMethod("SetIxmlTimecode", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tc = new LtcDecoder.Timecode(Hours: 1, Minutes: 2, Seconds: 3, Frames: 4, DropFrame: false, Fps: 24, SampleOffset: 0);
        return (string)method.Invoke(null, [content, tc])!;
    }
}
