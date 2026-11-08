using NAudio.Wave;
using System.Text.Json;

namespace WAVUtils.Core;

/// <summary>
/// Handles merging multiple mono/stereo/multi-channel WAV files into a single multi-channel WAV.
/// Each input file's channels are placed sequentially in the merged output.
/// Shorter files are padded with silence. All WAV chunks (bext, PMAP, custom) are preserved.
/// </summary>
public static class MergeCommand
{
    public static async Task<string> Run(string[] inputFiles, string? outputPath = null, Action<double, string>? progress = null)
    {
        if (inputFiles.Length < 2)
            throw new ArgumentException(LocalizationManager.GetResource("MergeMinFiles"));

        progress?.Invoke(0.1, LocalizationManager.GetResource("MergeReading"));

        // Open readers and collect metadata
        var readers = new WaveFileReader[inputFiles.Length];
        var trackMetas = new TrackMetadata[inputFiles.Length];
        int totalChannelCount = 0;
        WaveFormat? firstFormat = null;

        for (int i = 0; i < inputFiles.Length; i++)
        {
            string path = Path.GetFullPath(inputFiles[i]);
            readers[i] = new WaveFileReader(path);
            int channels = readers[i].WaveFormat.Channels;
            totalChannelCount += channels;

            if (firstFormat == null)
            {
                firstFormat = readers[i].WaveFormat;
            }
            else if (readers[i].WaveFormat.SampleRate != firstFormat.SampleRate ||
                     readers[i].WaveFormat.BitsPerSample != firstFormat.BitsPerSample)
            {
                readers[i].Dispose();
                throw new ArgumentException(
                    string.Format(LocalizationManager.GetResource("MergeSampleRateMismatch"), Path.GetFileName(path)));
            }

            var result = WavMetadataChunk.ReadWavChunks(path);
            trackMetas[i] = new TrackMetadata
            {
                Index = i,
                FileName = Path.GetFileName(path),
                DataLength = readers[i].Length,
                Channels = channels,
                Bext = result.TypedMetadata.Bext,
                Ixml = result.TypedMetadata.Ixml,
                Info = result.TypedMetadata.Info,
                Cue = result.TypedMetadata.Cue,
                Smpl = result.TypedMetadata.Smpl,
                Adtl = result.TypedMetadata.Adtl,
                Chunks = result.Chunks
            };
        }

        // Calculate max length in samples per channel
        int bytesPerSample = firstFormat!.BitsPerSample / 8;
        long maxSamplesPerChannel = 0;
        foreach (var r in readers)
        {
            long samples = r.Length / (r.WaveFormat.Channels * bytesPerSample);
            if (samples > maxSamplesPerChannel) maxSamplesPerChannel = samples;
        }

        // Generate output path
        if (string.IsNullOrEmpty(outputPath))
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string dir = Path.GetDirectoryName(Path.GetFullPath(inputFiles[0]))!;
            outputPath = Path.Combine(dir, string.Format(LocalizationManager.GetResource("MergeDefaultFilename"), timestamp));
        }
        else
        {
            outputPath = Path.GetFullPath(outputPath);
        }

        progress?.Invoke(0.3, LocalizationManager.GetResource("MergeMerging"));

        // Build entire file
        byte[] fileData = BuildMergedFile(
            readers, trackMetas, (int)maxSamplesPerChannel,
            bytesPerSample, totalChannelCount, firstFormat);

        File.WriteAllBytes(outputPath, fileData);

        foreach (var r in readers) r.Dispose();

        progress?.Invoke(1.0, LocalizationManager.GetResource("MergeDone"));

