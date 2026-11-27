using NAudio.Wave;
using System.Buffers.Binary;

namespace WAVUtils.Core;

/// <summary>
/// Handles merging multiple mono/stereo/multi-channel WAV files into a single multi-channel WAV.
/// Each input file's channels are placed sequentially in the merged output.
/// Shorter files are padded with silence. All WAV chunks (bext, PMAP, custom) are preserved.
/// </summary>
public static class MergeCommand
{
    public static Task<string> Run(string[] inputFiles, string? outputPath = null,
        Action<double, string>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => RunCore(inputFiles, outputPath, progress, cancellationToken), cancellationToken);

    private static string RunCore(string[] inputFiles, string? outputPath,
        Action<double, string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputFiles);
        if (inputFiles.Length < 2 || inputFiles.Length > WavMetadataChunk.MaxTracks)
            throw new ArgumentException(LocalizationManager.GetResource("MergeMinFiles"));
        cancellationToken.ThrowIfCancellationRequested();
        string[] paths = inputFiles.Select(Path.GetFullPath).ToArray();
        outputPath = Path.GetFullPath(string.IsNullOrEmpty(outputPath)
            ? Path.Combine(Path.GetDirectoryName(paths[0])!, string.Format(LocalizationManager.GetResource("MergeDefaultFilename"),
                DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss"))) : outputPath);
        if (File.Exists(outputPath) || Directory.Exists(outputPath) || paths.Contains(outputPath, StringComparer.OrdinalIgnoreCase))
            throw new IOException("Merge destination already exists or is an input file.");

        var streams = new FileStream?[paths.Length];
        var wavs = new WavChunksResult[paths.Length];
        var formats = new WavAudioFormat[paths.Length];
        var tracks = new TrackMetadata[paths.Length];
        string? temporary = null;
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long metadataBytes = 0;
            int channelCount = 0;
            long frameCount = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileName(paths[i]);
                WavMetadataChunk.ValidateFileName(name);
                if (!names.Add(name)) throw new ArgumentException("Input track names must be unique for splitting.");
                streams[i] = new FileStream(paths[i], FileMode.Open, FileAccess.Read, FileShare.Read, 262144, FileOptions.SequentialScan);
                wavs[i] = WavMetadataChunk.ReadFile(streams[i]!, cancellationToken);
                formats[i] = new WavAudioFormat(wavs[i].FormatData);
                formats[i].ValidateDataLength(wavs[i].DataLength);
                if (i > 0 && !formats[0].IsCompatibleWith(formats[i]))
                    throw new ArgumentException("Input sample rates, encodings, bit depths and valid bits must match.");
                metadataBytes += wavs[i].Chunks.Sum(chunk => (long)chunk.Data.Length);
                if (metadataBytes > WavMetadataChunk.MaxMetadataBytes) throw new InvalidDataException("Combined metadata exceeds the safety budget.");
                channelCount = checked(channelCount + formats[i].Channels);
                frameCount = Math.Max(frameCount, wavs[i].DataLength / formats[i].BlockAlign);
                var metadata = wavs[i].TypedMetadata;
                tracks[i] = new TrackMetadata
                {
                    Index = i, FileName = name, Channels = formats[i].Channels, DataLength = wavs[i].DataLength,
                    FormatData = wavs[i].FormatData, Chunks = wavs[i].Chunks,
                    Bext = metadata.Bext, Ixml = metadata.Ixml, Info = metadata.Info,
                    Cue = metadata.Cue, Smpl = metadata.Smpl, Adtl = metadata.Adtl
                };
                progress?.Invoke(0.1 * (i + 1) / paths.Length, LocalizationManager.GetResource("MergeReading"));
            }
            WavAudioFormat outputFormat = formats[0].WithChannels(channelCount);
            _ = checked(frameCount * outputFormat.BlockAlign);
            var tc = DecodeLtcFromReader(streams[^1]!, wavs[^1], cancellationToken);
            var chunks = WavChunkParsers.SerializeTypedMetadata(wavs[0].TypedMetadata, wavs[0].Chunks)
                .Where(chunk => !chunk.ChunkId.Equals("bext", StringComparison.OrdinalIgnoreCase) && chunk.ChunkId is not ("pmal" or "wtcp")).ToList();
            byte[]? bext = BuildMergedBext(tracks, paths, tc, formats[0].SampleRate);
            if (bext != null) chunks.Insert(0, new WavChunkData { ChunkId = "bext", Data = bext });
            if (tc.HasValue)
            {
                chunks.RemoveAll(chunk => chunk.ChunkId.Equals("iXML", StringComparison.OrdinalIgnoreCase));
                chunks.Add(new WavChunkData { ChunkId = "iXML", Data = new IxmlChunkData
                {
                    Content = IxmlChunkData.StampTimecode(content: tracks[0].Ixml?.Content, tc: tc.Value,
                        sampleRate: formats[0].SampleRate, timeReference: LtcDecoder.ToSampleReference(tc.Value, formats[0].SampleRate))
                }.Serialize() });
            }
            chunks.Add(new WavChunkData { ChunkId = "pmal", Data = WavMetadataChunk.SerializeTracks(tracks) });
            WavMetadataChunk.ValidateChunks(chunks);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            temporary = Path.Combine(Path.GetDirectoryName(outputPath)!, ".wavutils-" + Guid.NewGuid().ToString("N") + ".tmp");
            progress?.Invoke(0.15, LocalizationManager.GetResource("MergeMerging"));
            using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var writer = new Rf64WaveFileWriter(destination, outputFormat))
                {
                    WavMetadataChunk.AddChunks(writer, chunks);
                    int blockFrames = Math.Max(1, 262144 / outputFormat.BlockAlign);
                    byte[] outputBuffer = new byte[blockFrames * outputFormat.BlockAlign];
                    byte[][] inputBuffers = formats.Select(format => new byte[blockFrames * format.BlockAlign]).ToArray();
                    for (int i = 0; i < streams.Length; i++) streams[i]!.Position = wavs[i].DataOffset;
                    byte silence = outputFormat.SampleEncoding == WaveFormatEncoding.Pcm && outputFormat.BitsPerSample == 8 ? (byte)128 : (byte)0;
                    for (long frame = 0; frame < frameCount; frame += blockFrames)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = (int)Math.Min(blockFrames, frameCount - frame);
                        Array.Fill(outputBuffer, silence);
                        int channelOffset = 0;
                        for (int track = 0; track < tracks.Length; track++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int stride = formats[track].BlockAlign;
                            int available = (int)Math.Max(0, Math.Min(count, wavs[track].DataLength / stride - frame));
                            streams[track]!.ReadExactly(inputBuffers[track].AsSpan(0, available * stride));
                            for (int sample = 0; sample < available; sample++)
                                Buffer.BlockCopy(inputBuffers[track], sample * stride, outputBuffer,
                                    sample * outputFormat.BlockAlign + channelOffset, stride);
                            channelOffset += stride;
                        }
                        writer.Write(outputBuffer, 0, count * outputFormat.BlockAlign);
                        progress?.Invoke(0.15 + 0.8 * (frame + count) / frameCount, LocalizationManager.GetResource("MergeMerging"));
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
                destination.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, outputPath, overwrite: false);
            temporary = null;
            progress?.Invoke(1, LocalizationManager.GetResource("MergeDone"));
            return outputPath;
        }
        finally
        {
            foreach (var stream in streams) stream?.Dispose();
            if (temporary != null) File.Delete(temporary);
        }
    }

    /// <summary>
    /// Decodes up to 10 seconds per channel, without mixing unrelated audio or opposite polarities.
    /// </summary>
    internal static LtcDecoder.Timecode? DecodeLtcFromReader(Stream reader, WavChunksResult wav,
        CancellationToken cancellationToken = default)
    {
        var format = new WavAudioFormat(wav.FormatData);
        int frames = (int)Math.Min(wav.DataLength / format.BlockAlign, Math.Min((long)format.SampleRate * 10, 4 * 1024 * 1024));
        if (frames == 0) return null;
        float[] mono = new float[frames];
        int blockFrames = Math.Max(1, 262144 / format.BlockAlign);
        byte[] raw = new byte[blockFrames * format.BlockAlign];
        LtcDecoder.Timecode? found = null;
        for (int channel = 0; channel < format.Channels; channel++)
        {
            reader.Position = wav.DataOffset;
            for (int frame = 0; frame < frames; frame += blockFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(blockFrames, frames - frame);
                reader.ReadExactly(raw.AsSpan(0, count * format.BlockAlign));
                for (int sample = 0; sample < count; sample++)
                    mono[frame + sample] = SampleToFloat(raw, sample * format.BlockAlign + channel * format.BitsPerSample / 8, format);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var tc = LtcDecoder.Decode(mono, format.SampleRate, cancellationToken);
            if (!tc.HasValue) continue;
            if (found.HasValue && Math.Abs(LtcDecoder.ToSampleReference(found.Value, format.SampleRate) -
                LtcDecoder.ToSampleReference(tc.Value, format.SampleRate)) > 2)
                throw new InvalidDataException("Conflicting LTC channels; select a clean donor track.");
            found = tc;
        }
        return found;
    }

    private static float SampleToFloat(byte[] data, int offset, WavAudioFormat format) => format.BitsPerSample switch
    {
        8 => (data[offset] - 128) / 128f,
        16 => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset)) / 32768f,
        24 => ((sbyte)data[offset + 2] << 16 | data[offset + 1] << 8 | data[offset]) / 8388608f,
        32 when format.SampleEncoding == WaveFormatEncoding.IeeeFloat => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset)),
        32 => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)) / 2147483648f,
        64 => (float)BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(offset)),
        _ => throw new InvalidDataException("Unsupported LTC sample encoding.")
    };

    /// <summary>
    /// Preserves source bext bytes (last input, fallback first), changing only the EBU time fields.
    /// </summary>
    private static byte[]? BuildMergedBext(TrackMetadata[] trackMetas, string[] inputFiles, LtcDecoder.Timecode? tc, int sampleRate)
    {
        int sourceIndex = trackMetas[^1].Bext != null ? trackMetas.Length - 1 : 0;
        var source = trackMetas[sourceIndex].Bext;
        if (source == null && !tc.HasValue) return null;
        byte[]? raw = trackMetas[sourceIndex].Chunks.FirstOrDefault(c => c.ChunkId.Equals("bext", StringComparison.OrdinalIgnoreCase))?.Data
            ?? source?.Serialize();
        if (!tc.HasValue) return raw;
        long timeRef = LtcDecoder.ToSampleReference(tc.Value, sampleRate);
        return BextChunkData.StampTimeReference(source: raw, timeReference: timeRef, sampleRate: sampleRate);
    }

    /// <summary>
    /// Updates standard iXML SPEED fields without changing session metadata.
    /// </summary>
    private static string SetIxmlTimecode(string? content, LtcDecoder.Timecode tc)
    {
        return IxmlChunkData.StampTimecode(content: content, tc: tc);
    }
}
