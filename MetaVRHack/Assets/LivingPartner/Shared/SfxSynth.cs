using System;

namespace LivingPartner.Shared
{
    /// <summary>
    /// A small kit of sound effects, made from sines, noise and envelopes - no recordings. Every sound is short, soft
    /// and toy-like, to go with a storybook pet: wooden ticks, small bells, a bubble, a paper flick. Nothing harsh, and
    /// nothing below what a headset's small speakers give back.
    /// Like SongSynth it is plain arithmetic on a buffer (mono, -1..1, the same sample rate), so the headset can make
    /// all of them once at its start, off the main thread, and the tests can listen to the numbers.
    /// </summary>
    public static class SfxSynth
    {
        public const int SampleRate = SongSynth.SampleRate;

        /// <summary>Every sound there is.</summary>
        public static readonly string[] Names =
        {
            "tap", "pop", "open", "close", "page", "hammer", "sparkle", "ta-da", "coin", "error",
            "portal-in", "portal-out", "kick", "glove", "spell", "whoosh", "land", "squeak", "step", "hit", "bounce", "win", "lose", "drop", "tick",
        };

        // notes (Hz)
        const float G4 = 392.00f, C5 = 523.25f, D5 = 587.33f, E5 = 659.25f, G5 = 783.99f, B5 = 987.77f, C6 = 1046.50f, D6 = 1174.66f,
                    E6 = 1318.51f, G6 = 1567.98f, C7 = 2093.00f, E7 = 2637.02f, G7 = 3135.96f;

