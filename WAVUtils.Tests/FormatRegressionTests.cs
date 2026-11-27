using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;
using static WAVUtils.Tests.WaveFixture;

namespace WAVUtils.Tests;

[TestClass]
public sealed class FormatRegressionTests
{
    [TestMethod]
    public async Task Finding33ExtensiblePcmKeepsValidBitsAndOriginalChannelMasks()
    {
        using var f = new WaveFixture();
        byte[] mono = Format(channels: 1, mask: 4), stereo = Format(channels: 2, mask: 3);
        string a = WriteFormat(f, "a.wav", mono, [0, 0, 0, 64, 0, 0, 0, 32]);
        string b = WriteFormat(f, "b.wav", stereo, [0, 0, 0, 16, 0, 0, 0, 8]);
        string merged = await MergeCommand.Run([a, b], outputPath: f.PathOf("merged.wav"));
        byte[] format = ReadFormat(merged);
        Assert.AreEqual(40, format.Length);
        Assert.AreEqual((ushort)24, BitConverter.ToUInt16(format, 18));
        Assert.AreEqual(0U, BitConverter.ToUInt32(format, 20), "Concatenated tracks are not a speaker layout.");
        string[] split = await SplitCommand.Run(merged, outputDirectory: f.PathOf("out"));
        CollectionAssert.AreEqual(mono, Chunks(split[0])["fmt "]);
        CollectionAssert.AreEqual(stereo, Chunks(split[1])["fmt "]);
        CollectionAssert.AreEqual(Chunks(a)["data"], Chunks(split[0])["data"]);
        CollectionAssert.AreEqual(Chunks(b)["data"], Chunks(split[1])["data"]);
    }

    [TestMethod]
    public async Task Finding56SplitPadsOddFormatExtensionsWithoutChangingTheirBytes()
    {
        using var f = new WaveFixture();
        byte[] oddFormat = [.. Format(channels: 1, mask: 4), 0x7f];
        oddFormat[16] = 23; // cbSize includes the vendor extension byte.
        byte[] audio = [0, 0, 0, 64, 0, 0, 0, 32];
        string a = WriteFormat(f, "a.wav", oddFormat, audio);
        string b = WriteFormat(f, "b.wav", Format(channels: 1, mask: 4), audio);
        CollectionAssert.AreEqual(oddFormat, Chunks(a)["fmt "]);

        string merged = await MergeCommand.Run([a, b], outputPath: f.PathOf("merged.wav"));
        string[] split = await SplitCommand.Run(merged, outputDirectory: f.PathOf("out"));
        AssertRiffSize(split[0]);
        var restored = Chunks(split[0]);
        CollectionAssert.AreEqual(oddFormat, restored["fmt "]);
        CollectionAssert.AreEqual(audio, restored["data"]);
    }

    [TestMethod]
    public async Task Finding33CompressedEncodingIsRejectedBeforeOutput()
    {
        using var f = new WaveFixture();
        string output = f.PathOf("out.wav");
        await Assert.ThrowsAsync<ArgumentException>(() => MergeCommand.Run(
            [f.Write("a.wav", [1, 2], bits: 8, encoding: 6), f.Write("b.wav", [3, 4], bits: 8, encoding: 6)], outputPath: output));
        Assert.IsFalse(File.Exists(output));
    }

    [TestMethod]
    public void Finding37AnOddChunkMustHaveItsPaddingByte()
    {
        using var f = new WaveFixture();
        string path = f.Write("bad.wav", Pcm16(1), chunks: [new() { ChunkId = "JUNK", Data = [1] }]);
        byte[] bytes = File.ReadAllBytes(path)[..^1];
        BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
        File.WriteAllBytes(path, bytes);
        Assert.ThrowsExactly<InvalidDataException>(() => WavMetadataChunk.ReadWavChunks(path));
    }

    internal static byte[] Format(short channels, uint mask)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0xfffe);
        writer.Write(channels);
        writer.Write(48000);
        writer.Write(48000 * channels * 4);
        writer.Write((short)(channels * 4));
        writer.Write((short)32);
        writer.Write((short)22);
        writer.Write((short)24);
        writer.Write(mask);
        writer.Write(new byte[] { 1, 0, 0, 0, 0, 0, 16, 0, 128, 0, 0, 170, 0, 56, 155, 113 });
        return stream.ToArray();
    }

    private static byte[] ReadFormat(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Position = 12;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint length = reader.ReadUInt32();
            if (id == "fmt ") return reader.ReadBytes(checked((int)length));
            reader.BaseStream.Position += length + (length & 1);
        }
        throw new InvalidDataException("Missing fmt chunk in reference check.");
    }

    internal static string WriteFormat(WaveFixture f, string name, byte[] format, byte[] audio)
    {
        string path = f.PathOf(name);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);
        WriteChunk(writer, "fmt ", format);
        WriteChunk(writer, "data", audio);
        stream.Position = 4;
        writer.Write((uint)(stream.Length - 8));
        return path;
    }
}
