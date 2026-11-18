using System;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using Pose = LivingPartner.Shared.Pose;

namespace LivingPartner.MR
{
    /// <summary>
    /// GLASS GAMES OUT ON THE DESK (Shared/Glass.cs is the contract). The games themselves are simulations in Shared:
    /// GlassSurvival, GlassUmbrella - and GlassScript, a small program the partner writes when the user asks for a
    /// game, which draws with plain shapes, words and pictures. The PC decides which game, under which run
    /// number and on which side of the glass; when the side is "mr" the game is simulated and drawn HERE:
    ///  - THE STAGE: an upright field standing on the desk through the spot the partner's things are laid out round
    ///    (WorldFrame), turned to the user when the game begins and fixed after that. One unit of the field is
    ///    GU metres. At its full size the partner's own height is GlassGames.PetHeight (0.20) of the field's, so that
    ///    the REAL partner is exactly the game's partner: 0.50 m high and 0.89 m wide for the 10 cm partner. That is
    ///    used when the user sits 0.53 m or more from the stage; nearer than that the field is made smaller, so that
    ///    it stays within GlassViewDeg of the user's view (down to 0.35 m x 0.62 m). Things on the desk stay.
    ///  - A FLAT game (GlassGame.flat - a script says so) is not stood up: its field LIES on the desk, 0.62 x 0.35 m,
    ///    x to the user's right and y away from the user, 2 mm above the desk, in front of the partner's spot (its
    ///    near edge never nearer than 20 cm to the user). The pictures lie flat in the order of the list, the words
    ///    lie flat and read from the user's side; the sign, the banner and the result card stand behind the far edge.
    ///  - THE HANDS are the user's real ones. Upright: the middle of each palm dropped straight onto the plane of the
    ///    field (x, y in field units, the distance from the plane in metres, how open the hand is). Flat: the tip of
    ///    each index finger - x, y where it is over the field, dist = its height above the desk in metres. A soft
    ///    ring on the field shows where a hand is (nothing is drawn on the hand itself).
    ///  - THE PARTNER is the real one, moved and posed by the game: in the two built-in games it stands a little behind
    ///    the plane of the pictures (what closes in on it is seen in front of it); in every other game - a script - a
    ///    little in FRONT of that plane, so that nothing the script draws (a board, a background) can hide it; on a
    ///    flat field it walks on the desk, petX / petY being where its feet are. A motion the script defined
    ///    (petCustom) is shown as it is given; a line the script lets it say (petLine) is its caption.
    ///  - THE PICTURES: what the game lists is drawn with a pool of 160 pictures and 32 words, back to front in the
    ///    order of the list. A picture is one of the built-in ones (GlassArt), a plain white shape tinted with the
    ///    sprite's colour (rect, disc, hoop), or one of the pack's own ("pack:name", fetched from the PC). The sign
    ///    above the field, the banner, the cards and the result card are paper.
    ///  - The PC is told how the game stands five times a second, and at once when its phase changes.
    /// When the side is "pc" the game is drawn and played inside the monitor (PcGlass): nothing is drawn on the desk,
    /// a small sign near that screen says where to play, for four seconds.
    /// Debug (lpctl debug ...): glassBot 1|0 (a bot's hand plays; 2 = it plays to lose; in a scripted game it wanders
    /// and touches down here and there), glassXp n (survival: experience), glassChoose i, glassLives n (so many hearts
    /// are left), glassPalm dist x y [off] (a palm held at that place of the field - on a flat field a fingertip, dist
    /// above the desk), glassInfo (one line in the log), and - a game on the desk that the PC knows nothing of -
    /// glassStart 0 0 0 survival|umbrella|script:&lt;path of a .lua file on this machine&gt;|test:flat|test:upright|stop.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Glass games on the desk")]
        [SerializeField] GlassStageView _stage;

        /// <summary>What _game is while a glass game runs out here (every "is a game on?" in this app then says yes).</summary>
        const string GlassLocal = "glass";
        /// <summary>The partner's feet are this far behind the plane of the pictures (away from the user), so that nothing of a picture is hidden in its body - or, in a scripted game, this far in front of it.</summary>
        const float GlassBehind = 0.030f, GlassInFront = 0.055f;
        /// <summary>
        /// Is the partner drawn OVER the game's pictures (both renderers ask)? Yes on a flat field - it stands on them -
        /// and in every scripted game: a script that draws a board or a background must not lose the partner behind it.
        /// No in the two built-in games, where what closes in on the partner is seen in front of it.
        /// </summary>
        public static bool GlassPetInFront(GlassGame g) => g.flat || !(g is GlassSurvival || g is GlassUmbrella);
        /// <summary>
        /// What the sign shows of a game (both renderers ask): the hearts, the level and the score. The built-in games
        /// show all three. A scripted game shows only what its script has used - the hearts once it has called
        /// lives(...), the level after level(...), the star and the score after score(...) or add(...): a board game that
        /// counts nothing has a sign with its title and the clock.
        /// (GlassScript says what was used in UsedLives / UsedLevel / UsedScore. A build whose GlassScript does not have
        /// them yet is asked what it can tell: `seen` remembers, bit by bit, that the lives, the level or the score has
        /// ever been different from what a game begins with - startLives hearts, level 1, score 0.)
        /// </summary>
        public static void GlassSignShows(GlassGame g, int startLives, ref int seen, out bool hearts, out bool level, out bool score)
        {
            if (!(g is GlassScript)) { hearts = level = score = true; return; }
            if (g.lives != startLives) seen |= 1;
            if (g.level != 1) seen |= 2;
            if (g.score != 0) seen |= 4;
            if (UsedLives != null && (bool)UsedLives.GetValue(g)) seen |= 1;
            if (UsedLevel != null && (bool)UsedLevel.GetValue(g)) seen |= 2;
            if (UsedScore != null && (bool)UsedScore.GetValue(g)) seen |= 4;
            hearts = (seen & 1) != 0; level = (seen & 2) != 0; score = (seen & 4) != 0;
        }
        static readonly System.Reflection.FieldInfo UsedLives = UsedFlag("UsedLives"), UsedLevel = UsedFlag("UsedLevel"), UsedScore = UsedFlag("UsedScore");
        static System.Reflection.FieldInfo UsedFlag(string name) { var f = typeof(GlassScript).GetField(name); return f != null && f.FieldType == typeof(bool) ? f : null; }

        /// <summary>
        /// A picture that is thinner than a pixel and a half in the viewer's eye is drawn in pieces (a board's lines broke
        /// up into dashes when seen from further away, or at a slant). This says how thick it has to be drawn instead -
        /// and how much fainter, so that it keeps its weight. size: its thickness in metres; axis: the direction it is
        /// thin in (world, unit length); at: where it is; eye: where it is seen from; pixel: the angle of one pixel of
        /// the view (radians).
        /// </summary>
        public static float GlassThin(float size, Vector3 axis, Vector3 at, Vector3 eye, float pixel, out float alpha)
        {
            alpha = 1f;
            Vector3 to = at - eye; float dist = to.magnitude;
            if (!(dist > 1e-4f) || !(size > 0f)) return size;
            float along = Vector3.Dot(axis, to) / dist;                             // (seen at a slant it is thinner still)
            float seen = Mathf.Sqrt(Mathf.Max(0.04f, 1f - along * along));
            float need = 1.5f * pixel * dist / seen;
            if (size >= need) return size;
            alpha = Mathf.Max(0.25f, size / need);
            return need;
        }
        /// <summary>The angle of one pixel of the view (radians): the headset's eye, or the simulator's window.</summary>
        float GlassPixel => Cam != null && Cam.pixelHeight > 0 ? 2f * Mathf.Tan(Mathf.Clamp(Cam.fieldOfView, 20f, 140f) * 0.5f * Mathf.Deg2Rad) / Cam.pixelHeight : 0.0010f;
        /// <summary>A palm lower than this over the desk and further than GlassRestAway from the plane is a hand at rest (on the desk, the keyboard): it does not play.</summary>
        const float GlassRestLow = 0.030f, GlassRestAway = 0.08f;

        /// <summary>The field is no wider than this in the user's view (degrees, from where the head is when the game begins), and never smaller than GlassSmallest of its full size.</summary>
        const float GlassViewDeg = 80f, GlassSmallest = 0.70f;

        /// <summary>
        /// Metres of the room for one unit of a game's field, decided when a game begins. At the full size the partner
        /// is as tall on the field as it is on the desk (_H / PetHeight: 0.5 for the 10 cm partner).
        /// </summary>
        float GU => _gsUnit;
        float _gsUnit = 0.5f;

        static readonly GlassState NoGlass = new GlassState();
        GlassState GlassNow => _gsLocal != null ? _gsLocal : _snap != null && _snap.glass != null ? _snap.glass : NoGlass;
        /// <summary>A game started here by `lpctl debug glassStart` (the PC knows nothing of it, and is told nothing), and its pack.</summary>
        GlassState _gsLocal; GamePackDef _gsLocalPack; int _gsLocalRuns; Func<GlassGame> _gsLocalMake;

        // the game that runs here
        GlassGame _gsGame; GamePackDef _gsPack; int _gsRun, _gsEndedRun = int.MinValue, _gsSeenRun = int.MinValue;
        string _gsPhase = "", _gsTitle = "";
        readonly GlassInput _gsInput = new GlassInput();
        double _gsReportAt; int _gsFrame = -1, _gsReports, _gsSounds;
        float _gsOverT, _gsFade, _gsBeganAt;
        Vector3 _gsOrigin; Quaternion _gsRot = Quaternion.identity; float _gsYawUser;
        // the partner on the field
        readonly Pose _gsTarget = new Pose(), _gsBlend = new Pose();
        int _gsPetFrame = -1; float _gsArrive, _gsArriveTime, _gsWalk, _gsPickA, _gsYaw; Vector3 _gsFrom, _gsLastFeet;
        // the picture
        int _gsShown, _gsLabelsShown, _gsCardsShown, _gsMaxLives, _gsStartLives, _gsSeen, _gsHudKey = int.MinValue;
        string _gsBannerShown, _gsLineShown = "";
        static readonly Dictionary<string, int> GlassArtIndex = MakeGlassArtIndex();
        static Dictionary<string, int> MakeGlassArtIndex() { var d = new Dictionary<string, int>(); for (int i = 0; i < GlassArt.All.Length; i++) d[GlassArt.All[i]] = i; return d; }
        // the sign of a game that is played inside a screen
        int _gsPcRun = int.MinValue; float _gsPcUntil;
        // debug: the bot's hand, a palm held somewhere
        bool _gsBot, _gsBotLoses, _gsPalm; float _botX = 0.9f, _botY = 0.5f, _botDist = 0.2f, _gsPalmX, _gsPalmY, _gsPalmDist;
        int _gsPalmFingers = -1; string _gsPalmPath = "";      // what the palm of a check shows (finger bits; -1 = not known) and how it moves: "" | circle | sweep
        // how fast each hand moves, as the game is told (GlassInput.speed): measured on the very point that plays - the palm, the fingertip on a lying field
        readonly HandSpeed[] _gsSpeed = { new HandSpeed(), new HandSpeed() };
        // the clock on the sign (not for a tool); the run is paused while the partner goes through the screen (the host's phase "moving")
        bool _gsClock = true, _gsFrozen;
        /// <summary>A glass game is on, on either side of the glass: a pinch while pointing is its trigger, not an order to the partner.</summary>
        bool GlassHoldsPinch { get { var g = GlassNow; return g.game.Length > 0 && g.phase != "over"; } }

