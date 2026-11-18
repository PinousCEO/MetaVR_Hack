using LivingPartner.Character;
using LivingPartner.MR;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// The parts of MrCore that the scripts of things need (MR/MrRoomScript.cs): a pool of 24 little objects (each with
    /// its shadow) that are given one of five simple shapes, four paper labels, six lights for things that glow, and
    /// the strip of buttons that comes up over a scripted thing when it is pressed ("Wear" and up to four acts).
    /// Authored like everything else: real objects under MrCore, wired into serialized fields of MrApp.
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static void AuthorRoomScript(MrApp app, Transform t, Transform panels)
        {
            var root = Node("RoomScripts", t).transform;
            root.localPosition = new Vector3(-0.50f, 0.80f, 0.35f);      // authoring pose only

            // ---- the little objects: a ball, a box, a disc, a star or a ring - the mesh is given at run time (data)
            var objects = new PropView[RoomScript.MaxObjects];
            for (int i = 0; i < objects.Length; i++)
            {
                var go = Node("Object " + i, root);
                var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = _ballMesh;        // a stand-in until it is given its shape
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _propMat; mr.enabled = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                go.transform.localPosition = new Vector3((i % 8 - 3.5f) * 0.04f, (i / 8) * 0.04f, 0f); go.transform.localScale = Vector3.one * 0.02f;
                objects[i] = go.AddComponent<PropView>();
                objects[i].Bind(mf, mr);
                objects[i].shadow = Hidden(Fx("Shadow " + i, root, 0, new Color(0.18f, 0.14f, 0.16f, 0.28f), new Vector4(2.2f, 0, 0, 0), 1));
                objects[i].shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
                objects[i].shadow.transform.localScale = Vector3.one * 0.03f;
                // (a PropView has a ring that fills while a thing is being made; these objects are never "being made" - it stays hidden)
                objects[i].progress = Hidden(Fx("Progress " + i, root, 4, new Color(1f, 0.84f, 0.42f, 1f), new Vector4(0.78f, 0.20f, 0.1f, 0), 12, new Color(1f, 0.96f, 0.86f, 1f)));
                objects[i].progress.transform.localScale = Vector3.one * 0.01f;
            }

            // ---- labels: a word on a strip of paper, floating where the script puts it
            var labels = new StoryLabel[RoomScript.MaxLabels]; var labelBgs = new FxQuad[RoomScript.MaxLabels];
            for (int i = 0; i < labels.Length; i++)
            {
                labelBgs[i] = Hidden(Panel("LabelBg " + i, root, 0.07f, 0.018f, 0.007f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 10, 0.0010f));
                labelBgs[i].transform.localPosition = new Vector3(0, 0.14f + i * 0.025f, 0.002f);
                labels[i] = Label("Label " + i, root, "", 0.0135f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                labels[i].transform.localPosition = new Vector3(0, 0.14f + i * 0.025f, 0);
            }

            // ---- the light of a thing that glows: a soft disc round it, in the colour the script gave
            var glows = new FxQuad[6];
            for (int i = 0; i < glows.Length; i++)
            {
                glows[i] = Hidden(Fx("Glow " + i, root, 8, new Color(1f, 0.88f, 0.54f, 0.6f), new Vector4(2.4f, 0, 0, 0), 0, new Color(1f, 0.88f, 0.54f, 0.6f)));
                glows[i].transform.localPosition = new Vector3(0.20f, i * 0.03f, 0); glows[i].transform.localScale = Vector3.one * 0.12f;
            }

            // ---- the strip of buttons over a scripted thing: Wear / Take off, and what it offers (MrRoomScript lays them out)
            const float W = 0.34f, H = 0.050f;
            var strip = MakePanel("ThingStrip", panels, W, H, Ui.Pal.Orange, "", "");
            strip.transform.localPosition = new Vector3(0.84f, 0.36f, 0);      // authoring pose only
            Ui.SizePanel(strip.bg, W, H, H * 0.5f, 0.0016f);
            Hidden(strip.bar);
            var buttons = new UiButton[1 + RoomScript.MaxOffers];
            for (int i = 0; i < buttons.Length; i++)
                buttons[i] = AddPill(strip, i == 0 ? "Btn Wear" : "Btn Act " + i, i == 0 ? "Wear" : "Act", new Vector3((i - 2) * 0.064f, 0, 0), 0.070f, 0.030f, 0.0125f, i == 0 ? Ui.Pal.Orange : Ui.Pal.Mint);

            Wire(app, "_rsObjects", objects, "_rsLabels", labels, "_rsLabelBgs", labelBgs, "_rsGlows", glows, "_rsStrip", strip, "_rsButtons", buttons);
        }
    }
}
