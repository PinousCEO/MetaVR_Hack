using System;
using System.Numerics;

namespace LivingPartner.Shared
{
    /// <summary>
    /// Finds a display in a camera picture and works out where it is in space.
    ///
    /// The PC lights the display with one plain colour for a moment (red, green or blue - which colour says which
    /// display). In the picture that is one large patch of that colour with four straight edges. The edges are fitted as
    /// lines (so a cursor, a reflection or a finger over an edge does not move them), the four corners are where the
    /// lines cross, and because the real width of the lit area is known (the display reports it), four corner rays
    /// from the camera give the four corners in space - no depth sensor, no guessing where a bezel ends.
    ///
    /// Pure arithmetic on a byte buffer: the same code runs in the unit tests on rendered pictures and on the headset.
    /// Picture coordinates: x to the right, y DOWN, in pixels; pixel (i, j) covers [i, i+1) x [j, j+1).
    /// </summary>
    public sealed class ScreenFinder
    {
        public const int Red = 0, Green = 1, Blue = 2;
        public const int Step = 4;                 // the coarse grid the patch is first found on

        public struct Quad
        {
            public Vector2 tl, tr, br, bl;
            public float rms;                      // how straight the four edges are (pixels)
            public float inliers;                  // the worst edge: share of its points that lie on the fitted line
            public int cells;                      // size of the patch in coarse cells
        }

        byte[] _mask = new byte[0];
        int[] _label = new int[0], _queue = new int[0];
        float[] _px = new float[0], _py = new float[0];

        /// <summary>One colour clearly leading the other two, and not dark.</summary>
        public static bool IsColour(int colour, int r, int g, int b)
        {
            switch (colour)
            {
                case Red: return r >= 64 && r * 10 >= g * 15 && r * 10 >= b * 15;
                case Green: return g >= 64 && g * 10 >= r * 14 && g * 10 >= b * 14;
                default: return b >= 64 && b * 10 >= r * 15 && b * 10 >= g * 13;
            }
        }

