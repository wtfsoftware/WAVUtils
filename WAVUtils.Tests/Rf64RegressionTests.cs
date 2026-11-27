using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;
using WAVUtils.Core;

namespace WAVUtils.Tests;

[TestClass]
public sealed class Rf64RegressionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Finding59RejectsNonAsciiChunkIdentifiersBeforeDecoding(bool rf64)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(rf64 ? "RF64"u8 : "RIFF"u8);
        writer.Write(0U);
        writer.Write("WAVE"u8);
        long tableId = 0;
        if (rf64)
        {
            writer.Write("ds64"u8);
            writer.Write(40U);
            writer.Write(new byte[24]); // No placeholder for RIFF, data, or fact.
            writer.Write(1U);
            tableId = stream.Position;
            writer.Write("?abc"u8);
            writer.Write(1UL);
        }
        WaveFixture.WriteChunk(writer, "fmt ", [1, 0, 1, 0, 128, 187, 0, 0, 0, 119, 1, 0, 2, 0, 16, 0]);
        WaveFixture.WriteChunk(writer, "data", WaveFixture.Pcm16(1));
        long chunkId = stream.Position;
        writer.Write("?abc"u8);
        writer.Write(rf64 ? uint.MaxValue : 1U);
        writer.Write((byte)17);
        writer.Write((byte)0);
        stream.Position = 4;
        writer.Write(checked((uint)(stream.Length - 8)));
        Assert.AreEqual("?abc", WavMetadataChunk.ReadFile(stream).Chunks.Single().ChunkId);

        long invalidId = rf64 ? tableId : chunkId;
        stream.Position = invalidId;
        writer.Write((byte)0x80);
        stream.Position = invalidId;
        Assert.AreEqual(0x80, stream.ReadByte(), "The malformed FourCC must reach the reader.");
        Assert.ThrowsExactly<InvalidDataException>(() => WavMetadataChunk.ReadFile(stream));
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public void Finding53Rf64UsesDs64OnlyForPlaceholderLengths(bool largeRiff, bool largeData)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RF64"u8);
        writer.Write(largeRiff ? uint.MaxValue : 76U);
        writer.Write("WAVEds64"u8);
        writer.Write(28U);
        writer.Write(largeRiff ? 76UL : ulong.MaxValue);
        writer.Write(largeData ? 4UL : ulong.MaxValue);
        writer.Write(ulong.MaxValue); // No fact chunk, so this value is unused.
        writer.Write(0U);
        WaveFixture.WriteChunk(writer, "fmt ", [1, 0, 1, 0, 128, 187, 0, 0, 0, 119, 1, 0, 2, 0, 16, 0]);
        writer.Write("data"u8);
        writer.Write(largeData ? uint.MaxValue : 4U);
        writer.Write(WaveFixture.Pcm16(1, 2));
        Assert.AreEqual(84L, stream.Length);
        Assert.AreEqual(4L, WavMetadataChunk.ReadFile(stream).DataLength);
    }

    [TestMethod]
    [DataRow(2U, ulong.MaxValue, true)]
    [DataRow(uint.MaxValue, 2UL, true)]
    [DataRow(uint.MaxValue, 3UL, false)]
    [DataRow(3U, 2UL, false)]
    public void Finding53Rf64FactUsesItsOwnCountUnlessItIsAPlaceholder(uint factCount, ulong ds64Count, bool valid)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RF64"u8);
        writer.Write(uint.MaxValue);
        writer.Write("WAVEds64"u8);
        writer.Write(28U);
        writer.Write(92UL);
        writer.Write(8UL);
        writer.Write(ds64Count);
        writer.Write(0U);
        WaveFixture.WriteChunk(writer, "fmt ", [3, 0, 1, 0, 128, 187, 0, 0, 0, 238, 2, 0, 4, 0, 32, 0]);
        WaveFixture.WriteChunk(writer, "fact", BitConverter.GetBytes(factCount));
        writer.Write("data"u8);
        writer.Write(uint.MaxValue);
        writer.Write(new byte[8]);
        Assert.AreEqual(100L, stream.Length);
        if (valid) Assert.AreEqual(8L, WavMetadataChunk.ReadFile(stream).DataLength);
        else Assert.ThrowsExactly<InvalidDataException>(() => WavMetadataChunk.ReadFile(stream));
    }

    [TestMethod]
    [DataRow("merge")]
    [DataRow("tc")]
    [DataRow("donor")]
    public async Task Finding53LtcUsesTheValidatedRf64DataRange(string operation)
    {
        using var f = new WaveFixture();
        string ltc = f.PathOf("ltc.wav");
        byte[] samples = LtcRegressionTests.Audio();
        using (var writer = new BinaryWriter(File.Create(ltc)))
        {
            writer.Write("RF64"u8);
            writer.Write(682U + (uint)samples.Length);
            writer.Write("WAVEds64"u8);
            writer.Write(28U);
            writer.Write(new byte[28]); // No size or sample-count placeholders are used.
            WaveFixture.WriteChunk(writer, "fmt ", [1, 0, 1, 0, 128, 187, 0, 0, 0, 119, 1, 0, 2, 0, 16, 0]);
            WaveFixture.WriteChunk(writer, "bext", WaveFixture.Bext());
            WaveFixture.WriteChunk(writer, "data", samples);
            Assert.AreEqual(690L + samples.Length, writer.BaseStream.Length);
        }
        string first = f.Write("first.wav", WaveFixture.Pcm16(0, 0, 0, 0), channels: 2,
            chunks: [new() { ChunkId = "bext", Data = WaveFixture.Bext() }]);
        string result = operation switch
        {
            "merge" => await MergeCommand.Run([first, ltc], f.PathOf("merged.wav")),
            "donor" => first,
            _ => ltc
        };
        if (operation == "tc") TcCommand.Run([ltc]);
        if (operation == "donor") TcCommand.Run([first], tcSource: ltc);
        long reference = BinaryPrimitives.ReadInt64LittleEndian(WaveFixture.Chunks(result)["bext"].AsSpan(338));
        Assert.IsTrue(Math.Abs(reference - LtcRegressionTests.Reference) <= 2, $"Wrong start reference: {reference}.");
    }

    [TestMethod]
    public void Finding41Rf64WriterPreservesLengthsAboveFourGiBAndMetadataAfterAudio()
    {
        byte[] format = [1, 0, 1, 0, 128, 187, 0, 0, 0, 119, 1, 0, 2, 0, 16, 0];
        long audioLength = (long)uint.MaxValue + 1 + 4096;
        using var output = new SparseStream();
        using (var writer = new Rf64WaveFileWriter(output, new WavAudioFormat(format)))
        {
            writer.AddChunk("note", [11, 22, 33], ChunkPosition.AfterData);
            byte[] zeros = new byte[4 * 1024 * 1024];
            for (long written = 0; written < audioLength;)
            {
                int count = (int)Math.Min(zeros.Length, audioLength - written);
                writer.Write(zeros, 0, count);
                written += count;
            }
        }
        output.Position = 0;
        using var reader = new BinaryReader(output, Encoding.ASCII, leaveOpen: true);
        CollectionAssert.AreEqual("RF64"u8.ToArray(), reader.ReadBytes(4));
        Assert.AreEqual(uint.MaxValue, reader.ReadUInt32());
        CollectionAssert.AreEqual("WAVEds64"u8.ToArray(), reader.ReadBytes(8));
        Assert.AreEqual(28U, reader.ReadUInt32());
        Assert.AreEqual((ulong)(output.Length - 8), reader.ReadUInt64());
        Assert.AreEqual((ulong)audioLength, reader.ReadUInt64());
        Assert.AreEqual((ulong)(audioLength / 2), reader.ReadUInt64());
        Assert.AreEqual(0U, reader.ReadUInt32());
        CollectionAssert.AreEqual("fmt "u8.ToArray(), reader.ReadBytes(4));
        Assert.AreEqual(16U, reader.ReadUInt32());
        CollectionAssert.AreEqual(format, reader.ReadBytes(16));
        CollectionAssert.AreEqual("data"u8.ToArray(), reader.ReadBytes(4));
        Assert.AreEqual(uint.MaxValue, reader.ReadUInt32());
        output.Position += audioLength;
        CollectionAssert.AreEqual("note"u8.ToArray(), reader.ReadBytes(4));
        Assert.AreEqual(3U, reader.ReadUInt32());
        CollectionAssert.AreEqual(new byte[] { 11, 22, 33, 0 }, reader.ReadBytes(4));
        Assert.AreEqual(output.Length, output.Position);
        var parsed = WavMetadataChunk.ReadFile(output);
        Assert.AreEqual(audioLength, parsed.DataLength);
        CollectionAssert.AreEqual(new byte[] { 11, 22, 33 }, parsed.Chunks.Single().Data);
        Assert.IsTrue(output.StoredBytes < 16384, "The test stream must not allocate the virtual audio extent.");
    }

    [TestMethod]
    [DataRow((long)uint.MaxValue - 1, uint.MaxValue - 1)]
    [DataRow((long)uint.MaxValue + 513, uint.MaxValue)]
    public void Finding54Rf64FactUsesThePlaceholderWhenFrameCountOverflows(long frames, uint expectedFact)
    {
        byte[] format = [3, 0, 1, 0, 128, 187, 0, 0, 0, 238, 2, 0, 4, 0, 32, 0];
        using var output = new SparseStream();
        using (var writer = new Rf64WaveFileWriter(output, new WavAudioFormat(format)))
        {
            writer.AddChunk("note", [11, 22, 33], ChunkPosition.BeforeData);
            byte[] zeros = new byte[4 * 1024 * 1024];
            for (long bytes = frames * 4; bytes > 0; bytes -= zeros.Length)
                writer.Write(zeros, 0, (int)Math.Min(bytes, zeros.Length));
        }
        output.Position = 36;
        using var reader = new BinaryReader(output, Encoding.ASCII, leaveOpen: true);
        Assert.AreEqual((ulong)frames, reader.ReadUInt64());
        output.Position = 72;
        CollectionAssert.AreEqual("note\x03\0\0\0\x0b\x16\x21\0"u8.ToArray(), reader.ReadBytes(12));
        CollectionAssert.AreEqual("fact"u8.ToArray(), reader.ReadBytes(4));
        Assert.AreEqual(4U, reader.ReadUInt32());
        Assert.AreEqual(expectedFact, reader.ReadUInt32());
        Assert.AreEqual(frames * 4, WavMetadataChunk.ReadFile(output).DataLength);
    }

    // Retains framing writes, while large writes are independently checked to be zero-filled holes.
    private sealed class SparseStream : Stream
    {
        private readonly List<(long Position, byte[] Data)> _writes = [];
        private long _length;
        public int StoredBytes => _writes.Sum(write => write.Data.Length);
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get; set; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => Position = checked((origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => Position,
            SeekOrigin.End => Length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        }) + offset);
        public override void SetLength(long value) => _length = value;
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > 4096) Assert.IsTrue(buffer.IndexOfAnyExcept((byte)0) < 0);
            else _writes.Add((Position, buffer.ToArray()));
            Position = checked(Position + buffer.Length);
            _length = Math.Max(_length, Position);
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int count = (int)Math.Min(buffer.Length, Length - Position);
            buffer[..count].Clear();
            foreach (var write in _writes)
            {
                long start = Math.Max(Position, write.Position), end = Math.Min(Position + count, write.Position + write.Data.Length);
                if (start < end) write.Data.AsSpan((int)(start - write.Position), (int)(end - start)).CopyTo(buffer[(int)(start - Position)..]);
            }
            Position += count;
            return count;
        }
    }
}
