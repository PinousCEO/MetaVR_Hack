using System;
using System.Collections.Generic;
using LivingPartner.Shared;

namespace LivingPartner.MR
{
    /// <summary>
    /// A TEST CARD for the two renderers of the glass games (MrGlass on the desk, PcGlass inside a monitor). It is not
    /// a game anybody is offered and no template of GlassGames: `lpctl debug glassStart 0 0 0 test:flat` (or
    /// test:upright; add :full for more than the pools hold) starts it on the side the partner is on. It draws what a
    /// scripted game draws with, so that every drawing convention can be seen at once without a script:
    ///  - a board of plain shapes: a wood-coloured rect, a 9 x 9 grid of thin rects (lines), five discs, one line
    ///    turned by 45 degrees;
    ///  - a disc with a hoop round it on the grid point nearest to where a hand touches down (flat: a fingertip within
    ///    2 cm of the desk; upright: a palm within 5 cm of the plane), cream and brown in turn; the partner walks to
    ///    it (flat) or under it (upright);
    ///  - a hoop where each hand is, larger the further the hand is away;
    ///  - a built-in sticker as it is and tinted, and a picture of the pack ("pack:stone": the test has none, so the
    ///    renderer's stand-in shows);
    ///  - words: bare and coloured, and without a colour (on their strip of paper);
    ///  - with :full, 165 pictures and 34 words: five and two more than a renderer has to draw.
    /// Twelve stones end it (phase over), to see the result card.
    /// </summary>
    public sealed class GlassTestCard : GlassGame
    {
        const int N = 9, Ink = 0x4A2F27, Cream = 0xFFF8EA, Brown = 0x7C5544, Wood = 0xF1CF98;
        const float Side = 0.80f, Bottom = 0.08f;
        readonly bool full;
        readonly List<int> stones = new List<int>();
        readonly bool[] down = new bool[2];
        float clock, goalX, goalY;

        public GlassTestCard(bool flat, bool full) { this.flat = flat; this.full = full; }
        public override string Template { get { return "test"; } }

        float X0 { get { return (W - Side) * 0.5f; } }
        float Cell { get { return Side / N; } }

        public override void Start(GamePackDef pack, int seed)
        {
            phase = "intro"; banner = flat ? "Test card: touch a point with a fingertip" : "Test card: hold a hand to a point";
            result = ""; score = 0; lives = 3; level = 1; seconds = 0f; clock = 0f;
            stones.Clear(); down[0] = down[1] = false;
            petX = goalX = W - 0.16f; petY = goalY = flat ? 0.5f : 0f; petFacesRight = false; petPose = "idle";
            sprites.Clear(); sounds.Clear();
        }

        void Put(string art, float x, float y, float size, float w = 0f, int color = -1, float rot = 0f, float alpha = 1f, string text = "")
        {
            var s = new GlassSprite();
            s.id = sprites.Count + 1; s.art = art; s.x = x; s.y = y; s.size = size; s.w = w; s.color = color; s.rot = rot; s.alpha = alpha; s.text = text; s.text2 = "";
            sprites.Add(s);
        }

