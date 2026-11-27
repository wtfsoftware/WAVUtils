using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace WAVUtils.Core;

/// <summary>
/// EBU Tech 3285 Broadcast Extension (bext) chunk data.
/// Used in professional/broadcast WAV files for metadata like originator, coding history, etc.
/// </summary>
public class BextChunkData
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("originator")]
    public string Originator { get; set; } = string.Empty;

    [JsonPropertyName("originatorReference")]
    public string OriginatorReference { get; set; } = string.Empty;

    [JsonPropertyName("originationDate")]
    public string OriginationDate { get; set; } = string.Empty;

    [JsonPropertyName("originationTime")]
    public string OriginationTime { get; set; } = string.Empty;

    [JsonPropertyName("timeReference")]
    public long TimeReference { get; set; }

    [JsonPropertyName("version")]
    public short Version { get; set; }

    [JsonPropertyName("umid")]
    public byte[] Umid { get; set; } = new byte[64];

    [JsonPropertyName("codingHistory")]
    public string CodingHistory { get; set; } = string.Empty;

    [JsonPropertyName("reserved")]
    public byte[] Reserved { get; set; } = new byte[190];

    // Includes the version-2 loudness fields and reserved bytes, unchanged on stamping.
    private const int DescriptionOff = 0, OriginatorOff = 256, OriginatorRefOff = 288,
        DateOff = 320, TimeOff = 330, TimeRefOff = 338, VersionOff = 346,
        UmidOff = 348, FixedSize = 602;

    public static BextChunkData? Parse(byte[] data)
    {
        if (data.Length < FixedSize) return null;

        var result = new BextChunkData();

        result.Description = ReadAsciiNullTerminated(data, DescriptionOff, 256).TrimEnd('\0');
        result.Originator = ReadAsciiNullTerminated(data, OriginatorOff, 32).TrimEnd('\0');
        result.OriginatorReference = ReadAsciiNullTerminated(data, OriginatorRefOff, 32).TrimEnd('\0');
        result.OriginationDate = ReadAsciiNullTerminated(data, DateOff, 10).TrimEnd('\0');
        result.OriginationTime = ReadAsciiNullTerminated(data, TimeOff, 8);
        result.TimeReference = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(TimeRefOff));
        result.Version = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(VersionOff));
        Array.Copy(data, UmidOff, result.Umid, 0, 64);
        result.Reserved = data.AsSpan(412, 190).ToArray();
        result.CodingHistory = Encoding.ASCII.GetString(data, FixedSize, data.Length - FixedSize);

        return result;
    }

    public byte[] Serialize()
    {
        byte[] codingHistoryBytes = Encoding.ASCII.GetBytes(CodingHistory);
        int totalSize = FixedSize + codingHistoryBytes.Length;
        byte[] buffer = new byte[totalSize];

        WriteAsciiNullTerminated(buffer, DescriptionOff, Description, 256);
        WriteAsciiNullTerminated(buffer, OriginatorOff, Originator, 32);
        WriteAsciiNullTerminated(buffer, OriginatorRefOff, OriginatorReference, 32);
        WriteAsciiNullTerminated(buffer, DateOff, OriginationDate, 10);
        WriteAsciiNullTerminated(buffer, TimeOff, OriginationTime, 8);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(TimeRefOff), TimeReference);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(VersionOff), Version);
        Umid.AsSpan(0, Math.Min(Umid.Length, 64)).CopyTo(buffer.AsSpan(UmidOff));
        Reserved.AsSpan(0, Math.Min(Reserved.Length, 190)).CopyTo(buffer.AsSpan(412));
        codingHistoryBytes.CopyTo(buffer, FixedSize);

        return buffer;
    }

    internal static byte[] StampTimeReference(byte[]? source, long timeReference, int sampleRate)
    {
        if (sampleRate <= 0 || timeReference < 0 || timeReference >= 86400L * sampleRate)
            throw new InvalidDataException("Time reference is outside the timecode day.");
        if (source != null && source.Length < FixedSize)
            throw new InvalidDataException("Truncated bext chunk.");
        byte[] data = source == null ? new BextChunkData { Originator = "WAVUtils" }.Serialize() : (byte[])source.Clone();
        long seconds = timeReference / sampleRate;
        string clock = FormattableString.Invariant($"{seconds / 3600:00}:{seconds % 3600 / 60:00}:{seconds % 60:00}");
        Encoding.ASCII.GetBytes(clock).CopyTo(data, TimeOff);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(TimeRefOff), timeReference);
        return data;
    }

    private static string ReadAsciiNullTerminated(byte[] data, int offset, int maxLength)
    {
        int end = Array.IndexOf(data, (byte)0, offset);
        if (end < 0 || end > offset + maxLength) end = offset + maxLength;
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    private static void WriteAsciiNullTerminated(byte[] buffer, int offset, string value, int maxLength)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        int len = Math.Min(bytes.Length, maxLength);
        bytes.AsSpan(0, len).CopyTo(buffer.AsSpan(offset, len));
        if (len < maxLength) buffer[offset + len] = 0;
    }
}