        /// <summary>The sound of this name: mono, -1..1, at SampleRate. Null for a name there is no sound for.</summary>
        public static float[] Render(string name)
        {
            float[] b;
            switch (name)
            {
                case "portal-in":
                    // Air draws tight before the surface seals. Leave only a low, damped resonance.
                    b = New(0.32f);
                    Hiss(b, 0f, .17f, 480f, 3200f, 1350f, 6100f, .075f, .23f, .80f, 211u);
                    Tone(b, .115f, 495f, 132f, .09f, .006f, .052f, .75f);
                    Tone(b, .151f, 180f, 105f, .07f, .002f, .039f, .53f);
                    return Finish(b, .81f);
                case "portal-out":
                    b = New(.29f);
                    Tone(b, 0f, 150f, 320f, .08f, .003f, .047f, .62f);
                    Hiss(b, .022f, .22f, 510f, 7200f, 180f, 1800f, .008f, .076f, 1f, 223u);
                    return Finish(b, .79f);
                case "kick":
                case "glove":
                    bool save = name == "glove";
                    b = New(.23f);
                    Tone(b, 0f, save ? 235f : 175f, 82f, .06f, .001f, .047f, .94f);
                    Hiss(b, 0f, .095f, 450f, save ? 5100f : 2700f, 180f, 1100f, .001f, .027f, .88f, 229u);
                    if (save) Tone(b, .024f, 540f, 270f, .045f, .002f, .029f, .27f);
                    return Finish(b, .87f);
                case "spell":
                    b = New(.19f);
                    Tone(b, 0f, 900f, 380f, .085f, .002f, .048f, .59f);
                    Hiss(b, .006f, .13f, 1400f, 6500f, 480f, 1800f, .004f, .045f, .73f, 241u);
                    return Finish(b, .68f);
                case "tap":      // a soft wooden tick: a button was pressed
                    b = New(0.07f);
                    Tone(b, 0f, 620f, 480f, 0.03f, 0.001f, 0.014f, 1f);
                    Tone(b, 0f, 1450f, 1450f, 0f, 0.0008f, 0.006f, 0.35f);
                    Hiss(b, 0f, 0.012f, 1500f, 5000f, 1500f, 5000f, 0.0005f, 0.003f, 0.25f, 11u);
                    return Finish(b, 0.70f);
                case "pop":      // a bubble: the pitch runs up as it bursts
                    b = New(0.10f);
                    Tone(b, 0f, 380f, 1100f, 0.05f, 0.002f, 0.028f, 1f, 0.15f);
                    return Finish(b, 0.80f);
                case "open":     // two notes of a chime, rising: something opens
                    b = New(0.46f);
                    Bell(b, 0f, G5, 0.16f, 0.9f);
                    Bell(b, 0.10f, D6, 0.22f, 1f);
                    return Finish(b, 0.75f);
                case "close":    // the same two, falling
                    b = New(0.46f);
                    Bell(b, 0f, D6, 0.14f, 0.9f);
                    Bell(b, 0.10f, G5, 0.22f, 1f);
                    return Finish(b, 0.75f);
                case "page":     // a flick of paper
                    b = New(0.11f);
                    Hiss(b, 0f, 0.10f, 1800f, 6000f, 3500f, 9000f, 0.004f, 0.030f, 1f, 23u);
                    return Finish(b, 0.60f);
                case "hammer":   // a muted wooden knock, with a tiny ring of metal in it
                    b = New(0.26f);
                    Tone(b, 0f, 260f, 170f, 0.02f, 0.001f, 0.030f, 1f);
                    Hiss(b, 0f, 0.020f, 800f, 3500f, 800f, 3500f, 0.0005f, 0.006f, 0.5f, 37u);
                    Tone(b, 0f, 2350f, 2350f, 0f, 0.001f, 0.070f, 0.10f);
                    Tone(b, 0f, 3270f, 3270f, 0f, 0.001f, 0.050f, 0.06f);
                    return Finish(b, 0.85f);
                case "sparkle":  // three quick high bells
                    b = New(0.50f);
                    Bell(b, 0f, C7, 0.10f, 0.9f);
                    Bell(b, 0.06f, E7, 0.10f, 0.8f);
                    Bell(b, 0.12f, G7, 0.16f, 0.8f);
                    return Finish(b, 0.60f);
                case "ta-da":    // a small fanfare, four notes up, a bell on the last: something was made
                    b = New(1.00f);
                    Tone(b, 0f, C5, C5, 0f, 0.004f, 0.16f, 0.7f, 0.25f);
                    Tone(b, 0.11f, E5, E5, 0f, 0.004f, 0.16f, 0.7f, 0.25f);
                    Tone(b, 0.22f, G5, G5, 0f, 0.004f, 0.16f, 0.7f, 0.25f);
                    Tone(b, 0.33f, C6, C6, 0f, 0.004f, 0.40f, 1f, 0.25f);
                    Tone(b, 0.33f, E5, E5, 0f, 0.010f, 0.35f, 0.30f);      // the chord under the last note
                    Tone(b, 0.33f, G5, G5, 0f, 0.010f, 0.35f, 0.30f);
                    Bell(b, 0.33f, C7, 0.30f, 0.35f);
                    return Finish(b, 0.85f);
                case "coin":     // two bright pings: bought, sold
                    b = New(0.50f);
                    Tone(b, 0f, B5, B5, 0f, 0.002f, 0.07f, 0.8f, 0.20f);
                    Tone(b, 0.075f, E6, E6, 0f, 0.002f, 0.22f, 1f, 0.20f);
                    Tone(b, 0.075f, E6 * 2f, E6 * 2f, 0f, 0.002f, 0.06f, 0.15f);
                    return Finish(b, 0.70f);
                case "error":    // a low, soft double bump: that cannot be done
                    b = New(0.34f);
                    Tone(b, 0f, 196f, 150f, 0.08f, 0.004f, 0.06f, 1f, 0.10f);
                    Tone(b, 0.13f, 165f, 125f, 0.08f, 0.004f, 0.08f, 1f, 0.10f);
                    return Finish(b, 0.70f);
                case "whoosh":   // air going by: a shooting star, a jump through the screen
                    b = New(0.55f);
                    Whoosh(b, 53u);
                    return Finish(b, 0.60f);
                case "land":     // a soft thud
                    b = New(0.20f);
                    Tone(b, 0f, 150f, 62f, 0.07f, 0.002f, 0.055f, 1f);
                    Hiss(b, 0f, 0.05f, 100f, 700f, 100f, 700f, 0.001f, 0.020f, 0.5f, 71u);
                    return Finish(b, 0.80f);
                case "squeak":   // a short chirp upwards: startled, picked up
                    b = New(0.16f);
                    Squeak(b);
                    return Finish(b, 0.65f);
                case "step":     // a very soft tick: one footstep
                    b = New(0.04f);
                    Tone(b, 0f, 520f, 380f, 0.01f, 0.0008f, 0.007f, 0.8f);
                    Hiss(b, 0f, 0.012f, 600f, 2500f, 600f, 2500f, 0.0005f, 0.003f, 0.4f, 83u);
                    return Finish(b, 0.45f);
                case "hit":      // racket and ball
                    b = New(0.14f);
                    Hiss(b, 0f, 0.020f, 900f, 5000f, 900f, 5000f, 0.0005f, 0.005f, 0.7f, 97u);
                    Tone(b, 0f, 560f, 400f, 0.02f, 0.001f, 0.030f, 1f);
                    Tone(b, 0f, 1320f, 1320f, 0f, 0.001f, 0.040f, 0.25f);
                    return Finish(b, 0.80f);
                case "bounce":   // the ball on the desk
                    b = New(0.12f);
                    Tone(b, 0f, 340f, 190f, 0.04f, 0.0015f, 0.035f, 1f, 0.10f);
                    Hiss(b, 0f, 0.010f, 400f, 1500f, 400f, 1500f, 0.0005f, 0.003f, 0.2f, 101u);
                    return Finish(b, 0.70f);
                case "win":      // a short happy run of bells upwards
                    b = New(0.66f);
                    Bell(b, 0f, C6, 0.10f, 0.8f);
                    Bell(b, 0.075f, E6, 0.10f, 0.8f);
                    Bell(b, 0.150f, G6, 0.12f, 0.8f);
                    Bell(b, 0.225f, C7, 0.30f, 1f);
                    return Finish(b, 0.70f);
                case "lose":     // two notes falling, gently
                    b = New(0.60f);
                    Tone(b, 0f, G4, G4 * 0.95f, 0.20f, 0.006f, 0.16f, 1f, 0.20f);
                    Tone(b, 0.20f, 294f, 262f, 0.30f, 0.006f, 0.24f, 1f, 0.20f);
                    return Finish(b, 0.65f);
                case "drop":     // a drop of water: down for a moment, then up
                    b = New(0.16f);
                    Tone(b, 0f, 1350f, 720f, 0.02f, 0.001f, 0.015f, 0.6f);
                    Tone(b, 0.022f, 720f, 1750f, 0.07f, 0.002f, 0.040f, 1f);
                    return Finish(b, 0.70f);
                case "tick":     // one beat of a count
                    b = New(0.06f);
                    Tone(b, 0f, 1040f, 1040f, 0f, 0.0008f, 0.012f, 1f, 0.10f);
                    return Finish(b, 0.55f);
                default:
                    return null;
            }
        }

