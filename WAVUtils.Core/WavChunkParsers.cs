using System.Text;
using System.Text.Json.Serialization;

namespace WAVUtils.Core;

/// <summary>
/// AES3d Broadcast Extension (bEXT) chunk data.
/// Used in professional/broadcast WAV files for metadata like originator, coding history, etc.
/// </summary>
public class BextChunkData
{
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
    public byte[] Umid { get; set; } = new byte[12];

    [JsonPropertyName("codingHistory")]
    public string CodingHistory { get; set; } = string.Empty;

    public static BextChunkData? Parse(byte[] data)
    {
        if (data.Length < 602) return null;

        var result = new BextChunkData();

        result.Originator = ReadAsciiNullTerminated(data, 0, 180).TrimEnd('\0');
        result.OriginatorReference = ReadAsciiNullTerminated(data, 180, 30).TrimEnd('\0');
        result.OriginationDate = ReadAsciiNullTerminated(data, 210, 32).TrimEnd('\0');
        result.OriginationTime = ReadAsciiNullTerminated(data, 242, 32).TrimEnd('\0');
        result.TimeReference = BitConverter.ToInt64(data, 274);
        result.Version = BitConverter.ToInt16(data, 282);
        Array.Copy(data, 284, result.Umid, 0, 12);
        result.CodingHistory = ReadAsciiNullTerminated(data, 300, data.Length - 300).TrimEnd('\0');

        return result;
    }

    public byte[] Serialize()
    {
        int codingHistoryMax = Math.Max(1, 602 - 300);
        byte[] codingHistoryBytes = Encoding.ASCII.GetBytes(CodingHistory.Length > codingHistoryMax ? CodingHistory[..codingHistoryMax] : CodingHistory);
        int totalSize = 300 + codingHistoryBytes.Length;
        totalSize = (totalSize % 2 == 0) ? totalSize : totalSize + 1;

        byte[] buffer = new byte[totalSize];

        WriteAsciiNullTerminated(buffer, 0, Originator, 180);
        WriteAsciiNullTerminated(buffer, 180, OriginatorReference, 30);
        WriteAsciiNullTerminated(buffer, 210, OriginationDate, 32);
        WriteAsciiNullTerminated(buffer, 242, OriginationTime, 32);
        BitConverter.GetBytes(TimeReference).CopyTo(buffer, 274);
        BitConverter.GetBytes(Version).CopyTo(buffer, 282);
        Umid.CopyTo(buffer, 284);
        codingHistoryBytes.CopyTo(buffer, 300);

        return buffer;
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
        if (data.Length < 8) return null;

        string listId = Encoding.ASCII.GetString(data, 0, 4);
        if (listId != "info") return null;

        var tags = new Dictionary<string, string>();
        int pos = 4;

        while (pos + 8 <= data.Length)
        {
            string key = Encoding.ASCII.GetString(data, pos, 4);
            int len = BitConverter.ToInt32(data, pos + 4);

            pos += 8;
            if (pos + len > data.Length) break;

            string value = Encoding.ASCII.GetString(data, pos, len);
            value = value.TrimEnd('\0');
            tags[key] = value;

            pos += len;
            if (len % 2 != 0) pos++;
        }

        if (tags.Count > 0)
        {
            return new InfoChunkData { Tags = tags };
        }
        return null;
    }