        // ------------------------------------------------------------------ what the PC says
        /// <summary>glass.state from the PC: which game is on, on which side (also in every snapshot; this one comes at once).</summary>
        void OnGlassState(GlassState g)
        {
            if (g == null || _snap == null) return;
            var was = GlassNow;
            if (g.game != was.game || g.runId != was.runId || g.side != was.side)
                Debug.Log(g.game.Length > 0 ? $"[MR] glass: the PC says {g.game} '{g.title}' ({g.packId}) on the {g.side} side, run {g.runId}{(g.monitorId.Length > 0 ? " on " + ShortMon(g.monitorId) : "")}" : "[MR] glass: the PC says no game");
            _snap.glass = g;
        }

        /// <summary>glass.command from the PC to the side that runs the game: a card was chosen by words or from the PC.</summary>
        void OnGlassCommand(GlassCommand c)
        {
            if (c == null || _gsGame == null || c.cmd != "choose") return;
            Debug.Log($"[MR] glass: card {c.pick} chosen ({c.how})");
            _gsGame.Choose(c.pick);
        }

        /// <summary>Asks the PC to start a glass game (a pack's id, a title or a way of playing). The PC decides on which side of the glass it is played, and says so when it cannot be.</summary>
        void GlassAsk(string what, string how)
        {
            if (!_link.Connected || _snap == null) { Caption("I lost the PC - is the app still running there?", 4f); Sfx2D("error", 0.5f); return; }
            if (_tnPendingAt >= 0f) { Caption("We are already playing!", 3f); Sfx2D("error", 0.5f); return; }      // (on its way to a ball game the PC does not know of yet)
            _link.Send(Msg.GlassCommand, new GlassCommand { cmd = "start", game = what ?? "", how = Shorten(how, 40) });
            Debug.Log($"[MR] glass: asked the PC to start '{what}' ({how})");
        }

        /// <summary>Ends a glass game, whichever side of the glass it is played on: the PC is told, and the stage on the desk goes.</summary>
        void GlassStop(string why)
        {
            if (_gsLocal != null) { _gsLocal = null; _gsLocalPack = null; _gsLocalMake = null; }
            else if (_link.Connected && _snap != null && GlassNow.game.Length > 0)
            {
                _link.Send(Msg.GlassCommand, new GlassCommand { cmd = "stop", how = Shorten(why, 40) });
                Debug.Log($"[MR] glass: asked the PC to stop {GlassNow.packId} ({why})");
            }
            if (_gsGame != null) GlassEnd(why);
        }

        // ------------------------------------------------------------------ begin and end
        void GlassSync(bool hostOk)
        {
            var g = GlassNow;
            bool on = hostOk && g.game.Length > 0;
            bool here = on && g.side == Env.Mr && (GlassGames.Is(g.game) || _gsLocal != null);
            // a game at the glass begins: one of the headset's own gives way
            if (on && g.runId != _gsSeenRun)
            {
                _gsSeenRun = g.runId;
                if (_game.Length > 0 && _game != GlassLocal) EndLocalGame("a game at the glass begins");
            }
            if (_gsGame != null)
            {
                // (phase "moving": the partner is going through the screen and the game FOLLOWS it - the host has paused this run;
                //  it stays here, frozen, until the run on the other side begins - or the host ends it because the crossing failed)
                bool moving = on && here && g.runId == _gsRun && g.phase == "moving";
                string why = !hostOk ? "the PC is gone" : !on ? "the PC cleared the game" : !here ? "it is played inside the screen now" : g.runId != _gsRun ? "another run begins"
                    : _ch.owner != Env.Mr && !moving && !GlassDisplay.IsStockChart(_gsPack) ? "the partner left the room" : null;
                if (why != null)
                {
                    bool tell = hostOk && here && g.runId == _gsRun && _gsLocal == null;      // (it still runs as far as the PC knows: it is told)
                    GlassEnd(why);
                    if (tell) _link.Send(Msg.GlassCommand, new GlassCommand { cmd = "stop", how = why });
                }
            }
            if (_gsGame == null && here && _ch.owner == Env.Mr && _homeSet && g.runId != _gsEndedRun && g.phase != "over" && g.phase != "moving") GlassBegin(g);
        }

