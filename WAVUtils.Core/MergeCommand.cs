using NAudio.Wave;
using System.Text.Json;
using System.Text.RegularExpressions;

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

        // Decode LTC from the last input file: its start timecode drives bext TimeReference and iXML
        var ltc = DecodeLtcFromReader(readers[^1], firstFormat);
        IxmlChunkData? mergedIxml = ltc.HasValue
            ? new IxmlChunkData { Content = SetIxmlTimecode(trackMetas[0].Ixml?.Content, ltc.Value) }
            : null;
        byte[]? mergedBext = BuildMergedBext(trackMetas, inputFiles, ltc, firstFormat.SampleRate);

        progress?.Invoke(0.3, LocalizationManager.GetResource("MergeMerging"));

        // Build entire file
        byte[] fileData = BuildMergedFile(
            readers, trackMetas, (int)maxSamplesPerChannel,
            bytesPerSample, totalChannelCount, firstFormat, mergedBext, mergedIxml);

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
        WaveFormat firstFormat,
        byte[]? mergedBext,
        IxmlChunkData? mergedIxml)
    {
        // JSON metadata for PMAL chunk
        string json = JsonSerializer.Serialize(trackMetas, new JsonSerializerOptions { WriteIndented = false });
        byte[] pmalBytes = System.Text.Encoding.UTF8.GetBytes(json);

        // Calculate sizes for typed metadata chunks
        int bextSize = 0, ixmlSize = 0, infoSize = 0, cueSize = 0, smplSize = 0, adtlSize = 0;
        bool hasBext = false, hasIxml = false, hasInfo = false, hasCue = false, hasSmpl = false, hasAdtl = false;

        // bEXT and iXML content is precomputed in Run (bext from last file + LTC timecode; ixml from first file + timecode)
        byte[]? ixmlData0 = (mergedIxml ?? trackMetas[0].Ixml)?.Serialize();

        if (mergedBext != null)
        {
            bextSize = mergedBext.Length % 2 != 0 ? mergedBext.Length + 1 : mergedBext.Length;
            hasBext = true;
        }
        if (trackMetas.Length > 0)
        {
            var first = trackMetas[0];
            if (ixmlData0 != null)
            {
                ixmlSize = ixmlData0.Length % 2 != 0 ? ixmlData0.Length + 1 : ixmlData0.Length;
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
        // 44 = RIFF header (12) + fmt chunk (24) + data chunk header (8)
        int fileSize = 44 + preDataMetadataSize + (int)totalDataBytes + postDataMetadataSize + totalPmalChunkSize;
        int riffSize = fileSize - 8;

        byte[] buffer = new byte[fileSize];
        int pos = 0;

        // RIFF header
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes(riffSize).CopyTo(buffer, pos); pos += 4;
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(buffer, pos); pos += 4;

        // fmt chunk: WAVEFORMATEX body is exactly 16 bytes (2+2+4+4+2+2)
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes(16).CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes((short)firstFormat.Encoding).CopyTo(buffer, pos); pos += 2;
        BitConverter.GetBytes((short)totalChannels).CopyTo(buffer, pos); pos += 2;
        BitConverter.GetBytes(firstFormat.SampleRate).CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes(firstFormat.SampleRate * totalChannels * bytesPerSample).CopyTo(buffer, pos); pos += 4;
        BitConverter.GetBytes((short)(totalChannels * bytesPerSample)).CopyTo(buffer, pos); pos += 2;
        BitConverter.GetBytes((short)firstFormat.BitsPerSample).CopyTo(buffer, pos); pos += 2;

        // Pre-data metadata chunks (bEXT, iXML, INFO)
        if (mergedBext != null)
        {
            WriteChunk(buffer, ref pos, "bext", mergedBext);
        }
        if (hasIxml && ixmlData0 != null)
        {
            WriteChunk(buffer, ref pos, "ixml", ixmlData0);
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
        int dstPos = pos;
        for (int sample = 0; sample < maxSamples; sample++)
        {
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
        pos += (int)totalDataBytes;

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

    /// <summary>
    /// Decodes LTC timecode from the first 10 seconds of a reader's audio (mixed to mono).
    /// Returns null when no valid timecode is found or the sample encoding is unsupported.
    /// </summary>
    internal static LtcDecoder.Timecode? DecodeLtcFromReader(WaveFileReader reader, WaveFormat format)
    {
        reader.Position = 0;
        int toRead = (int)Math.Min(reader.Length, (long)format.SampleRate * 10 * format.BlockAlign);
        if (toRead <= 0) return null;
        byte[] raw = new byte[toRead];
        int got = reader.Read(raw, 0, toRead);
        int frames = got / format.BlockAlign;
        float[] mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int ch = 0; ch < format.Channels; ch++)
                sum += SampleToFloat(raw, i * format.BlockAlign + ch * format.BitsPerSample / 8, format);
            mono[i] = sum / format.Channels;
        }
        return LtcDecoder.Decode(mono, format.SampleRate);
    }

    private static float SampleToFloat(byte[] data, int offset, WaveFormat format) => format.BitsPerSample switch
    {
        8 => (data[offset] - 128) / 128f,
        16 => BitConverter.ToInt16(data, offset) / 32768f,
        24 => ((sbyte)data[offset + 2] << 16 | data[offset + 1] << 8 | data[offset]) / 8388608f,
        32 when format.Encoding == WaveFormatEncoding.IeeeFloat => BitConverter.ToSingle(data, offset),
        32 => BitConverter.ToInt32(data, offset) / 2147483648f,
        // shortcut: exotic encodings (a-law etc.) yield silence, so LTC is simply not found
        _ => 0f
    };

    /// <summary>
    /// Builds the merged bext chunk: source bytes (last file, fallback first) with the AES3 spec
    /// layout patched (OriginationTime 330..341 "HH:MM:SS.000", TimeReference 342..349), or a
    /// minimal spec bext when the source has none. Raw bytes are patched because the Zoom F6
    /// writes a non-conforming bext (8-char OriginationTime, TimeReference at 338); patching in
    /// place preserves UMID/CodingHistory while normalizing the layout for spec readers.
    /// </summary>
    private static byte[]? BuildMergedBext(TrackMetadata[] trackMetas, string[] inputFiles, LtcDecoder.Timecode? tc, int sampleRate)
    {
        int sourceIndex = trackMetas[^1].Bext != null ? trackMetas.Length - 1 : 0;
        var source = trackMetas[sourceIndex].Bext;
        if (source == null && !tc.HasValue) return null;
        if (!tc.HasValue) return source!.Serialize();

#pragma warning disable CS0618 // raw bytes needed; the typed model does not preserve the AES3 layout
        byte[]? raw = WavMetadataChunk.ReadWavChunksRaw(Path.GetFullPath(inputFiles[sourceIndex]))
            .FirstOrDefault(c => c.ChunkId.ToLowerInvariant() == "bext")?.Data;
#pragma warning restore CS0618

        byte[] data;
        if (raw != null && raw.Length >= 602)
        {
            data = (byte[])raw.Clone();
        }
        else
        {
            // minimal spec-layout bext (version 0, empty description/UMID), 602 bytes
            data = new byte[602];
            WriteAsciiField(data, 256, string.IsNullOrEmpty(source?.Originator) ? "WAVUtils" : source!.Originator, 32);
            WriteAsciiField(data, 288, source?.OriginatorReference ?? string.Empty, 32);
            // shortcut: OriginationDate keeps the source date; the exact calendar date of the
            // timecode is unknown (LTC carries time of day only)
            WriteAsciiField(data, 320, source?.OriginationDate ?? string.Empty, 10);
        }

        long timeRef = LtcDecoder.ToSampleReference(tc.Value, sampleRate);
        // zero the field region first: a non-conforming source (F6) has stale TR bytes under
        // where the 12-char OriginationTime goes
        Array.Clear(data, 330, 20);
        var start = TimeSpan.FromSeconds(Math.Max(0, timeRef) / (double)sampleRate);
        WriteAsciiField(data, 330, $"{(int)start.TotalHours:00}:{start.Minutes:00}:{start.Seconds:00}.000", 12);
        BitConverter.GetBytes(timeRef).CopyTo(data, 342);
        return data;
    }

    private static void WriteAsciiField(byte[] buffer, int offset, string value, int length)
    {
        System.Text.Encoding.ASCII.GetBytes(value, 0, Math.Min(value.Length, length), buffer, offset);
    }

    /// <summary>
    /// Inserts or replaces the TIMECODE element in iXML content (under SESSION), creating a
    /// minimal BWF_XML document when the source has no iXML at all.
    /// </summary>
    private static string SetIxmlTimecode(string? content, LtcDecoder.Timecode tc)
    {
        string standard = Math.Abs(tc.Fps - 25) < 0.01 ? "EBU"
            : (Math.Abs(tc.Fps - 29.97) < 0.01 || Math.Abs(tc.Fps - 30) < 0.01) ? "NTSC"
            : "FILM";
        string tcXml =
            $"<TIMECODE><HOUR>{tc.Hours:00}</HOUR><MINUTE>{tc.Minutes:00}</MINUTE>" +
            $"<SECOND>{tc.Seconds:00}</SECOND><FRAMES>{tc.Frames:00}</FRAMES><SUBFRAME>000000</SUBFRAME>" +
            $"<DROP FRAME>{(tc.DropFrame ? 1 : 0)}</DROP FRAME><24 HOUR MAX LIMIT>0</24 HOUR MAX LIMIT>" +
            $"<VIDEO STANDARD>{standard}</VIDEO STANDARD></TIMECODE>";

        if (string.IsNullOrWhiteSpace(content))
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?><BWF_XML><SESSION>" + tcXml + "</SESSION></BWF_XML>";
        if (Regex.IsMatch(content, @"<TIMECODE[\s>]"))
            return Regex.Replace(content, @"<TIMECODE[\s>].*?</TIMECODE>", tcXml, RegexOptions.Singleline);

        int idx = content.IndexOf("<SESSION", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            int gt = content.IndexOf('>', idx);
            return content.Insert(gt + 1, tcXml);
        }
        idx = content.LastIndexOf("</SESSION>", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return content.Insert(idx, tcXml);
        idx = content.LastIndexOf("</BWF_XML>", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return content.Insert(idx, "<SESSION>" + tcXml + "</SESSION>");
        return content + tcXml;
    }
}
