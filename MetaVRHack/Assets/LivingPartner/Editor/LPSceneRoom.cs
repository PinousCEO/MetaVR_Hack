using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.MR;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// The parts of MrCore that were added on 2026-11-17 for "the room answers when it is touched": the ring and the
    /// name strip of a thing that is pressed (MrPoke), the record picker and the notes (MrRecords), the ring and the
    /// ray of the pointing hand (MrGlassHand), the parcel and the dots of the shows (MrShow). Authored like everything
    /// else: real objects under MrCore, wired into serialized fields of MrApp.
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static void AuthorRoom(MrApp app, Transform t, Transform panels, Transform pointing)
        {
            Color ink = new Color(0.29f, 0.18f, 0.15f, 1f), cream = new Color(1f, 0.97f, 0.90f, 1f), honey = new Color(1f, 0.84f, 0.42f, 1f);

            // ---- the pointing hand: a thin soft ray to the display, and on the display a cream ring with a brown outline and a centre dot
            var ptrRay = Hidden(Fx("PointerRay", pointing, 9, new Color(1f, 0.97f, 0.90f, 0.95f), Vector4.zero, 5));
            ptrRay.transform.localScale = new Vector3(0.007f, 0.40f, 1f);
            var cursor = Node("PointerCursor", pointing).transform;
            cursor.localScale = Vector3.one * 0.040f;      // the ring is 4 cm across
            var cursorOutline = Hidden(Fx("Outline", cursor, 1, ink, new Vector4(0.78f, 0.22f, 0, 0), 5, ink));
            var cursorRing = Hidden(Fx("Ring", cursor, 1, cream, new Vector4(0.78f, 0.12f, 0, 0), 6, cream));
            cursorRing.transform.localPosition = new Vector3(0, 0, -0.01f);
            var cursorDot = Hidden(Panel("Dot", cursor, 0.24f, 0.24f, 0.12f, cream, cream, 7, 0.040f));
            cursorDot.transform.localPosition = new Vector3(0, 0, -0.02f);

            // ---- a thing that is pressed: the ring on the desk under the thing a fingertip is near, and its name on a strip of paper
            var poke = Node("Poke", t).transform;
            poke.localPosition = new Vector3(0.22f, 0.80f, 0.35f);       // authoring pose only
            var pokeRing = Hidden(Fx("Ring", poke, 1, new Color(honey.r, honey.g, honey.b, 0.85f), new Vector4(0.86f, 0.075f, 0, 0), 1, new Color(1f, 0.96f, 0.86f, 0.85f)));
            pokeRing.transform.localRotation = Quaternion.Euler(90, 0, 0); pokeRing.transform.localScale = Vector3.one * 0.12f;
            var pokeLabelBg = Hidden(Panel("LabelBg", poke, 0.07f, 0.018f, 0.007f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 10, 0.0010f));
            pokeLabelBg.transform.localPosition = new Vector3(0, 0.09f, 0.002f);
            var pokeLabel = Label("PokeLabel", poke, "", 0.0135f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            pokeLabel.transform.localPosition = new Vector3(0, 0.09f, 0);

            // ---- "Put away": the small button that comes up over a thing that was pressed (MrPoke) - it sends the thing back into the bag
            const float putW = 0.108f, putH = 0.046f;
            var putStrip = MakePanel("PutAway", panels, putW, putH, Ui.Pal.Orange, "", "");
            putStrip.transform.localPosition = new Vector3(0.84f, 0.44f, 0);      // authoring pose only
            Ui.SizePanel(putStrip.bg, putW, putH, putH * 0.5f, 0.0016f);
            Hidden(putStrip.bar);
            var putBtn = AddPill(putStrip, "Btn PutAway", "Put away", Vector3.zero, 0.092f, 0.032f, 0.0150f, Ui.Pal.Violet);

            var records = BuildRecords(panels);

            // ---- notes that rise from the thing that plays the tune: a head, a stem and a flag each
            var notesRoot = Node("Notes", t).transform;
            notesRoot.localPosition = new Vector3(0.32f, 0.90f, 0.35f);  // authoring pose only
            Color[] heads = { Ui.Pal.Pink, Ui.Pal.Cyan, Ui.Pal.Orange, Ui.Pal.Mint };
            var notes = new Transform[4]; var noteParts = new List<FxQuad>();
            for (int i = 0; i < notes.Length; i++)
            {
                notes[i] = Node("Note " + i, notesRoot).transform;
                notes[i].localPosition = new Vector3((i - 1.5f) * 0.02f, i * 0.015f, 0);
                var stem = Hidden(Panel("Stem", notes[i], 0.0014f, 0.0130f, 0.0007f, ink, ink, 14));
                stem.transform.localPosition = new Vector3(0.0011f, 0.0012f, 0);
                var flag = Hidden(Panel("Flag", notes[i], 0.0052f, 0.0020f, 0.0010f, ink, ink, 14));
                flag.transform.localPosition = new Vector3(0.0032f, 0.0066f, 0); flag.transform.localRotation = Quaternion.Euler(0, 0, -28f);
                var head = Hidden(Panel("Head", notes[i], 0.0078f, 0.0060f, 0.0030f, heads[i % heads.Length], heads[i % heads.Length], 15, 0.0010f));
                head.transform.localPosition = new Vector3(-0.0022f, -0.0052f, -0.0002f); head.transform.localRotation = Quaternion.Euler(0, 0, 18f);
                noteParts.Add(stem); noteParts.Add(flag); noteParts.Add(head);
            }

            // ---- shows: the parcel that arrives out of a screen, the dots towards what is being read
            var shows = Node("Shows", t).transform;
            shows.localPosition = new Vector3(-0.2f, 0.85f, 0.35f);      // authoring pose only
            var parcel = Sprite("DeliverParcel", shows, "parcel", 0.068f, 0.068f, 11);
            parcel.enabled = false; parcel.Renderer.enabled = false;
            var dots = new FxQuad[6];
            for (int i = 0; i < dots.Length; i++)
            {
                dots[i] = Hidden(Fx("LookDot " + i, shows, 0, new Color(1f, 0.90f, 0.62f, 0.95f), new Vector4(2.8f, 0, 0, 0), 12, new Color(1f, 0.97f, 0.88f, 0.95f)));
                dots[i].transform.localPosition = new Vector3(0.03f * i, 0.08f, 0); dots[i].transform.localScale = Vector3.one * 0.009f;
            }

            // ---- publish: the market stall on the desk beside the partner (MrShow) - the stall's picture, the thing on its
            // counter, the hammer in the partner's hand, the price tag on its pivot and the ribbon. (The stall is 9 cm
            // tall; the tag and its lettering are in the measure of the PC's stall, which is 11 cm for a partner of 10.)
            const float StallS = 0.092f, StallK = StallS / 0.11f;
            var stallRoot = Node("Stall", shows).transform;
            stallRoot.localPosition = new Vector3(0.14f, 0f, 0f);         // authoring pose only
            var stall = HiddenSprite(Sprite("Stall", stallRoot, "glass-stall", StallS, StallS, 8));
            stall.transform.localPosition = new Vector3(0, StallS * 0.42f, 0.004f);
            var stallItem = HiddenSprite(Sprite("Thing", stallRoot, "parcel", 0.036f * StallK, 0.036f * StallK, 12));
            stallItem.transform.localPosition = new Vector3(0, StallS * 0.45f, -0.006f);
            var stallTagPivot = Node("Tag", stallRoot);
            stallTagPivot.transform.localPosition = new Vector3(0.36f * StallS, 0.60f * StallS, -0.009f); stallTagPivot.transform.localScale = Vector3.one * (0.10f * StallK);
            var stallTag = Sprite("Paper", stallTagPivot.transform, "glass-tag", 0.56f, 0.28f, 14);
            stallTag.transform.localPosition = new Vector3(0, -0.2f, 0);
            var stallPrice = Ordered(Label("Price", stallTagPivot.transform, "15", 0.13f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), 15);
            stallPrice.transform.localPosition = new Vector3(0.105f, -0.2f, -0.001f);
            stallTagPivot.SetActive(false);
            var stallRibbon = HiddenSprite(Sprite("Listed", stallRoot, "glass-listed", 0.10f * StallK, 0.0326f * StallK, 16));
            stallRibbon.transform.localPosition = new Vector3(0, 1.02f * StallS, -0.011f);

            // ---- carrying a thing (MrCarry): under a thing that is turned, a ring of ticks (one every 15 degrees), the
            // arc it has turned through and a dot at its heading; and the ring on the top it would be put down on
            var carry = Node("Carry", t).transform;
            carry.localPosition = new Vector3(0.40f, 0.80f, 0.35f);      // authoring pose only
            Color inkSoft = new Color(ink.r, ink.g, ink.b, 0.80f);
            var carryTicks = Hidden(Fx("Ticks", carry, 7, inkSoft, new Vector4(0.88f, 0.10f, 24f, 0.12f), 2, inkSoft));
            carryTicks.phase = -0.0025f;                                  // (a tick straight ahead: its middle, not its edge)
            carryTicks.transform.localRotation = Quaternion.Euler(90, 0, 0); carryTicks.transform.localScale = Vector3.one * 0.15f;
            var carryArc = Hidden(Fx("Arc", carry, 4, new Color(1f, 0.72f, 0.30f, 0.95f), new Vector4(0.66f, 0.09f, 0.25f, 0), 3, new Color(honey.r, honey.g, honey.b, 0.95f)));
            carryArc.transform.localRotation = Quaternion.Euler(90, 0, 0); carryArc.transform.localScale = Vector3.one * 0.15f;
            var carryDot = Hidden(Fx("Heading", carry, 0, cream, new Vector4(3.6f, 0, 0, 0), 4, cream));
            carryDot.transform.localRotation = Quaternion.Euler(90, 0, 0); carryDot.transform.localScale = Vector3.one * 0.022f;
            carryDot.transform.localPosition = new Vector3(0, 0, 0.05f);
            var landRing = Hidden(Fx("Landing", carry, 1, new Color(honey.r, honey.g, honey.b, 0.9f), new Vector4(0.80f, 0.11f, 0, 0), 2, new Color(1f, 0.96f, 0.86f, 0.9f)));
            landRing.transform.localRotation = Quaternion.Euler(90, 0, 0); landRing.transform.localScale = Vector3.one * 0.08f;
            landRing.transform.localPosition = new Vector3(0.16f, 0.06f, 0);
            // ... and the soft glow on the open other palm while the carried thing is near enough to be let go into the bag there
            var palmGlow = Hidden(Fx("PalmGlow", carry, 8, new Color(honey.r, honey.g, honey.b, 0.7f), new Vector4(2.4f, 0, 0, 0), MenuOrder - 20, new Color(1f, 0.96f, 0.82f, 0.7f)));
            palmGlow.transform.localScale = Vector3.one * 0.13f; palmGlow.transform.localPosition = new Vector3(-0.12f, 0.10f, 0);

            // ---- a new thing arrives (MrArrive): a glow and a pulsing ring on the ground round it, and the arrow at the edge
            // of the view that points to it while it is out of sight (drawn over the boards)
            var arrive = Node("Arrive", t).transform;
            arrive.localPosition = new Vector3(0.58f, 0.80f, 0.35f);     // authoring pose only
            var arriveGlow = Hidden(Fx("Glow", arrive, 8, new Color(1f, 0.90f, 0.58f, 0.42f), new Vector4(2.4f, 0, 0, 0), 1, new Color(1f, 0.97f, 0.86f, 0.42f)));
            arriveGlow.transform.localRotation = Quaternion.Euler(90, 0, 0); arriveGlow.transform.localScale = Vector3.one * 0.14f;
            var arriveRing = Hidden(Fx("Ring", arrive, 1, new Color(1f, 0.80f, 0.36f, 0.95f), new Vector4(0.84f, 0.085f, 0, 0), 2, new Color(1f, 0.96f, 0.86f, 0.95f)));
            arriveRing.transform.localRotation = Quaternion.Euler(90, 0, 0); arriveRing.transform.localScale = Vector3.one * 0.10f;
            var arriveArrow = HiddenSprite(Sprite("Arrow", arrive, "arrow-sticker", 0.042f, 0.042f, MenuOrder + 140));
            arriveArrow.transform.localPosition = new Vector3(0, 0.12f, 0);

            // ---- the painted places (LivingPartner/Ambience): the small noise picture their clouds, waves and rocks are made of
            Wire(t.GetComponentInChildren<AmbienceView>(true), "_noise", AmbienceNoise.Ensure());

            // every panel under MrCore: a fingertip that is on one of them presses a button, not a thing on the desk behind it
            var allPanels = t.GetComponentsInChildren<UiPanel>(true);

            Wire(app, "_ptrRay", ptrRay, "_ptrCursor", cursor, "_ptrCursorParts", new[] { cursorOutline, cursorRing, cursorDot },
                "_pokeRing", pokeRing, "_pokeLabelBg", pokeLabelBg, "_pokeLabel", pokeLabel, "_allPanels", allPanels, "_putStrip", putStrip, "_putBtn", putBtn, "_palmGlow", palmGlow,
                "_records", records, "_notes", notes, "_noteParts", noteParts.ToArray(),
                "_showParcel", parcel, "_showDots", dots);
            Wire(app, "_stallSprite", stall, "_stallItem", stallItem, "_stallRibbon", stallRibbon,
                "_stallTagPivot", stallTagPivot.transform, "_stallPrice", stallPrice, "_stallParcelTex", Art("parcel"),
                "_carryTicks", carryTicks, "_carryArc", carryArc, "_carryDot", carryDot, "_landRing", landRing,
                "_arriveRing", arriveRing, "_arriveGlow", arriveGlow, "_arriveArrow", arriveArrow);
        }

        /// <summary>
        /// The record picker: a small sheet (30 x 22 cm) - "Records", six tiles (three by two) with a vinyl disc and a
        /// name each, earlier / later, "Stop" and a button that puts the sheet away. MrRecords fills and places it.
        /// </summary>
        static RecordsView BuildRecords(Transform panels)
        {
            const float W = 0.30f, H = 0.22f;
            var p = MakePanel("Records", panels, W, H, Ui.Pal.Orange, "", "");
            p.transform.localPosition = new Vector3(0.84f, 0.62f, 0);
            Ui.SizePanel(p.bg, W, H, 0.024f, 0.0020f);
            Hidden(p.bar);
            var root = p.transform;
            var v = p.gameObject.AddComponent<RecordsView>();
            v.panel = p;
            Object.DestroyImmediate(p.title.gameObject);
            p.title = Label("Title", root, "Records", 0.0175f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            p.title.transform.localPosition = new Vector3(0, H * 0.5f - 0.022f, -0.004f);
            v.close = AddPill(p, "Btn Close", "×", new Vector3(W * 0.5f - 0.026f, H * 0.5f - 0.022f, 0), 0.032f, 0.026f, 0.0170f, Rust);

            Color vinylTop = new Color(0.33f, 0.22f, 0.19f, 1f), vinylBottom = new Color(0.22f, 0.14f, 0.12f, 1f), cream = new Color(1f, 0.97f, 0.90f, 1f);
            const float tw = 0.086f, th = 0.062f, disc = 0.034f;
            v.tiles = new UiButton[6]; v.discs = new Transform[6]; v.discLabels = new FxQuad[6];
            for (int i = 0; i < 6; i++)
            {
                var tile = Node("Record " + i, root).transform;
                tile.localPosition = new Vector3((i % 3 - 1) * 0.092f, 0.040f - (i / 3) * 0.068f, 0);
                var b = tile.gameObject.AddComponent<UiButton>();
                b.radius = 0.034f; b.color = Ui.Pal.PanelTop; b.color2 = Ui.Pal.PanelBottom;
                b.half = new Vector2(tw * 0.5f, th * 0.5f);      // the whole tile is the button
                b.bg = Panel("Paper", tile, tw, th, 0.012f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 2, 0.0014f);
                b.bg.transform.localPosition = new Vector3(0, 0, -0.0015f);
                // the vinyl disc: dark, a groove, a paper label in the record's own colour, the hole - and a glint, so that it is seen to turn
                var d = Node("Disc", tile).transform;
                d.localPosition = new Vector3(0, 0.010f, -0.0030f);
                Panel("Vinyl", d, disc, disc, disc * 0.5f, vinylTop, vinylBottom, 3, 0.0012f);
                var groove = Fx("Groove", d, 1, new Color(0.55f, 0.42f, 0.36f, 0.55f), new Vector4(0.70f, 0.035f, 0, 0), 4, new Color(0.55f, 0.42f, 0.36f, 0.55f));
                groove.transform.localScale = Vector3.one * disc; groove.transform.localPosition = new Vector3(0, 0, -0.0003f);
                var glint = Fx("Glint", d, 0, new Color(1f, 1f, 1f, 0.55f), new Vector4(1.5f, 0, 0, 0), 5);
                glint.transform.localScale = new Vector3(0.0075f, 0.0045f, 1f); glint.transform.localPosition = new Vector3(0.0085f, 0.0070f, -0.0005f);
                glint.transform.localRotation = Quaternion.Euler(0, 0, -40f);
                v.discLabels[i] = Panel("Label", d, 0.0150f, 0.0150f, 0.0075f, Ui.Pal.Pink, Ui.Pal.Pink, 5, 0.0008f);
                v.discLabels[i].transform.localPosition = new Vector3(0, 0, -0.0006f);
                var hole = Panel("Hole", d, 0.0034f, 0.0034f, 0.0017f, cream, cream, 6);
                hole.transform.localPosition = new Vector3(0, 0, -0.0009f);
                v.discs[i] = d;
                b.label = Label("Name", tile, "Record", 0.0120f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                b.label.transform.localPosition = new Vector3(0, -0.0210f, -0.004f);
                p.buttons = new List<UiButton>(p.buttons) { b }.ToArray();
                v.tiles[i] = b;
            }
            float by = -H * 0.5f + 0.022f;
            v.prev = AddPill(p, "Btn Prev", "<", new Vector3(-0.122f, by, 0), 0.032f, 0.026f, 0.0160f, Purple);
            v.pageLabel = Label("Page", root, "1 / 2", 0.0130f, Ui.Pal.TextDim, TextAnchor.MiddleCenter, true);
            v.pageLabel.transform.localPosition = new Vector3(-0.082f, by, -0.003f);
            v.next = AddPill(p, "Btn Next", ">", new Vector3(-0.042f, by, 0), 0.032f, 0.026f, 0.0160f, Purple);
            v.stop = AddPill(p, "Btn Stop", "Stop", new Vector3(0.084f, by, 0), 0.090f, 0.028f, 0.0140f, Rust);
            return v;
        }
    }
}