        void GlassBegin(GlassState g)
        {
            var game = _gsLocal != null && _gsLocalMake != null ? _gsLocalMake() : GlassGames.Create(g.game);
            if (game == null) { _gsEndedRun = g.runId; Debug.Log($"[MR] glass: this build cannot play '{g.game}'"); return; }
            if (_game.Length > 0 && _game != GlassLocal) EndLocalGame("a game on the desk begins");
            GamePackDef pack = _gsLocal != null ? _gsLocalPack : null;
            var games = World.games;
            if (pack == null && games != null && g.packId.Length > 0) foreach (var p in games) if (p != null && p.id == g.packId) { pack = p; break; }
            game.W = 16f / 9f;
            game.inside = false;                                           // (on the desk: what it sends out goes up from the stage)
            if (game is GlassScript script) script.Rounded = true;         // (GlassArt.Paper is drawn here: the boxes of a tool have round corners)
            game.Start(pack, g.runId);
            // the game FOLLOWED the partner out of the screen: the run begins with what it had in there
            if (_gsLocal == null && GlassCarried(g))
            {
                game.score = g.score; game.level = Mathf.Max(1, g.level); game.lives = Mathf.Max(1, g.lives); game.seconds = Mathf.Max(0f, g.seconds);
                Debug.Log($"[MR] glass: it goes on from the other side of the glass: score {game.score}, level {game.level}, lives {game.lives}, {game.seconds:0} s");
            }
            _gsClock = GlassSignClock(pack, game); _gsFrozen = false;
            _gsAimDbg = false; _gsBotPoint = false; _gsBotFire = _gsBotFireWas = false; _gsBotFireT = 0f;
            _gsGame = game; _gsPack = pack; _gsRun = g.runId; _gsPhase = ""; _gsReportAt = 0; _gsReports = 0; _gsSounds = 0; _gsOverT = 0f; _gsFade = 0f; _gsBeganAt = Time.time;
            _gsTitle = g.title.Length > 0 ? g.title : pack != null && !string.IsNullOrEmpty(pack.title) ? pack.title : game.Template;
            _game = GlassLocal; _pack = pack; _emoteT = 0f;

            // everything of the game before is cleared away, whichever kind it was
            var v = _stage;
            for (int i = 0; i < v.sprites.Length; i++) if (v.sprites[i].enabled) v.sprites[i].enabled = false;
            for (int i = 0; i < v.labels.Length; i++) { v.labels[i].text = ""; if (v.labelBgs[i].enabled) v.labelBgs[i].enabled = false; }
            for (int i = 0; i < v.cards.Length; i++) v.cards[i].gameObject.SetActive(false);
            for (int i = 0; i < 2; i++) { v.handRing[i].enabled = false; v.handFill[i].enabled = false; v.aimRay[i].enabled = false; _gsAimShow[i] = 0f; _gsAimOn[i] = false; }
            foreach (var q in v.aimParts) q.enabled = false;
            v.botHand.enabled = false;
            v.result.gameObject.SetActive(false);
            v.banner.text = ""; v.bannerBg.enabled = false;
            _gsShown = _gsLabelsShown = _gsCardsShown = 0; _gsMaxLives = _gsStartLives = game.lives; _gsSeen = game is GlassScript ? GlassSignSeen(pack) : 0; _gsHudKey = int.MinValue; _gsBannerShown = null;

            // where it is played: on the desk through the spot the things are laid out round, turned to the user
            WorldFrame();
            Vector3 o = _worldFrame ? _worldHome : _home; o.y = _deskY;
            Vector3 toUser = Input.HeadPos - o; toUser.y = 0;
            if (toUser.sqrMagnitude < 1e-4f) toUser = -_worldFwd;
            toUser.Normalize();
            _gsRot = Quaternion.LookRotation(-toUser, Vector3.up); _gsYawUser = YawTo(toUser);
            Vector3 flat = Input.HeadPos - o; flat.y = 0;
            float full = _H / GlassGames.PetHeight;
            _gsFlat = game.flat;
            _gsHandTool = GlassDisplay.IsTool(pack);
            _gsGrip = -1;
            _gsSpan = 0f;
            string where;
            if (_gsFlat)
            {
                // a flat field lies on the desk in front of the partner's spot when there is room for that, never in the user's
                // lap; the stage's own frame then has its origin in the middle of the field's FAR edge (the sign stands there)
                _gsUnit = _gsHandTool ? 0.18f : GlassFlatUnit;
                float dHome = flat.magnitude, half = _gsUnit * 0.5f;
                float middle = Mathf.Min(Mathf.Max(dHome - half - 0.06f, GlassFlatNear + half), dHome + 0.03f);      // how far its middle is from the user
                _gsOrigin = o + toUser * (dHome - middle - half);
                // ... and where the desk is FREE: when the partner's things stand there, the field is laid to the left, to the
                // right or nearer to the user instead (5 cm steps, up to 25 cm) - where the least of it lies under a thing
                Vector3 right = _gsRot * Vector3.right;
                float best = GlassFlatCovered(game, _gsOrigin), bestS = 0f, bestN = 0f, bestShift = 0f, first = best;
                for (int n = 0; n <= 5 && best > 1e-6f; n++)
                {
                    float near = n * 0.05f;
                    if (n > 0 && middle - near < GlassFlatNear + half) break;      // (never into the user's lap)
                    for (int k = n == 0 ? 1 : 0; k <= 10; k++)
                    {
                        float side = ((k + 1) / 2) * 0.05f * (k % 2 == 1 ? -1f : 1f), shift = Mathf.Abs(side) + near;
                        float covered = GlassFlatCovered(game, _gsOrigin + right * side + toUser * near);
                        // (less under a thing wins; of two places that are as free as each other, the one nearer to where it would have lain)
                        if (covered < best - 1e-6f || (covered < best + 1e-6f && shift < bestShift - 1e-4f)) { best = covered; bestS = side; bestN = near; bestShift = shift; }
                    }
                }
                _gsOrigin += right * bestS + toUser * bestN; middle -= bestN;
                // SQUARE TO THE USER where it lies now: its near edge across the line from the user's head to its middle ("it
                // lies twisted" was a field that had been turned to the head as seen from the partner's spot and was then laid
                // somewhere else - beside that spot, where the desk was free). It is turned round its own middle.
                Vector3 centre = _gsOrigin + toUser * half, toHead = Input.HeadPos - centre; toHead.y = 0f;
                float turned = 0f;
                if (toHead.sqrMagnitude > 1e-4f)
                {
                    toHead.Normalize();
                    turned = Vector3.SignedAngle(toUser, toHead, Vector3.up);
                    _gsRot = Quaternion.LookRotation(-toHead, Vector3.up); _gsYawUser = YawTo(toHead);
                    _gsOrigin = centre - toHead * half; middle = new Vector2(Input.HeadPos.x - centre.x, Input.HeadPos.z - centre.z).magnitude;
                }
                string moved = bestShift <= 1e-4f ? "no place within 25 cm is freer" : $"moved {Mathf.Abs(bestS) * 100f:0} cm to the {(bestS < 0f ? "left" : "right")} and {bestN * 100f:0} cm nearer";
                string free = first <= 1e-6f ? "" : best <= 1e-6f ? $"; {moved}: things stood where it would have lain" : $"; {moved}, {best * 1e4f:0} cm2 of it still lie under things (they are drawn pale)";
                where = $"the field lies on the desk, {game.W * _gsUnit:0.00} x {_gsUnit:0.00} m, its middle {middle:0.00} m from the user and square to them (turned {turned:0} degrees for that), its far edge at {_gsOrigin.ToString("0.00")}{free}";
            }
            else
            {
                // how large the stage is: the partner's own size when the user sits far enough to see the whole field, smaller when nearer
                _gsOrigin = o;
                float fits = 2f * flat.magnitude * Mathf.Tan(GlassViewDeg * 0.5f * Mathf.Deg2Rad) / game.W;
                _gsUnit = Mathf.Clamp(fits, full * GlassSmallest, full);
                if (_gsHandTool)
                {
                    _gsUnit = 0.18f;
                    _gsOrigin = Input.HeadPos - toUser * 0.48f - Vector3.up * 0.20f;
                }
                float U0 = GU;
                where = $"the stage is {game.W * U0:0.00} x {U0:0.00} m ({(U0 >= full - 1e-4f ? "the partner's own size" : $"{U0 / full * 100f:0}% of it: the user is near")}) at {_gsOrigin.ToString("0.00")}";
            }
            v.stage.transform.SetPositionAndRotation(_gsOrigin, _gsRot);
            v.hud.gameObject.SetActive(!_gsHandTool);
            if (!v.stage.activeSelf) v.stage.SetActive(true);
            v.veil.enabled = !_gsFlat; v.floor.enabled = true;

            // the partner walks from where it is to its place on the field
            _gsFrom = new Vector3(_pos.x, _deskY, _pos.z); _gsLastFeet = _gsFrom; _gsArrive = 0f;
            _gsArriveTime = Mathf.Clamp(Vector3.Distance(_gsFrom, GlassFeet(game)) / 0.30f, 0.25f, 1.2f);
            _gsYaw = _rot.eulerAngles.y; _gsWalk = 0f; _gsPickA = 0f; _gsBlend.CopyFrom(_pose);
            _botX = game.W * 0.5f + 0.2f; _botY = 0.5f; _botDist = 0.2f; _botTouch = 0f; _botHasGoal = false;
            Debug.Log($"[MR] glass: {game.Template} '{_gsTitle}' begins on the desk, run {_gsRun}, pack {(pack != null ? pack.id : "none")}{(_gsLocal != null ? " (started here by the debug command)" : "")}, lives {game.lives}; {where}, {flat.magnitude:0.00} m in front of the user{(_gsBot ? "; the bot's hand plays" : "")}");
        }

        void GlassEnd(string why)
        {
            var g = _gsGame;
            if (g == null) return;
            Debug.Log($"[MR] glass: {g.Template} run {_gsRun} closed ({why}); phase {g.phase}, score {g.score}, level {g.level}, {g.seconds:0} s, {_gsReports} reports, {_gsSounds} sounds");
            _gsEndedRun = _gsRun; _gsGame = null; _gsPack = null;
            if (_game == GlassLocal) { _game = ""; _pack = null; _emoteT = 0f; }
            var v = _stage;
            if (v.stage.activeSelf) v.stage.SetActive(false);
            GlassThings(null, 0f);      // (the things that stood in the field are themselves again)
            if (_ch.owner == Env.Mr && _mode == Mode.Free && _gsPetFrame >= Time.frameCount - 2)
            {
                // back on its own feet where the game left it, then home (or back to what it was doing with its things)
                _pos.y = _deskY; _rot = Quaternion.Euler(0, _gsYaw, 0);
                _motor.Teleport(new Vector2(_pos.x, _pos.z), _gsYaw);
                if (_homeSet) _motor.MoveTo(new Vector2(_home.x, _home.z), false, YawTo(Input.HeadPos - _home));
                _actPhase = 0; _actT = 0f; _actFor = "";
                _sparks.Burst(_pos + Vector3.up * (_H * 0.6f), _gsRot * Vector3.right, Vector3.up, 0.06f, 10);
            }
        }

        // ------------------------------------------------------------------ one frame of the game
        /// <summary>Once a frame, by whoever needs the game first (the partner's pose, or the picture): begins / ends it, reads the hands, steps it, tells the PC.</summary>
        void GlassTick(float dt)
        {
            if (_gsFrame == Time.frameCount) return;
            _gsFrame = Time.frameCount;
            bool hostOk = _link.Connected && _snap != null;
            GlassSync(hostOk);
            var g = _gsGame;
            if (g == null) return;
            bool holding = _gsHandTool && MoveGlassTool(g);
            if (!_gsHandTool || _gsFlat) _gsOrigin.y = _deskY;
            _stage.stage.transform.SetPositionAndRotation(_gsOrigin, _gsRot);
            _gsFade = Mathf.Clamp01(_gsFade + dt / 0.4f);

            // the partner is on its way through the screen: the run is paused - nothing is simulated, nothing is reported
            var hostNow = GlassNow;
            bool frozen = _gsLocal == null && hostNow.runId == _gsRun && hostNow.phase == "moving";
            if (frozen != _gsFrozen)
            {
                _gsFrozen = frozen; _gsHudKey = int.MinValue;
                Debug.Log(frozen ? $"[MR] glass: run {_gsRun} is paused - the partner is going through the screen (score {g.score}, level {g.level}, lives {g.lives})" : $"[MR] glass: run {_gsRun} goes on");
            }
            if (frozen) return;
            if (holding) return;
            GlassHands(dt, g);
            g.touch = _gsFlat;                             // (on a flat field the hands are fingertips over the real desk: a tap is a fingertip coming down)
            g.Step(dt, _gsInput);
            GlassOutsLocal(g);                             // what it sent out goes up from the stage at the user (MrGlassOut)
            // a line the game lets the partner say: its caption, once
            if (g.petLineLeft > 0f && !string.IsNullOrEmpty(g.petLine)) { if (g.petLine != _gsLineShown) { _gsLineShown = g.petLine; if (_ch.owner == Env.Mr) Caption(Shorten(g.petLine, 80), Mathf.Clamp(g.petLineLeft, 1.2f, 8f)); } }
            else _gsLineShown = "";
            Vector3 at = GlassFeet(g) + Vector3.up * (_H * 0.5f);
            for (int i = 0; i < g.sounds.Count; i++)
            {
                string name = g.sounds[i];
                _gsSounds++;
                Sfx(name, at, name == "tick" ? 0.32f : name == "squeak" ? 0.28f : name == "drop" ? 0.40f : name == "ta-da" || name == "lose" ? 0.62f : 0.50f);
            }
            if (g.phase != _gsPhase)
            {
                Debug.Log($"[MR] glass: {g.Template} -> {g.phase} (score {g.score}, lives {g.lives}, level {g.level}{(g.choices.Length > 0 ? ", choices: " + string.Join(" | ", g.choices) : "")}{(g.result.Length > 0 ? ", result: " + g.result : "")})");
                _gsPhase = g.phase; _gsReportAt = 0;       // the PC hears of a new phase at once
            }
            if (g.phase == "over") _gsOverT += dt;
            double now = Time.realtimeSinceStartupAsDouble;
            if (hostOk && _gsLocal == null && now >= _gsReportAt)
            {
                _gsReportAt = now + 0.2;
                var host = GlassNow;
                var s = new GlassState { runId = _gsRun, game = g.Template, packId = host.packId, title = host.title, side = Env.Mr, best = host.best };
                g.FillState(s);
                _link.Send(Msg.GlassState, s);
                _gsReports++;
            }
            if (_gsLocal != null && g.phase == "over" && _gsOverT > 12f) GlassStop("the local game is over");
        }

        // ---- a FLAT game (GlassGame.flat): the field lies on the desk
        /// <summary>
        /// A flat field: metres for one unit (0.35 m deep, 0.62 m wide at 16:9); how far its pictures lie above the desk -
        /// ALL at the same height: which one is on top is their draw order alone (the order of the list; none of them
        /// writes depth), so nothing can fight over depth at any angle, while the partner, the hands and the things, which
        /// are above the field, still cover it; how near to the user its near edge may come.
        /// </summary>
        const float GlassFlatUnit = 0.35f, GlassFlatLift = 0.002f, GlassFlatNear = 0.20f;
        /// <summary>A thing that stands in a flat field is drawn this pale while the game runs, and after the field's pictures (this draw order), so that the game shows through it.</summary>
        const float GlassThingPale = 0.32f; const int GlassThingOrder = GlassOrder + 250;

        /// <summary>The footprint of a thing on the desk in the stage's own frame: its middle and half its size along x and z.</summary>
        void GlassFoot(ItemDef d, PropView p, Vector3 origin, out float x, out float z, out float hx, out float hz)
        {
            Vector3 l = Quaternion.Inverse(_gsRot) * ((p != null && p.Visible ? p.shown : ItemPos(d)) - origin);
            Vector2 half = FootHalf(p, d);
            float turn = (ItemRot(d).eulerAngles.y - _gsRot.eulerAngles.y) * Mathf.Deg2Rad, c = Mathf.Abs(Mathf.Cos(turn)), s = Mathf.Abs(Mathf.Sin(turn));
            x = l.x; z = l.z; hx = c * half.x + s * half.y; hz = s * half.x + c * half.y;
        }

        /// <summary>How much of a flat field whose far edge has its middle at `origin` would lie under one thing (square metres).</summary>
        float GlassFlatUnder(GlassGame g, float x, float z, float hx, float hz)
        {
            float hw = g.W * _gsUnit * 0.5f + 0.012f;
            float ox = Mathf.Min(x + hx, hw) - Mathf.Max(x - hx, -hw), oz = Mathf.Min(z + hz, 0.012f) - Mathf.Max(z - hz, -_gsUnit - 0.012f);
            return ox > 0f && oz > 0f ? ox * oz : 0f;
        }

        /// <summary>How much of a flat field laid there would lie under the things on the desk (square metres).</summary>
        float GlassFlatCovered(GlassGame g, Vector3 origin)
        {
            float sum = 0f;
            if (!_worldFrame) return 0f;
            foreach (var d in World.items)
            {
                if (d == null || !d.inRoom) continue;
                GlassFoot(d, PropOf(d.id), origin, out float x, out float z, out float hx, out float hz);
                sum += GlassFlatUnder(g, x, z, hx, hz);
            }
            return sum;
        }

        /// <summary>Every frame of a game, after the things have been drawn as they are: those that stand in a flat field are pale, and are drawn after it.</summary>
        void GlassThings(GlassGame g, float fade)
        {
            foreach (var p in _props)
            {
                var d = p.itemId.Length > 0 && p.Visible ? Item(p.itemId) : null;
                bool pale = false;
                if (d != null && g != null && _gsFlat && d.status != "making" && d.id != _heldItem)
                {
                    GlassFoot(d, p, _gsOrigin, out float x, out float z, out float hx, out float hz);
                    pale = GlassFlatUnder(g, x, z, hx, hz) > 1e-6f;
                }
                p.Order = pale ? GlassThingOrder : 0;
                if (pale) p.SetLook(new Color(1f, 1f, 1f, Mathf.Lerp(1f, GlassThingPale, fade)), 0f, 2f);
            }
        }
        /// <summary>In a flat game the sign, the banner and the result card stand behind the far edge, leaning back this much (degrees).</summary>
        const float GlassFlatLean = 16f;
        static readonly Quaternion GlassLying = Quaternion.Euler(90f, 0f, 0f);
        bool _gsFlat, _gsHandTool;
        int _gsGrip = -1;
        float _gsSpan, _gsGripUnit;
        Vector3 _gsAnchor;

        bool MoveGlassTool(GlassGame g)
        {
            var right = Input.Right;
            var left = Input.Left;
            bool r = right.tracked && right.pinch, l = left.tracked && left.pinch;
            if (!r && !l) { _gsGrip = -1; _gsSpan = 0f; return false; }
            bool Near(Vector3 tip, bool rim)
            {
                GlassDrop(g, tip, out float x, out float y, out _);
                Vector3 local = Quaternion.Inverse(_gsRot) * (tip - _gsOrigin);
                return Mathf.Abs(_gsFlat ? local.y : local.z) < .09f && x >= -.15f && x <= g.W+.15f && y >= -.15f && y <= 1.15f
                    && (!rim || x < .12f || x > g.W-.12f || y < .05f || y > .95f);
            }
            bool both = r && l && (_gsSpan > 0f || Near(right.indexTip, false) && Near(left.indexTip, false));
            int hand = both ? 2 : r && (_gsGrip == 0 || Near(right.indexTip, true)) ? 0 :
                l && (_gsGrip == 1 || Near(left.indexTip, true)) ? 1 : -1;
            if (hand < 0) { _gsGrip = -1; _gsSpan = 0f; return false; }
            Vector3 grip = both ? (right.indexTip + left.indexTip) * .5f : hand == 0 ? right.indexTip : left.indexTip;
            float span = Mathf.Max(.025f, Vector3.Distance(right.indexTip, left.indexTip));
            if (_gsGrip != hand)
            {
                _gsGrip = hand; _gsAnchor = (grip - _gsOrigin) / GU;
                _gsGripUnit = GU; _gsSpan = both ? span : 0f;
            }
            if (both) _gsUnit = Mathf.Clamp(_gsGripUnit * span / _gsSpan, .105f, .48f);
            _gsOrigin = grip - _gsAnchor * GU;
            return true;
        }

        /// <summary>
        /// A point of the field in the stage's own frame. layer: metres on the viewer's side of the field - towards the
        /// user when the field stands, up when it lies. (The stage's origin: the middle of the floor line of a standing
        /// field, the middle of the far edge of a lying one.)
        /// </summary>
        Vector3 FieldLocal(GlassGame g, float x, float y, float layer) => _gsFlat ? new Vector3((x - g.W * 0.5f) * GU, layer, (y - 1f) * GU) : new Vector3((x - g.W * 0.5f) * GU, y * GU, -layer);
        /// <summary>How a picture is turned: by rot degrees counter-clockwise as the user sees it, and laid down on a lying field (its top away from the user).</summary>
        Quaternion FieldTurn(float rot) => _gsFlat ? GlassLying * Quaternion.Euler(0, 0, rot) : Quaternion.Euler(0, 0, rot);

        /// <summary>Where a point of the field is in the room (z: metres behind the plane of the pictures, negative = towards the user; on a lying field: metres BELOW it, negative = above).</summary>
        Vector3 GlassAt(GlassGame g, float x, float y, float z) => _gsOrigin + _gsRot * FieldLocal(g, x, y, -z);
        Vector3 GlassFeet(GlassGame g)
        {
            Vector3 p = GlassAt(g, g.petX, g.petY, _gsFlat ? 0f : GlassPetInFront(g) ? -GlassInFront : GlassBehind);
            if (_gsFlat) p.y = _deskY;
            return p;
        }

        /// <summary>A point of the room dropped straight onto the field: x, y in field units, and how far from it in metres (a lying field: how high above the desk).</summary>
        void GlassDrop(GlassGame g, Vector3 world, out float x, out float y, out float dist)
        {
            Vector3 l = Quaternion.Inverse(_gsRot) * (world - _gsOrigin);
            float U = GU;
            x = l.x / U + g.W * 0.5f;
            if (_gsFlat) { y = l.z / U + 1f; dist = Mathf.Max(0f, l.y); }
            else { y = l.y / U; dist = Mathf.Abs(l.z); }
        }

        /// <summary>
        /// Fills _gsInput from the user's hands (0 = right, 1 = left) - or from the bot's hand, or the palm of a check.
        /// A standing field is played with the palms; a lying one with the tips of the index fingers (every tracked
        /// fingertip is a hand there, wherever it is: the game decides what a touch is).
        /// </summary>
        void GlassHands(float dt, GlassGame g)
        {
            var inp = _gsInput;
            if (_gsBot)
            {
                _gsBotPoint = false;
                GlassBot(dt, g);
                inp.valid[0] = !_gsBotPoint; inp.x[0] = _botX; inp.y[0] = _botY; inp.dist[0] = _botDist; inp.spread[0] = 1f;      // (a bot that points from a distance has no palm at the field)
                inp.valid[1] = false;
                inp.seen[0] = inp.valid[0]; inp.seen[1] = false; inp.fingers[0] = inp.fingers[1] = -1; inp.speed[0] = inp.speed[1] = 0f;
                GlassAim(dt, g);
                return;
            }
            GlassAim(dt, g);                               // where a hand POINTS at the field from a distance, and its trigger (MrGlassOut)
            for (int h = 0; h < 2; h++)
            {
                var hs = h == 0 ? Input.Right : Input.Left;
                Vector3 at = _gsFlat ? hs.indexTip : hs.palmPos; bool tracked = hs.tracked; float spread = tracked ? HandSpread(hs) : 1f;
                int fingers = hs.fingersKnown ? hs.fingers : -1;
                if (h == 0 && _gsPalm)
                {
                    // (a check: a palm - a fingertip - held at that place, read like a real one; it can show a sign, go round in a circle or sweep across the field)
                    float px = _gsPalmX, py = _gsPalmY, pt = Time.unscaledTime;
                    if (_gsPalmPath == "circle") { px += 0.10f * Mathf.Cos(pt * 6.28f); py += 0.10f * Mathf.Sin(pt * 6.28f); }
                    else if (_gsPalmPath == "sweep") px = g.W * 0.5f + (g.W * 0.5f - 0.2f) * Mathf.Sin(pt * 1.4f);
                    tracked = true; at = GlassAt(g, px, py, -_gsPalmDist); spread = 1f; fingers = _gsPalmFingers;
                }
                else if (tracked && Input.Simulated && hs.rayKind == 4)
                {
                    // the simulator's mouse: while its button is held, the hand is where the mouse's ray meets the field
                    if (_gsFlat)
                    {
                        tracked = hs.pinch && hs.rayDir.y < -0.05f && hs.rayOrigin.y > _deskY;
                        if (tracked) at = hs.rayOrigin + hs.rayDir * ((_deskY - hs.rayOrigin.y) / hs.rayDir.y) + Vector3.up * 0.005f;
                    }
                    else
                    {
                        Vector3 lo = Quaternion.Inverse(_gsRot) * (hs.rayOrigin - _gsOrigin), ld = Quaternion.Inverse(_gsRot) * hs.rayDir;
                        tracked = hs.pinch && ld.z > 0.05f && lo.z < 0f;
                        if (tracked) at = hs.rayOrigin + hs.rayDir * (-lo.z / ld.z) - (_gsRot * Vector3.forward) * 0.05f;
                    }
                    spread = 1f;
                }
                inp.valid[h] = false; inp.seen[h] = false; inp.fingers[h] = -1; inp.speed[h] = 0f;
                if (!tracked) { _gsSpeed[h].Lost(); continue; }
                GlassDrop(g, at, out float x, out float y, out float dist);
                // SEEN, wherever it is: where it is over the field, what it shows and how fast it moves. A game may ask for
                // that of a hand that is too far away to act (rock paper scissors, red light: GlassScript all_hands).
                inp.seen[h] = true; inp.x[h] = x; inp.y[h] = y; inp.dist[h] = dist; inp.fingers[h] = fingers;
                inp.speed[h] = _gsSpeed[h].Add(Time.unscaledTime, at.x, at.y, at.z);
                // a hand at rest - flat on the desk or on the keyboard, in front of a standing field - is not playing
                if (!_gsFlat && at.y - _deskY < GlassRestLow && dist > GlassRestAway) continue;
                inp.valid[h] = true; inp.spread[h] = spread;
            }
        }

        // ------------------------------------------------------------------ the partner on the field
        /// <summary>The game moves and poses the partner (called for the partner's pose while it is free on the desk). False: no game runs here.</summary>
        bool GlassPose(float dt)
        {
            GlassTick(dt);
            var g = _gsGame;
            if (g == null || _ch.owner != Env.Mr || _gsFrozen || GlassDisplay.IsStockChart(_gsPack)) return false;
            float time = Time.time;
            Vector3 feet = GlassFeet(g);
            string pose = g.petPose ?? "idle";
            float yawGo = float.NaN;
            if (_gsArrive < 1f)
            {
                // first to its place on the field, from wherever it was
                _gsArrive = Mathf.Min(1f, _gsArrive + dt / _gsArriveTime);
                Vector3 to = feet - _gsFrom; to.y = 0;
                if (to.sqrMagnitude > 0.02f * 0.02f) { if (_gsArrive < 0.85f) { pose = "walk"; yawGo = YawTo(to); } }
                feet = Vector3.Lerp(_gsFrom, feet, M.Smooth(_gsArrive));
            }
            Vector3 step = feet - _gsLastFeet; step.y = 0; _gsLastFeet = feet;
            bool walking = pose == "walk" || pose == "run";
            // (the field is wide for its legs: it runs, and its legs are not made to go faster than they can)
            _gsWalk += Mathf.Min(step.magnitude / (PoseLib.StrideLength(PoseLib.RunAmp) * _H), 7.5f * dt);
            _gsPickA = Mathf.MoveTowards(_gsPickA, pose == "pick" ? 1f : 0f, dt / 0.25f);
            if (walking) PoseLib.Run(_gsTarget, _gsWalk);
            else if (g.petCustom && g.petCustomPose != null) _gsTarget.CopyFrom(g.petCustomPose);      // (a motion the script defined, as it is given)
            else GlassPoses.For(pose, _gsTarget, time, _gsWalk, _gsPickA);
            Pose.Lerp(_gsBlend, _gsTarget, 1f - Mathf.Exp(-dt * (walking ? 40f : 18f)), _gsBlend);
            // seen from the side while it walks, turned to the user otherwise (a little towards where it is going);
            // on a lying field it walks on the desk: it looks where it goes
            float side = g.petFacesRight ? -1f : 1f;
            float yaw = !float.IsNaN(yawGo) ? yawGo : _gsFlat && walking && step.sqrMagnitude > 4e-8f ? YawTo(step)
                : _gsYawUser + side * (walking ? 72f : pose == "pick" ? 55f : pose == "down" ? 0f : 16f);
            _gsYaw = Mathf.MoveTowardsAngle(_gsYaw, yaw, 600f * dt);
            _pose.CopyFrom(_gsBlend);
            _pos = feet; _rot = Quaternion.Euler(0, _gsYaw, 0);
            _motor.Teleport(new Vector2(_pos.x, _pos.z), _gsYaw);
            _gsPetFrame = Time.frameCount;
            return true;
        }

        // ------------------------------------------------------------------ the picture
        /// <summary>Every frame (after the partner has been placed and shown): the stage of a game that runs here, the sign of one that is played inside a screen.</summary>
        void UpdateGlass(float dt)
        {
            GlassTick(dt);
            GlassPcSign();
            UpdateGlassOut(dt);      // what a game sent out of its field flies through the room; the shields on the palms (MrGlassOut)
            var g = _gsGame; var v = _stage;
            if (g == null) { if (v.stage.activeSelf) v.stage.SetActive(false); return; }
            float fade = _gsFade;
            if (_gsFrozen) { GlassFrozen(g, fade); return; }
            float U = GU;
            if (v.veil.enabled != !_gsFlat) v.veil.enabled = !_gsFlat;
            if (_gsFlat)
            {
                // a sheet of paper on the desk under the whole field: where the game is
                v.floor.transform.localPosition = new Vector3(0, 0.0008f, -0.5f * U);
                Ui.SizePanel(v.floor, g.W * U + 0.024f, U + 0.024f, 0.016f, 0.0014f);
                v.floor.SetColors(new Color(1f, 0.985f, 0.945f, 0.50f * fade), new Color(1f, 0.95f, 0.82f, 0.50f * fade));
            }
            else
            {
                v.veil.transform.localPosition = new Vector3(0, 0.5f * U, 0.07f);
                Ui.SizePanel(v.veil, g.W * U, U, 0.03f, 0f);
                v.veil.SetColors(new Color(1f, 0.973f, 0.918f, 0.07f * fade), new Color(1f, 0.973f, 0.918f, 0.07f * fade));
                v.floor.transform.localPosition = new Vector3(0, 0.0012f, 0.004f);
                Ui.SizePanel(v.floor, g.W * U + 0.03f, 0.034f, 0.017f, 0.0012f);
                v.floor.SetColors(new Color(1f, 0.985f, 0.945f, 0.62f * fade), new Color(1f, 0.93f, 0.72f, 0.62f * fade));
            }
            GlassSprites(g, fade);
            GlassRings(g, fade);
            GlassAimDraw(g, dt, fade);
            GlassHud(g, fade);
            GlassThings(g, fade);
            // hit a moment ago: it blinks (not while a line of its own is over its head: that line would jump away with it)
            if (_gsPetFrame == Time.frameCount && g.petHurt > 0f && g.phase != "over" && Time.time > _captionUntil && Mathf.Repeat(Time.time * 11f, 1f) < 0.42f) _rig.SetVisible(false);
        }

        /// <summary>
        /// The run is paused while the partner goes through the screen (the game follows it): the game's pictures are put
        /// away, nothing moves; only the sign stays, with the title and three dots, until the run on the other side begins.
        /// </summary>
        void GlassFrozen(GlassGame g, float fade)
        {
            var v = _stage;
            for (int i = 0; i < _gsShown; i++) v.sprites[i].enabled = false;
            for (int i = 0; i < _gsLabelsShown; i++) { v.labels[i].text = ""; v.labelBgs[i].enabled = false; }
            for (int i = 0; i < _gsCardsShown; i++) v.cards[i].gameObject.SetActive(false);
            _gsShown = _gsLabelsShown = _gsCardsShown = 0;
            for (int i = 0; i < 2; i++) { if (v.handRing[i].enabled) { v.handRing[i].enabled = false; v.handFill[i].enabled = false; } if (v.aimRay[i].enabled) v.aimRay[i].enabled = false; }
            foreach (var q in v.aimParts) if (q.enabled) q.enabled = false;
            if (v.botHand.enabled) v.botHand.enabled = false;
            if (v.bannerBg.enabled) v.bannerBg.enabled = false;
            if (_gsBannerShown != "") { _gsBannerShown = ""; v.banner.text = ""; }
            if (v.result.gameObject.activeSelf) v.result.gameObject.SetActive(false);
            if (v.veil.enabled) v.veil.enabled = false;
            if (v.floor.enabled) v.floor.enabled = false;
            float U = GU;
            GlassPaper(v.hud, 0, 1.085f * U, -0.004f, 0.050f);
            v.hud.localScale = Vector3.one * U;
            if (_gsHudKey != -77)
            {
                _gsHudKey = -77;
                FitLabel(v.title, _gsTitle + " ...", 0.90f);
                v.score.text = ""; v.level.text = ""; v.time.text = "";
                for (int i = 0; i < v.hearts.Length; i++) if (v.hearts[i].enabled) v.hearts[i].enabled = false;
                if (v.scoreStar.enabled) v.scoreStar.enabled = false;
                if (v.barBg.enabled) { v.barBg.enabled = false; v.bar.enabled = false; }
            }
            v.title.color = new Color(Ui.Pal.Ink.r, Ui.Pal.Ink.g, Ui.Pal.Ink.b, fade);
            v.hudBg.SetColors(new Color(Ui.Pal.PanelTop.r, Ui.Pal.PanelTop.g, Ui.Pal.PanelTop.b, 0.96f * fade), new Color(Ui.Pal.PanelBottom.r, Ui.Pal.PanelBottom.g, Ui.Pal.PanelBottom.b, 0.96f * fade));
            GlassThings(null, 0f);
        }

        Texture2D GlassArtTexture(string art)
        {
            var a = _stage.art;
            return art != null && GlassArtIndex.TryGetValue(art, out int i) && i < a.Length ? a[i] : a[0];
        }

        /// <summary>One of the pack's own pictures ("pack:name" -> GamePackDef.art), once it has arrived from the PC; null until then, or when the pack has none of that name.</summary>
        Texture2D GlassPackTexture(string art)
        {
            var list = _gsPack != null ? _gsPack.art : null;
            if (list == null || art == null || art.Length <= GlassArt.PackPrefix.Length) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && string.CompareOrdinal(art, GlassArt.PackPrefix.Length, list[i].name, 0, int.MaxValue) == 0) return Icon(list[i].file);
            return null;
        }

