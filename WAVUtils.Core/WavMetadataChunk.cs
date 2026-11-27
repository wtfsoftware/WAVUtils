using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.Wave;

namespace WAVUtils.Core;

/// <summary>
/// Metadata for a track stored in the PMAL chunk.
/// Includes file name, data length, typed metadata (bEXT, iXML, INFO, cue, smpl, adtl),
/// and all other WAV chunks from the original file.
/// </summary>
public class TrackMetadata
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("dataLength")]
    public long DataLength { get; set; }

    [JsonPropertyName("channels")]
    public int Channels { get; set; }

    /// <summary>
    /// Typed bEXT (Broadcast Extension) metadata.
    /// </summary>
    [JsonPropertyName("bext")]
    public BextChunkData? Bext { get; set; }

    /// <summary>
    /// Typed iXML (Interchange XML) metadata.
    /// </summary>
    [JsonPropertyName("ixml")]
    public IxmlChunkData? Ixml { get; set; }

    /// <summary>
    /// Typed INFO chunk metadata (ISFT, IART, INAM, etc.).
    /// </summary>
    [JsonPropertyName("info")]
    public InfoChunkData? Info { get; set; }

    /// <summary>
    /// Typed cue point metadata.
    /// </summary>
    [JsonPropertyName("cue")]
    public CueChunkData? Cue { get; set; }

    /// <summary>
    /// Typed smpl (sample loop) metadata.
    /// </summary>
    [JsonPropertyName("smpl")]
    public SmplChunkData? Smpl { get; set; }

    /// <summary>
    /// Typed adtl (Audio Data Link List) metadata.
    /// </summary>
    [JsonPropertyName("adtl")]
    public AdtlChunkData? Adtl { get; set; }

    /// <summary>
    /// List of WAV chunks (excluding fmt and data) preserved from the original file.
    /// Format: {chunkId(4 bytes)}{dataLength(4 bytes LE)}{rawData}.
    /// Stored as base64.
    /// </summary>
    [JsonPropertyName("chunks")]
    public List<WavChunkData> Chunks { get; set; } = new();

    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? FormatData { get; set; }
}

/// <summary>
/// Represents a WAV chunk's raw binary data.
/// </summary>
public class WavChunkData
{
    [JsonPropertyName("id")]
    public string ChunkId { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public byte[] Data { get; set; } = new byte[0];
}

/// <summary>
/// Result of reading WAV chunks — includes both raw chunks and typed metadata.
/// </summary>
public class WavChunksResult
{
    public List<WavChunkData> Chunks { get; set; } = new();
    public WavFileMetadata TypedMetadata { get; set; } = new();
    internal byte[] FormatData { get; set; } = [];
    internal long DataOffset { get; set; }
    internal long DataLength { get; set; }
}

/// <summary>
/// Handles reading and writing the PMAL metadata chunk in WAV files.
/// PMAL stores track metadata including preserved WAV chunks.
/// </summary>
public static class WavMetadataChunk
{
    private const string ChunkId = "pmal";
    internal const int MaxMetadataBytes = 64 * 1024 * 1024;
    internal const int MaxTracks = 256;