/// <summary>
/// iXML (Interchange XML) chunk data.
/// Contains XML metadata about the recording session, gain, calibration, etc.
/// </summary>
public class IxmlChunkData
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    public static IxmlChunkData? Parse(byte[] data)
    {
        if (data.Length == 0) return null;
        string xml = Encoding.UTF8.GetString(data);
        xml = xml.TrimEnd('\0');
        return new IxmlChunkData { Content = xml };
    }

    public byte[] Serialize()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Content);
        return bytes;
    }

    internal static string StampTimecode(string? content, LtcDecoder.Timecode tc,
        int? sampleRate = null, long? timeReference = null)
    {
        XDocument document;
        if (string.IsNullOrWhiteSpace(content))
            document = new XDocument(new XElement("BWF_XML"));
        else
        {
            using var text = new StringReader(content.TrimStart('\uFEFF').TrimEnd('\0'));
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 16 * 1024 * 1024
            });
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        XElement root = document.Root ?? throw new XmlException("Missing iXML root.");
        if (root.Name != "BWF_XML") throw new XmlException("Expected BWF_XML root.");
        if (root.Elements("SPEED").Count() > 1) throw new XmlException("Duplicate SPEED element.");
        XElement speed = root.Element("SPEED") ?? new XElement("SPEED");
        if (speed.Parent == null) root.Add(speed);
        int nominal = checked((int)Math.Round(tc.Fps));
        string rate = Math.Abs(tc.Fps - nominal * 1000.0 / 1001) < 0.005
            ? FormattableString.Invariant($"{nominal * 1000}/1001") : FormattableString.Invariant($"{nominal}/1");
        Set("TIMECODE_RATE", rate);
        Set("TIMECODE_FLAG", tc.DropFrame ? "DF" : "NDF");
        if (sampleRate.HasValue && timeReference.HasValue)
        {
            if (sampleRate <= 0 || timeReference < 0) throw new InvalidDataException("Invalid iXML timestamp.");
            Set("TIMESTAMP_SAMPLE_RATE", sampleRate.Value.ToString(CultureInfo.InvariantCulture));
            Set("TIMESTAMP_SAMPLES_SINCE_MIDNIGHT_HI", ((ulong)timeReference.Value >> 32).ToString(CultureInfo.InvariantCulture));
            Set("TIMESTAMP_SAMPLES_SINCE_MIDNIGHT_LO", ((uint)timeReference.Value).ToString(CultureInfo.InvariantCulture));
        }
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + document.ToString(SaveOptions.DisableFormatting);

        void Set(string name, string value)
        {
            speed.Elements(name).Remove();
            speed.Add(new XElement(name, value));
        }
    }
}

/// <summary>
/// RIFF INFO chunk data — key-value string pairs (ISFT, IART, INAM, ICRD, etc.)
/// </summary>
public class InfoChunkData
{
    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; } = new();

    public static InfoChunkData? Parse(byte[] data)
    {
        if (data.Length < 4) return null;

        string listId = WavChunkParsers.ReadListType(data);
        if (!listId.Equals("INFO", StringComparison.OrdinalIgnoreCase)) return null;

        var tags = new Dictionary<string, string>();
        int pos = 4;

        while (pos < data.Length)
        {
            var value = WavChunkParsers.ReadListChunk(data, ref pos, out string key);
            tags[key] = Encoding.ASCII.GetString(value).TrimEnd('\0');
        }

        if (tags.Count > 0)
        {
            return new InfoChunkData { Tags = tags };
        }
        return null;
    }

    public byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("INFO"u8);
        foreach (var kvp in Tags.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (kvp.Key.Length != 4 || kvp.Key.Any(c => c < 32 || c > 126))
                throw new InvalidDataException("INFO identifiers must be four ASCII characters.");
            writer.Write(Encoding.ASCII.GetBytes(kvp.Key));
            byte[] valueBytes = Encoding.ASCII.GetBytes(kvp.Value.TrimEnd('\0') + "\0");
            writer.Write(valueBytes.Length);
            writer.Write(valueBytes);
            if ((valueBytes.Length & 1) != 0) writer.Write((byte)0);
        }
        return stream.ToArray();
    }
}