        /// <summary>0xRRGGBB as a colour.</summary>
        static Color GlassTint(int rgb, float a) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

        /// <summary>
        /// The pictures of the game's list, back to front. How large a picture is drawn:
        ///  - w > 0: exactly w x size (any picture: it is stretched to that);
        ///  - a plain shape (rect, disc, hoop): size x size - they fill their picture;
        ///  - a built-in sticker: `size` is the THING's height; the picture has a margin round it and is drawn 1.28 times
        ///    larger (one of the pack's own pictures: 1.15 times).
        /// Colour: a plain shape is white and takes the sprite's colour (-1 = stays white); a picture is multiplied by a
        /// colour above 0 and left as it is otherwise.
        /// </summary>
        void GlassSprites(GlassGame g, float fade)
        {
            var v = _stage;
            bool survival = g is GlassSurvival;
            // the pack's own pictures, once they have arrived from the PC (as inside the screen: its creature for all that close in, its pickup for the gems / the stars)
            Texture2D enemyTex = _gsPack != null ? Icon(_gsPack.enemyIcon) : null, pickupTex = _gsPack != null ? Icon(_gsPack.pickupIcon) : null;
            float U = GU, pixel = GlassPixel;
            Vector3 eye = Cam != null ? Cam.transform.position : Input.HeadPos;
            int n = 0, nl = 0, nc = 0, nf = 0;
            var list = g.sprites;
            for (int k = 0; k < list.Count; k++)
            {
                var s = list[k];
                string art = s.art ?? "";
                if (GlassArt.IsFootball(art) && v.footballs != null && nf < v.footballs.Length)
                {
                    v.footballs[nf++].PlaceFootball(FieldLocal(g, s.x, s.y, 0.004f + s.size * U * 0.50f), s.size * U, Time.time * 115f + s.rot, fade * s.alpha > 0.04f);
                    continue;
                }
                if (art == GlassArt.Label) { if (nl < v.labels.Length) GlassLabel(nl++, s, g, fade); continue; }
                if (art == GlassArt.Card) { if (nc < v.cards.Length) GlassCard(nc++, s, g, fade); continue; }
                if (n >= v.sprites.Length) continue;
                var q = v.sprites[n];
                Texture2D tex; float scale = 1.28f, alpha = s.alpha;
                // (paper: the rounded box of a scripted tool - no picture: the sprite shader draws it from its size and its corner radius)
                bool paperBox = art == GlassArt.Paper;
                bool plain = paperBox || art == GlassArt.Rect || art == GlassArt.Disc || art == GlassArt.RingShape;
                bool creature = art == GlassArt.Enemy || art == GlassArt.Flyer || art == GlassArt.Brute;
                if (plain) { tex = GlassArtTexture(paperBox ? GlassArt.Rect : art); scale = 1f; }
                else if (art.StartsWith(GlassArt.PackPrefix, StringComparison.Ordinal))
                {
                    tex = GlassPackTexture(art); scale = 1.15f;
                    if (tex == null) { tex = GlassArtTexture(GlassArt.Disc); scale = 1f; alpha *= 0.35f; }      // (not here yet: a pale disc holds its place)
                }
                else
                {
                    tex = GlassArtTexture(art);
                    if (creature && enemyTex != null) { tex = enemyTex; scale = 1.15f; }
                    else if (pickupTex != null && (survival ? art == GlassArt.Gem : art == GlassArt.Star)) { tex = pickupTex; scale = 1.15f; }
                    else if (art == GlassArt.Ring || art == GlassArt.Frost) scale = 1.09f;
                    else if (art == GlassArt.Sparkle) scale = 1.2f;
                }
                float h, w;
                if (s.w > 0f) { w = s.w; h = s.size; }
                else if (art == GlassArt.Bolt) { h = s.size * 1.02f; w = Mathf.Max(s.size * 0.24f, 0.07f); }
                else { h = s.size * scale; w = h; }
                if (creature && s.frame == 1) w = -w;                                 // drawn looking to the right: mirrored for the left
                // back to front in the order of the list: that is their draw order (below). A lying field's pictures are all at
                // one height; a standing one's are each a hair nearer to the viewer than the one before
                float layer = _gsFlat ? GlassFlatLift : 0.002f + n * 0.0001f;
                var t = q.transform;
                Vector3 local = FieldLocal(g, s.x, s.y, layer); Quaternion turn = FieldTurn(paperBox ? 0f : s.rot);
                float wm = w * U, hm = h * U;
                if (Mathf.Min(Mathf.Abs(wm), hm) < 0.006f)
                {
                    // a line: never thinner than the eye can draw in one piece (fainter instead)
                    Vector3 at = _gsOrigin + _gsRot * local; Quaternion world = _gsRot * turn;
                    float a1, a2;
                    float wide = GlassThin(Mathf.Abs(wm), world * Vector3.right, at, eye, pixel, out a1), high = GlassThin(hm, world * Vector3.up, at, eye, pixel, out a2);
                    wm = wm < 0f ? -wide : wide; hm = high; alpha *= a1 * a2;
                }
                t.localPosition = local;
                t.localRotation = turn;
                t.localScale = new Vector3(wm, hm, 1f);
                q.round = paperBox ? new Vector4(Mathf.Abs(wm), hm, Mathf.Clamp(s.fill, 0f, 0.5f) * Mathf.Min(Mathf.Abs(wm), hm), 1f) : Vector4.zero;
                q.texture = tex;
                q.color = plain ? (s.color >= 0 ? GlassTint(s.color, alpha * fade) : new Color(1f, 1f, 1f, alpha * fade)) : s.color > 0 ? GlassTint(s.color, alpha * fade) : new Color(1f, 1f, 1f, alpha * fade);
                q.order = GlassOrder + 12 + n;
                if (!q.enabled) q.enabled = true; else q.Apply();
                n++;
            }
            if (v.footballs != null) for (int i = nf; i < v.footballs.Length; i++) v.footballs[i].Visible = false;
            for (int i = n; i < _gsShown; i++) v.sprites[i].enabled = false;
            _gsShown = n;
            for (int i = nl; i < _gsLabelsShown; i++) { v.labels[i].text = ""; v.labelBgs[i].enabled = false; }
            _gsLabelsShown = nl;
            for (int i = nc; i < _gsCardsShown; i++) v.cards[i].gameObject.SetActive(false);
            _gsCardsShown = nc;
        }
        /// <summary>The draw order of the stage (LPSceneGlass.cs authors it at the same number).</summary>
        const int GlassOrder = 400;

