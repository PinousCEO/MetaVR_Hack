using System;
using System.Collections.Generic;
using System.Numerics;

namespace LivingPartner.Shared
{
    /// <summary>
    /// The SHAPE of a sculpted thing (ItemDef.parts: boxes with rounded edges, cylinders, cones, balls, rods and rings,
    /// each with its place, its size, its turn and its colour) as plain triangle data: positions, normals, one colour
    /// per vertex, triangles.
    ///
    /// It is in Shared - compiled into Unity, into the .NET host and into the tests, so no Unity types - because the
    /// same shape is needed in two places: the headset draws it (PropMeshes.BuildParts fills a Mesh with exactly these
    /// arrays), and the host writes it out as a real 3D file when the partner carries a thing into the PC (GlbWriter).
    /// One tessellation, so the file is the thing the user saw on the desk.
    ///
    /// The measure of every thing: its longest side is 1, it stands on y = 0 with its middle over the origin, x is to
    /// the right, y up, z away from the viewer (its FRONT faces -z); triangles are wound the way Unity wants them
    /// (clockwise seen from outside in that left-handed space).
    /// </summary>
    public static class PartMesh
    {
        public sealed class Data
        {
            public float[] positions;   // x, y, z per vertex
            public float[] normals;     // x, y, z per vertex: smooth over every rounding, constant on every flat face
            public float[] colors;      // r, g, b per vertex, 0..1, as written in the part's "#RRGGBB" (every vertex of a part has the part's colour)
            public int[] indices;       // triangles
            public float height;        // how tall the whole is (its longest side being 1)
            public int Vertices => positions.Length / 3;
            public int Triangles => indices.Length / 3;
        }

        const float Flush = 0.012f;         // how much bigger the last part is made than the first (the whole thing being 1): two faces in one plane must not flicker
        static readonly Vector3 Wood = new Vector3(0.80f, 0.62f, 0.45f);

        /// <summary>"#RRGGBB" (or "#RGB") as three numbers 0..1; the fallback for anything else.</summary>
        public static Vector3 Hex(string hex, Vector3 fallback)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#') return fallback;
            int n = hex.Length - 1;
            if (n != 3 && n != 6 && n != 8) return fallback;
            int[] d = new int[n];
            for (int i = 0; i < n; i++)
            {
                char ch = hex[i + 1];
                int v = ch >= '0' && ch <= '9' ? ch - '0' : ch >= 'a' && ch <= 'f' ? ch - 'a' + 10 : ch >= 'A' && ch <= 'F' ? ch - 'A' + 10 : -1;
                if (v < 0) return fallback;
                d[i] = v;
            }
            if (n == 3) return new Vector3(d[0] * 17 / 255f, d[1] * 17 / 255f, d[2] * 17 / 255f);
            return new Vector3((d[0] * 16 + d[1]) / 255f, (d[2] * 16 + d[3]) / 255f, (d[4] * 16 + d[5]) / 255f);
        }

