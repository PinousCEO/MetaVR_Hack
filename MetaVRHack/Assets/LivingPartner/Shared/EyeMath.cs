using System;

namespace LivingPartner.Shared
{
    /// <summary>
    /// The sums of "the partner looks at what the user holds up" (Msg.EyeRequest / EyeFrame; MR/MrEyes.cs takes the
    /// picture): which part of the camera's picture is kept, and how it is brought to the size and the row order a JPEG
    /// encoder wants. Pure, so that the tests can check it without a headset.
    /// </summary>
    public static class EyeMath
    {
        /// <summary>The box round a few points of a picture, grown as points are added. Empty until one is.</summary>
        public struct Box
        {
            public float x0, y0, x1, y1; public int points;
            public bool Any => points > 0;
            public void Add(float x, float y)
            {
                if (points == 0) { x0 = x1 = x; y0 = y1 = y; }
                else { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                points++;
            }
        }

        /// <summary>
        /// A point in front of a camera as a pixel: (x right, y up, z out of the lens) in metres, the lens given by its
        /// focal lengths and centre in pixels (cy counted from the TOP row). False when the point is behind the lens or
        /// too close to it to mean anything.
        /// </summary>
        public static bool Project(float x, float y, float z, float fx, float fy, float cx, float cy, out float px, out float py)
        {
            px = py = 0f;
            if (!(z > 0.05f)) return false;
            px = cx + fx * x / z; py = cy - fy * y / z;
            return !float.IsNaN(px) && !float.IsNaN(py);
        }

        /// <summary>
        /// Which part of the picture is kept (x, y of its top-left corner, its width and height, in pixels). hands: the
        /// box round the points of the user's hands that fall inside the picture - the thing they hold reaches well
        /// beyond the fingers, so the box is doubled and a quarter of the picture is added, and it is never less than
        /// minSide across. No hand seen: the middle of the picture.
        /// </summary>
        public static void Crop(int imgW, int imgH, Box hands, out int x, out int y, out int w, out int h, int minSide = 560)
        {
            x = y = w = h = 0;
            if (imgW <= 0 || imgH <= 0) return;
            if (!hands.Any)
            {
                w = Math.Max(1, (int)(imgW * 0.72f)); h = Math.Max(1, (int)(imgH * 0.72f));
                x = (imgW - w) / 2; y = (imgH - h) / 2;
                return;
            }
            float bw = hands.x1 - hands.x0, bh = hands.y1 - hands.y0, mx = (hands.x0 + hands.x1) * 0.5f, my = (hands.y0 + hands.y1) * 0.5f;
            float side = Math.Max(Math.Max(bw, bh) * 2f + 0.25f * Math.Min(imgW, imgH), minSide);
            w = (int)Math.Min(side, imgW); h = (int)Math.Min(side, imgH);
            x = (int)Math.Max(0f, Math.Min(mx - w * 0.5f, imgW - w)); y = (int)Math.Max(0f, Math.Min(my - h * 0.5f, imgH - h));
        }

        /// <summary>
        /// Cuts the box (x, y, w, h) out of a picture of 4 bytes a pixel (top row first), makes it smaller by whole steps
        /// until its longest side is at most maxSide (each pixel the average of the ones it stands for), and writes it
        /// with the BOTTOM row first - the order Unity's JPEG encoder reads. Returns the bytes; outW / outH: its size.
        /// </summary>
        public static byte[] Cut(byte[] rgba, int imgW, int imgH, int x, int y, int w, int h, int maxSide, out int outW, out int outH)
        {
            outW = outH = 0;
            if (rgba == null || imgW <= 0 || imgH <= 0 || rgba.Length < imgW * imgH * 4) return null;
            x = Math.Max(0, Math.Min(x, imgW - 1)); y = Math.Max(0, Math.Min(y, imgH - 1));
            w = Math.Max(1, Math.Min(w, imgW - x)); h = Math.Max(1, Math.Min(h, imgH - y));
            int step = 1;
            while (Math.Max(w, h) / step > Math.Max(16, maxSide)) step++;
            outW = Math.Max(1, w / step); outH = Math.Max(1, h / step);
            var cut = new byte[outW * outH * 4];
            int n = step * step;
            for (int oy = 0; oy < outH; oy++)
            {
                int row = (outH - 1 - oy) * outW * 4;       // bottom row first
                for (int ox = 0; ox < outW; ox++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int dy = 0; dy < step; dy++)
                    {
                        int src = ((y + oy * step + dy) * imgW + x + ox * step) * 4;
                        for (int dx = 0; dx < step; dx++, src += 4) { r += rgba[src]; g += rgba[src + 1]; b += rgba[src + 2]; }
                    }
                    int o = row + ox * 4;
                    cut[o] = (byte)(r / n); cut[o + 1] = (byte)(g / n); cut[o + 2] = (byte)(b / n); cut[o + 3] = 255;
                }
            }
            return cut;
        }
    }
}