        /// <summary>
        /// Words on the field: `size` is the height of the letters (field units), x, y their middle. With a colour above 0
        /// they are bare letters in that colour; without one, ink on a small strip of paper, so that they read in front
        /// of any room (the "+3" of the built-in games). On a lying field they lie, and read from the user's side.
        /// </summary>
        void GlassLabel(int i, GlassSprite s, GlassGame g, float fade)
        {
            var v = _stage; var label = v.labels[i]; var bg = v.labelBgs[i];
            float a = s.alpha * fade, U = GU;
            bool paper = s.color <= 0;
            label.transform.localScale = Vector3.one * s.size * U;
            label.text = s.text ?? "";
            label.color = paper ? new Color(Ui.Pal.Ink.r, Ui.Pal.Ink.g, Ui.Pal.Ink.b, a) : GlassTint(s.color, a);
            float layer = _gsFlat ? GlassFlatLift : 0.024f;      // (lying: at the pictures' own height, over them by its draw order)
            Quaternion turn = FieldTurn(s.rot);
            label.transform.localPosition = FieldLocal(g, s.x, s.y, layer);
            label.transform.localRotation = turn;
            if (bg.enabled != paper) bg.enabled = paper;
            if (!paper) return;
            float w = label.Width + 0.5f * s.size * U, h = 1.15f * s.size * U;
            bg.transform.localPosition = FieldLocal(g, s.x, s.y, layer - (_gsFlat ? 0f : 0.002f));
            bg.transform.localRotation = turn;
            Ui.SizePanel(bg, w, h, h * 0.5f, h * 0.07f);
            bg.SetColors(new Color(1f, 0.985f, 0.945f, 0.96f * a), new Color(1f, 0.93f, 0.72f, 0.96f * a));
        }

