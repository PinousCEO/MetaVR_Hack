using LivingPartner.Shared;
using Xunit;
using Xunit.Abstractions;

namespace LivingPartner.Tests;

/// <summary>The kit of small sound effects the headset makes at its start (button ticks, bells, a hammer, a fanfare ...).</summary>
public class SfxSynthTests
{
    readonly ITestOutputHelper _out;
    public SfxSynthTests(ITestOutputHelper o) { _out = o; }

    public static IEnumerable<object[]> Names() => SfxSynth.Names.Select(n => new object[] { n });

    [Fact]
    public void The_kit_has_every_sound_the_headset_asks_for()
    {
        string[] asked =
        {
            "tap", "pop", "open", "close", "page", "hammer", "sparkle", "ta-da", "coin", "error",
            "whoosh", "land", "squeak", "step", "hit", "bounce", "win", "lose", "drop", "tick",
        };
        foreach (string name in asked) Assert.Contains(name, SfxSynth.Names);
        Assert.Equal(SfxSynth.Names.Length, SfxSynth.Names.Distinct().Count());
        Assert.Equal(SongSynth.SampleRate, SfxSynth.SampleRate);
    }

    [Theory, MemberData(nameof(Names))]
    public void A_sound_is_short_audible_and_never_clips(string name)
    {
        float[] x = SfxSynth.Render(name);
        Assert.NotNull(x);
        Assert.All(x, v => Assert.True(float.IsFinite(v)));
        double seconds = x.Length / (double)SfxSynth.SampleRate;
        float peak = x.Max(v => Math.Abs(v));
        double rms = Math.Sqrt(x.Sum(v => (double)v * v) / x.Length), mean = x.Average(v => (double)v);
        _out.WriteLine($"{name}: {seconds:F3} s  peak {peak:F3}  rms {rms:F3}");
        Assert.InRange(seconds, 0.03, 1.5);
        Assert.InRange(peak, 0.3f, 1.0f);
        Assert.True(rms > 0.01, $"rms {rms}");                  // something is really sounding, not one stray sample
        Assert.True(Math.Abs(mean) < 0.01, $"mean {mean}");     // it sits round zero
    }

    [Theory, MemberData(nameof(Names))]
    public void A_sound_starts_and_ends_without_a_click(string name)
    {
        float[] x = SfxSynth.Render(name);
        float peak = x.Max(v => Math.Abs(v));
        Assert.True(Math.Abs(x[0]) < 0.15f * peak, $"first sample {x[0]}");
        Assert.True(Math.Abs(x[^1]) < 0.02f * peak, $"last sample {x[^1]}");
    }

    [Theory, MemberData(nameof(Names))]
    public void A_sound_is_the_same_every_time_it_is_made(string name)
    {
        float[] a = SfxSynth.Render(name), b = SfxSynth.Render(name);
        Assert.Equal(a.Length, b.Length);
        Assert.Equal(a, b);
    }

    [Fact]
    public void A_name_nobody_knows_gives_nothing()
    {
        Assert.Null(SfxSynth.Render("kazoo"));
        Assert.Null(SfxSynth.Render(null));
    }

    [Fact]
    public void A_tune_brought_up_for_the_headset_peaks_where_it_was_told_to()
    {
        var song = WorldDefaults.Songs().First();
        float[] x = SongSynth.Render(song, out _);
        float before = x.Max(v => Math.Abs(v));
        float was = SongSynth.Normalize(x, 0.95f);
        Assert.Equal(before, was, 4);
        Assert.InRange(x.Max(v => Math.Abs(v)), 0.949f, 0.951f);
        Assert.Equal(0f, SongSynth.Normalize(new float[100]));       // silence stays silence
        Assert.Equal(0f, SongSynth.Normalize(null));
    }
}
