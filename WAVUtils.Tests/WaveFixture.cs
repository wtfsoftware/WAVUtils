using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace WAVUtils.Tests;

internal sealed class WaveFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "wavutils-test-" + Guid.NewGuid().ToString("N"));

    public WaveFixture() => Directory.CreateDirectory(Root);
    public string PathOf(string name) => Path.Combine(Root, name);

    // Reference bytes are authored here, independently of the production parsers/writers.
    public string Write(string name, byte[] audio, short channels = 1, short bits = 16,
        short encoding = 1, params WavChunkData[] chunks)
    {
        string path = PathOf(name);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write(encoding);
        writer.Write(channels);
        writer.Write(48000);
        writer.Write(48000 * channels * bits / 8);
        writer.Write((short)(channels * bits / 8));
        writer.Write(bits);
        WriteChunk(writer, "data", audio);
        foreach (var chunk in chunks) WriteChunk(writer, chunk.ChunkId, chunk.Data);
        stream.Position = 4;
        writer.Write(checked((uint)(stream.Length - 8)));
        return path;
    }

    public string Merged(params TrackMetadata[] tracks) => Write("crafted.wav",
        Pcm16(100, 300, 200, 400, 0, 500, 0, 600), channels: 2,
        chunks: [new() { ChunkId = "pmal", Data = JsonSerializer.SerializeToUtf8Bytes(tracks) }]);

    public static void WriteChunk(BinaryWriter writer, string id, byte[] data)
    {
        writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write(data.Length);
        writer.Write(data);
        if ((data.Length & 1) != 0) writer.Write((byte)0);
    }

    public static byte[] Pcm16(params short[] samples) => samples.SelectMany(BitConverter.GetBytes).ToArray();

    public static byte[] Bext(long timeReference = 172800000, short version = 1)
    {
        byte[] data = new byte[602];
        Encoding.ASCII.GetBytes("01:00:00").CopyTo(data, 330);
        BitConverter.GetBytes(timeReference).CopyTo(data, 338);
        BitConverter.GetBytes(version).CopyTo(data, 346);
        Enumerable.Range(1, 64).Select(n => (byte)n).ToArray().CopyTo(data, 348);
        return data;
    }

    public static Dictionary<string, byte[]> Chunks(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        stream.Position = 12;
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        while (stream.Position < stream.Length)
        {
            Assert.IsTrue(stream.Length - stream.Position >= 8, "Incomplete chunk header.");
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint size = reader.ReadUInt32();
            Assert.IsTrue(size <= stream.Length - stream.Position, $"Chunk {id} exceeds the file.");
            byte[] data = reader.ReadBytes(checked((int)size));
            if (id != "LIST") Assert.IsFalse(result.ContainsKey(id), $"Duplicate chunk {id}.");
            result[id] = data;
            if ((size & 1) != 0)
            {
                Assert.IsTrue(stream.Position < stream.Length, $"Missing alignment byte for {id}.");
                Assert.AreEqual((byte)0, reader.ReadByte(), $"Nonzero padding for {id}.");
            }
        }
        return result;
    }

    public static void AssertRiffSize(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Position = 4;
        Assert.AreEqual(reader.BaseStream.Length, reader.ReadUInt32() + 8L);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
