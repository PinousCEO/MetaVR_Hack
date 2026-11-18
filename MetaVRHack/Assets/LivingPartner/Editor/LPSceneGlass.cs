using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.MR;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// MrCore/GlassStage: a glass game played OUT on the desk (MrGlass.cs, Shared/Glass.cs). A field through the
    /// partner's spot - 16:9; standing, as high as five partners (the partner is 0.20 of the field's height), or, for
    /// a flat game, lying on the desk - with a pool of 160 pictures and 32 words (what a scripted game may ask for at
    /// once), the 3 cards of a choice, a ring for where each hand is, the sign above the field, the banner and the
    /// result card; and a small sign for the screen a game is played in while the partner is inside the PC. Authored
    /// like everything else: real objects, hidden, wired into GlassStageView. Laid out the way the PC client lays the
    /// same games out inside a monitor (LPScenePc.cs), so that a game looks the same on both sides of the glass.
    /// Draw order (B = 400): the floor B, the veil B + 1, the pictures B + 12 .. B + 171, the words B + 180 / 181, the
    /// cards B + 190 .. 193, the hands B + 198 .. 200, the banner B + 204 / 205, the sign B + 210 .. 213, the result
    /// B + 220 / 222, the sign of a game inside a screen B + 230 / 231.
    /// </summary>
    public static partial class LPSceneBuilder
    {
        /// <summary>The draw order of the stage: above the boards (the menu is at 200, its lettering at 300).</summary>
        const int GlassStageOrder = 400;

        /// <summary>The picture of a GlassArt name: Art/Ui/glass-&lt;name&gt;.png (a label is words: it has no picture and takes the star's).</summary>
        static Texture2D GlassPicture(string n) => Art("glass-" + (n == GlassArt.Label ? GlassArt.Star : n));

        static void AuthorGlass(MrApp app, Transform t)
        {
            const float U = PartnerHeight / GlassGames.PetHeight;      // metres for one unit of a game's field (the field is one unit high)
            const float W = 16f / 9f;
            const int B = GlassStageOrder;
            Color cream = new Color(1f, 0.97f, 0.90f, 1f), honey = new Color(1f, 0.84f, 0.42f, 1f);

            UiSprite Off(UiSprite s) { s.enabled = false; s.Renderer.enabled = false; return s; }
            StoryLabel Ord(StoryLabel l, int order) { l.GetComponent<MeshRenderer>().sortingOrder = order; return l; }

            var root = Node("GlassStage", t);
            var v = root.AddComponent<GlassStageView>();
            var stage = Node("Stage", root.transform);
            stage.transform.localPosition = new Vector3(0, 0.78f, 0.45f);        // authoring pose only: the app stands it on the desk
            Transform s = stage.transform;
            v.stage = stage;

            // the foot of the field: a strip of paper on the desk; and a faint sheet behind the field
            v.floor = Hidden(Panel("Floor", s, W * U + 0.03f, 0.034f, 0.017f, new Color(1f, 0.985f, 0.945f, 0.62f), new Color(1f, 0.93f, 0.72f, 0.62f), B, 0.0012f));
            v.floor.transform.localPosition = new Vector3(0, 0.0012f, 0.004f); v.floor.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Color paper = new Color(1f, 0.973f, 0.918f, 0.07f);
            v.veil = Hidden(Panel("Veil", s, W * U, U, 0.03f, paper, paper, B + 1));
            v.veil.transform.localPosition = new Vector3(0, 0.5f * U, 0.07f);

            var spritesRoot = Node("Sprites", s).transform;
            v.footballs = FootballPool(spritesRoot);
            v.sprites = new UiSprite[160];
            for (int i = 0; i < v.sprites.Length; i++)
            {
                v.sprites[i] = Off(Sprite("Sprite " + i, spritesRoot, "glass-star", 0.08f * U, 0.08f * U, B + 12 + i));
                v.sprites[i].transform.localPosition = new Vector3(((i % 16) - 7.5f) * 0.1f * U, (0.95f - (i / 16) * 0.1f) * U, -0.002f);
            }
            var labelsRoot = Node("Labels", s).transform;
            v.labels = new StoryLabel[32]; v.labelBgs = new FxQuad[32];
            for (int i = 0; i < v.labels.Length; i++)
            {
                v.labelBgs[i] = Hidden(Panel("Label Paper " + i, labelsRoot, 0.1f * U, 0.07f * U, 0.035f * U, Ui.Pal.PanelTop, Ui.Pal.Orange, B + 180, 0.005f * U));
                v.labels[i] = Ord(Label("Label " + i, labelsRoot, "", 1f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), B + 181);
                v.labels[i].transform.localScale = Vector3.one * 0.07f * U;
            }

            // the three cards of the survival game's choice (one unit = the height of the card's paper)
            var cardsRoot = Node("Cards", s).transform;
            v.cards = new Transform[3]; v.cardBg = new UiSprite[3]; v.cardIcon = new UiSprite[3]; v.cardRing = new FxQuad[3];
            v.cardName = new StoryLabel[3]; v.cardNote = new StoryLabel[3];
            for (int i = 0; i < 3; i++)
            {
                var c = Node("Card " + i, cardsRoot);
                v.cards[i] = c.transform;
                c.transform.localPosition = new Vector3((new[] { 0.22f, 0.5f, 0.78f }[i] - 0.5f) * W * U, GlassSurvival.CardY * U, -0.030f - 0.002f * i);
                c.transform.localScale = Vector3.one * GlassSurvival.CardSize * U;
                v.cardBg[i] = Sprite("Paper", c.transform, "glass-card", GlassSurvival.CardAspect * 216f / 188f, 300f / 272f, B + 190);
                v.cardBg[i].transform.localPosition = new Vector3(0, -0.015f, 0);
                v.cardRing[i] = Fx("Hold Ring", c.transform, 4, new Color(1f, 0.69f, 0.42f, 1f), new Vector4(0.86f, 0.12f, 0.35f, 0), B + 191, new Color(0.96f, 0.45f, 0.42f, 1f));
                v.cardRing[i].transform.localPosition = new Vector3(0, 0.28f, -0.004f); v.cardRing[i].transform.localScale = Vector3.one * 0.45f;
                v.cardIcon[i] = Sprite("Power", c.transform, "glass-ab-" + GlassSurvival.AbilityIds[i], 0.29f, 0.29f, B + 192);
                v.cardIcon[i].transform.localPosition = new Vector3(0, 0.28f, -0.008f);
                v.cardName[i] = Ord(Label("Name", c.transform, GlassSurvival.AbilityNames[i], 0.108f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), B + 193);      // (larger than inside a monitor: read from further away)
                v.cardName[i].transform.localPosition = new Vector3(0, -0.065f, -0.012f);
                v.cardNote[i] = Ord(Label("Note", c.transform, "", 0.084f, Ui.Pal.Ink), B + 193);      // (0.066 in dim ink could not be read on a card 17 cm tall)
                v.cardNote[i].transform.localPosition = new Vector3(0, -0.305f, -0.012f);
                c.SetActive(false);
            }

            // what a hand reaches on the field: a soft ring with a faint fill (nothing is drawn on the hand itself), and
            // the hand of the bot that plays for captures and checks without a headset
            var hands = Node("Hands", s).transform;
            v.handRing = new FxQuad[2]; v.handFill = new FxQuad[2];
            for (int i = 0; i < 2; i++)
            {
                v.handFill[i] = Hidden(Fx(i == 0 ? "Reach Fill R" : "Reach Fill L", hands, 0, new Color(1f, 0.97f, 0.88f, 0.16f), new Vector4(4.2f, 0, 0, 0), B + 198));
                v.handRing[i] = Hidden(Fx(i == 0 ? "Reach R" : "Reach L", hands, 1, new Color(honey.r, honey.g, honey.b, 0.9f), new Vector4(0.90f, 0.055f, 0, 0), B + 199, new Color(cream.r, cream.g, cream.b, 0.9f)));
                v.handFill[i].transform.localPosition = v.handRing[i].transform.localPosition = new Vector3((i == 0 ? 0.2f : -0.2f), 0.3f * U, -0.02f);
                v.handFill[i].transform.localScale = v.handRing[i].transform.localScale = Vector3.one * GlassGames.HandRadiusBase * U * 2f / 0.90f;
            }
            v.botHand = Off(Sprite("Bot Hand", hands, "glass-hand", GlassGames.HandRadiusBase * U / 0.36f, GlassGames.HandRadiusBase * U / 0.36f, B + 200));
            v.botHand.color = new Color(1f, 0.90f, 0.80f, 0.85f);

            // where a hand AIMS at the field from a distance: a small ring on the field (as the pointer's ring on a display) and
            // a thin ray from the hand (MrGlassOut.cs: GlassAim)
            Color aimInk = new Color(0.29f, 0.18f, 0.15f, 1f);
            v.aimCursor = new Transform[2]; v.aimParts = new FxQuad[6]; v.aimRay = new FxQuad[2];
            for (int i = 0; i < 2; i++)
            {
                var c = Node(i == 0 ? "Aim R" : "Aim L", hands).transform;
                c.localPosition = new Vector3((i == 0 ? 0.1f : -0.1f), 0.6f * U, -0.026f); c.localScale = Vector3.one * 0.038f;
                v.aimCursor[i] = c;
                v.aimParts[i * 3] = Hidden(Fx("Outline", c, 1, aimInk, new Vector4(0.78f, 0.22f, 0, 0), B + 201, aimInk));
                v.aimParts[i * 3 + 1] = Hidden(Fx("Ring", c, 1, cream, new Vector4(0.78f, 0.12f, 0, 0), B + 202, cream));
                v.aimParts[i * 3 + 1].transform.localPosition = new Vector3(0, 0, -0.01f);
                v.aimParts[i * 3 + 2] = Hidden(Panel("Dot", c, 0.24f, 0.24f, 0.12f, cream, cream, B + 203, 0.040f));
                v.aimParts[i * 3 + 2].transform.localPosition = new Vector3(0, 0, -0.02f);
                v.aimRay[i] = Hidden(Fx(i == 0 ? "Aim Ray R" : "Aim Ray L", hands, 9, new Color(1f, 0.97f, 0.90f, 0.95f), Vector4.zero, B + 201));
                v.aimRay[i].transform.localScale = new Vector3(0.007f, 0.40f, 1f);
            }

            // the sign above the field: one strip of paper (its children are laid out in field units)
            var hud = Node("Hud", s).transform;
            hud.localPosition = new Vector3(0, 1.085f * U, -0.004f); hud.localScale = Vector3.one * U;
            v.hud = hud;
            v.hudBg = Panel("Strip", hud, 1.26f, 0.088f, 0.044f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, B + 210, 0.004f);
            v.hudBg.transform.localPosition = new Vector3(0, 0, 0.004f);
            v.title = Ord(Label("Title", hud, "Vampire Night", 0.042f, Ui.Pal.Ink, TextAnchor.MiddleLeft, true), B + 213);
            v.title.transform.localPosition = new Vector3(-0.60f, 0, 0);
            v.hearts = new UiSprite[GlassGameBase.MaxLives];
            for (int i = 0; i < v.hearts.Length; i++)
            {
                v.hearts[i] = Sprite("Heart " + i, hud, "glass-heart", 0.066f, 0.066f, B + 211);
                v.hearts[i].transform.localPosition = new Vector3(-0.20f + 0.05f * i, -0.002f, 0);
            }
            v.scoreStar = Sprite("Score Star", hud, "glass-star", 0.066f, 0.066f, B + 211);
            v.scoreStar.transform.localPosition = new Vector3(0.125f, -0.001f, 0);
            v.score = Ord(Label("Score", hud, "0", 0.044f, Ui.Pal.Ink, TextAnchor.MiddleLeft, true), B + 213);
            v.score.transform.localPosition = new Vector3(0.160f, 0, 0);
            v.level = Ord(Label("Level", hud, "Lv 1  Wave 1", 0.032f, Ui.Pal.TextDim), B + 213);
            v.level.transform.localPosition = new Vector3(0.375f, 0, 0);
            v.time = Ord(Label("Time", hud, "0:00", 0.040f, Ui.Pal.Ink, TextAnchor.MiddleRight, true), B + 213);
            v.time.transform.localPosition = new Vector3(0.60f, 0, 0);
            Color barInk = new Color(0.29f, 0.18f, 0.15f, 0.55f);
            v.barBg = Hidden(Panel("Bar Groove", hud, 0.50f, 0.016f, 0.008f, barInk, barInk, B + 211));
            v.barBg.transform.localPosition = new Vector3(0, -0.058f, 0);
            v.bar = Hidden(Panel("Bar", hud, 0.20f, 0.016f, 0.008f, Ui.Pal.Cyan, Ui.Pal.Cyan, B + 212));
            v.bar.transform.localPosition = new Vector3(-0.15f, -0.058f, -0.002f);

            v.bannerBg = Hidden(Panel("Banner Paper", s, 0.6f * U, 0.1f * U, 0.045f * U, Ui.Pal.PanelTop, Ui.Pal.Orange, B + 204, 0.006f * U));
            v.bannerBg.transform.localPosition = new Vector3(0, 0.66f * U, -0.036f);
            v.banner = Ord(Label("Banner", s, "", 0.062f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), B + 205);
            v.banner.transform.localPosition = new Vector3(0, 0.66f * U, -0.040f); v.banner.transform.localScale = Vector3.one * U;

            var result = Node("Result", s);
            result.transform.localPosition = new Vector3(0, 0.56f * U, -0.045f); result.transform.localScale = Vector3.one * U;
            v.result = result.transform;
            v.resultBg = Panel("Card", result.transform, 1.0f, 0.40f, 0.05f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, B + 220, 0.006f);
            v.resultBg.transform.localPosition = new Vector3(0, 0, 0.004f);
            v.resultTitle = Ord(Label("Title", result.transform, "Game over", 0.075f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), B + 222);
            v.resultTitle.transform.localPosition = new Vector3(0, 0.115f, 0);
            v.resultLine = Ord(Label("Line", result.transform, "", 0.042f, Ui.Pal.TextDim), B + 222);
            v.resultLine.transform.localPosition = new Vector3(0, 0.0f, 0);
            v.resultScore = Ord(Label("Score", result.transform, "Score 0", 0.050f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), B + 222);
            v.resultScore.transform.localPosition = new Vector3(0, -0.125f, 0);
            result.SetActive(false);
            stage.SetActive(false);

            // the pictures a game asks for by name
            var art = new List<Texture2D>();
            foreach (string n in GlassArt.All) art.Add(GlassPicture(n));
            v.art = art.ToArray();
            var abilityArt = new List<Texture2D>();
            foreach (string id in GlassSurvival.AbilityIds) abilityArt.Add(Art("glass-ab-" + id));
            v.abilityArt = abilityArt.ToArray();

            // THROUGH THE GLASS (MrGlassOut.cs): what a game sends out of its field flies through the room at the user - a pool of
            // 8 things (a holder turned to the eye with the picture and a glow in it), their trails, the ring on the glass where
            // each came through, the shield on each open palm, and the soft red ring round the edge of the view for a hit.
            // Draw order: B + 240 .. 247, the flash at B + 260 (over everything).
            var outRoot = Node("Out", root.transform).transform;
            outRoot.localPosition = new Vector3(0, 1.0f, 0.5f);                 // authoring pose only: the app places every part in the room
            const int OB = B + 240;
            Color sky = new Color(0.56f, 0.83f, 0.96f, 1f);
            v.outThings = new Transform[8]; v.outPicture = new UiSprite[8]; v.outGlow = new FxQuad[8]; v.outTrail = new FxQuad[8]; v.outRipple = new FxQuad[8];
            for (int i = 0; i < 8; i++)
            {
                var th = Node("Thing " + i, outRoot).transform;
                th.localPosition = new Vector3((i - 3.5f) * 0.12f, 0, 0); th.localScale = Vector3.one * 0.07f;
                v.outThings[i] = th;
                v.outGlow[i] = Hidden(Fx("Glow", th, 8, new Color(honey.r, honey.g, honey.b, 0.55f), new Vector4(2.6f, 0, 0, 0), OB + 1, new Color(1f, 0.97f, 0.86f, 0.55f)));
                v.outGlow[i].transform.localPosition = new Vector3(0, 0, 0.01f); v.outGlow[i].transform.localScale = Vector3.one * 2.5f;
                v.outPicture[i] = Off(Sprite("Picture", th, "glass-orb", 1.28f, 1.28f, OB + 2));
                v.outTrail[i] = Hidden(Fx("Trail " + i, outRoot, 9, new Color(1f, 0.92f, 0.66f, 0.75f), Vector4.zero, OB));
                v.outTrail[i].transform.localPosition = new Vector3((i - 3.5f) * 0.12f, 0.12f, 0); v.outTrail[i].transform.localScale = new Vector3(0.05f, 0.20f, 1f);
                v.outRipple[i] = Hidden(Fx("Glass Ripple " + i, outRoot, 6, honey, new Vector4(0.25f, 0, 0.3f, 0), OB + 3, cream));
                v.outRipple[i].transform.localPosition = new Vector3((i - 3.5f) * 0.12f, -0.15f, 0); v.outRipple[i].transform.localScale = Vector3.one * 0.18f;
            }
            v.shieldDisc = new FxQuad[2]; v.shieldRing = new FxQuad[2];
            for (int i = 0; i < 2; i++)
            {
                v.shieldDisc[i] = Hidden(Fx(i == 0 ? "Shield R" : "Shield L", outRoot, 0, new Color(sky.r, sky.g, sky.b, 0.20f), new Vector4(4.4f, 0, 0, 0), OB + 5));
                v.shieldRing[i] = Hidden(Fx(i == 0 ? "Shield Rim R" : "Shield Rim L", outRoot, 1, new Color(sky.r, sky.g, sky.b, 0.8f), new Vector4(0.90f, 0.06f, 0, 0), OB + 6, new Color(cream.r, cream.g, cream.b, 0.8f)));
                v.shieldDisc[i].transform.localPosition = v.shieldRing[i].transform.localPosition = new Vector3(i == 0 ? 0.2f : -0.2f, -0.35f, 0);
                v.shieldDisc[i].transform.localScale = v.shieldRing[i].transform.localScale = Vector3.one * 0.20f;
            }
            Color hitRed = new Color(0.93f, 0.36f, 0.36f, 0.6f);
            v.vignette = Hidden(Fx("Hit Flash", outRoot, 1, hitRed, new Vector4(1.0f, 0.42f, 0, 0), B + 260, hitRed));
            v.vignette.transform.localPosition = new Vector3(0, 0.4f, 0); v.vignette.transform.localScale = Vector3.one * 0.6f;

            // the game is played inside a screen: a small sign near that screen, for a few seconds
            var pcSign = Node("PcSign", root.transform);
            pcSign.transform.localPosition = new Vector3(0, 1.30f, 0.6f);         // authoring pose only
            v.pcSign = pcSign;
            v.pcSignBg = Panel("Paper", pcSign.transform, 0.40f, 0.040f, 0.016f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, B + 230, 0.0016f);
            v.pcSignBg.transform.localPosition = new Vector3(0, 0, 0.002f);
            v.pcSignLabel = Ord(Label("Line", pcSign.transform, "Vampire Night - play at the screen with your hands", 0.020f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), B + 231);
            pcSign.SetActive(false);

            Wire(app, "_stage", v);
        }
    }
}
