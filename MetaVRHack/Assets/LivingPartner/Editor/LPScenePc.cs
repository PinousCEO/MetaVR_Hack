using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using LivingPartner.Windows;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// The parts of Scenes/PcCharacter.unity that were added on 2026-11-17 for the links between the room and the
    /// computer: the GLASS LAYER (PcCharacter/Glass, on its own Unity layer - a flat picture of one whole monitor:
    /// the shadows of the user's hands and the pointing ring, the frame around what the partner reads, a parcel on its
    /// way out, and the field of a glass game with its pool of sprites, its cards and its HUD) and the market stall
    /// that stands beside the partner (PcCharacter/Show). Authored like everything else: real objects, wired into
    /// serialized fields of PcApp (PcShadow.cs, PcGlass.cs, PcShow.cs). The glass layer is laid out here for a 16:9
    /// monitor, half a metre high; the app lays it out again for the monitor it is shown on.
    /// </summary>
    public static partial class LPSceneBuilder
    {
        static UiSprite HiddenSprite(UiSprite s) { s.enabled = false; s.Renderer.enabled = false; return s; }

        static StoryLabel Ordered(StoryLabel l, int order) { l.GetComponent<MeshRenderer>().sortingOrder = order; return l; }

        static void AuthorPcGlass(PcApp app, Transform t)
        {
            const float U = PartnerHeight / GlassGames.PetHeight;      // metres of the glass layer per unit of a game's field (the monitor is one unit high)
            const float W = 16f / 9f;
            Color ink = new Color(0.29f, 0.18f, 0.15f, 1f), cream = new Color(1f, 0.97f, 0.90f, 1f);
            var glass = Node("Glass", t);
            Transform g = glass.transform;

            // ---- the user's hands: a sharp and a soft shadow each (white pictures, tinted brown-violet), and the pointing ring
            var hands = Node("Hands", g).transform;
            var hsRoot = new Transform[2]; var hsSharp = new UiSprite[2]; var hsSoft = new UiSprite[2];
            for (int i = 0; i < 2; i++)
            {
                hsRoot[i] = Node(i == 0 ? "Hand Right" : "Hand Left", hands).transform;
                hsRoot[i].localPosition = new Vector3((i == 0 ? 0.22f : -0.22f) * U, 0.02f * U, 0.05f);
                float side = 0.24f * U * (i == 0 ? 1f : -1f);
                hsSoft[i] = Sprite("Soft", hsRoot[i], "glass-hand-soft", side * 1.2f, Mathf.Abs(side) * 1.2f, 130);
                hsSoft[i].transform.localPosition = new Vector3(0, 0, 0.001f);
                hsSharp[i] = Sprite("Sharp", hsRoot[i], "glass-hand", side, Mathf.Abs(side), 131);
                hsSoft[i].color = new Color(0.27f, 0.17f, 0.24f, 0.25f); hsSharp[i].color = new Color(0.27f, 0.17f, 0.24f, 0.45f);
                HiddenSprite(hsSoft[i]); HiddenSprite(hsSharp[i]);
            }
            var point = Node("Point", hands).transform;
            point.localPosition = new Vector3(0, 0.12f * U, 0.04f);
            point.localScale = Vector3.one * 56f / 1080f * U;           // 56 pixels of a 1080-line monitor
            var ptOutline = Hidden(Fx("Outline", point, 1, ink, new Vector4(0.78f, 0.22f, 0, 0), 133, ink));
            var ptRing = Hidden(Fx("Ring", point, 1, cream, new Vector4(0.78f, 0.12f, 0, 0), 134, cream));
            ptRing.transform.localPosition = new Vector3(0, 0, -0.0005f);
            var ptDot = Hidden(Panel("Dot", point, 0.24f, 0.24f, 0.12f, cream, cream, 135, 0.040f));
            ptDot.transform.localPosition = new Vector3(0, 0, -0.001f);
            // the four ticks that make the ring a crosshair in a game that is aimed at (cream with an ink rim, like the ring)
            var ptTicks = new FxQuad[4];
            for (int i = 0; i < 4; i++)
            {
                bool across = i < 2; float sgn = i % 2 == 0 ? 1f : -1f;
                ptTicks[i] = Hidden(Panel(new[] { "Tick Right", "Tick Left", "Tick Up", "Tick Down" }[i], point, across ? 0.36f : 0.10f, across ? 0.10f : 0.36f, 0.05f, cream, cream, 135, 0.026f));
                ptTicks[i].transform.localPosition = across ? new Vector3(0.52f * sgn, 0, -0.001f) : new Vector3(0, 0.52f * sgn, -0.001f);
            }

            // ---- a look: the frame around what the partner reads (the app lays the dashes along it)
            var look = Node("Look", g);
            look.transform.localPosition = new Vector3(0.2f * U, 0.1f * U, 0.07f);
            var lkGlow = new FxQuad[4];
            for (int i = 0; i < 4; i++)
            {
                lkGlow[i] = Panel(new[] { "Line Top", "Line Bottom", "Line Left", "Line Right" }[i], look.transform, i < 2 ? 0.3f * U : 0.005f * U, i < 2 ? 0.005f * U : 0.2f * U, 0.002f, cream, cream, 120);
                lkGlow[i].transform.localPosition = i < 2 ? new Vector3(0, (i == 0 ? 0.1f : -0.1f) * U, 0.004f) : new Vector3((i == 2 ? -0.15f : 0.15f) * U, 0, 0.004f);
            }
            var lkDashes = new FxQuad[64];
            for (int i = 0; i < lkDashes.Length; i++)
            {
                lkDashes[i] = Panel("Dash " + i, look.transform, 0.009f * U, 0.002f * U, 0.001f * U, ink, ink, 121);
                lkDashes[i].transform.localPosition = new Vector3((-0.14f + 0.0045f * i) * U, 0.1f * U, 0);
            }
            var lkShimmer = Sprite("Shimmer", look.transform, "glass-shimmer", 0.29f * U, 0.05f * U, 119);
            lkShimmer.transform.localPosition = new Vector3(0, 0, 0.006f); lkShimmer.color = new Color(1f, 0.97f, 0.86f, 0.4f);
            var lkSticker = Sprite("Magnifier", look.transform, "glass-magnifier", 0.028f * U, 0.028f * U, 122);
            lkSticker.transform.localPosition = new Vector3(0.155f * U, 0.105f * U, -0.006f);
            look.SetActive(false);

            // ---- deliver: a parcel with the thing's picture, and the sparkles it leaves behind
            var parcel = Node("Parcel", g);
            parcel.transform.localPosition = new Vector3(-0.3f * U, 0.1f * U, 0.06f); parcel.transform.localScale = Vector3.one * 0.09f * U;
            var dvParcel = Sprite("Box", parcel.transform, "parcel", 1f, 1f, 125);
            var dvIcon = Sprite("Picture", parcel.transform, "parcel", 0.5f, 0.5f, 126);
            dvIcon.transform.localPosition = new Vector3(0, -0.1f, -0.002f);
            parcel.SetActive(false);
            var sparkRoot = Node("Parcel Sparkles", g).transform;
            var dvSparks = new UiSprite[4];
            for (int i = 0; i < dvSparks.Length; i++) dvSparks[i] = HiddenSprite(Sprite("Sparkle " + i, sparkRoot, "glass-sparkle", 0.03f * U, 0.03f * U, 124));

            // ---- a glass game: the field over the whole monitor
            var game = Node("Game", g);
            var footballs = FootballPool(game.transform);
            Transform gm = game.transform;
            Color paper = new Color(1f, 0.973f, 0.918f, 0.18f);
            var veil = Panel("Veil", gm, W * U, U, 0f, paper, paper, -200);
            veil.transform.localPosition = new Vector3(0, 0, 0.64f);
            // the ground the partner stands on: a strip of paper along the bottom edge with an ink line on top
            var ground = Panel("Ground", gm, W * U, 0.035f * U, 0f, new Color(1f, 0.965f, 0.88f, 0.93f), new Color(0.97f, 0.90f, 0.78f, 0.93f), -190);
            ground.transform.localPosition = new Vector3(0, (-0.5f + 0.0175f) * U, 0.50f);
            var groundLine = Panel("Ground Line", gm, W * U, 0.0032f * U, 0f, ink, ink, -189);
            groundLine.transform.localPosition = new Vector3(0, (-0.5f + 0.035f) * U, 0.498f);
            var petShadow = Hidden(Fx("Partner Shadow", gm, 0, new Color(0.30f, 0.20f, 0.24f, 0.34f), new Vector4(1.4f, 0, 0, 0), -188));
            petShadow.transform.localPosition = new Vector3(0, -0.462f * U, 0.40f); petShadow.transform.localScale = new Vector3(0.15f * U, 0.028f * U, 1f);

            // the pool the game's list is drawn with: 160 pictures (what a scripted game may ask for at once), in the draw orders
            // -180 .. -21 - above the veil (-200), the ground (-190 / -189) and the partner's shadow (-188), below everything
            // else of the glass layer (the words and all that follows are at 107 and above)
            var spritesRoot = Node("Sprites", gm).transform;
            var sprites = new UiSprite[160];
            for (int i = 0; i < sprites.Length; i++)
            {
                sprites[i] = HiddenSprite(Sprite("Sprite " + i, spritesRoot, "glass-star", 0.08f * U, 0.08f * U, -180 + i));
                sprites[i].transform.localPosition = new Vector3(((i % 16) - 7.5f) * 0.1f * U, (0.45f - (i / 16) * 0.1f) * U, 0.10f);
            }
            var labelsRoot = Node("Labels", gm).transform;
            var labels = new StoryLabel[33]; var labelBgs = new FxQuad[33];      // (32 for a game's words, and one for what the partner says)
            for (int i = 0; i < labels.Length; i++)
            {
                labelBgs[i] = Hidden(Panel("Label Paper " + i, labelsRoot, 0.1f * U, 0.07f * U, 0.035f * U, Ui.Pal.PanelTop, Ui.Pal.Orange, 108, 0.005f * U));
                labels[i] = Ordered(Label("Label " + i, labelsRoot, "", 1f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), 109);
                labels[i].transform.localScale = Vector3.one * 0.07f * U;
            }

            // the three cards of the survival game's choice (one unit = the height of the card's paper)
            var cardsRoot = Node("Cards", gm).transform;
            var cards = new Transform[3]; var cardBg = new UiSprite[3]; var cardIcon = new UiSprite[3]; var cardRing = new FxQuad[3];
            var cardName = new StoryLabel[3]; var cardNote = new StoryLabel[3];
            for (int i = 0; i < 3; i++)
            {
                var c = Node("Card " + i, cardsRoot);
                cards[i] = c.transform;
                c.transform.localPosition = new Vector3((new[] { 0.22f, 0.5f, 0.78f }[i] - 0.5f) * W * U, (GlassSurvival.CardY - 0.5f) * U, 0.03f - 0.002f * i);
                c.transform.localScale = Vector3.one * GlassSurvival.CardSize * U;
                cardBg[i] = Sprite("Paper", c.transform, "glass-card", GlassSurvival.CardAspect * 216f / 188f, 300f / 272f, 112);
                cardBg[i].transform.localPosition = new Vector3(0, -0.015f, 0);
                cardRing[i] = Fx("Hold Ring", c.transform, 4, new Color(1f, 0.69f, 0.42f, 1f), new Vector4(0.86f, 0.12f, 0.35f, 0), 113, new Color(0.96f, 0.45f, 0.42f, 1f));
                cardRing[i].transform.localPosition = new Vector3(0, 0.28f, -0.001f); cardRing[i].transform.localScale = Vector3.one * 0.45f;
                cardIcon[i] = Sprite("Power", c.transform, "glass-ab-" + GlassSurvival.AbilityIds[i], 0.29f, 0.29f, 114);
                cardIcon[i].transform.localPosition = new Vector3(0, 0.28f, -0.002f);
                cardName[i] = Ordered(Label("Name", c.transform, GlassSurvival.AbilityNames[i], 0.112f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), 115);
                cardName[i].transform.localPosition = new Vector3(0, -0.075f, -0.003f);
                cardNote[i] = Ordered(Label("Note", c.transform, "", 0.074f, Ui.Pal.TextDim), 115);
                cardNote[i].transform.localPosition = new Vector3(0, -0.295f, -0.003f);
                c.SetActive(false);
            }

            // the HUD: one strip of paper at the top (its children are laid out in field units)
            var hud = Node("Hud", gm).transform;
            hud.localPosition = new Vector3(0, (0.945f - 0.5f) * U, 0.02f); hud.localScale = Vector3.one * U;
            var hudBg = Panel("Strip", hud, 1.26f, 0.088f, 0.044f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 140, 0.004f);
            hudBg.transform.localPosition = new Vector3(0, 0, 0.002f);
            var title = Ordered(Label("Title", hud, "Vampire Night", 0.042f, Ui.Pal.Ink, TextAnchor.MiddleLeft, true), 142);
            title.transform.localPosition = new Vector3(-0.60f, 0, 0);
            var hearts = new UiSprite[GlassGameBase.MaxLives];
            for (int i = 0; i < hearts.Length; i++)
            {
                hearts[i] = Sprite("Heart " + i, hud, "glass-heart", 0.066f, 0.066f, 141);
                hearts[i].transform.localPosition = new Vector3(-0.20f + 0.05f * i, -0.002f, 0);
            }
            var scoreStar = Sprite("Score Star", hud, "glass-star", 0.066f, 0.066f, 141);
            scoreStar.transform.localPosition = new Vector3(0.125f, -0.001f, 0);
            var score = Ordered(Label("Score", hud, "0", 0.044f, Ui.Pal.Ink, TextAnchor.MiddleLeft, true), 142);
            score.transform.localPosition = new Vector3(0.160f, 0, 0);
            var level = Ordered(Label("Level", hud, "Lv 1  Wave 1", 0.035f, Ui.Pal.Ink), 142);
            level.transform.localPosition = new Vector3(0.375f, 0, 0);
            var time = Ordered(Label("Time", hud, "0:00", 0.040f, Ui.Pal.Ink, TextAnchor.MiddleRight, true), 142);
            time.transform.localPosition = new Vector3(0.60f, 0, 0);
            Color barInk = new Color(0.29f, 0.18f, 0.15f, 0.55f);
            var barBg = Hidden(Panel("Bar Groove", hud, 0.50f, 0.016f, 0.008f, barInk, barInk, 140));
            barBg.transform.localPosition = new Vector3(0, -0.058f, 0);
            var bar = Hidden(Panel("Bar", hud, 0.20f, 0.016f, 0.008f, Ui.Pal.Cyan, Ui.Pal.Cyan, 141));
            bar.transform.localPosition = new Vector3(-0.15f, -0.058f, -0.002f);

            var bannerBg = Hidden(Panel("Banner Paper", gm, 0.6f * U, 0.1f * U, 0.045f * U, Ui.Pal.PanelTop, Ui.Pal.Orange, 144, 0.006f * U));
            bannerBg.transform.localPosition = new Vector3(0, 0.16f * U, 0.024f);
            var banner = Ordered(Label("Banner", gm, "", 0.062f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), 145);
            banner.transform.localPosition = new Vector3(0, 0.16f * U, 0.022f); banner.transform.localScale = Vector3.one * U;

            var result = Node("Result", gm);
            result.transform.localPosition = new Vector3(0, 0.06f * U, 0.016f); result.transform.localScale = Vector3.one * U;
            var resultBg = Panel("Card", result.transform, 1.0f, 0.40f, 0.05f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 150, 0.006f);
            resultBg.transform.localPosition = new Vector3(0, 0, 0.002f);
            var resultTitle = Ordered(Label("Title", result.transform, "Game over", 0.075f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), 152);
            resultTitle.transform.localPosition = new Vector3(0, 0.115f, 0);
            var resultLine = Ordered(Label("Line", result.transform, "", 0.047f, Ui.Pal.Ink), 152);
            resultLine.transform.localPosition = new Vector3(0, 0.0f, 0);
            var resultScore = Ordered(Label("Score", result.transform, "Score 0", 0.046f, Ui.Pal.TextDim, TextAnchor.MiddleCenter, true), 152);
            resultScore.transform.localPosition = new Vector3(0, -0.125f, 0);
            result.SetActive(false);
            game.SetActive(false);

            // the pictures a game asks for by name, and the sound voices
            var art = new List<Texture2D>();
            foreach (string n in GlassArt.All) art.Add(GlassPicture(n));
            var abilityArt = new List<Texture2D>();
            foreach (string id in GlassSurvival.AbilityIds) abilityArt.Add(Art("glass-ab-" + id));
            var soundRoot = Node("Sound", g).transform;
            var sfx = new AudioSource[4];
            for (int i = 0; i < sfx.Length; i++)
            {
                sfx[i] = Node("Sfx " + i, soundRoot).AddComponent<AudioSource>();
                sfx[i].playOnAwake = false; sfx[i].loop = false; sfx[i].spatialBlend = 0f; sfx[i].volume = 1f;
            }

            foreach (var tr in glass.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = PcApp.GlassLayer;
            glass.SetActive(false);

            // ---- the market stall beside the partner (in the partner's own small window: the ordinary layer, metres)
            var show = Node("Show", t);
            float H = PartnerHeight;
            show.transform.localPosition = new Vector3(0.1f, -0.05f, PlanBuilder.ResidentDepth(PartnerHeight));
            var stall = Sprite("Stall", show.transform, "glass-stall", 1.1f * H, 1.1f * H, -3);
            stall.transform.localPosition = new Vector3(0, 0.46f * H, 0.012f);
            var item = Sprite("Thing", show.transform, "parcel", 0.36f * H, 0.36f * H, 3);
            item.transform.localPosition = new Vector3(0, 0.50f * H, -0.02f);
            var tagPivot = Node("Tag", show.transform);
            tagPivot.transform.localPosition = new Vector3(0.4f * H, 0.66f * H, -0.025f); tagPivot.transform.localScale = Vector3.one * H;
            var tag = Sprite("Paper", tagPivot.transform, "glass-tag", 0.56f, 0.28f, 5);
            tag.transform.localPosition = new Vector3(0, -0.2f, 0);
            var price = Ordered(Label("Price", tagPivot.transform, "15", 0.13f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), 6);
            price.transform.localPosition = new Vector3(0.105f, -0.2f, -0.001f);
            tagPivot.SetActive(false);
            var ribbon = HiddenSprite(Sprite("Listed", show.transform, "glass-listed", 1.0f * H, 0.326f * H, 7));
            ribbon.transform.localPosition = new Vector3(0, 1.15f * H, -0.03f);
            show.SetActive(false);

            Wire(app, "_glRoot", glass, "_hsRoot", hsRoot, "_hsSharp", hsSharp, "_hsSoft", hsSoft, "_ptRoot", point, "_ptOutline", ptOutline, "_ptRing", ptRing, "_ptDot", ptDot, "_ptTicks", ptTicks);
            Wire(app, "_glGameRoot", game, "_glFootballs", footballs, "_glVeil", veil, "_glGround", ground, "_glGroundLine", groundLine, "_glPetShadow", petShadow, "_glSprites", sprites, "_glLabels", labels, "_glLabelBgs", labelBgs,
                "_glCards", cards, "_glCardBg", cardBg, "_glCardIcon", cardIcon, "_glCardRing", cardRing, "_glCardName", cardName, "_glCardNote", cardNote,
                "_glHud", hud, "_glHudBg", hudBg, "_glTitle", title, "_glScore", score, "_glLevel", level, "_glTime", time, "_glHearts", hearts, "_glScoreStar", scoreStar,
                "_glBarBg", barBg, "_glBar", bar, "_glBannerBg", bannerBg, "_glBanner", banner, "_glResult", result.transform, "_glResultBg", resultBg,
                "_glResultTitle", resultTitle, "_glResultLine", resultLine, "_glResultScore", resultScore, "_glArt", art, "_glAbilityArt", abilityArt, "_glSfx", sfx);
            Wire(app, "_lkRoot", look, "_lkDashes", lkDashes, "_lkGlow", lkGlow, "_lkSticker", lkSticker, "_lkShimmer", lkShimmer,
                "_dvRoot", parcel.transform, "_dvParcel", dvParcel, "_dvIcon", dvIcon, "_dvSparks", dvSparks,
                "_stRoot", show, "_stStall", stall, "_stItem", item, "_stRibbon", ribbon, "_stTagPivot", tagPivot.transform, "_stTag", tag, "_stPrice", price,
                "_stParcelTex", Art("parcel"));
        }
    }
}
