using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.MR;
using UnityEditor;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// The parts of MrCore for playing without the PC (MR/MrLocalHost.cs): the paper card with "Play now" and
    /// "Connect my PC" that takes the place of the bare pairing numpad when no paired PC is found, the one tiny hint of
    /// the first steps, the tag over the Tools page of the fan, and the stickers of the things and games of the local
    /// world (Art/Ui/Local - the pictures the product drew for its own modules, made small). Authored like everything
    /// else: real objects under MrCore, wired into serialized fields of MrApp.
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static void AuthorLocal(MrApp app, Transform t, Transform panels)
        {
            // ---- the card: a headline, the big button, the smaller one, and one line about what the PC app adds
            const float W = 0.30f, H = 0.17f;
            var card = MakePanel("Welcome", panels, W, H, Ui.Pal.Orange, "Hello! I'm Nilo.", "With the PC app Nilo can talk, make new\nthings for you, and step into your screen.");
            card.transform.localPosition = new Vector3(-0.52f, 0.36f, 0);      // authoring pose only
            card.title.characterSize = 0.0150f * 10f / 64f * 0.72f;
            card.title.transform.localPosition = new Vector3(0, H * 0.5f - 0.0085f, -0.001f);
            card.bar.transform.localPosition = new Vector3(0, H * 0.5f - 0.0300f, -0.0008f);
            var play = AddPill(card, "Btn Play", "Play on Quest", new Vector3(0, 0.024f, 0), 0.200f, 0.046f, 0.0170f, Ui.Pal.Orange);
            var pc = AddPill(card, "Btn Connect", "Connect Mac or PC", new Vector3(0, -0.024f, 0), 0.200f, 0.032f, 0.0115f, Ui.Pal.Mint);
            // (the one line under the buttons: in the middle, and large enough to be read at arm's length)
            card.body.characterSize = 0.0100f * 10f / 64f * 0.72f; card.body.anchor = TextAnchor.UpperCenter; card.body.Mesh.alignment = TextAlignment.Center;
            card.body.transform.localPosition = new Vector3(0, -0.046f, -0.001f);

            // ---- the hint of the first steps: a few words on a strip of paper beside the partner
            var root = Node("Local", t).transform;
            root.localPosition = new Vector3(0.50f, 0.80f, 0.35f);      // authoring pose only
            var hintBg = Hidden(Panel("HintBg", root, 0.12f, 0.027f, 0.0105f, new Color(1f, 0.93f, 0.70f, 0.98f), new Color(1f, 0.86f, 0.52f, 0.98f), 8, 0.0012f));
            hintBg.transform.localPosition = new Vector3(0, 0, 0.002f);
            var hint = Label("Hint", root, "", 0.0145f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);

            // ---- the tag over the Tools page of the fan (drawn with the fan: over its pictures, its words with the fan's lettering)
            var tagBg = Hidden(Panel("TagBg", root, 0.10f, 0.0165f, 0.0082f, new Color(0.86f, 0.95f, 0.84f, 0.98f), new Color(0.80f, 0.92f, 0.79f, 0.98f), MenuOrder + 3, 0.0009f));
            tagBg.transform.localPosition = new Vector3(0, 0.05f, 0.002f);
            var tag = Ordered(Label("Tag", root, "", 0.0092f, new Color(0.16f, 0.42f, 0.22f), TextAnchor.MiddleCenter, true), 300);
            tag.transform.localPosition = new Vector3(0, 0.05f, 0);

            // ---- the stickers of the local world's things and games
            var icons = new List<Texture2D>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UiArtDir + "/Local" }))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (tex != null) icons.Add(tex);
            }
            icons.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (icons.Count == 0) throw new System.Exception("the stickers of the local world are missing: " + UiArtDir + "/Local/*.png");

            Wire(app, "_welcome", card, "_welcomePlay", play, "_welcomePc", pc, "_hintBg", hintBg, "_hint", hint, "_localTagBg", tagBg, "_localTag", tag, "_localIcons", icons.ToArray());
        }
    }
}
