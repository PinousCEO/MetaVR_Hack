using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// A picture of a sculpted thing, drawn on the PC with no graphics card and no Unity: the preview that is saved next to
/// its 3D file, the thumbnail in the partner's hands when it carries the file into the PC, and the thing's icon in the
/// bag. A small triangle filler with a depth buffer, in the look of the headset's toy shader: two flat tones per
/// colour and a dark-brown ink line round the outline and along every fold.
/// Pure: mesh in, pixels out (PNG encoding is the caller's).
/// </summary>
public static class ModelRender
{
    public const uint Ink = 0xFF4A2F27, Cream = 0xFFFFF8EA;      // ARGB

    /// <summary>
    /// size x size pixels, 4 bytes each in the order blue, green, red, alpha (what a 32-bit Windows bitmap holds),
    /// the top row first. The thing is seen from the front, a little from its right and from above, and fills the
    /// picture. background: ARGB (0 = see-through).
    /// </summary>
    public static byte[] Render(PartMesh.Data mesh, int size, uint background = Cream, float yawDeg = -28f, float pitchDeg = 20f)
    {
        size = Math.Clamp(size, 16, 2048);
        const int SS = 2;                       // drawn at twice the size, then averaged: soft edges
        int W = size * SS, n = mesh.positions.Length / 3;
        // the view: the thing turned about y (to show a side), then tipped towards the viewer (to show its top)
        double yaw = yawDeg * Math.PI / 180, pitch = -pitchDeg * Math.PI / 180;
        float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw), cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);
        void View(float x, float y, float z, out float ox, out float oy, out float oz)
        {
            float x1 = cy * x + sy * z, z1 = -sy * x + cy * z;       // about y
            ox = x1; oy = cp * y - sp * z1; oz = sp * y + cp * z1;    // about x
        }
        var px = new float[n]; var py = new float[n]; var pz = new float[n]; var nx = new float[n]; var ny = new float[n]; var nz = new float[n];
        float lox = float.MaxValue, loy = lox, hix = float.MinValue, hiy = hix;
        for (int i = 0; i < n; i++)
        {
            View(mesh.positions[i * 3], mesh.positions[i * 3 + 1], mesh.positions[i * 3 + 2], out px[i], out py[i], out pz[i]);
            View(mesh.normals[i * 3], mesh.normals[i * 3 + 1], mesh.normals[i * 3 + 2], out nx[i], out ny[i], out nz[i]);
            lox = Math.Min(lox, px[i]); hix = Math.Max(hix, px[i]); loy = Math.Min(loy, py[i]); hiy = Math.Max(hiy, py[i]);
        }
        float span = Math.Max(hix - lox, hiy - loy);
        if (!(span > 1e-6f)) span = 1f;
        float scale = W * 0.84f / span, mx = (lox + hix) * 0.5f, my = (loy + hiy) * 0.5f;
        for (int i = 0; i < n; i++) { px[i] = W * 0.5f + (px[i] - mx) * scale; py[i] = W * 0.5f - (py[i] - my) * scale; }

        var depth = new float[W * W]; var colour = new uint[W * W]; var covered = new bool[W * W];
        Array.Fill(depth, float.MaxValue);
        // the light: from above, a little from the left and from the viewer's side
        float lx = -0.38f, ly = 0.74f, lz = -0.55f; { float l = MathF.Sqrt(lx * lx + ly * ly + lz * lz); lx /= l; ly /= l; lz /= l; }
        for (int t = 0; t + 2 < mesh.indices.Length; t += 3)
        {
            int a = mesh.indices[t], b = mesh.indices[t + 1], c = mesh.indices[t + 2];
            float ax = px[a], ay = py[a], bx = px[b], by = py[b], cx = px[c], cyy = py[c];
            float area = (bx - ax) * (cyy - ay) - (cx - ax) * (by - ay);
            if (Math.Abs(area) < 1e-6f) continue;
            int x0 = Math.Max(0, (int)MathF.Floor(Math.Min(ax, Math.Min(bx, cx)))), x1 = Math.Min(W - 1, (int)MathF.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            int y0 = Math.Max(0, (int)MathF.Floor(Math.Min(ay, Math.Min(by, cyy)))), y1 = Math.Min(W - 1, (int)MathF.Ceiling(Math.Max(ay, Math.Max(by, cyy))));
            float cr = mesh.colors[a * 3], cg = mesh.colors[a * 3 + 1], cb = mesh.colors[a * 3 + 2];
            float inv = 1f / area;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float qx = x + 0.5f, qy = y + 0.5f;
                    float w0 = ((bx - qx) * (cyy - qy) - (cx - qx) * (by - qy)) * inv, w1 = ((cx - qx) * (ay - qy) - (ax - qx) * (cyy - qy)) * inv, w2 = 1f - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float z = w0 * pz[a] + w1 * pz[b] + w2 * pz[c];
                    int at = y * W + x;
                    if (z >= depth[at]) continue;
                    depth[at] = z; covered[at] = true;
                    float ux = w0 * nx[a] + w1 * nx[b] + w2 * nx[c], uy = w0 * ny[a] + w1 * ny[b] + w2 * ny[c], uz = w0 * nz[a] + w1 * nz[b] + w2 * nz[c];
                    float len = MathF.Sqrt(ux * ux + uy * uy + uz * uz);
                    float ndl = len > 1e-6f ? (ux * lx + uy * ly + uz * lz) / len : 1f;
                    // two flat tones (and a third for what is turned right away from the light), a little paler than written - as the headset paints it
                    float k = ndl > 0.18f ? 1f : ndl > -0.45f ? 0.84f : 0.72f, pale = ndl > 0.18f ? 0.14f : 0.06f;
                    int R = (int)(255f * Math.Clamp((cr + (1f - cr) * pale) * k, 0f, 1f)), G = (int)(255f * Math.Clamp((cg + (1f - cg) * pale) * k, 0f, 1f)), B = (int)(255f * Math.Clamp((cb + (1f - cb) * pale) * k, 0f, 1f));
                    colour[at] = 0xFF000000u | (uint)R << 16 | (uint)G << 8 | (uint)B;
                }
        }

        // the ink line: round the outline (outside it, thick) and along every fold where the depth jumps (inside, thin)
        int outer = Math.Max(2, W / 170), inner = Math.Max(1, W / 340);
        const float jump = 0.03f;               // (of the thing's longest side)
        var ink = new bool[W * W];
        for (int y = 0; y < W; y++)
            for (int x = 0; x < W; x++)
            {
                int at = y * W + x;
                if (covered[at])
                {
                    bool fold = false;
                    for (int k = 0; k < 4 && !fold; k++)
                    {
                        int qx = x + (k == 0 ? inner : k == 1 ? -inner : 0), qy = y + (k == 2 ? inner : k == 3 ? -inner : 0);
                        if (qx < 0 || qy < 0 || qx >= W || qy >= W) continue;
                        int o = qy * W + qx;
                        // (the nearer side of a fold stays clean: the line is drawn on what lies behind)
                        if (covered[o] && depth[at] - depth[o] > jump) fold = true;
                    }
                    ink[at] = fold;
                }
                else
                {
                    bool near = false;
                    for (int dy = -outer; dy <= outer && !near; dy++)
                        for (int dx = -outer; dx <= outer && !near; dx++)
                        {
                            if (dx * dx + dy * dy > outer * outer) continue;
                            int qx = x + dx, qy = y + dy;
                            if (qx >= 0 && qy >= 0 && qx < W && qy < W && covered[qy * W + qx]) near = true;
                        }
                    ink[at] = near;
                }
            }

        var result = new byte[size * size * 4];
        float bgA = (background >> 24) / 255f, bgR = ((background >> 16) & 255) / 255f, bgG = ((background >> 8) & 255) / 255f, bgB = (background & 255) / 255f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = 0, g = 0, b = 0, al = 0;
                for (int dy = 0; dy < SS; dy++)
                    for (int dx = 0; dx < SS; dx++)
                    {
                        int at = (y * SS + dy) * W + x * SS + dx;
                        uint c = ink[at] ? Ink : covered[at] ? colour[at] : 0u;
                        float ca = (c >> 24) / 255f;
                        // (over the background, with the colour kept premultiplied until the end)
                        float oa = ca + bgA * (1f - ca);
                        r += ((c >> 16) & 255) / 255f * ca + bgR * bgA * (1f - ca);
                        g += ((c >> 8) & 255) / 255f * ca + bgG * bgA * (1f - ca);
                        b += (c & 255) / 255f * ca + bgB * bgA * (1f - ca);
                        al += oa;
                    }
                int o = (y * size + x) * 4;
                if (al > 1e-4f)
                {
                    result[o] = (byte)Math.Clamp(b / al * 255f + 0.5f, 0f, 255f);
                    result[o + 1] = (byte)Math.Clamp(g / al * 255f + 0.5f, 0f, 255f);
                    result[o + 2] = (byte)Math.Clamp(r / al * 255f + 0.5f, 0f, 255f);
                }
                result[o + 3] = (byte)Math.Clamp(al / (SS * SS) * 255f + 0.5f, 0f, 255f);
            }
        return result;
    }
}
