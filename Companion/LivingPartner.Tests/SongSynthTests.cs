using System.Diagnostics;
using LivingPartner.Shared;
using Xunit;
using Xunit.Abstractions;

namespace LivingPartner.Tests;

/// <summary>The synthesizer that turns a tune written as notes into the samples the headset plays in a loop.</summary>
public class SongSynthTests
{
    readonly ITestOutputHelper _out;
    public SongSynthTests(ITestOutputHelper o) { _out = o; }

    const int Rate = SongSynth.SampleRate;

    public static IEnumerable<object[]> BuiltIn() => new[] { new object[] { "song-club" }, new object[] { "song-lofi" }, new object[] { "song-lullaby" } };
    static SongDef Tune(string id) => WorldDefaults.Songs().First(s => s.id == id);

    static double Rms(float[] x, int from, int count)
    {
        double s = 0;
        for (int i = from; i < from + count; i++) s += (double)x[i] * x[i];
        return Math.Sqrt(s / count);
    }
    static double Rms(float[] x) => Rms(x, 0, x.Length);
    static float Peak(float[] x) => x.Max(v => Math.Abs(v));

    /// <summary>The first sample that is clearly sound and not the silence before it.</summary>
    static int Onset(float[] x)
    {
        float floor = Peak(x) * 0.02f;
        for (int i = 0; i < x.Length; i++) if (Math.Abs(x[i]) > floor) return i;
        return -1;
    }

    /// <summary>A tune with nothing in it but what the test puts there.</summary>
    static SongDef Bare(int bpm = 120, int bars = 1) => new SongDef { bpm = bpm, bars = bars, lead = "pluck", volume = 1f };

    // ---------------------------------------------------------------------------------------- the three tunes that come with the partner

    [Theory, MemberData(nameof(BuiltIn))]
    public void A_tune_is_exactly_as_long_as_its_bars_at_its_tempo(string id)
    {
        var song = Tune(id);
        var x = SongSynth.Render(song, out float seconds);
        double expected = song.bars * 16 * 60.0 / song.bpm / 4.0;
        Assert.InRange(x.Length, expected * Rate - 2, expected * Rate + 2);
        Assert.Equal(x.Length / (double)Rate, seconds, 5);
        Assert.Equal(expected / (song.bars * 16), SongSynth.StepSeconds(song), 5);
    }

    [Theory, MemberData(nameof(BuiltIn))]
    public void A_tune_is_audible_never_clips_and_sits_round_zero(string id)
    {
        var song = Tune(id);
        var x = SongSynth.Render(song, out _);
        Assert.All(x, v => Assert.True(float.IsFinite(v)));
        double rms = Rms(x), mean = x.Average(v => (double)v);
        float peak = Peak(x);
        _out.WriteLine($"{id}: peak {peak:F3}  rms {rms:F3}  mean {mean:E2}");
        Assert.True(rms > 0.05, $"rms {rms}");
        Assert.True(peak <= 1.0f, $"peak {peak}");
        Assert.InRange(peak, 0.8f * song.volume - 0.02f, 0.8f * song.volume + 0.02f);   // the level it was asked for
        Assert.True(Math.Abs(mean) < 1e-3, $"mean {mean}");
    }

    [Theory, MemberData(nameof(BuiltIn))]
    public void Two_renders_of_a_tune_are_the_same_sample_for_sample(string id)
    {
        var a = SongSynth.Render(Tune(id), out _);
        var b = SongSynth.Render(Tune(id), out _);
        Assert.Equal(a.Length, b.Length);
        Assert.True(a.AsSpan().SequenceEqual(b));
    }

    [Theory, MemberData(nameof(BuiltIn))]
    public void A_tune_loops_without_a_click(string id)
    {
        var x = SongSynth.Render(Tune(id), out _);
        // the step from the last sample back to the first must look like any other step inside the tune
        var steps = new float[x.Length - 1];
        for (int i = 0; i < steps.Length; i++) steps[i] = Math.Abs(x[i + 1] - x[i]);
        float across = Math.Abs(x[0] - x[^1]);
        Array.Sort(steps);
        float usual = steps[(int)(steps.Length * 0.90)], largest = steps[^1];
        _out.WriteLine($"{id}: across the loop point {across:F5}; nine steps in ten are below {usual:F5}, the largest is {largest:F5}");
        Assert.True(across <= usual, $"{across} across the loop point, {usual} inside");

        // and it is not merely quiet at both ends: something is still sounding there, carried round from the end
        Assert.True(Rms(x, 0, Rate / 50) > 0.002 || Rms(x, x.Length - Rate / 50, Rate / 50) > 0.002);
    }