        /// <summary>Where the paper of a game stands: the sign, the banner, the cards and the result card. On a standing field at their places on it; behind the far edge of a lying one, leaning back.</summary>
        void GlassPaper(Transform t, float x, float y, float z, float flatHeight)
        {
            if (_gsFlat) { t.localPosition = new Vector3(x, flatHeight, 0.012f + z); t.localRotation = Quaternion.Euler(GlassFlatLean, 0, 0); }
            else { t.localPosition = new Vector3(x, y, z); t.localRotation = Quaternion.identity; }
        }

        void GlassCard(int i, GlassSprite s, GlassGame g, float fade)
        {
            var v = _stage; var root = v.cards[i];
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            float a = s.alpha * fade, U = GU;
            GlassPaper(root, (s.x - g.W * 0.5f) * U, s.y * U, -0.030f - 0.002f * i, 0.20f);
            // (the card is authored one unit high; a hand resting on it makes it swell a little)
            root.localScale = Vector3.one * s.size * U * Mathf.Lerp(0.9f, 1f, Ui.Pop(s.alpha)) * (1f + 0.05f * s.fill);
            v.cardBg[i].SetColor(new Color(1f, 1f, 1f, a));
            var icon = s.frame >= 0 && s.frame < v.abilityArt.Length ? v.abilityArt[s.frame] : null;
            if (icon != null) v.cardIcon[i].SetTexture(icon);
            v.cardIcon[i].SetColor(new Color(1f, 1f, 1f, a));
            v.cardName[i].SetWrapped(s.text ?? "", 0.64f); v.cardName[i].color = new Color(Ui.Pal.Ink.r, Ui.Pal.Ink.g, Ui.Pal.Ink.b, a);
            v.cardNote[i].SetWrapped(s.text2 ?? "", 0.62f); v.cardNote[i].color = new Color(Ui.Pal.Ink.r, Ui.Pal.Ink.g, Ui.Pal.Ink.b, a * 0.86f);
            var ring = v.cardRing[i];
            ring.param = new Vector4(0.86f, 0.12f, Mathf.Clamp01(s.fill), 0f);
            ring.SetColors(new Color(1f, 0.69f, 0.42f, a), new Color(0.96f, 0.45f, 0.42f, a));
        }

        /// <summary>
        /// Where each hand is on the field. Standing: a soft ring as large as what the hand reaches, coming up as the hand
        /// comes within GlassGames.ActDistance of the plane. Lying: a small ring on the desk straight under the fingertip
        /// from 8 cm down, filled when the tip is within 2 cm of the desk (what the games take for a touch).
        /// </summary>
        void GlassRings(GlassGame g, float fade)
        {
            var v = _stage; var inp = _gsInput;
            float U = GU;
            Quaternion turn = FieldTurn(0f);
            for (int h = 0; h < 2; h++)
            {
                float a = 0f;
                if (inp.valid[h] && g.phase != "over")
                    a = (_gsFlat ? Mathf.Clamp01((0.08f - inp.dist[h]) / 0.05f) : Mathf.Clamp01((GlassGames.ActDistance - inp.dist[h]) / 0.10f)) * fade;
                if (_gsFlat && (inp.x[h] < -0.05f || inp.x[h] > g.W + 0.05f || inp.y[h] < -0.05f || inp.y[h] > 1.05f)) a = 0f;      // (a fingertip somewhere else on the desk)
                bool on = a > 0.01f;
                var ring = v.handRing[h]; var fill = v.handFill[h];
                if (ring.enabled != on) { ring.enabled = on; fill.enabled = on; }
                if (!on) continue;
                float r = _gsFlat ? 0.011f + 0.10f * inp.dist[h] : GlassGames.HandRadius(g, inp, h) * U;
                float touch = _gsFlat && inp.dist[h] < 0.02f ? 1f : 0f;
                ring.transform.localPosition = FieldLocal(g, inp.x[h], inp.y[h], _gsFlat ? GlassFlatLift : 0.020f);
                fill.transform.localPosition = FieldLocal(g, inp.x[h], inp.y[h], _gsFlat ? GlassFlatLift : 0.019f);
                ring.transform.localRotation = fill.transform.localRotation = turn;
                ring.transform.localScale = fill.transform.localScale = Vector3.one * (r * 2f / 0.90f);
                ring.phase = Time.time * 0.15f;
                ring.SetColors(new Color(1f, 0.84f, 0.42f, 0.90f * a), new Color(1f, 0.97f, 0.90f, 0.90f * a));
                fill.SetColors(new Color(1f, 0.97f, 0.88f, Mathf.Lerp(0.16f, 0.55f, touch) * a), new Color(1f, 0.93f, 0.72f, Mathf.Lerp(0.16f, 0.55f, touch) * a));
            }
            // the bot's hand (the user's own hands are real: nothing is drawn on them)
            bool bot = _gsBot && g.phase != "over" && !_gsBotPoint;      // (a bot that points from a distance is seen by its aim ring)
            if (v.botHand.enabled != bot) v.botHand.enabled = bot;
            if (bot)
            {
                float lifted = _gsFlat ? Mathf.Clamp01(_botDist / 0.08f) : Mathf.Clamp01((_botDist - 0.10f) / 0.25f);       // held back from the field: larger (nearer to the eyes) and fainter
                float side = _gsFlat ? 0.085f : GlassGames.HandRadius(g, inp, 0) * U / 0.36f * (1f + 0.25f * lifted);      // (in glass-hand.png an open hand's reach is 0.36 of the picture's side)
                // (on a lying field: the picture of a hand whose index fingertip - 0.37 of the picture above its middle, 0.12 to the left - is where the bot's finger is)
                v.botHand.transform.localPosition = _gsFlat ? FieldLocal(g, _botX, _botY, GlassFlatLift + 0.007f + _botDist) + new Vector3(0.12f * side, 0, -0.37f * side)
                                                            : FieldLocal(g, _botX, _botY, 0.022f + _botDist * 0.2f);
                v.botHand.transform.localRotation = FieldTurn(_gsFlat ? 0f : 7f * Mathf.Sin(Time.time * 1.3f));
                v.botHand.transform.localScale = new Vector3(side, side, 1f);
                v.botHand.SetColor(new Color(1f, 0.90f, 0.80f, Mathf.Lerp(0.80f, 0.35f, lifted) * fade));
            }
        }

        static string GlassClock(float seconds) { int s = Mathf.Max(0, Mathf.FloorToInt(seconds)); return (s / 60) + ":" + (s % 60).ToString("00"); }

