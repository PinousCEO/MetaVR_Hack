using System;
using System.Collections.Generic;
using System.Text;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEditor;
using UnityEngine;
using Pose = LivingPartner.Shared.Pose;
using SN = System.Numerics;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// Authoring-time: turns the owner's character model (a static, unrigged T-pose mesh from Tripo with one albedo
    /// texture) into the partner's skinned body and its two eye meshes, bound to the shared Skeleton.
    ///
    /// The model is several separate shells: one for body + bonnet + face + sleeves + leg stubs, and one each for the
    /// eyes, cuffs, hands, feet and the clasp at the neck. Whole shells that are rigid in life follow one bone (a foot,
    /// a hand, a cuff, the clasp). The big shell gets smooth weights: vertices that clearly belong to one bone are
    /// fixed, and the rest is relaxed over the surface (a harmonic blend), so a part that only hangs from the head -
    /// the rim of the bonnet - follows the head even where it comes close to the shoulders.
    /// The eyes are cut out into their own meshes so that the rig can still blink and look around with them.
    /// The model's shells have openings where one part sits inside another (the end of a sleeve inside its cuff, the
    /// neck under the head). They are closed here, because the cut face that is drawn while the body passes through
    /// the screen counts surfaces along each line of sight and needs every shell to be closed.
    /// Surfaces that can never be seen from outside (the inside of the bonnet behind the face, a wrist inside its cuff)
    /// are marked as enclosed: while the body is cut they are drawn as part of the cut face. The model's texture is dark
    /// and meaningless on those surfaces and on the ones that only face the floor (under the body, between the legs),
    /// so both kinds take the colour of the visible surface next to them, which shows when a pose turns them into view.
    /// Nothing here runs in a player.
    /// </summary>
    public static class PartnerModelBuilder
    {
        public const string ModelPath = "Assets/LivingPartner/Art/Characters/Tripo/TripoPartner.fbx";
        public const string AlbedoPath = "Assets/LivingPartner/Art/Characters/Tripo/TripoPartner_Albedo.png";

        public sealed class Result
        {
            public Mesh body, eyeL, eyeR;
            /// <summary>Centre of each eye in the Head bone's space (character units).</summary>
            public Vector3 eyeLPos, eyeRPos;
            public Texture2D albedo;
            public string report;
        }

        /// <summary>The pose in which the skeleton lies inside the model as it was made: arms straight out to the sides.</summary>
        public static void BindPose(out Vector3[] pos, out Quaternion[] rot)
        {
            var pose = new Pose();
            float lift = 90f - Skeleton.ArmRestSplay;
            pose[Bone.ArmUL] = new SN.Vector3(0, 0, -lift);
            pose[Bone.ArmUR] = new SN.Vector3(0, 0, lift);
            var p = new SN.Vector3[Skeleton.N]; var r = new SN.Quaternion[Skeleton.N];
            Skeleton.FK(pose, p, r);
            pos = new Vector3[Skeleton.N]; rot = new Quaternion[Skeleton.N];
            for (int i = 0; i < Skeleton.N; i++) { pos[i] = Conv.U(p[i]); rot[i] = Conv.U(r[i]); }
        }

        /// <summary>
        /// Per position: true when no straight line from just above the surface reaches the open air, in any of the
        /// tried directions - the surface is enclosed by the rest of the model. Directions that point further down than
        /// minDirY (the y of the unit direction) are not tried.
        /// </summary>
        static bool[] FindHidden(Vector3[] pos, Vector3[] normalSum, List<int> occluderTris, float minDirY)
        {
            int nt = occluderTris.Count / 3;
            var p0 = new Vector3[nt]; var e1 = new Vector3[nt]; var e2 = new Vector3[nt];
            for (int t = 0; t < nt; t++)
            {
                p0[t] = pos[occluderTris[3 * t]];
                e1[t] = pos[occluderTris[3 * t + 1]] - p0[t]; e2[t] = pos[occluderTris[3 * t + 2]] - p0[t];
            }
            const int nd = 72;
            var dirs = new Vector3[nd];
            for (int k = 0; k < nd; k++)                               // evenly spread over the sphere
            {
                float y = 1f - 2f * (k + 0.5f) / nd, r = Mathf.Sqrt(1f - y * y), a = k * 2.39996323f;
                dirs[k] = new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
            }
            bool Blocked(Vector3 o, Vector3 d)
            {
                for (int t = 0; t < nt; t++)
                {
                    Vector3 h = Vector3.Cross(d, e2[t]); float det = Vector3.Dot(e1[t], h);
                    if (det > -1e-9f && det < 1e-9f) continue;
                    float inv = 1f / det; Vector3 s = o - p0[t]; float u = inv * Vector3.Dot(s, h);
                    if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(s, e1[t]); float w = inv * Vector3.Dot(d, q);
                    if (w < 0f || u + w > 1f) continue;
                    if (inv * Vector3.Dot(e2[t], q) > 1e-4f) return true;
                }
                return false;
            }
            var hidden = new bool[pos.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 n = normalSum[i].sqrMagnitude > 1e-12f ? normalSum[i].normalized : Vector3.up;
                Vector3 o = pos[i] + n * 0.003f;
                bool open = n.y >= minDirY && !Blocked(o, n);
                for (int k = 0; k < nd && !open; k++) if (dirs[k].y >= minDirY && Vector3.Dot(dirs[k], n) > 0.08f && !Blocked(o, dirs[k])) open = true;
                hidden[i] = !open;
            }
            return hidden;
        }

        /// <summary>A CPU-readable copy of a texture (the asset itself stays non-readable).</summary>
        static Texture2D ReadableCopy(Texture2D src, int size)
        {
            if (src == null) throw new Exception("character texture not found: " + AlbedoPath);
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var copy = new Texture2D(size, size, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, size, size), 0, 0); copy.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        public static Result Build()
        {
            var modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelRoot == null) throw new Exception("character model not found: " + ModelPath);
            var imp = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (!imp.isReadable || imp.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                imp.isReadable = true; imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false; imp.importCameras = false; imp.importLights = false; imp.importBlendShapes = false;
                imp.meshCompression = ModelImporterMeshCompression.Off;
                imp.SaveAndReimport();
                modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            }
            var filters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length != 1) throw new Exception("expected one mesh in the character model, found " + filters.Length);
            var src = filters[0].sharedMesh;
            Matrix4x4 toRoot = modelRoot.transform.worldToLocalMatrix * filters[0].transform.localToWorldMatrix;

            // ---- bring the model into character units: height 1, feet on y = 0, centred left to right
            var v = src.vertices; var nrm = src.normals; var uv = src.uv; var tris = src.triangles;
            if (uv.Length != v.Length) throw new Exception("the character model has no UVs");
            if (nrm.Length != v.Length) { src.RecalculateNormals(); nrm = src.normals; }
            var bb = new Bounds(toRoot.MultiplyPoint3x4(v[0]), Vector3.zero);
            for (int i = 0; i < v.Length; i++) { v[i] = toRoot.MultiplyPoint3x4(v[i]); nrm[i] = toRoot.MultiplyVector(nrm[i]).normalized; bb.Encapsulate(v[i]); }
            float scale = 1f / bb.size.y;
            var shift = new Vector3(-bb.center.x, -bb.min.y, -bb.center.z);
            for (int i = 0; i < v.Length; i++) v[i] = (v[i] + shift) * scale;

            // ---- weld by position (the mesh is split along UV seams)
            var weld = new Dictionary<Vector3Int, int>(); var wid = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 20000f), Mathf.RoundToInt(v[i].y * 20000f), Mathf.RoundToInt(v[i].z * 20000f));
                if (!weld.TryGetValue(k, out int j)) { j = weld.Count; weld[k] = j; }
                wid[i] = j;
            }
            int nw = weld.Count;

            // ---- close every opening: a fan of triangles from the middle of each open loop
            int closedLoops, closedEdges;
            var rim = new HashSet<int>();          // positions on the edge of a closed opening, and the middle of each closing fan
            {
                var use = new Dictionary<long, (int n, int a, int b)>();          // per welded edge: how many triangles, and its vertices in that triangle's order
                for (int i = 0; i < tris.Length; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int ia = tris[i + e], ib = tris[i + (e + 1) % 3], wa = wid[ia], wb = wid[ib];
                        if (wa == wb) continue;
                        long key = wa < wb ? ((long)wa << 32) | (uint)wb : ((long)wb << 32) | (uint)wa;
                        use[key] = use.TryGetValue(key, out var u) ? (u.n + 1, u.a, u.b) : (1, ia, ib);
                    }
                var lp = new int[nw];
                for (int i = 0; i < nw; i++) lp[i] = i;
                int LFind(int a) { while (lp[a] != a) { lp[a] = lp[lp[a]]; a = lp[a]; } return a; }
                var open = new List<(int a, int b)>();
                foreach (var u in use.Values) if (u.n == 1) { open.Add((u.a, u.b)); lp[LFind(wid[u.a])] = LFind(wid[u.b]); }
                var loops = new Dictionary<int, List<(int a, int b)>>();
                foreach (var e in open) { int r = LFind(wid[e.a]); if (!loops.TryGetValue(r, out var l)) loops[r] = l = new List<(int, int)>(); l.Add(e); }
                var vL = new List<Vector3>(v); var nL = new List<Vector3>(nrm); var uvL = new List<Vector2>(uv);
                var tL = new List<int>(tris); var widL = new List<int>(wid);
                foreach (var l in loops.Values)
                {
                    Vector3 c = Vector3.zero, n = Vector3.zero;
                    foreach (var e in l) c += v[e.a];
                    c /= l.Count;
                    foreach (var e in l) n += Vector3.Cross(v[e.a] - v[e.b], c - v[e.b]);
                    int iv = vL.Count;
                    vL.Add(c); nL.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up); uvL.Add(uv[l[0].a]); rim.Add(nw); widL.Add(nw++);
                    foreach (var e in l) { tL.Add(e.b); tL.Add(e.a); tL.Add(iv); rim.Add(wid[e.a]); rim.Add(wid[e.b]); }      // against the edge's own direction: the same facing as its neighbour
                }
                closedLoops = loops.Count; closedEdges = open.Count;
                v = vL.ToArray(); nrm = nL.ToArray(); uv = uvL.ToArray(); tris = tL.ToArray(); wid = widL.ToArray();
            }

            // ---- the separate shells
            var wpos = new Vector3[nw];
            for (int i = 0; i < v.Length; i++) wpos[wid[i]] = v[i];
            var parent = new int[nw];
            for (int i = 0; i < nw; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            for (int i = 0; i < tris.Length; i += 3) { int a = Find(wid[tris[i]]); parent[Find(wid[tris[i + 1]])] = a; parent[Find(wid[tris[i + 2]])] = a; }
            var shells = new Dictionary<int, List<int>>();
            for (int i = 0; i < nw; i++) { int r = Find(i); if (!shells.TryGetValue(r, out var l)) shells[r] = l = new List<int>(); l.Add(i); }

            BindPose(out var bindPos, out var bindRot);
            Vector3 J(Bone b) => bindPos[(int)b];
            float neckY = J(Bone.Head).y, wristX = Mathf.Abs(J(Bone.ArmLL).x), shoulderX = Mathf.Abs(J(Bone.ArmUL).x);

            // ---- what each shell is
            const int Unset = -1, Smooth = -2, EyeL = -3, EyeR = -4;
            var rigid = new int[nw];                 // per welded vertex: a bone index, or one of the markers above
            var light = new bool[nw];                // the status light (the clasp)
            for (int i = 0; i < nw; i++) rigid[i] = Unset;
            int mainRoot = -1, mainCount = 0;
            foreach (var s in shells) if (s.Value.Count > mainCount) { mainCount = s.Value.Count; mainRoot = s.Key; }
            var report = new StringBuilder();
            report.AppendLine($"model {ModelPath}: {v.Length} vertices ({nw} positions), {tris.Length / 3} triangles, {shells.Count} shells; scaled x{scale:0.0000} to height 1");
            report.AppendLine($"  closed {closedLoops} openings ({closedEdges} open edges)");
            int eyes = 0;
            foreach (var s in shells)
            {
                var b = new Bounds(wpos[s.Value[0]], Vector3.zero);
                foreach (int i in s.Value) b.Encapsulate(wpos[i]);
                Vector3 c = b.center; int tag; string what;
                if (s.Key == mainRoot) { tag = Smooth; what = "body, bonnet, face, sleeves, leg stubs (smooth weights)"; }
                else if (b.max.y < 0.5f * J(Bone.Hips).y + 0.02f) { tag = (int)(c.x < 0 ? Bone.FootL : Bone.FootR); what = "foot"; }
                else if (c.y > neckY + 0.05f && c.z > 0.5f * Skeleton.HeadRadius && Mathf.Max(b.size.x, b.size.y) < 0.2f) { tag = c.x < 0 ? EyeL : EyeR; what = "eye (separate mesh)"; eyes++; }
                else if (Mathf.Abs(c.x) > wristX && c.y < neckY + 0.03f) { tag = (int)(c.x < 0 ? Bone.ArmLL : Bone.ArmLR); what = "hand"; }
                else if (Mathf.Abs(c.x) > shoulderX && c.y < neckY + 0.03f) { tag = (int)(c.x < 0 ? Bone.ArmUL : Bone.ArmUR); what = "cuff"; }
                else if (c.y < neckY + 0.03f) { tag = (int)Bone.Chest; what = "clasp (status light)"; foreach (int i in s.Value) light[i] = true; }
                else { tag = (int)Bone.Head; what = "head detail"; }
                foreach (int i in s.Value) rigid[i] = tag;
                report.AppendLine($"  shell {s.Value.Count,5} positions, centre {c.ToString("F3")}, size {b.size.ToString("F3")} -> {what}{(tag >= 0 ? " on " + (Bone)tag : "")}");
            }
            if (eyes != 2) throw new Exception("expected two eye shells in the character model, found " + eyes);

            // ---- smooth weights on the big shell
            // channels: the bones that shell is allowed to follow
            Bone[] ch = { Bone.Hips, Bone.Spine, Bone.Chest, Bone.Head, Bone.ArmUL, Bone.ArmUR, Bone.LegLL, Bone.LegLR };
            int nc = ch.Length;
            var w = new float[nw, nc]; var seeded = new bool[nw];
            float hipsY = J(Bone.Hips).y, spineY = J(Bone.Spine).y, chestY = J(Bone.Chest).y, kneeY = J(Bone.LegLL).y;
            int[] seedCount = new int[nc];
            for (int i = 0; i < nw; i++)
            {
                if (rigid[i] != Smooth) continue;
                Vector3 p = wpos[i]; float ax = Mathf.Abs(p.x); int c = -1;
                if (p.y > neckY + 0.06f) c = 3;                                                           // head and bonnet
                else if (ax > shoulderX + 0.02f && p.y < neckY - 0.005f && p.y > chestY) c = p.x < 0 ? 4 : 5;   // sleeve
                else if (p.y < kneeY - 0.015f) c = p.x < 0 ? 6 : 7;                                        // the end of a leg stub
                else if (ax < shoulderX - 0.045f && p.y > chestY - 0.01f && p.y < chestY + 0.05f) c = 2;   // chest
                else if (Mathf.Abs(p.y - spineY) < 0.015f) c = 1;                                          // waist
                else if (p.y > hipsY - 0.045f && p.y < hipsY - 0.01f) c = 0;                               // bottom of the body
                if (c >= 0) { w[i, c] = 1f; seeded[i] = true; seedCount[c]++; }
            }
            for (int c = 0; c < nc; c++) if (seedCount[c] == 0) throw new Exception("no vertices found for " + ch[c] + " on the character model: the skeleton does not fit it");

            var nbr = new HashSet<int>[nw];
            for (int i = 0; i < tris.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = wid[tris[i + e]], b = wid[tris[i + (e + 1) % 3]];
                    if (a == b) continue;
                    (nbr[a] ??= new HashSet<int>()).Add(b); (nbr[b] ??= new HashSet<int>()).Add(a);
                }
            var free = new List<int>();
            for (int i = 0; i < nw; i++) if (rigid[i] == Smooth && !seeded[i] && nbr[i] != null) free.Add(i);
            var acc = new float[nc];
            for (int it = 0; it < 900; it++)
                foreach (int i in free)
                {
                    Array.Clear(acc, 0, nc); int n = 0;
                    foreach (int j in nbr[i]) { for (int c = 0; c < nc; c++) acc[c] += w[j, c]; n++; }
                    for (int c = 0; c < nc; c++) w[i, c] = acc[c] / n;
                }

            // ---- surfaces that cannot be seen from outside, and the colour they take when a pose uncovers them
            var wn = new Vector3[nw]; var wuv = new Vector2[nw];
            for (int i = 0; i < v.Length; i++) { wn[wid[i]] += nrm[i]; wuv[wid[i]] = uv[i]; }
            var occluders = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                int tag = rigid[wid[tris[i]]];
                if (tag == EyeL || tag == EyeR) continue;            // the eyes move (blink): what is under them counts as visible
                occluders.Add(wid[tris[i]]); occluders.Add(wid[tris[i + 1]]); occluders.Add(wid[tris[i + 2]]);
            }
            var hidden = FindHidden(wpos, wn, occluders, -2f);                // enclosed: no way out in any direction
            var unpainted = FindHidden(wpos, wn, occluders, -0.3f);           // ... or only a way out downwards
            var fill = new Color[nw]; var reached = new bool[nw]; int hiddenCount = 0, unpaintedCount = 0;
            var albedoPixels = ReadableCopy(AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath), 512);
            var queue = new Queue<int>();
            for (int i = 0; i < nw; i++)
            {
                // a closing fan has no place in the texture: it and the edge it hangs from take the colour next to them
                if (rim.Contains(i)) unpainted[i] = true;
                // the leg stubs fold into the body when the legs bend; treated the same way they carry no ink line, which
                // would otherwise show as a black strip under the body in a crouch
                if (rigid[i] == Smooth && w[i, 6] + w[i, 7] > 0.05f) unpainted[i] = true;
                if (rigid[i] == EyeL || rigid[i] == EyeR) hidden[i] = unpainted[i] = false;
                if (hidden[i]) hiddenCount++;
                if (unpainted[i]) { unpaintedCount++; continue; }
                fill[i] = albedoPixels.GetPixelBilinear(wuv[i].x, wuv[i].y); reached[i] = true; queue.Enqueue(i);
            }
            UnityEngine.Object.DestroyImmediate(albedoPixels);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                if (nbr[i] == null) continue;
                foreach (int j in nbr[i]) if (!reached[j]) { reached[j] = true; fill[j] = fill[i]; queue.Enqueue(j); }
            }
            report.AppendLine($"  {hiddenCount} of {nw} positions are enclosed; {unpaintedCount} take their colour from the surface next to them");

            // ---- per welded vertex: up to four bones
            var bw = new BoneWeight[nw]; int unweighted = 0;
            var perBone = new float[Skeleton.N];
            for (int i = 0; i < nw; i++)
            {
                if (rigid[i] >= 0) { bw[i] = new BoneWeight { boneIndex0 = rigid[i], weight0 = 1f }; perBone[rigid[i]] += 1f; continue; }
                if (rigid[i] != Smooth) continue;
                var idx = new int[4]; var val = new float[4];
                for (int k = 0; k < 4; k++)
                {
                    int best = -1; float bv = 1e-4f;
                    for (int c = 0; c < nc; c++) if (w[i, c] > bv && Array.IndexOf(idx, c + 1, 0, k) < 0) { bv = w[i, c]; best = c; }
                    idx[k] = best + 1; val[k] = best < 0 ? 0f : bv;                  // idx is 1-based so that 0 means "none"
                }
                float sum = val[0] + val[1] + val[2] + val[3];
                if (sum < 1e-4f) { unweighted++; bw[i] = new BoneWeight { boneIndex0 = (int)(wpos[i].y > neckY ? Bone.Head : Bone.Chest), weight0 = 1f }; continue; }
                var o = new BoneWeight();
                o.boneIndex0 = (int)ch[Mathf.Max(0, idx[0] - 1)]; o.weight0 = val[0] / sum;
                o.boneIndex1 = idx[1] > 0 ? (int)ch[idx[1] - 1] : 0; o.weight1 = val[1] / sum;
                o.boneIndex2 = idx[2] > 0 ? (int)ch[idx[2] - 1] : 0; o.weight2 = val[2] / sum;
                o.boneIndex3 = idx[3] > 0 ? (int)ch[idx[3] - 1] : 0; o.weight3 = val[3] / sum;
                bw[i] = o;
                perBone[o.boneIndex0] += o.weight0; perBone[o.boneIndex1] += o.weight1; perBone[o.boneIndex2] += o.weight2; perBone[o.boneIndex3] += o.weight3;
            }
            report.Append("  weight per bone (welded vertices):");
            for (int i = 0; i < Skeleton.N; i++) if (perBone[i] > 0) report.Append($" {(Bone)i} {perBone[i]:0}");
            report.AppendLine();
            report.AppendLine($"  smooth shell: {free.Count} relaxed vertices, {unweighted} left without a weight (given to the nearest of head / chest)");

            // ---- meshes
            var bind = new Matrix4x4[Skeleton.N];
            for (int i = 0; i < Skeleton.N; i++) bind[i] = Matrix4x4.TRS(bindPos[i], bindRot[i], Vector3.one).inverse;
            var res = new Result { albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath) };
            if (res.albedo == null) throw new Exception("character texture not found: " + AlbedoPath);
            res.body = Extract("PartnerBody_momo", i => rigid[wid[i]] >= 0 || rigid[wid[i]] == Smooth, Vector3.zero, true);
            res.body.bindposes = bind;
            res.eyeLPos = Centre(EyeL) - J(Bone.Head); res.eyeRPos = Centre(EyeR) - J(Bone.Head);
            res.eyeL = Extract("PartnerEyeL_momo", i => rigid[wid[i]] == EyeL, Centre(EyeL), false);
            res.eyeR = Extract("PartnerEyeR_momo", i => rigid[wid[i]] == EyeR, Centre(EyeR), false);
            report.AppendLine($"  body mesh {res.body.vertexCount} vertices / {res.body.triangles.Length / 3} triangles; eyes {res.eyeL.vertexCount} + {res.eyeR.vertexCount} vertices at {res.eyeLPos.ToString("F3")} / {res.eyeRPos.ToString("F3")} in head space");
            res.report = report.ToString();
            return res;

            Vector3 Centre(int tag)
            {
                var b = new Bounds(); bool any = false;
                for (int i = 0; i < nw; i++) if (rigid[i] == tag) { if (!any) { b = new Bounds(wpos[i], Vector3.zero); any = true; } else b.Encapsulate(wpos[i]); }
                return b.center;
            }

            Mesh Extract(string name, Func<int, bool> keep, Vector3 origin, bool skinned)
            {
                var map = new int[v.Length]; var ov = new List<Vector3>(); var on = new List<Vector3>(); var ouv = new List<Vector2>();
                var ouv2 = new List<Vector2>(); var oc = new List<Color>(); var ow = new List<BoneWeight>(); var ot = new List<int>();
                for (int i = 0; i < v.Length; i++)
                {
                    if (!keep(i)) { map[i] = -1; continue; }
                    map[i] = ov.Count;
                    // colour: rgb = the colour to show instead of the texture, a = how much of it (1 where the texture is meaningless)
                    Color fc = fill[wid[i]].linear; fc.a = unpainted[wid[i]] ? 1f : 0f;
                    ov.Add(v[i] - origin); on.Add(nrm[i]); ouv.Add(uv[i]); oc.Add(fc);
                    ouv2.Add(new Vector2(light[wid[i]] ? 1f : 0f, hidden[wid[i]] ? 1f : 0f)); ow.Add(bw[wid[i]]);
                }
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = map[tris[i]], b = map[tris[i + 1]], c = map[tris[i + 2]];
                    if (a >= 0 && b >= 0 && c >= 0) { ot.Add(a); ot.Add(b); ot.Add(c); }
                }
                var m = new Mesh { name = name };
                m.SetVertices(ov); m.SetNormals(on); m.SetColors(oc); m.SetUVs(0, ouv); m.SetUVs(1, ouv2);
                m.SetTriangles(ot, 0);
                if (skinned) m.boneWeights = ow.ToArray();
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
