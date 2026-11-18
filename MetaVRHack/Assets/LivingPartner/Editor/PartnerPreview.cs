using System;
using System.Collections.Generic;
using System.IO;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEditor;
using UnityEngine;
using Pose = LivingPartner.Shared.Pose;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// Authoring-time check of the partner prefab: renders it in every shared pose, and cut by a plane the way the two
    /// renderers cut it while it passes through the screen, into PNG sheets under artifacts/. Edit mode only.
    /// </summary>
    public static class PartnerPreview
    {
        const string PrefabPath = "Assets/LivingPartner/Prefabs/Partner.prefab";
        static readonly int ClipPlaneId = Shader.PropertyToID("_ClipPlane"), ClipSignId = Shader.PropertyToID("_ClipSign");

        [MenuItem("LivingPartner/Render Partner Check Sheets")]
        public static void RenderAll()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            RenderPoseSheet(Path.Combine(repo, "artifacts", "partner-pose-sheet.png"));
            RenderCutSheet(Path.Combine(repo, "artifacts", "partner-cut-sheet.png"));
        }

        static readonly (string name, Action<Pose> pose, float yaw)[] Poses =
        {
            ("idle front", p => PoseLib.Idle(p, 0.3f), 180f), ("idle 3/4", p => PoseLib.Idle(p, 0.3f), 145f),
            ("idle side", p => PoseLib.Idle(p, 0.3f), 90f), ("idle back", p => PoseLib.Idle(p, 0.3f), 0f),
            ("look aside", p => { PoseLib.Idle(p, 0.3f); PoseLib.AddLook(p, 60f, -15f); }, 180f),
            ("walk a", p => PoseLib.Walk(p, 0.15f), 110f), ("walk b", p => PoseLib.Walk(p, 0.65f), 145f),
            ("run a", p => PoseLib.Run(p, 0.2f), 100f), ("run b", p => PoseLib.Run(p, 0.7f), 145f),
            ("crouch", p => PoseLib.Crouch(p, 1f), 120f),
            ("dive", p => PoseLib.Dive(p), 110f), ("tuck", p => PoseLib.Tuck(p), 110f), ("land", p => PoseLib.Land(p, 1f), 150f),
            ("hang", p => PoseLib.Hang(p), 150f), ("pull up", p => PoseLib.PullUp(p, 1f), 120f),
            ("wave", p => PoseLib.Wave(p, 0.2f), 170f), ("happy", p => PoseLib.Happy(p, 0.2f), 170f), ("sit", p => PoseLib.Sit(p, 0.2f), 140f),
            ("shiver", p => PoseLib.Shiver(p, 0.2f), 150f), ("cower", p => PoseLib.Cower(p, 0.2f), 150f),
            ("think", p => PoseLib.Think(p, 0.5f), 165f), ("work", p => PoseLib.Work(p, 0.1f), 150f), ("point", p => PoseLib.Point(p, 0.1f), 140f),
            ("thankful", p => PoseLib.Thankful(p, 0.4f), 150f), ("balance", p => PoseLib.Balance(p, 0.3f, 1f), 180f),
            ("conjure", p => LivingPartner.MR.MrApp.ConjurePose(p, 0.3f, 1f), 180f), ("conjure 3/4", p => LivingPartner.MR.MrApp.ConjurePose(p, 1.2f, 1f), 145f),      // arms up to the light while it makes something (MrCraft)
        };

        /// <summary>Every shared pose on the prefab, five to a row, in the order of the list above.</summary>
        public static void RenderPoseSheet(string outPath, int cell = 420)
        {
            const int cols = 5;
            int rows = (Poses.Length + cols - 1) / cols;
            var sheet = new Texture2D(cell * cols, cell * rows, TextureFormat.RGB24, false);
            using (var stage = new Stage())
                for (int k = 0; k < Poses.Length; k++)
                {
                    var rig = stage.Spawn(Poses[k].yaw);
                    var pose = new Pose(); Poses[k].pose(pose);
                    ApplyPose(rig, pose);
                    stage.Shoot(sheet, (k % cols) * cell, (rows - 1 - k / cols) * cell, cell, 0.085f, 0.055f);
                    stage.Clear();
                }
            sheet.Apply();
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        /// <summary>
        /// The body threading through the screen as it does in a crossing (dive pose, head first, rising at 45 degrees),
        /// at five moments, seen from in front of the screen. Top row: the part still in front of the glass (what the
        /// room side draws). Middle row: the part already behind the glass (what the PC draws). Bottom row: the two
        /// together. The glass is the plane z = 0 of the stage; the camera is in the room.
        /// </summary>
        public static void RenderCutSheet(string outPath, int cell = 420)
        {
            float[] along = { -0.42f, -0.20f, 0.0f, 0.20f, 0.42f };        // where the middle of the body is, along its path (body heights)
            var sheet = new Texture2D(cell * along.Length, cell * 3, TextureFormat.RGB24, false);
            Vector3 d = new Vector3(0, 1, 1).normalized;                   // travel direction: up and into the screen
            var view = new Vector3(-0.45f, 0.30f, -1f);
            using (var stage = new Stage())
                for (int k = 0; k < along.Length; k++)
                    for (int row = 0; row < 3; row++)
                    {
                        var rig = stage.Spawn(0f);
                        var pose = new Pose(); PoseLib.Dive(pose);
                        ApplyPose(rig, pose);
                        float H = rig.Height;
                        var rot = Quaternion.FromToRotation(Vector3.up, d);          // the body's up axis lies along the path, belly down
                        Vector3 glass = Stage.Origin + new Vector3(0, 0.06f, 0);     // the chosen point on the glass
                        rig.transform.SetPositionAndRotation(glass + d * (along[k] * H) - rot * new Vector3(0, 0.5f * H, 0), rot);
                        var plane = new Vector4(0, 0, 1, -glass.z);
                        var block = new MaterialPropertyBlock();
                        foreach (var r in rig.GetComponentsInChildren<Renderer>())
                        {
                            if (r.GetComponent<FxQuad>() != null) { r.enabled = false; continue; }
                            if (r == rig.CutFace) continue;
                            r.GetPropertyBlock(block);
                            block.SetVector(ClipPlaneId, plane); block.SetFloat(ClipSignId, row == 0 ? -1f : row == 1 ? 1f : 0f);
                            r.SetPropertyBlock(block);
                        }
                        rig.CutFace.enabled = row < 2;
                        PartnerRig.PlaceCutFace(rig.CutFace.transform, rig.ChestWorld, glass, Vector3.forward, H);
                        stage.Shoot(sheet, k * cell, (2 - row) * cell, cell, 0.10f, 0.06f, view);
                        stage.Clear();
                    }
            sheet.Apply();
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        /// <summary>
        /// The tennis poses, for looking at them without a game: the ready stance (top row, seen from the front as the
        /// user sees it through the glass) and one forehand from start to finish (front, then from above the shoulder).
        /// </summary>
        public static void RenderTennisSheet(string outPath, int cell = 300)
        {
            const int n = 10;
            var rows = new (float yaw, Vector3? view, Action<Pose, float> pose)[]
            {
                (180f, null, (p, u) => PoseLib.TennisReady(p, u * 1.8f)),
                (180f, null, (p, u) => PoseLib.TennisSwing(p, u, 0.3f)),
                (180f, new Vector3(-0.5f, 0.9f, -1f), (p, u) => PoseLib.TennisSwing(p, u, 0.3f)),
            };
            var sheet = new Texture2D(cell * n, cell * rows.Length, TextureFormat.RGB24, false);
            using (var stage = new Stage())
                for (int r = 0; r < rows.Length; r++)
                    for (int k = 0; k < n; k++)
                    {
                        var rig = stage.Spawn(rows[r].yaw);
                        var pose = new Pose(); rows[r].pose(pose, k / (float)(n - 1));
                        ApplyPose(rig, pose);
                        if (rows[r].view.HasValue) stage.Shoot(sheet, k * cell, (rows.Length - 1 - r) * cell, cell, 0.085f, 0.055f, rows[r].view.Value);
                        else stage.Shoot(sheet, k * cell, (rows.Length - 1 - r) * cell, cell, 0.085f, 0.055f);
                        stage.Clear();
                    }
            sheet.Apply();
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        /// <summary>A few poses large, for looking at details of the skinning.</summary>
        public static void RenderCloseUps(string outPath, string[] names, int cell = 800)
        {
            var sheet = new Texture2D(cell * names.Length, cell, TextureFormat.RGB24, false);
            using (var stage = new Stage())
                for (int k = 0; k < names.Length; k++)
                {
                    int idx = Array.FindIndex(Poses, q => q.name == names[k]);
                    if (idx < 0) throw new Exception("no such pose: " + names[k]);
                    var rig = stage.Spawn(Poses[idx].yaw);
                    var pose = new Pose(); Poses[idx].pose(pose);
                    ApplyPose(rig, pose);
                    stage.Shoot(sheet, k * cell, 0, cell, 0.075f, 0.055f);
                    stage.Clear();
                }
            sheet.Apply();
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        /// <summary>
        /// Motion as rows of frames, for looking at how the body moves without running the apps: standing, walking,
        /// running, listening and talking (ten frames each, time runs left to right), then a whole crossing in each
        /// direction seen from the side (the glass is the vertical plane in the middle of each frame, the room on the left).
        /// </summary>
        public static void RenderMotionSheet(string outPath, int cell = 300)
        {
            const int n = 10;
            var rows = new (string name, float yaw, Action<Pose, float> pose)[]
            {
                ("idle", 160f, (p, u) => PoseLib.Idle(p, u * 3.6f)),
                ("walk", 115f, (p, u) => PoseLib.Walk(p, u)),
                ("run", 115f, (p, u) => PoseLib.Run(p, u)),
                ("listen", 160f, (p, u) => PoseLib.Listen(p, u * 2.5f)),
                ("talk", 160f, (p, u) => PoseLib.Talk(p, u * 1.6f, Mathf.Abs(Mathf.Sin(u * 9f)))),
            };
            var sheet = new Texture2D(cell * n, cell * (rows.Length + 2), TextureFormat.RGB24, false);
            int total = rows.Length + 2;
            using (var stage = new Stage())
            {
                for (int r = 0; r < rows.Length; r++)
                    for (int k = 0; k < n; k++)
                    {
                        var rig = stage.Spawn(rows[r].yaw);
                        var pose = new Pose(); rows[r].pose(pose, k / (float)n);
                        ApplyPose(rig, pose);
                        stage.Shoot(sheet, k * cell, (total - 1 - r) * cell, cell, 0.075f, 0.055f);
                        stage.Clear();
                    }
                for (int dir = 0; dir < 2; dir++)
                {
                    var probe = stage.Spawn(0f); float H = probe.Height; stage.Clear();
                    // a typical desk: the portal 20 cm above the desk and the partner 22 cm in front of the glass; the PC floor 30 cm below the portal
                    var plan = PlanBuilder.Build("preview", "partner", dir, H, new System.Numerics.Vector3(0, -0.20f, -0.22f), System.Numerics.Vector3.UnitY, -0.30f, 0.30f);
                    var cs = new CharState();
                    Vector3 portal = Stage.Origin + new Vector3(0, 0.30f, 0);
                    for (int k = 0; k < n; k++)
                    {
                        float tau = plan.Total * (0.06f + 0.88f * k / (n - 1));
                        PlanEvaluator.Evaluate(plan, tau, cs);
                        var rig = stage.Spawn(0f);
                        rig.transform.SetPositionAndRotation(portal + Conv.U(cs.RootPos), Conv.U(cs.RootRot));
                        ApplyPose(rig, cs.Pose);
                        foreach (var q in rig.GetComponentsInChildren<FxQuad>()) q.GetComponent<Renderer>().enabled = false;
                        stage.Shoot(sheet, k * cell, (1 - dir) * cell, cell, 0.30f, 0.20f, new Vector3(-1f, 0.10f, 0f));
                        stage.Clear();
                    }
                }
            }
            sheet.Apply();
            // the glass, as a thin line down the middle of the crossing frames
            for (int k = 0; k < n; k++)
                for (int y = 0; y < cell * 2; y++) sheet.SetPixel(k * cell + cell / 2, y, new Color(0.55f, 0.62f, 0.75f));
            sheet.Apply();
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        static void ApplyPose(PartnerRig rig, Pose pose)
        {
            var bones = rig.Bones;
            for (int i = 0; i < Skeleton.N; i++)
            {
                var r = Skeleton.RestEuler[i]; var e = pose.Euler[i];
                bones[i].localRotation = Quaternion.Euler(r.X, r.Y, r.Z) * Quaternion.Euler(e.X, e.Y, e.Z);
            }
            bones[(int)Bone.Hips].localPosition = Conv.U(Skeleton.RestLocal[(int)Bone.Hips]) + Conv.U(pose.HipsOffset);
            float sq = pose.Squash;
            bones[(int)Bone.Root].localScale = new Vector3(1f + 0.12f * sq, 1f - 0.16f * sq, 1f + 0.12f * sq);
        }

        /// <summary>A hidden camera and one hidden instance of the prefab, far from anything in the open scene.</summary>
        sealed class Stage : IDisposable
        {
            public static readonly Vector3 Origin = new Vector3(300, 0, 0);
            readonly GameObject _prefab, _camGo; readonly Camera _cam; GameObject _instance;

            public Stage()
            {
                _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (_prefab == null) throw new Exception("partner prefab not found: " + PrefabPath);
                _camGo = new GameObject("PartnerPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                _cam = _camGo.AddComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.97f, 0.94f, 0.88f, 1);
                _cam.orthographic = true; _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 5f; _cam.enabled = false;
            }

            public PartnerRig Spawn(float yaw)
            {
                _instance = (GameObject)PrefabUtility.InstantiatePrefab(_prefab);
                _instance.hideFlags = HideFlags.HideAndDontSave;
                _instance.transform.SetPositionAndRotation(Origin, Quaternion.Euler(0, yaw, 0));
                return _instance.GetComponent<PartnerRig>();
            }

            public void Shoot(Texture2D into, int x, int y, int size, float halfHeight, float lookAtHeight, Vector3? from = null)
            {
                _cam.orthographicSize = halfHeight;
                _camGo.transform.position = Origin + new Vector3(0, lookAtHeight, 0) + (from ?? new Vector3(0, 0.12f, -1f));
                _camGo.transform.rotation = Quaternion.LookRotation(Origin + new Vector3(0, lookAtHeight, 0) - _camGo.transform.position);
                var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
                _cam.targetTexture = rt; _cam.Render(); _cam.Render();       // the first render after spawning can miss the skinned mesh
                RenderTexture.active = rt;
                into.ReadPixels(new Rect(0, 0, size, size), x, y);
                RenderTexture.active = null; _cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }

            public void Clear() { if (_instance != null) UnityEngine.Object.DestroyImmediate(_instance); _instance = null; }
            public void Dispose() { Clear(); UnityEngine.Object.DestroyImmediate(_camGo); }
        }
    }
}
