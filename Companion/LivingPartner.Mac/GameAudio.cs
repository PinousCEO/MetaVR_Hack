using System.Text;
using LivingPartner.Shared;

namespace LivingPartner.Mac;

// Serve the same synthesized effects as the headset/PC from the loopback-only TV stage.
internal static class GameAudio
{
    static readonly Dictionary<string, Lazy<byte[]>> Clips = SfxSynth.Names.ToDictionary(name => name,
        name => new Lazy<byte[]>(() => Wave(SfxSynth.Render(name))));

    public static IResult Get(string name) => Clips.TryGetValue(name, out var clip) ?
        Results.Bytes(clip.Value, "audio/wav") : Results.NotFound();

    static byte[] Wave(float[] samples)
    {
        using var buffer = new MemoryStream(44 + samples.Length * 2);
        using var wave = new BinaryWriter(buffer, Encoding.ASCII, true);
        wave.Write("RIFF"u8); wave.Write(36 + samples.Length * 2);
        wave.Write("WAVEfmt "u8); wave.Write(16); wave.Write((short)1); wave.Write((short)1);
        wave.Write(SfxSynth.SampleRate); wave.Write(2 * SfxSynth.SampleRate); wave.Write((short)2); wave.Write((short)16);
        wave.Write("data"u8); wave.Write(samples.Length * 2);
        foreach (float sample in samples) wave.Write((short)Math.Round(Math.Clamp(sample, -1, 1) * short.MaxValue));
        return buffer.ToArray();
    }
}
