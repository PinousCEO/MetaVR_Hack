namespace LivingPartner.Host;

/// <summary>
/// Looks at the outline of a finished icon. Every icon is meant to be a die-cut sticker of the object alone; now and
/// then the image model puts a round plate behind the object or draws a thin circle round it, and then the icons in
/// the bag do not look like one family any more. Such a picture is recognised by its outline: seen from the middle,
/// the outermost opaque pixel is the same distance away in every direction.
/// Plain arithmetic on the picture's opacity (no drawing library: the tests run it on shapes they make themselves).
/// </summary>
public static class IconCheck
{
    /// <summary>The outer contour counts as a circle when its radii differ by less than this much of their mean ...</summary>
    public const float MaxSpread = 0.03f;
    /// <summary>... and the box round everything opaque is square within this much.</summary>
    public const float MaxOffSquare = 0.04f;
    const int Opaque = 40;              // (of 255) a pixel this opaque belongs to the drawing - the thin ring of a plate is anti-aliased
    const int Rays = 180;

    /// <summary>
    /// Does the opaque part of the picture have a near-perfect circle as its outline (a round plate, a ring, a
    /// badge - or a thing that is simply round, like a ball: the caller draws once more and then takes what comes)?
    /// alpha: one byte per pixel, row by row. spread: how much the radii of the outline differ (of their mean);
    /// offSquare: how far the box round the drawing is from a square.
    /// </summary>
    public static bool LooksRound(byte[] alpha, int w, int h, out float spread, out float offSquare)
    {
        spread = 1f; offSquare = 1f;
        if (alpha == null || w < 8 || h < 8 || alpha.Length < w * h) return false;
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (alpha[y * w + x] > Opaque) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        if (x1 < 0) return false;                                           // nothing drawn at all
        int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
        if (Math.Min(bw, bh) < Math.Min(w, h) / 4) return false;            // a speck: not an icon to judge
        offSquare = Math.Abs(bw - bh) / (float)Math.Max(bw, bh);
        // the outermost opaque pixel in every direction, seen from the middle of that box
        double cx = (x0 + x1) * 0.5, cy = (y0 + y1) * 0.5, far = Math.Sqrt((double)bw * bw + (double)bh * bh) * 0.5 + 2;
        var radii = new double[Rays];
        void Measure()
        {
            for (int i = 0; i < Rays; i++)
            {
                double a = i * 2 * Math.PI / Rays, dx = Math.Cos(a), dy = Math.Sin(a), found = 0;
                for (double r = far; r > 0; r -= 0.5)
                {
                    int x = (int)Math.Round(cx + dx * r), y = (int)Math.Round(cy + dy * r);
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    if (alpha[y * w + x] > Opaque) { found = r; break; }
                }
                radii[i] = found;
            }
        }
        Measure();
        // The middle of the box is not quite the middle of a plate that something pokes out of: seen from beside the
        // middle, a circle's radius swings once round (r = R + ex cos a + ey sin a). That swing says where the middle
        // is; the outline is measured once more from there.
        double ex = 0, ey = 0;
        for (int i = 0; i < Rays; i++) { double a = i * 2 * Math.PI / Rays; ex += radii[i] * Math.Cos(a); ey += radii[i] * Math.Sin(a); }
        cx += ex * 2 / Rays; cy += ey * 2 / Rays;
        Measure();
        Array.Sort(radii);
        double mean = radii.Average();
        if (mean < 1) return false;
        // (the middle nine tenths: a leaf or a handle that pokes a little way out of a plate does not make it less of a plate)
        spread = (float)((radii[Rays * 95 / 100] - radii[Rays * 5 / 100]) / mean);
        return spread < MaxSpread && offSquare <= MaxOffSquare;
    }

    public static bool LooksRound(byte[] alpha, int w, int h) => LooksRound(alpha, w, h, out _, out _);
}
