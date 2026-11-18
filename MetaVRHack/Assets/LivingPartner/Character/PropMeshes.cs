using System.Collections.Generic;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// The shapes of the partner's things as mesh DATA (no objects are made here: a PropView in the prefab shows them).
    ///  - the built-in toy shapes, one per ItemKinds id: a handful of boxes, cylinders and balls in pastel colours - what
    ///    a thing looks like at once, before (or without) a generated model;
    ///  - a generated model, from the bytes of its .glb file (GlbReader).
    /// Every shape is made to the same measure: its longest side is 1, it stands on y = 0 with its middle over the
    /// origin, and its front faces -z (towards the user when it is not turned).
    /// </summary>
    public static class PropMeshes
    {
        sealed class B
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Color> c = new List<Color>(); public readonly List<int> t = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col)
            {
                int i = v.Count; Vector3 nn = Vector3.Cross(b - a, d - a).normalized;
                v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
                for (int k = 0; k < 4; k++) { n.Add(nn); c.Add(col); }
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            /// <summary>A box by its middle and its size.</summary>
            public void Box(Vector3 mid, Vector3 size, Color col)
            {
                Vector3 h = size * 0.5f;
                Vector3 p000 = mid + new Vector3(-h.x, -h.y, -h.z), p100 = mid + new Vector3(h.x, -h.y, -h.z), p010 = mid + new Vector3(-h.x, h.y, -h.z), p110 = mid + new Vector3(h.x, h.y, -h.z);
                Vector3 p001 = mid + new Vector3(-h.x, -h.y, h.z), p101 = mid + new Vector3(h.x, -h.y, h.z), p011 = mid + new Vector3(-h.x, h.y, h.z), p111 = mid + new Vector3(h.x, h.y, h.z);
                Quad(p000, p010, p110, p100, col); Quad(p101, p111, p011, p001, col);      // front (-z), back
                Quad(p001, p011, p010, p000, col); Quad(p100, p110, p111, p101, col);      // left, right
                Quad(p010, p011, p111, p110, col); Quad(p001, p000, p100, p101, col);      // top, bottom
            }

            /// <summary>An upright cylinder (or a cone: two radii) standing on the point "foot".</summary>
            public void Cyl(Vector3 foot, float r0, float r1, float height, Color col, int seg = 14)
            {
                int i0 = v.Count;
                float slope = (r0 - r1) / Mathf.Max(1e-4f, height);
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 nn = new Vector3(d.x, slope, d.z).normalized;
                    v.Add(foot + d * r0); n.Add(nn); c.Add(col);
                    v.Add(foot + d * r1 + Vector3.up * height); n.Add(nn); c.Add(col);
                }
                for (int s = 0; s < seg; s++) { int i = i0 + s * 2; t.Add(i); t.Add(i + 1); t.Add(i + 3); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
                // the lid and the base
                for (int cap = 0; cap < 2; cap++)
                {
                    float r = cap == 0 ? r1 : r0; if (r < 1e-4f) continue;
                    Vector3 mid = foot + Vector3.up * (cap == 0 ? height : 0f), nn = cap == 0 ? Vector3.up : Vector3.down;
                    int ic = v.Count; v.Add(mid); n.Add(nn); c.Add(col);
                    for (int s = 0; s <= seg; s++) { float a = s / (float)seg * Mathf.PI * 2f; v.Add(mid + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r); n.Add(nn); c.Add(col); }
                    for (int s = 0; s < seg; s++) { if (cap == 0) { t.Add(ic); t.Add(ic + s + 2); t.Add(ic + s + 1); } else { t.Add(ic); t.Add(ic + s + 1); t.Add(ic + s + 2); } }
                }
            }

            public void Ball(Vector3 mid, Vector3 radii, Color col, int lat = 8, int lon = 12)
            {
                int i0 = v.Count;
                for (int y = 0; y <= lat; y++)
                    for (int x = 0; x <= lon; x++)
                    {
                        float a = y / (float)lat * Mathf.PI, b = x / (float)lon * Mathf.PI * 2f;
                        Vector3 d = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b));
                        v.Add(mid + Vector3.Scale(d, radii)); n.Add(d); c.Add(col);
                    }
                for (int y = 0; y < lat; y++)
                    for (int x = 0; x < lon; x++)
                    {
                        int i = i0 + y * (lon + 1) + x;
                        t.Add(i); t.Add(i + 1); t.Add(i + lon + 2); t.Add(i); t.Add(i + lon + 2); t.Add(i + lon + 1);
                    }
            }

            // ---- the solids a sculpted thing is made of (BuildParts). Each is built round the origin, standing along y;
            // the caller turns and moves it. Normals are smooth over every rounding and constant on every flat face, so
            // the toon shader paints flat faces flat and the ink line (pushed out along the normals) closes round the edges.

            readonly List<float> _su = new List<float>(), _sv = new List<float>();

            /// <summary>Where the rows of one face of a rounded box lie along one of its sides: the flat middle, and `steps` rows over each rounded edge.</summary>
            static void Rows(List<float> rows, float half, float r, int steps)
            {
                rows.Clear();
                float flat = half - r;
                // (spaced by equal angles of the rounding, up to the 45 degrees where the neighbouring face takes over)
                for (int m = steps; m >= 1; m--) rows.Add(-(flat + r * Mathf.Tan(Mathf.PI * 0.25f * m / steps)));
                if (flat > 1e-5f) { rows.Add(-flat); rows.Add(flat); } else rows.Add(0f);
                for (int m = 1; m <= steps; m++) rows.Add(flat + r * Mathf.Tan(Mathf.PI * 0.25f * m / steps));
            }

            /// <summary>A box whose edges and corners are rounded with the radius r (a small box with a ball rolled round it).</summary>
            public void RoundBox(Vector3 size, float r, Color col, int steps)
            {
                Vector3 h = size * 0.5f;
                r = Mathf.Clamp(r, 1e-4f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)));
                Vector3 core = new Vector3(h.x - r, h.y - r, h.z - r);
                for (int w = 0; w < 3; w++)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        // the two sides of this face, in the order that makes it face outwards
                        int u = (w + 1) % 3, vv = (w + 2) % 3;
                        if (sign < 0) { int swap = u; u = vv; vv = swap; }
                        Rows(_su, h[u], r, steps); Rows(_sv, h[vv], r, steps);
                        int i0 = v.Count, nu = _su.Count, nv = _sv.Count;
                        for (int j = 0; j < nv; j++)
                            for (int i = 0; i < nu; i++)
                            {
                                Vector3 p = Vector3.zero; p[u] = _su[i]; p[vv] = _sv[j]; p[w] = sign * h[w];
                                Vector3 inner = new Vector3(Mathf.Clamp(p.x, -core.x, core.x), Mathf.Clamp(p.y, -core.y, core.y), Mathf.Clamp(p.z, -core.z, core.z));
                                Vector3 d = (p - inner).normalized;
                                v.Add(inner + d * r); n.Add(d); c.Add(col);
                            }
                        for (int j = 0; j < nv - 1; j++)
                            for (int i = 0; i < nu - 1; i++)
                            {
                                int a = i0 + j * nu + i;
                                t.Add(a); t.Add(a + 1); t.Add(a + nu + 1); t.Add(a); t.Add(a + nu + 1); t.Add(a + nu);
                            }
                    }
            }

            // the outline of a turned solid: from the bottom of its axis round the outside to the top (radius, height), with the outward direction at each point
            readonly List<Vector2> _pp = new List<Vector2>(), _pn = new List<Vector2>();

            void Point(Vector2 p, Vector2 normal) { _pp.Add(p); _pn.Add(normal); }

            /// <summary>The corner `at` of the outline, rounded with the radius r (never cutting more than maxCut off either side); a hard corner when r is 0.</summary>
            void Corner(Vector2 before, Vector2 at, Vector2 after, float r, float maxCut, int steps)
            {
                Vector2 d1 = (at - before).normalized, d2 = (after - at).normalized;
                Vector2 n1 = new Vector2(d1.y, -d1.x), n2 = new Vector2(d2.y, -d2.x);
                float turn = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d1, d2), -1f, 1f)), tan = Mathf.Tan(turn * 0.5f);
                if (tan > 1e-4f) r = Mathf.Min(r, maxCut / tan);
                if (r < 1e-5f || turn < 1e-3f) { Point(at, n1); Point(at, n2); return; }
                Vector2 centre = at - d1 * (r * tan) - n1 * r;
                for (int s = 0; s <= steps; s++)
                {
                    float a = turn * s / steps, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                    Vector2 nn = new Vector2(n1.x * cs - n1.y * sn, n1.x * sn + n1.y * cs);
                    Point(centre + nn * r, nn);
                }
            }

            /// <summary>
            /// Turns the outline round the y axis. squeeze: how much narrower (or wider) the solid is along z than along
            /// x - an oval instead of a circle. widen: added to the radius along z only (an oval ring). ring: the outline
            /// is a closed loop away from the axis, not a line from the axis and back to it.
            /// </summary>
            void Turn(Color col, int seg, float squeeze = 1f, float widen = 0f, bool ring = false)
            {
                int np = _pp.Count, i0 = v.Count;
                squeeze = Mathf.Max(squeeze, 1e-3f);
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                    for (int i = 0; i < np; i++)
                    {
                        Vector2 p = _pp[i], nn = _pn[i];
                        v.Add(new Vector3(p.x * cs, p.y, (p.x * squeeze + widen) * sn));
                        n.Add(new Vector3(nn.x * cs, nn.y, nn.x * sn / squeeze).normalized); c.Add(col);
                    }
                }
                for (int i = 0; i < np - 1; i++)
                {
                    // nothing to draw between a point and itself (a hard corner, two roundings that meet) or along the axis
                    if ((_pp[i] - _pp[i + 1]).sqrMagnitude < 1e-12f) continue;
                    bool lowOnAxis = !ring && _pp[i].x < 1e-6f, highOnAxis = !ring && _pp[i + 1].x < 1e-6f;
                    for (int s = 0; s < seg; s++)
                    {
                        int a = i0 + s * np + i, b = a + np;
                        if (!highOnAxis) { t.Add(a); t.Add(a + 1); t.Add(b + 1); }
                        if (!lowOnAxis) { t.Add(a); t.Add(b + 1); t.Add(b); }
                    }
                }
                _pp.Clear(); _pn.Clear();
            }

            /// <summary>A cylinder along y whose two rims are rounded with the radius r; with r as large as it goes, a rod with round ends (or a pill).</summary>
            public void RoundCyl(Vector3 size, float r, Color col, int seg, int steps)
            {
                float a = size.x * 0.5f, b = size.y * 0.5f;
                r = Mathf.Clamp(r, 0f, Mathf.Min(a, b));
                Vector2 p0 = new Vector2(0, -b), p1 = new Vector2(a, -b), p2 = new Vector2(a, b), p3 = new Vector2(0, b);
                Point(p0, Vector2.down);
                Corner(p0, p1, p2, r, Mathf.Min(a, b), steps);
                Corner(p1, p2, p3, r, Mathf.Min(a, b), steps);
                Point(p3, Vector2.up);
                Turn(col, seg, size.z / Mathf.Max(size.x, 1e-5f));
            }

            /// <summary>A cone along y, wide end down; blunt = the radius of its top as a part of the base's (0 = a point).</summary>
            public void Cone(Vector3 size, float blunt, Color col, int seg, int steps)
            {
                float a = size.x * 0.5f, b = size.y * 0.5f, top = a * Mathf.Clamp(blunt, 0f, 0.95f);
                Vector2 p0 = new Vector2(0, -b), p1 = new Vector2(a, -b), p2 = new Vector2(top, b), p3 = new Vector2(0, b);
                float side = (p2 - p1).magnitude, soft = 0.1f * Mathf.Min(a, b);
                Point(p0, Vector2.down);
                Corner(p0, p1, p2, soft, 0.3f * Mathf.Min(a, side), steps);
                if (top > 0.02f * a)
                {
                    Corner(p1, p2, p3, soft, 0.3f * Mathf.Min(top, side), steps);
                    Point(p3, Vector2.up);
                }
                else
                {
                    Vector2 d = (p3 - p1).normalized;
                    Point(p3, new Vector2(d.y, -d.x));      // the point keeps the slope's direction all the way round: shaded like the rest of the side
                }
                Turn(col, seg, size.z / Mathf.Max(size.x, 1e-5f));
            }

            /// <summary>A ball, an egg or a cushion: the three diameters.</summary>
            public void Egg(Vector3 size, Color col, int lat, int seg)
            {
                float a = size.x * 0.5f, b = size.y * 0.5f;
                for (int i = 0; i <= lat; i++)
                {
                    float u = i / (float)lat * Mathf.PI, sn = Mathf.Sin(u), cs = Mathf.Cos(u);
                    Point(new Vector2(i == 0 || i == lat ? 0f : a * sn, -b * cs), new Vector2(sn / a, -cs / b).normalized);
                }
                Turn(col, seg, size.z / Mathf.Max(size.x, 1e-5f));
            }

            /// <summary>A ring lying flat: outer diameters size.x and size.z, the ring itself size.y thick.</summary>
            public void Ring(Vector3 size, Color col, int seg, int tube)
            {
                float r = Mathf.Min(size.y * 0.5f, Mathf.Min(size.x, size.z) * 0.25f), a = size.x * 0.5f - r, cz = size.z * 0.5f - r;
                for (int i = 0; i <= tube; i++)
                {
                    float u = i / (float)tube * Mathf.PI * 2f; Vector2 d = new Vector2(Mathf.Cos(u), Mathf.Sin(u));
                    Point(new Vector2(a, 0) + d * r, d);
                }
                Turn(col, seg, 1f, cz - a, true);
            }
        }

        static Color Hex(string hex, Color fallback) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
        static Color Dark(Color c, float k = 0.78f) => new Color(c.r * k, c.g * k, c.b * k, 1f);
        static Color Light(Color c, float k = 0.35f) => Color.Lerp(c, Color.white, k);
        static readonly Color Cream = new Color(1f, 0.97f, 0.90f), Wood = new Color(0.80f, 0.62f, 0.45f), Ink = new Color(0.29f, 0.19f, 0.16f);

        /// <summary>How tall the built-in shape of a kind is (its longest side being 1), and how high its seat or top is.</summary>
        public static void Measures(string kind, out float height, out float seat)
        {
            switch (kind)
            {
                case "desk": height = 0.63f; seat = 0.63f; break;
                case "chair": height = 1.0f; seat = 0.50f; break;
                case "bed": height = 0.42f; seat = 0.25f; break;
                case "sofa": height = 0.56f; seat = 0.24f; break;
                case "book": height = 0.16f; seat = 0.16f; break;
                case "ball": height = 1.0f; seat = 1.0f; break;
                case "record": height = 0.26f; seat = 0.20f; break;
                case "cup": height = 0.9f; seat = 0.9f; break;
                case "food": height = 0.62f; seat = 0.5f; break;
                case "box": height = 0.72f; seat = 0.72f; break;
                default: height = 1.0f; seat = 0.5f; break;
            }
        }

        /// <summary>The built-in shape of a kind, in the colour given ("" = the kind's own).</summary>
        public static Mesh Build(string kind, string colour)
        {
            var k = ItemKinds.Find(kind);
            Color col = Hex(string.IsNullOrEmpty(colour) ? k.color : colour, Hex(k.color, Wood));
            var b = new B();
            switch (k.id)
            {
                case "desk":
                    b.Box(new Vector3(0, 0.595f, 0), new Vector3(1.0f, 0.07f, 0.56f), col);
                    foreach (float x in new[] { -0.44f, 0.44f }) foreach (float z in new[] { -0.22f, 0.22f }) b.Box(new Vector3(x, 0.28f, z), new Vector3(0.07f, 0.56f, 0.07f), Dark(col));
                    b.Box(new Vector3(0.27f, 0.46f, 0.02f), new Vector3(0.30f, 0.18f, 0.44f), Light(col, 0.2f));
                    b.Ball(new Vector3(0.27f, 0.46f, -0.215f), Vector3.one * 0.03f, Ink, 5, 8);
                    break;
                case "chair":
                    b.Box(new Vector3(0, 0.46f, 0), new Vector3(0.62f, 0.08f, 0.62f), col);
                    foreach (float x in new[] { -0.26f, 0.26f }) foreach (float z in new[] { -0.26f, 0.26f }) b.Box(new Vector3(x, 0.21f, z), new Vector3(0.08f, 0.42f, 0.08f), Dark(col));
                    b.Box(new Vector3(0, 0.76f, 0.27f), new Vector3(0.62f, 0.48f, 0.08f), Light(col, 0.15f));       // the back, away from the user
                    break;
                case "bed":
                    b.Box(new Vector3(0, 0.07f, 0), new Vector3(1.0f, 0.14f, 0.50f), Dark(Wood, 0.9f));
                    b.Box(new Vector3(0.02f, 0.19f, 0), new Vector3(0.94f, 0.10f, 0.46f), Cream);
                    b.Box(new Vector3(0.20f, 0.225f, 0), new Vector3(0.56f, 0.07f, 0.48f), col);                    // the blanket
                    b.Ball(new Vector3(-0.33f, 0.27f, 0), new Vector3(0.11f, 0.05f, 0.17f), Color.white, 6, 10);     // the pillow
                    b.Box(new Vector3(-0.47f, 0.21f, 0), new Vector3(0.06f, 0.42f, 0.50f), Wood);
                    break;
                case "sofa":
                    b.Box(new Vector3(0, 0.12f, 0), new Vector3(1.0f, 0.24f, 0.46f), col);
                    b.Box(new Vector3(0, 0.39f, 0.17f), new Vector3(1.0f, 0.34f, 0.13f), Light(col, 0.12f));
                    foreach (float x in new[] { -0.44f, 0.44f }) b.Box(new Vector3(x, 0.24f, -0.02f), new Vector3(0.12f, 0.24f, 0.42f), Dark(col, 0.88f));
                    b.Ball(new Vector3(0.22f, 0.29f, 0.02f), new Vector3(0.13f, 0.07f, 0.11f), Light(col, 0.5f), 6, 10);
                    break;
                case "book":
                    b.Box(new Vector3(0, 0.08f, 0), new Vector3(0.72f, 0.16f, 1.0f), col);
                    b.Box(new Vector3(0.025f, 0.08f, 0), new Vector3(0.70f, 0.11f, 0.94f), Cream);
                    b.Box(new Vector3(-0.05f, 0.163f, -0.12f), new Vector3(0.36f, 0.006f, 0.26f), Light(col, 0.55f));
                    break;
                case "ball":
                    b.Ball(new Vector3(0, 0.5f, 0), Vector3.one * 0.5f, col, 10, 14);
                    b.Cyl(new Vector3(0, 0.44f, 0), 0.502f, 0.502f, 0.12f, Cream, 18);
                    break;
                case "lamp":
                    b.Cyl(Vector3.zero, 0.22f, 0.20f, 0.06f, Dark(Wood), 14);
                    b.Cyl(new Vector3(0, 0.06f, 0), 0.035f, 0.035f, 0.62f, Wood, 8);
                    b.Cyl(new Vector3(0, 0.66f, 0), 0.32f, 0.17f, 0.34f, col, 16);
                    b.Ball(new Vector3(0, 0.67f, 0), Vector3.one * 0.10f, new Color(1f, 0.98f, 0.8f), 6, 8);
                    break;
                case "plant":
                    b.Cyl(Vector3.zero, 0.18f, 0.24f, 0.30f, new Color(0.86f, 0.55f, 0.40f), 14);
                    b.Ball(new Vector3(0, 0.56f, 0), new Vector3(0.30f, 0.30f, 0.30f), col, 7, 10);
                    b.Ball(new Vector3(0.16f, 0.76f, 0.04f), Vector3.one * 0.20f, Light(col, 0.2f), 6, 9);
                    b.Ball(new Vector3(-0.14f, 0.80f, -0.03f), Vector3.one * 0.19f, Dark(col, 0.9f), 6, 9);
                    b.Ball(new Vector3(0.02f, 0.90f, -0.1f), Vector3.one * 0.07f, new Color(1f, 0.72f, 0.80f), 5, 8);
                    break;
                case "cup":
                    b.Cyl(Vector3.zero, 0.34f, 0.42f, 0.9f, col, 16);
                    b.Cyl(new Vector3(0, 0.80f, 0), 0.36f, 0.36f, 0.06f, new Color(0.55f, 0.36f, 0.26f), 16);
                    b.Box(new Vector3(0.50f, 0.48f, 0), new Vector3(0.14f, 0.42f, 0.10f), col);
                    break;
                case "record":
                    b.Box(new Vector3(0, 0.09f, 0), new Vector3(1.0f, 0.18f, 0.80f), col);
                    b.Cyl(new Vector3(-0.12f, 0.18f, 0), 0.33f, 0.33f, 0.035f, Ink, 22);
                    b.Cyl(new Vector3(-0.12f, 0.215f, 0), 0.11f, 0.11f, 0.008f, new Color(1f, 0.82f, 0.36f), 14);
                    b.Box(new Vector3(0.34f, 0.23f, 0.02f), new Vector3(0.05f, 0.05f, 0.46f), Cream);                  // the arm
                    b.Cyl(new Vector3(0.34f, 0.18f, 0.26f), 0.06f, 0.06f, 0.07f, Dark(Cream, 0.85f), 10);
                    break;
                case "speaker":
                    b.Box(new Vector3(0, 0.5f, 0), new Vector3(0.62f, 1.0f, 0.50f), col);
                    b.Ball(new Vector3(0, 0.33f, -0.25f), new Vector3(0.21f, 0.21f, 0.05f), Ink, 6, 14);
                    b.Ball(new Vector3(0, 0.74f, -0.25f), new Vector3(0.12f, 0.12f, 0.04f), Ink, 6, 12);
                    break;
                case "food":
                    b.Cyl(Vector3.zero, 0.5f, 0.5f, 0.36f, col, 18);
                    b.Cyl(new Vector3(0, 0.36f, 0), 0.5f, 0.46f, 0.10f, Cream, 18);
                    b.Ball(new Vector3(0, 0.54f, 0), Vector3.one * 0.09f, new Color(0.93f, 0.25f, 0.30f), 6, 8);
                    break;
                case "toy":
                    b.Ball(new Vector3(0, 0.30f, 0), new Vector3(0.30f, 0.30f, 0.26f), col, 8, 12);
                    b.Ball(new Vector3(0, 0.72f, 0), Vector3.one * 0.26f, Light(col, 0.15f), 8, 12);
                    foreach (float x in new[] { -0.19f, 0.19f }) b.Ball(new Vector3(x, 0.93f, 0), Vector3.one * 0.09f, col, 5, 8);
                    foreach (float x in new[] { -0.09f, 0.09f }) b.Ball(new Vector3(x, 0.75f, -0.235f), Vector3.one * 0.035f, Ink, 5, 6);
                    break;
                default:
                    b.Box(new Vector3(0, 0.33f, 0), new Vector3(1.0f, 0.66f, 0.80f), col);
                    b.Box(new Vector3(0, 0.69f, 0), new Vector3(1.04f, 0.07f, 0.84f), Dark(col, 0.88f));
                    b.Box(new Vector3(0, 0.40f, -0.401f), new Vector3(0.16f, 0.16f, 0.01f), Cream);
                    break;
            }
            var m = new Mesh { name = "Prop " + k.id };
            m.SetVertices(b.v); m.SetNormals(b.n); m.SetColors(b.c); m.SetTriangles(b.t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// A thing sculpted out of simple parts (ItemDef.parts: boxes with rounded edges, cylinders, cones, balls, rods
        /// and rings, each with its place, its size, its turn and its colour) as ONE mesh with vertex colours, in the
        /// look of the built-in shapes. The parts come to the common measure from the host (WorldRules.ValidateParts);
        /// the solids built here are the ones it measured. height: how tall the whole is. Null when nothing could be built.
        /// </summary>
        public static Mesh BuildParts(IList<ShapePart> parts, out float height)
        {
            // (C1, 2026-11-18) The tessellation itself lives in Shared/PartMesh.cs now - the same code builds the mesh
            // here and the 3D file the host writes when the partner carries a thing into the PC (GlbWriter), so the
            // file is the thing that stood on the desk. Compared in the Editor before the switch: the same vertices
            // (to 2e-7), normals, colours and triangles as BuildPartsHere below, which is kept for that comparison.
            height = 1f;
            var data = PartMesh.Build(parts);
            if (data == null) return null;
            height = data.height;
            int n = data.Vertices;
            var v = new Vector3[n]; var nn = new Vector3[n]; var c = new Color[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(data.positions[i * 3], data.positions[i * 3 + 1], data.positions[i * 3 + 2]);
                nn[i] = new Vector3(data.normals[i * 3], data.normals[i * 3 + 1], data.normals[i * 3 + 2]);
                c[i] = new Color(data.colors[i * 3], data.colors[i * 3 + 1], data.colors[i * 3 + 2], 1f);
            }
            var m = new Mesh { name = "Prop parts" };
            m.vertices = v; m.normals = nn; m.colors = c; m.triangles = data.indices;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The same shape built with Unity's own vector types (what BuildParts was until 2026-11-18): only for comparing with Shared/PartMesh.</summary>
        public static Mesh BuildPartsHere(IList<ShapePart> parts, out float height)
        {
            height = 1f;
            if (parts == null) return null;
            int count = Mathf.Min(parts.Count, ShapePart.Max);
            // a thing of many parts is made of smaller ones: each can do with fewer triangles (a full desk of things has to stay light for a headset)
            bool fine = count <= 24;
            int seg = fine ? 16 : 12;
            var b = new B();
            for (int k = 0; k < count; k++)
            {
                var p = parts[k];
                if (p == null || !Number(p.x + p.y + p.z + p.sx + p.sy + p.sz + p.rx + p.ry + p.rz + p.round)) continue;
                Vector3 size = new Vector3(Mathf.Max(Mathf.Abs(p.sx), 0.004f), Mathf.Max(Mathf.Abs(p.sy), 0.004f), Mathf.Max(Mathf.Abs(p.sz), 0.004f));
                // Two parts are often given faces in the very same plane (the icing flush with the top of the cake): the
                // two colours would flicker through each other. So each part is a hair bigger than the one before it -
                // too little to see, enough for the later one (the smaller detail: they come big to small) to win.
                size += Vector3.one * (Flush * k / count);
                float round = Mathf.Clamp01(p.round), thin = Mathf.Min(size.x, size.y) * 0.5f;
                Color col = Hex(p.color, Wood);
                int from = b.v.Count;
                switch (p.shape)
                {
                    case "box": round = Mathf.Max(round, 0.1f); b.RoundBox(size, round * Mathf.Min(thin, size.z * 0.5f), col, fine && round > 0.15f ? 2 : 1); break;
                    case "cylinder": round = Mathf.Max(round, 0.1f); b.RoundCyl(size, round * thin, col, seg, round > 0.5f ? (fine ? 4 : 3) : round > 0.15f ? 2 : 1); break;
                    case "capsule": b.RoundCyl(size, thin, col, seg, fine ? 4 : 3); break;
                    case "cone": b.Cone(size, round, col, seg, 1); break;
                    case "sphere": b.Egg(size, col, fine ? 10 : 7, seg); break;
                    case "torus": b.Ring(size, col, seg, fine ? 8 : 6); break;
                    default: continue;      // a shape this build does not know: the rest of the thing is still worth showing
                }
                Quaternion turn = Quaternion.Euler(p.rx, p.ry, p.rz); Vector3 at = new Vector3(p.x, p.y, p.z);
                for (int i = from; i < b.v.Count; i++) { b.v[i] = at + turn * b.v[i]; b.n[i] = turn * b.n[i]; }
            }
            if (b.t.Count == 0) return null;

            // Normally nothing is left to do here. Parts that did not come through the host's check (a file made by hand)
            // are brought to the measure all the same: everything that places a thing on the desk relies on it.
            Vector3 lo = b.v[0], hi = b.v[0];
            for (int i = 1; i < b.v.Count; i++) { lo = Vector3.Min(lo, b.v[i]); hi = Vector3.Max(hi, b.v[i]); }
            Vector3 span = hi - lo; float longest = Mathf.Max(span.x, Mathf.Max(span.y, span.z));
            if (longest < 1e-5f) return null;
            Vector3 shift = new Vector3((lo.x + hi.x) * 0.5f, lo.y, (lo.z + hi.z) * 0.5f);
            if (Mathf.Abs(longest - 1f) > 0.02f || shift.magnitude > 0.02f)
            {
                float s = 1f / longest;
                for (int i = 0; i < b.v.Count; i++) b.v[i] = (b.v[i] - shift) * s;
                span *= s;
            }
            height = Mathf.Max(0.05f, span.y);

            var m = new Mesh { name = "Prop parts" };
            m.SetVertices(b.v); m.SetNormals(b.n); m.SetColors(b.c); m.SetTriangles(b.t, 0);
            m.RecalculateBounds();
            return m;
        }

        static bool Number(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        const float Flush = 0.012f;         // how much bigger the last part of a sculpted thing is made than the first (the whole thing being 1): half a millimetre a side on a thing of 10 cm

        // the key of each list of parts that has been asked for (a snapshot's lists are never changed once they have arrived)
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IList<ShapePart>, string> PartKeys = new System.Runtime.CompilerServices.ConditionalWeakTable<IList<ShapePart>, string>();

        /// <summary>
        /// The name under which a thing's sculpted shape is kept: its id and a fingerprint of its parts, so that a thing
        /// sculpted anew is built anew. Asked every frame, so it is worked out once for each list.
        /// </summary>
        public static string PartsKey(string id, IList<ShapePart> parts)
        {
            if (parts == null) return "parts:" + id + ":0";
            if (PartKeys.TryGetValue(parts, out string key)) return key;
            unchecked
            {
                uint h = 2166136261;
                void Mix(int x) { for (int s = 0; s < 32; s += 8) h = (h ^ (uint)((x >> s) & 255)) * 16777619; }
                void Word(string s) { if (s != null) foreach (char ch in s) Mix(ch); Mix(-1); }
                void Num(float f) => Mix(Mathf.RoundToInt(Mathf.Clamp(f, -1e5f, 1e5f) * 1000f));      // (the host rounds every number to a thousandth)
                foreach (var p in parts)
                {
                    if (p == null) { Mix(0); continue; }
                    Word(p.shape); Word(p.color);
                    Num(p.x); Num(p.y); Num(p.z); Num(p.sx); Num(p.sy); Num(p.sz); Num(p.rx); Num(p.ry); Num(p.rz); Num(p.round);
                }
                key = "parts:" + id + ":" + h.ToString("x8");
            }
            PartKeys.Add(parts, key);
            return key;
        }

        /// <summary>
        /// A generated model from the bytes of its .glb file: one mesh to the common measure, its texture, and how
        /// tall it is. Null (with the reason) when the file cannot be read.
        /// </summary>
        public static Mesh FromGlb(byte[] glb, out Texture2D texture, out float height, out string error)
        {
            texture = null; height = 1f;
            var model = GlbReader.Read(glb, out error);
            if (model == null) return null;
            GlbReader.Normalize(model, 1f);
            int n = model.positions.Length / 3;
            var v = new Vector3[n]; var nn = new Vector3[n]; var uv = new Vector2[n]; var c = new Color[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(model.positions[i * 3], model.positions[i * 3 + 1], model.positions[i * 3 + 2]);
                nn[i] = new Vector3(model.normals[i * 3], model.normals[i * 3 + 1], model.normals[i * 3 + 2]);
                uv[i] = new Vector2(model.uvs[i * 2], model.uvs[i * 2 + 1]);
                c[i] = new Color(model.colors[i * 4], model.colors[i * 4 + 1], model.colors[i * 4 + 2], 1f);
            }
            var m = new Mesh { name = "Prop model" };
            if (n > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; m.normals = nn; m.uv = uv; m.colors = c; m.triangles = model.indices;
            m.RecalculateBounds();
            height = Mathf.Max(0.05f, model.max.Y - model.min.Y);
            if (model.texture != null && model.texture.Length > 0)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Prop texture", wrapMode = TextureWrapMode.Repeat };
                if (tex.LoadImage(model.texture, true)) texture = tex; else Object.Destroy(tex);
            }
            return m;
        }
    }
}