        void GlassHud(GlassGame g, float fade)
        {
            var v = _stage;
            float W = g.W, U = GU;
            GlassPaper(v.hud, 0, 1.085f * U, -0.004f, 0.050f);
            v.hud.localScale = Vector3.one * U;
            var sv = g as GlassSurvival;
            float shownTime = g.seconds;
            // (the words are written again only when a number in them has changed)
            // (only what the game uses is on the sign: a script that never counts lives has no hearts - GlassSignShows)
            GlassSignShows(g, _gsStartLives, ref _gsSeen, out bool showHearts, out bool showLevel, out bool showScore);
            int key = g.score * 31 + g.level * 977 + g.lives * 7919 + Mathf.FloorToInt(shownTime) * 104729 + (sv != null ? sv.Wave * 13 : 0) + _gsSeen * 15485863;
            if (key != _gsHudKey)
            {
                _gsHudKey = key;
                FitLabel(v.title, _gsTitle, showHearts ? 0.36f : showScore ? 0.66f : 0.90f);      // (field units: the sign is laid out in them)
                v.score.text = showScore ? g.score.ToString() : "";
                v.level.text = !showLevel ? "" : sv != null ? $"Lv {g.level}  Wave {Mathf.Max(1, sv.Wave)}" : "Level " + g.level;
                v.time.text = _gsClock ? GlassClock(shownTime) : "";      // (a tool - keys and no lives - has no clock: its sign is its title)
                if (!v.floor.enabled) v.floor.enabled = true;
                if (g.lives > _gsMaxLives) _gsMaxLives = g.lives;
            }
            Color ink = new Color(Ui.Pal.Ink.r, Ui.Pal.Ink.g, Ui.Pal.Ink.b, fade), dim = new Color(Ui.Pal.TextDim.r, Ui.Pal.TextDim.g, Ui.Pal.TextDim.b, fade);
            v.title.color = ink; v.score.color = ink; v.level.color = dim;
            v.time.color = ink;
            v.hudBg.SetColors(new Color(Ui.Pal.PanelTop.r, Ui.Pal.PanelTop.g, Ui.Pal.PanelTop.b, 0.96f * fade), new Color(Ui.Pal.PanelBottom.r, Ui.Pal.PanelBottom.g, Ui.Pal.PanelBottom.b, 0.96f * fade));
            if (v.scoreStar.enabled != showScore) v.scoreStar.enabled = showScore;
            if (showScore) v.scoreStar.SetColor(new Color(1f, 1f, 1f, fade));
            for (int i = 0; i < v.hearts.Length; i++)
            {
                bool slot = showHearts && i < _gsMaxLives;
                if (v.hearts[i].enabled != slot) v.hearts[i].enabled = slot;
                if (slot) v.hearts[i].SetColor(new Color(1f, 1f, 1f, (i < g.lives ? 1f : 0.20f) * fade));
            }
            // one bar under the strip: the way to the next level (survival)
            float bar = sv != null ? Mathf.Clamp01(sv.Xp / (float)Mathf.Max(1, sv.XpNeeded)) : -1f;
            bool hasBar = bar >= 0f;
            if (v.barBg.enabled != hasBar) { v.barBg.enabled = hasBar; v.bar.enabled = hasBar; }
            if (hasBar)
            {
                const float bw = 0.50f, bh = 0.016f;
                v.barBg.SetColors(new Color(0.29f, 0.18f, 0.15f, 0.55f * fade), new Color(0.29f, 0.18f, 0.15f, 0.55f * fade));
                float fw = Mathf.Max(bh, bw * bar);
                v.bar.transform.localPosition = new Vector3(-bw * 0.5f + fw * 0.5f, -0.058f, -0.002f);
                Ui.SizePanel(v.bar, fw, bh, bh * 0.5f, 0f);
                Color c1 = new Color(0.56f, 0.83f, 0.96f, fade);
                v.bar.SetColors(c1, c1);
            }

            // ---- the banner: big, across the field, for as long as the game has a line (not over the result card)
            string banner = g.phase == "over" ? "" : g.banner ?? "";
            if (banner != _gsBannerShown)
            {
                _gsBannerShown = banner;
                v.banner.transform.localScale = Vector3.one * U;
                v.banner.SetWrapped(banner, 0.86f * W * U);
            }
            bool hasBanner = banner.Length > 0;
            if (v.bannerBg.enabled != hasBanner) v.bannerBg.enabled = hasBanner;
            if (hasBanner)
            {
                float bw = v.banner.Width + 0.09f * U, bh = v.banner.Height + 0.045f * U;
                GlassPaper(v.banner.transform, 0, (g.phase == "choose" ? 0.835f : 0.66f) * U, -0.040f, 0.080f + bh * 0.5f);
                v.banner.color = ink;
                v.bannerBg.transform.localRotation = v.banner.transform.localRotation;
                v.bannerBg.transform.localPosition = v.banner.transform.localPosition + v.banner.transform.localRotation * new Vector3(0, 0, 0.004f);
                Ui.SizePanel(v.bannerBg, bw, bh, Mathf.Min(bh * 0.5f, 0.045f * U), 0.006f * U);
                v.bannerBg.SetColors(new Color(1f, 0.985f, 0.945f, 0.95f * fade), new Color(1f, 0.93f, 0.72f, 0.95f * fade));
            }

            // ---- over: the result on a card of paper
            bool over = g.phase == "over";
            if (v.result.gameObject.activeSelf != over) v.result.gameObject.SetActive(over);
            if (!over) _gsOvSet = false;
            if (over)
            {
                float pop = Ui.Pop(Mathf.Clamp01(_gsOverT / 0.45f));
                GlassPaper(v.result, 0, 0.56f * U, -0.045f, 0.105f + 0.20f * U);
                v.result.localScale = Vector3.one * U * Mathf.Max(0.001f, pop);
                // (a best is the PC's to keep: only a game it started has one, and only once it has been played before)
                int hostBest = _gsLocal == null ? GlassNow.best : 0;
                string res = g.result ?? "";
                // (the words of the card are written when they change, not in every frame)
                if (!_gsOvSet || res != _gsOvResult || showScore != _gsOvShow || g.score != _gsOvScore || hostBest != _gsOvBest)
                {
                    _gsOvSet = true; _gsOvResult = res; _gsOvShow = showScore; _gsOvScore = g.score; _gsOvBest = hostBest;
                    v.resultTitle.text = "Game over";
                    v.resultLine.SetWrapped(res, 0.92f);
                    v.resultScore.text = !showScore ? "" : hostBest <= 0 ? "Score " + g.score : g.score > hostBest ? $"Score {g.score}   Best {g.score} - a new best!" : $"Score {g.score}   Best {hostBest}";
                }
                v.resultTitle.color = ink; v.resultLine.color = dim; v.resultScore.color = ink;
                v.resultBg.SetColors(new Color(1f, 0.985f, 0.945f, 0.97f * fade), new Color(0.985f, 0.945f, 0.875f, 0.97f * fade));
            }
        }

        bool _gsOvSet, _gsOvShow; string _gsOvResult = ""; int _gsOvScore, _gsOvBest;      // what the result card says now

        /// <summary>The game is played inside a screen: a small sign near that screen says so, for four seconds. Nothing else is drawn or heard here.</summary>
        void GlassPcSign()
        {
            var v = _stage; var g = GlassNow;
            bool pc = _link.Connected && _snap != null && g.game.Length > 0 && g.side == Env.Pc;
            if (pc && g.runId != _gsPcRun)
            {
                _gsPcRun = g.runId; _gsPcUntil = g.phase == "over" ? 0f : Time.time + 4f;
                Debug.Log($"[MR] glass: '{g.title}' is played inside {ShortMon(g.monitorId)}: nothing is drawn on the desk");
            }
            var mv = pc ? Mon(g.monitorId) ?? FirstMon() : null;
            bool show = pc && mv != null && Time.time < _gsPcUntil && g.phase != "over" && !_calibrating;
            if (v.pcSign.activeSelf != show) v.pcSign.SetActive(show);
            if (!show) return;
            string line = (g.title.Length > 0 ? g.title : "The game") + " - play at the screen with your hands";
            if (v.pcSignLabel.text != line) v.pcSignLabel.text = line;
            float left = _gsPcUntil - Time.time, a = Mathf.Clamp01(left / 0.4f) * Mathf.Clamp01((4f - left) / 0.25f);
            // over the top edge of that screen (above the place of the partner's own line), a little in front of the glass, turned the way the screen is
            v.pcSign.transform.SetPositionAndRotation(mv.Origin + mv.Y * (mv.frame.heightM * 0.5f + 0.105f) - mv.Z * 0.03f, Quaternion.LookRotation(mv.Z, mv.Y));
            float w = v.pcSignLabel.Width + 0.040f, h = v.pcSignLabel.Height + 0.020f;
            Ui.SizePanel(v.pcSignBg, w, h, Mathf.Min(h * 0.5f, 0.016f), 0.0016f);
            v.pcSignBg.SetColors(new Color(1f, 0.985f, 0.945f, 0.96f * a), new Color(1f, 0.93f, 0.72f, 0.96f * a));
            v.pcSignLabel.color = new Color(Ui.Pal.Ink.r, Ui.Pal.Ink.g, Ui.Pal.Ink.b, a);
        }

