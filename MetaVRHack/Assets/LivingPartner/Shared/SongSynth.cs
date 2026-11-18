using System;
using System.Runtime.CompilerServices;

namespace LivingPartner.Shared
{
    /// <summary>
    /// Turns a tune written as notes (SongDef) into sound. It runs on the headset, once per tune, so it is plain
    /// arithmetic: no allocations inside the loops, one sine table instead of trig calls, and every note is added
    /// straight into the buffer that is played.
    ///
    /// The buffer is one pass of the tune and is made to LOOP: a note that is still ringing when the tune ends is
    /// carried on at the beginning of the buffer, so the second time round sounds exactly like the first and there is
    /// no click where the end meets the start.
    /// </summary>
    public static class SongSynth
    {
        public const int SampleRate = 32000;   // enough for a small speaker (nothing above 16 kHz), a third less work than 48 kHz

        const int MinBpm = 50, MaxBpm = 180, MinBars = 1, MaxBars = 8, MinNote = 24, MaxNote = 108;
        const int StepsPerBar = 16;
        const int SilentSamples = SampleRate / 4;   // what a tune with nothing in it comes out as

        // How loud each voice goes into the mix, before the whole is brought to one level. Only the proportions
        // matter: the melody in front, bass and kick under it, chords and hats as a background.
        // Bass and kick are kept modest on purpose: a headset speaker gives back little of them, and whatever room they
        // take at the top of the mix is taken away from the melody, which it does give back.
        const float LeadLevel = 0.30f, ChordLevel = 0.036f, BassLevel = 0.24f, KickLevel = 0.45f, SnareLevel = 0.24f, HatLevel = 0.18f;

        // The last stage: the body of the tune sits at BodyRms, where the limiter does nothing; only peaks that stand
        // out above Knee (a kick landing on a bass note) are bent over, and never driven further than MaxDrive.
        const float PeakOut = 0.8f, BodyRms = 0.20f, Knee = 0.6f, MaxDrive = 1.3f;

        const float Tiny = 1e-20f;   // added to every decaying value so that it never becomes a denormal number (those are very slow)
        const float TwoPi = 6.2831853f;

        // ------------------------------------------------------------------------------------------------ API

        /// <summary>
        /// One pass of the tune, mono, -1..1, made to LOOP seamlessly (the tails of the last notes wrap round into the
        /// start). seconds = its exact length. A tune with nothing in it gives a quarter of a second of silence.
        /// </summary>
        public static float[] Render(SongDef song, out float seconds)
        {
            float[] buf = Mix(song);
            if (buf == null || !Master(buf, Volume(song))) buf = new float[SilentSamples];
            seconds = buf.Length / (float)SampleRate;
            return buf;
        }

        /// <summary>
        /// Brings a rendered tune up so that its loudest moment is at 'peak', in place, and says where that moment was
        /// before. For the headset: Render leaves a tune at 0.8 x its own volume (about 0.6 for the tunes that come
        /// with the partner) - right for a PC's speakers, too quiet for the small ones beside the ears. Silence stays
        /// silence.
        /// </summary>
        public static float Normalize(float[] pcm, float peak = 0.95f)
        {
            if (pcm == null) return 0f;
            float top = 0f;
            for (int i = 0; i < pcm.Length; i++) { float a = pcm[i] < 0f ? -pcm[i] : pcm[i]; if (a > top) top = a; }
            if (!(top > 1e-6f)) return 0f;
            float gain = peak / top;
            for (int i = 0; i < pcm.Length; i++) pcm[i] *= gain;
            return top;
        }

