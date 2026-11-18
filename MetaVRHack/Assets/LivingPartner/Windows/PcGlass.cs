using System;
using System.Collections;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using UnityEngine.Networking;
using Pose = LivingPartner.Shared.Pose;

namespace LivingPartner.Windows
{
    /// <summary>
    /// GLASS GAMES inside the screen (Shared/Glass.cs). When the host says that a glass game is on and that this side
    /// runs it (the partner is in the PC), the monitor it is played on becomes the field: the desktop stays visible
    /// under a light veil of paper, the partner stands on the bottom
    /// edge, and the user plays from outside with the hands - they come in as shadows (PcShadow.cs), sized to exactly
    /// what they touch. This file steps the simulation (GlassGames.Create), draws its sprite list with the pictures
    /// authored under PcCharacter/Glass/Game (a pool of quads, labels and three cards - nothing is created here),
    /// draws the partner itself from petX / petY / petPose, writes the HUD, plays the sounds and tells the host how
    /// the game stands. When the host clears the game, or the partner is no longer in the PC, the monitor is given back.
    /// THE PICTURES: the game's list is drawn back to front with a pool of 160 pictures and 32 words. A picture is a
    /// built-in one (GlassArt), a plain white shape tinted with the sprite's colour (rect, disc, hoop - what a
    /// scripted game, GlassScript, draws with) or one of the pack's own ("pack:name", fetched from the host).
    /// A FLAT game (GlassGame.flat: it lies on the desk when the headset draws it) is drawn here like any other, as a
    /// picture seen from above: its pictures lie BEHIND the partner, which stands on them at petX, petY. So do the
    /// pictures of every scripted game (MrApp.GlassPetInFront): a board or a background must not hide the partner.
    /// Only in the two built-in games is what closes in on the partner drawn in front of it.
    /// </summary>
    public sealed partial class PcApp
    {
        [Header("Glass games (PcCharacter/Glass/Game)")]
        [SerializeField] GameObject _glGameRoot;
        [SerializeField] BallView[] _glFootballs;
        [Tooltip("The sheet of paper over the desktop while a game is on.")]
        [SerializeField] FxQuad _glVeil;
        [SerializeField] FxQuad _glPetShadow;
        [Tooltip("The strip of paper the partner stands on, and the ink line along its top.")]
        [SerializeField] FxQuad _glGround, _glGroundLine;
        [Tooltip("The pool the sprite list is drawn with, back to front.")]
        [SerializeField] UiSprite[] _glSprites;
        [SerializeField] StoryLabel[] _glLabels;
        [SerializeField] FxQuad[] _glLabelBgs;
        [Tooltip("The three cards of the survival game's choice: the paper, the power's picture, the ring that fills, two lines.")]
        [SerializeField] Transform[] _glCards;
        [SerializeField] UiSprite[] _glCardBg, _glCardIcon;
        [SerializeField] FxQuad[] _glCardRing;
        [SerializeField] StoryLabel[] _glCardName, _glCardNote;
        [Header("Glass games: the HUD")]
        [SerializeField] Transform _glHud;
        [SerializeField] FxQuad _glHudBg;
        [SerializeField] StoryLabel _glTitle, _glScore, _glLevel, _glTime;
        [SerializeField] UiSprite[] _glHearts;
        [SerializeField] UiSprite _glScoreStar;
        [SerializeField] FxQuad _glBarBg, _glBar;
        [SerializeField] FxQuad _glBannerBg;
        [SerializeField] StoryLabel _glBanner;
        [SerializeField] Transform _glResult;
        [SerializeField] FxQuad _glResultBg;
        [SerializeField] StoryLabel _glResultTitle, _glResultLine, _glResultScore;
        [Header("Glass games: pictures and sound")]
        [Tooltip("One picture for each name in GlassArt.All, in that order (Art/Ui/glass-<name>.png).")]
        [SerializeField] Texture2D[] _glArt;
        [Tooltip("The powers of the survival game, in the order of GlassSurvival.AbilityIds.")]
        [SerializeField] Texture2D[] _glAbilityArt;
        [SerializeField] AudioSource[] _glSfx;

        // the game as the host has it (who runs it, which one), and the one that runs here
        GlassState _gl = new GlassState();
        bool _glLocal; int _glLocalRun; GamePackDef _glLocalPack; Func<GlassGame> _glLocalMake;      // started by `lpctl debug glassStart`: the host knows nothing of it
        GlassGame _glGame; GamePackDef _glPack; MonitorDesc _glMon;
        string _sayText = "";                          // what the partner said last, as it came (PcApp shows it wrapped for its own small bubble)
        bool _glOn, _glBot, _glBotLoses; float _glSpeed = 1f; int _glRun; string _glPhase = "", _glTitleText = "";
        float _glFade, _glLeaveT = -1f, _glRefusedUntil, _glOverT, _glPetX, _glPetY, _glWalk, _glPickA, _glYaw = 180f;
        double _glReportAt;
        int _glShown, _glLabelsShown, _glCardsShown, _glMaxLives, _glStartLives, _glSeen, _glHudKey = int.MinValue, _glSounds;
        string _glBannerShown = null, _glLineShown = ""; bool _glSayShown, _glBannerSmall;
        readonly GlassInput _glInput = new GlassInput();
        readonly string[] _glShapeWas = new string[2];      // what each hand showed last (null: not seen), for the log
        readonly Pose _glTarget = new Pose(), _glBlend = new Pose();
        Texture2D _glEnemyTex, _glPickupTex;
        static readonly Dictionary<string, int> ArtIndex = MakeArtIndex();
        static Dictionary<string, int> MakeArtIndex() { var d = new Dictionary<string, int>(); for (int i = 0; i < GlassArt.All.Length; i++) d[GlassArt.All[i]] = i; return d; }

        // THROUGH THE GLASS (Shared/Glass.cs: GlassOut): what the game sends out goes to the host (-> the headsets, which fly it
        // through the room), the verdicts come back; AIMING: the pointed spot is the aim, the pointing hand's pinch the trigger.
        bool _glClock = true, _glPinchWas; int _glOutsSent, _glOutsBack, _glTestOuts;
        GamePackDef _glRidePack; int _glRideRun;       // debug glassRide: the run the host has on plays THIS pack here instead of its own
        float _botFireT;

        // (phase "moving": the partner is on its way through the screen and the game follows it - the host has paused the run;
        //  the monitor is given back, and the run that begins on the other side - or here again - is a new one)
        bool GlassWanted => _gl.game.Length > 0 && _gl.side == Env.Pc && _gl.phase != "moving" && (GlassGames.Is(_gl.game) || _glLocal);

        /// <summary>glass.out.result from a headset, through the host: what became of something this game sent out of the screen.</summary>
        void OnGlassOutResult(GlassOutResult r)
        {
            if (r == null || !_glOn || r.runId != _glRun) return;
            _glOutsBack++;
            _glGame.OutResult(r.id, r.what);
            Log($"glass: out {r.id} -> {r.what} ({_glOutsBack} verdicts for {_glOutsSent} sent out)");
        }

        /// <summary>After every Step: what the game sent out toward the user leaves this side - to the host, for the headsets.</summary>
        void GlassOutsTake(GlassGame g)
        {
            var outs = g.outs;
            if (outs.Count == 0) return;
            for (int i = 0; i < outs.Count; i++)
            {
                var o = outs[i];
                if (o == null) continue;
                o.runId = _glRun; _glOutsSent++;
                if (_link.Connected) _link.Send(Msg.GlassOut, o);
                Log($"glass: out {o.id} leaves the screen at ({o.x:0.00}, {o.y:0.00}): {o.art}, {o.speed:0.00} m/s, {o.size * 100f:0} cm{(_glLocal ? " (a local game: the host does not pass it on)" : "")}");
            }
            outs.Clear();
        }

        /// <summary>The glass game as the host has it (in every snapshot, and on its own when it changes).</summary>
        void OnGlassState(GlassState g)
        {
            if (g == null || _glLocal) return;
            if (g.game != _gl.game || g.runId != _gl.runId || g.side != _gl.side)
                Log(g.game.Length > 0 ? $"glass: the host says {g.game} '{g.title}' ({g.packId}) on the {g.side} side, run {g.runId}{(g.monitorId.Length > 0 ? " on " + g.monitorId : "")}" : "glass: the host says no game");
            _gl = g;
        }

        /// <summary>The connection to the host is gone: a game does not outlive it.</summary>
        void GlassOnConnection()
        {
            if (!_glLocal) _gl = new GlassState();
        }