        return outputPath;
    }

    private static byte[] BuildMergedFile(
        WaveFileReader[] readers,
        TrackMetadata[] trackMetas,
        int maxSamples,
        int bytesPerSample,
        int totalChannels,
        WaveFormat firstFormat)
    {
        // JSON metadata for PMAL chunk
        string json = JsonSerializer.Serialize(trackMetas, new JsonSerializerOptions { WriteIndented = false });
        byte[] pmalBytes = System.Text.Encoding.UTF8.GetBytes(json);

        // Calculate sizes for typed metadata chunks
        int bextSize = 0, ixmlSize = 0, infoSize = 0, cueSize = 0, smplSize = 0, adtlSize = 0;
        bool hasBext = false, hasIxml = false, hasInfo = false, hasCue = false, hasSmpl = false, hasAdtl = false;

        if (trackMetas.Length > 0)
        {
            var first = trackMetas[0];
            if (first.Bext != null)
            {
                byte[] bextData = first.Bext.Serialize();
                bextSize = bextData.Length % 2 != 0 ? bextData.Length + 1 : bextData.Length;
                hasBext = true;
            }
            if (first.Ixml != null)
            {
                byte[] ixmlData = first.Ixml.Serialize();
                ixmlSize = ixmlData.Length % 2 != 0 ? ixmlData.Length + 1 : ixmlData.Length;
                hasIxml = true;
            }
            if (first.Info != null)
            {
                byte[] infoData = first.Info.Serialize();
                infoSize = infoData.Length % 2 != 0 ? infoData.Length + 1 : infoData.Length;
                hasInfo = true;
            }
            if (first.Cue != null)
            {
                byte[] cueData = first.Cue.Serialize();
                cueSize = cueData.Length % 2 != 0 ? cueData.Length + 1 : cueData.Length;
                hasCue = true;
            }
            if (first.Smpl != null)
            {
                byte[] smplData = first.Smpl.Serialize();
                smplSize = smplData.Length % 2 != 0 ? smplData.Length + 1 : smplData.Length;
                hasSmpl = true;
            }
            if (first.Adtl != null)
            {
                byte[] adtlData = first.Adtl.Serialize();
                adtlSize = adtlData.Length % 2 != 0 ? adtlData.Length + 1 : adtlData.Length;
                hasAdtl = true;
            }
        }

        int bextBlock = hasBext ? 8 + bextSize : 0;
        int ixmlBlock = hasIxml ? 8 + ixmlSize : 0;
        int infoBlock = hasInfo ? 8 + infoSize : 0;
        int cueBlock = hasCue ? 8 + cueSize : 0;
        int smplBlock = hasSmpl ? 8 + smplSize : 0;
        int adtlBlock = hasAdtl ? 8 + adtlSize : 0;

        long totalDataBytes = (long)maxSamples * totalChannels * bytesPerSample;
        int totalPmalChunkSize = 8 + pmalBytes.Length;
        int preDataMetadataSize = bextBlock + ixmlBlock + infoBlock;
        int postDataMetadataSize = cueBlock + smplBlock + adtlBlock;
        int fileSize = 44 + preDataMetadataSize + (int)totalDataBytes + postDataMetadataSize + totalPmalChunkSize;
        int riffSize = fileSize - 8;

        byte[] buffer = new byte[fileSize];
        int pos = 0;

        // RIFF header
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes(riffSize).CopyTo(buffer, pos); pos += 4;
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(buffer, pos); pos += 4;

        // fmt chunk
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes(16).CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes((short)firstFormat.Encoding).CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes((short)totalChannels).CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes(firstFormat.SampleRate).CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes(firstFormat.SampleRate * totalChannels * bytesPerSample).CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes((short)(totalChannels * bytesPerSample)).CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes((short)firstFormat.BitsPerSample).CopyTo(buffer, pos); pos += 4;

        // Pre-data metadata chunks (bEXT, iXML, INFO)
        if (hasBext && trackMetas[0].Bext != null)
        {
            byte[] bextData = trackMetas[0].Bext.Serialize();
            WriteChunk(buffer, ref pos, "bext", bextData);
        }
        if (hasIxml && trackMetas[0].Ixml != null)
        {
            byte[] ixmlData = trackMetas[0].Ixml.Serialize();
            WriteChunk(buffer, ref pos, "ixml", ixmlData);
        }
        if (hasInfo && trackMetas[0].Info != null)
        {
            byte[] infoData = trackMetas[0].Info.Serialize();
            WriteChunk(buffer, ref pos, "list", infoData);
        }

        // data chunk header
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes((int)totalDataBytes).CopyTo(buffer, pos); pos += 4;

        // Interleave samples
        for (int sample = 0; sample < maxSamples; sample++)
        {
            int dstPos = pos;
            
            for (int t = 0; t < readers.Length; t++)
            {
                int channels = readers[t].WaveFormat.Channels;
                int frameSize = channels * bytesPerSample;
                byte[] frameBuffer = new byte[frameSize];
                
                long sampleOffset = (long)sample * frameSize;
                readers[t].Position = sampleOffset;
                
                int read = readers[t].Read(frameBuffer);
                if (read > 0)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int srcOffset = ch * bytesPerSample;
                        Array.Copy(frameBuffer, srcOffset, buffer, dstPos, bytesPerSample);
                        dstPos += bytesPerSample;
                    }
                }
                else
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        dstPos += bytesPerSample;
                    }
                }
            }
        }

        // Post-data metadata chunks (cue, smpl, adtl)
        if (hasCue && trackMetas[0].Cue != null)
        {
            byte[] cueData = trackMetas[0].Cue.Serialize();
            WriteChunk(buffer, ref pos, "cue ", cueData);
        }
        if (hasSmpl && trackMetas[0].Smpl != null)
        {
            byte[] smplData = trackMetas[0].Smpl.Serialize();
            WriteChunk(buffer, ref pos, "smpl", smplData);
        }
        if (hasAdtl && trackMetas[0].Adtl != null)
        {
            byte[] adtlData = trackMetas[0].Adtl.Serialize();
            WriteChunk(buffer, ref pos, "adtl", adtlData);
        }

        // PMAL chunk after everything
        System.Text.Encoding.ASCII.GetBytes("pmal").CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes(pmalBytes.Length).CopyTo(buffer, pos); pos += 4;
        pmalBytes.CopyTo(buffer, pos); pos += pmalBytes.Length;

        return buffer;
    }

    private static void WriteChunk(byte[] buffer, ref int pos, string chunkId, byte[] chunkData)
    {
        System.Text.Encoding.ASCII.GetBytes(chunkId).CopyTo(buffer, pos); pos += 4;
        int dataLen = chunkData.Length;
        BitConverter.GetBytes(dataLen).CopyTo(buffer, pos); pos += 4;
        chunkData.CopyTo(buffer, pos); pos += dataLen;
        if (dataLen % 2 != 0) pos++;
    }
}