        /// <summary>
        /// The largest patch of the colour, as a four-cornered shape. rgba: 4 bytes a pixel, top row first.
        /// False (with the reason) when there is none, when it runs out of the picture, or when its edges are not straight.
        /// </summary>
        public bool FindQuad(byte[] rgba, int w, int h, int colour, out Quad q, out string why)
        {
            q = default; why = "";
            int gw = w / Step, gh = h / Step, n = gw * gh;
            if (gw < 8 || gh < 8 || rgba == null || rgba.Length < w * h * 4) { why = "no picture"; return false; }
            if (_mask.Length < n) { _mask = new byte[n]; _label = new int[n]; _queue = new int[n]; }
            int half = Step / 2;
            for (int gy = 0; gy < gh; gy++)
            {
                int row = ((gy * Step + half) * w + half) * 4;
                for (int gx = 0; gx < gw; gx++)
                {
                    int i = row + gx * Step * 4;
                    _mask[gy * gw + gx] = IsColour(colour, rgba[i], rgba[i + 1], rgba[i + 2]) ? (byte)1 : (byte)0;
                }
            }
            Array.Clear(_label, 0, n);
            // the largest connected patch
            int best = 0, bx0 = 0, by0 = 0, bx1 = 0, by1 = 0, labels = 0;
            for (int s = 0; s < n; s++)
            {
                if (_mask[s] == 0 || _label[s] != 0) continue;
                labels++;
                int head = 0, tail = 0, count = 0, x0 = gw, y0 = gh, x1 = -1, y1 = -1;
                _queue[tail++] = s; _label[s] = labels;
                while (head < tail)
                {
                    int c = _queue[head++]; count++;
                    int cx = c % gw, cy = c / gw;
                    if (cx < x0) x0 = cx; if (cx > x1) x1 = cx; if (cy < y0) y0 = cy; if (cy > y1) y1 = cy;
                    if (cx > 0 && _mask[c - 1] != 0 && _label[c - 1] == 0) { _label[c - 1] = labels; _queue[tail++] = c - 1; }
                    if (cx < gw - 1 && _mask[c + 1] != 0 && _label[c + 1] == 0) { _label[c + 1] = labels; _queue[tail++] = c + 1; }
                    if (cy > 0 && _mask[c - gw] != 0 && _label[c - gw] == 0) { _label[c - gw] = labels; _queue[tail++] = c - gw; }
                    if (cy < gh - 1 && _mask[c + gw] != 0 && _label[c + gw] == 0) { _label[c + gw] = labels; _queue[tail++] = c + gw; }
                }
                if (count > best) { best = count; bx0 = x0; by0 = y0; bx1 = x1; by1 = y1; }
            }
            if (best < Math.Max(120, n / 400)) { why = best == 0 ? "none of that colour in the picture" : "only a small patch of that colour (" + best + " cells)"; return false; }
            if (bx0 <= 0 || by0 <= 0 || bx1 >= gw - 1 || by1 >= gh - 1) { why = "the patch runs out of the picture"; return false; }
            q.cells = best;

            // its four edges, pixel by pixel
            int px0 = Math.Max(0, bx0 * Step - 8), px1 = Math.Min(w - 1, bx1 * Step + Step + 8);
            int py0 = Math.Max(0, by0 * Step - 8), py1 = Math.Min(h - 1, by1 * Step + Step + 8);
            int bw = px1 - px0 + 1, bh = py1 - py0 + 1;
            int need = Math.Max(bw, bh) + 4;
            if (_px.Length < need) { _px = new float[need]; _py = new float[need]; }
            int ya = py0 + bh / 10, yb = py1 - bh / 10, xa = px0 + bw / 10, xb = px1 - bw / 10;
            float sum2 = 0; int sumN = 0; float worst = 1f;
            // left: x = a*y + b
            int m = 0;
            for (int y = ya; y <= yb; y++) { int x = FirstRun(rgba, w, colour, px0, px1, y, 1, true); if (x >= 0) { _px[m] = y + 0.5f; _py[m] = x; m++; } }
            if (!FitLine(_px, _py, m, out float la, out float lb, ref sum2, ref sumN, ref worst)) { why = "left edge not found"; return false; }
            m = 0;
            for (int y = ya; y <= yb; y++) { int x = FirstRun(rgba, w, colour, px1, px0, y, -1, true); if (x >= 0) { _px[m] = y + 0.5f; _py[m] = x + 1; m++; } }
            if (!FitLine(_px, _py, m, out float ra, out float rb, ref sum2, ref sumN, ref worst)) { why = "right edge not found"; return false; }
            // top: y = a*x + b
            m = 0;
            for (int x = xa; x <= xb; x++) { int y = FirstRun(rgba, w, colour, py0, py1, x, 1, false); if (y >= 0) { _px[m] = x + 0.5f; _py[m] = y; m++; } }
            if (!FitLine(_px, _py, m, out float ta, out float tb, ref sum2, ref sumN, ref worst)) { why = "top edge not found"; return false; }
            m = 0;
            for (int x = xa; x <= xb; x++) { int y = FirstRun(rgba, w, colour, py1, py0, x, -1, false); if (y >= 0) { _px[m] = x + 0.5f; _py[m] = y + 1; m++; } }
            if (!FitLine(_px, _py, m, out float ba, out float bb, ref sum2, ref sumN, ref worst)) { why = "bottom edge not found"; return false; }

            q.tl = Cross(la, lb, ta, tb); q.tr = Cross(ra, rb, ta, tb); q.br = Cross(ra, rb, ba, bb); q.bl = Cross(la, lb, ba, bb);
            q.rms = (float)Math.Sqrt(sum2 / Math.Max(1, sumN)); q.inliers = worst;
            if (worst < 0.55f) { why = "an edge is not a straight line (" + (int)(worst * 100) + " % of it fits)"; return false; }
            if (q.rms > 1.6f) { why = "the edges are ragged (" + q.rms.ToString("0.0") + " px)"; return false; }
            if (!(q.tr.X - q.tl.X > 24 && q.br.X - q.bl.X > 24 && q.bl.Y - q.tl.Y > 24 && q.br.Y - q.tr.Y > 24)) { why = "not a four-cornered shape"; return false; }
            if (q.tl.X < 1 || q.bl.X < 1 || q.tl.Y < 1 || q.tr.Y < 1 || q.tr.X > w - 1 || q.br.X > w - 1 || q.bl.Y > h - 1 || q.br.Y > h - 1) { why = "a corner is outside the picture"; return false; }
            return true;
        }

        /// <summary>Along a row (or a column): the first pixel that starts a run of four of the colour. -1 when there is none.</summary>
        static int FirstRun(byte[] rgba, int w, int colour, int from, int to, int fixedCoord, int dir, bool alongX)
        {
            int run = 0;
            for (int v = from; dir > 0 ? v <= to : v >= to; v += dir)
            {
                int i = alongX ? (fixedCoord * w + v) * 4 : (v * w + fixedCoord) * 4;
                if (IsColour(colour, rgba[i], rgba[i + 1], rgba[i + 2])) { if (++run == 4) return v - 3 * dir; }
                else run = 0;
            }
            return -1;
        }