    internal static string ReadChunkId(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
            if (value is < 32 or > 126) throw new InvalidDataException("Invalid chunk identifier.");
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>
    /// Reads all chunks from a WAV file, excluding 'fmt ' and 'data' chunks.
    /// Returns both raw chunks and typed metadata (bEXT, iXML, INFO, cue, smpl, adtl).
    /// </summary>
    public static WavChunksResult ReadWavChunks(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadFile(fs);
    }

    /// <summary>
    /// Reads all chunks from a WAV file, excluding 'fmt ' and 'data' chunks.
    /// Legacy method — returns only raw chunks for backward compatibility.
    /// </summary>
    [Obsolete("Use ReadWavChunks(string) which returns typed metadata as well.")]
    public static List<WavChunkData> ReadWavChunksRaw(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadFile(fs).Chunks;
    }

    internal static WavChunksResult ReadFile(Stream stream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanSeek || stream.Length < 12) throw new InvalidDataException("Truncated WAV header.");
        stream.Position = 0;
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        bool rf64 = header[..4].SequenceEqual("RF64"u8);
        if ((!rf64 && !header[..4].SequenceEqual("RIFF"u8)) || !header[8..].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Expected RIFF/RF64 WAVE.");
        uint riffSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if ((!rf64 || riffSize != uint.MaxValue) && riffSize != stream.Length - 8)
            throw new InvalidDataException("RIFF size does not match the file.");

        ulong rf64DataLength = 0, rf64SampleCount = 0;
        var sizes = new Dictionary<string, Queue<long>>(StringComparer.Ordinal);
        if (rf64)
        {
            if (stream.Length - stream.Position < 8) throw new InvalidDataException("Missing ds64 chunk.");
            stream.ReadExactly(header[..8]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (!header[..4].SequenceEqual("ds64"u8) || size < 28 || size > 16 * 1024 * 1024 ||
                (long)size + (size & 1) > stream.Length - stream.Position)
                throw new InvalidDataException("Invalid RF64 ds64 chunk.");
            byte[] ds64 = new byte[(int)size];
            stream.ReadExactly(ds64);
            ulong largeRiffSize = BinaryPrimitives.ReadUInt64LittleEndian(ds64);
            rf64DataLength = BinaryPrimitives.ReadUInt64LittleEndian(ds64.AsSpan(8));
            rf64SampleCount = BinaryPrimitives.ReadUInt64LittleEndian(ds64.AsSpan(16));
            uint tableCount = BinaryPrimitives.ReadUInt32LittleEndian(ds64.AsSpan(24));
            if ((riffSize == uint.MaxValue && largeRiffSize != (ulong)(stream.Length - 8)) ||
                28L + tableCount * 12L != size)
                throw new InvalidDataException("Invalid ds64 lengths.");
            for (int entry = 0; entry < tableCount; entry++)
            {
                int offset = 28 + entry * 12;
                string id = ReadChunkId(ds64.AsSpan(offset, 4));
                ulong length = BinaryPrimitives.ReadUInt64LittleEndian(ds64.AsSpan(offset + 4));
                if (length > long.MaxValue) throw new InvalidDataException("RF64 chunk length is too large.");
                if (!sizes.TryGetValue(id, out var queue)) sizes.Add(id, queue = new Queue<long>());
                queue.Enqueue((long)length);
            }
            if ((size & 1) != 0) stream.Position++;
        }

        var result = new WavChunksResult();
        long metadataBytes = 0;
        int chunkCount = 0;
        bool hasData = false;
        while (stream.Position < stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++chunkCount > 65536 || stream.Length - stream.Position < 8)
                throw new InvalidDataException("Too many chunks or a truncated chunk header.");
            stream.ReadExactly(header[..8]);
            string id = ReadChunkId(header[..4]);
            uint declared = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            long length = declared;
            if (declared == uint.MaxValue)
            {
                if (!rf64) throw new InvalidDataException("RF64 length without ds64.");
                if (id == "data" && !hasData)
                {
                    if (rf64DataLength > long.MaxValue) throw new InvalidDataException("RF64 data length is too large.");
                    length = (long)rf64DataLength;
                }
                else if (sizes.TryGetValue(id, out var queue) && queue.Count > 0) length = queue.Dequeue();
                else throw new InvalidDataException("Missing ds64 table entry.");
            }
            long body = stream.Position;
            long remaining = stream.Length - body;
            if (length < 0 || length > remaining || (length & 1) > remaining - length)
                throw new InvalidDataException($"Truncated {id} chunk or padding.");
            if (id == "data")
            {
                if (hasData) throw new InvalidDataException("Duplicate data chunk.");
                hasData = true;
                result.DataOffset = body;
                result.DataLength = length;
            }
            else
            {
                if (id == "ds64") throw new InvalidDataException("Unexpected ds64 chunk.");
                metadataBytes += length;
                if (length > (id == ChunkId ? MaxMetadataBytes : 16 * 1024 * 1024) || metadataBytes > MaxMetadataBytes)
                    throw new InvalidDataException("WAV metadata exceeds the safety budget.");
                byte[] data = new byte[(int)length];
                stream.ReadExactly(data);
                if (id == "fmt ")
                {
                    if (result.FormatData.Length != 0 || data.Length < 16) throw new InvalidDataException("Invalid or duplicate fmt chunk.");
                    result.FormatData = data;
                }
                // The RF64 writer's empty reservation is structural, not source metadata.
                else if (!(id == "JUNK" && body == 20 && length == 28 && data.All(b => b == 0)))
                    result.Chunks.Add(new WavChunkData { ChunkId = id, Data = data });
            }
            stream.Position = body + length;
            if ((length & 1) != 0 && stream.ReadByte() != 0)
                throw new InvalidDataException($"Nonzero {id} padding.");
        }
        if (!hasData || result.FormatData.Length == 0 || sizes.Values.Any(queue => queue.Count != 0))
            throw new InvalidDataException("Missing WAV format/data or unused ds64 entries.");
        int align = BinaryPrimitives.ReadUInt16LittleEndian(result.FormatData.AsSpan(12));
        if (rf64 && align > 0 && result.Chunks.FirstOrDefault(chunk => chunk.ChunkId == "fact") is { } fact)
        {
            if (fact.Data.Length < 4) throw new InvalidDataException("Truncated RF64 fact chunk.");
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(fact.Data);
            if ((count == uint.MaxValue ? rf64SampleCount : count) != (ulong)(result.DataLength / align))
                throw new InvalidDataException("RF64 sample count does not match the audio.");
        }
        result.TypedMetadata = WavChunkParsers.ExtractTypedMetadata(result.Chunks);
        return result;
    }

    /// <summary>
    /// Writes the PMAL chunk at the end of a WAV file.
    /// </summary>
    public static void Write(Stream wavStream, TrackMetadata[] metadata)
    {
        WriteChunksToStream(wavStream, [new() { ChunkId = ChunkId, Data = SerializeTracks(metadata) }]);
    }

    /// <summary>
    /// Reads PMAL chunk from a WAV file and returns track metadata.
    /// </summary>
    public static TrackMetadata[] Read(Stream wavStream)
    {
        return ReadTracks(ReadFile(wavStream));
    }

    internal static byte[] SerializeTracks(TrackMetadata[] tracks)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(tracks);
        if (bytes.Length > MaxMetadataBytes) throw new InvalidDataException("PMAL exceeds the safety budget.");
        return bytes;
    }

