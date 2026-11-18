using LivingPartner.Character;
using LivingPartner.Shared;
using LivingPartner.Windows;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// The parts of Scenes/PcCharacter.unity of round 4 (2026-11-18):
    ///   PcCharacter/Work     THE WORK QUEUE PANEL (Windows/PcWork.cs): on its own Unity layer, drawn into a window of its
    ///                        own. One unit = one design pixel; the paper hangs from "Top" (its top-left corner, x right,
    ///                        y down), the folded chip from "Chip" (its bottom-right corner). A pool of job cards - the app
    ///                        lays each out for what its job is.
    ///   PcCharacter/WorkAt   the hammer in the partner's hand while it works at the panel (the partner's own window).
    ///   PcCharacter/Operate  the operate show (Windows/PcShow.cs): the ring at the spot that is clicked, the paper step the
    ///                        partner stands on beside it, the tag with the step.
    ///   PcCharacter/DocFile  + the thumbnail on the sheet the partner holds (Windows/PcDocs.cs).
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static float Em(float em) => em / 0.72f;      // Label takes a "world height"; the em of the type is 0.72 of it

        static StoryLabel WkLabel(string name, Transform parent, string text, float em, Color color, TextAnchor anchor, bool bold, float x, float y)
        {
            var l = Label(name, parent, text, Em(em), color, anchor, bold);
            l.transform.localPosition = new Vector3(x, -y, 0f);
            l.Mesh.alignment = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft ? TextAlignment.Left : anchor == TextAnchor.MiddleRight ? TextAlignment.Right : TextAlignment.Center;
            return l;
        }

        static FxQuad WkPanel(string name, Transform parent, float w, float h, float corner, Color top, Color bottom, int order, float rim, float x, float y)
        {
            var q = Panel(name, parent, w, h, corner, top, bottom, order, rim);
            q.transform.localPosition = new Vector3(x, -y, 0f);
            return q;
        }

        static void AuthorPcU2(PcApp app, Transform t)
        {
            const float PW = PcApp.WkPW, CW = PcApp.WkCW, HeadH = PcApp.WkHeadH, ChipW = PcApp.WkChipW, ChipH = PcApp.WkChipH;
            Color ink = PcApp.WkInk, dim = PcApp.WkDim;      // (as they are to be seen: see PcWork.cs)
            Color honeyTop = new Color(1f, 0.92f, 0.70f, 1f), honeyBottom = new Color(0.99f, 0.83f, 0.56f, 1f);
            Color paperTop = new Color(1f, 0.985f, 0.945f, 1f), paperBottom = new Color(0.985f, 0.945f, 0.875f, 1f);
            Color groove = new Color(0.29f, 0.18f, 0.15f, 0.22f);

            // ------------------------------------------------------------ the panel
            var work = Node("Work", t);
            work.transform.localPosition = new Vector3(0f, 0f, 0f);
            var top = Node("Top", work.transform).transform;
            top.localPosition = new Vector3(-PW * 0.5f, 300f, 0f);
            var paper = WkPanel("Paper", top, PW, 420f, 26f, paperTop, paperBottom, 0, 3f, PW * 0.5f, 210f);
            var head = WkPanel("Header", top, PW - 20f, HeadH - 20f, 24f, honeyTop, honeyBottom, 1, 2.4f, PW * 0.5f, HeadH * 0.5f);
            var headIcon = Sprite("Hammer", top, "hammer", 50f, 50f, 2);
            headIcon.transform.localPosition = new Vector3(46f, -HeadH * 0.5f, 0f);
            var headTitle = WkLabel("Title", top, "Workshop", 31f, ink, TextAnchor.MiddleLeft, true, 78f, HeadH * 0.5f - 1f);
            var headCount = WkLabel("Count", top, "1 job", 22f, dim, TextAnchor.MiddleRight, false, PW - 88f, HeadH * 0.5f);
            var foldBg = WkPanel("Fold", top, 48f, 48f, 24f, paperTop, paperBottom, 2, 2.2f, PW - 46f, HeadH * 0.5f);
            var foldLabel = WkLabel("FoldLabel", top, "–", 30f, ink, TextAnchor.MiddleCenter, true, PW - 46f, HeadH * 0.5f - 1f);

            const int N = 7;
            var card = new Transform[N]; var cardBg = new FxQuad[N]; var barBg = new FxQuad[N]; var bar = new FxQuad[N]; var btnBg = new FxQuad[N]; var mark = new FxQuad[N];
            var icon = new UiSprite[N];
            var title = new StoryLabel[N]; var time = new StoryLabel[N]; var step = new StoryLabel[N]; var pct = new StoryLabel[N]; var log = new StoryLabel[N]; var btn = new StoryLabel[N]; var markLabel = new StoryLabel[N];
            for (int i = 0; i < N; i++)
            {
                var c = Node("Card " + i, top).transform;
                c.localPosition = new Vector3(14f, -(HeadH + 6f + i * 232f), 0f);
                card[i] = c;
                // (authored as a job that is being worked on; the app lays the queued and the finished ones out itself)
                cardBg[i] = WkPanel("Paper", c, CW, 222f, 20f, new Color(1f, 0.975f, 0.88f, 1f), new Color(1f, 0.93f, 0.76f, 1f), 2, 2.4f, CW * 0.5f, 111f);
                icon[i] = Sprite("Kind", c, "tile-i-tennis", 56f, 56f, 3);
                icon[i].transform.localPosition = new Vector3(44f, -42f, 0f);
                title[i] = WkLabel("Title", c, "Clone Range", PcApp.WkEmTitle, ink, TextAnchor.MiddleLeft, true, 82f, 40f);
                time[i] = WkLabel("Time", c, "1:24", 22f, dim, TextAnchor.MiddleRight, false, CW - 18f, 40f);
                step[i] = WkLabel("Step", c, "Play-testing round 2", PcApp.WkEmStep, ink, TextAnchor.MiddleLeft, false, 20f, 88f);
                barBg[i] = WkPanel("Bar Groove", c, 380f, 22f, 11f, groove, groove, 3, 0f, 210f, 127f);
                bar[i] = WkPanel("Bar", c, 236f, 22f, 11f, new Color(0.62f, 0.86f, 0.66f, 1f), new Color(0.47f, 0.76f, 0.56f, 1f), 4, 2f, 20f + 118f, 127f);
                pct[i] = WkLabel("Percent", c, "62%", 29f, ink, TextAnchor.MiddleRight, true, CW - 18f, 127f);
                log[i] = WkLabel("Log", c, "Wrote the first version\nRound 1: the ball never comes back\nFixing it", 19f, dim, TextAnchor.UpperLeft, false, 20f, 148f);
                btnBg[i] = WkPanel("Button", c, 124f, 44f, 22f, new Color(0.99f, 0.84f, 0.84f, 1f), new Color(0.96f, 0.71f, 0.72f, 1f), 4, 2.2f, CW - 18f - 62f, 186f);
                btn[i] = WkLabel("ButtonLabel", c, "Cancel", 21f, ink, TextAnchor.MiddleCenter, true, CW - 18f - 62f, 187f);
                mark[i] = Hidden(WkPanel("Mark", c, 46f, 46f, 23f, new Color(0.62f, 0.84f, 0.62f, 1f), new Color(0.50f, 0.76f, 0.54f, 1f), 3, 2.4f, 40f, 36f));
                markLabel[i] = WkLabel("MarkLabel", c, "", 30f, ink, TextAnchor.MiddleCenter, true, 40f, 36f);
                if (i > 0) c.gameObject.SetActive(false);
            }
            var clearBg = WkPanel("Clear", top, 210f, 44f, 22f, new Color(0.84f, 0.93f, 0.98f, 1f), new Color(0.66f, 0.84f, 0.93f, 1f), 2, 2.2f, PW - 18f - 105f, 380f);
            var clearLabel = WkLabel("ClearLabel", top, "Clear finished", 21f, ink, TextAnchor.MiddleCenter, true, PW - 18f - 105f, 381f);
            var more = WkLabel("More", top, "", 21f, dim, TextAnchor.MiddleLeft, false, 24f, 380f);

            // sparkles and paper bits at a new step; the result's picture that pops out of the panel
            var sparks = new UiSprite[8];
            for (int i = 0; i < sparks.Length; i++) sparks[i] = HiddenSprite(Sprite("Sparkle " + i, top, "spark", 34f, 34f, 130));
            Color[] pastel = { new Color(0.96f, 0.55f, 0.58f, 1f), new Color(0.45f, 0.74f, 0.92f, 1f), new Color(0.50f, 0.80f, 0.56f, 1f), new Color(0.99f, 0.74f, 0.33f, 1f), new Color(0.66f, 0.58f, 0.90f, 1f), new Color(0.96f, 0.55f, 0.58f, 1f) };
            var bits = new FxQuad[6];
            for (int i = 0; i < bits.Length; i++) bits[i] = Hidden(Panel("Paper Bit " + i, top, 22f, 14f, 3f, pastel[i], pastel[i], 129, 0f));
            var popGlow = Hidden(Fx("Result Glow", top, 8, new Color(1f, 0.84f, 0.42f, 0.5f), new Vector4(2.2f, 0, 0, 0), 138, new Color(1f, 0.96f, 0.84f, 0.5f)));
            popGlow.transform.localPosition = new Vector3(PW * 0.5f, 62f, 0f); popGlow.transform.localScale = Vector3.one * 190f;
            var pop = HiddenSprite(Sprite("Result", top, "item-any", 104f, 104f, 140));
            pop.transform.localPosition = new Vector3(PW * 0.5f, 62f, 0f);

            // folded: the chip (it hangs from its bottom-right corner)
            var chip = Node("Chip", work.transform).transform;
            chip.localPosition = new Vector3(PW * 0.5f, -300f, 0f);
            var chipBg = Panel("Paper", chip, ChipW, ChipH, ChipH * 0.5f, honeyTop, honeyBottom, 0, 3f);
            chipBg.transform.localPosition = new Vector3(-ChipW * 0.5f, ChipH * 0.5f, 0f);
            var chipIcon = Sprite("Hammer", chip, "hammer", 48f, 48f, 2);
            chipIcon.transform.localPosition = new Vector3(-ChipW + 42f, ChipH * 0.5f, 0f);
            var chipLabel = Label("Label", chip, "Workshop – 1 job, 62%", Em(26f), ink, TextAnchor.MiddleLeft, true);
            chipLabel.transform.localPosition = new Vector3(-ChipW + 76f, ChipH * 0.5f + 5f, 0f);
            var chipBarBg = Panel("Bar Groove", chip, ChipW - 110f, 8f, 4f, groove, groove, 1);
            chipBarBg.transform.localPosition = new Vector3(-ChipW + 78f + (ChipW - 110f) * 0.5f, ChipH - 55f, 0f);
            var chipBar = Panel("Bar", chip, 200f, 8f, 4f, new Color(0.47f, 0.76f, 0.56f, 1f), new Color(0.40f, 0.68f, 0.50f, 1f), 2);
            chipBar.transform.localPosition = new Vector3(-ChipW + 78f + 100f, ChipH - 55f, 0f);
            chip.gameObject.SetActive(false);

            var kinds = new[] { Art("item-any"), Art("tile-i-tennis"), Art("hammer"), Art("skill-any"), Art("doc-pdf"), Art("doc-pic"), Art("gem"), Art("tile-i-find"), Art("tile-i-screen") };
            foreach (var tr in work.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = PcApp.WorkLayer;
            work.SetActive(false);
            Wire(app, "_wkRoot", work, "_wkTop", top, "_wkPaper", paper, "_wkHead", head, "_wkFoldBg", foldBg, "_wkClearBg", clearBg, "_wkHeadIcon", headIcon,
                "_wkHeadTitle", headTitle, "_wkHeadCount", headCount, "_wkFoldLabel", foldLabel, "_wkClearLabel", clearLabel, "_wkMore", more,
                "_wkCard", card, "_wkCardBg", cardBg, "_wkBarBg", barBg, "_wkBar", bar, "_wkBtnBg", btnBg, "_wkMark", mark, "_wkIcon", icon,
                "_wkTitle", title, "_wkTime", time, "_wkStep", step, "_wkPct", pct, "_wkLog", log, "_wkBtn", btn, "_wkMarkLabel", markLabel,
                "_wkChip", chip, "_wkChipBg", chipBg, "_wkChipBarBg", chipBarBg, "_wkChipBar", chipBar, "_wkChipLabel", chipLabel,
                "_wkSparks", sparks, "_wkBits", bits, "_wkPop", pop, "_wkPopGlow", popGlow, "_wkKindTex", kinds);

            // ------------------------------------------------------------ the partner at the panel
            float H = PartnerHeight;
            // ------------------------------------------------------------ operate: the ring at the spot, the step, the tag
            var op = Node("Operate", t);
            var ring = Hidden(Fx("Ring", op.transform, 1, new Color(0.98f, 0.62f, 0.30f, 0.95f), new Vector4(0.78f, 0.20f, 0, 0), 8));
            ring.transform.localPosition = new Vector3(0.05f, 0.03f, 0.0006f); ring.transform.localScale = Vector3.one * 0.05f;
            var ringInner = Hidden(Fx("Ring Inner", op.transform, 1, new Color(1f, 0.97f, 0.90f, 0.95f), new Vector4(0.78f, 0.14f, 0, 0), 9));
            ringInner.transform.localPosition = new Vector3(0.05f, 0.03f, 0.0004f); ringInner.transform.localScale = Vector3.one * 0.04f;
            var plate = Hidden(Panel("Step", op.transform, 0.56f * H, 0.058f * H, 0.029f * H, paperTop, new Color(0.97f, 0.90f, 0.78f, 1f), -2, 0.008f * H));
            plate.transform.localPosition = new Vector3(0f, -0.02f, PlanBuilder.ResidentDepth(H) + 0.004f);
            var tagBg = Hidden(Panel("Tag", op.transform, 0.09f, 0.02f, 0.008f, new Color(0.93f, 0.97f, 1f, 0.98f), new Color(0.80f, 0.91f, 0.97f, 0.98f), 6, 0.0011f));
            tagBg.transform.localPosition = new Vector3(0f, 0.09f, 0.0008f);
            var tag = Label("TagLabel", op.transform, "", 0.0108f, ink);
            tag.transform.localPosition = new Vector3(0f, 0.09f, 0.0004f);
            op.SetActive(false);
            Wire(app, "_opRoot", op, "_opRing", ring, "_opRingInner", ringInner, "_opPlate", plate, "_opTagBg", tagBg, "_opTag", tag);

            // ------------------------------------------------------------ the carried file: its thumbnail on the sheet
            var doc = t.Find("DocFile");      // (authored just before, by AuthorPcDocs)
            var frame = Hidden(Panel("ThumbFrame", doc, 0.50f, 0.42f, 0.03f, paperTop, paperBottom, 10, 0.012f));
            frame.transform.localPosition = new Vector3(-0.015f, 0.12f, -0.004f);
            var thumb = HiddenSprite(Sprite("Thumb", doc, "doc-page", 0.44f, 0.36f, 11));
            thumb.transform.localPosition = new Vector3(-0.015f, 0.12f, -0.006f);
            Wire(app, "_dcThumb", thumb, "_dcThumbFrame", frame, "_dcTexPage", Art("doc-page"));
        }
    }
}