        // ------------------------------------------------------------------ the bot (a hand for seeing the games without a headset)
        /// <summary>
        /// `lpctl debug glassBot 1`: a simple hand plays instead of the tracked ones (the same hand the PC client's bot
        /// is). Survival: to the creature nearest the partner, else to a gem; it rests on the first card while
        /// choosing. Umbrella: held over the partner, drawn back from the plane for a golden drop. Any other game (a
        /// script): it wanders over the field and touches down here and there - enough to see that the game answers.
        /// </summary>
        void GlassBot(float dt, GlassGame g)
        {
            float W = g.W, gx = g.petX, gy = 0.34f, gd = 0.05f, speed = 1.7f;
            var list = g.sprites;
            if (g.phase == "over" || g.phase == "intro") { gx = W * 0.5f + 0.25f; gy = 0.45f; gd = 0.2f; }
            else if (_gsBotLoses)
            {
                // the clumsy hand (glassBot 2), for seeing a game end: held high where the nearest creature is - the partner
                // follows it there -, away from the partner in the rain
                gy = 0.88f; gd = g is GlassSurvival ? 0.05f : 0.40f; gx = g is GlassSurvival ? g.petX : W * 0.5f;
                float best = 9f;
                if (g is GlassSurvival)
                    for (int i = 0; i < list.Count; i++)
                    {
                        var s = list[i];
                        if (s.art != GlassArt.Enemy && s.art != GlassArt.Brute) continue;
                        float d = Mathf.Abs(s.x - g.petX);
                        if (d < best) { best = d; gx = s.x; }
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
                    if (foe) { gx = ex; gy = ey; } else if (gem) { gx = mx; gy = my; } else { gx = W * 0.5f + 0.12f; gy = 0.26f; }      // nothing near: back to the middle (the partner follows the hand)
                }
            }
            else if (g is GlassUmbrella)
            {
                gx = g.petX; gy = 0.36f;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].art == GlassArt.Gold && Mathf.Abs(list[i].x - g.petX) < 0.22f && list[i].y > 0.2f && list[i].y < 0.85f) { gd = 0.40f; gy = 0.44f; }      // drawn back: the gold goes through
            }
            else if (g is GlassScript shot && shot.ReadShots)
            {
                // a game that is SHOT at: the bot points at the nearest target (a creature, one of the pack's pictures or a disc
                // of some size that is not ink) from a distance and pulls the trigger when its aim is on it (as the PC client's bot)
                float best = 9f; bool any = false; gx = W * 0.5f; gy = 0.6f; speed = 2.2f; gd = 0.4f;
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i]; string a = s.art ?? "";
                    bool target = a == GlassArt.Enemy || a == GlassArt.Flyer || a == GlassArt.Brute || a.StartsWith(GlassArt.PackPrefix, StringComparison.Ordinal)
                                  || (a == GlassArt.Disc && s.size >= 0.09f && s.color != LuaSandbox.Ink && s.alpha > 0.5f);
                    if (!target) continue;
                    float d = Mathf.Abs(s.x - _botX) + Mathf.Abs(s.y - _botY);
                    if (d < best) { best = d; gx = s.x; gy = s.y; any = true; }
                }
                _gsBotPoint = true;
                _gsBotFireT = Mathf.Max(0f, _gsBotFireT - dt);
                if (any && best < 0.02f && _gsBotFireT <= 0f) _gsBotFireT = 0.45f;
                _gsBotFire = _gsBotFireT > 0.33f;
            }
            else
            {
                // a game nobody here knows (a script): to a place, down on it for most of a second (a hand that rests is a tap), up again, to the next
                if (!_botHasGoal) { _botHasGoal = true; _botGoalX = W * (0.12f + 0.76f * (float)_botRandom.NextDouble()); _botGoalY = 0.12f + 0.76f * (float)_botRandom.NextDouble(); _botTouch = 0f; }
                gx = _botGoalX; gy = _botGoalY; speed = 1.1f;
                bool there = Mathf.Abs(gx - _botX) + Mathf.Abs(gy - _botY) < 0.012f;
                gd = _gsFlat ? (there ? 0.004f : 0.06f) : (there ? 0.01f : 0.14f);
                if (there && Mathf.Abs(_botDist - gd) < 0.003f) { _botTouch += dt; if (_botTouch > 0.8f) _botHasGoal = false; }
            }
            Vector2 to = new Vector2(gx - _botX, gy - _botY); float left = to.magnitude;
            if (left > 1e-4f) { float step = Mathf.Min(left, speed * dt * Mathf.Clamp(left / 0.12f, 0.25f, 1f)); _botX += to.x / left * step; _botY += to.y / left * step; }
            _botDist = Mathf.MoveTowards(_botDist, gd, dt * (_gsFlat ? 0.5f : 1.2f));
        }
        float _botGoalX, _botGoalY, _botTouch; bool _botHasGoal;
        readonly System.Random _botRandom = new System.Random(20261117);

        // ------------------------------------------------------------------ debug commands (lpctl debug <cmd> <value> <px> <py> <text>)
        bool OnDebugGlass(DebugCommand d)
        {
            if (OnDebugGlassOut(d)) return true;      // glassOutTest, glassBlock, glassAim, glassOutInfo (MrGlassOut)
            switch (d.cmd)
            {
                case "glassBot":
                    _gsBot = d.value > 0.5f; _gsBotLoses = d.value > 1.5f;
                    Debug.Log("[MR] glass debug: the bot's hand " + (_gsBotLoses ? "plays to lose" : _gsBot ? "plays" : "is gone"));
                    return true;
                case "glassXp":
                    if (_gsGame is GlassSurvival sv) { sv.GrantXp(Mathf.Max(1, Mathf.RoundToInt(d.value))); Debug.Log($"[MR] glass debug: {Mathf.RoundToInt(d.value)} experience granted"); }
                    return true;
                case "glassChoose":
                    if (_gsGame != null) _gsGame.Choose(Mathf.Clamp(Mathf.RoundToInt(d.value), 0, 2));
                    return true;
                case "glassLives":
                    // lpctl debug glassLives 1: so many hearts are left (to see a game end without waiting for it)
                    if (_gsGame != null && _gsGame.phase != "over") { _gsGame.lives = Mathf.Clamp(Mathf.RoundToInt(d.value), 1, GlassGameBase.MaxLives); Debug.Log($"[MR] glass debug: {_gsGame.lives} hearts left"); }
                    return true;
                case "glassPalm":
                    // lpctl debug glassPalm <dist m> <x> <y> [off | rock | paper | scissors | point] [circle | sweep]: the right palm held at
                    // (x, y) of the field, dist metres in front of its plane (on a lying field: the tip of the right index finger over
                    // (x, y), dist metres above the desk). A sign: what the hand shows (none: not known). circle: it goes round that
                    // place, 10 % of the field's height away, once a second (a hand that MOVES); sweep: to and fro across the field.
                    _gsPalm = d.text != "off"; _gsPalmDist = d.value; _gsPalmX = d.px; _gsPalmY = d.py;
                    _gsPalmFingers = -1; _gsPalmPath = "";
                    foreach (string word in (d.text ?? "").ToLowerInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (word == "circle" || word == "sweep") _gsPalmPath = word;
                        else if (GlassGames.FingersOf(word) >= 0) _gsPalmFingers = GlassGames.FingersOf(word);
                    }
                    Debug.Log("[MR] glass debug: " + (_gsPalm ? $"a palm at ({_gsPalmX:0.00}, {_gsPalmY:0.00}) of the field, {_gsPalmDist:0.00} m in front of it, showing '{GlassGames.Shape(_gsPalmFingers)}'{(_gsPalmPath.Length > 0 ? ", path " + _gsPalmPath : "")}" : "the palm is gone"));
                    return true;
                case "glassStart":
                {
                    // a game on the desk without the PC: lpctl debug glassStart 0 0 0 survival|umbrella|script:<path of a .lua file on this machine>|
                    //   test:flat|test:upright|test:flat:full|stop
                    // (the PC client has the same command for a game inside the screen: both get it - the one the partner is with plays)
                    string text = (d.text ?? "").Trim(), what = text.ToLowerInvariant(), arg = "";
                    int colon = text.IndexOf(':');
                    if (colon > 0) { arg = text.Substring(colon + 1).Trim(); what = text.Substring(0, colon).Trim().ToLowerInvariant(); }
                    if (!(GlassGames.Is(what) || what == "script" || what == "test" || what == "example") || _ch.owner != Env.Mr)
                    {
                        if (_gsLocal != null) { GlassStop("the debug command"); Debug.Log("[MR] glass debug: the local game is stopped"); }
                        return true;
                    }
                    GamePackDef pack = null; Func<GlassGame> make = null; string title = "";
                    if (what == "example")
                    {
                        // example:<id> - one of GlassScriptExamples (keeper | shooter | calculator | omok ...), as the pack it would be
                        var ex = GlassScriptExamples.Find(arg.ToLowerInvariant());
                        if (ex == null) { Debug.Log($"[MR] glass debug: there is no example '{arg}'"); return true; }
                        pack = GlassScriptExamples.Pack(ex); title = ex.title; what = GlassScript.TemplateName;
                    }
                    else if (what == "script")
                    {
                        // (a file on this machine, for trying a script before the PC has it; data, not a scene object)
                        string code;
                        try { code = System.IO.File.ReadAllText(arg); }
                        catch (Exception e) { Debug.Log($"[MR] glass debug: the script '{arg}' could not be read: {e.Message}"); return true; }
                        title = System.IO.Path.GetFileNameWithoutExtension(arg);
                        pack = new GamePackDef { id = "debug-script", template = "script", title = title, script = code };
                        if (!GlassGames.Is("script")) { Debug.Log("[MR] glass debug: this build has no scripted games yet (GlassGames knows no 'script')"); return true; }
                    }
                    else if (what == "test")
                    {
                        string mode = arg.ToLowerInvariant();
                        title = "Test Card";
                        pack = new GamePackDef { id = "debug-test", template = "test", title = title };
                        make = () => new GlassTestCard(mode.StartsWith("flat"), mode.EndsWith("full"));
                    }
                    if (_gsGame != null) GlassEnd("another local game begins");
                    _gsLocalPack = pack; _gsLocalMake = make;
                    _gsLocal = new GlassState { game = what, title = title, side = Env.Mr, runId = 900000 + ++_gsLocalRuns, phase = "intro" };
                    Debug.Log($"[MR] glass debug: a local game on the desk: {what}{(arg.Length > 0 ? " (" + arg + ")" : "")}");
                    return true;
                }
                case "glassInfo":
                {
                    var g = _gsGame; var host = GlassNow;
                    if (g == null) { Debug.Log($"[MR] glass info: no game runs here; the PC says game='{host.game}' side='{host.side}' run={host.runId} phase='{host.phase}'; _game='{_game}', fps {_fps:0}"); return true; }
                    var inp = _gsInput;
                    var gsc = g as GlassScript; var words = new System.Text.StringBuilder(); int nw = 0;
                    for (int i = 0; i < g.sprites.Count && nw < 4; i++) if (g.sprites[i].art == GlassArt.Label && !string.IsNullOrEmpty(g.sprites[i].text)) { if (nw++ > 0) words.Append(" | "); words.Append(g.sprites[i].text); }
                    Debug.Log($"[MR] glass info: words: {words}; aim {(inp.aimValid[0] ? $"({inp.aimX[0]:0.00}, {inp.aimY[0]:0.00})" : inp.aimValid[1] ? $"({inp.aimX[1]:0.00}, {inp.aimY[1]:0.00})" : "-")}; clock {(_gsClock ? "shown" : "hidden")}; stage yaw {_gsRot.eulerAngles.y:0}, to the head {YawTo(Input.HeadPos - (_gsOrigin - _gsRot * Vector3.forward * (_gsFlat ? GU * 0.5f : 0f))) + 180f:0}" +
                              (gsc != null ? $"; script: kit={gsc.UsedKit} aim={gsc.ReadAim} shots={gsc.ReadShots} outs={gsc.UsedOuts} sent={gsc.OutsSent}{(gsc.Error.Length > 0 ? " ERROR " + gsc.Error : "")}" : ""));
                    Debug.Log($"[MR] glass info: {g.Template}{(_gsFlat ? " (flat)" : "")} run {_gsRun} phase {g.phase} score {g.score} lives {g.lives} level {g.level} t={g.seconds:0.0}s banner='{g.banner}'; {g.sprites.Count} sprites ({_gsShown} pictures, {_gsLabelsShown} words, {_gsCardsShown} cards); " +
                              $"pet x={g.petX:0.00} {(g.petFacesRight ? "right" : "left")} {g.petPose}, at {_pos.ToString("0.000")}; hand R {(inp.valid[0] ? $"({inp.x[0]:0.00}, {inp.y[0]:0.00}) {inp.dist[0]:0.00} m, reach {GlassGames.HandRadius(g, inp, 0):0.000}{(GlassGames.Acts(inp, 0) ? ", acts" : "")}" : "-")}, " +
                              $"L {(inp.valid[1] ? $"({inp.x[1]:0.00}, {inp.y[1]:0.00}) {inp.dist[1]:0.00} m" : "-")}; seen R {(inp.seen[0] ? $"'{GlassGames.Shape(inp.fingers[0])}' {inp.speed[0]:0.00} m/s" : "-")} L {(inp.seen[1] ? $"'{GlassGames.Shape(inp.fingers[1])}' {inp.speed[1]:0.00} m/s" : "-")}; {_gsReports} reports, {_gsSounds} sounds; fps {_fps:0}");
                    return true;
                }
            }
            return false;
        }
    }
}