        /// <summary>
        /// The whole thing as one mesh. The parts come to the common measure from the host (WorldRules.ValidateParts);
        /// parts that did not (a file made by hand) are brought to it here all the same. Null when nothing could be built.
        /// </summary>
        public static Data Build(IList<ShapePart> parts)
        {
            if (parts == null) return null;
            int count = Math.Min(parts.Count, ShapePart.Max);
            // a thing of many parts is made of smaller ones: each can do with fewer triangles (a full desk of things has to stay light for a headset)
            bool fine = count <= 24;
            int seg = fine ? 16 : 12;
            var b = new B();
            for (int k = 0; k < count; k++)
            {
                var p = parts[k];
                if (p == null || !Number(p.x + p.y + p.z + p.sx + p.sy + p.sz + p.rx + p.ry + p.rz + p.round)) continue;
                Vector3 size = new Vector3(Math.Max(Math.Abs(p.sx), 0.004f), Math.Max(Math.Abs(p.sy), 0.004f), Math.Max(Math.Abs(p.sz), 0.004f));
                // Two parts are often given faces in the very same plane (the icing flush with the top of the cake): the
                // two colours would flicker through each other. So each part is a hair bigger than the one before it -
                // too little to see, enough for the later one (the smaller detail: they come big to small) to win.
                size += Vector3.One * (Flush * k / count);
                float round = Clamp(p.round, 0f, 1f), thin = Math.Min(size.X, size.Y) * 0.5f;
                Vector3 col = Hex(p.color, Wood);
                int from = b.v.Count;
                switch (p.shape)
                {
                    case "box": round = Math.Max(round, 0.1f); b.RoundBox(size, round * Math.Min(thin, size.Z * 0.5f), col, fine && round > 0.15f ? 2 : 1); break;
                    case "cylinder": round = Math.Max(round, 0.1f); b.RoundCyl(size, round * thin, col, seg, round > 0.5f ? (fine ? 4 : 3) : round > 0.15f ? 2 : 1); break;
                    case "capsule": b.RoundCyl(size, thin, col, seg, fine ? 4 : 3); break;
                    case "cone": b.Cone(size, round, col, seg, 1); break;
                    case "sphere": b.Egg(size, col, fine ? 10 : 7, seg); break;
                    case "torus": b.Ring(size, col, seg, fine ? 8 : 6); break;
                    default: continue;      // a shape this build does not know: the rest of the thing is still worth showing
                }
                Turn turn = new Turn(p.rx, p.ry, p.rz); Vector3 at = new Vector3(p.x, p.y, p.z);
                for (int i = from; i < b.v.Count; i++) { b.v[i] = at + turn.Apply(b.v[i]); b.n[i] = turn.Apply(b.n[i]); }
            }
            if (b.t.Count == 0) return null;

            Vector3 lo = b.v[0], hi = b.v[0];
            for (int i = 1; i < b.v.Count; i++) { lo = Vector3.Min(lo, b.v[i]); hi = Vector3.Max(hi, b.v[i]); }
            Vector3 span = hi - lo; float longest = Math.Max(span.X, Math.Max(span.Y, span.Z));
            if (longest < 1e-5f) return null;
            Vector3 shift = new Vector3((lo.X + hi.X) * 0.5f, lo.Y, (lo.Z + hi.Z) * 0.5f);
            if (Math.Abs(longest - 1f) > 0.02f || shift.Length() > 0.02f)
            {
                float s = 1f / longest;
                for (int i = 0; i < b.v.Count; i++) b.v[i] = (b.v[i] - shift) * s;
                span *= s;
            }

            int n = b.v.Count;
            var d = new Data { positions = new float[n * 3], normals = new float[n * 3], colors = new float[n * 3], indices = b.t.ToArray(), height = Math.Max(0.05f, span.Y) };
            for (int i = 0; i < n; i++)
            {
                Vector3 v = b.v[i], nn = b.n[i], c = b.c[i];
                d.positions[i * 3] = v.X; d.positions[i * 3 + 1] = v.Y; d.positions[i * 3 + 2] = v.Z;
                d.normals[i * 3] = nn.X; d.normals[i * 3 + 1] = nn.Y; d.normals[i * 3 + 2] = nn.Z;
                d.colors[i * 3] = c.X; d.colors[i * 3 + 1] = c.Y; d.colors[i * 3 + 2] = c.Z;
            }
            return d;
        }

