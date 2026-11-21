using NAudio.Wave;
using System.Text.Json;

namespace WAVUtils.Core;

/// <summary>
/// Handles splitting a multi-channel WAV into individual tracks using PMAL metadata.
/// Each track is reconstructed with its original channel count (mono/stereo/multi).
/// All WAV chunks from each original track are preserved.
/// </summary>
public static class SplitCommand
{
    public static async Task<string[]> Run(string inputMergedFile, string? outputDirectory = null)
    {
        string mergedPath = Path.GetFullPath(inputMergedFile);
        if (!File.Exists(mergedPath))
            throw new FileNotFoundException(string.Format(LocalizationManager.GetResource("SplitFileNotFound"), mergedPath));

        // Read PMAL metadata to get track layout
        byte[] mergedBytes = File.ReadAllBytes(mergedPath);
        TrackMetadata[] tracks = ReadPmalChunks(mergedBytes);

        if (tracks.Length == 0)
            throw new InvalidOperationException(LocalizationManager.GetResource("SplitNoPmal"));

        if (string.IsNullOrEmpty(outputDirectory))
            outputDirectory = Path.GetDirectoryName(mergedPath)!;

        Directory.CreateDirectory(outputDirectory);

        // Open merged file for channel extraction
        using var mergedReader = new WaveFileReader(mergedPath);
        int totalChannels = mergedReader.WaveFormat.Channels;
        int bytesPerSample = mergedReader.WaveFormat.BitsPerSample / 8;
        int bytesPerFrame = totalChannels * bytesPerSample;
        long totalFrames = mergedReader.Length / bytesPerFrame;

        var outputFiles = new string[tracks.Length];
        byte[] frameBuffer = new byte[bytesPerFrame];

        for (int t = 0; t < tracks.Length; t++)
        {
            TrackMetadata track = tracks[t];
            int channels = track.Channels;
            int channelBytesPerFrame = channels * bytesPerSample;

            // Create output format matching original channel count
            WaveFormat outputFormat = WaveFormat.CreateCustomFormat(
                mergedReader.WaveFormat.Encoding,
                mergedReader.WaveFormat.SampleRate,
                channels,
                mergedReader.WaveFormat.SampleRate * channelBytesPerFrame,
                channelBytesPerFrame,
                mergedReader.WaveFormat.BitsPerSample);

            // Find the starting channel index for this track in the merged file
            int startChannel = 0;
            for (int i = 0; i < t; i++)
            {
                startChannel += tracks[i].Channels;
            }

            string outputPath = Path.Combine(outputDirectory, track.FileName);
            
            // Extract channel data for this track
            long framesWritten = 0;
            mergedReader.Position = 0;

            // Write audio data
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new WaveFileWriter(fs, outputFormat))
            {
                while (framesWritten < totalFrames)
                {
                    int read = mergedReader.Read(frameBuffer);
                    if (read == 0) break;

                    // Extract channels for this track
                    int srcOffset = startChannel * bytesPerSample;
                    writer.Write(frameBuffer, srcOffset, channelBytesPerFrame);
                    framesWritten++;
                }
                writer.Flush();
            }

            outputFiles[t] = outputPath;

            // Append preserved chunks and typed metadata to output file (after writer is closed)
            if (track.Chunks.Count > 0 || track.Bext != null || track.Ixml != null ||
                track.Info != null || track.Cue != null || track.Smpl != null || track.Adtl != null)
            {
                AppendChunksToOutput(outputPath, track);
            }
        }

        return outputFiles;
    }

    private static TrackMetadata[] ReadPmalChunks(byte[] fileData)
    {
        // Walk the chunk list to the end of the data chunk: merged files carry bext/ixml/INFO
        // before data, so the "fmt at 12, data at 36" assumption does not hold.
        int dataChunkEnd = -1;
        int pos = 12;
        byte[] lenBytes = new byte[4];

        while (pos + 8 <= fileData.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(fileData, pos, 4);
            System.Array.Copy(fileData, pos + 4, lenBytes, 0, 4);
            int len = BitConverter.ToInt32(lenBytes, 0);
            if (id == "data")
            {
                dataChunkEnd = pos + 8 + len + (len & 1);
                break;
            }
            pos += 8 + len + (len & 1);
        }

        if (dataChunkEnd < 0 || dataChunkEnd + 8 > fileData.Length)
            return Array.Empty<TrackMetadata>();

        byte[] pmalId = new byte[4];
        System.Array.Copy(fileData, dataChunkEnd, pmalId, 0, 4);
        
        if (System.Text.Encoding.ASCII.GetString(pmalId) != "pmal")
            return Array.Empty<TrackMetadata>();

        System.Array.Copy(fileData, dataChunkEnd + 4, lenBytes, 0, 4);
        int pmalSize = BitConverter.ToInt32(lenBytes, 0);
        
        byte[] pmalData = new byte[pmalSize];
        System.Array.Copy(fileData, dataChunkEnd + 8, pmalData, 0, pmalSize);
        
        return JsonSerializer.Deserialize<TrackMetadata[]>(
            System.Text.Encoding.UTF8.GetString(pmalData)) ?? Array.Empty<TrackMetadata>();
    }

    private static void AppendChunksToOutput(string outputPath, TrackMetadata track)
    {
        using var fs = new FileStream(outputPath, FileMode.Append, FileAccess.Write);

        // Write typed metadata chunks
        if (track.Bext != null)
        {
            byte[] bextData = track.Bext.Serialize();
            WriteChunkToStream(fs, "bext", bextData);
        }

        if (track.Ixml != null)
        {
            byte[] ixmlData = track.Ixml.Serialize();
            WriteChunkToStream(fs, "ixml", ixmlData);
        }

        if (track.Info != null)
        {
            byte[] infoData = track.Info.Serialize();
            WriteChunkToStream(fs, "list", infoData);
        }

        if (track.Cue != null)
        {
            byte[] cueData = track.Cue.Serialize();
            WriteChunkToStream(fs, "cue ", cueData);
        }

        if (track.Smpl != null)
        {
            byte[] smplData = track.Smpl.Serialize();
            WriteChunkToStream(fs, "smpl", smplData);
        }

        if (track.Adtl != null)
        {
            byte[] adtlData = track.Adtl.Serialize();
            WriteChunkToStream(fs, "adtl", adtlData);
        }

        // Write raw chunks; those already written from the typed model are skipped - a RIFF
        // chunk type must appear at most once (F6 files carry bext in both places)
        var typedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (track.Bext != null) typedIds.Add("bext");
        if (track.Ixml != null) typedIds.Add("ixml");
        if (track.Info != null) typedIds.Add("list");
        if (track.Cue != null) typedIds.Add("cue ");
        if (track.Smpl != null) typedIds.Add("smpl");
        if (track.Adtl != null) typedIds.Add("adtl");
        WavMetadataChunk.WriteChunksToStream(fs, track.Chunks.Where(c => !typedIds.Contains(c.ChunkId)).ToList());
    }

    private static void WriteChunkToStream(Stream stream, string chunkId, byte[] chunkData)
    {
        byte[] idBytes = System.Text.Encoding.ASCII.GetBytes(chunkId);
        stream.Write(idBytes, 0, 4);

        int dataLen = chunkData.Length;
        byte[] lenBytes = BitConverter.GetBytes(dataLen);
        if (!BitConverter.IsLittleEndian)
            lenBytes = lenBytes.Reverse().ToArray();
        stream.Write(lenBytes, 0, 4);

        stream.Write(chunkData, 0, dataLen);

        if (dataLen % 2 != 0)
        {
            stream.WriteByte(0);
        }
    }
}