        /// <summary>The floor of the field is drawn this far above the monitor's bottom edge (in field units), so that feet are not cut by it.</summary>
        const float GlassFloor = LivingPartner.MR.MrApp.GlassPcFloor;
        /// <summary>
        /// A FLAT game is a picture seen from above, and all of its field has to be seen: it is drawn at this share of
        /// the monitor's height, in the middle, its bottom edge this far above the monitor's (so that its top edge ends
        /// under the sign, which hangs down to 0.90).
        /// </summary>
        const float GlassFlatScale = LivingPartner.MR.MrApp.GlassPcFlatScale, GlassFlatBottom = LivingPartner.MR.MrApp.GlassPcFlatBottom;      // (the headset has to know them: it flies what leaves the field from that spot of the real screen)
        /// <summary>How the field of the game that runs lies on the monitor: its scale (1; a flat one GlassFlatScale) and how far its y = 0 is above the monitor's bottom edge (in units of the monitor's height).</summary>
        float _glK = 1f, _glY0 = GlassFloor;
        /// <summary>Metres of the glass layer per unit of the FIELD of the game that runs (GU: per unit of the monitor's height).</summary>
        float FU => GU * _glK;
        /// <summary>A point of the field in the glass layer.</summary>
        Vector3 FieldAt(float x, float y, float z) => new Vector3((x - _glGame.W * 0.5f) * FU, (y * _glK + _glY0 - 0.5f) * GU, z);
        /// <summary>A point of the monitor (fractions from its top-left corner, as the hands come) in the field, and back.</summary>
        void FieldFromUv(float u, float v, out float x, out float y) { float W = _glGame.W; x = W * 0.5f + (u - 0.5f) * W / _glK; y = (1f - v - _glY0) / _glK; }
        void UvFromField(float x, float y, out float u, out float v) { float W = _glGame.W; u = 0.5f + (x - W * 0.5f) * _glK / W; v = 1f - (y * _glK + _glY0); }
        /// <summary>A point of the monitor itself (x 0..W, y 0..1 from its bottom edge) in the glass layer: for the HUD.</summary>
        Vector3 ScreenAt(float x, float y, float z) => new Vector3((x - _glGame.W * 0.5f) * GU, (y - 0.5f) * GU, z);

        static string GlassTitle(string template) => template == "survival" ? "Vampire Night" : template == "umbrella" ? "Shadow Umbrella" : template;

