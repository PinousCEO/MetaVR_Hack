using System;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// THROUGH THE GLASS (Shared/Glass.cs: GlassOut, GlassOutResult) - the games at the boundary between the screen and
    /// the room. A game may send things OUT of its field at the user: out of the real monitor while it is played inside
    /// the PC (glass.out from the PC client, through the host), up from the stage while it is played on the desk
    /// (MrGlass hands them over directly). The HEADSET flies them and decides what became of each:
    ///  - A thing appears AT THE GLASS - the spot of the calibrated screen that is the field's (x, y), or that spot of
    ///    the stage on the desk - with a ripple ring on the glass and a whoosh, and flies in a low ARC to where the
    ///    user's head was when it left, at the speed the script asked for (never faster; slower when the user sits so
    ///    near that it would arrive in less than OutMinSeconds). It is a REAL BALL ("the goalkeeper ball must be
    ///    clearly seen like in tennis - now it is just a 2D ball whooshing" - the user, 2026-11-18): a shaded, turning
    ///    sphere (GlassOutBallView: a toy football in a kicking game, else a ball that carries the game's picture, or a
    ///    plain one with a band) with a blob shadow on the desk under it, puffs of a trail and a soft halo. Never a
    ///    flat card.
    ///  - BLOCKED: an open hand whose palm is within OutBlockReach + the thing's size of the thing (either hand; the
    ///    stretch flown in this frame is tested, so nothing slips through between two frames). The ball is SQUASHED
    ///    flat against the palm for a moment and flashes, a ring bursts there, the shield flashes, sparks fly,
    ///    "bounce" - then it springs away, turning, and shrinks to nothing.
    ///  - HIT: it comes within OutHitRadius of the head: a rose ring bursts in front of the eyes where it came from, a
    ///    soft red vignette at the edge of the view for nearly half a second, and "error". The ball itself is gone at
    ///    once - nothing is ever drawn in the user's face.
    ///  - MISSED: the head was moved out of its way and it flew past (or more than OutMaxFlying were on their way).
    ///  The verdict goes back to the game: Msg.GlassOutResult to the host, or GlassGame.OutResult here on the desk.
    ///  - THE SHIELD: while a game that sends things out is on, an open hand that is raised carries a round
    ///    translucent disc on its palm - what the user blocks with is seen before anything flies.
    /// AIMING (GlassInput.aim*, fire) on the desk is here too: the pointing ray of a hand, met with the plane of the
    /// field, is the aim - a small ring on the field and a thin ray from the hand - and the moment that hand closes
    /// thumb and finger is the trigger.
    /// Comfort: seated play; at most OutMaxFlying things at once, none faster than OutTopSpeed, each at least
    /// OutMinSeconds on its way; one short flash, never a flicker.
    /// Debug (lpctl debug ...): glassOutTest [speed] [count] (things from the middle of the field - of the monitor when
    /// no game runs on the desk), glassBlock 1|0 (a scripted open hand held in the path of what flies), glassOutSlow k
    /// (slow motion for a capture), glassOutFreeze f (what flies stops at that share of its way, 0 = off), glassAim
    /// &lt;fire 0|1&gt; &lt;x&gt; &lt;y&gt; [off] (a hand that points at that place of the field on the desk), glassOutInfo.
    /// </summary>
    public sealed partial class MrApp
    {
        // ---- what both renderers of a glass game share (the PC client asks here: Windows/PcGlass.cs)
        /// <summary>How the PC client lays a game's field on its monitor: the floor of the field this far above the monitor's bottom edge (in monitor heights); a FLAT game at this share of the monitor's height, its bottom edge this far up.</summary>
        public const float GlassPcFloor = GlassDisplay.Floor, GlassPcFlatScale = GlassDisplay.FlatScale, GlassPcFlatBottom = GlassDisplay.FlatBottom;
        /// <summary>... and a TOOL (GlassIsTool) at this share of the monitor's height, standing on the floor line: its panel then ends under the sign instead of behind it.</summary>
        public const float GlassPcToolScale = GlassDisplay.ToolScale;
        /// <summary>The share of the monitor's height a pack's field is drawn at by the PC client.</summary>
        public static float GlassPcScale(GamePackDef pack, bool flat) => GlassDisplay.Scale(pack, flat);

        /// <summary>A TOOL: a script that has keys (button) and counts no lives - a calculator, a timer with buttons.</summary>
        public static bool GlassIsTool(GamePackDef pack) => GlassDisplay.IsTool(pack);

        /// <summary>What the sign shows from the first moment on (the bits of MrApp.GlassSignShows' `seen`): the hearts when the script counts lives at all - a game that begins with five must show five before one is lost.</summary>
        public static int GlassSignSeen(GamePackDef pack) => pack != null && !string.IsNullOrEmpty(pack.script) && pack.script.IndexOf("lives(", StringComparison.Ordinal) >= 0 ? 1 : 0;

        /// <summary>
        /// The run that begins FOLLOWED the partner through the screen (the host's GlassRules.Resume): its state comes
        /// with phase "intro" and a score, a level or seconds already in it. The side that starts the simulation then
        /// puts score / level / lives / seconds into the game after its Start. (A fresh run has 0 / 1 / 0 s: the
        /// game's own Start decides its lives.)
        /// </summary>
        public static bool GlassCarried(GlassState s) => s != null && s.phase == "intro" && (s.score != 0 || s.level > 1 || s.seconds > 0.01f);

        /// <summary>Does the sign show the clock? Not for a TOOL: a script that has keys (button) and counts no lives - a calculator's sign is its title.</summary>
        public static bool GlassSignClock(GamePackDef pack, GlassGame g) => !(g is GlassScript && GlassIsTool(pack));

        // ---- the rules of the flight
        public const int OutMaxFlying = 6;
        /// <summary>A thing is at least / at most this long on its way, and never faster than OutTopSpeed (metres a second) whatever the script asked for.</summary>
        // (OutMinSeconds was 0.6: on the headset that was "a 2D ball whooshing" - too short to see it come and get a hand up)
        public const float OutMinSeconds = 0.9f, OutMaxSeconds = 5f, OutTopSpeed = 2.5f;
        /// <summary>Blocked: the middle of an open palm within OutBlockReach + the thing's size of the thing. Hit: the thing within OutHitRadius of the head. Missed: it has flown OutPast beyond where the head was, or the head is behind it.</summary>
        public const float OutBlockReach = 0.09f, OutHitRadius = 0.18f, OutPast = 0.30f;
        /// <summary>The shield: a palm this far above the desk and this open carries it; it is this many metres across.</summary>
        const float ShieldRaised = 0.10f, ShieldOpen = 0.45f, ShieldSize = 0.20f;
        const float OutMissFade = 0.30f, OutRippleSeconds = 0.60f, OutVignetteSeconds = 0.45f, OutFlashSeconds = 0.30f;
        /// <summary>The ball: it is at least this large (a smaller one is not seen coming), its arc rises by this share of the way (at most OutArcMost metres), it turns this many degrees for a metre flown; blocked, it stays squashed on the palm for OutSquashHold seconds and is round again after OutSquashSeconds.</summary>
        const float OutMinSize = 0.05f, OutArc = 0.14f, OutArcMost = 0.14f, OutSpin = 420f, OutSquashHold = 0.07f, OutSquashSeconds = 0.18f;
        const float OutBurstSeconds = 0.32f, OutHitBurstSeconds = 0.42f;

        [SerializeField] GlassOutBallView _goBalls;      // the balls, their shadows, trails and bursts (authored: LPSceneU1Ball.cs)

        struct OutThing
        {
            public int state;                   // 0 = free, 1 = flying, 2 = bounced off a hand (fading), 3 = flew past (fading)
            public bool local; public int runId, id;
            public Vector3 portal; public float returnSeconds;
            public Vector3 from, dir, pos; // dir: unit, towards where the head was
            public float dist, speed, gone, size, t;
            public Color tint; public bool plain;
            public Vector3 arc;                 // the top of its arc over the straight way (up, across the way)
            public Vector3 spinAxis, hitN, hitTo; public Quaternion rot0; public float spin;      // how it turns; blocked: the palm's normal, and where on the palm it lands (from: where it was when it was blocked)
        }
        readonly OutThing[] _go = new OutThing[8];
        readonly float[] _goBurst = { -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f };      // the ring that bursts where each was blocked / got through (0..1, -1 = none)
        readonly bool[] _goBurstHit = new bool[8];
        float _goFreeze;                        // (debug glassOutFreeze: what flies stops at this share of its way, for a capture)
        string _goTestArt = "";                 // (debug glassOutTest ... <art>: the picture of the test's things)
        string _goLookScript; bool _goLookKick; // (is the pack that is on a game of kicking a ball? asked once per script)

        /// <summary>Where a thing is when it has flown `gone` metres of its way: the straight way to where the head was, with a low arc over it.</summary>
        static Vector3 OutPathPos(in OutThing t, float gone)
        {
            float f = Mathf.Clamp01(gone / Mathf.Max(0.01f, t.dist));
            return t.from + t.dir * gone + t.arc * (4f * f * (1f - f));
        }

        static bool OutHas(string s, string word) => s != null && s.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Is this pack a game in which a BALL is kicked at the user (a goalkeeper game)? Then what it sends out with the plain orb is a football. (No pack: the debug command's ball.)</summary>
        bool GlassOutKickGame(GamePackDef pack)
        {
            if (pack == null) return true;
            if (ReferenceEquals(pack.script, _goLookScript) && _goLookScript != null) return _goLookKick;
            _goLookScript = pack.script;
            _goLookKick = false;
            foreach (string w in OutKickWords) if (OutHas(pack.title, w) || OutHas(pack.howTo, w) || OutHas(pack.script, w)) { _goLookKick = true; break; }
            return _goLookKick;
        }
        static readonly string[] OutKickWords = { "kick", "goal", "keeper", "soccer", "football" };
        readonly float[] _goRipple = { -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f };
        readonly Vector3[] _goPalm = new Vector3[2], _goPalmNormal = new Vector3[2], _goPalmUp = new Vector3[2];
        readonly bool[] _goPalmOk = new bool[2];
        readonly float[] _goShield = new float[2], _goFlash = { -1f, -1f };
        float _goVignette = -1f, _goSeenAt = -100f;
        int _goSeenRun = int.MinValue, _goShieldRun = int.MinValue; bool _goShieldGame;
        int _goLaunched, _goBlocked, _goHit, _goMissed, _goTests, _goTestLeft; float _goTestAt, _goTestSpeed;
        bool _goBlockBot; float _goSlow = 1f;      // (debug glassOutSlow: the flight in slow motion, for a capture)
        // (how the frame rate holds while four or more are in the air)
        float _goBusyTime, _goBusyWorst; int _goBusyFrames;

        // ------------------------------------------------------------------ launching
        /// <summary>glass.out from the PC client, through the host: a game that is played inside a monitor sent something out of the screen.</summary>
        void OnGlassOut(GlassOut o)
        {
            if (o == null) return;
            var g = GlassNow;
            if (g.game.Length == 0 || o.runId != g.runId) { Debug.Log($"[MR] glass out: {o.id} of run {o.runId} is not of the game that is on (run {g.runId}): left out"); return; }
            GamePackDef pack = GlassOutPack(g.packId);
            var mv = Mon(g.monitorId) ?? FirstMon();
            Vector3 from, normal, up;
            if (mv != null)
            {
                // the same mapping as the PC client's (PcGlass.UvFromField): the field over the whole lit area, its floor a little above the bottom edge
                float W = mv.desc.bounds.w / (float)Mathf.Max(1, mv.desc.bounds.h);
                bool flat = pack != null && pack.script != null && pack.script.IndexOf("flat(true)", StringComparison.Ordinal) >= 0;
                float k = GlassPcScale(pack, flat), y0 = flat ? GlassPcFlatBottom : GlassPcFloor;
                float u = Mathf.Clamp01(0.5f + (o.x - W * 0.5f) * k / W), v = Mathf.Clamp01(1f - (o.y * k + y0));
                from = mv.ToWorld(u, v) - mv.Z * 0.012f; normal = -mv.Z; up = mv.Y;
            }
            else
            {
                // (no screen is known here: it comes from straight ahead)
                Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f;
                fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
                from = Input.HeadPos + fwd * 0.70f + Vector3.down * 0.10f; normal = -fwd; up = Vector3.up;
            }
            GlassOutLaunch(o, false, from, normal, up, pack);
        }

        GamePackDef GlassOutPack(string id)
        {
            var games = World != null ? World.games : null;
            if (games == null || string.IsNullOrEmpty(id)) return null;
            foreach (var p in games) if (p != null && p.id == id) return p;
            return null;
        }

        /// <summary>After every Step of a game on the desk: what it sent out goes up from the stage - at once, without the host.</summary>
        void GlassOutsLocal(GlassGame g)
        {
            var outs = g.outs;
            if (outs.Count == 0) return;
            for (int i = 0; i < outs.Count; i++)
            {
                var o = outs[i];
                if (o == null) continue;
                o.runId = _gsRun;
                Vector3 from = GlassAt(g, o.x, o.y, -0.012f);
                if (_gsFlat) GlassOutLaunch(o, true, from, Vector3.up, _gsRot * Vector3.forward, _gsPack);
                else GlassOutLaunch(o, true, from, -(_gsRot * Vector3.forward), Vector3.up, _gsPack);
            }
            outs.Clear();
        }

        void GlassOutLaunch(GlassOut o, bool local, Vector3 from, Vector3 normal, Vector3 up, GamePackDef pack)
        {
            _goSeenRun = o.runId; _goSeenAt = Time.time;
            int flying = 0, slot = -1;
            for (int i = 0; i < _go.Length; i++) { if (_go[i].state == 1) flying++; else if (slot < 0 && _go[i].state == 0) slot = i; }
            if (slot < 0) for (int i = 0; i < _go.Length; i++) if (_go[i].state == 3) { slot = i; break; }      // (one that is only fading gives way)
            if (flying >= OutMaxFlying || slot < 0) { GlassOutVerdict(local, o.runId, o.id, "missed", "too many are on their way"); return; }
            Vector3 to = Input.HeadPos - from; float dist = to.magnitude;
            if (dist < OutHitRadius) { GlassOutVerdict(local, o.runId, o.id, "missed", "the user's head is at the glass"); return; }
            var v = _stage;
            float asked = Mathf.Clamp(o.speed, 0.1f, OutTopSpeed);
            Vector3 way = to / dist, over = Vector3.up - way * Vector3.Dot(Vector3.up, way), side = Vector3.Cross(Vector3.up, way);
            var t = new OutThing
            {
                state = 1, portal = from, local = local, runId = o.runId, id = o.id, from = from, dir = way, pos = from, dist = dist,
                speed = Mathf.Max(Mathf.Min(asked, dist / OutMinSeconds), dist / OutMaxSeconds), size = Mathf.Clamp(o.size, OutMinSize, 0.20f),
                // a low arc over the straight way (none for a thing that goes straight up or down), and it rolls forward as it flies
                arc = over.sqrMagnitude > 0.04f ? over.normalized * Mathf.Min(dist * OutArc, OutArcMost) : Vector3.zero,
                spinAxis = side.sqrMagnitude > 0.01f ? side.normalized : Vector3.right,
                rot0 = Quaternion.Euler(o.id * 47f % 360f, o.id * 83f % 360f, 0f),
            };
            // its picture: a built-in one, one of the pack's own, or a plain shape in the colour that was asked for
            string art = o.art ?? "";
            Texture2D tex = null;
            t.plain = art == GlassArt.Disc || art == GlassArt.Rect || art == GlassArt.RingShape;
            if (art.StartsWith(GlassArt.PackPrefix, StringComparison.Ordinal))
            {
                var list = pack != null ? pack.art : null;
                if (list != null) for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && string.CompareOrdinal(art, GlassArt.PackPrefix.Length, list[i].name, 0, int.MaxValue) == 0) { tex = Icon(list[i].file); break; }
            }
            else if (GlassArtIndex.ContainsKey(art)) tex = GlassArtTexture(art);
            bool football = art.IndexOf("soccer", StringComparison.OrdinalIgnoreCase) >= 0 || art.IndexOf("football", StringComparison.OrdinalIgnoreCase) >= 0 || (art == GlassArt.Orb && GlassOutKickGame(pack));
            bool orb = football || art == GlassArt.Orb || tex == null;      // (the plain orb, or a picture that is not known / has not arrived: a ball without a picture)
            if (tex == null) { tex = GlassArtTexture(GlassArt.Orb); t.plain = false; }
            bool tinted = t.plain ? o.color >= 0 : o.color > 0;
            t.tint = tinted ? GlassTint(o.color, 1f) : Color.white;
            _go[slot] = t;
            // what the ball looks like: a toy football (the plain orb of a game in which a ball is kicked), a plain ball with a
            // band (a plain shape, or the orb of any other game), or a ball that carries the game's picture on its sides
            Color cream = new Color(1f, 0.97f, 0.90f, 1f), ink = new Color(0.29f, 0.18f, 0.15f, 1f), honeyPale = new Color(1f, 0.90f, 0.62f, 1f);
            Quaternion eyeRot = Cam != null ? Cam.transform.rotation : Input.HeadRot;
            _goBalls.SetLight(eyeRot * new Vector3(-0.40f, 0.80f, -0.45f));      // (lit from above, from the user's left and the user's side: its round shape is seen)
            if (football) _goBalls.SetLook(slot, GlassOutBallView.LookFootball, cream, ink, null);
            else if (orb || t.plain) _goBalls.SetLook(slot, GlassOutBallView.LookBand, tinted ? t.tint : honeyPale, tinted ? Color.Lerp(t.tint, Color.white, 0.55f) : cream, null);
            else _goBalls.SetLook(slot, GlassOutBallView.LookPicture, tinted ? Color.Lerp(t.tint, Color.white, 0.60f) : cream, ink, tex);
            v.outGlow[slot].color = tinted ? Color.Lerp(t.tint, Color.white, 0.35f) : new Color(1f, 0.90f, 0.58f, 1f);
            // the ring on the glass where it came through
            _goRipple[slot] = 0f;
            var rp = v.outRipple[slot];
            rp.transform.SetPositionAndRotation(from, Quaternion.LookRotation(-normal, Mathf.Abs(Vector3.Dot(normal, up)) > 0.95f ? Vector3.forward : up));
            rp.transform.localScale = Vector3.one * Mathf.Max(0.16f, t.size * 3.2f);
            Sfx("whoosh", from, 0.45f);
            _goLaunched++;
            Debug.Log($"[MR] glass out: {o.id} of run {o.runId} leaves {(local ? "the stage" : "the screen")} at ({o.x:0.00}, {o.y:0.00}) = {from.ToString("0.00")}: {art}, {t.size * 100f:0} cm, {dist:0.00} m to the head at {t.speed:0.00} m/s (asked {o.speed:0.00}) = {dist / t.speed:0.0} s; {flying + 1} on their way");
        }

        void GlassOutVerdict(bool local, int runId, int id, string what, string why)
        {
            if (what == "blocked") _goBlocked++; else if (what == "hit") _goHit++; else _goMissed++;
            Debug.Log($"[MR] glass out: {id} of run {runId} -> {what}{(why.Length > 0 ? " (" + why + ")" : "")}; so far {_goLaunched} out, {_goBlocked} blocked, {_goHit} hit, {_goMissed} missed");
            if (runId < 0) return;                                                      // (a thing of the debug command: nobody waits for it)
            if (local) { if (_gsGame != null && _gsRun == runId) _gsGame.OutResult(id, what); }
            else if (_link.Connected) _link.Send(Msg.GlassOutResult, new GlassOutResult { runId = runId, id = id, what = what });
        }

        // ------------------------------------------------------------------ every frame
        static float OutSegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float len2 = ab.sqrMagnitude;
            float k = len2 > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
            return Vector3.Distance(p, a + ab * k);
        }

        /// <summary>Is a game that sends things out on (on either side of the glass)? Then a raised open hand shows its shield.</summary>
        bool GlassOutShields(bool flying)
        {
            if (flying) return true;
            var g = GlassNow;
            if (g.game.Length == 0 || g.phase == "over" || g.phase == "moving" || !_link.Connected && _gsLocal == null) return false;
            if (g.runId == _goSeenRun) return true;                                    // (something has come out in this run)
            if (g.runId != _goShieldRun)
            {
                _goShieldRun = g.runId;
                var pack = _gsGame != null && _gsRun == g.runId ? _gsPack : GlassOutPack(g.packId);
                _goShieldGame = pack != null && pack.script != null && pack.script.IndexOf("shoot_out", StringComparison.Ordinal) >= 0;
            }
            return _goShieldGame;
        }

        /// <summary>Once a frame (from UpdateGlass, whether a game runs on the desk or not): the things in the air, the rings on the glass, the shields on the palms, the flash at the edge of the view.</summary>
        void UpdateGlassOut(float dt)
        {
            var v = _stage; var bv = _goBalls;
            if (bv == null) return;      // (a scene authored before the balls existed: LPSceneBuilder.BuildAll has not run)
            dt *= _goSlow;
            Vector3 head = Input.HeadPos;
            Vector3 eye = Cam != null ? Cam.transform.position : head;
            Quaternion eyeRot = Cam != null ? Cam.transform.rotation : Input.HeadRot;

            // a debug command asked for some: one every 0.15 s
            if (_goTestLeft > 0 && Time.time >= _goTestAt) { _goTestLeft--; _goTestAt = Time.time + 0.15f; GlassOutTest(_goTestSpeed); }

            int flying = 0;
            for (int i = 0; i < _go.Length; i++) if (_go[i].state == 1) flying++;
            if (flying >= 4) { _goBusyTime += dt; _goBusyFrames++; if (dt > _goBusyWorst) _goBusyWorst = dt; }

            // ---- the hands: an open palm that is raised is a shield
            for (int h = 0; h < 2; h++)
            {
                var hs = h == 0 ? Input.Right : Input.Left;
                bool ok = hs.tracked && !hs.pinch && !(Input.Simulated && hs.rayKind == 4) && hs.palmPos.y - _deskY > ShieldRaised && HandSpread(hs) >= ShieldOpen
                          && !(_mode == Mode.OnHand && h == _rideHand) && !(_mode == Mode.Held && h == _holdHand) && !(_grab >= 0 && h == _grabHand);
                _goPalmOk[h] = ok;
                if (ok) { _goPalm[h] = hs.palmPos; _goPalmNormal[h] = hs.palmNormal.sqrMagnitude > 0.5f ? hs.palmNormal : -(eyeRot * Vector3.forward); _goPalmUp[h] = hs.palmForward.sqrMagnitude > 0.5f ? hs.palmForward : Vector3.up; }
            }
            if (_goBlockBot)
            {
                // the scripted hand of a check: an open right palm held in the path of the first thing that flies, 0.30 m in
                // front of the head (with nothing in the air: there, straight ahead)
                Vector3 dir = -(eyeRot * Vector3.forward), over = Vector3.zero;
                for (int i = 0; i < _go.Length; i++) if (_go[i].state == 1) { dir = _go[i].dir; over = OutPathPos(_go[i], _go[i].dist - 0.24f) - (_go[i].from + dir * (_go[i].dist - 0.24f)); break; }
                _goPalmOk[0] = true; _goPalm[0] = head - dir * 0.24f + over + Vector3.down * 0.02f; _goPalmNormal[0] = -dir; _goPalmUp[0] = Vector3.up;
            }

            // ---- the things
            for (int i = 0; i < _go.Length; i++)
            {
                ref var t = ref _go[i];
                var glow = v.outGlow[i]; var ball = bv.balls[i]; var shadow = bv.shadows[i];
                bool show = t.state != 0, tail = false;
                float alpha = 1f, squash = 0f, flashed = 0f;
                if (t.state == 1)
                {
                    Vector3 a = t.pos;
                    t.gone += t.speed * dt;
                    if (_goFreeze > 0f) t.gone = Mathf.Min(t.gone, _goFreeze * t.dist);      // (a capture: it stops on its way)
                    t.pos = OutPathPos(t, t.gone); t.spin += t.speed * dt * OutSpin;
                    int by = -1;
                    for (int h = 0; h < 2 && by < 0; h++) if (_goPalmOk[h] && OutSegmentDistance(_goPalm[h], a, t.pos) <= OutBlockReach + t.size) by = h;
                    if (by >= 0)
                    {
                        // BLOCKED: squashed flat against the palm for a moment, then it springs away and shrinks; a ring bursts there; the shield flashes
                        Vector3 going = t.pos - a; going = going.sqrMagnitude > 1e-10f ? going.normalized : t.dir;
                        Vector3 n = _goPalmNormal[by]; if (Vector3.Dot(n, going) > 0f) n = -n;
                        t.state = 2; t.t = 0f; t.returnSeconds = Mathf.Clamp(Vector3.Distance(t.pos, t.portal) / Mathf.Max(0.55f,t.speed * 1.35f), 0.30f, 1.45f);
                        t.hitN = n; t.spinAxis = n; t.spin = 0f; t.rot0 = Quaternion.LookRotation(n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up);
                        // (the reach of a blocking hand is generous: the ball is seen to LAND on the palm - at most 6 cm from its middle)
                        Vector3 rel = t.pos - _goPalm[by], lat = Vector3.ClampMagnitude(rel - n * Vector3.Dot(rel, n), 0.06f);
                        t.from = t.pos; t.hitTo = _goPalm[by] + lat + n * (t.size * 0.30f);
                        _goBurst[i] = 0f; _goBurstHit[i] = false;
                        bv.bursts[i].transform.SetPositionAndRotation(t.hitTo - n * (t.size * 0.2f), t.rot0);
                        _goFlash[by] = 0f;
                        _sparks.Burst(t.pos, eyeRot * Vector3.right, Vector3.up, Mathf.Max(0.05f, t.size), 12);
                        Sfx("bounce", t.pos, 0.60f);
                        tail = true;
                    }
                    else if (OutSegmentDistance(head, a, t.pos) <= OutHitRadius)
                    {
                        // HIT: gone at once (nothing is drawn in the user's face); a rose ring bursts in front of the eyes, on the
                        // side it came from, and the edge of the view flashes
                        t.state = 0; show = false; _goVignette = 0f;
                        _goBurst[i] = 0f; _goBurstHit[i] = true;
                        Vector3 at = eye - t.dir * 0.42f + t.arc * 0.5f;
                        bv.bursts[i].transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - eye, eyeRot * Vector3.up));
                        Sfx2D("error", 0.45f);
                        GlassOutVerdict(t.local, t.runId, t.id, "hit", "");
                    }
                    else if (t.gone > t.dist + OutPast || Vector3.Dot(head - t.pos, t.dir) < -0.05f)
                    {
                        // MISSED: the head was moved out of its way
                        t.state = 3; t.t = 0f;
                        GlassOutVerdict(t.local, t.runId, t.id, "missed", $"it passed {OutSegmentDistance(head, t.from, t.pos):0.00} m from the head");
                    }
                    else tail = true;
                }
                else if (t.state == 2)
                {
                    // (it lands on the palm and is squashed flat there; then it springs away, round again, turning about the palm's normal)
                    t.t += dt;
                    if (t.t <= OutSquashHold)
                    {
                        float w = t.t / OutSquashHold;
                        t.pos = Vector3.Lerp(t.from, t.hitTo, w * (2f - w)); squash = w; flashed = w;
                    }
                    else
                    {
                        float travel = Mathf.Clamp01((t.t - OutSquashHold) / t.returnSeconds);
                        t.pos = Vector3.Lerp(t.hitTo, t.portal, travel) + t.arc * (2f * travel * (1f - travel));
                        t.spin += 480f * dt; tail = true;
                        float u = Mathf.Clamp01((t.t - OutSquashHold) / OutSquashSeconds);
                        squash = (1f - u) * (1f - u); flashed = 1f - u;
                    }
                    if (t.t >= OutSquashHold + t.returnSeconds)
                    {
                        t.state = 0; show = false; _goRipple[i] = 0f;
                        GlassOutVerdict(t.local, t.runId, t.id, "blocked", "the hand returned it through the glass");
                    }
                }
                else if (t.state == 3)
                {
                    t.t += dt; t.gone += t.speed * dt; t.pos = OutPathPos(t, t.gone); t.spin += t.speed * dt * OutSpin;
                    alpha = 1f - t.t / OutMissFade;
                    if (alpha <= 0f) { t.state = 0; show = false; }
                }
                if (ball.enabled != show) { ball.enabled = show; glow.enabled = show; }
                bool shade = false;
                float size = 0f;
                if (show)
                {
                    // THE BALL: a real sphere, turning as it flies. (It is not see-through: where it has to go it shrinks to nothing.)
                    size = t.size * Mathf.Clamp01(0.4f + t.gone / 0.06f) * Mathf.Max(0.001f, alpha);
                    Transform bt = ball.transform;
                    bt.SetPositionAndRotation(t.pos, Quaternion.AngleAxis(t.spin, t.spinAxis) * t.rot0);
                    // (blocked: its own z is the palm's normal - flat against the palm, wide across it)
                    bt.localScale = new Vector3(size * (1f + 0.26f * squash), size * (1f + 0.26f * squash), size * (1f - 0.50f * squash));
                    bv.SetFlash(i, flashed);
                    // a soft halo behind it, turned to the eye (the ball hides its middle)
                    Vector3 look = t.pos - eye;
                    var tr = v.outThings[i];
                    tr.SetPositionAndRotation(t.pos, look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, eyeRot * Vector3.up) : eyeRot);
                    tr.localScale = Vector3.one * size;
                    var gc = glow.color; glow.SetColors(new Color(gc.r, gc.g, gc.b, 0.34f * alpha), new Color(1f, 0.97f, 0.86f, 0.34f * alpha));
                    // its shadow on the desk, straight under it: darker and smaller the lower the ball is (it fades out near the
                    // user, where the desk ends)
                    float above = t.pos.y - _deskY;
                    shade = above > 0.005f && t.state != 3;
                    if (shade)
                    {
                        float f = Mathf.Clamp01(t.gone / Mathf.Max(0.01f, t.dist));
                        float near = t.state == 1 ? 1f - Mathf.SmoothStep(0f, 1f, (f - 0.70f) / 0.30f) : alpha;
                        shadow.transform.SetPositionAndRotation(new Vector3(t.pos.x, _deskY + 0.0014f, t.pos.z), Quaternion.Euler(90f, 0f, 0f));
                        shadow.transform.localScale = Vector3.one * (size * (1.25f + above * 1.0f));
                        shadow.SetColor(new Color(0.18f, 0.14f, 0.16f, 0.46f / (1f + above * 1.8f) * near));
                    }
                }
                if (shadow.enabled != shade) shadow.enabled = shade;
                // its trail: soft puffs where it was a moment ago, each smaller and fainter (turned to the eye: seen from any side)
                float step = Mathf.Max(0.035f, t.size * 0.55f);
                for (int k = 0; k < GlassOutBallView.Puffs; k++)
                {
                    var puff = bv.puffs[i * GlassOutBallView.Puffs + k];
                    float g = t.gone - (k + 1) * step;
                    bool on = tail && g > 0.01f;
                    if (puff.enabled != on) puff.enabled = on;
                    if (!on) continue;
                    Vector3 pp = OutPathPos(t, g), pl = pp - eye;
                    puff.transform.SetPositionAndRotation(pp, pl.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(pl, eyeRot * Vector3.up) : eyeRot);
                    puff.transform.localScale = Vector3.one * (size * (0.82f - 0.14f * k));
                    var pc = glow.color; puff.SetColor(new Color(pc.r, pc.g, pc.b, 0.46f - 0.09f * k));
                }
            }

            // ---- the rings that burst where a ball was blocked (cream to honey, on the palm) or got through (rose, in front of the eyes)
            for (int i = 0; i < _goBurst.Length; i++)
            {
                var ring = bv.bursts[i];
                bool on = _goBurst[i] >= 0f;
                if (on) { _goBurst[i] += dt / (_goBurstHit[i] ? OutHitBurstSeconds : OutBurstSeconds); if (_goBurst[i] >= 1f) { _goBurst[i] = -1f; on = false; } }
                if (ring.enabled != on) ring.enabled = on;
                if (!on) continue;
                float k = _goBurst[i], ease = 1f - (1f - k) * (1f - k), a = (1f - k) * (1f - k);
                ring.transform.localScale = Vector3.one * (_goBurstHit[i] ? Mathf.Lerp(0.10f, 0.46f, ease) : Mathf.Lerp(0.09f, 0.30f, ease));
                ring.param = new Vector4(0.90f, Mathf.Lerp(0.16f, 0.03f, k), 0f, 0f);
                if (_goBurstHit[i]) ring.SetColors(new Color(0.95f, 0.42f, 0.40f, 0.95f * a), new Color(1f, 0.80f, 0.74f, 0.95f * a));
                else ring.SetColors(new Color(1f, 0.97f, 0.90f, a), new Color(1f, 0.84f, 0.42f, a));
            }

            // ---- the rings on the glass
            for (int i = 0; i < _goRipple.Length; i++)
            {
                var rp = v.outRipple[i];
                bool on = _goRipple[i] >= 0f;
                if (on) { _goRipple[i] += dt / OutRippleSeconds; if (_goRipple[i] >= 1f) { _goRipple[i] = -1f; on = false; } }
                if (rp.enabled != on) rp.enabled = on;
                if (on) rp.SetParam(new Vector4(0.25f, 0f, _goRipple[i], 0f));
            }

            // ---- the shields on the palms
            bool shields = GlassOutShields(flying > 0);
            for (int h = 0; h < 2; h++)
            {
                _goShield[h] = Mathf.MoveTowards(_goShield[h], shields && _goPalmOk[h] ? 1f : 0f, dt / 0.15f);
                if (_goFlash[h] >= 0f) { _goFlash[h] += dt / OutFlashSeconds; if (_goFlash[h] >= 1f) _goFlash[h] = -1f; }
                float flash = _goFlash[h] >= 0f ? 1f - _goFlash[h] : 0f, a = Mathf.Max(_goShield[h], flash);
                bool on = a > 0.01f;
                var disc = v.shieldDisc[h]; var ring = v.shieldRing[h];
                if (disc.enabled != on) { disc.enabled = on; ring.enabled = on; }
                if (!on) continue;
                Vector3 n = _goPalmNormal[h], up = _goPalmUp[h];
                if (Mathf.Abs(Vector3.Dot(n, up)) > 0.95f) up = Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up;
                Quaternion rot = Quaternion.LookRotation(-n, up);
                float size = ShieldSize * Mathf.Max(0.001f, Ui.Pop(Mathf.Clamp01(a))) * (1f + 0.22f * flash);
                disc.transform.SetPositionAndRotation(_goPalm[h] + n * 0.025f, rot); ring.transform.SetPositionAndRotation(_goPalm[h] + n * 0.026f, rot);
                disc.transform.localScale = ring.transform.localScale = Vector3.one * size;
                Color sky = new Color(0.56f, 0.83f, 0.96f, 1f), cream = new Color(1f, 0.97f, 0.90f, 1f);
                Color fill = Color.Lerp(sky, cream, flash); fill.a = (0.20f + 0.50f * flash) * a;
                disc.SetColors(fill, fill);
                Color edge = Color.Lerp(sky, cream, flash); edge.a = (0.80f + 0.20f * flash) * a;
                ring.phase = Time.time * 0.12f;
                ring.SetColors(edge, new Color(cream.r, cream.g, cream.b, edge.a));
            }

            // ---- hit: a soft red flash round the edge of the view
            bool vig = _goVignette >= 0f;
            if (vig) { _goVignette += dt / OutVignetteSeconds; if (_goVignette >= 1f) { _goVignette = -1f; vig = false; } }
            if (v.vignette.enabled != vig) v.vignette.enabled = vig;
            if (vig)
            {
                // a ring 0.45 m in front of the eyes whose outer edge lies outside the view: only its soft inner side is seen.
                // The simulator's window: its outer edge just beyond the corners. A headset: at 50 degrees from straight ahead,
                // so that the red begins at about 34 degrees (the ring's soft inner side starts at 0.58 of its radius:
                // tan 34.8 = 0.58 tan 50). At 58 degrees it began at 43 - beyond what the lenses show clearly: the hit was not seen.
                const float away = 0.45f;
                float tanV = Mathf.Tan(Mathf.Clamp(Cam != null ? Cam.fieldOfView : 90f, 40f, 110f) * 0.5f * Mathf.Deg2Rad), tanH = tanV * (Cam != null ? Mathf.Clamp(Cam.aspect, 0.8f, 2f) : 1f);
                float tanEdge = Input.Simulated ? Mathf.Sqrt(tanH * tanH + tanV * tanV) * 1.03f : Mathf.Tan(50f * Mathf.Deg2Rad);
                v.vignette.transform.SetPositionAndRotation(eye + eyeRot * Vector3.forward * away, eyeRot);
                v.vignette.transform.localScale = Vector3.one * (2f * away * tanEdge);
                // (it comes up fast and goes slowly: at its most after a twentieth of a second, well seen for a third of one)
                float k = _goVignette, env = Mathf.Clamp01(k / 0.10f) * Mathf.Pow(1f - k, 1.5f);
                var red = new Color(0.93f, 0.36f, 0.36f, 0.90f * env);
                v.vignette.SetColors(red, red);
            }
        }

        // ------------------------------------------------------------------ AIMING at a field on the desk
        /// <summary>A hand aims at a standing field only from this far in front of it (nearer, it plays with the palm), at a lying one only from this high above the desk.</summary>
        const float GlassAimAway = 0.12f, GlassAimAbove = 0.15f;
        readonly bool[] _gsAimOn = new bool[2]; readonly Vector3[] _gsAimFrom = new Vector3[2];
        readonly float[] _gsAimShow = new float[2];
        bool _gsAimDbg; float _gsAimDbgX, _gsAimDbgY; bool _gsAimDbgFire;
        bool _gsBotPoint, _gsBotFire, _gsBotFireWas; float _gsBotFireT;

        /// <summary>Fills the aim of _gsInput: where each hand's pointing ray meets the field, and whether its trigger was pulled in this frame.</summary>
        void GlassAim(float dt, GlassGame g)
        {
            var inp = _gsInput;
            float U = GU, k = 1f - Mathf.Exp(-dt * 18f);
            Quaternion inv = Quaternion.Inverse(_gsRot);
            for (int h = 0; h < 2; h++)
            {
                bool was = _gsAimOn[h];
                inp.aimValid[h] = false; inp.fire[h] = false; _gsAimOn[h] = false;
                float x, y; bool fire; Vector3 from;
                if (_gsBot)
                {
                    // the bot of a game that is shot at (GlassBot) points and pulls the trigger
                    if (h != 0 || !_gsBotPoint) continue;
                    x = _botX; y = _botY; fire = _gsBotFire && !_gsBotFireWas; _gsBotFireWas = _gsBotFire;
                    from = Input.HeadPos + Input.HeadRot * new Vector3(0.16f, -0.22f, 0.28f);
                }
                else if (h == 0 && _gsAimDbg)
                {
                    x = _gsAimDbgX; y = _gsAimDbgY; fire = _gsAimDbgFire; _gsAimDbgFire = false;
                    from = Input.HeadPos + Input.HeadRot * new Vector3(0.16f, -0.22f, 0.28f);
                }
                else
                {
                    var hs = h == 0 ? Input.Right : Input.Left;
                    if (!HandPoints(h, hs) || _rayPanel[h] != null) continue;       // (its ray is on a board: that is what it points at)
                    Vector3 lo = inv * (hs.rayOrigin - _gsOrigin), ld = inv * hs.rayDir;
                    float t;
                    if (_gsFlat) { if (ld.y > -0.05f || lo.y < GlassAimAbove) continue; t = -lo.y / ld.y; }
                    else { if (ld.z < 0.05f || lo.z > -GlassAimAway) continue; t = -lo.z / ld.z; }
                    Vector3 hit = lo + ld * t;
                    x = hit.x / U + g.W * 0.5f; y = _gsFlat ? hit.z / U + 1f : hit.y / U;
                    if (x < -0.03f || x > g.W + 0.03f || y < -0.03f || y > 1.03f) continue;
                    x = Mathf.Clamp(x, 0f, g.W); y = Mathf.Clamp01(y);
                    fire = hs.pinchDown && !_uiConsumedPinch; from = hs.rayOrigin;
                    if (was) { x = Mathf.Lerp(inp.aimX[h], x, k); y = Mathf.Lerp(inp.aimY[h], y, k); }      // (a hand trembles: the aim follows it softly)
                }
                inp.aimValid[h] = true; inp.aimX[h] = x; inp.aimY[h] = y; inp.fire[h] = fire;
                _gsAimOn[h] = true; _gsAimFrom[h] = from;
            }
        }

        /// <summary>The ring on the field where a hand aims, and the thin ray from that hand - only in a game that reads the aim, shots or has keys.</summary>
        void GlassAimDraw(GlassGame g, float dt, float fade)
        {
            var v = _stage; var inp = _gsInput;
            var sc = g as GlassScript;
            bool reads = sc != null && (sc.ReadAim || sc.ReadShots || sc.UsedKit) && g.phase != "over" && !_gsFrozen;
            Vector3 eye = Cam != null ? Cam.transform.position : Input.HeadPos;
            for (int h = 0; h < 2; h++)
            {
                bool want = reads && inp.aimValid[h];
                _gsAimShow[h] = Mathf.MoveTowards(_gsAimShow[h], want ? 1f : 0f, dt / 0.12f);
                bool show = want && _gsAimShow[h] > 0.01f;
                bool small = sc != null && sc.ReadAim;                 // (the script draws a crosshair of its own: only a small ring in its middle)
                for (int p = 0; p < 3; p++) { var q = v.aimParts[h * 3 + p]; bool on = show && !(small && p == 2); if (q.enabled != on) q.enabled = on; }
                bool ray = false;
                if (show)
                {
                    var c = v.aimCursor[h];
                    c.localPosition = FieldLocal(g, inp.aimX[h], inp.aimY[h], _gsFlat ? GlassFlatLift : 0.026f);
                    c.localRotation = FieldTurn(0f);
                    c.localScale = Vector3.one * ((small ? 0.022f : 0.038f) * Mathf.Max(0.001f, Ui.Pop(_gsAimShow[h])) * (inp.fire[h] ? 0.75f : 1f));
                    Color ink = new Color(0.29f, 0.18f, 0.15f, fade), cream = new Color(1f, 0.97f, 0.90f, fade);
                    v.aimParts[h * 3].SetColors(ink, ink); v.aimParts[h * 3 + 1].SetColors(cream, cream); if (!small) v.aimParts[h * 3 + 2].SetColors(cream, cream);
                    Vector3 to = c.position, dir = to - _gsAimFrom[h]; float len = dir.magnitude;
                    if (len > 0.10f)
                    {
                        dir /= len;
                        Vector3 start = _gsAimFrom[h] + dir * 0.03f, mid = (start + to) * 0.5f, toEye = eye - mid; toEye -= dir * Vector3.Dot(toEye, dir);
                        if (toEye.sqrMagnitude > 1e-6f)
                        {
                            ray = true;
                            v.aimRay[h].transform.SetPositionAndRotation(mid, Quaternion.LookRotation(-toEye.normalized, -dir));
                            v.aimRay[h].transform.localScale = new Vector3(0.007f, len - 0.03f, 1f);
                        }
                    }
                }
                if (v.aimRay[h].enabled != ray) v.aimRay[h].enabled = ray;
            }
        }

        // ------------------------------------------------------------------ debug
        /// <summary>glassOutTest: one thing from the middle of the field of the game on the desk - or, when no game is on at all, from the middle of the first screen.</summary>
        void GlassOutTest(float speed)
        {
            // (the ball of the goalkeeper game: the orb, 8.5 cm. glassOutTest with text "shot" / "heart" / "disc" ...: that picture instead)
            var o = new GlassOut { runId = -1, id = 9000 + ++_goTests, speed = speed > 0f ? speed : 0.7f, art = string.IsNullOrEmpty(_goTestArt) ? GlassArt.Orb : _goTestArt, size = 0.085f };
            if (_gsGame != null)
            {
                o.x = _gsGame.W * (0.5f + 0.06f * ((_goTests % 5) - 2)); o.y = 0.5f + 0.05f * ((_goTests % 3) - 1); o.runId = _gsRun;
                Vector3 from = GlassAt(_gsGame, o.x, o.y, -0.012f);
                if (_gsFlat) GlassOutLaunch(o, true, from, Vector3.up, _gsRot * Vector3.forward, _gsPack);
                else GlassOutLaunch(o, true, from, -(_gsRot * Vector3.forward), Vector3.up, _gsPack);
                return;
            }
            if (GlassNow.game.Length > 0) return;       // (a game inside the PC: the PC client has the same command and sends one out of the screen)
            var mv = FirstMon();
            if (mv != null) GlassOutLaunch(o, true, mv.ToWorld(0.5f + 0.05f * ((_goTests % 5) - 2), 0.5f + 0.05f * ((_goTests % 3) - 1)) - mv.Z * 0.012f, -mv.Z, mv.Y, null);
            else GlassOutLaunch(o, true, Input.HeadPos + Input.HeadRot * new Vector3(0.04f * ((_goTests % 5) - 2), -0.1f, 0.8f), -(Input.HeadRot * Vector3.forward), Vector3.up, null);
        }

        bool OnDebugGlassOut(DebugCommand d)
        {
            switch (d.cmd)
            {
                case "glassOutTest":
                    // lpctl debug glassOutTest [speed m/s] [count]: so many things fly at the user, one every 0.15 s
                    _goTestSpeed = d.value; _goTestLeft = Mathf.Clamp(Mathf.RoundToInt(d.px), 1, 12); _goTestAt = 0f; _goTestArt = d.text ?? "";
                    Debug.Log($"[MR] glass debug: {_goTestLeft} thing(s) are sent out at {(d.value > 0f ? d.value : 0.7f):0.00} m/s");
                    return true;
                case "glassBlock":
                    _goBlockBot = d.value > 0.5f;
                    Debug.Log("[MR] glass debug: the scripted open hand " + (_goBlockBot ? "is held in the path of what flies" : "is gone"));
                    return true;
                case "glassAim":
                    // lpctl debug glassAim <fire 0|1> <x> <y> [off]: the right hand points at (x, y) of the field on the desk; fire 1 = it pulls the trigger once
                    _gsAimDbg = d.text != "off"; _gsAimDbgX = d.px; _gsAimDbgY = d.py; _gsAimDbgFire = _gsAimDbg && d.value > 0.5f;
                    Debug.Log("[MR] glass debug: " + (_gsAimDbg ? $"a hand points at ({d.px:0.00}, {d.py:0.00}) of the field{(_gsAimDbgFire ? " and pulls the trigger" : "")}" : "the pointing hand is gone"));
                    return true;
                case "glassOutSlow":
                    // lpctl debug glassOutSlow <0.02 .. 1>: what flies, the shield's flash and the flash of a hit run at this share of their pace (for a capture; 1 = as they are)
                    _goSlow = d.value > 0f ? Mathf.Clamp(d.value, 0.02f, 1f) : 1f;
                    Debug.Log($"[MR] glass debug: what flies out runs at {_goSlow:0.00} of its pace");
                    return true;
                case "glassOutFreeze":
                    // lpctl debug glassOutFreeze <0 .. 0.95>: what flies stops at this share of its way and hangs there (for a capture); 0 = it flies on
                    _goFreeze = Mathf.Clamp(d.value, 0f, 0.95f);
                    Debug.Log("[MR] glass debug: " + (_goFreeze > 0f ? $"what flies out stops at {_goFreeze:0.00} of its way" : "what flies out flies on"));
                    return true;
                case "glassOutInfo":
                {
                    int flying = 0; for (int i = 0; i < _go.Length; i++) if (_go[i].state == 1) flying++;
                    Debug.Log($"[MR] glass out info: {_goLaunched} out, {_goBlocked} blocked, {_goHit} hit, {_goMissed} missed; {flying} in the air; with 4 or more in the air: {_goBusyFrames} frames in {_goBusyTime:0.0} s = {(_goBusyTime > 0f ? _goBusyFrames / _goBusyTime : 0f):0} fps, the longest {_goBusyWorst * 1000f:0} ms; fps now {_fps:0}; shields {(GlassOutShields(flying > 0) ? "on" : "off")} (right {(_goPalmOk[0] ? "raised" : "-")}, left {(_goPalmOk[1] ? "raised" : "-")})");
                    return true;
                }
            }
            return false;
        }
    }
}
