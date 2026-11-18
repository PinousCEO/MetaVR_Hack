using UnityEditor;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// The small picture of noise the painted places are made of (LivingPartner/Ambience, patterns 8 and 9): the
    /// clouds, the waves, the foam, the ridges and the rock are all looked up in it, a handful of times per pixel,
    /// instead of being worked out per pixel octave by octave. 256 x 256, it repeats seamlessly in both directions:
    ///   R   a landscape of soft hills (five octaves of gradient noise, the largest four hills across the picture)
    ///   G,B the slope of that landscape along u and along v (0.5 = level): light on clouds and rock, and the small
    ///       waves of the sea, come from it without a second look-up
    ///   A   another, finer landscape (billows, eight across): what frays the edges of clouds and foam, and the grain of rock
    /// Made once in the Editor and kept as an asset (Art/Textures/LPNoise2.asset); not compressed, with mip maps.
    /// </summary>
    public static class AmbienceNoise
    {
        const int N = 256, Version = 2;      // (a new version is a new file: LPNoise<version>.asset)
        const string Dir = "Assets/LivingPartner/Art/Textures", Name = "LPNoise2", Path = Dir + "/" + Name + ".asset";

        public static Texture2D Ensure()
        {
            var have = AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
            if (have != null && have.width == N && have.name == Name) return have;
            var tex = have != null ? have : new Texture2D(N, N, TextureFormat.RGBA32, true, true);
            tex.name = Name;
            tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Trilinear; tex.anisoLevel = 1;
            var h = new float[N * N]; var fine = new float[N * N];
            float lo = float.MaxValue, hi = float.MinValue, flo = float.MaxValue, fhi = float.MinValue;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N, a = 0f, amp = 1f, b = 0f, bamp = 1f;
                    for (int o = 0, per = 4; o < 5; o++, per *= 2) { a += amp * Gradient(u * per, v * per, per, 11 + o * 7); amp *= 0.5f; }
                    for (int o = 0, per = 8; o < 4; o++, per *= 2) { b += bamp * (1f - Mathf.Abs(Gradient(u * per, v * per, per, 71 + o * 5)) * 2.2f); bamp *= 0.5f; }
                    h[y * N + x] = a; fine[y * N + x] = b;
                    lo = Mathf.Min(lo, a); hi = Mathf.Max(hi, a); flo = Mathf.Min(flo, b); fhi = Mathf.Max(fhi, b);
                }
            float steep = 0f;
            var gx = new float[N * N]; var gy = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int xm = (x + N - 1) % N, xp = (x + 1) % N, ym = (y + N - 1) % N, yp = (y + 1) % N;
                    gx[y * N + x] = h[y * N + xp] - h[y * N + xm]; gy[y * N + x] = h[yp * N + x] - h[ym * N + x];
                    steep = Mathf.Max(steep, Mathf.Max(Mathf.Abs(gx[y * N + x]), Mathf.Abs(gy[y * N + x])));
                }
            var px = new Color32[N * N];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(B((h[i] - lo) / (hi - lo)), B(0.5f + 0.5f * gx[i] / steep), B(0.5f + 0.5f * gy[i] / steep), B((fine[i] - flo) / (fhi - flo)));
            tex.SetPixels32(px);
            tex.Apply(true, false);
            if (have == null)
            {
                if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/LivingPartner/Art", "Textures");
                AssetDatabase.CreateAsset(tex, Path);
            }
            else EditorUtility.SetDirty(tex);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AmbienceNoise] {Path} written: {N}x{N} RGBA32, {tex.mipmapCount} mip levels");
            return tex;
        }

        static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        /// <summary>Gradient noise (-0.5 .. 0.5 or so) that repeats after `period` cells.</summary>
        static float Gradient(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f), sy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            float n00 = Dot(x0, y0, fx, fy, period, seed), n10 = Dot(x0 + 1, y0, fx - 1f, fy, period, seed);
            float n01 = Dot(x0, y0 + 1, fx, fy - 1f, period, seed), n11 = Dot(x0 + 1, y0 + 1, fx - 1f, fy - 1f, period, seed);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, sx), Mathf.Lerp(n01, n11, sx), sy);
        }

        static float Dot(int ix, int iy, float dx, float dy, int period, int seed)
        {
            ix = ((ix % period) + period) % period; iy = ((iy % period) + period) % period;
            uint hsh = (uint)(ix * 374761393 + iy * 668265263 + seed * 1274126177);
            hsh = (hsh ^ (hsh >> 13)) * 1274126177u; hsh ^= hsh >> 16;
            float ang = (hsh & 0xFFFF) / 65536f * Mathf.PI * 2f;
            return Mathf.Cos(ang) * dx + Mathf.Sin(ang) * dy;
        }
    }
}
