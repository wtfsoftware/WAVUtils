using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NAudio.Wave;

namespace WAVUtils.Core;

/// <summary>
/// Stamps existing WAV files' bext with the timecode recorded in their audio: patches
/// TimeReference and OriginationTime in place so files from a free-running recorder join
/// the timecode generator's timeline. Mirrors MergeCommand.BuildMergedBext (raw AES3 patch;
/// only the 20-byte OriginationTime+TimeReference block changes, normalized to spec layout).
/// Re-running on a stamped file is a no-op.
/// With tcSource, the timecode is decoded once from that file and applied to targets whose
/// own recorder-clock bext is shifted by the same amount - for sibling tracks (lav mics)
/// that start with the TC track but carry no LTC themselves.
/// </summary>
public static class TcCommand
{
    public static IReadOnlyList<string> Run(string[] files, string? tcSource = null)
    {
        (LtcDecoder.Timecode Tc, long Decoded, long BaseOld, int Rate)? from = null;
        if (tcSource != null)
        {
            LtcDecoder.Timecode? tc;
            int rate;
            using (var reader = new WaveFileReader(tcSource))
            {
                tc = MergeCommand.DecodeLtcFromReader(reader, reader.WaveFormat);
                rate = reader.WaveFormat.SampleRate;
            }
            if (tc is null)
                return new[] { $"{tcSource}: no LTC found, nothing applied" };
            long decoded = LtcDecoder.ToSampleReference(tc.Value, rate);
            long baseOld;
            using (var s = new FileStream(tcSource, FileMode.Open, FileAccess.Read))
            {
                long body = FindBextBody(s);
                // shortcut: a source without bext is assumed to start exactly at the decoded timecode
                baseOld = body >= 0 && TryReadTimeRef(s, body, out long v) ? v : decoded;
            }
            from = (tc.Value, decoded, baseOld, rate);
        }

        var results = new List<string>();
        foreach (var file in files)
        {
            try { results.Add(PatchOne(file, from)); }
            catch (Exception ex) { results.Add($"{file}: ERROR {ex.Message}"); }
        }
        return results;
    }

    private static string PatchOne(string file, (LtcDecoder.Timecode Tc, long Decoded, long BaseOld, int Rate)? from)
    {
        LtcDecoder.Timecode tc;
        long newRef;
        int sampleRate;
        using (var reader = new WaveFileReader(file))
        {
            sampleRate = reader.WaveFormat.SampleRate;
            if (from is null)
            {
                LtcDecoder.Timecode? own = MergeCommand.DecodeLtcFromReader(reader, reader.WaveFormat);
                if (own is null)
                    return $"{file}: no LTC found, file untouched";
                tc = own.Value;
                newRef = LtcDecoder.ToSampleReference(own.Value, sampleRate);
            }
            else
            {
                if (sampleRate != from.Value.Rate)
                    return $"{file}: sample rate {sampleRate} != source {from.Value.Rate}, skipped";
                tc = from.Value.Tc;
                newRef = from.Value.Decoded;
            }
        }

        using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite);
        long bodyPos = FindBextBody(stream);
        if (bodyPos < 0)
            return $"{file}: no bext chunk, file untouched";
        if (!TryReadTimeRef(stream, bodyPos, out long oldRef))
            return $"{file}: unreadable bext, file untouched";
        if (from is not null)
        {
            // A stamped file no longer carries the recorder clock: applying a delta twice
            // (or to a mismatched set) would push the value out of the day range - refuse it.
            if (!InDayRange(oldRef, sampleRate) || !InDayRange(from.Value.BaseOld, sampleRate))
                return $"{file}: bext clock out of day range ({oldRef}/{from.Value.BaseOld}), file untouched";
            newRef += oldRef - from.Value.BaseOld;
        }
        if (!InDayRange(newRef, sampleRate))
            return $"{file}: computed TimeRef {newRef} out of day range, file untouched";
        if (oldRef == newRef)
            return $"{file}: already {FormatClock(newRef, sampleRate)} @ {FormatTc(tc)}";

        // Normalize to the AES3 layout in one 20-byte write: OriginationTime "HH:MM:SS.000" at
        // 330..341 + TimeReference at 342..349. This also overwrites stale TR bytes a
        // non-conforming source (F6) left at 338.
        var start = TimeSpan.FromSeconds(newRef / (double)sampleRate);
        byte[] block = new byte[20];
        Encoding.ASCII.GetBytes($"{(int)start.TotalHours:00}:{start.Minutes:00}:{start.Seconds:00}.000").CopyTo(block, 0);
        BitConverter.GetBytes(newRef).CopyTo(block, 12);
        stream.Position = bodyPos + 330;
        stream.Write(block);
        return $"{file}: {FormatClock(oldRef, sampleRate)} -> {FormatClock(newRef, sampleRate)} @ {FormatTc(tc)} (TimeRef {oldRef} -> {newRef})";
    }

    private static bool InDayRange(long timeRef, int sampleRate) => timeRef >= 0 && timeRef < 86400L * sampleRate;

    private static string FormatTc(LtcDecoder.Timecode tc) =>
        $"{tc.Hours:00}:{tc.Minutes:00}:{tc.Seconds:00}:{tc.Frames:00} {tc.Fps:0.##}{(tc.DropFrame ? "DF" : "NDF")}";

    private static string FormatClock(long timeRef, int sampleRate)
    {
        long total = timeRef / sampleRate;
        long frac = timeRef % sampleRate * 1000 / sampleRate;
        return $"{total / 3600:00}:{total % 3600 / 60:00}:{total % 60:00}.{frac:000}";
    }

    private static bool TryReadTimeRef(FileStream stream, long bodyPos, out long timeRef)
    {
        stream.Position = bodyPos + 330;
        byte[] buf = new byte[20];
        try { stream.ReadExactly(buf); }
        catch (EndOfStreamException) { timeRef = 0; return false; }
        // spec OriginationTime is 12 chars ("HH:MM:SS.mmm"), so a '.' at 338 means the TR
        // follows at 342; otherwise assume the F6 layout (8-char time, TR at 338)
        int off = buf[8] == (byte)'.' ? 12 : 8;
        timeRef = BitConverter.ToInt64(buf, off);
        return true;
    }

    // Walks RIFF chunk headers, returns the body offset of the first bext chunk with the
    // spec-minimum 602 bytes (TimeReference lives at body+342), or -1 when absent.
    private static long FindBextBody(FileStream stream)
    {
        stream.Position = 12;
        byte[] header = new byte[8];
        while (stream.Position + 8 <= stream.Length)
        {
            try { stream.ReadExactly(header, 0, 8); }
            catch (EndOfStreamException) { break; }
            long len = BitConverter.ToUInt32(header, 4);
            long body = stream.Position;
            if (Encoding.ASCII.GetString(header, 0, 4).Equals("bext", StringComparison.OrdinalIgnoreCase) && len >= 602)
                return body;
            if (len > stream.Length) return -1; // corrupt header: stop, do not wander
            stream.Position = body + len + (len & 1);
        }
        return -1;
    }
}