/// <summary>
/// A cue point in a WAV file (cue chunk).
/// </summary>
public class CuePointData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("dataChunkId")]
    public int DataChunkId { get; set; }

    [JsonPropertyName("chunkStart")]
    public int ChunkStart { get; set; }

    [JsonPropertyName("blockStart")]
    public int BlockStart { get; set; }

    [JsonPropertyName("frameOffset")]
    public int FrameOffset { get; set; }
}

/// <summary>
/// Wrapper for cue chunk containing multiple cue points.
/// </summary>
public class CueChunkData
{
    [JsonPropertyName("points")]
    public List<CuePointData> Points { get; set; } = new();

    public static CueChunkData? Parse(byte[] data)
    {
        if (data.Length < 4) return null;

        int pos = 0;
        int cueCount = BitConverter.ToInt32(data, pos);
        pos += 4;

        var points = new List<CuePointData>();
        for (int i = 0; i < cueCount && pos + 24 <= data.Length; i++)
        {
            int id = BitConverter.ToInt32(data, pos); pos += 4;
            int position = BitConverter.ToInt32(data, pos); pos += 4;
            int dataChunkId = BitConverter.ToInt32(data, pos); pos += 4;
            int chunkStart = BitConverter.ToInt32(data, pos); pos += 4;
            int blockStart = BitConverter.ToInt32(data, pos); pos += 4;
            int frameOffset = BitConverter.ToInt32(data, pos); pos += 4;

            points.Add(new CuePointData
            {
                Id = id,
                Position = position,
                DataChunkId = dataChunkId,
                ChunkStart = chunkStart,
                BlockStart = blockStart,
                FrameOffset = frameOffset
            });
        }

        if (points.Count > 0)
        {
            return new CueChunkData { Points = points };
        }
        return null;
    }

    public byte[] Serialize()
    {
        int totalSize = 4 + Points.Count * 24;
        byte[] buffer = new byte[totalSize];
        BitConverter.GetBytes(Points.Count).CopyTo(buffer, 0);
        int pos = 4;
        foreach (var p in Points)
        {
            BitConverter.GetBytes(p.Id).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(p.Position).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(p.DataChunkId).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(p.ChunkStart).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(p.BlockStart).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(p.FrameOffset).CopyTo(buffer, pos); pos += 4;
        }
        return buffer;
    }
}

/// <summary>
/// smpl (sample loop) chunk data.
/// Contains loop information for a WAV file.
/// </summary>
public class SmplChunkData
{
    [JsonPropertyName("manufacturerId")]
    public int ManufacturerId { get; set; }

    [JsonPropertyName("productId")]
    public int ProductId { get; set; }

    [JsonPropertyName("samplePeriod")]
    public int SamplePeriod { get; set; }

    [JsonPropertyName("midiUnityNotes")]
    public int MidiUnityNotes { get; set; }

    [JsonPropertyName("midiPitchFraction")]
    public int MidiPitchFraction { get; set; }

    [JsonPropertyName("smpteFormat")]
    public int SmpteFormat { get; set; }

    [JsonPropertyName("smpteOffset")]
    public int SmpteOffset { get; set; }

    [JsonPropertyName("loopCount")]
    public int LoopCount { get; set; }

    [JsonPropertyName("loops")]
    public List<SmplLoopData> Loops { get; set; } = new();

    [JsonPropertyName("sampleData")]
    public int SampleData { get; set; }