    [Fact]
    public void A_note_struck_on_the_last_step_rings_on_at_the_start_of_the_loop()
    {
        var song = Bare();
        song.lead = "bell";
        song.melody = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 72 };
        var x = SongSynth.Render(song, out _);
        int step = x.Length / 16;
        double tail = Rms(x, 0, Rate / 20), struck = Rms(x, 15 * step, Rate / 20), longAfter = Rms(x, 8 * step, Rate / 20);
        _out.WriteLine($"struck {struck:F4}  tail at the start {tail:F4}  half a bar later {longAfter:F6}");
        Assert.True(tail > struck * 0.1, "the tail is missing from the start of the loop");
        Assert.True(longAfter < tail * 0.05, "it should have died away by the middle of the bar");
        // the tail runs on from the last sample: no jump where the buffer ends
        float largest = 0;
        for (int i = 0; i < x.Length - 1; i++) largest = Math.Max(largest, Math.Abs(x[i + 1] - x[i]));
        Assert.True(Math.Abs(x[0] - x[^1]) <= largest);
    }

    // ---------------------------------------------------------------------------------------- how the notes are read

    [Fact]
    public void A_held_note_is_not_struck_again()
    {
        var held = Bare(); held.melody = new[] { 72, -1, -1, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var again = Bare(); again.melody = new[] { 72, -1, 72, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var h = SongSynth.Render(held, out _);
        var a = SongSynth.Render(again, out _);
        int third = h.Length / 16 * 2, window = Rate / 50;   // 20 ms either side of where the third step begins

        double heldBefore = Rms(h, third - window, window), heldAfter = Rms(h, third + 64, window);
        double againAfter = Rms(a, third + 64, window);
        _out.WriteLine($"held: {heldBefore:F4} -> {heldAfter:F4}   struck again: -> {againAfter:F4}");
        Assert.True(heldAfter <= heldBefore, "a held pluck goes on dying away");
        Assert.True(againAfter > heldAfter * 2, "a second strike is clearly louder than a hold");
    }

    [Fact]
    public void A_hold_after_a_rest_holds_the_rest()
    {
        var song = Bare(); song.melody = new[] { 0, -1, -1, -1 };
        var x = SongSynth.Render(song, out float seconds);
        Assert.Equal(0f, Peak(x));
        Assert.True(seconds > 0);
    }

    [Fact]
    public void Swing_makes_the_off_beat_come_later_and_leaves_the_beat_alone()
    {
        const float swing = 0.25f;
        SongDef With(string kick, float s) { var d = Bare(); d.kick = kick; d.swing = s; return d; }
        int step = (int)Math.Round(SongSynth.StepSeconds(Bare()) * Rate);

        int offStraight = Onset(SongSynth.Render(With("-x--------------", 0f), out _));
        int offSwung = Onset(SongSynth.Render(With("-x--------------", swing), out _));
        int onStraight = Onset(SongSynth.Render(With("--x-------------", 0f), out _));
        int onSwung = Onset(SongSynth.Render(With("--x-------------", swing), out _));
        _out.WriteLine($"step {step}: off-beat {offStraight} -> {offSwung}, beat {onStraight} -> {onSwung}");

        Assert.InRange(offStraight, step, step + 64);
        Assert.InRange(offSwung - offStraight, swing * step - 8, swing * step + 8);
        Assert.Equal(onStraight, onSwung);
        // the length of the loop, and so the tempo, is the same
        Assert.Equal(SongSynth.Render(With("-x--", 0f), out _).Length, SongSynth.Render(With("-x--", swing), out _).Length);
    }

    [Fact]
    public void A_short_pattern_repeats_and_a_long_one_is_cut()
    {
        var once = Bare(120, 2); once.bass = new[] { 45, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var x = SongSynth.Render(once, out _);
        int bar = x.Length / 2;
        Assert.Equal(Rms(x, 0, bar), Rms(x, bar, bar), 4);   // the second bar is the first again

        var tooLong = Bare(120, 1); tooLong.bass = new int[40]; tooLong.bass[0] = 45; tooLong.bass[20] = 57;
        var cut = Bare(120, 1); cut.bass = new[] { 45 }.Concat(new int[15]).ToArray();
        Assert.True(SongSynth.Render(tooLong, out _).AsSpan().SequenceEqual(SongSynth.Render(cut, out _)));
    }

    [Theory]
    [InlineData("bell"), InlineData("pluck"), InlineData("pad"), InlineData("square"), InlineData("soft"), InlineData("no such thing"), InlineData(null)]
    public void Every_lead_sound_plays_cleanly(string lead)
    {
        var song = WorldDefaults.Lofi();
        song.lead = lead; song.kick = song.snare = song.hat = "";
        var x = SongSynth.Render(song, out _);
        Assert.All(x, v => Assert.True(float.IsFinite(v)));
        Assert.True(Rms(x) > 0.03);
        Assert.True(Peak(x) <= 0.8f * song.volume + 0.02f);
        // no click anywhere: with the drums out, no single step between samples is a large part of the full swing
        float largest = 0;
        for (int i = 0; i < x.Length; i++) largest = Math.Max(largest, Math.Abs(x[(i + 1) % x.Length] - x[i]));
        _out.WriteLine($"{lead ?? "(null)"}: rms {Rms(x):F3}, largest step {largest:F4}");
        Assert.True(largest < 0.25f, $"largest step {largest}");
    }

    [Fact]
    public void Major_and_minor_chords_differ_and_a_bar_without_a_chord_is_silent()
    {
        var major = Bare(100, 2); major.chords = new[] { 60, 0 };
        var minor = Bare(100, 2); minor.chords = new[] { 60, 0 }; minor.minor = true;
        var a = SongSynth.Render(major, out _);
        var b = SongSynth.Render(minor, out _);
        Assert.False(a.AsSpan().SequenceEqual(b));
        int bar = a.Length / 2;
        Assert.True(Rms(a, 0, bar) > 0.05);
        Assert.True(Rms(a, bar + bar / 2, bar / 2) < 1e-4, "the second bar has no chord; only the fade of the first reaches into it");
    }

    // ---------------------------------------------------------------------------------------- the beat, for lights and dancing

    [Fact]
    public void The_beat_has_one_value_per_step_and_is_strongest_on_the_kick()
    {
        foreach (var song in WorldDefaults.Songs())
        {
            var beat = SongSynth.Beat(song);
            Assert.Equal(song.bars * 16, beat.Length);
            Assert.All(beat, v => Assert.InRange(v, 0f, 1f));
        }

        var club = WorldDefaults.Club();
        var b = SongSynth.Beat(club);
        var onKick = new List<float>(); var onNothing = new List<float>();
        for (int i = 0; i < b.Length; i++)
        {
            bool kick = club.kick[i % 16] == 'x';
            bool nothing = !kick && club.snare[i % 16] != 'x' && club.hat[i % 16] != 'x' && club.melody[i % club.melody.Length] == 0 && club.bass[i % club.bass.Length] == 0;
            if (kick) onKick.Add(b[i]);
            if (nothing) onNothing.Add(b[i]);
        }
        Assert.NotEmpty(onKick); Assert.NotEmpty(onNothing);
        Assert.True(onKick.Min() > onNothing.Max() + 0.3f, $"kick {onKick.Min()} against nothing {onNothing.Max()}");

        // a lullaby has no drums, and still breathes with its melody
        var lull = SongSynth.Beat(WorldDefaults.Lullaby());
        Assert.True(lull[0] > lull[1] && lull[1] > 0f);
        Assert.True(lull.Max() < b.Max());
    }

    // ---------------------------------------------------------------------------------------- whatever the language model writes

    [Fact]
    public void Garbage_is_clamped_not_thrown()
    {
        var nulls = new SongDef { bpm = 0, bars = 0, swing = float.NaN, lead = null, melody = null, chords = null, bass = null, kick = null, snare = null, hat = null, volume = float.NaN };
        var x = SongSynth.Render(nulls, out float seconds);
        Assert.True(x.Length > 0 && x.Length < Rate && seconds > 0);   // a short silence
        Assert.Equal(0f, Peak(x));
        Assert.Equal(16, SongSynth.Beat(nulls).Length);                  // bars 0 -> 1
        Assert.Equal(60f / 50f / 4f, SongSynth.StepSeconds(nulls), 5);   // bpm 0 -> 50

        var silly = new SongDef
        {
            bpm = 100000, bars = 99, swing = 7f, lead = "kazoo", volume = 50f, minor = true,
            melody = new[] { 500, -3, 60, int.MinValue, int.MaxValue, 23, 109, 108, 24, -1 },
            chords = new[] { -1, 500, 107, 0, -7 }, bass = new[] { -1, -1, -1 }, kick = "xXzz", snare = "x", hat = "\0x",
        };
        var y = SongSynth.Render(silly, out seconds);
        Assert.Equal(8 * 16 * 60.0 / 180 / 4, seconds, 3);   // bars -> 8, bpm -> 180
        Assert.All(y, v => Assert.True(float.IsFinite(v)));
        Assert.InRange(Peak(y), 0.7f, 0.82f);                // volume -> 1, and something is playing
        Assert.Equal(8 * 16, SongSynth.Beat(silly).Length);
        Assert.All(SongSynth.Beat(silly), v => Assert.InRange(v, 0f, 1f));

        var none = SongSynth.Render(null, out seconds);
        Assert.True(none.Length > 0 && seconds > 0);
        Assert.Equal(0f, Peak(none));
        Assert.NotEmpty(SongSynth.Beat(null));
        Assert.True(SongSynth.StepSeconds(null) > 0);

        var mute = WorldDefaults.Club(); mute.volume = 0f;
        Assert.Equal(0f, Peak(SongSynth.Render(mute, out _)));
    }

    // ---------------------------------------------------------------------------------------- for the ear, and the clock

    /// <summary>Each tune twice in a row, so that the place where it loops can be listened to: artifacts/songs/*.wav.</summary>
    [Fact]
    public void The_built_in_tunes_are_written_out_for_listening()
    {
        string dir = Path.Combine(RepoRoot(), "artifacts", "songs");
        Directory.CreateDirectory(dir);
        foreach (var song in WorldDefaults.Songs())
        {
            var x = SongSynth.Render(song, out float seconds);
            string path = Path.Combine(dir, song.id + ".wav");
            WriteWav(path, x, loops: 2);
            Assert.Equal(44 + x.Length * 2 * 2, new FileInfo(path).Length);
            _out.WriteLine($"{path}  ({seconds:F2} s x 2)");
        }
    }

    [Fact]
    public void Rendering_a_tune_takes_a_small_part_of_a_second()
    {
        var song = WorldDefaults.Lofi();
        SongSynth.Render(song, out float seconds);   // the first call also pays for compiling the code
        double best = double.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            SongSynth.Render(song, out _);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        _out.WriteLine($"lo-fi: {seconds:F2} s of sound rendered in {best:F1} ms (best of 5) = {seconds * 1000 / best:F0} times faster than it plays");
        Console.WriteLine($"[SongSynth] lo-fi: {seconds:F2} s of sound rendered in {best:F1} ms (best of 5)");
        Assert.True(best < 500, $"{best} ms");   // on a PC; the headset is several times slower and must still stay under a second
    }

    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !(Directory.Exists(Path.Combine(d.FullName, "Companion")) && Directory.Exists(Path.Combine(d.FullName, "MetaVRHack")))) d = d.Parent;
        Assert.True(d != null, "the repository root was not found above " + AppContext.BaseDirectory);
        return d.FullName;
    }

    /// <summary>16-bit mono PCM.</summary>
    static void WriteWav(string path, float[] samples, int loops)
    {
        int bytes = samples.Length * loops * 2;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        for (int l = 0; l < loops; l++)
            foreach (float v in samples) w.Write((short)Math.Round(Math.Clamp(v, -1f, 1f) * 32767f));
    }
}
