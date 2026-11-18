using System.Numerics;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// Finding a display that shows one plain colour in a camera picture, and where it is in space. The pictures are
/// rendered here with the numbers of a Quest 3 passthrough camera (1280x960, focal length 866 px).
/// </summary>
public class ScreenFinderTests
{
    const int W = 1280, H = 960;
    const float Fx = 865.65f, Fy = 865.65f, Cx = 643.28f, Cy = 480.9f;

    sealed class Screen
    {
        public Vector3 centre, right, up; public float w, h; public byte r, g, b;
        public Vector3 Tl => centre - right * (w / 2) + up * (h / 2);
        public Vector3 Tr => centre + right * (w / 2) + up * (h / 2);
        public Vector3 Bl => centre - right * (w / 2) - up * (h / 2);
        public Vector3 Br => centre + right * (w / 2) - up * (h / 2);
    }

    static Screen Make(Vector3 centre, float yawDeg, float pitchDeg, float rollDeg, float w, float h, byte r, byte g, byte b)
    {
        var q = Quaternion.CreateFromYawPitchRoll(yawDeg * M.Deg2Rad, pitchDeg * M.Deg2Rad, rollDeg * M.Deg2Rad);
        return new Screen { centre = centre, right = Vector3.Transform(Vector3.UnitX, q), up = Vector3.Transform(Vector3.UnitY, q), w = w, h = h, r = r, g = g, b = b };
    }