    public static SmplChunkData? Parse(byte[] data)
    {
        if (data.Length < 36) return null;

        var result = new SmplChunkData
        {
            ManufacturerId = BitConverter.ToInt32(data, 0),
            ProductId = BitConverter.ToInt32(data, 4),
            SamplePeriod = BitConverter.ToInt32(data, 8),
            MidiUnityNotes = BitConverter.ToInt32(data, 12),
            MidiPitchFraction = BitConverter.ToInt32(data, 16),
            SmpteFormat = BitConverter.ToInt32(data, 20),
            SmpteOffset = BitConverter.ToInt32(data, 24),
            LoopCount = BitConverter.ToInt32(data, 28),
            SampleData = BitConverter.ToInt32(data, 32),
        };

        int pos = 36;
        for (int i = 0; i < result.LoopCount && pos + 24 <= data.Length; i++)
        {
            int id = BitConverter.ToInt32(data, pos); pos += 4;
            int type = BitConverter.ToInt32(data, pos); pos += 4;
            int start = BitConverter.ToInt32(data, pos); pos += 4;
            int end = BitConverter.ToInt32(data, pos); pos += 4;
            int fraction = BitConverter.ToInt32(data, pos); pos += 4;
            int playCount = BitConverter.ToInt32(data, pos); pos += 4;

            result.Loops.Add(new SmplLoopData
            {
                Id = id,
                Type = type,
                Start = start,
                End = end,
                Fraction = fraction,
                PlayCount = playCount
            });
        }

        if (result.LoopCount > 0 || result.SampleData > 0)
        {
            return result;
        }
        return null;
    }

    public byte[] Serialize()
    {
        int loopsSize = Loops.Count * 24;
        int dataPadding = SampleData % 2 != 0 ? 1 : 0;
        int totalSize = 36 + loopsSize + SampleData + dataPadding;

        byte[] buffer = new byte[totalSize];
        BitConverter.GetBytes(ManufacturerId).CopyTo(buffer, 0);
        BitConverter.GetBytes(ProductId).CopyTo(buffer, 4);
        BitConverter.GetBytes(SamplePeriod).CopyTo(buffer, 8);
        BitConverter.GetBytes(MidiUnityNotes).CopyTo(buffer, 12);
        BitConverter.GetBytes(MidiPitchFraction).CopyTo(buffer, 16);
        BitConverter.GetBytes(SmpteFormat).CopyTo(buffer, 20);
        BitConverter.GetBytes(SmpteOffset).CopyTo(buffer, 24);
        BitConverter.GetBytes(LoopCount).CopyTo(buffer, 28);
        BitConverter.GetBytes(SampleData).CopyTo(buffer, 32);

        int pos = 36;
        foreach (var loop in Loops)
        {
            BitConverter.GetBytes(loop.Id).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(loop.Type).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(loop.Start).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(loop.End).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(loop.Fraction).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(loop.PlayCount).CopyTo(buffer, pos); pos += 4;
        }

        return buffer;
    }
}

/// <summary>
/// A single loop within the smpl chunk.
/// </summary>
public class SmplLoopData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }

    [JsonPropertyName("fraction")]
    public int Fraction { get; set; }

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }
}

/// <summary>
/// Labels and notes linked to cue points in an Associated Data List.
/// </summary>
public class AdtlChunkData
{
    [JsonPropertyName("labels")]
    public List<AdtlLabelData> Labels { get; set; } = new();

    [JsonPropertyName("notes")]
    public List<AdtlNoteData> Notes { get; set; } = new();

    public static AdtlChunkData? Parse(byte[] data) => ParseSegments(data);

    internal static AdtlChunkData? ParseSegments(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return null;

        var result = new AdtlChunkData();
        int pos = 0;

        while (pos < data.Length)
        {
            var segment = WavChunkParsers.ReadListChunk(data, ref pos, out string segmentId, adtl: true);
            switch (segmentId)
            {
                case "labl":
                case "labL":
                    result.Labels.Add(new AdtlLabelData
                    {
                        Id = BinaryPrimitives.ReadInt32LittleEndian(segment),
                        Text = Encoding.ASCII.GetString(segment[4..]).TrimEnd('\0')
                    });
                    break;

                case "note":
                case "Note":
                    result.Notes.Add(new AdtlNoteData
                    {
                        Id = BinaryPrimitives.ReadInt32LittleEndian(segment),
                        Text = Encoding.ASCII.GetString(segment[4..]).TrimEnd('\0')
                    });
                    break;
            }
        }

        if (result.Labels.Count > 0 || result.Notes.Count > 0)
        {
            return result;
        }
        return null;
    }

