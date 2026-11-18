using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.MR;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// The parts of MrCore that the partner's DOCUMENTS need (MR/MrDocs.cs, MR/DocBoardView.cs):
    ///  - DocReport: an upright sheet, 35 x 51 cm, with one page of a report as a picture (29.7 x 42 cm: A4 at 1.41 times
    ///    its size, so that body text can be read at half a metre), a top strip with the title that it is moved by,
    ///    earlier / later with the page number, "Send to PC", and a round button that puts it away;
    ///  - DocDesigns: a wider sheet, 56 x 36 cm: the original on the left ("Now"), four options as cards, "Send to PC";
    ///  - DocStrip: the small strip of paper beside the partner while it is making a document;
    ///  - DocSheet: the sheet it holds over its head on its way into the screen.
    /// Authored like every other board: real objects under MrCore/Panels, wired into MrApp._docs.
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static void AuthorDocs(MrApp app, Transform t, Transform panels)
        {
            var holder = Node("Docs", t);
            holder.transform.localPosition = new Vector3(1.30f, 1.25f, 0.5f);      // authoring pose only
            var v = holder.AddComponent<DocBoardView>();
            Color honeyTop = new Color(1f, 0.90f, 0.62f, 0.98f), honeyBottom = new Color(1f, 0.84f, 0.50f, 0.98f), honey = new Color(1f, 0.80f, 0.36f, 1f);
            Color paper = new Color(1f, 0.995f, 0.975f, 1f), edge = new Color(0.86f, 0.80f, 0.72f, 1f), grip = new Color(0.29f, 0.18f, 0.15f, 0.38f);

            // ------------------------------------------------------------ the report board
            {
                const float W = 0.35f, H = 0.51f, barH = 0.030f, pageW = 0.297f, pageH = 0.420f;
                var p = MakePanel("DocReport", panels, W, H, Ui.Pal.Orange, "", "");
                p.transform.localPosition = new Vector3(1.30f, 0.20f, 0);      // authoring pose only
                Ui.SizePanel(p.bg, W, H, 0.022f, 0.0020f);
                Hidden(p.bar);
                var root = p.transform;
                float barY = H * 0.5f - 0.005f - barH * 0.5f;
                v.reportBar = Panel("TopBar", root, W - 0.012f, barH, barH * 0.5f, honeyTop, honeyBottom, 2, 0.0014f);
                v.reportBar.transform.localPosition = new Vector3(0, barY, -0.0015f);
                for (int i = 0; i < 2; i++)
                {
                    var g = Panel("Grip " + i, root, 0.020f, 0.0026f, 0.0013f, grip, grip, 3);
                    g.transform.localPosition = new Vector3(-W * 0.5f + 0.028f, barY + (i == 0 ? 0.0034f : -0.0034f), -0.0025f);
                }
                v.reportTitle = Label("DocTitle", root, "Report", 0.0150f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                v.reportTitle.transform.localPosition = new Vector3(-0.004f, barY, -0.004f);
                v.close = AddPill(p, "Btn Close", "×", new Vector3(W * 0.5f - 0.028f, barY, -0.002f), 0.036f, 0.024f, 0.0170f, Rust);

                float pageY = barY - barH * 0.5f - 0.005f - pageH * 0.5f;
                v.pageEdge = Panel("PageEdge", root, pageW + 0.004f, pageH + 0.004f, 0.003f, edge, edge, 2);
                v.pageEdge.transform.localPosition = new Vector3(0, pageY, -0.0015f);
                v.page = Sprite("Page", root, "doc-page", pageW, pageH, 6);
                v.page.transform.localPosition = new Vector3(0, pageY, -0.003f);
                v.reportNote = Label("PageNote", root, "", 0.0135f, Ui.Pal.TextDim, TextAnchor.MiddleCenter, true);
                v.reportNote.transform.localPosition = new Vector3(0, pageY, -0.005f);

                float by = -H * 0.5f + 0.005f + 0.016f;
                v.prev = AddPill(p, "Btn Prev", "<", new Vector3(-0.139f, by, 0), 0.054f, 0.032f, 0.0200f, Purple);
                v.reportPage = Label("PageNumber", root, "1 / 1", 0.0150f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                v.reportPage.transform.localPosition = new Vector3(-0.079f, by, -0.003f);
                v.next = AddPill(p, "Btn Next", ">", new Vector3(-0.019f, by, 0), 0.054f, 0.032f, 0.0200f, Purple);
                v.send = AddPill(p, "Btn Send", "Send to PC", new Vector3(0.098f, by, 0), 0.136f, 0.032f, 0.0145f, Green);
                v.report = p; v.pageW = pageW; v.pageH = pageH; v.pageY = pageY; v.barH = barH;
            }

            // ------------------------------------------------------------ the designs board
            {
                const float W = 0.56f, H = 0.36f, barH = 0.030f;
                var p = MakePanel("DocDesigns", panels, W, H, Ui.Pal.Orange, "", "");
                p.transform.localPosition = new Vector3(1.80f, 0.20f, 0);      // authoring pose only
                Ui.SizePanel(p.bg, W, H, 0.022f, 0.0020f);
                Hidden(p.bar);
                var root = p.transform;
                float barY = H * 0.5f - 0.005f - barH * 0.5f;
                v.designsBar = Panel("TopBar", root, W - 0.012f, barH, barH * 0.5f, honeyTop, honeyBottom, 2, 0.0014f);
                v.designsBar.transform.localPosition = new Vector3(0, barY, -0.0015f);
                for (int i = 0; i < 2; i++)
                {
                    var g = Panel("Grip " + i, root, 0.020f, 0.0026f, 0.0013f, grip, grip, 3);
                    g.transform.localPosition = new Vector3(-W * 0.5f + 0.028f, barY + (i == 0 ? 0.0034f : -0.0034f), -0.0025f);
                }
                v.designsTitle = Label("DocTitle", root, "New designs", 0.0150f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                v.designsTitle.transform.localPosition = new Vector3(-0.004f, barY, -0.004f);
                v.designsClose = AddPill(p, "Btn Close", "×", new Vector3(W * 0.5f - 0.028f, barY, -0.002f), 0.036f, 0.024f, 0.0170f, Rust);

                // on the left: what it is now, small
                const float ox = -0.220f, oy = 0.030f;
                var nowBg = Panel("NowBg", root, 0.090f, 0.128f, 0.012f, new Color(0.96f, 0.93f, 0.87f, 0.98f), new Color(0.93f, 0.89f, 0.82f, 0.98f), 1, 0.0012f);
                nowBg.transform.localPosition = new Vector3(ox, oy - 0.010f, -0.001f);
                v.original = Sprite("Original", root, "item-blank", 0.076f, 0.076f, 6);
                v.original.transform.localPosition = new Vector3(ox, oy + 0.010f, -0.003f);
                var now = Label("Now", root, "Now", 0.0130f, Ui.Pal.TextDim, TextAnchor.MiddleCenter, true);
                now.transform.localPosition = new Vector3(ox, oy - 0.054f, -0.004f);
                var arrow = Label("Arrow", root, "→", 0.0240f, Ui.Pal.TextDim, TextAnchor.MiddleCenter, true);
                arrow.transform.localPosition = new Vector3(ox + 0.062f, oy - 0.004f, -0.004f);

                // the options: four cards, the whole card is the button
                const float cw = 0.094f, ch = 0.172f, art = 0.082f;
                v.cards = new UiButton[4]; v.cardPicked = new GameObject[4];
                for (int i = 0; i < 4; i++)
                {
                    var c = Node("Option " + i, root).transform;
                    c.localPosition = new Vector3(-0.088f + i * 0.100f, 0.010f, 0);
                    // the mark of the picked one: a honey frame round the card and a tick on its corner
                    var mark = Node("Picked", c);
                    var frame = Panel("Frame", mark.transform, cw + 0.010f, ch + 0.010f, 0.018f, honey, honey, 1);
                    frame.transform.localPosition = new Vector3(0, 0, -0.0008f);
                    var tickBg = Panel("TickBg", mark.transform, 0.026f, 0.026f, 0.013f, honey, honey, 8, 0.0016f);
                    tickBg.transform.localPosition = new Vector3(cw * 0.5f - 0.010f, ch * 0.5f - 0.010f, -0.0050f);
                    var tick = Label("Tick", mark.transform, "✓", 0.0190f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                    tick.transform.localPosition = new Vector3(cw * 0.5f - 0.010f, ch * 0.5f - 0.010f, -0.0060f);
                    mark.SetActive(false);
                    v.cardPicked[i] = mark;
                    var b = c.gameObject.AddComponent<UiButton>();
                    b.radius = cw * 0.5f; b.color = Ui.Pal.PanelTop; b.color2 = Ui.Pal.PanelBottom; b.hoverGrow = 0.05f;
                    b.half = new Vector2(cw * 0.5f, ch * 0.5f);
                    b.bg = Panel("Paper", c, cw, ch, 0.014f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 2, 0.0016f);
                    b.bg.transform.localPosition = new Vector3(0, 0, -0.0015f);
                    b.art = Sprite("Art", c, "item-blank", art, art, 6);
                    b.art.transform.localPosition = new Vector3(0, ch * 0.5f - 0.008f - art * 0.5f, -0.0035f);
                    b.label = Label("Name", c, "Option " + (i + 1), 0.0124f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                    b.label.transform.localPosition = new Vector3(0, -0.042f, -0.004f);
                    p.buttons = new List<UiButton>(p.buttons) { b }.ToArray();
                    v.cards[i] = b;
                }
                float by = -H * 0.5f + 0.005f + 0.018f;
                v.designsNote = Label("Note", root, "Touch the one you like.", 0.0140f, Ui.Pal.TextDim, TextAnchor.MiddleLeft, true);
                v.designsNote.transform.localPosition = new Vector3(-W * 0.5f + 0.022f, by, -0.003f);
                v.designsSend = AddPill(p, "Btn Send", "Send to PC", new Vector3(W * 0.5f - 0.086f, by, 0), 0.136f, 0.032f, 0.0145f, Green);
                v.designs = p;
            }

            // ------------------------------------------------------------ the strip beside the partner while it works
            {
                const float W = 0.210f, H = 0.066f;
                var p = MakePanel("DocStrip", panels, W, H, Ui.Pal.Orange, "", "");
                p.transform.localPosition = new Vector3(1.30f, -0.14f, 0);      // authoring pose only
                Ui.SizePanel(p.bg, W, H, 0.014f, 0.0016f);
                Hidden(p.bar);
                var root = p.transform;
                v.stripIcon = Sprite("Icon", root, "doc-pdf", 0.056f, 0.056f, 6);
                v.stripIcon.transform.localPosition = new Vector3(-W * 0.5f + 0.032f, 0, -0.003f);
                const float tx = 0.027f, tw = 0.144f;
                v.stripTitle = Label("StripTitle", root, "Working on it", 0.0148f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                v.stripTitle.transform.localPosition = new Vector3(tx, 0.0165f, -0.003f);
                v.stripStage = Label("StripStage", root, "", 0.0120f, Ui.Pal.Text, TextAnchor.MiddleCenter);
                v.stripStage.transform.localPosition = new Vector3(tx, 0.0008f, -0.003f);
                v.stripBarBg = Panel("BarBg", root, tw, 0.0070f, 0.0035f, new Color(0.93f, 0.88f, 0.80f, 1f), new Color(0.90f, 0.85f, 0.77f, 1f), 2);
                v.stripBarBg.transform.localPosition = new Vector3(tx, -0.0180f, -0.0015f);
                v.stripBar = Panel("Bar", root, tw, 0.0070f, 0.0035f, honey, new Color(1f, 0.70f, 0.30f, 1f), 3);
                v.stripBar.transform.localPosition = new Vector3(tx, -0.0180f, -0.0025f);
                v.strip = p; v.stripBarW = tw;
            }

            // ------------------------------------------------------------ the sheet it carries over its head
            v.sheet = HiddenSprite(Sprite("DocSheet", holder.transform, "doc-pdf", 0.062f, 0.062f, 11));

            v.texPdf = Art("doc-pdf"); v.texPic = Art("doc-pic"); v.texBlankPage = Art("doc-page"); v.texBlankCard = Art("item-blank");
            Wire(app, "_docs", v);
        }
    }
}
