using LivingPartner.Character;
using LivingPartner.MR;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    public static partial class LPSceneBuilder
    {
        static void AuthorTvRally(MrApp app, Transform parent)
        {
            var root = Node("TV Rally", parent);
            var t = root.transform;
            var paper = Panel("Instructions", t, 0.35f, 0.10f, 0.012f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 10, 0.001f);
            var label = Label("Instruction text", t, "", 0.012f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            var rings = new FxQuad[2];
            for (int i = 0; i < rings.Length; i++)
                rings[i] = Hidden(Fx(i == 0 ? "Gold gate" : "Blue gate", t, 1, i == 0 ? new Color(1f, 0.77f, 0.25f) : new Color(0.32f, 0.89f, 1f),
                    new Vector4(0.83f, 0.15f, 0, 0), 8, Color.white));
            var ball = Ball("Relay ball", t);
            Wire(app, "_tvRoot", root, "_tvLabel", label, "_tvPaper", paper, "_tvRings", rings, "_tvBall", ball);
            root.SetActive(false);
        }
    }
}