        /// <summary>
        /// How loud the tune is at each sixteenth step (0..1), one value per step of the loop - so that lights and a
        /// dancer can pulse with the beat without analysing audio. It is worked out from the notes, not from the sound,
        /// so it costs nothing: a kick counts most, then the snare, then notes starting, and a held note only a little.
        /// </summary>
        public static float[] Beat(SongDef song)
        {
            int bars = Bars(song), steps = bars * StepsPerBar;
            var beat = new float[steps];
            if (song == null) return beat;

            AddNotes(beat, Expand(song.melody, steps), 1, 0.25f, 0.08f);
            AddNotes(beat, Expand(song.bass, steps), 1, 0.25f, 0.05f);
            AddNotes(beat, Expand(song.chords, bars), StepsPerBar, 0.10f, 0.04f);
            for (int i = 0; i < steps; i++)
            {
                float v = beat[i];
                if (Hit(song.kick, i)) v += 0.70f;
                if (Hit(song.snare, i)) v += 0.50f;
                if (Hit(song.hat, i)) v += 0.15f;
                beat[i] = v > 1f ? 1f : v;
            }
            return beat;
        }

        /// <summary>Length of one sixteenth step in seconds at the tune's tempo (swing does not change the average).</summary>
        public static float StepSeconds(SongDef song) => 15f / Bpm(song);   // 60 s / bpm is a quarter note; a sixteenth is a quarter of that

        // ------------------------------------------------------------------------------------------------ reading the tune
        // The tune is written by a language model, so anything may be in it. Whatever is out of range is brought back
        // into range or dropped; nothing here throws.

        static int Bpm(SongDef s) => s == null ? 100 : Math.Min(MaxBpm, Math.Max(MinBpm, s.bpm));
        static int Bars(SongDef s) => s == null ? MinBars : Math.Min(MaxBars, Math.Max(MinBars, s.bars));

        static float Swing(SongDef s)
        {
            float w = s.swing;
            if (!(w > 0f)) return 0f;          // also catches NaN
            return w > 0.5f ? 0.5f : w;        // more than half a step would put the off-beat on top of the next note
        }

        static float Volume(SongDef s)
        {
            if (s == null) return 0f;
            float v = s.volume;
            if (float.IsNaN(v)) return 0.8f;
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }

        /// <summary>
        /// A pattern laid out over n places: shorter ones repeat, longer ones are cut. What comes out is a note number,
        /// 0 for silence, or -1 for "hold the note before". A note no instrument plays (500, 3) is silence.
        /// </summary>
        static int[] Expand(int[] pattern, int n)
        {
            var r = new int[n];
            if (pattern == null || pattern.Length == 0) return r;
            for (int i = 0; i < n; i++)
            {
                int v = pattern[i % pattern.Length];
                r[i] = v < 0 ? -1 : (v >= MinNote && v <= MaxNote) ? v : 0;
            }
            return r;
        }

        /// <summary>
        /// How many places after i hold the note at i. It counts round the end of the pattern, because the tune loops:
        /// a hold in the first place carries on the note the loop ended with.
        /// </summary>
        static int Holds(int[] pattern, int i)
        {
            int n = pattern.Length, h = 0;
            while (h < n - 1 && pattern[(i + 1 + h) % n] < 0) h++;
            return h;
        }

        static bool Hit(string pattern, int step)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            char c = pattern[step % pattern.Length];
            return c == 'x' || c == 'X';
        }

        static float Freq(int note) => (float)(440.0 * Math.Pow(2.0, (note - 69) / 12.0));

        static void AddNotes(float[] beat, int[] pattern, int stride, float onStart, float whileHeld)
        {
            int n = pattern.Length;
            for (int i = 0; i < n; i++)
            {
                if (pattern[i] <= 0) continue;
                int places = 1 + Holds(pattern, i);
                for (int k = 0; k < places * stride; k++) beat[((i * stride) + k) % beat.Length] += whileHeld;
                beat[i * stride] += onStart;
            }
        }

        // ------------------------------------------------------------------------------------------------ putting the voices together

