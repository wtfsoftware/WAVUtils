namespace WAVUtils.Core;

/// <summary>
/// Handles splitting a multi-channel WAV into individual tracks using PMAL metadata.
/// Each track is reconstructed with its original channel count (mono/stereo/multi).
/// All WAV chunks from each original track are preserved.
/// </summary>
public static class SplitCommand
{
    public static Task<string[]> Run(string inputMergedFile, string? outputDirectory = null,
        Action<double, string>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => RunCore(inputMergedFile, outputDirectory, progress, cancellationToken), cancellationToken);

    private static string[] RunCore(string inputMergedFile, string? outputDirectory,
        Action<double, string>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string mergedPath = Path.GetFullPath(inputMergedFile);
        if (!File.Exists(mergedPath))
            throw new FileNotFoundException(string.Format(LocalizationManager.GetResource("SplitFileNotFound"), mergedPath));

        using var source = new FileStream(mergedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 262144, FileOptions.SequentialScan);
        WavChunksResult wav = WavMetadataChunk.ReadFile(source, cancellationToken);
        TrackMetadata[] tracks = WavMetadataChunk.ReadTracks(wav);
        var mergedFormat = new WavAudioFormat(wav.FormatData);
        outputDirectory = Path.GetFullPath(string.IsNullOrEmpty(outputDirectory) ? Path.GetDirectoryName(mergedPath)! : outputDirectory);
        string[] outputFiles = tracks.Select(track => Path.Combine(outputDirectory, track.FileName)).ToArray();
        var formats = tracks.Select(track => track.FormatData == null ? mergedFormat.WithChannels(track.Channels) : new WavAudioFormat(track.FormatData)).ToArray();
        var chunks = tracks.Select(track => WavChunkParsers.SerializeTypedMetadata(new WavFileMetadata
        {
            Bext = track.Bext, Ixml = track.Ixml, Info = track.Info,
            Cue = track.Cue, Smpl = track.Smpl, Adtl = track.Adtl
        }, track.Chunks).ToList()).ToArray();
        long metadataBytes = 0;
        for (int i = 0; i < tracks.Length; i++)
        {
            WavMetadataChunk.ValidateChunks(chunks[i]);
            metadataBytes += chunks[i].Sum(chunk => (long)chunk.Data.Length);
            if (metadataBytes > WavMetadataChunk.MaxMetadataBytes) throw new InvalidDataException("Split metadata exceeds the safety budget.");
            if (File.Exists(outputFiles[i]) || Directory.Exists(outputFiles[i]))
                throw new IOException("Split destination already exists: " + tracks[i].FileName);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(outputDirectory);
        var destinations = new FileStream?[tracks.Length];
        var writers = new Rf64WaveFileWriter?[tracks.Length];
        var temporary = new string?[tracks.Length];
        try
        {
            for (int i = 0; i < tracks.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                temporary[i] = Path.Combine(outputDirectory, ".wavutils-" + Guid.NewGuid().ToString("N") + ".tmp");
                destinations[i] = new FileStream(temporary[i]!, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                writers[i] = new Rf64WaveFileWriter(destinations[i]!, formats[i]);
                WavMetadataChunk.AddChunks(writers[i]!, chunks[i]);
            }
            int blockFrames = Math.Max(1, 262144 / mergedFormat.BlockAlign);
            byte[] buffer = new byte[blockFrames * mergedFormat.BlockAlign];
            byte[][] outputBuffers = formats.Select(format => new byte[blockFrames * format.BlockAlign]).ToArray();
            long totalFrames = wav.DataLength / mergedFormat.BlockAlign;
            source.Position = wav.DataOffset;
            progress?.Invoke(0.1, LocalizationManager.GetResource("AppSplitOperation"));
            for (long frame = 0; frame < totalFrames; frame += blockFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min(blockFrames, totalFrames - frame);
                source.ReadExactly(buffer.AsSpan(0, count * mergedFormat.BlockAlign));
                int channelOffset = 0;
                for (int track = 0; track < tracks.Length; track++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int stride = formats[track].BlockAlign;
                    int available = (int)Math.Max(0, Math.Min(count, tracks[track].DataLength / stride - frame));
                    for (int sample = 0; sample < available; sample++)
                        Buffer.BlockCopy(buffer, sample * mergedFormat.BlockAlign + channelOffset,
                            outputBuffers[track], sample * stride, stride);
                    writers[track]!.Write(outputBuffers[track], 0, available * stride);
                    channelOffset += stride;
                }
                progress?.Invoke(0.1 + 0.8 * (frame + count) / totalFrames, LocalizationManager.GetResource("AppSplitOperation"));
            }
            for (int i = 0; i < tracks.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writers[i]!.Dispose();
                writers[i] = null;
                destinations[i]!.Flush(flushToDisk: true);
                destinations[i]!.Dispose();
                destinations[i] = null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < tracks.Length; i++)
            {
                // Each move is atomic, not the batch: never roll back by deleting published tracks.
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary[i]!, outputFiles[i], overwrite: false);
                temporary[i] = null;
            }
            progress?.Invoke(1, LocalizationManager.GetResource("ProgressComplete"));
            return outputFiles;
        }
        finally
        {
            var errors = new List<Exception>();
            for (int i = 0; i < tracks.Length; i++)
            {
                try { writers[i]?.Dispose(); }
                catch (Exception ex) { errors.Add(ex); }
                try { destinations[i]?.Dispose(); }
                catch (Exception ex) { errors.Add(ex); }
                try { if (temporary[i] != null) File.Delete(temporary[i]!); }
                catch (Exception ex) { errors.Add(ex); }
            }
            if (errors.Count > 0) throw new AggregateException("Split temporary-file cleanup failed.", errors);
        }
    }
}
