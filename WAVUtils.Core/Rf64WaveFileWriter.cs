using System.Buffers.Binary;
using NAudio.Wave;

namespace WAVUtils.Core;

internal sealed class Rf64WaveFileWriter : WaveFileWriter
{
    private readonly Stream _destination;
    private bool _finalized;

    internal Rf64WaveFileWriter(Stream destination, WavAudioFormat format)
        : base(destination, format, new WaveFileWriterOptions { EnableRf64 = true }) => _destination = destination;

    protected override void Dispose(bool disposing)
    {
        if (_finalized) return;
        _finalized = disposing;
        base.Dispose(disposing);
        if (!disposing || WaveFormat.Encoding == WaveFormatEncoding.Pcm || Length / WaveFormat.BlockAlign <= uint.MaxValue) return;

        // shortcut: NAudio 3.3.0 truncates fact; remove this patch when a stable version writes the RF64 sentinel.
        _destination.Position = 12;
        Span<byte> header = stackalloc byte[8];
        while (_destination.Length - _destination.Position >= header.Length)
        {
            _destination.ReadExactly(header);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (header[..4].SequenceEqual("data"u8)) break;
            if (header[..4].SequenceEqual("fact"u8) && size == 4)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(header, uint.MaxValue);
                _destination.Write(header[..4]);
                _destination.Flush();
                return;
            }
            _destination.Position += size + (size & 1L);
        }
        throw new InvalidDataException("RF64 writer did not emit the expected fact chunk.");
    }
}