        static float[] Mix(SongDef song)
        {
            if (song == null) return null;
            int bars = Bars(song), steps = bars * StepsPerBar;
            int len = (int)Math.Round(steps * 15.0 * SampleRate / Bpm(song));
            float swing = Swing(song);
            var buf = new float[len];

            // Where each step starts, in samples. It goes on for a second round so that a note held across the end of
            // the tune can simply ask where the step after its last one is. Swing delays the odd steps only, so the
            // loop keeps its length; the number of steps is even, so odd stays odd in the second round.
            double stepSamples = (double)len / steps;
            var onset = new int[steps * 2 + 1];
            for (int i = 0; i < onset.Length; i++) onset[i] = (int)Math.Round((i + ((i & 1) != 0 ? swing : 0.0)) * stepSamples);

            int[] lead = Expand(song.melody, steps);
            int timbre = TimbreOf(song.lead);
            for (int i = 0; i < steps; i++)
            {
                if (lead[i] <= 0) continue;
                // a held note is ONE long note: it gets no second attack, which is what makes it legato
                int end = i + 1 + Holds(lead, i);
                Lead(buf, onset[i], onset[end] - onset[i], Freq(lead[i]), timbre);
            }

            int[] bass = Expand(song.bass, steps);
            for (int i = 0; i < steps; i++)
            {
                if (bass[i] <= 0) continue;
                int end = i + 1 + Holds(bass, i);
                Voice(buf, onset[i], onset[end] - onset[i], Freq(bass[i]), BassLevel, BassTone);
            }

            int[] chords = Expand(song.chords, bars);
            int third = song.minor ? 3 : 4;
            for (int b = 0; b < bars; b++)
            {
                if (chords[b] <= 0) continue;
                int from = b * StepsPerBar, to = (b + 1 + Holds(chords, b)) * StepsPerBar;
                int gate = onset[to] - onset[from];
                Pad(buf, onset[from], gate, chords[b], ChordLevel, ChordTone, 4f);
                Pad(buf, onset[from], gate, chords[b] + third, ChordLevel, ChordTone, 4f);
                Pad(buf, onset[from], gate, chords[b] + 7, ChordLevel, ChordTone, 4f);
            }

            for (int i = 0; i < steps; i++)
            {
                // the step number seeds the noise: every hit is a little different, and every render is the same
                if (Hit(song.kick, i)) Kick(buf, onset[i], KickLevel);
                if (Hit(song.snare, i)) Snare(buf, onset[i], SnareLevel, (uint)(i * 2 + 1) * 2654435761u);
                if (Hit(song.hat, i)) Hat(buf, onset[i], HatLevel, (uint)(i * 2 + 1) * 2246822519u);
            }
            return buf;
        }

        /// <summary>
        /// Brings the mix to its final level, in place: no DC, peaks bent over softly, the highest one at
        /// 0.8 * volume. False if there was nothing to hear.
        /// </summary>
        static bool Master(float[] buf, float volume)
        {
            int n = buf.Length;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += buf[i];
            float mean = (float)(sum / n);

            double squares = 0;
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float v = buf[i] - mean;
                buf[i] = v;
                float a = v < 0f ? -v : v;
                if (a > peak) peak = a;
                squares += (double)v * v;
            }
            if (!(peak > 1e-6f)) return false;

            // Tunes differ a lot in how far their peaks stand out: a lullaby of sines hardly at all, a club tune with
            // a kick a great deal. Setting the level by the body of the sound (RMS) and letting the limiter take the
            // top off the peaks keeps both about equally loud; setting it by the peak alone would leave the club tune
            // quiet behind its own kick.
            float rms = (float)Math.Sqrt(squares / n);
            float gain = BodyRms / rms;
            if (gain * peak > MaxDrive) gain = MaxDrive / peak;
            float outGain = PeakOut * volume / Limit(peak * gain);

