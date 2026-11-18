using LivingPartner.Character;
using LivingPartner.MR;
using LivingPartner.Shared;
using LivingPartner.Windows;
using UnityEditor;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// MrCore/Swarm: everything SCREEN SCAMPS shows (MR/MrSwarm.cs, SwarmView; the game is Shared/Swarm.cs). Pools of real
    /// objects, authored hidden: 64 monsters (a mesh renderer each, all with the one prop material - their shapes are
    /// mesh data the app builds at start) and their blob shadows, 48 bolts (a glow and a streak), 64 gems, rings for the
    /// glass, puffs, chain sparks, the stars round the hand; the storybook window they come out of when there is no
    /// screen; the line and ring of the aim; the shield; the red ring of a hit; the sign with the hearts; the three
    /// augment cards (each ONE big button: poke it, or point and pinch) and the result card.
    /// Draw order (B = 600, above the glass stage): shadows B, the window B .. B + 3, rings on the glass B + 5, monsters
    /// B + 10, bolts B + 19 / 20, gems B + 21, puffs B + 22, aim B + 24 / 25, the sign B + 30 .. 33, the banner B + 40 / 41,
    /// the cards B + 49 .. 53, the result B + 60 .., the red ring B + 90.
    /// </summary>
    public static partial class LPSceneBuilder
    {
        const int SwarmOrder = 600;
        static Material _demonFelt;

        static void CreateDemonArt()
        {
            var cloth = new Material(Shader.Find("LivingPartner/DemonFelt")) { name = "DemonFelt" };
            cloth.SetTexture("_Weave", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/Textures/DemonFelt.png"));
            _demonFelt = SaveAsset(cloth, ArtDir + "/Materials/DemonFelt.mat");
        }

        /// <summary>Lifts everything under a transform in the draw order (a board that must be seen over the game).</summary>
        static void RaiseOrder(Transform root, int add)
        {
            foreach (var q in root.GetComponentsInChildren<FxQuad>(true)) { q.order += add; q.Apply(); q.Renderer.sortingOrder = q.order; }
            foreach (var s in root.GetComponentsInChildren<UiSprite>(true)) { s.order += add; s.Apply(); s.Renderer.sortingOrder = s.order; }
            foreach (var l in root.GetComponentsInChildren<StoryLabel>(true)) l.GetComponent<MeshRenderer>().sortingOrder += add;
        }

        static void AuthorSwarm(MrApp app, Transform t, Transform panels)
        {
            const int B = SwarmOrder;
            Color honey = new Color(1f, 0.84f, 0.42f, 1f), cream = new Color(1f, 0.97f, 0.90f, 1f), ink = new Color(0.29f, 0.18f, 0.15f, 1f), sky = new Color(0.56f, 0.83f, 0.96f, 1f);
            var holder = Node("Swarm", t);
            var v = holder.AddComponent<SwarmView>();
            var root = Node("Field", holder.transform);
            root.transform.localPosition = new Vector3(0f, 0.95f, 0.75f);      // authoring pose only: the app places every part in the room
            v.root = root;
            Transform r = root.transform;
            v.gate = Sprite("Plumwood Gate", r, "demon-gate", 0.42f, 0.25f, B + 2);

            // ---- the storybook window on the desk (playing without a screen): a wooden frame, a pane of morning sky, a sill
            const float WW = 0.42f, WH = 0.25f;
            var win = Node("Window", r);
            v.window = win;
            v.windowFrame = Panel("Frame", win.transform, WW + 0.030f, WH + 0.030f, 0.030f, new Color(0.62f, 0.44f, 0.34f, 1f), new Color(0.50f, 0.34f, 0.27f, 1f), B, 0.003f);
            v.windowFrame.transform.localPosition = new Vector3(0, 0, 0.004f);
            v.windowPane = Panel("Pane", win.transform, WW, WH, 0.018f, new Color(0.74f, 0.88f, 0.98f, 0.96f), new Color(1f, 0.90f, 0.80f, 0.96f), B + 1);
            v.windowPane.transform.localPosition = new Vector3(0, 0, 0.002f);
            v.windowSill = Panel("Sill", win.transform, WW + 0.075f, 0.024f, 0.012f, new Color(1f, 0.96f, 0.86f, 1f), new Color(0.98f, 0.84f, 0.60f, 1f), B + 3, 0.0016f);
            v.windowSill.transform.localPosition = new Vector3(0, -WH * 0.5f - 0.020f, -0.006f);
            win.SetActive(false);

            // ---- the sheet on the glass while something is behind it, and its glow
            v.glassVeil = Hidden(Panel("Glass Veil", r, 0.5f, 0.3f, 0.012f, new Color(1f, 0.97f, 0.90f, 0.10f), new Color(1f, 0.90f, 0.70f, 0.16f), B + 1));
            v.glassGlow = Hidden(Fx("Glass Glow", r, 8, new Color(1f, 0.88f, 0.56f, 0.3f), new Vector4(2.2f, 0, 0, 0), B + 2, new Color(1f, 0.97f, 0.86f, 0.3f)));
            v.glassGlow.transform.localScale = new Vector3(0.8f, 0.5f, 1f);

            // ---- the monsters and their shadows
            var monstersRoot = Node("Monsters", r).transform; var shadowsRoot = Node("Shadows", r).transform;
            v.monsters = new Transform[SwarmSim.MaxMonsters]; v.monsterMesh = new MeshFilter[SwarmSim.MaxMonsters]; v.monsterRend = new MeshRenderer[SwarmSim.MaxMonsters]; v.shadows = new FxQuad[SwarmSim.MaxMonsters];
            for (int i = 0; i < SwarmSim.MaxMonsters; i++)
            {
                var go = Node("Monster " + i, monstersRoot);
                var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = _ballMesh;        // a stand-in: the app gives it its kind's shape
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _demonFelt; mr.enabled = false; mr.sortingOrder = B + 10;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                go.transform.localPosition = new Vector3((i % 16 - 7.5f) * 0.07f, (i / 16) * 0.07f - 0.1f, 0f); go.transform.localScale = Vector3.one * 0.06f;
                v.monsters[i] = go.transform; v.monsterMesh[i] = mf; v.monsterRend[i] = mr;
                v.shadows[i] = Hidden(Fx("Shadow " + i, shadowsRoot, 0, new Color(0.18f, 0.14f, 0.16f, 0.26f), new Vector4(2.2f, 0, 0, 0), B));
                v.shadows[i].transform.localRotation = Quaternion.Euler(90, 0, 0); v.shadows[i].transform.localScale = Vector3.one * 0.07f;
                v.shadows[i].transform.localPosition = new Vector3((i % 16 - 7.5f) * 0.07f, -0.2f, (i / 16) * 0.07f);
            }

            // ---- bolts of light, gems
            var boltsRoot = Node("Bolts", r).transform;
            v.boltGlow = new FxQuad[SwarmSim.MaxBolts]; v.boltStreak = new FxQuad[SwarmSim.MaxBolts];
            for (int i = 0; i < SwarmSim.MaxBolts; i++)
            {
                v.boltStreak[i] = Hidden(Fx("Streak " + i, boltsRoot, 9, new Color(1f, 0.93f, 0.62f, 0.85f), Vector4.zero, B + 19));
                v.boltStreak[i].transform.localScale = new Vector3(0.02f, 0.08f, 1f);
                v.boltGlow[i] = Hidden(Fx("Bolt " + i, boltsRoot, 8, new Color(1f, 0.90f, 0.55f, 1f), new Vector4(3.4f, 0, 0, 0), B + 20, new Color(1f, 0.99f, 0.92f, 1f)));
                v.boltGlow[i].transform.localScale = Vector3.one * 0.08f;
                v.boltGlow[i].transform.localPosition = v.boltStreak[i].transform.localPosition = new Vector3((i % 12 - 5.5f) * 0.05f, 0.35f + (i / 12) * 0.05f, 0);
            }
            var gemsRoot = Node("Gems", r).transform;
            v.gems = new UiSprite[SwarmSim.MaxGems];
            for (int i = 0; i < SwarmSim.MaxGems; i++)
            {
                v.gems[i] = HiddenSprite(Sprite("Gem " + i, gemsRoot, "glass-gem", 0.024f, 0.024f, B + 21));
                v.gems[i].transform.localPosition = new Vector3((i % 16 - 7.5f) * 0.04f, 0.6f + (i / 16) * 0.04f, 0);
            }

            // ---- rings on the glass, puffs, chain sparks, the stars round the hand
            var fxRoot = Node("Effects", r).transform;
            v.ripples = new FxQuad[10];
            for (int i = 0; i < v.ripples.Length; i++) { v.ripples[i] = Hidden(Fx("Glass Ring " + i, fxRoot, 6, honey, new Vector4(0.25f, 0, 0.3f, 0), B + 5, cream)); v.ripples[i].transform.localScale = Vector3.one * 0.15f; }
            v.puffs = new FxQuad[24];
            for (int i = 0; i < v.puffs.Length; i++) { v.puffs[i] = Hidden(Fx("Puff " + i, fxRoot, 0, cream, new Vector4(2.4f, 0, 0, 0), B + 22)); v.puffs[i].transform.localScale = Vector3.one * 0.06f; }
            v.chains = new FxQuad[6];
            for (int i = 0; i < v.chains.Length; i++) { v.chains[i] = Hidden(Fx("Chain " + i, fxRoot, 9, new Color(1f, 0.97f, 0.78f, 0.95f), Vector4.zero, B + 23)); v.chains[i].transform.localScale = new Vector3(0.012f, 0.2f, 1f); }
            v.orbitStars = new FxQuad[SwarmSim.MaxOrbit];
            for (int i = 0; i < v.orbitStars.Length; i++) { v.orbitStars[i] = Hidden(Fx("Hand Star " + i, fxRoot, 5, honey, new Vector4(1.3f, 0, 0, 0), B + 23, cream)); v.orbitStars[i].transform.localScale = Vector3.one * 0.055f; }

            // ---- where the finger points; the shield on the left palm; the mist; a heart lost
            v.aimLine = Hidden(Fx("Aim Line", fxRoot, 9, new Color(1f, 0.97f, 0.90f, 0.90f), Vector4.zero, B + 24));
            v.aimLine.transform.localScale = new Vector3(0.006f, 0.4f, 1f);
            v.reticle = Hidden(Fx("Aim Ring", fxRoot, 1, honey, new Vector4(0.82f, 0.13f, 0, 0), B + 25, cream));
            v.reticle.transform.localScale = Vector3.one * 0.05f;
            v.reticleDot = Hidden(Panel("Aim Dot", fxRoot, 0.007f, 0.007f, 0.0035f, cream, cream, B + 25, 0.0008f));
            v.shieldDisc = Hidden(Fx("Shield", fxRoot, 0, new Color(sky.r, sky.g, sky.b, 0.22f), new Vector4(4.4f, 0, 0, 0), B + 26));
            v.shieldRing = Hidden(Fx("Shield Rim", fxRoot, 1, new Color(sky.r, sky.g, sky.b, 0.85f), new Vector4(0.90f, 0.06f, 0, 0), B + 27, new Color(cream.r, cream.g, cream.b, 0.85f)));
            v.shieldDisc.transform.localScale = v.shieldRing.transform.localScale = Vector3.one * 0.22f;
            v.mist = Hidden(Fx("Sleepy Mist", fxRoot, 7, new Color(0.80f, 0.86f, 1f, 0.45f), new Vector4(0.90f, 0.035f, 0, 0), B + 4, new Color(1f, 1f, 1f, 0.45f)));
            v.mist.transform.localScale = Vector3.one * 0.8f;
            v.ouch = Hidden(Fx("Ouch Ring", fxRoot, 6, honey, new Vector4(0.25f, 0, 0.3f, 0), B + 26, cream));
            v.ouch.transform.localScale = Vector3.one * 0.5f;
            Color hitRed = new Color(0.93f, 0.36f, 0.36f, 0.6f);
            v.vignette = Hidden(Fx("Hit Flash", fxRoot, 1, hitRed, new Vector4(1.0f, 0.42f, 0, 0), B + 90, hitRed));
            v.vignette.transform.localScale = Vector3.one * 0.6f;

            // ---- the sign over the glass: hearts, level, the bar to the next one, the clock; under it the boss's bar
            var hud = Node("Sign", r).transform;
            hud.localPosition = new Vector3(0, 0.22f, 0);
            v.hud = hud;
            v.hudBg = Panel("Strip", hud, 0.37f, 0.038f, 0.019f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, B + 30, 0.0016f);
            v.hudBg.transform.localPosition = new Vector3(0, 0, 0.003f);
            v.hearts = new UiSprite[6];
            for (int i = 0; i < v.hearts.Length; i++)
            {
                v.hearts[i] = Sprite("Heart " + i, hud, "glass-heart", 0.027f, 0.027f, B + 31);
                v.hearts[i].transform.localPosition = new Vector3(-0.163f + 0.026f * i, -0.0005f, 0);
            }
            v.level = Ordered(Label("Level", hud, "Lv 1", 0.0150f, Ui.Pal.Ink, TextAnchor.MiddleLeft, true), B + 33);
            v.level.transform.localPosition = new Vector3(-0.004f, 0, 0);
            Color groove = new Color(ink.r, ink.g, ink.b, 0.45f);
            v.barBg = Panel("Bar Groove", hud, 0.094f, 0.007f, 0.0035f, groove, groove, B + 31);
            v.barBg.transform.localPosition = new Vector3(0.090f, 0, 0);
            v.bar = Panel("Bar", hud, 0.02f, 0.007f, 0.0035f, Ui.Pal.Cyan, sky, B + 32);
            v.bar.transform.localPosition = new Vector3(0.053f, 0, -0.002f);
            v.clock = Ordered(Label("Clock", hud, "0:00", 0.0150f, Ui.Pal.Ink, TextAnchor.MiddleRight, true), B + 33);
            v.clock.transform.localPosition = new Vector3(0.176f, 0, 0);
            v.bossBarBg = Hidden(Panel("Boss Groove", hud, 0.22f, 0.010f, 0.005f, groove, groove, B + 31));
            v.bossBarBg.transform.localPosition = new Vector3(0, 0.050f, 0);
            v.bossBar = Hidden(Panel("Boss Bar", hud, 0.22f, 0.010f, 0.005f, new Color(0.98f, 0.60f, 0.62f, 1f), new Color(0.96f, 0.45f, 0.50f, 1f), B + 32));
            v.bossBar.transform.localPosition = new Vector3(0, 0.050f, -0.002f);
            v.bossName = Ordered(Label("Boss Name", hud, "", 0.0125f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), B + 33);
            v.bossName.transform.localPosition = new Vector3(0, 0.034f, 0);

            v.bannerBg = Hidden(Panel("Banner Paper", r, 0.3f, 0.05f, 0.014f, Ui.Pal.PanelTop, Ui.Pal.Orange, B + 40, 0.0016f));
            v.banner = Ordered(Label("Banner", r, "", 0.0190f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), B + 41);
            v.banner.transform.localPosition = new Vector3(0, 0.10f, -0.05f);
            root.SetActive(false);

            // ---- the three augment cards: the storybook card with a picture in its round window, a name, one line. Each
            //      is ONE button as large as the card (its paper, picture and words hang on the button: they grow under a finger).
            const float CW = 0.106f, CH = 0.150f;
            v.cards = new UiPanel[3]; v.cardButtons = new UiButton[3]; v.cardIcons = new UiSprite[3]; v.cardBadges = new FxQuad[3];
            v.cardNames = new StoryLabel[3]; v.cardLines = new StoryLabel[3]; v.cardLevels = new StoryLabel[3];
            for (int i = 0; i < 3; i++)
            {
                var p = MakePanel("ScampCard " + i, panels, CW, CH, Ui.Pal.Orange, "", "");
                p.transform.localPosition = new Vector3(-0.9f + i * 0.13f, -0.2f, 0);      // authoring pose only
                Hidden(p.bg); Hidden(p.glow); Hidden(p.bar);
                var b = AddPill(p, "Card", "", Vector3.zero, CW, CH, 0.004f, honey);
                b.hoverGrow = 0.07f; b.radius = CH * 0.5f; b.half = new Vector2(CW * 0.5f, CH * 0.5f);
                // (the button's own quad is the light behind the card: unseen until a finger or the ray is on it)
                b.color = new Color(1f, 0.86f, 0.46f, 0f); b.color2 = new Color(1f, 0.86f, 0.46f, 0f);
                Ui.SizePanel(b.bg, CW + 0.018f, CH + 0.018f, 0.020f, 0f);
                b.bg.order = 49; b.bg.SetColors(b.color, b.color2); b.bg.transform.localPosition = new Vector3(0, 0, 0.003f);
                Transform c = b.transform;
                var paper = Sprite("Paper", c, "glass-card", 0.1185f, 0.1657f, 50);
                paper.transform.localPosition = new Vector3(0, -0.002f, 0.001f);
                v.cardBadges[i] = Fx("Badge", c, 0, Ui.Pal.Orange, new Vector4(9f, 0, 0, 0), 51, Ui.Pal.Orange);
                v.cardBadges[i].transform.localPosition = new Vector3(0, 0.0420f, -0.001f); v.cardBadges[i].transform.localScale = Vector3.one * 0.050f;
                v.cardIcons[i] = Sprite("Picture", c, "glass-ab-star", 0.043f, 0.043f, 52);
                v.cardIcons[i].transform.localPosition = new Vector3(0, 0.0420f, -0.003f);
                v.cardNames[i] = Ordered(Label("Name", c, "Quick Sparks", 0.0118f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), 53);
                v.cardNames[i].transform.localPosition = new Vector3(0, -0.0030f, -0.004f);
                v.cardLines[i] = Ordered(Label("Line", c, "Bolts fire faster", 0.0088f, Ui.Pal.TextDim), 53);
                v.cardLines[i].transform.localPosition = new Vector3(0, -0.0290f, -0.004f);
                v.cardLevels[i] = Ordered(Label("Level", c, "new", 0.0082f, new Color(0.66f, 0.42f, 0.16f), TextAnchor.MiddleCenter, true), 53);
                v.cardLevels[i].transform.localPosition = new Vector3(0, -0.0580f, -0.004f);
                RaiseOrder(p.transform, B);
                v.cards[i] = p; v.cardButtons[i] = b;
                p.gameObject.SetActive(false);
            }
            var art = new Texture2D[SwarmAug.Count];
            for (int i = 0; i < art.Length; i++) art[i] = Art("demon-charm-" + (i + 1).ToString("00"));
            v.augArt = art;

            // ---- the result: what the run came to, Play again, Close
            const float RW = 0.31f, RH = 0.185f;
            var res = MakePanel("ScampResult", panels, RW, RH, Ui.Pal.Orange, "", "");
            res.transform.localPosition = new Vector3(-0.9f, -0.45f, 0);      // authoring pose only
            Ui.SizePanel(res.bg, RW, RH, 0.026f, 0.0020f);
            Hidden(res.bar);
            v.resultTitle = Label("Result Title", res.transform, "You popped Big Grump!", 0.0200f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.resultTitle.transform.localPosition = new Vector3(0, 0.058f, -0.002f);
            v.resultLine = Label("Result Line", res.transform, "Time 0:00     Level 1     Popped 0", 0.0112f, Ui.Pal.TextDim);
            v.resultLine.transform.localPosition = new Vector3(0, 0.024f, -0.002f);
            v.resultScore = Label("Result Score", res.transform, "Score 0", 0.0145f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.resultScore.transform.localPosition = new Vector3(0, -0.004f, -0.002f);
            v.again = AddPill(res, "Btn Again", "Play again", new Vector3(-0.066f, -0.054f, 0), 0.136f, 0.040f, 0.0140f, Ui.Pal.Orange);
            v.close = AddPill(res, "Btn Close", "Close", new Vector3(0.082f, -0.054f, 0), 0.100f, 0.040f, 0.0140f, Ui.Pal.Mint);
            RaiseOrder(res.transform, B + 60);
            v.result = res;
            res.gameObject.SetActive(false);

            Wire(app, "_swarm", v);
        }

        /// <summary>
        /// PcCharacter/Swarm (Windows/PcSwarm.cs): the monsters of Screen Scamps as the PC client shows them BEHIND THE GLASS of
        /// a monitor - a pool of 24 (the same prop material; their shapes are given at run time), six rings of light for
        /// where one comes out, and a glow. On a layer of their own: only PcApp.SwarmLate draws them, into its own window.
        /// </summary>
        static void AuthorPcSwarm(PcApp app, Transform t)
        {
            Color honey = new Color(1f, 0.84f, 0.42f, 1f), cream = new Color(1f, 0.97f, 0.90f, 1f);
            var swarm = Node("Swarm", t);
            var v = swarm.AddComponent<PcSwarmView>();
            v.root = swarm;
            v.gate = Sprite("Plumwood Gate", swarm.transform, "demon-gate", 1f, 1f, -5);
            v.monsters = new Transform[SwarmGlass.Max]; v.monsterMesh = new MeshFilter[SwarmGlass.Max]; v.monsterRend = new MeshRenderer[SwarmGlass.Max];
            for (int i = 0; i < SwarmGlass.Max; i++)
            {
                var go = Node("Monster " + i, swarm.transform);
                var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = _ballMesh;        // a stand-in: the app gives it its kind's shape
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _demonFelt; mr.enabled = false; mr.sortingOrder = 10;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                go.transform.localPosition = new Vector3((i % 8 - 3.5f) * 0.07f, (i / 8 - 1f) * 0.07f, 0.5f); go.transform.localScale = Vector3.one * 0.06f;
                v.monsters[i] = go.transform; v.monsterMesh[i] = mf; v.monsterRend[i] = mr;
            }
            v.rings = new FxQuad[6];
            for (int i = 0; i < v.rings.Length; i++)
            {
                v.rings[i] = Hidden(Fx("Glass Ring " + i, swarm.transform, 6, honey, new Vector4(0.25f, 0, 0.3f, 0), 12, cream));
                v.rings[i].transform.localPosition = new Vector3(0, 0, 0.4f); v.rings[i].transform.localScale = Vector3.one * 0.15f;
            }
            v.glow = Hidden(Fx("Glow", swarm.transform, 8, new Color(1f, 0.88f, 0.56f, 0.25f), new Vector4(2.2f, 0, 0, 0), 1, new Color(1f, 0.97f, 0.86f, 0.25f)));
            v.glow.transform.localPosition = new Vector3(0, 0, 1.2f); v.glow.transform.localScale = new Vector3(0.8f, 0.5f, 1f);
            foreach (var tr in swarm.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = PcApp.SwarmLayer;
            swarm.SetActive(false);
            Wire(app, "_swarmView", v);
        }
    }
}
