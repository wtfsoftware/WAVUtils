using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace WAVUtils.Core;

/// <summary>
/// Stamps standard bext and iXML through a temporary WAV, retaining the original backup.
/// Donor shifts use the original recorder clock stored in versioned wtcp provenance.
/// </summary>
public static class TcCommand
{
    private static readonly JsonSerializerOptions ProvenanceOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 16
    };

    private sealed record TimecodeSource(LtcDecoder.Timecode Tc, long Decoded, long OriginalReference, int Rate);

    private sealed class Provenance
    {
        public required int Version { get; init; }
        public required int SampleRate { get; init; }
        public required byte[]? OriginalBext { get; init; }
        public required string OriginalSha256 { get; init; }
        public required long AppliedReference { get; init; }
        public required LtcDecoder.Timecode Timecode { get; init; }
        public long? DonorOriginalReference { get; init; }
    }

    private sealed class StampPlan : IDisposable
    {
        public required string Path { get; init; }
        public required FileStream Source { get; init; }
        public required WavChunksResult Wav { get; init; }
        public required WavAudioFormat Format { get; init; }
        public required List<WavChunkData> Chunks { get; init; }
        public required string SourceHash { get; init; }
        public required Provenance State { get; init; }
        public required bool Changed { get; init; }
        public void Dispose() => Source.Dispose();
    }

    public static IReadOnlyList<string> Run(string[] files, string? tcSource = null,
        Action<double, string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Length == 0 || files.Length > WavMetadataChunk.MaxTracks) throw new ArgumentException("Specify 1 to 256 target files.");
        cancellationToken.ThrowIfCancellationRequested();
        string[] paths = files.Select(Path.GetFullPath).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length) throw new ArgumentException("Duplicate timecode target.");
        TimecodeSource? donor = null;
        if (tcSource != null)
        {
            string sourcePath = Path.GetFullPath(tcSource);
            using var source = File.OpenRead(sourcePath);
            var wav = WavMetadataChunk.ReadFile(source, cancellationToken);
            var format = new WavAudioFormat(wav.FormatData);
            format.ValidateDataLength(wav.DataLength);
            var state = ReadProvenance(wav, format.SampleRate);
            if (state != null)
            {
                using var backup = File.OpenRead(sourcePath + ".bak");
                VerifyBackup(backup, state, (value, text) => progress?.Invoke(0.02 * value, text), cancellationToken);
            }
            byte[]? original = state == null ? SingleChunk(wav, "bext") : state.OriginalBext;
            long originalReference = ReadReference(original, format.SampleRate)
                ?? throw new InvalidDataException("Donor has no original bext recorder clock.");
            var tc = MergeCommand.DecodeLtcFromReader(source, wav, cancellationToken)
                ?? throw new InvalidDataException("No LTC found in the donor.");
            donor = new TimecodeSource(tc, LtcDecoder.ToSampleReference(tc, format.SampleRate), originalReference, format.SampleRate);
        }
        var plans = new List<StampPlan>();
        try
        {
            long metadataBytes = 0;
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var plan = BuildPlan(path, donor, progress: (value, text) =>
                    progress?.Invoke(0.02 + 0.18 * (plans.Count + value) / paths.Length, text), cancellationToken);
                plans.Add(plan);
                metadataBytes += plan.Chunks.Sum(chunk => (long)chunk.Data.Length);
                if (metadataBytes > WavMetadataChunk.MaxMetadataBytes) throw new InvalidDataException("Combined timecode metadata exceeds the safety budget.");
            }
            var results = new List<string>();
            for (int i = 0; i < plans.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Apply(plans[i], progress: (value, text) => progress?.Invoke(0.2 + 0.8 * (i + value) / plans.Count, text), cancellationToken);
                results.Add($"{plans[i].Path}: {(plans[i].Changed ? "stamped" : "already stamped")} TimeReference {plans[i].State.AppliedReference}; backup {plans[i].Path}.bak");
            }
            progress?.Invoke(1, LocalizationManager.GetResource("ProgressComplete"));
            return results;
        }
        finally { foreach (var plan in plans) plan.Dispose(); }
    }

    // Retains the independently reproduced donor-shift seam without exposing a testing API.
    private static string PatchOne(string file, (LtcDecoder.Timecode Tc, long Decoded, long BaseOld, int Rate)? from)
    {
        var donor = from.HasValue ? new TimecodeSource(from.Value.Tc, from.Value.Decoded, from.Value.BaseOld, from.Value.Rate) : null;
        using var plan = BuildPlan(Path.GetFullPath(file), donor, progress: null, CancellationToken.None);
        Apply(plan, progress: null, CancellationToken.None);
        return $"{file}: TimeReference {plan.State.AppliedReference}";
    }

    private static StampPlan BuildPlan(string path, TimecodeSource? donor, Action<double, string>? progress, CancellationToken cancellationToken)
    {
        var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 262144, FileOptions.SequentialScan);
        try
        {
            var wav = WavMetadataChunk.ReadFile(source, cancellationToken);
            var format = new WavAudioFormat(wav.FormatData);
            format.ValidateDataLength(wav.DataLength);
            var previous = ReadProvenance(wav, format.SampleRate);
            string backup = path + ".bak";
            if (Directory.Exists(backup)) throw new IOException("Backup path is a directory.");
            using var existing = File.Exists(backup) ? File.OpenRead(backup) : null;
            if (previous != null)
            {
                if (existing == null) throw new IOException("Original backup is missing; restore it before using the stamp.");
                VerifyBackup(existing, previous, (value, text) => progress?.Invoke(0.5 * value, text), cancellationToken);
            }
            byte[]? original = previous == null ? SingleChunk(wav, "bext") : previous.OriginalBext;
            long? originalReference = ReadReference(original, format.SampleRate);
            LtcDecoder.Timecode tc;
            long reference;
            if (donor == null)
            {
                tc = MergeCommand.DecodeLtcFromReader(source, wav, cancellationToken)
                    ?? throw new InvalidDataException("No LTC found in " + Path.GetFileName(path));
                reference = LtcDecoder.ToSampleReference(tc, format.SampleRate);
            }
            else
            {
                if (donor.Rate != format.SampleRate || originalReference == null)
                    throw new InvalidDataException("Sibling stamping needs an original bext clock and the donor's sample rate.");
                tc = donor.Tc;
                reference = checked(donor.Decoded + originalReference.Value - donor.OriginalReference);
            }
            byte[] bext = BextChunkData.StampTimeReference(source: original, timeReference: reference, sampleRate: format.SampleRate);
            byte[] ixml = new IxmlChunkData { Content = IxmlChunkData.StampTimecode(content: wav.TypedMetadata.Ixml?.Content,
                tc: tc, sampleRate: format.SampleRate, timeReference: reference) }.Serialize();
            double hashStart = previous == null ? 0 : 0.5;
            double hashSpan = existing == null ? 1 : 0.5;
            string sourceHash = Hash(source, (value, text) => progress?.Invoke(hashStart + hashSpan * value, text), cancellationToken);
            if (previous == null && existing != null &&
                Hash(existing, (value, text) => progress?.Invoke(0.5 + 0.5 * value, text), cancellationToken) != sourceHash)
                throw new IOException("An unrelated original backup already exists.");
            var state = new Provenance
            {
                Version = 1, SampleRate = format.SampleRate, OriginalBext = original,
                OriginalSha256 = previous?.OriginalSha256 ?? sourceHash, AppliedReference = reference,
                Timecode = tc, DonorOriginalReference = donor?.OriginalReference
            };
            byte[] provenance = JsonSerializer.SerializeToUtf8Bytes(state, ProvenanceOptions);
            var chunks = wav.Chunks.Where(chunk => !chunk.ChunkId.Equals("bext", StringComparison.OrdinalIgnoreCase) &&
                !chunk.ChunkId.Equals("iXML", StringComparison.OrdinalIgnoreCase) && !chunk.ChunkId.Equals("wtcp", StringComparison.OrdinalIgnoreCase)).ToList();
            chunks.Insert(0, new WavChunkData { ChunkId = "bext", Data = bext });
            chunks.Add(new WavChunkData { ChunkId = "iXML", Data = ixml });
            chunks.Add(new WavChunkData { ChunkId = "wtcp", Data = provenance });
            WavMetadataChunk.ValidateChunks(chunks);
            bool changed = !bext.AsSpan().SequenceEqual(SingleChunk(wav, "bext")) ||
                !ixml.AsSpan().SequenceEqual(SingleChunk(wav, "iXML")) || !provenance.AsSpan().SequenceEqual(SingleChunk(wav, "wtcp"));
            return new StampPlan { Path = path, Source = source, Wav = wav, Format = format, Chunks = chunks,
                SourceHash = sourceHash, State = state, Changed = changed };
        }
        catch { source.Dispose(); throw; }
    }

    private static Provenance? ReadProvenance(WavChunksResult wav, int sampleRate)
    {
        byte[]? bytes = SingleChunk(wav, "wtcp");
        _ = SingleChunk(wav, "iXML");
        if (bytes == null) return null;
        Provenance state;
        try { state = JsonSerializer.Deserialize<Provenance>(bytes, ProvenanceOptions) ?? throw new InvalidDataException("Null wtcp provenance."); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid wtcp provenance.", ex); }
        if (state.Version != 1 || state.SampleRate != sampleRate || state.OriginalSha256 == null ||
            state.OriginalSha256.Length != 64 || state.OriginalSha256.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("Unsupported or invalid wtcp provenance.");
        _ = ReadReference(state.OriginalBext, sampleRate);
        byte[] expected = BextChunkData.StampTimeReference(source: state.OriginalBext, timeReference: state.AppliedReference, sampleRate: sampleRate);
        if (!expected.AsSpan().SequenceEqual(SingleChunk(wav, "bext")))
            throw new InvalidDataException("bext no longer matches the stored stamp; refusing an ambiguous recorder clock.");
        return state;
    }

    private static void VerifyBackup(Stream backup, Provenance state, Action<double, string>? progress, CancellationToken cancellationToken)
    {
        if (Hash(backup, progress, cancellationToken) != state.OriginalSha256)
            throw new IOException("An unrelated original backup already exists.");
        byte[]? original = SingleChunk(WavMetadataChunk.ReadFile(backup, cancellationToken), "bext");
        if ((original == null) != (state.OriginalBext == null) || !original.AsSpan().SequenceEqual(state.OriginalBext))
            throw new InvalidDataException("Original bext does not match the backup.");
    }

    private static byte[]? SingleChunk(WavChunksResult wav, string id)
    {
        var chunks = wav.Chunks.Where(chunk => chunk.ChunkId.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (chunks.Length > 1) throw new InvalidDataException("Duplicate " + id + " chunk.");
        return chunks.FirstOrDefault()?.Data;
    }

    private static long? ReadReference(byte[]? bext, int sampleRate)
    {
        if (bext == null) return null;
        if (bext.Length < 602) throw new InvalidDataException("Truncated bext chunk.");
        if (bext.AsSpan(338, 4).SequenceEqual(".000"u8))
            throw new InvalidDataException("Possible legacy shifted bext layout; explicit backup-based migration is required.");
        long reference = BinaryPrimitives.ReadInt64LittleEndian(bext.AsSpan(338));
        if (reference < 0 || reference >= 86400L * sampleRate) throw new InvalidDataException("Original bext clock is outside the timecode day.");
        return reference;
    }

    private static string Hash(Stream stream, Action<double, string>? progress, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[262144];
        string checking = LocalizationManager.GetResource("TimecodeCheckingOriginal");
        while (stream.Position < stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = (int)Math.Min(buffer.Length, stream.Length - stream.Position);
            stream.ReadExactly(buffer.AsSpan(0, count));
            hash.AppendData(buffer, 0, count);
            progress?.Invoke((double)stream.Position / stream.Length, checking);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Apply(StampPlan plan, Action<double, string>? progress, CancellationToken cancellationToken)
    {
        if (!plan.Changed) { progress?.Invoke(1, LocalizationManager.GetResource("ProgressComplete")); return; }
        string directory = Path.GetDirectoryName(plan.Path)!;
        string? temporary = Path.Combine(directory, ".wavutils-" + Guid.NewGuid().ToString("N") + ".tmp");
        string? backupTemporary = null;
        string? rollback = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var writer = new Rf64WaveFileWriter(output, plan.Format))
                {
                    WavMetadataChunk.AddChunks(writer, plan.Chunks);
                    byte[] buffer = new byte[Math.Max(1, 262144 / plan.Format.BlockAlign) * plan.Format.BlockAlign];
                    plan.Source.Position = plan.Wav.DataOffset;
                    string writing = LocalizationManager.GetResource("TimecodeWriting");
                    for (long written = 0; written < plan.Wav.DataLength;)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = (int)Math.Min(buffer.Length, plan.Wav.DataLength - written);
                        plan.Source.ReadExactly(buffer.AsSpan(0, count));
                        writer.Write(buffer, 0, count);
                        written += count;
                        progress?.Invoke(0.7 * written / plan.Wav.DataLength, writing);
                    }
                }
                output.Flush(flushToDisk: true);
                _ = WavMetadataChunk.ReadFile(output, cancellationToken);
            }
            string backup = plan.Path + ".bak";
            if (!File.Exists(backup))
            {
                backupTemporary = Path.Combine(directory, ".wavutils-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var output = new FileStream(backupTemporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    plan.Source.Position = 0;
                    byte[] buffer = new byte[262144];
                    string saving = LocalizationManager.GetResource("TimecodeSavingBackup");
                    while (plan.Source.Position < plan.Source.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = (int)Math.Min(buffer.Length, plan.Source.Length - plan.Source.Position);
                        plan.Source.ReadExactly(buffer.AsSpan(0, count));
                        output.Write(buffer, 0, count);
                        progress?.Invoke(0.7 + 0.2 * plan.Source.Position / plan.Source.Length, saving);
                    }
                    output.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(backupTemporary, backup, overwrite: false);
                backupTemporary = null;
            }
            using (var existing = File.OpenRead(backup))
                if (Hash(existing, progress: (value, text) => progress?.Invoke(0.9 + 0.05 * value, text), cancellationToken) != plan.State.OriginalSha256)
                    throw new IOException("Original backup changed during stamping.");
            cancellationToken.ThrowIfCancellationRequested();
            string verifying = LocalizationManager.GetResource("TimecodeVerifying");
            plan.Source.Dispose();
            rollback = Path.Combine(directory, ".wavutils-" + Guid.NewGuid().ToString("N") + ".rollback");
            File.Replace(temporary, plan.Path, rollback, ignoreMetadataErrors: false);
            temporary = null;
            // Keep rollback until verification succeeds; cancelling a long hash restores the previous WAV.
            using (var previous = File.OpenRead(rollback))
                if (Hash(previous, progress: (value, _) => progress?.Invoke(0.95 + 0.05 * value, verifying), cancellationToken) != plan.SourceHash)
                    throw new IOException("WAV changed concurrently before replacement.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(rollback);
            rollback = null;
            progress?.Invoke(1, LocalizationManager.GetResource("ProgressComplete"));
        }
        catch (Exception failure)
        {
            if (rollback != null && File.Exists(rollback))
            {
                try { File.Replace(rollback, plan.Path, destinationBackupFileName: null, ignoreMetadataErrors: false); rollback = null; }
                catch (Exception recovery) { throw new AggregateException("Timecode failed and rollback needs manual recovery: " + rollback, failure, recovery); }
            }
            throw;
        }
        finally
        {
            if (temporary != null) File.Delete(temporary);
            if (backupTemporary != null) File.Delete(backupTemporary);
        }
    }
}
