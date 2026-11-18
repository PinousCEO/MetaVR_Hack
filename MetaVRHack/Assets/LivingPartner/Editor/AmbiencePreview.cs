using System.IO;
using UnityEditor;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// Looks at one of the surroundings without the app: the dome shader (LivingPartner/Ambience) on a sphere round a
    /// temporary camera, rendered in several directions side by side into a PNG. In the simulator the work area's
    /// window hides most of the dome, so a view painted round the user (the mountain top, the seaside) cannot be
    /// judged there. Nothing is saved to the project; everything made here is destroyed again.
    ///   Render        left, ahead, right, behind at one moment
    ///   RenderTwo     the same at two moments, one row under the other: what differs between the rows is what MOVES
    ///   RenderDetail  one direction at the size a headset shows it (about 20 pixels per degree)
    /// </summary>
    public static class AmbiencePreview
    {
        static readonly float[] Round = { -90f, 0f, 90f, 180f };

        /// <summary>pattern: 1 spots .. 7 bubbles, 8 peak, 9 coast. Colours as "#RRGGBB". Returns a line about what was written.</summary>
        public static string Render(int pattern, string c0, string c1, string c2, string pngPath, float seconds = 3f)
            => RenderCore(pattern, c0, c1, c2, pngPath, new[] { seconds }, Round, -4f, 78f, 900, 620, 0f, 1f);

        /// <summary>Two moments (seconds apart), four directions each. workHalf &gt; 0: with the work area's window cut out, so many degrees to each side.</summary>
        public static string RenderTwo(int pattern, string c0, string c1, string c2, string pngPath, float t0 = 0f, float t1 = 2f, float speed = 1f, float workHalf = 0f)
            => RenderCore(pattern, c0, c1, c2, pngPath, new[] { t0, t1 }, Round, -4f, 78f, 900, 620, workHalf, speed);

        /// <summary>One direction (degrees to the right of straight ahead, degrees up) at two moments, side by side.</summary>
        public static string RenderDetail(int pattern, string c0, string c1, string c2, float lookYaw, float lookPitch, string pngPath, float t0 = 0f, float t1 = 2f, float fov = 60f, float speed = 1f)
            => RenderCore(pattern, c0, c1, c2, pngPath, new[] { t0 }, new[] { lookYaw, lookYaw + 0.0001f }, -lookPitch, fov, 1000, 760, 0f, speed, t1);

        // ------------------------------------------------------------------ does a place stay put in the WORLD?
        /// <summary>
        /// Renders the surroundings as the headset app feeds them while the HEAD TURNS by 0, 20 and 40 degrees (the eyes
        /// swing round the neck, the dome follows them, the work area's window is worked out anew from where the
        /// screens now are - as MrWorld.UpdateAmbience does, settled), brings the three pictures back to one grid of
        /// WORLD directions (0.1 degree per pixel) and measures how much the same world direction differs between
        /// them. Surroundings that are anchored in the world give the same picture three times.
        ///   mode "turn"  the app's feed. latched: the place's bearing is fixed when it comes on (_PlaceDir);
        ///                false: it follows the work area all the time (how it was until 2026-11-18)
        ///   mode "spin"  the camera only turns on the spot and nothing else changes: the check of the measurement itself
        ///   mode "eyes"  the two eyes, 64 mm apart, at one moment: do both see the same thing along the same world ray?
        /// The time stands still (speed 0), so nothing but the anchoring can differ. The sheet: the pictures on the
        /// world grid side by side, then the difference between the first and the last (four times enlarged);
        /// directions inside the work area's window, or outside one of the pictures, are left black and not counted.
        /// (The glitter of the sea is finer than a pixel, so even the same view sampled on another grid of pixels
        /// differs a little: "spin" gives that floor; an anchored place gives the same numbers as "spin".)
        /// </summary>
        public static string AnchorTest(int pattern, string c0, string c1, string c2, string pngPath, string mode = "turn", bool latched = true, float baseYaw = -120f)
        {
            // Below the horizon the seaside is GROUND that lies in the room (the beach, the sea): there the pictures are
            // compared point of the ground by point. Everything else (the sky, the far ranges and the sea of clouds of
            // the mountain top, the patterns 1 - 7) is infinitely far away and is compared direction by direction.
            bool groundLies = pattern == 9;
            var shader = Shader.Find("LivingPartner/Ambience");
            if (shader == null) return "shader LivingPartner/Ambience not found";
            const int W = 800, H = 600, GW = 520, GH = 460; const float fov = 80f, lookDown = 8f, neck = 0.09f, step = 0.1f;
            var neckPos = new Vector3(0, 500, 0);
            // three screens on a desk in front (as in the user's room): centre, half width, half height
            Vector3[][] screens = { Quad(neckPos, -0.62f, 0.05f, 0.58f, 0.17f, 0.30f), Quad(neckPos, 0.02f, 0.28f, 0.72f, 0.30f, 0.17f), Quad(neckPos, 0.02f, -0.08f, 0.70f, 0.30f, 0.17f) };
            bool eyes = mode == "eyes";
            float[] turns = eyes ? new[] { 0f, 0f } : new[] { 0f, 20f, 40f };
            int n = turns.Length;
            var mat = new Material(shader);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var camGo = new GameObject("AmbiencePreviewCamera");
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var shots = new Texture2D[n]; var rots = new Quaternion[n]; var dirs = new Vector3[n]; var sizes = new Vector4[n]; var eyeAt = new Vector3[n];
            const float below = 1.2f;                                                    // the eyes above the place's ground when it came on
            Vector3 head0 = neckPos + Quaternion.Euler(0, baseYaw, 0) * Vector3.forward * neck;
            var sheet = new Texture2D(GW * (n + 1), GH, TextureFormat.RGB24, false);
            try
            {
                Color A(string hex, Color fallback) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
                mat.SetColor("_C0", A(c0, Color.blue)); mat.SetColor("_C1", A(c1, Color.yellow)); mat.SetColor("_C2", A(c2, Color.cyan));
                mat.SetFloat("_Pattern", pattern); mat.SetFloat("_Dim", 1f); mat.SetFloat("_Speed", 0f); mat.SetFloat("_Density", 1f);
                mat.SetFloat("_Beat", 0.5f); mat.SetFloat("_Amount", 1f); mat.SetFloat("_TimeShift", 0f);
                mat.SetTexture("_Noise", AmbienceNoise.Ensure());
                Object.DestroyImmediate(sphere.GetComponent<Collider>());
                sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;
                sphere.transform.localScale = Vector3.one * 14f;
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.42f, 0.40f);
                cam.targetTexture = rt;
                for (int k = 0; k < n; k++)
                {
                    float yawK = baseYaw + turns[k];
                    Vector3 head = neckPos + Quaternion.Euler(0, mode == "spin" ? baseYaw : yawK, 0) * Vector3.forward * neck;
                    WorkArea(screens, head, out dirs[k], out sizes[k]);
                    rots[k] = Quaternion.Euler(lookDown, yawK, 0);
                    sphere.transform.position = head;                                    // (the app puts the dome on the head)
                    cam.transform.rotation = rots[k];
                    eyeAt[k] = head + (eyes ? rots[k] * Vector3.right * (k == 0 ? -0.032f : 0.032f) : Vector3.zero);
                    cam.transform.position = eyeAt[k];
                    mat.SetVector("_WorkDir", dirs[k]); mat.SetVector("_WorkSize", sizes[k]);
                    mat.SetVector("_PlaceDir", latched ? new Vector4(dirs[0].x, 0f, dirs[0].z, sizes[0].x) : Vector4.zero);
                    mat.SetVector("_PlaceAt", latched ? new Vector4(head0.x, head0.y - below, head0.z, below) : Vector4.zero);
                    cam.Render();
                    RenderTexture.active = rt;
                    shots[k] = new Texture2D(W, H, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
                    shots[k].ReadPixels(new Rect(0, 0, W, H), 0, 0); shots[k].Apply();
                    RenderTexture.active = null;
                }
                float tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), aspect = W / (float)H;
                var sum = new double[n]; var over = new int[n]; int counted = 0;
                var px = new Color[n];
                for (int gy = 0; gy < GH; gy++)
                    for (int gx = 0; gx < GW; gx++)
                    {
                        float yaw = (baseYaw - 6f + gx * step) * Mathf.Deg2Rad, pitch = (14f - gy * step) * Mathf.Deg2Rad;
                        var w0 = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
                        // below the horizon the direction stands for a POINT of the ground (where it meets the sheet 1.2 m
                        // under the head as it was at the start): every picture is asked for that same point of the room
                        bool isGround = groundLies && w0.y < -0.002f;
                        Vector3 point = isGround ? head0 + w0 * (below / -w0.y) : Vector3.zero;
                        bool ok = true;
                        for (int k = 0; k < n && ok; k++)
                        {
                            Vector3 w = isGround ? (point - eyeAt[k]).normalized : w0;
                            Vector3 v = Quaternion.Inverse(rots[k]) * w;
                            float u = 0.5f + 0.5f * v.x / (Mathf.Max(v.z, 1e-4f) * tanV * aspect), vv = 0.5f + 0.5f * v.y / (Mathf.Max(v.z, 1e-4f) * tanV);
                            ok = v.z > 0.05f && u > 0.004f && u < 0.996f && vv > 0.004f && vv < 0.996f && InsideWork(w, dirs[k], sizes[k]) < 0.001f;
                            if (ok) px[k] = shots[k].GetPixelBilinear(u, vv);
                        }
                        int row = GH - 1 - gy;
                        if (!ok) { for (int k = 0; k <= n; k++) sheet.SetPixel(k * GW + gx, row, Color.black); continue; }
                        counted++;
                        for (int k = 0; k < n; k++)
                        {
                            sheet.SetPixel(k * GW + gx, row, px[k]);
                            float dr = Mathf.Abs(px[k].r - px[0].r), dg = Mathf.Abs(px[k].g - px[0].g), db = Mathf.Abs(px[k].b - px[0].b);
                            sum[k] += (dr + dg + db) / 3f * 255f;
                            if (Mathf.Max(dr, Mathf.Max(dg, db)) * 255f > 10f) over[k]++;
                            if (k == n - 1) sheet.SetPixel(n * GW + gx, row, new Color(dr * 4f, dg * 4f, db * 4f));
                        }
                    }
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
                File.WriteAllBytes(pngPath, sheet.EncodeToPNG());
                var sb = new System.Text.StringBuilder();
                sb.Append($"pattern {pattern} {mode}{(mode == "turn" ? (latched ? " latched" : " following") : "")}: {counted} world directions");
                for (int k = 1; k < n; k++)
                    sb.Append($" | {(eyes ? "right eye against left" : turns[k] + " deg against 0")}: mean difference {sum[k] / Mathf.Max(1, counted):0.00} of 255, {100.0 * over[k] / Mathf.Max(1, counted):0.0} % of directions differ by more than 10; work direction turned {Vector3.SignedAngle(dirs[0], dirs[k], Vector3.up):0.0} deg, half width {sizes[0].x:0.0} -> {sizes[k].x:0.0}");
                sb.Append($" | {pngPath}");
                return sb.ToString();
            }
            finally
            {
                var cam = camGo.GetComponent<Camera>();
                if (cam != null) cam.targetTexture = null;
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(sphere); Object.DestroyImmediate(mat);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(sheet);
                foreach (var s in shots) if (s != null) Object.DestroyImmediate(s);
            }
        }

        static Vector3[] Quad(Vector3 origin, float x, float y, float z, float halfW, float halfH)
            => new[] { origin + new Vector3(x - halfW, y - halfH, z), origin + new Vector3(x + halfW, y - halfH, z), origin + new Vector3(x - halfW, y + halfH, z), origin + new Vector3(x + halfW, y + halfH, z) };

        /// <summary>The work area as MrWorld.UpdateAmbience works it out from the screens and the head (the settled values).</summary>
        static void WorkArea(Vector3[][] screens, Vector3 head, out Vector3 dir, out Vector4 size)
        {
            Vector3 sum = Vector3.zero;
            foreach (var q in screens) { Vector3 d = (q[0] + q[1] + q[2] + q[3]) * 0.25f - head; d.y = 0; sum += d.normalized; }
            dir = sum.normalized;
            float wide = 30f, top = 12f; Vector3 right = Vector3.Cross(Vector3.up, dir);
            foreach (var q in screens)
                foreach (var p in q)
                {
                    Vector3 corner = p - head;
                    wide = Mathf.Max(wide, Mathf.Abs(Mathf.Atan2(Vector3.Dot(corner, right), Vector3.Dot(corner, dir)) * Mathf.Rad2Deg));
                    top = Mathf.Max(top, Mathf.Asin(Mathf.Clamp(corner.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);
                }
            size = new Vector4(Mathf.Clamp(wide + 9f, 35f, 85f), Mathf.Clamp(top + 7f, 12f, 55f), 78f, 9f);
        }

        /// <summary>The shader's own window: 1 inside the work area, 0 outside.</summary>
        static float InsideWork(Vector3 w, Vector3 dir, Vector4 size)
        {
            Vector3 f = new Vector3(dir.x, 0, dir.z).normalized, r = new Vector3(f.z, 0, -f.x);
            float yaw = Mathf.Atan2(Vector3.Dot(w, r), Vector3.Dot(w, f)) * Mathf.Rad2Deg, pitch = Mathf.Asin(Mathf.Clamp(w.y, -1f, 1f)) * Mathf.Rad2Deg, e = Mathf.Max(size.w, 0.5f);
            return (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(size.x - e, size.x + e, Mathf.Abs(yaw))))
                 * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(size.y - e, size.y + e, pitch)))
                 * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(size.z - e, size.z + e, -pitch)));
        }

        static string RenderCore(int pattern, string c0, string c1, string c2, string pngPath, float[] times, float[] yaws, float pitch, float fov, int W, int H, float workHalf, float speed, float secondColumnTime = float.NaN)
        {
            var shader = Shader.Find("LivingPartner/Ambience");
            if (shader == null) return "shader LivingPartner/Ambience not found";
            var mat = new Material(shader);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var camGo = new GameObject("AmbiencePreviewCamera");
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var sheet = new Texture2D(W * yaws.Length, H * times.Length, TextureFormat.RGB24, false);
            try
            {
                Color A(string hex, Color fallback) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
                mat.SetColor("_C0", A(c0, Color.blue)); mat.SetColor("_C1", A(c1, Color.yellow)); mat.SetColor("_C2", A(c2, Color.cyan));
                mat.SetFloat("_Pattern", pattern); mat.SetFloat("_Dim", 1f); mat.SetFloat("_Speed", speed); mat.SetFloat("_Density", 1f);
                mat.SetFloat("_Beat", 0.5f); mat.SetFloat("_Amount", 1f);
                mat.SetVector("_WorkDir", new Vector4(0, 0, 1, 0));
                mat.SetVector("_WorkSize", workHalf > 0f ? new Vector4(workHalf, 22f, 78f, 9f) : new Vector4(0.1f, 0.1f, 0.1f, 0.5f));          // (no work area: the whole dome is shown)
                mat.SetTexture("_Noise", AmbienceNoise.Ensure());
                Object.DestroyImmediate(sphere.GetComponent<Collider>());
                sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;
                sphere.transform.position = new Vector3(0, 500, 0);                     // far from anything in the open scene
                sphere.transform.localScale = Vector3.one * 14f;
                var cam = camGo.AddComponent<Camera>();
                cam.transform.position = sphere.transform.position;
                cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.42f, 0.40f);      // a "room" for dim < 1 to show through
                cam.targetTexture = rt;
                for (int row = 0; row < times.Length; row++)
                    for (int i = 0; i < yaws.Length; i++)
                    {
                        float seconds = i == 1 && !float.IsNaN(secondColumnTime) ? secondColumnTime : times[row];
                        mat.SetFloat("_TimeShift", seconds);      // (the pipeline sets the time itself for every picture: the moment is moved by this much)
                        cam.transform.rotation = Quaternion.Euler(pitch, yaws[i], 0f);
                        cam.Render();
                        RenderTexture.active = rt;
                        sheet.ReadPixels(new Rect(0, 0, W, H), i * W, (times.Length - 1 - row) * H);      // (the first moment is the upper row)
                        RenderTexture.active = null;
                    }
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
                File.WriteAllBytes(pngPath, sheet.EncodeToPNG());
                return $"wrote {pngPath} ({sheet.width}x{sheet.height}), pattern {pattern}";
            }
            finally
            {
                var cam = camGo.GetComponent<Camera>();
                if (cam != null) cam.targetTexture = null;
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(sphere); Object.DestroyImmediate(mat);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(sheet);
            }
        }
    }
}