    public byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        foreach (var label in Labels)
            WriteSegment("labl"u8, label.Id, label.Text);
        foreach (var note in Notes)
            WriteSegment("note"u8, note.Id, note.Text);
        return stream.ToArray();

        void WriteSegment(ReadOnlySpan<byte> type, int id, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text.TrimEnd('\0') + "\0");
            writer.Write(type);
            writer.Write(checked(4 + bytes.Length));
            writer.Write(id);
            writer.Write(bytes);
            if ((bytes.Length & 1) != 0) writer.Write((byte)0);
        }
    }
}

/// <summary>
/// A label segment within the adtl chunk.
/// </summary>
public class AdtlLabelData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// A note segment within the adtl chunk.
/// </summary>
public class AdtlNoteData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Holds all parsed metadata from a WAV file's chunks.
/// </summary>
public class WavFileMetadata
{
    [JsonPropertyName("bext")]
    public BextChunkData? Bext { get; set; }

    [JsonPropertyName("ixml")]
    public IxmlChunkData? Ixml { get; set; }

    [JsonPropertyName("info")]
    public InfoChunkData? Info { get; set; }

    [JsonPropertyName("cue")]
    public CueChunkData? Cue { get; set; }

    [JsonPropertyName("smpl")]
    public SmplChunkData? Smpl { get; set; }

    [JsonPropertyName("adtl")]
    public AdtlChunkData? Adtl { get; set; }

    /// <summary>
    /// List of all other chunks that were not recognized as known types.
    /// Stored as raw binary data for lossless preservation.
    /// </summary>
    [JsonPropertyName("otherChunks")]
    public List<WavChunkData> OtherChunks { get; set; } = new();
}

/// <summary>
/// Static utilities for parsing and serializing known WAV chunk types.
/// </summary>
public static class WavChunkParsers
{
    internal static string ReadListType(ReadOnlySpan<byte> data) => data.Length >= 4
        ? WavMetadataChunk.ReadChunkId(data[..4]) : throw new InvalidDataException("Truncated LIST type.");

