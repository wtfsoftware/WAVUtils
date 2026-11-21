namespace WAVUtils.Core;

/// <summary>
/// Minimal LTC (biphase-mark) timecode decoder, ported from libltc's decoder.c.
/// Scans mono audio for the first valid SMPTE LTC frame and reports its timecode
/// and the sample offset of the frame start relative to the audio start.
/// Only forward-running timecode is detected (reverse sync word is ignored).
/// </summary>
public static class LtcDecoder
{
    public readonly record struct Timecode(
        int Hours, int Minutes, int Seconds, int Frames,
        bool DropFrame, double Fps, long SampleOffset);

    private static readonly double[] FpsCandidates = { 23.976, 24, 25, 29.97, 30 };

    public static Timecode? Decode(IReadOnlyList<float> samples, int sampleRate)
    {
        // State ported from libltc LTCDecoder (ltc_decoder_create defaults).
        double center = 128.0;
        double min = center, max = center;
        int state = 0;            // snd_to_biphase_state
        int biphaseState = 1;     // biphase_state
        int prev = 0;             // biphase_prev
        double period = sampleRate / (30.0 * 80.0); // initial guess, converges from signal
        double limit = period * 3 / 4;
        long count = 0;           // snd_to_biphase_cnt

        int bitCnt = 0;
        int syncWord = 0;
        byte[] frame = new byte[10];
        double frameStartOff = 0;
        double frameStartPrev = -1;

        var detected = new List<(byte[] Frame, double Off)>(2);
        bool done = false;

        // libltc's min/max tracker works on an 8-bit full-scale signal; quiet LTC
        // (e.g. -40 dBFS from a Zoom LTC input) would quantize to +/-1 LSB and be
        // destroyed by threshold jitter. Scale to full scale first.
        // shortcut: peak = global max; an isolated transient much louder than the
        // LTC lowers the effective level - switch to a percentile peak if seen.
        float peak = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            float a = Math.Abs(samples[i]);
            if (a > peak) peak = a;
        }
        if (peak < 1e-4f) return null;
        double scale = 127.0 / peak;

        for (int i = 0; i < samples.Count && !done; i++)
        {
            int v = (int)(128 + Math.Clamp(samples[i] * scale, -127.0, 127.0));

            // track minimum and maximum values (slow decay toward center)
            min = center - ((center - min) * 15 / 16);
            max = center + ((max - center) * 15 / 16);
            if (v < min) min = v;
            if (v > max) max = v;

            double minThreshold = center - ((center - min) * 8 / 16);
            double maxThreshold = center + ((max - center) * 8 / 16);

            if ((state != 0 && v > maxThreshold) || (state == 0 && v < minThreshold))
            {
                if (count > limit)
                {
                    Decode2(0); Decode2(0); // single transition in a bit period -> 0
                }
                else
                {
                    count *= 2;             // half-period transition -> 1 (with its neighbor)
                    Decode2(0);
                }

                if (count > period * 4)
                {
                    bitCnt = 0;             // long silence: reset parser
                }
                else
                {
                    period = (period * 3.0 + count) / 4.0;
                    limit = period * 3 / 4;
                }
                count = 0;
                state ^= 1;
            }
            count++;

            void Decode2(double pos)
            {
                if (count <= 2 * period) pos -= period - count;
                if (state == prev)
                {
                    biphaseState = 1;
                    ParseLtc(0, i, pos);
                }
                else
                {
                    biphaseState = 1 - biphaseState;
                    if (biphaseState == 1) ParseLtc(1, i, pos);
                }
                prev = state;
            }
        }

        void ParseLtc(int bit, long offset, double pos)
        {
            if (bitCnt == 0)
            {
                Array.Clear(frame);
                frameStartOff = frameStartPrev < 0 ? pos - period : frameStartPrev;
            }
            frameStartPrev = offset + pos;

            if (bitCnt >= 80)
            {
                // shift bits backwards by one
                for (int k = 0; k < 10; k++)
                {
                    byte next = k + 1 < 10 ? frame[k + 1] : (byte)0;
                    frame[k] = (byte)((frame[k] >> 1) | ((next & 1) << 7));
                }
                // accumulate the float period (libltc's per-bit ceil() biases
                // non-integer periods +1 sample/bit and breaks 29.97 fps estimation)
                frameStartOff += period;
                bitCnt--;
            }

            syncWord = (syncWord << 1) & 0xFFFF;
            if (bit != 0)
            {
                syncWord |= 1;
                if (bitCnt < 80)
                    frame[bitCnt >> 3] |= (byte)(1 << (bitCnt & 7));
            }
            bitCnt++;

            if (syncWord == 0x3FFD /* forward sync word */)
            {
                if (bitCnt == 80)
                {
                    detected.Add(((byte[])frame.Clone(), frameStartOff));
                    if (detected.Count >= 2) done = true;
                }
                bitCnt = 0;
            }
        }

