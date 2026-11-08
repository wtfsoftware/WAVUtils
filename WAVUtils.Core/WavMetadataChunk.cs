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
}

/// <summary>
/// Handles reading and writing the PMAL metadata chunk in WAV files.
/// PMAL stores track metadata including preserved WAV chunks.
/// </summary>
public static class WavMetadataChunk
{
    private const string ChunkId = "pmal";

    /// <summary>
    /// Reads all chunks from a WAV file, excluding 'fmt ' and 'data' chunks.
    /// Returns both raw chunks and typed metadata (bEXT, iXML, INFO, cue, smpl, adtl).
    /// </summary>
    public static WavChunksResult ReadWavChunks(string filePath)
    {
        var chunks = new List<WavChunkData>();
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        ReadChunksFromStream(fs, chunks);

        var typedMetadata = WavChunkParsers.ExtractTypedMetadata(chunks);

        return new WavChunksResult
        {
            Chunks = chunks,
            TypedMetadata = typedMetadata
        };
    }

    /// <summary>
    /// Reads all chunks from a WAV file, excluding 'fmt ' and 'data' chunks.
    /// Legacy method — returns only raw chunks for backward compatibility.
    /// </summary>
    [Obsolete("Use ReadWavChunks(string) which returns typed metadata as well.")]
    public static List<WavChunkData> ReadWavChunksRaw(string filePath)
    {
        var chunks = new List<WavChunkData>();
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        ReadChunksFromStream(fs, chunks);
        return chunks;
    }

    private static void ReadChunksFromStream(Stream stream, List<WavChunkData> chunks)
    {
        // Skip RIFF header (12 bytes)
        stream.Seek(12, SeekOrigin.Begin);

        while (stream.Position < stream.Length - 8)
        {
            long chunkStart = stream.Position;
            byte[] id = new byte[4];
            if (stream.Read(id, 0, 4) < 4) break;
            
            byte[] lenBytes = new byte[4];
            if (stream.Read(lenBytes, 0, 4) < 4) break;

            string chunkName = System.Text.Encoding.ASCII.GetString(id);
            int chunkLength = BitConverter.ToInt32(lenBytes, 0);
            
            // Skip 'fmt ' and 'data' chunks
            if (chunkName != "fmt " && chunkName != "data")
            {
                byte[] chunkData = new byte[chunkLength];
                stream.Read(chunkData, 0, chunkLength);
                chunks.Add(new WavChunkData
                {
                    ChunkId = chunkName,
                    Data = chunkData
                });
            }
            else
            {
                stream.Seek(chunkLength, SeekOrigin.Current);
            }

            // Align to even boundary
            if (chunkLength % 2 != 0)
            {
                stream.Seek(1, SeekOrigin.Current);
            }
        }
    }

    /// <summary>
    /// Writes the PMAL chunk at the end of a WAV file.
    /// </summary>
    public static void Write(Stream wavStream, TrackMetadata[] metadata)
    {
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = false
        });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        // "pmal" chunk header
        byte[] chunkIdBytes = System.Text.Encoding.ASCII.GetBytes(ChunkId);
        wavStream.Write(chunkIdBytes, 0, 4);
        WriteInt32LE(wavStream, bytes.Length);
        wavStream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Reads PMAL chunk from a WAV file and returns track metadata.
    /// </summary>
    public static TrackMetadata[] Read(Stream wavStream)
    {
        wavStream.Seek(12, SeekOrigin.Begin);

        while (wavStream.Position < wavStream.Length - 8)
        {
            byte[] id = new byte[4];
            if (wavStream.Read(id, 0, 4) < 4) break;

            byte[] lenBytes = new byte[4];
            if (wavStream.Read(lenBytes, 0, 4) < 4) break;

            string chunkName = System.Text.Encoding.ASCII.GetString(id);

            if (chunkName == ChunkId)
            {
                int dataLength = BitConverter.ToInt32(lenBytes, 0);
                byte[] data = new byte[dataLength];
                wavStream.Read(data, 0, dataLength);
                string json = System.Text.Encoding.UTF8.GetString(data);
                return JsonSerializer.Deserialize<TrackMetadata[]>(json)!;
            }

            int skipLength = BitConverter.ToInt32(lenBytes, 0);
            wavStream.Seek(skipLength, SeekOrigin.Current);
            if (skipLength % 2 != 0)
            {
                wavStream.Seek(1, SeekOrigin.Current);
            }
        }

        throw new InvalidOperationException("PMAL chunk not found in WAV file.");
    }

    /// <summary>
    /// Writes all preserved chunks into a WAV file buffer at the specified offset.
    /// Returns the total size used (including chunk headers and padding).
    /// </summary>
    public static int WriteChunksToBuffer(byte[] buffer, int offset, List<WavChunkData> chunks)
    {
        int pos = offset;
        foreach (var chunk in chunks)
        {
            // Chunk ID
            byte[] idBytes = System.Text.Encoding.ASCII.GetBytes(chunk.ChunkId);
            idBytes.CopyTo(buffer, pos);
            pos += 4;

            // Chunk length (little-endian)
            BitConverter.GetBytes(chunk.Data.Length).CopyTo(buffer, pos);
            pos += 4;

            // Chunk data
            chunk.Data.CopyTo(buffer, pos);
            pos += chunk.Data.Length;

            // Even alignment
            if (chunk.Data.Length % 2 != 0)
            {
                pos++;
            }
        }
        return pos - offset;
    }

    /// <summary>
    /// Writes all preserved chunks into a stream.
    /// </summary>
    public static void WriteChunksToStream(Stream stream, List<WavChunkData> chunks)
    {
        foreach (var chunk in chunks)
        {
            // Chunk ID
            byte[] idBytes = System.Text.Encoding.ASCII.GetBytes(chunk.ChunkId);
            stream.Write(idBytes, 0, 4);

            // Chunk length (little-endian)
            WriteInt32LE(stream, chunk.Data.Length);

            // Chunk data
            stream.Write(chunk.Data, 0, chunk.Data.Length);

            // Even alignment
            if (chunk.Data.Length % 2 != 0)
            {
                stream.WriteByte(0);
            }
        }
    }

    private static void WriteInt32LE(Stream stream, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian)
            bytes = bytes.Reverse().ToArray();
        stream.Write(bytes, 0, 4);
    }
}
