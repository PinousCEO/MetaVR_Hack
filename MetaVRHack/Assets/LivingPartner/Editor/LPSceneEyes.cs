using LivingPartner.Character;
using LivingPartner.MR;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// The sign that stands beside the partner while it looks at what the user holds up (MR/MrEyes.cs): a small strip of
    /// cream paper with the words "Bambi is looking" and a coral light that pulses for as long as the headset's camera
    /// is being read. Authored like everything else: real objects under MrCore, hidden, wired into MrApp.
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static void AuthorEyes(MrApp app, Transform t)
        {
            var root = Node("Eyes", t).transform;
            root.localPosition = new Vector3(-0.50f, 1.02f, 0.35f);      // authoring pose only (MrEyes places it every frame it is shown)

            var bg = Hidden(Panel("LookBg", root, 0.150f, 0.026f, 0.013f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 10, 0.0011f));
            bg.transform.localPosition = new Vector3(-0.008f, 0f, 0.002f);
            var label = Label("LookLabel", root, "", 0.0135f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            // the light: a soft coral disc with a pale rim, to the left of the words
            var dot = Hidden(Fx("LookLight", root, 0, new Color(1f, 0.62f, 0.42f, 1f), new Vector4(2.6f, 0, 0, 0), 12, new Color(1f, 0.86f, 0.72f, 1f)));
            dot.transform.localPosition = new Vector3(-0.062f, 0f, -0.001f);
            dot.transform.localScale = Vector3.one * 0.011f;

            Wire(app, "_eyeLabel", label, "_eyeBg", bg, "_eyeDot", dot);
        }
    }
}