    public byte[] Serialize()
    {
        int totalSize = 4; // "info"
        foreach (var kvp in Tags)
        {
            totalSize += 8; // key + len
            totalSize += kvp.Value.Length;
            if (kvp.Value.Length % 2 != 0) totalSize++;
        }

        byte[] buffer = new byte[totalSize];
        Encoding.ASCII.GetBytes("info").CopyTo(buffer, 0);
        int pos = 4;

        foreach (var kvp in Tags)
        {
            Encoding.ASCII.GetBytes(kvp.Key).CopyTo(buffer, pos);
            pos += 4;
            byte[] valueBytes = Encoding.ASCII.GetBytes(kvp.Value);
            BitConverter.GetBytes(valueBytes.Length).CopyTo(buffer, pos);
            pos += 4;
            valueBytes.CopyTo(buffer, pos);
            pos += valueBytes.Length;
            if (valueBytes.Length % 2 != 0) pos++;
        }

        return buffer;
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
/// adtl (Audio Data Link List) chunk data.
/// Contains labelf, note, LNAM, etc. — metadata linked to cue points.
/// </summary>
public class AdtlChunkData
{
    [JsonPropertyName("labels")]
    public List<AdtlLabelData> Labels { get; set; } = new();

    [JsonPropertyName("notes")]
    public List<AdtlNoteData> Notes { get; set; } = new();

    public static AdtlChunkData? Parse(byte[] data)
    {
        if (data.Length == 0) return null;

        var result = new AdtlChunkData();
        int pos = 0;

        while (pos + 8 <= data.Length)
        {
            string segmentId = Encoding.ASCII.GetString(data, pos, 4);
            int segmentLen = BitConverter.ToInt32(data, pos + 4);
            pos += 8;

            if (pos + segmentLen > data.Length) break;

            byte[] segmentData = new byte[segmentLen];
            Array.Copy(data, pos, segmentData, 0, segmentLen);
            pos += segmentLen;
            if (segmentLen % 2 != 0) pos++;

            switch (segmentId)
            {
                case "labl":
                case "labL":
                    if (segmentLen >= 4)
                    {
                        result.Labels.Add(new AdtlLabelData
                        {
                            Id = BitConverter.ToInt32(segmentData, 0),
                            Text = Encoding.ASCII.GetString(segmentData, 4, segmentLen - 4).TrimEnd('\0')
                        });
                    }
                    break;

                case "note":
                case "Note":
                    if (segmentLen >= 4)
                    {
                        result.Notes.Add(new AdtlNoteData
                        {
                            Id = BitConverter.ToInt32(segmentData, 0),
                            Text = Encoding.ASCII.GetString(segmentData, 4, segmentLen - 4).TrimEnd('\0')
                        });
                    }
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
        int totalSize = 0;
        foreach (var label in Labels)
        {
            int textLen = Encoding.ASCII.GetBytes(label.Text).Length;
            totalSize += 8 + 4 + textLen + (textLen % 2 != 0 ? 1 : 0);
        }
        foreach (var note in Notes)
        {
            int textLen = Encoding.ASCII.GetBytes(note.Text).Length;
            totalSize += 8 + 4 + textLen + (textLen % 2 != 0 ? 1 : 0);
        }

        byte[] buffer = new byte[totalSize];
        int pos = 0;

        foreach (var label in Labels)
        {
            Encoding.ASCII.GetBytes("labl").CopyTo(buffer, pos); pos += 4;
            int textLen = Encoding.ASCII.GetBytes(label.Text).Length;
            BitConverter.GetBytes(4 + textLen).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(label.Id).CopyTo(buffer, pos); pos += 4;
            byte[] textBytes = Encoding.ASCII.GetBytes(label.Text);
            textBytes.CopyTo(buffer, pos); pos += textLen;
            if (textLen % 2 != 0) pos++;
        }

        foreach (var note in Notes)
        {
            Encoding.ASCII.GetBytes("note").CopyTo(buffer, pos); pos += 4;
            int textLen = Encoding.ASCII.GetBytes(note.Text).Length;
            BitConverter.GetBytes(4 + textLen).CopyTo(buffer, pos); pos += 4;
            BitConverter.GetBytes(note.Id).CopyTo(buffer, pos); pos += 4;
            byte[] textBytes = Encoding.ASCII.GetBytes(note.Text);
            textBytes.CopyTo(buffer, pos); pos += textLen;
            if (textLen % 2 != 0) pos++;
        }

        return buffer;
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
                    metadata.Info = InfoChunkData.Parse(chunk.Data);
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

        if (metadata.Bext != null)
        {
            result.Add(new WavChunkData
            {
                ChunkId = "bext",
                Data = metadata.Bext.Serialize()
            });
        }

        if (metadata.Ixml != null)
        {
            result.Add(new WavChunkData
            {
                ChunkId = "ixml",
                Data = metadata.Ixml.Serialize()
            });
        }

        if (metadata.Info != null)
        {
            result.Add(new WavChunkData
            {
                ChunkId = "list",
                Data = metadata.Info.Serialize()
            });
        }

        if (metadata.Cue != null)
        {
            result.Add(new WavChunkData
            {
                ChunkId = "cue ",
                Data = metadata.Cue.Serialize()
            });
        }

        if (metadata.Smpl != null)
        {
            result.Add(new WavChunkData
            {
                ChunkId = "smpl",
                Data = metadata.Smpl.Serialize()
            });
        }

        if (metadata.Adtl != null)
        {
            result.Add(new WavChunkData
            {
                ChunkId = "adtl",
                Data = metadata.Adtl.Serialize()
            });
        }

        foreach (var raw in rawChunks)
        {
            var knownIds = new[] { "bext", "ixml", "list", "cue ", "smpl", "adtl" };
            if (!knownIds.Contains(raw.ChunkId.ToLowerInvariant()))
            {
                result.Add(raw);
            }
        }

        return result.ToArray();
    }
}