        public override void Step(float dt, GlassInput input)
        {
            sprites.Clear(); sounds.Clear();
            if (!(dt > 0f)) dt = 0f; else if (dt > 0.25f) dt = 0.25f;
            clock += dt;
            if (phase == "intro" && clock > 1.5f) { phase = "play"; banner = ""; }
            float cell = Cell, x0 = X0;
            if (phase == "play")
            {
                seconds += dt;
                for (int h = 0; h < 2; h++)
                {
                    bool touch = input != null && input.valid[h] && input.dist[h] < (flat ? 0.02f : 0.05f);
                    if (touch && !down[h])
                    {
                        int col = (int)Math.Floor((input.x[h] - x0) / cell), row = (int)Math.Floor((input.y[h] - Bottom) / cell);
                        if (col >= 0 && col < N && row >= 0 && row < N && !stones.Contains(row * N + col))
                        {
                            stones.Add(row * N + col); score++; sounds.Add("tap");
                            goalX = x0 + (col + 0.5f) * cell + (flat ? cell : 0f); goalY = flat ? Bottom + (row + 0.5f) * cell : 0f;
                        }
                    }
                    down[h] = touch;
                }
                if (stones.Count >= 12) { phase = "over"; result = "Twelve stones: that is all the card asks for."; sounds.Add("ta-da"); }
            }
            // the partner walks to the stone that was placed last
            float dx = goalX - petX, dy = goalY - petY, left = (float)Math.Sqrt(dx * dx + dy * dy);
            if (left > 0.01f) { float step = Math.Min(left, 0.5f * dt); petX += dx / left * step; petY += dy / left * step; petFacesRight = dx > 0f; petPose = "walk"; }
            else petPose = phase == "over" ? "cheer" : "idle";

            // ---- the board, of plain shapes
            float mid = W * 0.5f, midY = Bottom + Side * 0.5f;
            Put(GlassArt.Rect, mid, midY, Side + 0.05f, Side + 0.05f, Wood);
            for (int i = 0; i < N; i++)
            {
                Put(GlassArt.Rect, x0 + (i + 0.5f) * cell, midY, Side - cell, 0.006f, Ink);
                Put(GlassArt.Rect, mid, Bottom + (i + 0.5f) * cell, 0.006f, Side - cell, Ink);
            }
            Put(GlassArt.Rect, mid, midY, 0.5f, 0.005f, 0xE5484D, 45f, 0.55f);      // a line turned by 45 degrees: from the lower right to the upper left
            int[] dots = { 2, 2, 6, 2, 4, 4, 2, 6, 6, 6 };
            for (int i = 0; i < dots.Length; i += 2) Put(GlassArt.Disc, x0 + (dots[i] + 0.5f) * cell, Bottom + (dots[i + 1] + 0.5f) * cell, 0.022f, 0f, Ink);
            for (int i = 0; i < stones.Count; i++)
            {
                float sx = x0 + (stones[i] % N + 0.5f) * cell, sy = Bottom + (stones[i] / N + 0.5f) * cell;
                Put(GlassArt.Disc, sx, sy, cell * 0.86f, 0f, i % 2 == 0 ? Cream : Brown);
                Put(GlassArt.RingShape, sx, sy, cell * 0.90f, 0f, Ink);
            }
            // ---- the hands
            for (int h = 0; h < 2; h++)
                if (input != null && input.valid[h]) Put(GlassArt.RingShape, input.x[h], input.y[h], 0.05f + Math.Min(0.3f, input.dist[h]), 0f, h == 0 ? 0x3B82F6 : 0x22A06B, 0f, 0.9f);
            // ---- pictures: a sticker as it is, the same one tinted, one of the pack's own, an oval (a disc with a width)
            float lx = x0 * 0.5f;
            Put(GlassArt.Star, lx - 0.08f, 0.30f, 0.10f);
            Put(GlassArt.Star, lx + 0.08f, 0.30f, 0.10f, 0f, 0xFF7AA2);
            Put(GlassArt.PackPrefix + "stone", lx - 0.08f, 0.15f, 0.10f);
            Put(GlassArt.Disc, lx + 0.08f, 0.15f, 0.06f, 0.14f, 0x8FD3F4, 20f);
            // ---- words
            Put(GlassArt.Label, lx, 0.80f, 0.060f, 0f, Ink, 0f, 1f, "Test card");
            Put(GlassArt.Label, lx, 0.68f, 0.045f, 0f, 0x2E7D32, 0f, 1f, stones.Count + " of 12");
            Put(GlassArt.Label, lx, 0.56f, 0.040f, 0f, -1, 0f, 1f, flat ? "flat" : "upright");
            if (full)
            {
                // more than the pools hold: 165 pictures and 34 words (the last five and two are not drawn)
                int pictures = 0, words = 0;
                for (int i = 0; i < sprites.Count; i++) if (sprites[i].art == GlassArt.Label) words++; else pictures++;
                float rx = W - x0 * 0.5f;
                for (int k = 0; pictures < 165; k++, pictures++)
                {
                    float a = k * 0.4f + clock, r = 0.03f + 0.0016f * k;
                    Put(GlassArt.Disc, rx + (float)Math.Cos(a) * r, 0.5f + (float)Math.Sin(a) * r, 0.014f, 0f, Hue(k * 9), 0f, 0.9f);
                }
                for (int k = 0; words < 34; k++, words++)
                    Put(GlassArt.Label, x0 + (k % N + 0.5f) * cell, Bottom + (k / N + 5.5f) * cell, 0.028f, 0f, Hue(k * 40), 0f, 1f, (k + 1).ToString());
            }
        }

        static int Hue(int degrees)
        {
            float h = (degrees % 360) / 60f, x = 1f - Math.Abs(h % 2f - 1f);
            float r = h < 1 ? 1 : h < 2 ? x : h < 4 ? 0 : h < 5 ? x : 1, g = h < 1 ? x : h < 3 ? 1 : h < 4 ? x : 0, b = h < 2 ? 0 : h < 3 ? x : h < 5 ? 1 : x;
            return ((int)(60 + r * 180) << 16) | ((int)(60 + g * 180) << 8) | (int)(60 + b * 180);
        }
    }
}