        /// <summary>
        /// v = a*u + b through most of the points: pairs of points propose a line, the one most points lie on wins and
        /// is then fitted to exactly those points. (The ends of an edge row may belong to the neighbouring edge.)
        /// </summary>
        static bool FitLine(float[] u, float[] v, int n, out float a, out float b, ref float sum2, ref int sumN, ref float worst)
        {
            a = b = 0;
            if (n < 12) return false;
            int bestIn = -1; float bestA = 0, bestB = 0;
            uint seed = 2463534242u;
            for (int it = 0; it < 80; it++)
            {
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; int i = (int)(seed % (uint)n);
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; int j = (int)(seed % (uint)n);
                if (Math.Abs(u[i] - u[j]) < n * 0.2f) continue;
                float ca = (v[j] - v[i]) / (u[j] - u[i]), cb = v[i] - ca * u[i];
                int inl = 0;
                for (int k = 0; k < n; k++) if (Math.Abs(ca * u[k] + cb - v[k]) <= 1.5f) inl++;
                if (inl > bestIn) { bestIn = inl; bestA = ca; bestB = cb; }
            }
            if (bestIn < 8) return false;
            // least squares on the points of that line (twice: the second time with the better line)
            for (int pass = 0; pass < 2; pass++)
            {
                double su = 0, sv = 0, suu = 0, suv = 0; int c = 0;
                for (int k = 0; k < n; k++)
                {
                    if (Math.Abs(bestA * u[k] + bestB - v[k]) > 1.5f) continue;
                    su += u[k]; sv += v[k]; suu += (double)u[k] * u[k]; suv += (double)u[k] * v[k]; c++;
                }
                double den = c * suu - su * su;
                if (c < 8 || Math.Abs(den) < 1e-6) return false;
                bestA = (float)((c * suv - su * sv) / den); bestB = (float)((sv - bestA * su) / c);
            }
            int inliers = 0;
            for (int k = 0; k < n; k++)
            {
                float e = bestA * u[k] + bestB - v[k];
                if (Math.Abs(e) > 1.5f) continue;
                sum2 += e * e; sumN++; inliers++;
            }
            a = bestA; b = bestB;
            worst = Math.Min(worst, inliers / (float)n);
            return true;
        }

        /// <summary>Where the line x = a*y + b crosses the line y = c*x + d.</summary>
        static Vector2 Cross(float a, float b, float c, float d)
        {
            float x = (a * d + b) / (1f - a * c);
            return new Vector2(x, c * x + d);
        }

        /// <summary>The direction, in the camera's own frame (x right, y UP, z forward), a pixel looks along.</summary>
        public static Vector3 PixelRay(Vector2 p, float fx, float fy, float cx, float cy) =>
            Vector3.Normalize(new Vector3((p.X - cx) / fx, -(p.Y - cy) / fy, 1f));

        /// <summary>
        /// Four rays from one eye point through the corners of a flat four-cornered shape whose opposite sides are
        /// parallel, and its real width: that fixes the corners in space. Returns the corners, the height that follows
        /// (to compare with the height the display reports) and how far from a right angle its corner is.
        /// </summary>
        public static bool SolveRectangle(Vector3 eye, Vector3 rTl, Vector3 rTr, Vector3 rBr, Vector3 rBl, float widthM,
                                          out Vector3 tl, out Vector3 tr, out Vector3 br, out Vector3 bl, out float heightM, out float skewDeg)
        {
            tl = tr = br = bl = default; heightM = 0; skewDeg = 0;
            // tl - tr - bl + br = 0 with tl = 1 * rTl:   -dTr*rTr - dBl*rBl + dBr*rBr = -rTl
            Vector3 c0 = -rTr, c1 = -rBl, c2 = rBr, rhs = -rTl;
            float det = Vector3.Dot(c0, Vector3.Cross(c1, c2));
            if (Math.Abs(det) < 1e-9f) return false;
            float dTr = Vector3.Dot(rhs, Vector3.Cross(c1, c2)) / det;
            float dBl = Vector3.Dot(c0, Vector3.Cross(rhs, c2)) / det;
            float dBr = Vector3.Dot(c0, Vector3.Cross(c1, rhs)) / det;
            if (!(dTr > 0.05f && dBl > 0.05f && dBr > 0.05f) || dTr > 20f || dBl > 20f || dBr > 20f) return false;
            Vector3 a = rTl, b = rTr * dTr, c = rBr * dBr, d = rBl * dBl;
            float wNow = 0.5f * ((b - a).Length() + (c - d).Length());
            if (wNow < 1e-6f) return false;
            float s = widthM / wNow;
            tl = eye + a * s; tr = eye + b * s; br = eye + c * s; bl = eye + d * s;
            heightM = 0.5f * ((bl - tl).Length() + (br - tr).Length());
            Vector3 ex = Vector3.Normalize(tr - tl), ey = Vector3.Normalize(bl - tl);
            skewDeg = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vector3.Dot(ex, ey)))) * 180.0 / Math.PI) - 90f;
            return true;
        }
    }
}