            sum = 0;
            for (int i = 0; i < n; i++)
            {
                float v = Limit(buf[i] * gain) * outGain;
                buf[i] = v;
                sum += v;
            }
            // bending the top of a lopsided wave shifts its average a little; take that out again
            mean = (float)(sum / n);
            for (int i = 0; i < n; i++) buf[i] -= mean;
            return true;
        }

        /// <summary>Straight up to the knee, then curving towards 1 without ever reaching it. The slope does not jump at the knee, so it adds no edge of its own.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Limit(float x)
        {
            float a = x < 0f ? -x : x;
            if (a <= Knee) return x;
            float y = Knee + (1f - Knee) * MathF.Tanh((a - Knee) / (1f - Knee));
            return x < 0f ? -y : y;
        }

        // ------------------------------------------------------------------------------------------------ the sine table
        // One table for every oscillator. A phase is a whole 32-bit number that wraps round by itself once per cycle;
        // its top 12 bits pick the table entry and the rest say how far to the next one.

        const int TableBits = 12, TableSize = 1 << TableBits, FracBits = 32 - TableBits;
        const float FracScale = 1f / (1 << FracBits);
        const double PhasePerCycle = 4294967296.0;
        static readonly float[] Table = MakeTable();

        static float[] MakeTable()
        {
            var t = new float[TableSize + 1];   // one more, so that "the next entry" needs no wrapping
            for (int i = 0; i <= TableSize; i++) t[i] = (float)Math.Sin(i * (2.0 * Math.PI / TableSize));
            return t;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Sin(uint phase)
        {
            uint i = phase >> FracBits;
            float a = Table[i];
            return a + (Table[i + 1] - a) * ((phase & ((1u << FracBits) - 1)) * FracScale);
        }

        // ------------------------------------------------------------------------------------------------ the pitched voices

        /// <summary>
        /// The sound of one instrument: how it swells, dies away and stops, and how bright it is. The brightness comes
        /// from one sine bending the phase of another (FM): one extra table look-up buys a whole set of overtones, and
        /// letting the amount fall away quickly gives the bright strike and mellow ring of a struck or plucked thing.
        /// </summary>
        sealed class Tone
        {
            public float attack;       // seconds from nothing to full; never zero, or the note would start with a click
            public float decay;        // seconds for the struck part to fall to about a third
            public float sustain;      // 0..1: what it falls to while the note is held (0 = it dies away like a bell)
            public float release;      // seconds to fade out after the note ends
            public float indexPeak;    // brightness at the strike ...
            public float indexFloor;   // ... and once it has settled
            public float indexDecay;   // seconds from the one to the other
            public float second;       // how much of the octave above is mixed in
            public float keyTrack;     // above 0: high notes die away faster than low ones, as on a real instrument
        }

        static readonly Tone BellTone = new Tone { attack = 0.002f, decay = 0.75f, sustain = 0f, release = 0.30f, indexPeak = 1.3f, indexFloor = 0.2f, indexDecay = 0.10f, keyTrack = 0.5f };
        static readonly Tone TineTone = new Tone { attack = 0.001f, decay = 0.07f, sustain = 0f, release = 0.05f };
        static readonly Tone PluckTone = new Tone { attack = 0.002f, decay = 0.20f, sustain = 0f, release = 0.10f, indexPeak = 2.6f, indexFloor = 0.3f, indexDecay = 0.045f, keyTrack = 0.5f };
        static readonly Tone PadTone = new Tone { attack = 0.14f, decay = 1.5f, sustain = 0.8f, release = 0.35f, indexPeak = 0.5f, indexFloor = 0.35f, indexDecay = 0.5f, second = 0.12f };
        static readonly Tone SoftTone = new Tone { attack = 0.03f, decay = 0.7f, sustain = 0.65f, release = 0.20f, second = 0.12f };
        static readonly Tone ChordTone = new Tone { attack = 0.10f, decay = 1.2f, sustain = 0.75f, release = 0.35f, indexPeak = 0.25f, indexFloor = 0.25f, indexDecay = 1f, second = 0.08f };
        // the octave above is there for the headset: its speakers hardly reproduce a 65 Hz note, but the ear fills the note in from its overtones
        static readonly Tone BassTone = new Tone { attack = 0.004f, decay = 0.25f, sustain = 0.3f, release = 0.12f, indexPeak = 0.9f, indexFloor = 0.15f, indexDecay = 0.05f, second = 0.3f };

        const int Bell = 0, Pluck = 1, PadLead = 2, SquareLead = 3, Soft = 4;

        static int TimbreOf(string lead)
        {
            if (string.Equals(lead, "pluck", StringComparison.OrdinalIgnoreCase)) return Pluck;
            if (string.Equals(lead, "pad", StringComparison.OrdinalIgnoreCase)) return PadLead;
            if (string.Equals(lead, "square", StringComparison.OrdinalIgnoreCase)) return SquareLead;
            if (string.Equals(lead, "soft", StringComparison.OrdinalIgnoreCase)) return Soft;
            return Bell;   // also for null and for a name nobody knows
        }

        static void Lead(float[] buf, int start, int gate, float freq, int timbre)
        {
            // The same strength sounds louder and sharper the higher the note; lean against that, so that a melody
            // that climbs does not start to pierce.
            float tilt = (float)Math.Pow(440.0 / freq, 0.3);
            float level = LeadLevel * (tilt < 0.6f ? 0.6f : tilt > 1.2f ? 1.2f : tilt);
            switch (timbre)
            {
                case Pluck:
                    Voice(buf, start, gate, freq, level, PluckTone);
                    break;
                case PadLead:
                    // two oscillators a few cents apart drift against each other: the slow shimmer of a pad
                    Voice(buf, start, gate, freq * 1.0029f, level * 0.5f, PadTone);
                    Voice(buf, start, gate, freq / 1.0029f, level * 0.5f, PadTone);
                    break;
                case SquareLead:
                    Square(buf, start, gate, freq, level * 0.8f);
                    break;
                case Soft:
                    Voice(buf, start, gate, freq, level, SoftTone);
                    break;
                default:
                    Voice(buf, start, gate, freq, level, BellTone);
                    // the "ting" of a music box: two octaves up, gone in a moment. Left out where it would be too high to be pleasant.
                    if (freq * 4f < 9000f) Voice(buf, start, gate, freq * 4f, level * 0.16f, TineTone);
                    break;
            }
        }

        /// <summary>One note of a chord: two oscillators slightly apart, as for the pad. Notes above the top of the range are left out.</summary>
        static void Pad(float[] buf, int start, int gate, int note, float level, Tone tone, float cents)
        {
            if (note > MaxNote) return;
            float freq = Freq(note), apart = (float)Math.Pow(2.0, cents / 1200.0);
            Voice(buf, start, gate, freq * apart, level, tone);
            Voice(buf, start, gate, freq / apart, level, tone);
        }

        /// <summary>
        /// Adds one note to the buffer, starting at 'start', held for 'gate' samples and then fading out. Writing goes
        /// round the end of the buffer back to the beginning - that is the whole of what makes the tune loop.
        /// </summary>
        static void Voice(float[] buf, int start, int gate, float freq, float level, Tone t)
        {
            int len = buf.Length;
            int attack = Math.Max(8, (int)(t.attack * SampleRate));
            int release = Math.Max(32, (int)(t.release * SampleRate));
            int total = gate + release;
            float attackStep = 1f / attack, releaseStep = 1f / release;

            float decayTime = t.decay;
            if (t.keyTrack > 0f)
            {
                float k = (float)Math.Pow(261.63 / freq, t.keyTrack);
                decayTime *= k < 0.3f ? 0.3f : k > 2f ? 2f : k;
            }
            float decayMul = (float)Math.Exp(-1.0 / (decayTime * SampleRate));
            float sustain = t.sustain, struck = 1f - sustain, fall = 1f;

            // Overtones made by FM reach higher the higher the note, and what goes past half the sample rate comes
            // back down as a harsh, out-of-tune whistle. So high notes get less of it - they are bright enough anyway.
            float guard = freq > 1200f ? 1200f / freq : 1f;
            float indexFloor = t.indexFloor * guard, indexSpan = (t.indexPeak - t.indexFloor) * guard, bright = 1f;
            float indexMul = (float)Math.Exp(-1.0 / (Math.Max(0.001f, t.indexDecay) * SampleRate));
            bool fm = t.indexPeak > 0f || t.indexFloor > 0f;
            const float RadiansToPhase = (float)(PhasePerCycle / (2.0 * Math.PI));

            float second = t.second;
            if (freq * 2f > 12000f) second = 0f;
            bool hasSecond = second != 0f;

            uint inc = (uint)(freq * (PhasePerCycle / SampleRate));
            uint phase = 0;   // every note starts where the sine crosses zero
            int at = start % len;

            unchecked
            {
                for (int k = 0; k < total; k++)
                {
                    float env = sustain + struck * fall;
                    fall = fall * decayMul + Tiny;
                    if (k < attack) env *= k * attackStep;
                    if (k >= gate) { float r = (total - k) * releaseStep; env *= r * r; }   // squared: it leaves the note gently and arrives at nothing gently

                    uint p = phase;
                    if (fm)
                    {
                        // carrier and modulator at the same pitch, so they can share one phase. Going through long
                        // keeps the wrap-round the same on every CPU (a float too large for an int is not).
                        float index = indexFloor + indexSpan * bright;
                        bright = bright * indexMul + Tiny;
                        p += (uint)(long)(Sin(phase) * index * RadiansToPhase);
                    }
                    float s = Sin(p);
                    if (hasSecond) s += second * Sin(phase << 1);
                    phase += inc;

                    buf[at] += level * env * s;
                    if (++at == len) at = 0;
                }
            }
        }

        /// <summary>
        /// The chiptune voice: a square wave. A square cut straight out of +1 and -1 has edges that land between
        /// samples, which is heard as a gritty buzz; here each edge is rounded by the fraction of a sample it is off
        /// (PolyBLEP), and two gentle low-pass stages take the rest of the sting out for small speakers.
        /// </summary>
        static void Square(float[] buf, int start, int gate, float freq, float level)
        {
            int len = buf.Length;
            int attack = (int)(0.004f * SampleRate), release = (int)(0.05f * SampleRate);
            int total = gate + release;
            float attackStep = 1f / attack, releaseStep = 1f / release;
            float decayMul = (float)Math.Exp(-1.0 / (0.12 * SampleRate));
            const float sustain = 0.65f;
            float fall = 1f;

            // the filter follows the note, so that low notes are not left buzzing and high ones are not muffled
            float cutoff = freq * 6f;
            cutoff = cutoff < 1800f ? 1800f : cutoff > 4200f ? 4200f : cutoff;
            float f = 1f - (float)Math.Exp(-TwoPi * cutoff / SampleRate);
            float low1 = 0f, low2 = 0f;

            float dt = freq / SampleRate, t = 0f;   // t runs 0..1 once per cycle
            int at = start % len;

            for (int k = 0; k < total; k++)
            {
                float v = t < 0.5f ? 1f : -1f;
                v += Blep(t, dt);
                float half = t + 0.5f; if (half >= 1f) half -= 1f;
                v -= Blep(half, dt);
                t += dt; if (t >= 1f) t -= 1f;

                low1 += f * (v - low1);
                low2 += f * (low1 - low2);

                float env = sustain + (1f - sustain) * fall;
                fall = fall * decayMul + Tiny;
                if (k < attack) env *= k * attackStep;
                if (k >= gate) { float r = (total - k) * releaseStep; env *= r * r; }

                buf[at] += level * env * low2;
                if (++at == len) at = 0;
            }
        }

        /// <summary>The correction for one edge of the square: zero everywhere but within one sample of the edge.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Blep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }

        // ------------------------------------------------------------------------------------------------ the drums

        /// <summary>A sine that falls from a knock (165 Hz) to a thump (48 Hz) in a few hundredths of a second. The fall is what a small speaker lets through.</summary>
        static void Kick(float[] buf, int start, float level)
        {
            int len = buf.Length;
            int total = (int)(0.30f * SampleRate), attack = (int)(0.0015f * SampleRate), fade = (int)(0.03f * SampleRate);
            float attackStep = 1f / attack, fadeStep = 1f / fade;
            float incLow = (float)(48.0 * PhasePerCycle / SampleRate), incSpan = (float)(117.0 * PhasePerCycle / SampleRate);
            float pitchMul = (float)Math.Exp(-1.0 / (0.028 * SampleRate)), ampMul = (float)Math.Exp(-1.0 / (0.10 * SampleRate));
            float pitch = 1f, amp = 1f;
            uint phase = 0;
            int at = start % len;

            unchecked
            {
                for (int k = 0; k < total; k++)
                {
                    float env = amp;
                    amp = amp * ampMul + Tiny;
                    if (k < attack) env *= k * attackStep;
                    if (k >= total - fade) env *= (total - k) * fadeStep;   // it has not quite died away by itself; bring it to nothing

                    buf[at] += level * env * Sin(phase);
                    phase += (uint)(incLow + incSpan * pitch);
                    pitch = pitch * pitchMul + Tiny;
                    if (++at == len) at = 0;
                }
            }
        }

        /// <summary>A burst of noise for the wires and a short tone for the skin. The noise is kept to the middle of the spectrum: no rumble, no hiss.</summary>
        static void Snare(float[] buf, int start, float level, uint seed)
        {
            int len = buf.Length;
            int total = (int)(0.20f * SampleRate), attack = (int)(0.001f * SampleRate), fade = (int)(0.02f * SampleRate);
            float attackStep = 1f / attack, fadeStep = 1f / fade;
            float noiseMul = (float)Math.Exp(-1.0 / (0.05 * SampleRate)), toneMul = (float)Math.Exp(-1.0 / (0.06 * SampleRate));
            float top = 1f - (float)Math.Exp(-TwoPi * 5000f / SampleRate), bottom = 1f - (float)Math.Exp(-TwoPi * 900f / SampleRate);
            float lowA = 0f, lowB = 0f, noiseAmp = 1f, toneAmp = 0.5f;
            uint inc = (uint)(190.0 * PhasePerCycle / SampleRate), phase = 0;
            uint rnd = seed | 1u;   // the generator must not start at zero: it would stay there
            int at = start % len;

            unchecked
            {
                for (int k = 0; k < total; k++)
                {
                    float white = Noise(ref rnd);
                    lowA += top * (white - lowA);        // everything below 5 kHz ...
                    lowB += bottom * (lowA - lowB);      // ... minus everything below 900 Hz = the band between
                    float s = (lowA - lowB) * noiseAmp + Sin(phase) * toneAmp;
                    noiseAmp = noiseAmp * noiseMul + Tiny;
                    toneAmp = toneAmp * toneMul + Tiny;
                    phase += inc;

                    float env = 1f;
                    if (k < attack) env = k * attackStep;
                    if (k >= total - fade) env *= (total - k) * fadeStep;
                    buf[at] += level * env * s;
                    if (++at == len) at = 0;
                }
            }
        }

        /// <summary>A tick of high noise, over almost before it began. The very top is rounded off so that it ticks rather than spits.</summary>
        static void Hat(float[] buf, int start, float level, uint seed)
        {
            int len = buf.Length;
            int total = (int)(0.06f * SampleRate), attack = (int)(0.0005f * SampleRate), fade = (int)(0.01f * SampleRate);
            float attackStep = 1f / attack, fadeStep = 1f / fade;
            float ampMul = (float)Math.Exp(-1.0 / (0.014 * SampleRate));
            float bottom = 1f - (float)Math.Exp(-TwoPi * 6000f / SampleRate), top = 1f - (float)Math.Exp(-TwoPi * 11000f / SampleRate);
            float low = 0f, smooth = 0f, amp = 1f;
            uint rnd = seed | 1u;
            int at = start % len;

            for (int k = 0; k < total; k++)
            {
                float white = Noise(ref rnd);
                low += bottom * (white - low);
                smooth += top * ((white - low) - smooth);   // what is above 6 kHz, with the very top softened

                float env = amp;
                amp = amp * ampMul + Tiny;
                if (k < attack) env *= k * attackStep;
                if (k >= total - fade) env *= (total - k) * fadeStep;
                buf[at] += level * env * smooth;
                if (++at == len) at = 0;
            }
        }

        /// <summary>
        /// White noise, -1..1, from a tiny generator of our own (xorshift) rather than System.Random: the same seed
        /// gives the same numbers on the PC and on the headset, so a tune sounds the same every time it is made.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Noise(ref uint state)
        {
            unchecked
            {
                uint x = state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                state = x;
                return (int)x * (1f / 2147483648f);
            }
        }
    }
}