        if (detected.Count == 0) return null;

        // Estimate FPS: from the distance between two consecutive frames, else from bit period.
        double estFps = detected.Count == 2
            ? sampleRate / (detected[1].Off - detected[0].Off)
            : sampleRate / (period * 80.0);
        // shortcut: a 2-frame baseline cannot separate 29.97 from 30 (0.1% apart);
        // the in-frame DF flag is the arbiter for that pair. 23.976 vs 24 stays
        // ambiguous on clips shorter than ~10 frames - scan more frames if needed.
        bool dfBit = ((detected[0].Frame[1] >> 2) & 1) != 0;
        double fps;
        if (dfBit && Math.Abs(estFps - 29.97) / 29.97 <= 0.01)
        {
            fps = 29.97;
        }
        else
        {
            fps = FpsCandidates[0];
            double bestErr = double.MaxValue;
            foreach (double c in FpsCandidates)
            {
                double err = Math.Abs(estFps - c) / c;
                if (err < bestErr) { bestErr = err; fps = c; }
            }
            if (bestErr > 0.05) return null; // not a plausible LTC bit rate
        }

        byte[] f = detected[0].Frame;
        // Field positions per libltc's little-endian LTCFrame struct
        // (stream bit N = struct bit N): frame units 0-3, tens 8-9, dfbit 10,
        // secs 16-19/24-26, mins 32-35/40-42, hours 48-51/56-57.
        int frameUnits = f[0] & 0x0F;
        int frameTens = f[1] & 0x03;
        bool dropFrame = ((f[1] >> 2) & 1) != 0 && Math.Abs(fps - 29.97) < 0.01;
        int secsUnits = f[2] & 0x0F;
        int secsTens = f[3] & 0x07;
        int minsUnits = f[4] & 0x0F;
        int minsTens = f[5] & 0x07;
        int hoursUnits = f[6] & 0x0F;
        int hoursTens = f[7] & 0x03;

        // reject frames with invalid BCD fields (false sync matches on noise)
        if (frameUnits > 9 || secsUnits > 9 || minsUnits > 9 || hoursUnits > 9) return null;
        if (frameTens > 3 || secsTens > 5 || minsTens > 5 || hoursTens > 2) return null;

        int seconds = secsTens * 10 + secsUnits;
        int minutes = minsTens * 10 + minsUnits;
        int hours = hoursTens * 10 + hoursUnits;
        int frames = frameTens * 10 + frameUnits;
        if (seconds >= 60 || minutes >= 60 || hours >= 24 || frames >= (int)Math.Round(fps)) return null;

        long off = Math.Max(0, (long)Math.Round(detected[0].Off));
        return new Timecode(hours, minutes, seconds, frames, dropFrame, fps, off);
    }

    /// <summary>
    /// Converts a decoded timecode to the bext TimeReference value:
    /// true (wall-clock) samples since midnight of the frame, minus the sample
    /// offset where the frame begins in the audio, so the result refers to audio position 0.
    /// </summary>
    public static long ToSampleReference(in Timecode tc, int sampleRate)
    {
        double realSeconds;
        if (Math.Abs(tc.Fps - 29.97) < 0.01)
        {
            double totalFrames = tc.Hours * 108000.0 + tc.Minutes * 1800.0 + tc.Seconds * 30.0 + tc.Frames;
            if (tc.DropFrame)
            {
                int totalMinutes = tc.Hours * 60 + tc.Minutes;
                totalFrames -= 2 * (totalMinutes - totalMinutes / 10);
            }
            realSeconds = totalFrames * 1001.0 / 30000.0;
        }
        else if (Math.Abs(tc.Fps - 23.976) < 0.01)
        {
            double totalFrames = tc.Hours * 86400.0 + tc.Minutes * 1440.0 + tc.Seconds * 24.0 + tc.Frames;
            realSeconds = totalFrames * 1001.0 / 24000.0;
        }
        else
        {
            realSeconds = tc.Hours * 3600.0 + tc.Minutes * 60.0 + tc.Seconds + tc.Frames / tc.Fps;
        }
        return (long)Math.Round(realSeconds * sampleRate) - tc.SampleOffset;
    }
}
