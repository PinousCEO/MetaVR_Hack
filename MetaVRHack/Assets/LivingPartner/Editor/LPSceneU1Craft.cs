using LivingPartner.Character;
using LivingPartner.MR;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// MrCore/CraftBench (MR/MrCraft.cs, MR/CraftBenchView.cs): the little workbench the partner hammers at while it
    /// makes something - a plank on two legs as a paper cut-out that stands on the desk, its soft shadow and the
    /// dashed ring that says it can be taken, the hammer for the partner's hand, and the paper card over it with the
    /// name of what is being made, the percentage in large figures with a bar, and one line of what is being done.
    /// (The thing in the making itself is the ball of light of MrCore/Making.) Authored like everything else: real
    /// objects under MrCore, wired into a serialized field of MrApp.
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static void AuthorCraftBench(MrApp app, Transform t)
        {
            Color ink = new Color(0.29f, 0.18f, 0.15f, 1f);
            Color woodTop = new Color(0.95f, 0.78f, 0.55f, 1f), woodLow = new Color(0.86f, 0.64f, 0.41f, 1f);
            Color legTop = new Color(0.80f, 0.58f, 0.37f, 1f), legLow = new Color(0.68f, 0.47f, 0.29f, 1f);
            var node = Node("CraftBench", t).transform;
            node.localPosition = new Vector3(0.30f, 0.80f, 0.35f);       // authoring pose only
            var v = node.gameObject.AddComponent<CraftBenchView>();

            // ---- the bench: its foot is the root's origin; metres for a partner of 10 cm (the app scales the root)
            var root = Node("Bench", node).transform;
            v.root = root;
            const float top = CraftBenchView.Top, plankH = 0.0090f, plankW = 0.074f, legH = top - plankH + 0.002f, legW = 0.0085f;
            // (legs and the rail behind the plank; the plank last, in front)
            for (int s = -1; s <= 1; s += 2)
            {
                var leg = Panel(s < 0 ? "Leg L" : "Leg R", root, legW, legH, 0.0022f, legTop, legLow, 9, 0.0007f);
                leg.transform.localPosition = new Vector3(s * 0.0260f, legH * 0.5f, 0.0010f);
                leg.transform.localRotation = Quaternion.Euler(0f, 0f, -s * 5f);      // splayed a little
            }
            var rail = Panel("Rail", root, 0.050f, 0.0042f, 0.0016f, legTop, legLow, 9, 0.0006f);
            rail.transform.localPosition = new Vector3(0f, 0.0085f, 0.0012f);
            var plank = Panel("Plank", root, plankW, plankH, 0.0036f, woodTop, woodLow, 10, 0.0009f);
            plank.transform.localPosition = new Vector3(0f, top - plankH * 0.5f, 0f);
            // (two little nail heads on the plank)
            for (int s = -1; s <= 1; s += 2)
            {
                var nail = Panel(s < 0 ? "Nail L" : "Nail R", root, 0.0022f, 0.0022f, 0.0011f, ink, ink, 11);
                nail.transform.localPosition = new Vector3(s * 0.0300f, top - plankH * 0.5f, -0.0006f);
            }

            // ---- on the desk under it: the soft shadow, and the dashed ring that shows when a hand comes near ("take me")
            v.shadow = Hidden(Fx("Shadow", node, 0, new Color(0.29f, 0.18f, 0.15f, 0.26f), new Vector4(1.6f, 0, 0, 0), 1));
            v.shadow.transform.localRotation = Quaternion.Euler(90, 0, 0); v.shadow.transform.localScale = new Vector3(0.110f, 0.050f, 1f);
            v.grip = Hidden(Fx("Grip Ring", node, 7, new Color(1f, 0.78f, 0.34f, 0.8f), new Vector4(0.90f, 0.045f, 14f, 0.5f), 2, new Color(1f, 0.93f, 0.74f, 0.8f)));
            v.grip.transform.localRotation = Quaternion.Euler(90, 0, 0); v.grip.transform.localScale = Vector3.one * 0.135f;

            // ---- the hammer for the partner's right hand
            v.hammer = HiddenSprite(Sprite("Hammer", node, "hammer", 0.048f, 0.048f, 13));
            v.hammer.transform.localPosition = new Vector3(-0.05f, 0.07f, -0.008f);

            // ---- the card over the bench: what is being made / 42% and a bar / what is being done just now
            const float W = CraftBenchView.CardMinW, H = CraftBenchView.CardH;
            var card = Node("Card", node).transform;
            card.localPosition = new Vector3(0f, 0.125f, 0f);
            v.card = card;
            v.cardBg = Panel("Bg", card, W, H, 0.008f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 22, 0.0011f);
            v.cardBg.transform.localPosition = new Vector3(0, 0, 0.001f);
            v.title = Ordered(Label("Title", card, "Wooden chair", 0.0090f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), 123);
            v.title.transform.localPosition = new Vector3(0, 0.0165f, -0.001f);
            v.percent = Ordered(Label("Percent", card, "42%", 0.0190f, ink, TextAnchor.MiddleCenter, true), 123);
            v.percent.transform.localPosition = new Vector3(0, 0.0030f, -0.001f);
            v.barBg = Panel("Bar", card, W - 0.018f, 0.0044f, 0.0022f, new Color(0.93f, 0.87f, 0.76f, 1f), new Color(0.90f, 0.83f, 0.71f, 1f), 23, 0f);
            v.barBg.transform.localPosition = new Vector3(0, -0.0095f, 0f);
            v.barFill = Panel("Bar Fill", card, (W - 0.018f) * 0.42f, 0.0044f, 0.0022f, new Color(1f, 0.80f, 0.36f, 1f), new Color(1f, 0.70f, 0.26f, 1f), 24, 0f);
            v.barFill.transform.localPosition = new Vector3(-(W - 0.018f) * 0.29f, -0.0095f, -0.0006f);
            v.step = Ordered(Label("Step", card, "Hammering away ...", 0.0078f, Ui.Pal.TextDim, TextAnchor.MiddleCenter), 123);
            v.step.transform.localPosition = new Vector3(0, -0.0180f, -0.001f);

            Wire(app, "_bench", v);
        }
    }
}