        // ------------------------------------------------------------------------------------------------ building blocks

        static float[] New(float seconds) => new float[(int)(seconds * SampleRate)];

        /// <summary>
        /// Adds a sine that starts at 'at' seconds: its pitch goes from f0 to f1 over 'glide' seconds (0 = it stays at
        /// f0), it comes up in 'attack' seconds and then dies away, falling to about a third every 'decay' seconds.
        /// second = how much of the octave above is mixed in (a rounder, less pure note).
        /// </summary>
        static void Tone(float[] b, float at, float f0, float f1, float glide, float attack, float decay, float level, float second = 0f)
        {
            int start = (int)(at * SampleRate);
            double phase = 0.0;
            float ratio = f1 / f0;
            for (int i = start; i < b.Length; i++)
            {
                float t = (i - start) / (float)SampleRate;
                float env = (t < attack ? t / attack : 1f) * MathF.Exp(-t / decay);
                if (t > attack && env < 0.0004f) break;                                  // it has died away
                float f = glide > 0f && ratio != 1f ? f0 * MathF.Pow(ratio, t >= glide ? 1f : t / glide) : f0;
                phase += f / SampleRate;
                if (phase >= 1.0) phase -= 1.0;
                float s = (float)Math.Sin(phase * 2.0 * Math.PI);
                if (second != 0f) s += second * (float)Math.Sin(phase * 4.0 * Math.PI);
                b[i] += level * env * s;
            }
        }

        /// <summary>A small bell: the note, and two partials above it that are not in tune with it and are gone sooner - the "ting".</summary>
        static void Bell(float[] b, float at, float freq, float decay, float level)
        {
            Tone(b, at, freq, freq, 0f, 0.0015f, decay, level);
            if (freq * 2.76f < 11000f) Tone(b, at, freq * 2.76f, freq * 2.76f, 0f, 0.001f, decay * 0.35f, level * 0.22f);
            if (freq * 5.40f < 11000f) Tone(b, at, freq * 5.40f, freq * 5.40f, 0f, 0.001f, decay * 0.15f, level * 0.08f);
        }

