using LivingPartner.Host;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// The check that tells an icon on a round plate (or with a ring drawn round it) from a die-cut sticker of the object
/// alone (IconCheck.cs, from the host's own source), on shapes made here: opacity only, 256 x 256.
/// </summary>
public class IconCheckTests
{
    const int N = 256;

    static byte[] Paint(Func<double, double, bool> inside)
    {
        var a = new byte[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                if (inside(x + 0.5 - N / 2.0, y + 0.5 - N / 2.0)) a[y * N + x] = 255;
        return a;
    }

    static bool Disc(double x, double y, double r, double cx = 0, double cy = 0) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
    static bool Box(double x, double y, double hw, double hh, double cx = 0, double cy = 0) => Math.Abs(x - cx) <= hw && Math.Abs(y - cy) <= hh;

    [Fact]
    public void A_disc_is_round()
    {
        // a plate with the object somewhere on it: only the plate's outline is seen
        Assert.True(IconCheck.LooksRound(Paint((x, y) => Disc(x, y, 100)), N, N, out float spread, out float off));
        Assert.True(spread < 0.02f, spread.ToString()); Assert.True(off < 0.02f);
        // wherever it sits in the picture, and at another size
        Assert.True(IconCheck.LooksRound(Paint((x, y) => Disc(x, y, 60, 30, -20)), N, N));
    }

    [Fact]
    public void A_ring_with_an_object_inside_is_round()
    {
        // a thin circle drawn round the object (three pixels of ink), the object well inside it
        var ring = Paint((x, y) => (Disc(x, y, 104) && !Disc(x, y, 101)) || Box(x, y, 30, 55) || Disc(x, y, 25, 30, -30));
        Assert.True(IconCheck.LooksRound(ring, N, N));
        // and a plate with a rim, the object drawn over it
        Assert.True(IconCheck.LooksRound(Paint((x, y) => Disc(x, y, 96) || (Disc(x, y, 106) && !Disc(x, y, 102))), N, N));
        // a soft (anti-aliased) ring is still a ring
        var soft = Paint((x, y) => Box(x, y, 40, 40));
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                double r = Math.Sqrt((x + 0.5 - N / 2.0) * (x + 0.5 - N / 2.0) + (y + 0.5 - N / 2.0) * (y + 0.5 - N / 2.0));
                if (Math.Abs(r - 100) < 1.5) soft[y * N + x] = (byte)Math.Max(soft[y * N + x], (int)(255 * (1 - Math.Abs(r - 100) / 1.5) * 0.9 + 60 * 0.1));
            }
        Assert.True(IconCheck.LooksRound(soft, N, N));
    }

    [Fact]
    public void An_irregular_blob_is_not_round()
    {
        // a die-cut sticker: the outline follows the object (here: a body, a head, two ears and a tail)
        var blob = Paint((x, y) => Disc(x, y, 55, 0, 25) || Disc(x, y, 38, 0, -45) || Disc(x, y, 14, -30, -85) || Disc(x, y, 14, 30, -85) || Box(x, y, 40, 8, 70, 40));
        Assert.False(IconCheck.LooksRound(blob, N, N, out float spread, out _));
        Assert.True(spread > 0.2f, spread.ToString());
        // an egg: smooth, but not a circle
        Assert.False(IconCheck.LooksRound(Paint((x, y) => x * x / (70.0 * 70.0) + y * y / (100.0 * 100.0) <= 1), N, N));
        // a star: as wide as it is high, and nothing like a circle
        Assert.False(IconCheck.LooksRound(Paint((x, y) => { double a = Math.Atan2(y, x), r = Math.Sqrt(x * x + y * y); return r <= 60 + 40 * Math.Cos(5 * a); }), N, N));
    }

    [Fact]
    public void A_square_is_not_round()
    {
        Assert.False(IconCheck.LooksRound(Paint((x, y) => Box(x, y, 90, 90)), N, N, out float spread, out float off));
        Assert.True(off < 0.02f);                              // square it is ...
        Assert.True(spread > 0.2f, spread.ToString());         // ... but its corners are far from its sides
        // a picture with no see-through background at all is a square, too
        Assert.False(IconCheck.LooksRound(Paint((x, y) => true), N, N));
    }

    [Fact]
    public void What_cannot_be_judged_is_not_called_round()
    {
        Assert.False(IconCheck.LooksRound(new byte[N * N], N, N));                         // nothing drawn
        Assert.False(IconCheck.LooksRound(Paint((x, y) => Disc(x, y, 12)), N, N));         // a speck
        Assert.False(IconCheck.LooksRound(null, N, N));
        Assert.False(IconCheck.LooksRound(new byte[10], N, N));
        // a plate with something sticking far out of it: the box round it is no square any more, and it is left alone
        Assert.False(IconCheck.LooksRound(Paint((x, y) => Disc(x, y, 80) || Box(x, y, 10, 20, 0, -95)), N, N));
        // a little way out is still a plate
        Assert.True(IconCheck.LooksRound(Paint((x, y) => Disc(x, y, 100) || Box(x, y, 6, 4, 0, -100)), N, N));
    }
}