    internal static ReadOnlySpan<byte> ReadListChunk(ReadOnlySpan<byte> data, scoped ref int pos, out string id, bool adtl = false)
    {
        if (data.Length - pos < 8) throw new InvalidDataException("Truncated LIST subchunk header.");
        id = WavMetadataChunk.ReadChunkId(data.Slice(pos, 4));
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 4)..]);
        pos += 8;
        int pad = (int)(size & 1);
        if ((long)size + pad > data.Length - pos) throw new InvalidDataException("Truncated LIST subchunk or padding.");
        if (adtl && size < 4 && id is "labl" or "labL" or "note" or "Note")
            throw new InvalidDataException("Truncated adtl cue identifier.");
        var body = data.Slice(pos, (int)size);
        pos += (int)size;
        if (pad != 0 && data[pos++] != 0) throw new InvalidDataException("Nonzero LIST subchunk padding.");
        return body;
    }

    internal static void ValidateList(WavChunkData chunk)
    {
        ReadOnlySpan<byte> data = chunk.Data;
        string type = chunk.ChunkId;
        if (type.Equals("LIST", StringComparison.OrdinalIgnoreCase))
        {
            type = ReadListType(data);
            data = data[4..];
            if (!type.Equals("INFO", StringComparison.OrdinalIgnoreCase) && !type.Equals("adtl", StringComparison.OrdinalIgnoreCase)) return;
        }
        else if (!type.Equals("adtl", StringComparison.OrdinalIgnoreCase)) return;

        bool adtl = type.Equals("adtl", StringComparison.OrdinalIgnoreCase);
        int pos = 0;
        while (pos < data.Length) _ = ReadListChunk(data, ref pos, out _, adtl);
    }

    /// <summary>
    /// Parse a WAV chunk by its ID into typed metadata.
    /// Unknown chunk types are returned as raw WavChunkData.
    /// </summary>
    public static WavChunkData[] ParseKnownChunks(IEnumerable<WavChunkData> chunks)
    {
        var otherChunks = new List<WavChunkData>();

        foreach (var chunk in chunks)
        {
            switch (chunk.ChunkId.ToLowerInvariant())
            {
                case "bext":
                    otherChunks.Add(chunk);
                    break;

                case "ixml":
                    otherChunks.Add(chunk);
                    break;

                case "list":
                    otherChunks.Add(chunk);
                    break;

                case "cue ":
                    otherChunks.Add(chunk);
                    break;

                case "smpl":
                    otherChunks.Add(chunk);
                    break;

                case "adtl":
                    otherChunks.Add(chunk);
                    break;

                default:
                    otherChunks.Add(chunk);
                    break;
            }
        }

        return otherChunks.ToArray();
    }

    /// <summary>
    /// Extract typed metadata from raw chunks.
    /// Returns null for chunk types that are not present.
    /// </summary>
    public static WavFileMetadata ExtractTypedMetadata(IEnumerable<WavChunkData> chunks)
    {
        var metadata = new WavFileMetadata();

        foreach (var chunk in chunks)
        {
            switch (chunk.ChunkId.ToLowerInvariant())
            {
                case "bext":
                    metadata.Bext = BextChunkData.Parse(chunk.Data);
                    break;

                case "ixml":
                    metadata.Ixml = IxmlChunkData.Parse(chunk.Data);
                    break;

                case "list":
                    string type = ReadListType(chunk.Data);
                    if (type.Equals("INFO", StringComparison.OrdinalIgnoreCase) && InfoChunkData.Parse(chunk.Data) is { } info)
                        metadata.Info = info;
                    else if (type.Equals("adtl", StringComparison.OrdinalIgnoreCase) && AdtlChunkData.ParseSegments(chunk.Data.AsSpan(4)) is { } adtl)
                        metadata.Adtl = adtl;
                    else metadata.OtherChunks.Add(chunk);
                    break;

                case "cue ":
                    metadata.Cue = CueChunkData.Parse(chunk.Data);
                    break;

                case "smpl":
                    metadata.Smpl = SmplChunkData.Parse(chunk.Data);
                    break;

                case "adtl":
                    metadata.Adtl = AdtlChunkData.Parse(chunk.Data);
                    break;

                default:
                    metadata.OtherChunks.Add(chunk);
                    break;
            }
        }

        return metadata;
    }

    /// <summary>
    /// Serialize typed metadata back to raw chunk bytes.
    /// Only known chunk types are serialized; unknown chunks are passed through.
    /// </summary>
    public static WavChunkData[] SerializeTypedMetadata(WavFileMetadata metadata, List<WavChunkData> rawChunks)
    {
        var result = new List<WavChunkData>();

        if (metadata.Bext != null && !rawChunks.Any(c => c.ChunkId.Equals("bext", StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new WavChunkData
            {
                ChunkId = "bext",
                Data = metadata.Bext.Serialize()
            });
        }

        if (metadata.Ixml != null && !rawChunks.Any(c => c.ChunkId.Equals("iXML", StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new WavChunkData
            {
                ChunkId = "iXML",
                Data = metadata.Ixml.Serialize()
            });
        }

        if (metadata.Info != null && !rawChunks.Any(c => IsList(c, "INFO")))
        {
            result.Add(new WavChunkData
            {
                ChunkId = "LIST",
                Data = metadata.Info.Serialize()
            });
        }

        if (metadata.Cue != null && !rawChunks.Any(c => c.ChunkId.Equals("cue ", StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new WavChunkData
            {
                ChunkId = "cue ",
                Data = metadata.Cue.Serialize()
            });
        }

        if (metadata.Smpl != null && !rawChunks.Any(c => c.ChunkId.Equals("smpl", StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new WavChunkData
            {
                ChunkId = "smpl",
                Data = metadata.Smpl.Serialize()
            });
        }

        if (metadata.Adtl != null && !rawChunks.Any(c => c.ChunkId.Equals("adtl", StringComparison.OrdinalIgnoreCase) || IsList(c, "adtl")))
        {
            result.Add(new WavChunkData
            {
                ChunkId = "LIST",
                Data = [.. "adtl"u8, .. metadata.Adtl.Serialize()]
            });
        }

        result.AddRange(rawChunks);

        return result.ToArray();
    }

    private static bool IsList(WavChunkData chunk, string type) =>
        chunk.ChunkId.Equals("LIST", StringComparison.OrdinalIgnoreCase) && ReadListType(chunk.Data).Equals(type, StringComparison.OrdinalIgnoreCase);
}