    internal static TrackMetadata[] ReadTracks(WavChunksResult wav)
    {
        var pmal = wav.Chunks.Where(chunk => chunk.ChunkId == ChunkId).ToArray();
        if (pmal.Length != 1) throw new InvalidDataException("Expected exactly one PMAL chunk.");
        TrackMetadata[] tracks;
        try { tracks = JsonSerializer.Deserialize<TrackMetadata[]>(pmal[0].Data) ?? throw new InvalidDataException("Null PMAL."); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid PMAL JSON.", ex); }
        if (tracks.Length == 0 || tracks.Length > MaxTracks) throw new InvalidDataException("Invalid PMAL track count.");
        var mergedFormat = new WavAudioFormat(wav.FormatData);
        mergedFormat.ValidateDataLength(wav.DataLength);
        long frames = wav.DataLength / mergedFormat.BlockAlign;
        long channelSum = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < tracks.Length; i++)
        {
            var track = tracks[i];
            if (track == null || track.Index != i || track.Channels <= 0 || track.Channels > short.MaxValue || track.Chunks == null)
                throw new InvalidDataException("Invalid PMAL track/index/channels.");
            ValidateFileName(track.FileName);
            if (!names.Add(track.FileName)) throw new InvalidDataException("Duplicate PMAL output name.");
            var format = track.FormatData == null ? mergedFormat.WithChannels(track.Channels) : new WavAudioFormat(track.FormatData);
            if (format.Channels != track.Channels || !mergedFormat.IsCompatibleWith(format))
                throw new InvalidDataException("PMAL format does not match merged audio.");
            format.ValidateDataLength(track.DataLength);
            if (track.DataLength / format.BlockAlign > frames) throw new InvalidDataException("PMAL length exceeds merged audio.");
            ValidateChunks(track.Chunks);
            channelSum += track.Channels;
        }
        if (channelSum != mergedFormat.Channels) throw new InvalidDataException("PMAL channel sum does not match merged audio.");
        return tracks;
    }

    internal static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)) || name.Length > 255)
            throw new InvalidDataException("PMAL output must be a safe file name, not a path.");
        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                "123456789\u00b9\u00b2\u00b3".Contains(stem[3])))
            throw new InvalidDataException("Reserved Windows output name.");
    }

    internal static void ValidateChunks(List<WavChunkData> chunks)
    {
        long total = 0;
        if (chunks.Count > 65536) throw new InvalidDataException("Too many metadata chunks.");
        foreach (var chunk in chunks)
        {
            if (chunk == null || chunk.ChunkId == null || chunk.ChunkId.Length != 4 || chunk.ChunkId.Any(c => c < 32 || c > 126) || chunk.Data == null)
                throw new InvalidDataException("Invalid preserved chunk.");
            if (chunk.ChunkId is "fmt " or "data" or "ds64") throw new InvalidDataException("Structural chunk inside PMAL metadata.");
            total += chunk.Data.Length;
            if (total > MaxMetadataBytes) throw new InvalidDataException("Preserved metadata exceeds the safety budget.");
            WavChunkParsers.ValidateList(chunk);
        }
    }

    internal static void AddChunks(WaveFileWriter writer, List<WavChunkData> chunks)
    {
        foreach (var chunk in chunks)
        {
            if (chunk.ChunkId == "fact") continue; // NAudio regenerates the sample count.
            bool after = chunk.ChunkId is "cue " or "smpl" or "adtl" or "pmal" ||
                (chunk.ChunkId == "LIST" && chunk.Data.AsSpan().StartsWith("adtl"u8));
            writer.AddChunk(chunk.ChunkId, chunk.Data, after ? ChunkPosition.AfterData : ChunkPosition.BeforeData);
        }
    }

    /// <summary>
    /// Writes all preserved chunks into a WAV file buffer at the specified offset.
    /// Returns the total size used (including chunk headers and padding).
    /// </summary>
    public static int WriteChunksToBuffer(byte[] buffer, int offset, List<WavChunkData> chunks)
    {
        using var stream = new MemoryStream(buffer, writable: true);
        stream.Position = offset;
        WriteChunksToStream(stream, chunks);
        return checked((int)stream.Position - offset);
    }

    /// <summary>
    /// Writes all preserved chunks into a stream.
    /// </summary>
    public static void WriteChunksToStream(Stream stream, List<WavChunkData> chunks)
    {
        ValidateChunks(chunks);
        Span<byte> length = stackalloc byte[4];
        foreach (var chunk in chunks)
        {
            stream.Write(Encoding.ASCII.GetBytes(chunk.ChunkId));
            BinaryPrimitives.WriteInt32LittleEndian(length, chunk.Data.Length);
            stream.Write(length);
            stream.Write(chunk.Data);
            if ((chunk.Data.Length & 1) != 0) stream.WriteByte(0);
        }
    }
}