    /// <summary>A grey room with a little noise, the screens in it, seen from the origin looking along +z.</summary>
    static byte[] Render(params Screen[] screens)
    {
        var img = new byte[W * H * 4];
        var rnd = new Random(7);
        for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                int o = (j * W + i) * 4;
                byte grey = (byte)(70 + rnd.Next(30));
                img[o] = grey; img[o + 1] = grey; img[o + 2] = (byte)(grey + 6); img[o + 3] = 255;
                var ray = ScreenFinder.PixelRay(new Vector2(i + 0.5f, j + 0.5f), Fx, Fy, Cx, Cy);
                float nearest = float.MaxValue;
                foreach (var s in screens)
                {
                    var n = Vector3.Cross(s.right, s.up);
                    float den = Vector3.Dot(ray, n);
                    if (Math.Abs(den) < 1e-6f) continue;
                    float t = Vector3.Dot(s.centre, n) / den;
                    if (t <= 0 || t > nearest) continue;
                    var p = ray * t - s.centre;
                    if (Math.Abs(Vector3.Dot(p, s.right)) > s.w / 2 || Math.Abs(Vector3.Dot(p, s.up)) > s.h / 2) continue;
                    nearest = t;
                    int jitter = rnd.Next(12);
                    img[o] = (byte)Math.Min(255, s.r + jitter); img[o + 1] = (byte)Math.Min(255, s.g + jitter); img[o + 2] = (byte)Math.Min(255, s.b + jitter);
                }
            }
        return img;
    }

    static void Fill(byte[] img, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int j = y0; j < y1; j++) for (int i = x0; i < x1; i++) { int o = (j * W + i) * 4; img[o] = r; img[o + 1] = g; img[o + 2] = b; }
    }

    static float Locate(byte[] img, int colour, Screen truth, out float height, out string why)
    {
        height = 0;
        var f = new ScreenFinder();
        if (!f.FindQuad(img, W, H, colour, out var q, out why)) return float.MaxValue;
        Vector3 R(Vector2 p) => ScreenFinder.PixelRay(p, Fx, Fy, Cx, Cy);
        if (!ScreenFinder.SolveRectangle(Vector3.Zero, R(q.tl), R(q.tr), R(q.br), R(q.bl), truth.w, out var tl, out var tr, out var br, out var bl, out height, out float skew)) { why = "no solution"; return float.MaxValue; }
        Assert.True(Math.Abs(skew) < 1.0f, $"skew {skew}");
        return Math.Max(Math.Max((tl - truth.Tl).Length(), (tr - truth.Tr).Length()), Math.Max((bl - truth.Bl).Length(), (br - truth.Br).Length()));
    }

    [Theory]
    [InlineData(0f, 0.02f, 0.74f, 0f, 0f, 0f)]            // straight ahead
    [InlineData(-0.10f, 0.05f, 0.80f, 28f, -8f, 0f)]      // turned and leaning back
    [InlineData(0.12f, -0.03f, 0.95f, -35f, 5f, 6f)]      // off to the side, seen with the head tilted
    [InlineData(0.0f, 0.0f, 1.60f, 15f, 0f, -9f)]         // far away
    public void A_landscape_display_is_located_to_a_few_millimetres(float x, float y, float z, float yaw, float pitch, float roll)
    {
        var s = Make(new Vector3(x, y, z), yaw, pitch, roll, 0.598f, 0.336f, 20, 160, 25);
        float err = Locate(Render(s), ScreenFinder.Green, s, out float height, out string why);
        Assert.True(err < 0.004f + 0.004f * z, $"corner error {err * 1000:0.0} mm ({why})");
        Assert.True(Math.Abs(height - s.h) < 0.006f, $"height {height}");
    }

    [Fact]
    public void A_portrait_display_next_to_others_in_other_colours_is_the_one_asked_for()
    {
        var portrait = Make(new Vector3(-0.28f, 0.03f, 0.78f), -22f, 0f, 0f, 0.336f, 0.598f, 170, 20, 20);
        var beside = Make(new Vector3(0.20f, 0.0f, 0.80f), 6f, -6f, 0f, 0.598f, 0.336f, 20, 30, 170);
        var img = Render(portrait, beside);
        Assert.True(Locate(img, ScreenFinder.Red, portrait, out float hp, out string w1) < 0.006f, w1);
        Assert.True(Math.Abs(hp - 0.598f) < 0.008f);
        Assert.True(Locate(img, ScreenFinder.Blue, beside, out _, out string w2) < 0.006f, w2);
        Assert.False(new ScreenFinder().FindQuad(img, W, H, ScreenFinder.Green, out _, out _));
    }

    [Fact]
    public void A_cursor_a_reflection_and_a_hand_over_an_edge_do_not_move_the_corners()
    {
        var s = Make(new Vector3(0.02f, 0.0f, 0.74f), 12f, -5f, 3f, 0.598f, 0.336f, 20, 160, 25);
        var img = Render(s);
        Fill(img, 600, 440, 640, 500, 250, 250, 250);          // a white window in the middle
        Fill(img, 380, 300, 470, 380, 90, 80, 70);             // a hand over the left edge
        Fill(img, 700, 250, 760, 330, 240, 240, 230);          // a lamp reflected at the top edge
        Fill(img, 100, 800, 160, 860, 30, 170, 30);            // something green elsewhere in the room
        float err = Locate(img, ScreenFinder.Green, s, out _, out string why);
        Assert.True(err < 0.006f, $"corner error {err * 1000:0.0} mm ({why})");
    }

    [Fact]
    public void A_display_that_runs_out_of_the_picture_is_not_guessed_at()
    {
        var s = Make(new Vector3(0.52f, 0.0f, 0.74f), 0f, 0f, 0f, 0.598f, 0.336f, 20, 160, 25);
        Assert.False(new ScreenFinder().FindQuad(Render(s), W, H, ScreenFinder.Green, out _, out string why));
        Assert.Contains("out of the picture", why);
    }

    [Fact]
    public void Washed_out_or_dark_pixels_are_not_taken_for_a_colour()
    {
        Assert.True(ScreenFinder.IsColour(ScreenFinder.Green, 90, 240, 110));
        Assert.True(ScreenFinder.IsColour(ScreenFinder.Red, 230, 70, 60));
        Assert.True(ScreenFinder.IsColour(ScreenFinder.Blue, 60, 100, 250));
        Assert.False(ScreenFinder.IsColour(ScreenFinder.Green, 235, 255, 240));     // overexposed: nearly white
        Assert.False(ScreenFinder.IsColour(ScreenFinder.Green, 20, 50, 20));        // too dark
        Assert.False(ScreenFinder.IsColour(ScreenFinder.Red, 180, 150, 140));       // skin
    }
}
