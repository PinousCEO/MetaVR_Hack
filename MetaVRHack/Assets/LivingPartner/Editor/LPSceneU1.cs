using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.MR;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// What agent U1 authors in MrCore (2026-11-18):
    ///  - AuthorU1Menu: the menu is a CAROUSEL again (MrMenuBoard, MenuBoardView): the two round arrows that turn the
    ///    ring leave the header and stand under the ring, each with the arrow sticker; the ribbon and the line of words
    ///    stand over the ring; the pictures of the folders.
    ///  - AuthorU1Work: the work queue's strip (MrWorkStrip, WorkStripView): a honey handle and three paper cards.
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static void AuthorU1Menu(MenuBoardView v)
        {
            Transform root = v.panel.transform;
            // the ribbon (with the stars and the round button beside it) and the line of words: over the ring
            v.header.localPosition = new Vector3(0f, MenuBoardView.CarHeaderY, 0f);
            v.statusBg.transform.localPosition = new Vector3(0f, MenuBoardView.CarStatusY, 0.001f);
            v.status.transform.localPosition = new Vector3(0f, MenuBoardView.CarStatusY, -0.002f);
            // the arrows: under the ring, one at each side of the big sticker - 3.4 cm, each with the arrow sticker on it
            const float size = 0.034f;
            v.texArrow = Art("arrow-sticker"); v.texPlaces = Art("tile-kind-ambience");
            for (int i = 0; i < 2; i++)
            {
                var b = i == 0 ? v.prev : v.next;
                b.transform.SetParent(root, false);
                b.transform.localPosition = new Vector3((i == 0 ? -1f : 1f) * MenuBoardView.ArrowX, MenuBoardView.ArrowY, MenuBoardView.RingFrontZ - 0.006f);
                b.transform.localRotation = Quaternion.identity;
                b.radius = size * 0.62f; b.half = Vector2.zero; b.hoverGrow = 0.12f;
                b.label.text = "";
                // (its pill is only a soft glow behind the sticker now)
                Color glow = new Color(1f, 0.90f, 0.62f, 0.0f);
                b.color = glow; b.color2 = glow;
                b.bg.mode = 8; b.bg.param = new Vector4(3.4f, 0, 0, 0); b.bg.SetColors(glow, glow);
                b.bg.transform.localScale = Vector3.one * (size * 1.5f);
                var art = Sprite("Arrow", b.transform, "arrow-sticker", size, size, 6 + MenuOrder);
                art.transform.localPosition = new Vector3(0f, 0f, -0.003f);
                art.transform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 180f : 0f);
                b.art = art;
                if (i == 0) v.prevArt = art; else v.nextArt = art;
                b.bg.Apply();
            }
        }

        static void AuthorU1Work(MrApp app, Transform t, Transform panels)
        {
            const float W = WorkStripView.W, HandleH = WorkStripView.HandleH, RowH = WorkStripView.RowH, Pad = WorkStripView.Pad, RW = W - 2f * Pad;
            float H = WorkStripView.Height(WorkStripView.Rows);
            Color honeyTop = new Color(1f, 0.90f, 0.62f, 0.98f), honeyBottom = new Color(1f, 0.84f, 0.50f, 0.98f), honey = new Color(1f, 0.80f, 0.36f, 1f);
            Color grip = new Color(0.29f, 0.18f, 0.15f, 0.38f), paper = new Color(1f, 0.985f, 0.945f, 0.98f), paperDeep = new Color(0.985f, 0.945f, 0.875f, 0.98f);

            var p = MakePanel("WorkStrip", panels, W, H, Ui.Pal.Orange, "", "");
            p.transform.localPosition = new Vector3(1.75f, 0.20f, 0);      // authoring pose only
            Ui.SizePanel(p.bg, W, H, 0.016f, 0.0018f);
            p.bg.SetColors(new Color(0.99f, 0.955f, 0.89f, 0.98f), new Color(0.97f, 0.92f, 0.83f, 0.98f));      // (the sheet the cards lie on: a shade deeper than the cards)
            Hidden(p.bar);
            var root = p.transform;
            var v = root.gameObject.AddComponent<WorkStripView>();
            v.panel = p;

            // ---- the handle: a honey bar with grip lines at both ends; it is taken here
            v.top = Node("Top", root).transform;
            v.top.localPosition = new Vector3(0f, H * 0.5f - Pad - HandleH * 0.5f, 0f);
            v.handle = Panel("Handle", v.top, RW, HandleH, HandleH * 0.5f, honeyTop, honeyBottom, 2, 0.0014f);
            v.handle.transform.localPosition = new Vector3(0, 0, -0.0015f);
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++)
                {
                    var g = Panel("Grip " + side + " " + i, v.top, 0.0026f, 0.0120f, 0.0013f, grip, grip, 3);
                    g.transform.localPosition = new Vector3(side * (RW * 0.5f - 0.016f - i * 0.0052f), 0f, -0.0025f);
                }
            v.title = Label("Title", v.top, "Nilo's work", 0.0135f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.title.transform.localPosition = new Vector3(0f, 0.0004f, -0.003f);
            v.more = Label("More", v.top, "", 0.0098f, Ui.Pal.TextDim, TextAnchor.MiddleRight);
            v.more.transform.localPosition = new Vector3(RW * 0.5f - 0.036f, 0f, -0.003f);

            // ---- the cards
            int n = WorkStripView.Rows;
            v.rows = new Transform[n]; v.rowBtn = new UiButton[n]; v.cancel = new UiButton[n];
            v.rowTitle = new StoryLabel[n]; v.rowStep = new StoryLabel[n]; v.rowWhere = new StoryLabel[n]; v.rowPct = new StoryLabel[n];
            v.rowWhereBg = new FxQuad[n]; v.rowBarBg = new FxQuad[n]; v.rowBar = new FxQuad[n]; v.rowMark = new FxQuad[n]; v.rowTick = new GameObject[n];
            var cancels = new List<UiButton>(); var cards = new List<UiButton>();
            float left = -RW * 0.5f + 0.009f;
            for (int r = 0; r < n; r++)
            {
                var row = Node("Card " + r, root).transform;
                row.localPosition = new Vector3(0f, H * 0.5f - Pad - HandleH - WorkStripView.Gap - RowH * 0.5f - r * (RowH + WorkStripView.Gap), 0f);
                v.rows[r] = row;
                var b = row.gameObject.AddComponent<UiButton>();
                b.radius = RowH * 0.5f; b.half = new Vector2(RW * 0.5f, RowH * 0.5f); b.hoverGrow = 0f;
                b.color = paper; b.color2 = paperDeep;
                b.bg = Panel("Paper", row, RW, RowH, 0.011f, paper, paperDeep, 1, 0.0012f);
                b.bg.transform.localPosition = new Vector3(0, 0, -0.0012f);
                // title (left), where it is worked (a chip at the right), the round button that cancels it (far right)
                v.rowTitle[r] = Label("Title", row, "New icon", 0.0128f, Ui.Pal.Ink, TextAnchor.MiddleLeft, true);
                v.rowTitle[r].transform.localPosition = new Vector3(left, 0.0150f, -0.003f);
                b.label = v.rowTitle[r];
                v.rowWhereBg[r] = Panel("WhereBg", row, 0.060f, 0.0128f, 0.0064f, new Color(0.80f, 0.92f, 0.79f, 1f), new Color(0.80f, 0.92f, 0.79f, 1f), 2);
                v.rowWhereBg[r].transform.localPosition = new Vector3(0.045f, 0.0150f, -0.0020f);
                v.rowWhere[r] = Label("Where", row, "in the room", 0.0092f, Ui.Pal.Ink, TextAnchor.MiddleCenter);
                v.rowWhere[r].transform.localPosition = new Vector3(0.045f, 0.0152f, -0.003f);
                // what is being done, in one line
                v.rowStep[r] = Label("Step", row, "", 0.0105f, Ui.Pal.TextDim, TextAnchor.MiddleLeft);
                v.rowStep[r].transform.localPosition = new Vector3(left, -0.0010f, -0.003f);
                // the bar and the percentage
                float barX = left + WorkStripView.BarW * 0.5f;
                v.rowBarBg[r] = Panel("BarBg", row, WorkStripView.BarW, WorkStripView.BarH, WorkStripView.BarH * 0.5f, new Color(0.93f, 0.88f, 0.80f, 1f), new Color(0.90f, 0.85f, 0.77f, 1f), 2);
                v.rowBarBg[r].transform.localPosition = new Vector3(barX, -0.0165f, -0.0020f);
                v.rowBar[r] = Panel("Bar", row, WorkStripView.BarW * 0.5f, WorkStripView.BarH, WorkStripView.BarH * 0.5f, honey, new Color(1f, 0.70f, 0.30f, 1f), 3);
                v.rowBar[r].transform.localPosition = new Vector3(barX, -0.0165f, -0.0028f);
                v.rowPct[r] = Label("Percent", row, "50%", 0.0128f, Ui.Pal.Ink, TextAnchor.MiddleRight, true);
                v.rowPct[r].transform.localPosition = new Vector3(RW * 0.5f - 0.009f, -0.0162f, -0.003f);
                // the mark of a finished one: a round dot at the right of its second line, with a tick when it worked out
                v.rowMark[r] = Hidden(Panel("Mark", row, 0.0200f, 0.0200f, 0.0100f, new Color(0.50f, 0.78f, 0.50f, 1f), new Color(0.50f, 0.78f, 0.50f, 1f), 2));
                v.rowMark[r].transform.localPosition = new Vector3(RW * 0.5f - 0.0185f, -0.0095f, -0.0020f);
                var tick = Node("Tick", row); tick.transform.localPosition = new Vector3(RW * 0.5f - 0.0185f, -0.0095f, -0.0030f);
                Color white = new Color(1f, 1f, 0.98f, 1f);
                var s1 = Panel("Short", tick.transform, 0.0064f, 0.0027f, 0.00135f, white, white, 3);
                s1.transform.localPosition = new Vector3(-0.0040f, -0.0012f, 0f); s1.transform.localRotation = Quaternion.Euler(0, 0, -45f);
                var s2 = Panel("Long", tick.transform, 0.0112f, 0.0027f, 0.00135f, white, white, 3);
                s2.transform.localPosition = new Vector3(0.0018f, 0.0006f, 0f); s2.transform.localRotation = Quaternion.Euler(0, 0, 50f);
                tick.SetActive(false);
                v.rowTick[r] = tick;
                // cancel: a small round rose button with a cross (pressed like every button: a poke through its face)
                var c = AddPill(p, "Cancel " + r, "×", Vector3.zero, 0.0185f, 0.0185f, 0.0150f, Ui.Pal.Pink);
                c.transform.SetParent(row, false);
                c.transform.localPosition = new Vector3(RW * 0.5f - 0.0145f, 0.0150f, -0.0012f);
                c.radius = 0.0125f; c.half = Vector2.zero; c.hoverGrow = 0.12f;
                v.cancel[r] = c; cancels.Add(c);
                v.rowBtn[r] = b; cards.Add(b);
            }
            // (a finger at a cancel button presses that, not the card under it: the buttons are asked in this order)
            cancels.AddRange(cards);
            p.buttons = cancels.ToArray();
            Wire(app, "_work", v);
            AuthorU1DocSheet(t);
        }

        /// <summary>
        /// The sheet the partner carries into the PC shows the file it really is (MrDocs.UpdateDocSheet): the picture of
        /// its first page on a paper edge, its name on a strip under it; the drawn PDF / picture icon becomes the badge
        /// of its kind at the corner. (The icon itself is authored by AuthorDocs: LPSceneDocs.cs.)
        /// </summary>
        static void AuthorU1DocSheet(Transform t)
        {
            var v = t.GetComponentInChildren<DocBoardView>(true);
            if (v == null) throw new System.Exception("AuthorU1DocSheet: AuthorDocs has to run first (no DocBoardView)");
            Transform holder = v.transform;
            v.sheetPaper = Hidden(Panel("DocSheetPaper", holder, 0.058f, 0.080f, 0.003f, new Color(1f, 0.995f, 0.975f, 1f), new Color(0.97f, 0.95f, 0.91f, 1f), 9, 0.0010f));
            v.sheetPic = HiddenSprite(Sprite("DocSheetPicture", holder, "doc-page", 0.052f, 0.074f, 10));
            v.sheetNameBg = Hidden(Panel("DocSheetNameBg", holder, 0.070f, 0.0135f, 0.0060f, new Color(1f, 0.975f, 0.91f, 0.98f), new Color(0.99f, 0.93f, 0.80f, 0.98f), 11, 0.0009f));
            v.sheetName = Label("DocSheetName", holder, "", 0.0098f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.sheet.order = 12; v.sheet.Apply();      // (the badge lies on the picture)
        }
    }
}