        void GlassEnter()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _glRefusedUntil) return;
            var mon = ScreenMath.FindMonitor(_mons, _gl.monitorId) ?? _mon;
            bool ride = !_glLocal && _glRidePack != null && _glRideRun == _gl.runId;
            var game = ride ? GlassGames.Create(GlassScript.TemplateName) : _glLocal && _glLocalMake != null ? _glLocalMake() : GlassGames.Create(_gl.game);
            if (game == null || !GlassOverlayFor(mon)) { _glRefusedUntil = now + 5f; Log($"glass: {_gl.game} cannot be played here now"); return; }
            _glMon = mon; _glGame = game; _glRun = _gl.runId;
            game.W = mon.bounds.w / (float)mon.bounds.h;
            _glPack = ride ? _glRidePack : _glLocal ? _glLocalPack : null;
            var games = _link.Snapshot != null && _link.Snapshot.world != null ? _link.Snapshot.world.games : null;
            if (_glPack == null && games != null && _gl.packId.Length > 0) _glPack = games.Find(p => p != null && p.id == _gl.packId);
            game.inside = true;                                            // (it is played inside a monitor: what it sends out comes out of the screen)
            if (game is GlassScript script) script.Rounded = true;         // (GlassArt.Paper is drawn here: the boxes of a tool have round corners)
            game.Start(_glPack, _gl.runId);
            // the game FOLLOWED the partner through the screen: the run begins with what it had on the other side
            if (!_glLocal && LivingPartner.MR.MrApp.GlassCarried(_gl))
            {
                game.score = _gl.score; game.level = Mathf.Max(1, _gl.level); game.lives = Mathf.Max(1, _gl.lives); game.seconds = Mathf.Max(0f, _gl.seconds);
                Log($"glass: it goes on from the other side of the glass: score {game.score}, level {game.level}, lives {game.lives}, {game.seconds:0} s");
            }
            _glClock = LivingPartner.MR.MrApp.GlassSignClock(_glPack, game);
            _glPinchWas = false; _glOutsSent = _glOutsBack = 0; _botFireT = 0f;
            // (a tool - a calculator - is drawn a little smaller, so that its panel ends under the sign and not behind it)
            _glK = LivingPartner.MR.MrApp.GlassPcScale(_glPack, game.flat); _glY0 = game.flat ? GlassFlatBottom : GlassFloor;
            _glStartLives = game.lives; _glSeen = game is GlassScript ? LivingPartner.MR.MrApp.GlassSignSeen(_glPack) : 0;
            _glTitleText = _gl.title.Length > 0 ? _gl.title : _glPack != null && !string.IsNullOrEmpty(_glPack.title) ? _glPack.title : GlassTitle(game.Template);
            _glOn = true; _glFade = 0f; _glLeaveT = -1f; _glPhase = ""; _glReportAt = 0; _glOverT = 0f; _glMaxLives = game.lives;
            _glPetX = game.petX; _glPetY = game.petY; _glWalk = 0f; _glPickA = 0f; _glYaw = 180f; _glHudKey = int.MinValue; _glBannerShown = null; _glSounds = 0; _botHasGoal = false;
            _glEnemyTex = _glPickupTex = null;
            _glBlend.CopyFrom(_pose);
            if (!_glRoot.activeSelf) _glRoot.SetActive(true);
            _glGameRoot.SetActive(true);
            for (int i = 0; i < _glSprites.Length; i++) if (_glSprites[i].enabled) _glSprites[i].enabled = false;
            for (int i = 0; i < _glLabels.Length; i++) { _glLabels[i].text = ""; if (_glLabelBgs[i].enabled) _glLabelBgs[i].enabled = false; }
            for (int i = 0; i < _glCards.Length; i++) _glCards[i].gameObject.SetActive(false);
            _glShown = _glLabelsShown = _glCardsShown = 0;
            _glResult.gameObject.SetActive(false);
            _glBanner.text = ""; _glBannerBg.enabled = false;
            _glVeil.enabled = true;
            GlassSfxInit();
            Log($"glass: {game.Template} '{_glTitleText}' begins on {mon.id} ({mon.bounds.w}x{mon.bounds.h}px, field {game.W:0.00} x 1), run {_glRun}, pack {(_glPack != null ? _glPack.id : "none")}, lives {game.lives}{(_glLocal ? " (started here by the debug command)" : "")}");
        }

        void GlassLeave(string why)
        {
            if (!_glOn) return;
            Log($"glass: {_glGame.Template} run {_glRun} closed ({why}); score {_glGame.score}, level {_glGame.level}, {_glGame.seconds:0} s, {_glSounds} sounds");
            _glOn = false; _glGame = null; _glLeaveT = -1f; _glFade = 0f;
            _glGameRoot.SetActive(false);
            _rig.transform.localScale = Vector3.one;
            _fallV = 0; _nextWander = Time.realtimeSinceStartupAsDouble + 8;
        }

        /// <summary>One frame of the game. Returns whether the partner is shown.</summary>
        bool GlassFrame(float dt)
        {
            var g = _glGame;
            _mode = "Glass";
            bool riding = !_glLocal && _glRidePack != null && _glRideRun == _glRun && _glPack == _glRidePack;      // (debug glassRide: this run plays another pack than the host's)
            bool leaving = !GlassWanted || _gl.runId != _glRun || (_gl.game != g.Template && !riding);
            if (leaving && _glLeaveT < 0f) _glLeaveT = 0f;
            if (_glLeaveT >= 0f)
            {
                _glLeaveT += dt;
                if (_glLeaveT >= 0.35f) { GlassLeave(GlassWanted ? "another run begins" : "the host cleared the game"); return Roam(dt, false); }
            }
            _glFade = Mathf.Clamp01(_glFade + (_glLeaveT >= 0f ? -dt / 0.3f : dt / 0.4f));

            // ---- the hands, the step, the sounds
            if (_glBot && _glLeaveT < 0f) GlassBot(dt, g);
            bool here = _ghMon != null && _ghMon.id == _glMon.id;
            for (int i = 0; i < 2; i++)
            {
                bool v = here && _hsLive[i];
                // the newest report of this hand: what it shows and how fast it moves - also of a hand that is seen but too
                // far from the glass to act (GlassHandPoint.seen), which a game may ask about (GlassScript all_hands)
                var hp = here && Time.realtimeSinceStartup - _ghAt < HandStale && _gh.hands != null && _gh.hands.Length > i ? _gh.hands[i] : null;
                bool seen = v || (hp != null && (hp.seen || hp.valid) && !float.IsNaN(hp.u) && !float.IsNaN(hp.v));
                _glInput.valid[i] = v; _glInput.seen[i] = seen;
                _glInput.fingers[i] = seen && hp != null ? hp.fingers : -1;
                _glInput.speed[i] = seen && hp != null && hp.speed > 0f && hp.speed < 50f ? hp.speed : 0f;
                // (one line in the log when what a hand shows changes, when it comes to the glass or leaves it, when it starts
                // or stops moving, and when the one who says so changes: the headset, or a hand made up here - a debug hand, the bot)
                bool made = !_ghFromHeadset || _dbgHand != null || _glBot;
                string shape = seen ? GlassGames.Shape(_glInput.fingers[i]) : null;
                string shown = shape == null ? null : shape + (v ? "|near" : "|far") + (_glInput.speed[i] > 0.15f ? "|moving" : "|still") + (made ? "|made" : "|headset");
                if (shown != _glShapeWas[i])
                {
                    _glShapeWas[i] = shown;
                    Log(shape == null ? $"glass: the {(i == 0 ? "right" : "left")} hand is not seen any more"
                        : $"glass: the {(i == 0 ? "right" : "left")} hand shows '{shape}' (fingers {_glInput.fingers[i]}, {_glInput.speed[i]:0.00} m/s, {(v ? "at the glass" : "away from the glass")}; {(made ? "a hand made up here" : "said by the headset, through the host")})");
                }
                if (v) { FieldFromUv(_hsU[i], _hsV[i], out _glInput.x[i], out _glInput.y[i]); _glInput.dist[i] = _hsDist[i]; _glInput.spread[i] = _hsSpread[i]; }
                else if (seen) { FieldFromUv(hp.u, hp.v, out _glInput.x[i], out _glInput.y[i]); _glInput.dist[i] = Mathf.Max(0f, hp.dist); }
            }
            // AIMING: the spot the user points at from a distance - where the ring is drawn - is the aim of the hand that
            // points, and the moment that hand closes thumb and finger is the trigger (one Step)
            {
                int ah = _gh.pointHand == 1 ? 1 : 0;
                bool aim = here && _ptLive, pinch = aim && _gh.pointPinch;
                _glInput.aimValid[ah] = aim; _glInput.aimValid[1 - ah] = false;
                _glInput.fire[ah] = pinch && !_glPinchWas; _glInput.fire[1 - ah] = false;
                _glPinchWas = pinch;
                if (aim) FieldFromUv(_ptU, _ptV, out _glInput.aimX[ah], out _glInput.aimY[ah]);
            }
            if (_glLeaveT < 0f)
            {
                g.touch = false;                               // (through the glass nothing is touched: a scripted game takes a hand that rests for a tap)
                g.Step(dt * _glSpeed, _glInput);
                GlassOutsTake(g);                              // what it sent out of the screen: to the host, for the headsets
                for (int i = 0; i < g.sounds.Count; i++) GlassSfx(g.sounds[i]);
                // a line the game lets the partner say: over its head, like what the host lets it say
                if (g.petLineLeft > 0f && !string.IsNullOrEmpty(g.petLine)) { if (g.petLine != _glLineShown) { _glLineShown = g.petLine; _sayText = g.petLine; _sayUntil = Time.time + Mathf.Clamp(g.petLineLeft, 1.2f, 8f); } }
                else _glLineShown = "";
            }
            if (g.phase != _glPhase)
            {
                Log($"glass: {g.Template} -> {g.phase} (score {g.score}, lives {g.lives}, level {g.level}{(g.choices.Length > 0 ? ", choices: " + string.Join(" | ", g.choices) : "")}{(g.result.Length > 0 ? ", result: " + g.result : "")})");
                _glPhase = g.phase; _glReportAt = 0;
            }
            GlassReport(g);
            if (g.phase == "over") { _glOverT += dt; if (_glLocal && _glOverT > 8f) { _glLocal = false; _gl = new GlassState(); } }

            // ---- the picture
            float W = g.W, fade = _glFade;
            if (_glVeil.enabled)
            {
                _glVeil.transform.localPosition = new Vector3(0, 0, 0.64f);
                Ui.SizePanel(_glVeil, W * GU * 1.01f, GU * 1.01f, 0f, 0f);
                var paper = new Color(1f, 0.973f, 0.918f, 0.18f * fade);
                _glVeil.SetColors(paper, paper);
            }
            // the ground: a strip of paper from the floor of the field down to the monitor's edge, an ink line on top
            float gh = GlassFloor * GU;
            if (_glGround.enabled == g.flat) { _glGround.enabled = !g.flat; _glGroundLine.enabled = !g.flat; }      // (a flat game is seen from above: there is no floor line)
            _glGround.transform.localPosition = new Vector3(0, -0.5f * GU + gh * 0.5f, 0.50f);
            Ui.SizePanel(_glGround, W * GU * 1.01f, gh, 0f, 0f);
            _glGround.SetColors(new Color(1f, 0.965f, 0.88f, 0.93f * fade), new Color(0.97f, 0.90f, 0.78f, 0.93f * fade));
            _glGroundLine.transform.localPosition = new Vector3(0, -0.5f * GU + gh, 0.498f);
            _glGroundLine.transform.localScale = new Vector3(W * GU * 1.01f, 0.0032f * GU, 1f);
            var groundInk = new Color(0.29f, 0.18f, 0.15f, 0.85f * fade);
            _glGroundLine.SetColors(groundInk, groundInk);
            GlassSprites(g, fade);
            bool petShown = GlassDisplay.IsStockChart(_glPack) ? Roam(dt, false) : GlassPet(g, dt, fade);
            GlassHud(g, fade);
            PlaceHands(_glMon, g, _glInput, fade);
            PlaceLook(_glMon);
            PlaceParcel(null);
            return petShown;
        }

        /// <summary>Renders the monitor of the game: the glass layer and the partner standing in it.</summary>
        void GlassLate()
        {
            if (_glMon == null || !GlassOverlayFor(_glMon)) return;
            bool chart = GlassDisplay.IsStockChart(_glPack);
            GlassRender(!chart);
            if (chart) return;
            if (_ov.Shown && _ovGlass.FramesPresented >= 2) _ov.Hide();      // the small window stays until the field shows the partner
            _dbgLabel.text = "";
            GlassDiag("game");
        }

        // ------------------------------------------------------------------ the sprite list
        Texture2D ArtTexture(string art)
        {
            return art != null && ArtIndex.TryGetValue(art, out int i) && i < _glArt.Length ? _glArt[i] : _glArt[0];
        }

        void GlassSprites(GlassGame g, float fade)
        {
            bool survival = g.Template == "survival";
            if (_glPack != null)
            {
                // the pack's own pictures, once they have arrived from the host
                if (_glEnemyTex == null && !string.IsNullOrEmpty(_glPack.enemyIcon)) _glEnemyTex = WorldIcon(_glPack.enemyIcon);
                if (_glPickupTex == null && !string.IsNullOrEmpty(_glPack.pickupIcon)) _glPickupTex = WorldIcon(_glPack.pickupIcon);
            }
            int n = 0, nl = 0, nc = 0, nf = 0;
            var list = g.sprites;
            for (int k = 0; k < list.Count; k++)
            {
                var s = list[k];
                string art = s.art ?? "";
                if (GlassArt.IsFootball(art) && _glFootballs != null && nf < _glFootballs.Length)
                {
                    _glFootballs[nf++].PlaceFootball(FieldAt(s.x, s.y, 0.04f), s.size * FU, Time.time * 115f + s.rot, fade * s.alpha > 0.04f);
                    continue;
                }
                if (art == GlassArt.Label) { if (nl < _glLabels.Length - 1) GlassLabel(nl++, s, fade); continue; }      // (the last strip of paper is kept for what the partner says)
                if (art == GlassArt.Card) { if (nc < _glCards.Length) GlassCard(nc++, s, fade); continue; }
                if (n >= _glSprites.Length) continue;
                var q = _glSprites[n];
                // How large a picture is drawn: w > 0: exactly w x size; a plain shape (rect, disc, hoop): size x size, they fill
                // their picture; a built-in sticker: `size` is the THING's height and the picture, which has a margin round it, is
                // drawn 1.28 times larger (one of the pack's own pictures: 1.15 times).
                Texture2D tex; float scale = 1.28f, alpha = s.alpha, y = s.y, h, w;
                // (paper: the rounded box of a scripted tool - no picture: the sprite shader draws it from its size and its corner radius)
                bool paperBox = art == GlassArt.Paper;
                bool plain = paperBox || art == GlassArt.Rect || art == GlassArt.Disc || art == GlassArt.RingShape, back = LivingPartner.MR.MrApp.GlassPetInFront(g);
                bool creature = art == GlassArt.Enemy || art == GlassArt.Flyer || art == GlassArt.Brute;
                if (plain) { tex = ArtTexture(paperBox ? GlassArt.Rect : art); scale = 1f; }
                else if (art.StartsWith(GlassArt.PackPrefix, StringComparison.Ordinal))
                {
                    tex = PackTexture(art); scale = 1.15f;
                    if (tex == null) { tex = ArtTexture(GlassArt.Disc); scale = 1f; alpha *= 0.35f; }      // (not here yet: a pale disc holds its place)
                }
                else
                {
                    tex = ArtTexture(art);
                    if (creature && _glEnemyTex != null) { tex = _glEnemyTex; scale = 1.15f; }
                    else if (_glPickupTex != null && (survival ? art == GlassArt.Gem : art == GlassArt.Star)) { tex = _glPickupTex; scale = 1.15f; }
                    else if (art == GlassArt.Ring || art == GlassArt.Frost) scale = 1.09f;
                    else if (art == GlassArt.Sparkle) scale = 1.2f;
                }
                if (s.w > 0f) { w = s.w; h = s.size; }
                else if (art == GlassArt.Bolt) { h = s.size * 1.02f; w = Mathf.Max(s.size * 0.24f, 0.07f); }
                else { h = s.size * scale; w = h; }
                if (creature && s.frame == 1) w = -w;                                 // drawn looking to the right: mirrored for the left
                // back to front in the order of the list; the pictures of a flat game and of every scripted one lie behind the
                // partner (it stands on them, or in front of them) - those of the two built-in games in front of it
                float z = (back ? BackZ : 0.10f) - n * 0.0002f;
                var t = q.transform;
                t.localPosition = FieldAt(s.x, y, z);
                t.localRotation = paperBox ? Quaternion.identity : Quaternion.Euler(0, 0, s.rot);
                t.localScale = new Vector3(w * FU, h * FU, 1f);
                q.round = paperBox ? new Vector4(w * FU, h * FU, Mathf.Clamp(s.fill, 0f, 0.5f) * Mathf.Min(w, h) * FU, 1f) : Vector4.zero;
                // (the frost over a slowed creature is kept light: the creature itself has to stay readable)
                float a = alpha * fade * (art == GlassArt.Frost ? 0.6f : 1f);
                // a plain shape is white and takes the sprite's colour (-1: it stays white); a picture is multiplied by a colour above 0
                q.texture = tex; q.color = (plain ? s.color >= 0 : s.color > 0) ? Tint(s.color, a) : new Color(1f, 1f, 1f, a);
                q.order = SpriteOrder + n;
                if (!q.enabled) q.enabled = true; else q.Apply();
                n++;
            }
            if (_glFootballs != null) for (int i = nf; i < _glFootballs.Length; i++) _glFootballs[i].Visible = false;
            for (int i = n; i < _glShown; i++) _glSprites[i].enabled = false;
            _glShown = n;
            // what the partner says, over its head (the host's line at the start and at the end of a game)
            if (Time.time < _sayUntil && _sayText.Length > 0 && g.phase != "choose" && nl < _glLabels.Length)
            {
                int i = _glLabels.Length - 1;
                var label = _glLabels[i]; var bg = _glLabelBgs[i];
                const float size = 0.042f;
                label.transform.localScale = Vector3.one * size * GU;
                label.transform.localRotation = bg.transform.localRotation = Quaternion.identity;
                label.SetWrapped(_sayText, 0.62f * GU);
                label.color = new Color(Ui.Pal.Ink.r, Ui.Pal.Ink.g, Ui.Pal.Ink.b, fade);
                float w = label.Width + 0.05f * GU, h = label.Height + 0.03f * GU;
                // (kept inside the monitor - which is wider than the field of a flat game)
                float edge = g.W * 0.5f / _glK - w * 0.5f / FU - 0.02f;
                float x = g.W * 0.5f + Mathf.Clamp(g.petX - g.W * 0.5f, -edge, edge);
                Vector3 p = FieldAt(x, g.petY + GlassGames.PetHeight + (0.045f + h * 0.5f / GU) / _glK, 0.026f);
                label.transform.localPosition = p;
                if (!bg.enabled) bg.enabled = true;
                bg.transform.localPosition = p + new Vector3(0, 0, 0.002f);
                Ui.SizePanel(bg, w, h, Mathf.Min(h * 0.5f, 0.03f * GU), 0.004f * GU);
                bg.SetColors(new Color(1f, 0.985f, 0.945f, 0.97f * fade), new Color(0.985f, 0.945f, 0.875f, 0.97f * fade));
                _glSayShown = true;
            }
            else if (_glSayShown) { _glSayShown = false; _glLabels[_glLabels.Length - 1].text = ""; _glLabelBgs[_glLabels.Length - 1].enabled = false; }
            for (int i = nl; i < _glLabelsShown; i++) { _glLabels[i].text = ""; _glLabelBgs[i].enabled = false; }
            _glLabelsShown = nl;
            for (int i = nc; i < _glCardsShown; i++) _glCards[i].gameObject.SetActive(false);
            _glCardsShown = nc;
        }

        /// <summary>What the partner stands on or in front of (a flat game's pictures, a scripted game's) lies this deep in the glass layer: behind the partner (0.30) and its shadow (0.40).</summary>
        const float BackZ = 0.47f;
        /// <summary>The draw order of the first picture of the pool (LPScenePc.cs authors the pool from this number up; the veil and the ground are below it, words, cards, hands and the HUD from 107 up).</summary>
        const int SpriteOrder = -180;
        /// <summary>0xRRGGBB as a colour.</summary>
        static Color Tint(int rgb, float a) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

        /// <summary>One of the pack's own pictures ("pack:name" -> GamePackDef.art), once it has arrived from the host; null until then, or when the pack has none of that name.</summary>
        Texture2D PackTexture(string art)
        {
            var list = _glPack != null ? _glPack.art : null;
            if (list == null || art == null || art.Length <= GlassArt.PackPrefix.Length) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && string.CompareOrdinal(art, GlassArt.PackPrefix.Length, list[i].name, 0, int.MaxValue) == 0) return WorldIcon(list[i].file);
            return null;
        }
        /// <summary>
        /// Words on the field: `size` is the height of the letters (field units), x, y their middle. With a colour above 0
        /// they are bare letters in that colour; without one, ink on a small strip of paper, so that they read on any
        /// wallpaper (the "+3" of the built-in games).
        /// </summary>
        void GlassLabel(int i, GlassSprite s, float fade)
        {
            var label = _glLabels[i]; var bg = _glLabelBgs[i];
            float a = s.alpha * fade;
            bool paper = s.color <= 0;
            label.transform.localScale = Vector3.one * s.size * FU;
            label.text = s.text ?? "";
            label.color = paper ? new Color(Ui.Pal.Ink.r, Ui.Pal.Ink.g, Ui.Pal.Ink.b, a) : Tint(s.color, a);
            Vector3 p = FieldAt(s.x, s.y, 0.06f);
            label.transform.localPosition = p;
            label.transform.localRotation = Quaternion.Euler(0, 0, s.rot);
            if (bg.enabled != paper) bg.enabled = paper;
            if (!paper) return;
            float w = label.Width + 0.5f * s.size * FU, h = 1.15f * s.size * FU;
            bg.transform.localPosition = p + new Vector3(0, 0, 0.002f);
            bg.transform.localRotation = label.transform.localRotation;
            Ui.SizePanel(bg, w, h, h * 0.5f, h * 0.07f);
            bg.SetColors(new Color(1f, 0.985f, 0.945f, 0.96f * a), new Color(1f, 0.93f, 0.72f, 0.96f * a));
        }

        void GlassCard(int i, GlassSprite s, float fade)
        {
            var root = _glCards[i];
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            float a = s.alpha * fade;
            root.localPosition = FieldAt(s.x, s.y, 0.03f - 0.002f * i);
            // (the card is authored one unit high; a hand resting on it makes it swell a little)
            root.localScale = Vector3.one * s.size * FU * Mathf.Lerp(0.9f, 1f, Ui.Pop(s.alpha)) * (1f + 0.05f * s.fill);
            _glCardBg[i].SetColor(new Color(1f, 1f, 1f, a));
            var icon = s.frame >= 0 && s.frame < _glAbilityArt.Length ? _glAbilityArt[s.frame] : null;
            if (icon != null) _glCardIcon[i].SetTexture(icon);
            _glCardIcon[i].SetColor(new Color(1f, 1f, 1f, a));
            _glCardName[i].SetWrapped(s.text ?? "", 0.62f); _glCardName[i].color = new Color(Ui.Pal.Ink.r, Ui.Pal.Ink.g, Ui.Pal.Ink.b, a);
            _glCardNote[i].SetWrapped(s.text2 ?? "", 0.60f); _glCardNote[i].color = new Color(Ui.Pal.TextDim.r, Ui.Pal.TextDim.g, Ui.Pal.TextDim.b, a);
            var ring = _glCardRing[i];
            ring.param = new Vector4(0.86f, 0.12f, Mathf.Clamp01(s.fill), 0f);
            ring.SetColors(new Color(1f, 0.69f, 0.42f, a), new Color(0.96f, 0.45f, 0.42f, a));
        }

        // ------------------------------------------------------------------ the partner on the field
        bool GlassPet(GlassGame g, float dt, float fade)
        {
            float time = Time.time;
            // (how far it went, in the measure of its own legs: the same on a field that is drawn smaller, where it is smaller too; on a flat field it also goes up and down the picture)
            float moved = (g.flat ? new Vector2(g.petX - _glPetX, g.petY - _glPetY).magnitude : Mathf.Abs(g.petX - _glPetX)) * GU; _glPetX = g.petX; _glPetY = g.petY;
            _glWalk += moved / (PoseLib.StrideLength(PoseLib.WalkAmp) * _H);
            string pose = g.petPose ?? "idle";
            _glPickA = Mathf.MoveTowards(_glPickA, pose == "pick" ? 1f : 0f, dt / 0.25f);
            bool walking = pose == "walk" || pose == "run";
            if (g.petCustom && g.petCustomPose != null && !walking) _glTarget.CopyFrom(g.petCustomPose);      // (a motion the script defined, as it is given)
            else GlassPoses.For(pose, _glTarget, time, _glWalk, _glPickA);
            Pose.Lerp(_glBlend, _glTarget, 1f - Mathf.Exp(-dt * (walking ? 40f : 18f)), _glBlend);
            // seen from the side while it walks, turned to the user otherwise (a little towards where it is going)
            float side = g.petFacesRight ? -1f : 1f;
            float yaw = 180f + side * (walking ? 72f : pose == "pick" ? 55f : pose == "down" ? 0f : 16f);
            _glYaw = Mathf.MoveTowardsAngle(_glYaw, yaw, 600f * dt);
            _pose.CopyFrom(_glBlend);
            Vector3 feet = FieldAt(g.petX, g.petY, 0.30f);
            _rig.transform.SetPositionAndRotation(feet, Quaternion.Euler(0, _glYaw, 0));
            _rig.transform.localScale = Vector3.one * _glK * Mathf.Max(0.001f, Ui.Pop(fade));      // (0.20 of the field's height, whatever size the field is drawn at)
            _rig.Apply(_pose);
            _rig.SetGlow(StatusColors.For("idle"), 0f);
            _rig.SetShadow(false, feet, Vector3.up, 0f);       // (its own shadow lies flat on a floor seen edge-on here: a drawn one is used instead)
            bool shadow = fade > 0.3f;
            if (_glPetShadow.enabled != shadow) _glPetShadow.enabled = shadow;
            if (shadow)
            {
                float lift = Mathf.Clamp01(g.petY / 0.2f);
                // (a flat game is seen from above: the shadow lies round its feet, over the game's pictures)
                _glPetShadow.transform.localPosition = g.flat ? FieldAt(g.petX, g.petY + 0.004f, 0.40f) : FieldAt(g.petX, 0.006f, 0.40f);
                if (g.flat) lift = 0f;
                int shadowOrder = g.flat ? SpriteOrder + _glSprites.Length + 1 : SpriteOrder - 8;
                if (_glPetShadow.order != shadowOrder) _glPetShadow.order = shadowOrder;
                _glPetShadow.transform.localScale = new Vector3(0.15f * FU * (1f - 0.3f * lift), (g.flat ? 0.05f : 0.028f) * FU, 1f);
                _glPetShadow.SetColor(new Color(0.30f, 0.20f, 0.24f, 0.34f * fade * (1f - 0.5f * lift)));
            }
            // hit a moment ago: it blinks
            return fade > 0.02f && !(g.petHurt > 0f && g.phase != "over" && Mathf.Repeat(time * 11f, 1f) < 0.42f);
        }

        // ------------------------------------------------------------------ the HUD
        static string Clock(float seconds) { int s = Mathf.Max(0, Mathf.FloorToInt(seconds)); return (s / 60) + ":" + (s % 60).ToString("00"); }

        void GlassHud(GlassGame g, float fade)
        {
            float W = g.W;
            float hk = Mathf.Min(1f, W / 1.34f);
            _glHud.localPosition = ScreenAt(W * 0.5f, 0.945f, 0.02f);
            _glHud.localScale = Vector3.one * GU * hk;
            var sv = g as GlassSurvival;
            float shownTime = g.seconds;
            // (the words are written again only when a number in them has changed)
            // (only what the game uses is on the sign: a script that never counts lives has no hearts - MrApp.GlassSignShows)
            LivingPartner.MR.MrApp.GlassSignShows(g, _glStartLives, ref _glSeen, out bool showHearts, out bool showLevel, out bool showScore);
            int key = g.score * 31 + g.level * 977 + g.lives * 7919 + Mathf.FloorToInt(shownTime) * 104729 + (sv != null ? sv.Wave * 13 : 0) + _glSeen * 15485863;
            if (key != _glHudKey)
            {
                _glHudKey = key;
                _glTitle.text = _glTitleText;
                _glScore.text = showScore ? g.score.ToString() : "";
                _glLevel.text = !showLevel ? "" : sv != null ? $"Lv {g.level}  Wave {Mathf.Max(1, sv.Wave)}" : "Level " + g.level;
                _glTime.text = _glClock ? Clock(shownTime) : "";       // (a tool - keys and no lives - has no clock: its sign is its title)
                if (g.lives > _glMaxLives) _glMaxLives = g.lives;
            }
            Color ink = new Color(Ui.Pal.Ink.r, Ui.Pal.Ink.g, Ui.Pal.Ink.b, fade), dim = new Color(Ui.Pal.TextDim.r, Ui.Pal.TextDim.g, Ui.Pal.TextDim.b, fade);
            _glTitle.color = ink; _glScore.color = ink; _glLevel.color = ink;
            _glTime.color = ink;
            _glHudBg.SetColors(new Color(Ui.Pal.PanelTop.r, Ui.Pal.PanelTop.g, Ui.Pal.PanelTop.b, 0.96f * fade), new Color(Ui.Pal.PanelBottom.r, Ui.Pal.PanelBottom.g, Ui.Pal.PanelBottom.b, 0.96f * fade));
            if (_glScoreStar.enabled != showScore) _glScoreStar.enabled = showScore;
            if (showScore) _glScoreStar.SetColor(new Color(1f, 1f, 1f, fade));
            for (int i = 0; i < _glHearts.Length; i++)
            {
                bool slot = showHearts && i < _glMaxLives;
                if (_glHearts[i].enabled != slot) _glHearts[i].enabled = slot;
                if (slot) _glHearts[i].SetColor(new Color(1f, 1f, 1f, (i < g.lives ? 1f : 0.20f) * fade));
            }
            // one bar under the strip: the way to the next level (survival)
            float bar = sv != null ? Mathf.Clamp01(sv.Xp / (float)Mathf.Max(1, sv.XpNeeded)) : -1f;
            bool hasBar = bar >= 0f && g.phase != "choose";
            if (_glBarBg.enabled != hasBar) { _glBarBg.enabled = hasBar; _glBar.enabled = hasBar; }
            if (hasBar)
            {
                const float bw = 0.50f, bh = 0.016f;
                _glBarBg.SetColors(new Color(0.29f, 0.18f, 0.15f, 0.55f * fade), new Color(0.29f, 0.18f, 0.15f, 0.55f * fade));
                float fw = Mathf.Max(bh, bw * bar);
                _glBar.transform.localPosition = new Vector3(-bw * 0.5f + fw * 0.5f, -0.058f, -0.002f);
                Ui.SizePanel(_glBar, fw, bh, bh * 0.5f, 0f);
                Color c1 = new Color(0.56f, 0.83f, 0.96f, fade);
                _glBar.SetColors(c1, c1);
            }

            // ---- the banner: big, in the middle, for as long as the game has a line (not over the result card)
            // (while a flat game is played its lines stand small under the strip at the top: the middle of the picture is the game)
            bool small = g.flat && g.phase == "play";
            string banner = g.phase == "over" ? "" : g.banner ?? "";
            if (banner != _glBannerShown || small != _glBannerSmall)
            {
                _glBannerShown = banner; _glBannerSmall = small;
                _glBanner.transform.localScale = Vector3.one * GU * (small ? 0.62f : Mathf.Min(1f, W / 1.2f));
                _glBanner.SetWrapped(banner, 0.86f * W * GU);
            }
            bool hasBanner = banner.Length > 0;
            if (_glBannerBg.enabled != hasBanner) _glBannerBg.enabled = hasBanner;
            if (hasBanner)
            {
                float bw = _glBanner.Width + 0.09f * GU, bh = _glBanner.Height + 0.045f * GU;
                // (a flat game's field reaches up to the sign: its small line is laid over the sign for as long as it is there, so that no row of the field is hidden)
                Vector3 p = ScreenAt(W * 0.5f, small ? Mathf.Min(0.945f, 0.992f - bh * 0.5f / GU) : g.phase == "choose" ? 0.85f : 0.66f, small ? 0.012f : 0.022f);
                _glBanner.transform.localPosition = p; _glBanner.color = ink;
                _glBannerBg.transform.localPosition = p + new Vector3(0, 0, 0.002f);
                Ui.SizePanel(_glBannerBg, bw, bh, Mathf.Min(bh * 0.5f, 0.045f * GU), 0.006f * GU);
                _glBannerBg.SetColors(new Color(1f, 0.985f, 0.945f, 0.985f * fade), new Color(1f, 0.93f, 0.72f, 0.985f * fade));
            }

            // ---- over: the result on a card of paper
            bool over = g.phase == "over";
            if (_glResult.gameObject.activeSelf != over) _glResult.gameObject.SetActive(over);
            if (over)
            {
                float pop = Ui.Pop(Mathf.Clamp01(_glOverT / 0.45f));
                _glResult.localPosition = ScreenAt(W * 0.5f, 0.56f, 0.016f);
                _glResult.localScale = Vector3.one * GU * Mathf.Min(1f, W / 1.25f) * Mathf.Max(0.001f, pop);
                _glResultTitle.text = "Game over";
                _glResultLine.SetWrapped(g.result ?? "", 0.92f);
                int best = Mathf.Max(_gl.best, g.score);
                _glResultScore.text = !showScore ? "" : _glLocal || _gl.best <= 0 ? "Score " + g.score : $"Score {g.score}   Best {best}";
                _glResultTitle.color = ink; _glResultLine.color = ink; _glResultScore.color = dim;
                _glResultBg.SetColors(new Color(1f, 0.985f, 0.945f, 0.995f * fade), new Color(0.985f, 0.945f, 0.875f, 0.995f * fade));
            }
        }

        // ------------------------------------------------------------------ telling the host
        void GlassReport(GlassGame g)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (_glLocal || now < _glReportAt || !_link.Connected) return;
            _glReportAt = now + 0.2;
            var s = new GlassState { runId = _glRun, game = g.Template, packId = _gl.packId, title = _gl.title, side = Env.Pc, monitorId = _glMon.id, best = _gl.best };
            g.FillState(s);
            _link.Send(Msg.GlassState, s);
        }

        // ------------------------------------------------------------------ sounds
        readonly Dictionary<string, AudioClip> _glClips = new Dictionary<string, AudioClip>();
        volatile Dictionary<string, float[]> _glPcm;        // made once on a worker thread; turned into clips on the main thread
        bool _glSfxAsked, _glSfxReady, _glMute; int _glSfxNext;

        void GlassSfxInit()
        {
            if (_glSfxAsked) return;
            _glSfxAsked = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var all = new Dictionary<string, float[]>();
                    foreach (string n in SfxSynth.Names) all[n] = SfxSynth.Render(n);
                    _glPcm = all;
                }
                catch (Exception e) { Debug.Log("[PC] sound: the effects could not be made: " + e.Message); }
            });
        }

        /// <summary>Once a frame: the samples that were made become clips (sound data, not scene objects).</summary>
        void GlassSfxPump()
        {
            if (_glSfxReady || _glPcm == null) return;
            foreach (var kv in _glPcm)
            {
                if (kv.Value == null || kv.Value.Length == 0) continue;
                var clip = AudioClip.Create("Sfx " + kv.Key, kv.Value.Length, 1, SfxSynth.SampleRate, false);
                clip.SetData(kv.Value, 0);
                _glClips[kv.Key] = clip;
            }
            _glPcm = null; _glSfxReady = true;
            Log($"sound: {_glClips.Count} effects ready");
            foreach (var w in _glSfxWaiting) if (Time.time - w.Value < 0.8f) { _glSounds--; GlassSfx(w.Key); }      // (asked for a moment ago: still worth playing)
            _glSfxWaiting.Clear();
        }
        readonly List<KeyValuePair<string, float>> _glSfxWaiting = new List<KeyValuePair<string, float>>();

        void GlassSfx(string name)
        {
            _glSounds++;
            if (_settings.quietMode || _glMute) return;         // Quiet: no sound effects at all (glassMute: a test run that is not to be heard)
            GlassSfxInit();
            if (!_glSfxReady) { if (_glSfxWaiting.Count < 6) _glSfxWaiting.Add(new KeyValuePair<string, float>(name, Time.time)); return; }
            if (!_glClips.TryGetValue(name, out var clip)) return;
            float volume = name == "tick" ? 0.32f : name == "squeak" ? 0.28f : name == "drop" ? 0.40f : name == "ta-da" || name == "lose" ? 0.62f : 0.50f;
            var src = _glSfx[_glSfxNext]; _glSfxNext = (_glSfxNext + 1) % _glSfx.Length;
            src.pitch = UnityEngine.Random.Range(0.96f, 1.04f);
            src.PlayOneShot(clip, volume);
        }

        // ------------------------------------------------------------------ pictures from the host (/world/)
        readonly Dictionary<string, Texture2D> _wIcons = new Dictionary<string, Texture2D>();
        readonly HashSet<string> _wIconAsked = new HashSet<string>();
        readonly Dictionary<string, float> _wIconFailedAt = new Dictionary<string, float>();

        /// <summary>A picture the host keeps (a thing's sticker, a pack's creature), or null until it has arrived.</summary>
        Texture2D WorldIcon(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (_wIcons.TryGetValue(file, out var have)) return have;
            if (_wIconFailedAt.TryGetValue(file, out float failedAt) && Time.time - failedAt < 10f) return null;
            if (_wIconAsked.Add(file)) StartCoroutine(FetchWorldIcon(file));
            return null;
        }

        IEnumerator FetchWorldIcon(string file)
        {
            string ws = _link.CurrentUrl ?? "";
            string http = ws.Replace("wss://", "https://").Replace("ws://", "http://");
            int cut = http.LastIndexOf("/ws", StringComparison.Ordinal);
            if (cut > 0) http = http.Substring(0, cut);
            if (http.Length == 0) { _wIconAsked.Remove(file); _wIconFailedAt[file] = Time.time; yield break; }
            using (var req = UnityWebRequest.Get(http + "/world/" + UnityWebRequest.EscapeURL(file).Replace("+", "%20")))
            {
                req.timeout = 20;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Log($"world: could not fetch {file}: {req.error}"); _wIconAsked.Remove(file); _wIconFailedAt[file] = Time.time; yield break; }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Icon " + file, wrapMode = TextureWrapMode.Clamp };
                if (tex.LoadImage(req.downloadHandler.data, true)) { _wIcons[file] = tex; Log($"world: icon {file} arrived ({tex.width}x{tex.height})"); }
                else { Destroy(tex); _wIconAsked.Remove(file); _wIconFailedAt[file] = Time.time; }
            }
        }

        // ------------------------------------------------------------------ the bot (a hand for seeing the games without a headset)
        float _botX = 0.9f, _botY = 0.5f, _botDist = 0.05f;
        float _botGoalX, _botGoalY, _botTouch; bool _botHasGoal;      // a scripted game: where the bot's hand is going, how long it has been down there

        /// <summary>
        /// `lpctl debug glassBot 1`: a simple hand plays instead of the headset's. It is fed in like a real one, so it is
        /// drawn as a shadow and acts through the same input. Survival: to the nearest creature close to the partner,
        /// else to a gem, and it rests on the first card while choosing. Umbrella: held over the partner, lifted away
        /// for a golden drop. Any other game (a script): it wanders over the field and touches down here and there.
        /// </summary>
        void GlassBot(float dt, GlassGame g)
        {
            float W = g.W, gx = g.petX, gy = 0.34f, gd = 0.05f, speed = 1.7f;
            bool point = false, fire = false;
            var list = g.sprites;
            if (g.phase == "over" || g.phase == "intro") { gx = W * 0.5f + 0.25f; gy = 0.45f; gd = 0.2f; }
            else if (_glBotLoses && g.phase != "choose")
            {
                // the clumsy hand: in survival it is held high over the nearest creature, so that the partner walks into it;
                // in the others it rests in a corner and does nothing
                gx = W * 0.12f; gy = 0.80f; gd = g is GlassSurvival ? 0.05f : 0.40f;
                if (g is GlassSurvival)
                {
                    float best = 99f;
                    for (int i = 0; i < list.Count; i++)
                        if ((list[i].art == GlassArt.Enemy || list[i].art == GlassArt.Flyer || list[i].art == GlassArt.Brute) && Mathf.Abs(list[i].x - g.petX) < best) { best = Mathf.Abs(list[i].x - g.petX); gx = list[i].x; }
                }
            }
            else if (g is GlassSurvival sv)
            {
                if (g.phase == "choose") { gx = sv.CardX(0, Mathf.Max(1, g.choices.Length)); gy = GlassSurvival.CardY; }
                else
                {
                    float best = 0.55f * 0.55f, bestGem = 9f; bool foe = false, gem = false; float ex = 0, ey = 0, mx = 0, my = 0;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var s = list[i];
                        float dx = s.x - g.petX, dy = s.y - GlassGames.PetMidY, d2 = dx * dx + dy * dy;
                        if (s.art == GlassArt.Enemy || s.art == GlassArt.Flyer || s.art == GlassArt.Brute) { if (d2 < best) { best = d2; foe = true; ex = s.x; ey = s.y; } }
                        else if (s.art == GlassArt.Gem && d2 < bestGem) { bestGem = d2; gem = true; mx = s.x; my = s.y; }
                    }
                    if (foe) { gx = ex; gy = ey; } else if (gem) { gx = mx; gy = my; } else { gx = g.petX + 0.16f; gy = 0.26f; }
                }
            }
            else if (g is GlassUmbrella)
            {
                // close over the partner's head, under the drop that will land on it first (a slanted one is met
                // where it will be); lifted away when that one is golden: it goes through
                const float hy = 0.30f;
                gx = g.petX; gy = hy;
                float first = 9f; bool goldNext = false;
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    bool ink = s.art == GlassArt.Drop || s.art == GlassArt.Blob, gold = s.art == GlassArt.Gold;
                    if ((!ink && !gold) || s.y < hy - 0.03f) continue;
                    float at = s.x + Mathf.Tan(s.rot * Mathf.Deg2Rad) * (s.y - hy);        // where it will cross the hand's height
                    if (Mathf.Abs(at - g.petX) > 0.17f || s.y - hy >= first) continue;
                    first = s.y - hy; goldNext = gold;
                    gx = ink ? Mathf.Clamp(at, g.petX - 0.09f, g.petX + 0.09f) : g.petX;
                }
                if (goldNext && first < 0.45f) { gd = 0.40f; gy = hy + 0.10f; }
            }
            else if (g is GlassScript shot && shot.ReadShots)
            {
                // a game that is SHOT at: the bot points at the nearest target (a creature, one of the pack's pictures or a disc
                // of some size that is not ink) from a distance and pulls the trigger when the ring is on it
                float best = 9f; bool any = false; gx = W * 0.5f; gy = 0.6f; speed = 2.2f;
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i]; string a = s.art ?? "";
                    bool target = a == GlassArt.Enemy || a == GlassArt.Flyer || a == GlassArt.Brute || a.StartsWith(GlassArt.PackPrefix, StringComparison.Ordinal)
                                  || (a == GlassArt.Disc && s.size >= 0.09f && s.color != LuaSandbox.Ink && s.alpha > 0.5f);
                    if (!target) continue;
                    float d = Mathf.Abs(s.x - _botX) + Mathf.Abs(s.y - _botY);
                    if (d < best) { best = d; gx = s.x; gy = s.y; any = true; }
                }
                point = true;
                _botFireT = Mathf.Max(0f, _botFireT - dt);
                if (any && best < 0.02f && _botFireT <= 0f) _botFireT = 0.45f;      // (closed for 0.12 s, then open again for a third of a second)
                fire = _botFireT > 0.33f;
            }
            else
            {
                // a game nobody here knows (a script): to a place, down on it for most of a second (a hand that rests is a tap), up again, to the next
                if (!_botHasGoal) { _botHasGoal = true; _botGoalX = W * (0.12f + 0.76f * (float)_botRandom.NextDouble()); _botGoalY = 0.12f + 0.76f * (float)_botRandom.NextDouble(); _botTouch = 0f; }
                gx = _botGoalX; gy = _botGoalY; speed = 1.1f;
                bool there = Mathf.Abs(gx - _botX) + Mathf.Abs(gy - _botY) < 0.012f;
                gd = there ? 0.005f : 0.12f;
                if (there && Mathf.Abs(_botDist - gd) < 0.003f) { _botTouch += dt; if (_botTouch > 0.8f) _botHasGoal = false; }
            }
            Vector2 to = new Vector2(gx - _botX, gy - _botY); float left = to.magnitude;
            if (left > 1e-4f) { float step = Mathf.Min(left, speed * dt * Mathf.Clamp(left / 0.12f, 0.25f, 1f)); _botX += to.x / left * step; _botY += to.y / left * step; }
            _botDist = Mathf.MoveTowards(_botDist, gd, dt * 1.2f);
            var h = new GlassHand { monitorId = _glMon.id, t = _link.HostNow };
            UvFromField(_botX, _botY, out float bu, out float bv);
            if (point) { h.pointValid = true; h.pointU = bu; h.pointV = bv; h.pointPinch = fire; }      // (a hand that points from a distance: no shadow, the ring)
            else h.hands[0] = new GlassHandPoint { valid = true, u = bu, v = bv, dist = _botDist, spread = 1f, roll = 7f * Mathf.Sin(Time.time * 1.3f) };
            FeedHand(h, _glMon);
        }

        readonly System.Random _botRandom = new System.Random(20261117);

        /// <summary>The first words a game has on its field just now (for the log: what a tool's display shows).</summary>
        static string GlassWords(GlassGame g)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            for (int i = 0; i < g.sprites.Count && n < 4; i++)
                if (g.sprites[i].art == GlassArt.Label && !string.IsNullOrEmpty(g.sprites[i].text)) { if (n++ > 0) sb.Append(" | "); sb.Append(g.sprites[i].text); }
            return sb.ToString();
        }

        /// <summary>
        /// lpctl debug pcShot 0 0 0 "&lt;absolute path of a .png&gt;": what this client shows on the partner's monitor just now,
        /// saved as a picture - the frames its windows were given last (the glass layer, the game overlay of tennis, the
        /// partner's own small window), laid over a plain colour that stands for the desktop. It is the only way to see
        /// a PARKED client (-lp-park), and it never reads a screen.
        /// </summary>
        void PcShot(string path)
        {
            var m = _glOn && _glMon != null ? _glMon : _ovGlassMon != null ? _ovGlassMon : _mon;
            if (m == null || string.IsNullOrEmpty(path)) { Log("debug: pcShot needs a monitor and a path"); return; }
            int w = m.bounds.w, h = m.bounds.h;
            var px = new Color32[w * h];
            var desk = new Color32(104, 124, 146, 255);
            for (int i = 0; i < px.Length; i++) px[i] = desk;
            bool glass = _ovGlass != null && _ovGlass.BlendInto(px, w, h, m.bounds);
            bool game = _ovGame != null && _ovGame.BlendInto(px, w, h, m.bounds);
            WorkBlend(px, w, h, m.bounds);      // the work queue panel's own window (PcWork.cs)
            SwarmBlend(px, w, h, m.bounds);     // the monsters behind the glass (PcSwarm.cs)
            bool own = _ov != null && _ov.BlendInto(px, w, h, m.bounds);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);      // (picture data, not a scene object)
            try
            {
                tex.SetPixels32(px); tex.Apply(false);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Log($"debug: pcShot {path}: {m.id} {w}x{h}, glass layer {(glass ? "yes" : "no")}, game overlay {(game ? "yes" : "no")}, the partner's window {(own ? "yes" : "no")}{(LayeredOverlay.ParkX != 0 ? "; the windows are parked " + LayeredOverlay.ParkX + " px to the side" : "")}");
            }
            catch (Exception e) { Log("debug: pcShot failed: " + e.Message); }
            finally { Destroy(tex); }
        }

        // ------------------------------------------------------------------ debug commands (lpctl debug <cmd> <value> <px> <py> <text>)
        void GlassDebug(DebugCommand d)
        {
            switch (d.cmd)
            {
                case "glassBot":
                    _glBot = d.value > 0.5f; _glBotLoses = d.value > 1.5f;      // (2 = a clumsy hand that plays to lose, for seeing a game end - the same as on the headset)
                    if (!_glBot) _ghAt = -10f;
                    Log("debug: the bot's hand " + (_glBot ? (_glBotLoses ? "plays to lose" : "plays") : "is gone"));
                    break;
                case "glassXp":
                    if (_glGame is GlassSurvival sv) { sv.GrantXp(Mathf.Max(1, Mathf.RoundToInt(d.value))); Log($"debug: {Mathf.RoundToInt(d.value)} experience granted"); }
                    break;
                case "glassSpeed": _glSpeed = Mathf.Clamp(d.value, 0f, 4f); Log($"debug: the game runs at {_glSpeed:0.00} of its pace"); break;      // (0 = it stands still: for a capture)
                case "glassLives":
                    if (_glOn) { _glGame.lives = Mathf.Clamp(Mathf.RoundToInt(d.value), 1, GlassGameBase.MaxLives); Log($"debug: {_glGame.lives} lives"); }
                    break;
                case "glassChoose":
                    if (_glOn) _glGame.Choose(Mathf.Clamp(Mathf.RoundToInt(d.value), 0, 2));
                    break;
                case "glassStart":
                {
                    // a game without the host: lpctl debug glassStart 0 0 0 survival|umbrella|script:<path of a .lua file on this PC>|
                    //   test:flat|test:upright|test:flat:full|stop
                    // (the headset has the same command for a game on the desk: both get it - the one the partner is with plays)
                    string text = (d.text ?? "").Trim(), what = text.ToLowerInvariant(), arg = "";
                    int colon = text.IndexOf(':');
                    if (colon > 0) { arg = text.Substring(colon + 1).Trim(); what = text.Substring(0, colon).Trim().ToLowerInvariant(); }
                    bool inPc = _ch.owner != Env.Mr;
                    if (!(GlassGames.Is(what) || what == "script" || what == "test" || what == "example") || !inPc)
                    {
                        if (_glLocal) { _glLocal = false; _glLocalPack = null; _glLocalMake = null; _gl = _link.Snapshot != null && _link.Snapshot.glass != null ? _link.Snapshot.glass : new GlassState(); Log("debug: the local glass game is stopped"); }
                        break;
                    }
                    GamePackDef pack = null; Func<GlassGame> make = null; string title = GlassTitle(what);
                    if (what == "example")
                    {
                        // example:<id> - one of GlassScriptExamples (keeper | shooter | calculator | omok ...), as the pack it would be
                        var ex = GlassScriptExamples.Find(arg.ToLowerInvariant());
                        if (ex == null) { Log($"debug: there is no example '{arg}'"); break; }
                        pack = GlassScriptExamples.Pack(ex); title = ex.title; what = GlassScript.TemplateName;
                    }
                    else if (what == "script")
                    {
                        // (a file on this PC, for trying a script before the host has it)
                        string code;
                        try { code = System.IO.File.ReadAllText(arg); }
                        catch (Exception e) { Log($"debug: the script '{arg}' could not be read: {e.Message}"); break; }
                        if (!GlassGames.Is("script")) { Log("debug: this build has no scripted games yet (GlassGames knows no 'script')"); break; }
                        title = System.IO.Path.GetFileNameWithoutExtension(arg);
                        pack = new GamePackDef { id = "debug-script", template = "script", title = title, script = code };
                    }
                    else if (what == "test")
                    {
                        string mode = arg.ToLowerInvariant();
                        title = "Test Card";
                        pack = new GamePackDef { id = "debug-test", template = "test", title = title };
                        make = () => new LivingPartner.MR.GlassTestCard(mode.StartsWith("flat"), mode.EndsWith("full"));
                    }
                    _glLocal = true; _glLocalPack = pack; _glLocalMake = make;
                    _gl = new GlassState { game = what, title = title, side = Env.Pc, monitorId = _mon != null ? _mon.id : "", runId = 900000 + ++_glLocalRun };
                    Log("debug: a local glass game: " + what + (arg.Length > 0 ? " (" + arg + ")" : ""));
                    break;
                }
                case "glassRide":
                {
                    // lpctl debug glassRide 0 0 0 <example id>: the run the HOST has on inside this PC plays that example instead of its
                    // own pack - under the host's run number, so that what it sends out of the screen really goes through the host
                    // to the headsets and the verdicts really come back (a local game's would be dropped by the host). "off": as it was.
                    var ex = GlassScriptExamples.Find((d.text ?? "").Trim().ToLowerInvariant());
                    if (ex == null || _glLocal || _gl.game.Length == 0 || _gl.side != Env.Pc) { _glRidePack = null; Log("debug: glassRide - " + (ex == null ? "no such example (the ride is off)" : "the host has no game on inside this PC")); break; }
                    _glRidePack = GlassScriptExamples.Pack(ex); _glRideRun = _gl.runId;
                    if (_glOn) GlassLeave("the debug command: it begins again as '" + ex.title + "'");
                    Log($"debug: run {_gl.runId} of the host plays the example '{ex.id}' here");
                    break;
                }
                case "glassOutTest":
                    // lpctl debug glassOutTest [speed m/s] [x] [y]: the game that runs here sends one thing out of the screen (from the middle of the field)
                    if (_glOn)
                    {
                        _glGame.outs.Add(new GlassOut { id = 9000 + ++_glTestOuts, x = d.px > 0f ? d.px : _glGame.W * 0.5f, y = d.py > 0f ? d.py : 0.5f, speed = d.value > 0f ? d.value : 0.7f, art = GlassArt.Orb, size = 0.07f });
                        Log("debug: one thing is sent out of the screen");
                    }
                    break;
                case "glassInfo":
                    if (_glOn)
                    {
                        var gs = _glGame as GlassScript;
                        Log($"glass info: {_glGame.Template} '{_glTitleText}' run {_glRun} phase {_glGame.phase} score {_glGame.score} lives {_glGame.lives} level {_glGame.level} t={_glGame.seconds:0.0}s; {_glGame.sprites.Count} sprites ({_glShown} pictures, {_glLabelsShown} words); inside={_glGame.inside}; " +
                            $"aim {(_glInput.aimValid[0] ? $"({_glInput.aimX[0]:0.00}, {_glInput.aimY[0]:0.00})" : _glInput.aimValid[1] ? $"({_glInput.aimX[1]:0.00}, {_glInput.aimY[1]:0.00})" : "-")}; out {_glOutsSent} sent, {_glOutsBack} verdicts; clock {(_glClock ? "shown" : "hidden")}" +
                            (gs != null ? $"; script: kit={gs.UsedKit} aim={gs.ReadAim} shots={gs.ReadShots} outs={gs.UsedOuts} sent={gs.OutsSent} refused={gs.OutsRefused}{(gs.Error.Length > 0 ? " ERROR " + gs.Error : "")}; words: {GlassWords(_glGame)}" : ""));
                    }
                    else Log($"glass info: no game runs here; the host says game='{_gl.game}' side='{_gl.side}' run={_gl.runId} phase='{_gl.phase}'");
                    break;
                case "pcShot": PcShot(d.text); break;
                case "pcHand": DebugHand(d); break;      // (not "glassHand": that is the simulated HEADSET hand - Windows/MrSim.cs - and both clients get every debug command)
                case "glassMute": _glMute = d.value > 0.5f; Log("debug: the games' sounds are " + (_glMute ? "muted" : "heard")); break;
                case "show": DebugShow(d); break;
            }
        }
    }
}