        static bool Number(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        static Vector3 Norm(Vector3 v) { float l = v.Length(); return l > 1e-5f ? v / l : Vector3.Zero; }
        static Vector2 Norm(Vector2 v) { float l = v.Length(); return l > 1e-5f ? v / l : Vector2.Zero; }
        static float Get(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        static void Set(ref Vector3 v, int axis, float value) { if (axis == 0) v.X = value; else if (axis == 1) v.Y = value; else v.Z = value; }

        /// <summary>A part's turn: about z first, then x, then y - the order (and the sense) of Unity's Quaternion.Euler(rx, ry, rz).</summary>
        readonly struct Turn
        {
            readonly float m00, m01, m02, m10, m11, m12, m20, m21, m22;
            readonly bool none;

            public Turn(float rx, float ry, float rz)
            {
                none = rx == 0f && ry == 0f && rz == 0f;
                const double Deg = Math.PI / 180.0;
                double x = rx * Deg, y = ry * Deg, z = rz * Deg;
                double cx = Math.Cos(x), sx = Math.Sin(x), cy = Math.Cos(y), sy = Math.Sin(y), cz = Math.Cos(z), sz = Math.Sin(z);
                // R = Ry * Rx * Rz
                m00 = (float)(cy * cz + sy * sx * sz); m01 = (float)(-cy * sz + sy * sx * cz); m02 = (float)(sy * cx);
                m10 = (float)(cx * sz); m11 = (float)(cx * cz); m12 = (float)(-sx);
                m20 = (float)(-sy * cz + cy * sx * sz); m21 = (float)(sy * sz + cy * sx * cz); m22 = (float)(cy * cx);
            }

            public Vector3 Apply(Vector3 v) => none ? v : new Vector3(m00 * v.X + m01 * v.Y + m02 * v.Z, m10 * v.X + m11 * v.Y + m12 * v.Z, m20 * v.X + m21 * v.Y + m22 * v.Z);
        }

        // ---- the solids. Each is built round the origin, standing along y; Build turns and moves it. Normals are smooth
        // over every rounding and constant on every flat face, so a toon shader paints flat faces flat and an ink line
        // (pushed out along the normals) closes round the edges.
        sealed class B
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(), c = new List<Vector3>();
            public readonly List<int> t = new List<int>();
            readonly List<float> _su = new List<float>(), _sv = new List<float>();

            /// <summary>Where the rows of one face of a rounded box lie along one of its sides: the flat middle, and `steps` rows over each rounded edge.</summary>
            static void Rows(List<float> rows, float half, float r, int steps)
            {
                rows.Clear();
                float flat = half - r;
                // (spaced by equal angles of the rounding, up to the 45 degrees where the neighbouring face takes over)
                for (int m = steps; m >= 1; m--) rows.Add(-(flat + r * MathF.Tan(MathF.PI * 0.25f * m / steps)));
                if (flat > 1e-5f) { rows.Add(-flat); rows.Add(flat); } else rows.Add(0f);
                for (int m = 1; m <= steps; m++) rows.Add(flat + r * MathF.Tan(MathF.PI * 0.25f * m / steps));
            }

            /// <summary>A box whose edges and corners are rounded with the radius r (a small box with a ball rolled round it).</summary>
            public void RoundBox(Vector3 size, float r, Vector3 col, int steps)
            {
                Vector3 h = size * 0.5f;
                r = Clamp(r, 1e-4f, Math.Min(h.X, Math.Min(h.Y, h.Z)));
                Vector3 core = new Vector3(h.X - r, h.Y - r, h.Z - r);
                for (int w = 0; w < 3; w++)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        // the two sides of this face, in the order that makes it face outwards
                        int u = (w + 1) % 3, vv = (w + 2) % 3;
                        if (sign < 0) { int swap = u; u = vv; vv = swap; }
                        Rows(_su, Get(h, u), r, steps); Rows(_sv, Get(h, vv), r, steps);
                        int i0 = v.Count, nu = _su.Count, nv = _sv.Count;
                        for (int j = 0; j < nv; j++)
                            for (int i = 0; i < nu; i++)
                            {
                                Vector3 p = Vector3.Zero; Set(ref p, u, _su[i]); Set(ref p, vv, _sv[j]); Set(ref p, w, sign * Get(h, w));
                                Vector3 inner = new Vector3(Clamp(p.X, -core.X, core.X), Clamp(p.Y, -core.Y, core.Y), Clamp(p.Z, -core.Z, core.Z));
                                Vector3 d = Norm(p - inner);
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
                Vector2 d1 = Norm(at - before), d2 = Norm(after - at);
                Vector2 n1 = new Vector2(d1.Y, -d1.X), n2 = new Vector2(d2.Y, -d2.X);
                float turn = MathF.Acos(Clamp(Vector2.Dot(d1, d2), -1f, 1f)), tan = MathF.Tan(turn * 0.5f);
                if (tan > 1e-4f) r = Math.Min(r, maxCut / tan);
                if (r < 1e-5f || turn < 1e-3f) { Point(at, n1); Point(at, n2); return; }
                Vector2 centre = at - d1 * (r * tan) - n1 * r;
                for (int s = 0; s <= steps; s++)
                {
                    float a = turn * s / steps, cs = MathF.Cos(a), sn = MathF.Sin(a);
                    Vector2 nn = new Vector2(n1.X * cs - n1.Y * sn, n1.X * sn + n1.Y * cs);
                    Point(centre + nn * r, nn);
                }
            }

            /// <summary>
            /// Turns the outline round the y axis. squeeze: how much narrower (or wider) the solid is along z than along
            /// x - an oval instead of a circle. widen: added to the radius along z only (an oval ring). ring: the outline
            /// is a closed loop away from the axis, not a line from the axis and back to it.
            /// </summary>
            void Lathe(Vector3 col, int seg, float squeeze = 1f, float widen = 0f, bool ring = false)
            {
                int np = _pp.Count, i0 = v.Count;
                squeeze = Math.Max(squeeze, 1e-3f);
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * MathF.PI * 2f, cs = MathF.Cos(a), sn = MathF.Sin(a);
                    for (int i = 0; i < np; i++)
                    {
                        Vector2 p = _pp[i], nn = _pn[i];
                        v.Add(new Vector3(p.X * cs, p.Y, (p.X * squeeze + widen) * sn));
                        n.Add(Norm(new Vector3(nn.X * cs, nn.Y, nn.X * sn / squeeze))); c.Add(col);
                    }
                }
                for (int i = 0; i < np - 1; i++)
                {
                    // nothing to draw between a point and itself (a hard corner, two roundings that meet) or along the axis
                    if ((_pp[i] - _pp[i + 1]).LengthSquared() < 1e-12f) continue;
                    bool lowOnAxis = !ring && _pp[i].X < 1e-6f, highOnAxis = !ring && _pp[i + 1].X < 1e-6f;
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
            public void RoundCyl(Vector3 size, float r, Vector3 col, int seg, int steps)
            {
                float a = size.X * 0.5f, b = size.Y * 0.5f;
                r = Clamp(r, 0f, Math.Min(a, b));
                Vector2 p0 = new Vector2(0, -b), p1 = new Vector2(a, -b), p2 = new Vector2(a, b), p3 = new Vector2(0, b);
                Point(p0, new Vector2(0, -1));
                Corner(p0, p1, p2, r, Math.Min(a, b), steps);
                Corner(p1, p2, p3, r, Math.Min(a, b), steps);
                Point(p3, new Vector2(0, 1));
                Lathe(col, seg, size.Z / Math.Max(size.X, 1e-5f));
            }

            /// <summary>A cone along y, wide end down; blunt = the radius of its top as a part of the base's (0 = a point).</summary>
            public void Cone(Vector3 size, float blunt, Vector3 col, int seg, int steps)
            {
                float a = size.X * 0.5f, b = size.Y * 0.5f, top = a * Clamp(blunt, 0f, 0.95f);
                Vector2 p0 = new Vector2(0, -b), p1 = new Vector2(a, -b), p2 = new Vector2(top, b), p3 = new Vector2(0, b);
                float side = (p2 - p1).Length(), soft = 0.1f * Math.Min(a, b);
                Point(p0, new Vector2(0, -1));
                Corner(p0, p1, p2, soft, 0.3f * Math.Min(a, side), steps);
                if (top > 0.02f * a)
                {
                    Corner(p1, p2, p3, soft, 0.3f * Math.Min(top, side), steps);
                    Point(p3, new Vector2(0, 1));
                }
                else
                {
                    Vector2 d = Norm(p3 - p1);
                    Point(p3, new Vector2(d.Y, -d.X));      // the point keeps the slope's direction all the way round: shaded like the rest of the side
                }
                Lathe(col, seg, size.Z / Math.Max(size.X, 1e-5f));
            }

            /// <summary>A ball, an egg or a cushion: the three diameters.</summary>
            public void Egg(Vector3 size, Vector3 col, int lat, int seg)
            {
                float a = size.X * 0.5f, b = size.Y * 0.5f;
                for (int i = 0; i <= lat; i++)
                {
                    float u = i / (float)lat * MathF.PI, sn = MathF.Sin(u), cs = MathF.Cos(u);
                    Point(new Vector2(i == 0 || i == lat ? 0f : a * sn, -b * cs), Norm(new Vector2(sn / a, -cs / b)));
                }
                Lathe(col, seg, size.Z / Math.Max(size.X, 1e-5f));
            }

            /// <summary>A ring lying flat: outer diameters size.x and size.z, the ring itself size.y thick.</summary>
            public void Ring(Vector3 size, Vector3 col, int seg, int tube)
            {
                float r = Math.Min(size.Y * 0.5f, Math.Min(size.X, size.Z) * 0.25f), a = size.X * 0.5f - r, cz = size.Z * 0.5f - r;
                for (int i = 0; i <= tube; i++)
                {
                    float u = i / (float)tube * MathF.PI * 2f; Vector2 d = new Vector2(MathF.Cos(u), MathF.Sin(u));
                    Point(new Vector2(a, 0) + d * r, d);
                }
                Lathe(col, seg, 1f, cz - a, true);
            }
        }
    }
}