        /// <summary>
        /// Adds noise between two frequencies, for 'dur' seconds from 'at'. The band may move while it sounds (from
        /// low0..high0 to low1..high1). It comes up in 'attack' seconds, dies away like a Tone, and is brought to
        /// nothing over its last fifth so that it ends without a click.
        /// </summary>
        static void Hiss(float[] b, float at, float dur, float low0, float high0, float low1, float high1, float attack, float decay, float level, uint seed)
        {
            int start = (int)(at * SampleRate), n = Math.Min((int)(dur * SampleRate), b.Length - start);
            float top = 0f, bottom = 0f;
            uint rnd = seed | 1u;
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)n, t = k / (float)SampleRate;
                float high = high0 + (high1 - high0) * u, low = low0 + (low1 - low0) * u;
                float a = 1f - MathF.Exp(-6.2831853f * high / SampleRate), c = 1f - MathF.Exp(-6.2831853f * low / SampleRate);
                float white = Noise(ref rnd);
                top += a * (white - top);            // everything below 'high' ...
                bottom += c * (top - bottom);        // ... minus everything below 'low' = the band between
                float env = (t < attack ? t / attack : 1f) * MathF.Exp(-t / decay);
                if (u > 0.8f) env *= (1f - u) * 5f;
                b[start + k] += level * env * (top - bottom) * 2.5f;
            }
        }

        /// <summary>Air going by: a band of noise that climbs and falls again while it swells and fades.</summary>
        static void Whoosh(float[] b, uint seed)
        {
            int n = b.Length;
            float top = 0f, bottom = 0f;
            uint rnd = seed | 1u;
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)n;
                float swell = MathF.Sin(u * MathF.PI);                         // 0 .. 1 .. 0
                float centre = 500f + 2300f * MathF.Sin(MathF.Min(1f, u * 1.25f) * MathF.PI * 0.8f);
                float a = 1f - MathF.Exp(-6.2831853f * centre * 1.6f / SampleRate), c = 1f - MathF.Exp(-6.2831853f * centre * 0.6f / SampleRate);
                float white = Noise(ref rnd);
                top += a * (white - top);
                bottom += c * (top - bottom);
                b[k] += swell * swell * (top - bottom) * 2.5f;
            }
        }

        /// <summary>A chirp that runs up, with a little wobble in it - the voice of something small that got a fright.</summary>
        static void Squeak(float[] b)
        {
            int n = b.Length;
            double phase = 0.0;
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)SampleRate, u = k / (float)n;
                float rise = MathF.Min(1f, t / 0.11f);
                float f = 950f * MathF.Pow(2300f / 950f, rise) * (1f + 0.03f * MathF.Sin(t * 2f * MathF.PI * 38f));
                phase += f / SampleRate;
                if (phase >= 1.0) phase -= 1.0;
                float env = MathF.Sin(MathF.Min(1f, u * 1.15f) * MathF.PI);
                env = env < 0f ? 0f : env;
                float s = (float)Math.Sin(phase * 2.0 * Math.PI) + 0.20f * (float)Math.Sin(phase * 4.0 * Math.PI);
                b[k] += MathF.Sqrt(env) * (t < 0.004f ? t / 0.004f : 1f) * s;
            }
        }

        /// <summary>
        /// The last step: no DC, the last few milliseconds brought down to nothing, and the loudest moment at 'peak'.
        /// (Each sound has a level of its own here, so that a footstep is not as loud as a fanfare when both are played
        /// at the same volume.)
        /// </summary>
        static float[] Finish(float[] b, float peak)
        {
            int n = b.Length;
            if (n == 0) return b;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += b[i];
            float mean = (float)(sum / n);
            int fade = Math.Min(n / 4, (int)(0.008f * SampleRate));
            float top = 0f;
            for (int i = 0; i < n; i++)
            {
                float v = b[i] - mean;
                if (i >= n - fade) v *= (n - 1 - i) / (float)fade;
                if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
                b[i] = v;
                float a = v < 0f ? -v : v;
                if (a > top) top = a;
            }
            if (top > 1e-6f)
            {
                float gain = peak / top;
                for (int i = 0; i < n; i++) b[i] *= gain;
            }
            return b;
        }

        /// <summary>White noise, -1..1, from a tiny generator of our own (as in SongSynth): the same on every machine, every time.</summary>
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
