using System;
using LivingPartner.Character;
using LivingPartner.MR;
using UnityEditor;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// MrCore/GlassOutBalls (GlassOutBallView, flown by MrGlassOut.cs): what a glass game sends out of the screen at the
    /// user is a REAL BALL in the room - "the goalkeeper ball must be clearly seen like in tennis - now it is just a 2D
    /// ball whooshing" (the user, 2026-11-18). For each of the 8 things that can be in the air: a toon sphere (the
    /// tennis ball's mesh, Art/Materials/LPOutBall.mat), a soft blob shadow on the desk, four puffs of a trail and a
    /// ring that bursts where it was blocked or got through. Authored hidden; the app places everything in the room.
    /// Draw order: the shadows low (they lie on the desk), the puffs and bursts with the other things that fly out
    /// (GlassStageOrder + 240 ..).
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static void AuthorU1Ball(MrApp app, Transform t)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Resources/Shaders/LPOutBall.shader");
            if (shader == null) throw new Exception("out-ball shader not found");
            var m = new Material(shader) { name = "LPOutBall" };
            m.SetColor("_OutlineColor", PartnerRig.InkColor);
            var mat = SaveAsset(m, ArtDir + "/Materials/LPOutBall.mat");

            const int OB = GlassStageOrder + 240;
            var root = Node("GlassOutBalls", t).transform;
            root.localPosition = new Vector3(0, 1.0f, 0.75f);                  // authoring pose only: the app places every part in the room
            var v = root.gameObject.AddComponent<GlassOutBallView>();
            int n = GlassOutBallView.Count, pn = GlassOutBallView.Puffs;
            v.balls = new MeshRenderer[n]; v.shadows = new FxQuad[n]; v.puffs = new FxQuad[n * pn]; v.bursts = new FxQuad[n];
            Color cream = new Color(1f, 0.97f, 0.90f, 1f), honey = new Color(1f, 0.84f, 0.42f, 1f);
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * 0.5f) * 0.12f;
                var go = Node("Ball " + i, root);
                go.AddComponent<MeshFilter>().sharedMesh = _ballMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat; r.enabled = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                go.transform.localPosition = new Vector3(x, 0, 0); go.transform.localScale = Vector3.one * 0.08f;
                v.balls[i] = r;
                v.shadows[i] = Hidden(Fx("Shadow " + i, root, 0, new Color(0.18f, 0.14f, 0.16f, 0.42f), new Vector4(2.2f, 0, 0, 0), 2));
                v.shadows[i].transform.localPosition = new Vector3(x, -0.22f, 0); v.shadows[i].transform.localRotation = Quaternion.Euler(90, 0, 0);
                v.shadows[i].transform.localScale = Vector3.one * 0.10f;
                for (int k = 0; k < pn; k++)
                {
                    var p = Hidden(Fx("Puff " + i + "-" + k, root, 0, new Color(1f, 0.93f, 0.72f, 0.5f), new Vector4(2.0f, 0, 0, 0), OB));
                    p.transform.localPosition = new Vector3(x, 0.02f * (k + 1), 0.05f * (k + 1)); p.transform.localScale = Vector3.one * (0.06f - 0.01f * k);
                    v.puffs[i * pn + k] = p;
                }
                v.bursts[i] = Hidden(Fx("Burst " + i, root, 1, cream, new Vector4(0.90f, 0.10f, 0, 0), OB + 4, honey));
                v.bursts[i].transform.localPosition = new Vector3(x, 0.14f, 0); v.bursts[i].transform.localScale = Vector3.one * 0.12f;
            }
            Wire(app, "_goBalls", v);
        }
    }
}
