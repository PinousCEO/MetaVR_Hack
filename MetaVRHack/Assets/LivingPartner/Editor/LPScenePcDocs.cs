using LivingPartner.Character;
using LivingPartner.Shared;
using LivingPartner.Windows;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// PcCharacter/DocFile: the file the partner holds up inside the PC (Windows/PcDocs.cs) - a sheet with a folded
    /// corner, a soft honey glow behind it, the file's name on a strip of paper over it, and the small "Click me" tag
    /// that pulses at its right. Authored in the measure of the sheet (one unit = its side); the app scales the
    /// whole thing so that the sheet is about 96 pixels on the monitor, and holds it up over the partner's head. It is in
    /// the partner's own window (the ordinary layer).
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static void AuthorPcDocs(PcApp app, Transform t)
        {
            var root = Node("DocFile", t);
            root.transform.localPosition = new Vector3(-0.12f, 0.0f, PlanBuilder.ResidentDepth(PartnerHeight) - 0.03f);      // authoring pose only
            root.transform.localScale = Vector3.one * 0.027f;
            Color honey = new Color(1f, 0.84f, 0.42f, 0.5f), honeyTop = new Color(1f, 0.90f, 0.62f, 1f), honeyBottom = new Color(1f, 0.82f, 0.46f, 1f);
            var glow = Fx("Glow", root.transform, 8, honey, new Vector4(2.2f, 0, 0, 0), 7, new Color(1f, 0.96f, 0.84f, 0.5f));
            glow.transform.localPosition = new Vector3(0, 0.02f, 0.01f); glow.transform.localScale = Vector3.one * 1.7f;
            var sheet = Sprite("Sheet", root.transform, "doc-pdf", 1f, 1f, 9);
            var nameBg = Panel("NameBg", root.transform, 1.66f, 0.30f, 0.11f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 10, 0.014f);
            nameBg.transform.localPosition = new Vector3(0, 0.66f, -0.01f);
            var name = Label("Name", root.transform, "Report.pdf", 0.185f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            name.transform.localPosition = new Vector3(0, 0.66f, -0.02f);
            var hint = Node("Hint", root.transform);
            hint.transform.localPosition = new Vector3(0.90f, 0.06f, -0.02f); hint.transform.localRotation = Quaternion.Euler(0, 0, -7f);
            var hintBg = Panel("HintBg", hint.transform, 0.98f, 0.28f, 0.14f, honeyTop, honeyBottom, 12, 0.014f);
            hintBg.transform.localPosition = new Vector3(0, 0, 0.005f);
            var hintLabel = Label("HintLabel", hint.transform, "Click me", 0.165f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            root.SetActive(false);
            Wire(app, "_dcRoot", root.transform, "_dcSheet", sheet, "_dcGlow", glow, "_dcName", name, "_dcHint", hint.transform, "_dcHintLabel", hintLabel,
                "_dcTexPdf", Art("doc-pdf"), "_dcTexPic", Art("doc-pic"));
        }
    }
}
