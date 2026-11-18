using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using SN = System.Numerics;

namespace LivingPartner.MR
{
    /// <summary>
    /// SCREEN SCAMPS in the room (the game itself is Shared/Swarm.cs; what is shown is MrCore/Swarm, SwarmView).
    /// Little monsters gather BEHIND THE GLASS of the user's screen - or, with no screen calibrated (playing without the
    /// PC), in a storybook window that stands at the far side of the desk - press against it, burst out and come for
    /// the user, who sits. Bolts of light fire by themselves along the pointing finger (no pinch; a wide cone of help,
    /// a thin line and a ring show where they go), gems fly in, and at every level time slows to a stop and three
    /// augment cards float within reach: poke one, or point at it and pinch. A monster that arrives costs a heart (a
    /// soft red ring at the edge of the view, a sound, and a ring of light that pops what is close). After two and a
    /// half minutes Big Grump squeezes out. A card with the result at the end; the best score is kept on the device.
    /// The headset decides everything (it works the same with and without the PC); with a PC connected the same state
    /// goes to it ten times a second (Msg.SwarmState) so that the screen can show them behind its own glass (Windows/PcSwarm.cs).
    /// Nothing is created while it runs: every monster, bolt, gem and ring is an authored object of a pool; the
    /// monsters' shapes are mesh data built once (SwarmMeshes), drawn with one shared material.
    /// Debug (lpctl debug ... / a local script): swarmStart, swarmStop, swarmBot 0|1|2 (a hand that points at the nearest
    /// monster; 2: it also takes the first card after four seconds), swarmAim 0 u v (that hand points at this place of the glass, -1..1), swarmPick n (card 0..2),
    /// swarmTime k (the game's clock runs k times as fast), swarmSkip s (the clock is put forward to s seconds),
    /// swarmXp n, swarmAug id level, swarmHearts n, swarmBossHp n, swarmFill n (n more monsters at once), swarmDesk 1 (the window on the desk even when a screen is known), swarmInfo.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Screen Scamps (MrCore/Swarm)")]
        [SerializeField] SwarmView _swarm;

        readonly SwarmSim _sw = new SwarmSim();
        bool _swOn, _swInit, _swDeskOnly; MonView _swMon;
        Mesh[] _swMesh, _swFlash;
        readonly int[] _swKindShown = new int[SwarmSim.MaxMonsters]; readonly bool[] _swFlashShown = new bool[SwarmSim.MaxMonsters];
        Vector3 _swCenter, _swRight, _swUp, _swOut; float _swHalfW, _swHalfH, _swFar = 1f;
        Quaternion _swGlassRot = Quaternion.identity;
        Vector3 _swAimDir, _swAimFrom; bool _swAimSet, _swAiming;
        float _swClockScale = 1f, _swBannerT = -1f, _swBannerLife = 1.6f, _swVig = -1f, _swOuchT = -1f, _swOverT = -1f, _swGlow, _swCombo, _swComboAt;
        int _swBot; Vector2 _swBotAim; bool _swBotFixed; Vector3 _swBotDir;
        int _swHearts = -1, _swLevel = -1, _swClock = -1, _swXp = -1, _swBossHp = -1, _swBest; bool _swNewBest, _swResultUp;
        Vector3 _swCardsAt, _swCardsRight; float _swCardsT;
        GamePackDef _swPack;
        // the frame time while a wave is out (for the log)
        float _swBusyTime, _swBusyWorst; int _swBusyFrames, _swBusyMost;
        readonly float[] _swSfxAt = new float[24];

        struct SwFx { public float t, life, size; public Vector3 pos, pos2; public Color color; }
        readonly SwFx[] _swRipples = new SwFx[10], _swPuffs = new SwFx[24], _swChains = new SwFx[6];
        int _swRippleNext, _swPuffNext, _swChainNext;

        static readonly Color SwHoney = new Color(1f, 0.84f, 0.42f, 1f), SwCream = new Color(1f, 0.97f, 0.90f, 1f), SwRose = new Color(0.96f, 0.55f, 0.60f, 1f), SwSky = new Color(0.56f, 0.83f, 0.96f, 1f);

        public bool SwarmActive => _swOn;

        /// <summary>The game is in the bag whatever the PC's list says (it comes with the app): first among the games.</summary>
        void SwarmEnsurePack(StateSnapshot s)
        {
            var w = s != null ? s.world : null;
            if (w == null) return;
            if (w.games == null) w.games = new List<GamePackDef>();
            foreach (var g in w.games) if (g != null && g.template == SwarmRules.Template) return;
            if (_swPack == null) _swPack = new GamePackDef { id = SwarmRules.PackId, template = SwarmRules.Template, title = SwarmRules.Title, howTo = "검지로 조준하면 발사합니다." };
            w.games.Insert(0, _swPack);
        }

        void InitSwarm()
        {
            if (_swInit) return;
            _swInit = true;
            SwarmMeshes.Build(out _swMesh, out _swFlash);
            var v = _swarm;
            for (int i = 0; i < v.cardButtons.Length; i++) { int k = i; v.cardButtons[i].press = () => SwarmChoose(k, "a hand"); }
            v.again.press = () => { Debug.Log("[MR] swarm: Play again pressed"); SwarmBegin("Play again"); };
            v.close.press = () => StopSwarm("Close pressed");
            _swBest = PlayerPrefs.GetInt("lp.swarm.best", 0);
        }

        /// <summary>Starts the game (the bag, a spoken wish, a check). Whatever else is being played ends.</summary>
        void StartSwarm(string how)
        {
            if (_swOn && _sw.phase != SwarmPhase.Over) { Caption("We are already playing!", 2.5f); return; }
            if (_calibrating) { Caption("One moment - let me finish finding your screens.", 3f); return; }
            StopGames("another game");
            if (_tn.active || _tnPendingAt >= 0f) ToggleTennis("another game");
            Debug.Log("[MR] swarm: start asked by " + how);
            SwarmBegin(how);
        }

        void SwarmBegin(string how)
        {
            InitSwarm();
            var v = _swarm;
            Vector3 head = Input.HeadPos, fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            // the screen in front of the user (the one the partner is in first), when one is known and near enough
            MonView mv = null;
            if (!_swDeskOnly)
            {
                float best = float.MaxValue;
                foreach (var m in _mons.Values)
                {
                    float score = SwarmRules.ScreenScore(Conv.N(head), Conv.N(Input.HeadRot * Vector3.forward), Conv.N(m.Origin),
                        Conv.N(m.X), Conv.N(m.Y), Conv.N(-m.Z), m.frame.widthM, m.frame.heightM, _snap?.pc?.monitorId == m.desc.id);
                    if (score < best) { best = score; mv = m; }
                }
            }
            _swMon = mv;
            if (mv != null)
            {
                _swCenter = mv.Origin; _swRight = mv.X; _swUp = mv.Y; _swOut = -mv.Z;
                _swHalfW = mv.frame.widthM * 0.5f; _swHalfH = mv.frame.heightM * 0.5f;
            }
            else
            {
                _swHalfW = 0.21f; _swHalfH = 0.125f;
                _swRight = Vector3.Cross(Vector3.up, fwd); _swUp = Vector3.up; _swOut = -fwd;
                _swCenter = new Vector3(head.x, _deskY + 0.028f + _swHalfH, head.z) + fwd * 0.64f;
            }
            _swGlassRot = Quaternion.LookRotation(-_swOut, _swUp);
            float dist = Vector3.Distance(head, _swCenter);
            _swFar = Mathf.Clamp(dist / 0.70f, 1f, 2.2f);
            var arena = new SwarmArena
            {
                center = Conv.N(_swCenter), right = Conv.N(_swRight), up = Conv.N(_swUp), outward = Conv.N(_swOut), halfW = _swHalfW, halfH = _swHalfH, deskY = _deskY,
                pace = Mathf.Clamp(dist / 0.70f, 0.85f, 2.2f),
            };
            _sw.Start((uint)(System.DateTime.UtcNow.Ticks & 0x7FFFFFFF) | 1u, arena);
            _swOn = true; _swOverT = -1f; _swResultUp = false; _swNewBest = false; _swVig = -1f; _swOuchT = -1f; _swBannerT = -1f; _swAimSet = false; _swCombo = 0f;
            _swPcSeenAt = -100f;
            _swHearts = _swLevel = _swClock = _swXp = _swBossHp = -1;
            _swBusyTime = 0f; _swBusyWorst = 0f; _swBusyFrames = 0; _swBusyMost = 0;
            for (int i = 0; i < _swKindShown.Length; i++) { _swKindShown[i] = -1; _swFlashShown[i] = false; }
            for (int i = 0; i < _swRipples.Length; i++) _swRipples[i].life = 0f;
            for (int i = 0; i < _swPuffs.Length; i++) _swPuffs[i].life = 0f;
            for (int i = 0; i < _swChains.Length; i++) _swChains[i].life = 0f;

            // ---- what stands still for the whole run: the window (no screen), the sheet on the glass, the sign
            v.root.SetActive(true);
            bool window = mv == null;
            if (v.window.activeSelf != window) v.window.SetActive(window);
            if (window) v.window.transform.SetPositionAndRotation(_swCenter, _swGlassRot);
            v.gate.transform.SetPositionAndRotation(_swCenter, _swGlassRot);
            v.gate.transform.localScale = new Vector3(_swHalfW * 2f, _swHalfH * 2f, 1f);
            v.glassVeil.transform.SetPositionAndRotation(_swCenter - _swOut * 0.002f, _swGlassRot);
            Ui.SizePanel(v.glassVeil, _swHalfW * 2f, _swHalfH * 2f, 0.012f, 0f);
            v.glassGlow.transform.SetPositionAndRotation(_swCenter + _swOut * 0.003f, _swGlassRot);
            v.hud.SetPositionAndRotation(_swCenter + _swUp * (_swHalfH + 0.050f * _swFar) + _swOut * 0.012f, _swGlassRot);
            v.hud.localScale = Vector3.one * _swFar;
            foreach (var c in v.cards) c.Hide();
            v.result.Hide();
            SwarmBanner("검지로 조준", 2f);
            Sfx("squeak", _swCenter, 0.6f);
            if (_ch.owner == Env.Mr && _mode == Mode.Free) { _motor.Face(YawTo(_swCenter - _pos)); _emoteForced = Emote.Startle; _emoteT = 1.2f; }
            Debug.Log($"[MR] swarm: a run begins ({how}); they come out of {(mv != null ? "the screen " + mv.desc.id : "the window on the desk")} {dist:0.00} m away, {(_swHalfW * 200f):0} x {(_swHalfH * 200f):0} cm; best so far {_swBest}");
        }

        /// <summary>Ends the game and puts everything of it away.</summary>
        void StopSwarm(string why)
        {
            if (!_swOn) return;
            _swOn = false; _sw.Stop();
            SwarmSendState(true);
            var v = _swarm;
            foreach (var c in v.cards) c.Hide();
            v.result.Hide();
            v.root.SetActive(false);
            Debug.Log("[MR] swarm: ended (" + why + ")" + SwarmFrameLine());
        }

        string SwarmFrameLine() => _swBusyFrames > 0 ? $"; with 20 or more monsters out: {_swBusyFrames} frames, {_swBusyTime / _swBusyFrames * 1000f:0.0} ms a frame on average, {_swBusyWorst * 1000f:0.0} ms the longest, {_swBusyMost} monsters at most" : "";

        void SwarmBanner(string text, float seconds)
        {
            var v = _swarm;
            v.banner.text = text; _swBannerT = 0f; _swBannerLife = seconds;
        }

        void SwarmChoose(int card, string how)
        {
            if (!_swOn || _sw.phase != SwarmPhase.Choosing || _swCardsT < 0.35f) return;
            int a = _sw.choices[Mathf.Clamp(card, 0, 2)];
            if (!_sw.Choose(card)) return;
            SwarmEvents();
            Debug.Log($"[MR] swarm: level {_sw.level}: '{SwarmAug.Names[a]}' taken by {how} (now {_sw.aug[a]})");
            if (_sw.phase != SwarmPhase.Choosing) foreach (var c in _swarm.cards) c.Hide();
        }

        bool OnDebugSwarm(DebugCommand d)
        {
            switch (d.cmd)
            {
                case "swarmStart": StartSwarm("a check"); return true;
                case "swarmStop": StopSwarm("a check"); return true;
                case "swarmBot": _swBot = (int)d.value; _swBotFixed = false; Debug.Log("[MR] swarm: the scripted hand is " + (_swBot != 0 ? "on" : "off")); return true;
                case "swarmAim": _swBot = 1; _swBotFixed = true; _swBotAim = new Vector2(d.px / 100f, d.py / 100f); return true;
                case "swarmPick": SwarmChoose((int)d.value, "a check"); return true;
                case "swarmTime": _swClockScale = Mathf.Clamp(d.value, 0f, 20f); Debug.Log("[MR] swarm: the clock runs x" + _swClockScale); return true;
                case "swarmSkip": _sw.SkipTo(d.value); Debug.Log($"[MR] swarm: the clock is put forward to {_sw.time:0} s"); return true;
                case "swarmXp": _sw.GiveXp((int)d.value); return true;
                case "swarmAug": { int a = Mathf.Clamp((int)d.value, 0, SwarmAug.Count - 1); _sw.aug[a] = Mathf.Clamp((int)d.px, 0, SwarmAug.Max[a]); Debug.Log($"[MR] swarm: '{SwarmAug.Names[a]}' is at {_sw.aug[a]}"); return true; }
                case "swarmHearts": _sw.hearts = Mathf.Clamp((int)d.value, 1, 99); if (_sw.hearts > _sw.maxHearts) _sw.maxHearts = _sw.hearts; return true;
                case "swarmBossHp": if (_sw.bossIndex >= 0 && _sw.monsters[_sw.bossIndex].alive && _sw.monsters[_sw.bossIndex].kind == SwarmKind.Boss) _sw.monsters[_sw.bossIndex].hp = Mathf.Max(1f, d.value); return true;
                case "swarmDesk": _swDeskOnly = d.value > 0.5f; return true;
                case "swarmFill":
                {
                    // a full wave at once, in front of the glass (for the frame time and for pictures)
                    int n = Mathf.Clamp((int)d.value, 1, SwarmSim.MaxMonsters);
                    for (int i = 0; i < n; i++)
                    {
                        int kind = i % 9 == 8 ? SwarmKind.Tank : i % 5 == 4 ? SwarmKind.Splitter : i % 3 == 2 ? SwarmKind.Flyer : i % 7 == 6 ? SwarmKind.Mini : SwarmKind.Hopper;
                        Vector3 at = _swCenter + _swOut * (0.08f + 0.05f * (i % 5)) + _swRight * (((i * 37) % 100 / 100f - 0.5f) * 2f * _swHalfW) + _swUp * (((i * 61) % 100 / 100f - 0.5f) * 2f * _swHalfH);
                        _sw.Put(kind, Conv.N(at));
                    }
                    Debug.Log($"[MR] swarm: {n} more put out in front of the glass ({_sw.alive} out now)");
                    return true;
                }
                case "swarmInfo":
                    Debug.Log($"[MR] swarm: on={_swOn} phase={_sw.phase} time={_sw.time:0.0} hearts={_sw.hearts}/{_sw.maxHearts} level={_sw.level} xp={_sw.xp}/{_sw.XpNeed} popped={_sw.kills} score={_sw.score} out={_sw.alive} boss={(_sw.bossDead ? "popped" : _sw.bossOut ? "out" : _sw.bossSpawned ? "squeezing" : "no")} aiming={_swAiming} best={_swBest} fps={_fps:0}{SwarmFrameLine()}");
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ effects (pooled)
        void SwRipple(Vector3 at, float size, Color c, float life = 0.5f)
        {
            _swRipples[_swRippleNext] = new SwFx { t = 0f, life = life, size = size, pos = at, color = c };
            _swRippleNext = (_swRippleNext + 1) % _swRipples.Length;
        }
        void SwPuff(Vector3 at, float size, Color c, float life = 0.32f)
        {
            _swPuffs[_swPuffNext] = new SwFx { t = 0f, life = life, size = size, pos = at, color = c };
            _swPuffNext = (_swPuffNext + 1) % _swPuffs.Length;
        }
        void SwSfx(int slot, string name, Vector3 at, float volume, float pitch = 1f, float every = 0.07f)
        {
            if (Time.time - _swSfxAt[slot] < every) return;
            _swSfxAt[slot] = Time.time;
            Sfx(name, at, volume, pitch);
        }

        /// <summary>What happened in the step just made: rings, puffs, sounds, the partner's part.</summary>
        void SwarmEvents()
        {
            Vector3 right = Input.HeadRot * Vector3.right;
            bool partnerOut = _ch.owner == Env.Mr && _rig.Visible;
            for (int e = 0; e < _sw.eventCount; e++)
            {
                ref SwarmEvent ev = ref _sw.events[e];
                Vector3 p = Conv.U(ev.pos);
                switch (ev.type)
                {
                    case SwarmEv.Emerge:
                        SwRipple(p + _swOut * 0.004f, ev.a == SwarmKind.Tank ? 0.20f : 0.13f, SwHoney);
                        _swBursts++;
                        if (ev.b == 1) { _sparks.Burst(p + _swOut * 0.02f, _swRight, _swUp, 0.035f, 5); SwSfx(0, "pop", p, 0.55f, 0.8f, 0.05f); _swGlow = 1f; }
                        else SwSfx(0, "pop", p, 0.35f, 1.1f);
                        break;
                    case SwarmEv.Hit: SwPuff(p, 0.035f, SwCream, 0.18f); SwSfx(1, "tap", p, 0.30f, 1.5f, 0.06f); break;
                    case SwarmEv.Pop:
                    {
                        Color c = SwarmMeshes.Body[Mathf.Clamp(ev.a, 0, SwarmKind.Count - 1)];
                        float size = SwarmKind.Radius[Mathf.Clamp(ev.a, 0, SwarmKind.Count - 1)] * 3.2f;
                        SwPuff(p, size, c, 0.34f);
                        if (ev.a == SwarmKind.Tank || ev.a == SwarmKind.Boss || ev.a == SwarmKind.Splitter) _sparks.Burst(p, right, Vector3.up, size * 0.5f, ev.a == SwarmKind.Boss ? 16 : 7);
                        if (ev.b == 0) SwSfx(2, "pop", p, 0.5f, ev.a == SwarmKind.Tank ? 0.7f : ev.a == SwarmKind.Mini ? 1.5f : 1.15f, 0.05f);
                        break;
                    }
                    case SwarmEv.Fire: SwSfx(3, "step", p, 0.35f, 1.9f, 0.09f); break;
                    case SwarmEv.Gem:
                        if (Time.time - _swComboAt > 0.9f) _swCombo = 0f;
                        _swCombo = Mathf.Min(8f, _swCombo + 1f); _swComboAt = Time.time;
                        SwSfx(4, "drop", p, 0.22f, 1.2f + 0.08f * _swCombo, 0.05f);
                        break;
                    case SwarmEv.HeartLost:
                        _swVig = 0f; _swOuchT = 0f;
                        Sfx2D("lose", 0.7f, 1.2f);
                        if (partnerOut) { _emoteForced = Emote.Startle; _emoteT = 0.9f; }
                        Debug.Log($"[MR] swarm: a {SwarmKind.Names[Mathf.Clamp(ev.a, 0, SwarmKind.Count - 1)]} got to the user: {ev.b} heart(s) left at {_sw.time:0} s");
                        break;
                    case SwarmEv.LevelUp:
                        Sfx2D("sparkle", 0.6f);
                        if (partnerOut) { _emoteForced = Emote.Happy; _emoteT = 1.6f; _sparks.Burst(_rig.HeadTopWorld, right, Vector3.up, 0.04f, 8); }
                        break;
                    case SwarmEv.Choices: SwarmShowCards(); break;
                    case SwarmEv.Chosen:
                        Sfx2D("coin", 0.6f);
                        SwarmBanner(SwarmAug.Names[ev.a] + (SwarmAug.Max[ev.a] < 50 && ev.b > 1 ? "  Lv " + ev.b : ""), 1.2f);
                        break;
                    case SwarmEv.CometFire:
                        SwSfx(5, "whoosh", p, 0.25f, 1.5f, 0.2f);
                        break;
                    case SwarmEv.Surge:

                        Sfx("squeak", _swCenter, 0.5f, 0.8f);
                        break;
                    case SwarmEv.BossWarn:
                        SwarmBanner("가시 왕", 2.0f);
                        Sfx("error", _swCenter, 0.8f, 0.6f);
                        if (partnerOut) { _emoteForced = Emote.Cower; _emoteT = 2.5f; }
                        Debug.Log($"[MR] swarm: the boss is squeezing out at {_sw.time:0} s (level {_sw.level}, {_sw.kills} popped, {_sw.hearts} hearts)");
                        break;
                    case SwarmEv.BossOut:
                        SwRipple(p + _swOut * 0.004f, 0.55f, SwRose, 0.9f); SwRipple(p + _swOut * 0.004f, 0.35f, SwHoney, 0.6f);
                        _sparks.Burst(p + _swOut * 0.05f, _swRight, _swUp, 0.10f, 16);
                        Sfx("land", p, 0.9f, 0.7f); _swGlow = 1f;
                        break;
                    case SwarmEv.Explosion: SwRipple(p, ev.f * 2.2f, SwHoney, 0.30f); SwPuff(p, ev.f * 1.5f, new Color(1f, 0.90f, 0.62f, 1f), 0.26f); break;
                    case SwarmEv.Chain:
                        _swChains[_swChainNext] = new SwFx { t = 0f, life = 0.16f, pos = p, pos2 = Conv.U(ev.pos2) };
                        _swChainNext = (_swChainNext + 1) % _swChains.Length;
                        break;
                    case SwarmEv.OrbitHit: SwPuff(p, 0.05f, SwHoney, 0.2f); SwSfx(7, "tap", p, 0.3f, 1.9f, 0.08f); break;
                    case SwarmEv.Win:
                    case SwarmEv.Lose:
                    {
                        bool win = ev.type == SwarmEv.Win;
                        _swOverT = 0f;
                        _swNewBest = _sw.score > _swBest;
                        if (_swNewBest) { _swBest = _sw.score; PlayerPrefs.SetInt("lp.swarm.best", _swBest); PlayerPrefs.Save(); }
                        Sfx2D(win ? "win" : "lose", 0.8f);
                        if (win) { _sparks.Burst(_swCenter + _swOut * 0.2f, _swRight, _swUp, 0.12f, 18); Sfx2D("ta-da", 0.7f); }
                        if (partnerOut) { _emoteForced = win ? Emote.Happy : Emote.Thankful; _emoteT = 3.5f; }
                        Debug.Log($"[MR] swarm: the run is over - {(win ? "WON" : "lost")} after {SwarmRules.Clock(_sw.time)}, level {_sw.level}, {_sw.kills} popped, score {_sw.score} (best {_swBest}{(_swNewBest ? ", new" : "")})" + SwarmFrameLine());
                        break;
                    }
                }
            }
            _sw.eventCount = 0;
        }

        void SwarmShowCards()
        {
            var v = _swarm;
            Vector3 head = Input.HeadPos, fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : -_swOut;
            _swCardsRight = Vector3.Cross(Vector3.up, fwd);
            _swCardsAt = head + fwd * 0.40f + Vector3.down * 0.11f;
            _swCardsT = 0f;
            for (int i = 0; i < 3; i++)
            {
                int a = _sw.choices[i];
                v.cardIcons[i].SetTexture(v.augArt[a == SwarmAug.Comet ? SwarmAug.Pierce : a]);
                v.cardNames[i].text = SwarmAug.Names[a];
                v.cardLines[i].SetWrapped(SwarmAug.Lines[a], 0.088f);
                v.cardLevels[i].text = a == SwarmAug.Heal || _sw.aug[a] == 0 ? "" : "Lv " + (_sw.aug[a] + 1);
                Color c = SwarmCardColor(a);
                v.cardBadges[i].SetColors(c, Color.Lerp(c, Color.white, 0.35f));
                v.cards[i].Hide(); v.cards[i].Show(0.07f * i);
            }

            Sfx2D("open", 0.6f);
            Debug.Log($"[MR] swarm: level {_sw.level} at {_sw.time:0} s - the cards: {SwarmAug.Names[_sw.choices[0]]} | {SwarmAug.Names[_sw.choices[1]]} | {SwarmAug.Names[_sw.choices[2]]}");
        }

        static Color SwarmCardColor(int a)
        {
            switch (a % 5) { case 0: return Ui.Pal.Orange; case 1: return Ui.Pal.Cyan; case 2: return Ui.Pal.Pink; case 3: return Ui.Pal.Mint; default: return Ui.Pal.Violet; }
        }

        // ------------------------------------------------------------------ every frame
        void UpdateSwarm(float dt, bool hostOk)
        {
            var v = _swarm;
            if (!_swOn) { if (v.root.activeSelf) v.root.SetActive(false); return; }
            if (_tn.active || GlassNow.game.Length > 0 || _calibrating) { StopSwarm("something else began"); return; }
            if (_swMon != null && !_mons.ContainsKey(_swMon.desc.id)) { StopSwarm("the screen is gone"); return; }
            Vector3 head = Input.HeadPos; Quaternion headRot = Input.HeadRot;
            Vector3 eye = Cam != null ? Cam.transform.position : head; Quaternion eyeRot = Cam != null ? Cam.transform.rotation : headRot;
            Quaternion face = Quaternion.LookRotation(eyeRot * Vector3.forward, Vector3.up);
            Vector3 chest = head + Vector3.down * 0.20f;
            bool partnerOut = _ch.owner == Env.Mr && _rig.Visible;

            // ---- what the player does
            var inp = new SwarmInput { target = Conv.N(chest) };
            bool aim = false; Vector3 from = default, dir = default;
            if (_swBot != 0)
            {
                // the scripted hand of a check: it points at the monster nearest to the user (or at a given place of the glass)
                from = head + headRot * new Vector3(0.17f, -0.24f, 0.30f);
                Vector3 want = _swCenter + _swRight * (_swBotAim.x * _swHalfW) + _swUp * (_swBotAim.y * _swHalfH);
                if (!_swBotFixed)
                {
                    float bd = float.MaxValue;
                    for (int i = 0; i < SwarmSim.MaxMonsters; i++)
                    {
                        ref SwarmMonster m = ref _sw.monsters[i];
                        if (!m.alive) continue;
                        float d2 = (Conv.U(m.pos) - chest).sqrMagnitude;
                        if (d2 < bd) { bd = d2; want = Conv.U(m.pos); }
                    }
                }
                Vector3 wd = (want - from).normalized;
                _swBotDir = _swBotDir.sqrMagnitude < 0.5f ? wd : Vector3.Slerp(_swBotDir, wd, 1f - Mathf.Exp(-dt * 9f));
                dir = _swBotDir; aim = true;
            }
            else
                for (int h = 0; h < 2 && !aim; h++)
                {
                    var hs = h == 0 ? Input.Right : Input.Left;
                    if (!hs.tracked || hs.palmUp) continue;
                    if (_mode == Mode.OnHand && h == _rideHand) continue;
                    Vector3 d = hs.rayDir.sqrMagnitude > 0.5f ? hs.rayDir : hs.indexTip - hs.wrist;
                    if (d.sqrMagnitude < 1e-5f) continue;
                    d.Normalize();
                    if (Vector3.Dot(d, _swCenter - head) <= 0f) continue;                                  // (it points back at the user: not an aim)
                    from = hs.indexTip.sqrMagnitude > 1e-6f ? hs.indexTip : hs.rayOrigin; dir = d; aim = true;
                    inp.handValid = true; inp.hand = Conv.N(hs.palmPos);
                }
            if (aim)
            {
                _swAimDir = _swAimSet ? Vector3.Slerp(_swAimDir, dir, 1f - Mathf.Exp(-dt * 24f)).normalized : dir; _swAimSet = true; _swAimFrom = from;
                inp.aimValid = true; inp.aimOrigin = Conv.N(from); inp.aimDir = Conv.N(_swAimDir);
                if (!inp.handValid) { inp.handValid = true; inp.hand = Conv.N(from - _swAimDir * 0.09f); }
            }
            else _swAimSet = false;
            _swAiming = aim;
            // ---- the game moves on
            if (_sw.phase == SwarmPhase.Choosing) { _swCardsT += dt; if (_swBot == 2 && _swCardsT > 4f) SwarmChoose(0, "the scripted hand"); }
            float left = Mathf.Min(dt, 0.05f) * _swClockScale; int guard = 0;
            while (left > 1e-6f && guard++ < 24 && (_sw.phase == SwarmPhase.Intro || _sw.phase == SwarmPhase.Play))
            {
                float h = _swClockScale > 1.01f ? Mathf.Min(left, 1f / 60f) : left;
                _sw.Step(h, inp);
                left -= h;
                SwarmEvents();
                if (!_swOn) return;
            }
            if (_sw.alive >= 20) { _swBusyTime += Time.unscaledDeltaTime; _swBusyFrames++; if (Time.unscaledDeltaTime > _swBusyWorst) _swBusyWorst = Time.unscaledDeltaTime; if (_sw.alive > _swBusyMost) _swBusyMost = _sw.alive; }

            // ---- the monsters
            float time = Time.time; int inside = 0, onScreen = 0; float deskAt = _deskY + 0.0015f; bool pcDraws = SwarmPcDraws;
            if (v.gate.enabled == pcDraws) v.gate.enabled = !pcDraws;
            for (int i = 0; i < SwarmSim.MaxMonsters; i++)
            {
                ref SwarmMonster m = ref _sw.monsters[i];
                var r = v.monsterRend[i]; var sh = v.shadows[i];
                if (!m.alive || (pcDraws && m.state == SwarmState.Inside && onScreen++ < SwarmGlass.Max)) { if (r.enabled) r.enabled = false; if (sh.enabled) sh.enabled = false; continue; }
                bool flash = m.flash > 0f;
                if (_swKindShown[i] != m.kind || _swFlashShown[i] != flash) { _swKindShown[i] = m.kind; _swFlashShown[i] = flash; v.monsterMesh[i].sharedMesh = flash ? _swFlash[m.kind] : _swMesh[m.kind]; }
                if (!r.enabled) r.enabled = true;
                Transform t = v.monsters[i];
                Vector3 pos = Conv.U(m.pos); float size = m.radius * 2f;
                bool shadow = false;
                if (m.state == SwarmState.Inside)
                {
                    inside++;
                    float stride = Mathf.Sin(m.phase * 7f);
                    t.SetPositionAndRotation(pos, _swGlassRot * Quaternion.Euler(stride * 8f, m.du * 16f, stride * 9f));
                    t.localScale = new Vector3(size, size * (1f + stride * 0.045f), size);
                }
                else if (m.state == SwarmState.Emerging)
                {
                    float k = m.grow, s = size;
                    Vector3 to = pos - head;
                    t.SetPositionAndRotation(pos, Quaternion.Slerp(_swGlassRot, Quaternion.LookRotation(to.sqrMagnitude > 1e-6f ? to : -_swOut, Vector3.up), k));
                    t.localScale = new Vector3(s, s * (1f + 0.13f * Mathf.Sin(k * Mathf.PI)), s);
                }
                else
                {
                    Vector3 to = pos - head; if (to.sqrMagnitude < 1e-6f) to = -_swOut;
                    Quaternion look = Quaternion.LookRotation(to, Vector3.up);
                    float sx = 1f, sy = 1f, roll = 0f;
                    switch (m.kind)
                    {
                        case SwarmKind.Hopper: { float hop = Mathf.Abs(Mathf.Sin(m.phase * 6.5f)); sy = 0.86f + 0.26f * hop; sx = 1.10f - 0.14f * hop; break; }
                        case SwarmKind.Flyer: roll = Mathf.Sin(m.phase * 4.2f) * 22f; sy = 1f + 0.06f * Mathf.Sin(m.phase * 17f); break;
                        case SwarmKind.Tank: sy = 1f + 0.05f * Mathf.Sin(m.phase * 3f); sx = 1f - 0.04f * Mathf.Sin(m.phase * 3f); roll = Mathf.Sin(m.phase * 1.5f) * 5f; break;
                        case SwarmKind.Splitter: sx = 1f + 0.10f * Mathf.Sin(m.phase * 6f); sy = 1f - 0.07f * Mathf.Sin(m.phase * 6f); break;
                        case SwarmKind.Mini: roll = Mathf.Sin(m.phase * 9f) * 14f; break;
                        default: sy = 1f + 0.04f * Mathf.Sin(m.phase * 2.4f); sx = 1f - 0.03f * Mathf.Sin(m.phase * 2.4f); roll = Mathf.Sin(m.phase * 1.2f) * 4f; break;
                    }
                    if (flash) { sx *= 1.12f; sy *= 0.90f; }
                    t.SetPositionAndRotation(pos, look * Quaternion.Euler(0f, 0f, roll));
                    t.localScale = new Vector3(size * sx, size * sy, size * sx);
                    shadow = true;
                }
                // its shadow on the desk (only while it is near enough to the desk to throw one)
                float above = pos.y - m.radius - _deskY;
                shadow = shadow && above > -0.02f && above < 0.45f;
                if (sh.enabled != shadow) sh.enabled = shadow;
                if (shadow)
                {
                    sh.transform.position = new Vector3(pos.x, deskAt, pos.z);
                    sh.transform.localScale = Vector3.one * (size * (1.25f - Mathf.Min(0.7f, above * 1.6f)));
                }
            }

            // ---- the sheet on the glass: there while something is behind it
            _swGlow = Mathf.MoveTowards(_swGlow, 0f, dt * 2.2f);
            bool veil = (inside > 0 || _swGlow > 0.01f) && !pcDraws;
            if (v.glassVeil.enabled != veil) { v.glassVeil.enabled = veil; v.glassGlow.enabled = veil; }
            if (veil)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 7f);
                float a = Mathf.Clamp01(inside * 0.05f + 0.10f) * (0.7f + 0.3f * pulse) + _swGlow * 0.35f;
                v.glassGlow.transform.localScale = new Vector3(_swHalfW * 2.6f, _swHalfH * 2.6f, 1f) * (0.9f + 0.1f * pulse + 0.3f * _swGlow);
                v.glassGlow.SetColors(new Color(1f, 0.88f, 0.56f, a), new Color(1f, 0.97f, 0.86f, a));
            }

            // ---- bolts
            for (int i = 0; i < SwarmSim.MaxBolts; i++)
            {
                ref SwarmBolt b = ref _sw.bolts[i];
                var g = v.boltGlow[i]; var st = v.boltStreak[i];
                if (!b.alive || (pcDraws && Vector3.Dot(Conv.U(b.pos) - _swCenter, _swOut) < 0f)) { if (g.enabled) { g.enabled = false; st.enabled = false; } continue; }
                Vector3 pos = Conv.U(b.pos), bd = Conv.U(b.dir);
                if (!g.enabled) g.enabled = true;
                float size = b.radius * (b.star ? 4.6f : 5.2f);
                g.transform.SetPositionAndRotation(pos, face * Quaternion.Euler(0f, 0f, b.star ? time * 500f : 0f));
                g.transform.localScale = Vector3.one * size;
                if (g.mode != (b.star ? 5 : 8)) { g.mode = b.star ? 5 : 8; g.param = new Vector4(b.star ? 1.3f : 3.4f, 0, 0, 0); g.SetColors(b.star ? new Color(1f, 0.72f, 0.80f, 1f) : new Color(1f, 0.90f, 0.55f, 1f), new Color(1f, 0.99f, 0.92f, 1f)); }
                float len = Mathf.Min(0.10f, Vector3.Distance(pos, _swAimFrom) * 0.5f + 0.01f);
                Vector3 mid = pos - bd * (len * 0.5f), toEye = eye - mid; toEye -= bd * Vector3.Dot(toEye, bd);
                bool streak = !b.star && toEye.sqrMagnitude > 1e-6f;
                if (st.enabled != streak) st.enabled = streak;
                if (streak) { st.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(-toEye.normalized, -bd)); st.transform.localScale = new Vector3(b.radius * 1.6f, len, 1f); }
            }

            // ---- gems
            for (int i = 0; i < SwarmSim.MaxGems; i++)
            {
                ref SwarmGem g = ref _sw.gems[i];
                var s = v.gems[i];
                if (!g.alive) { if (s.enabled) s.enabled = false; continue; }
                if (!s.enabled) s.enabled = true;
                s.transform.SetPositionAndRotation(Conv.U(g.pos), face * Quaternion.Euler(0f, 0f, Mathf.Sin(time * 6f + i) * 14f));
                s.transform.localScale = Vector3.one * (0.020f + 0.004f * Mathf.Min(3, g.value)) * Mathf.Clamp01(g.t / 0.12f + 0.3f);
            }

            // ---- the stars round the hand
            for (int i = 0; i < v.orbitStars.Length; i++)
            {
                var q = v.orbitStars[i]; bool on = i < _sw.orbitCount && _sw.phase != SwarmPhase.Over;
                if (q.enabled != on) q.enabled = on;
                if (on) { q.transform.SetPositionAndRotation(Conv.U(_sw.orbit[i]), face * Quaternion.Euler(0f, 0f, time * 240f + i * 60f)); }
            }

            // ---- rings on the glass, puffs, sparks between two monsters
            for (int i = 0; i < _swRipples.Length; i++)
            {
                var q = v.ripples[i]; ref SwFx f = ref _swRipples[i];
                bool on = f.life > 0f && f.t < f.life;
                if (on) { f.t += dt; on = f.t < f.life; }
                if (q.enabled != on) q.enabled = on;
                if (!on) continue;
                float k = f.t / f.life, e = 1f - (1f - k) * (1f - k);
                q.transform.SetPositionAndRotation(f.pos, _swGlassRot);
                q.transform.localScale = Vector3.one * Mathf.Lerp(f.size * 0.25f, f.size, e);
                Color c = f.color; c.a = 1f - k;
                q.SetColors(c, new Color(1f, 0.98f, 0.9f, 1f - k));
            }
            for (int i = 0; i < _swPuffs.Length; i++)
            {
                var q = v.puffs[i]; ref SwFx f = ref _swPuffs[i];
                bool on = f.life > 0f && f.t < f.life;
                if (on) { f.t += dt; on = f.t < f.life; }
                if (q.enabled != on) q.enabled = on;
                if (!on) continue;
                float k = f.t / f.life, e = 1f - (1f - k) * (1f - k);
                q.transform.SetPositionAndRotation(f.pos, face);
                q.transform.localScale = Vector3.one * Mathf.Lerp(f.size * 0.5f, f.size * 1.5f, e);
                Color c = f.color; c.a = 0.85f * (1f - k) * (1f - k);
                q.SetColors(c, c);
            }
            for (int i = 0; i < _swChains.Length; i++)
            {
                var q = v.chains[i]; ref SwFx f = ref _swChains[i];
                bool on = f.life > 0f && f.t < f.life;
                if (on) { f.t += dt; on = f.t < f.life; }
                Vector3 d = f.pos2 - f.pos; float len = d.magnitude;
                Vector3 mid = (f.pos + f.pos2) * 0.5f, toEye = eye - mid;
                if (on && len > 1e-4f) { d /= len; toEye -= d * Vector3.Dot(toEye, d); on = toEye.sqrMagnitude > 1e-6f; } else on = false;
                if (q.enabled != on) q.enabled = on;
                if (on) { q.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(-toEye.normalized, -d)); q.transform.localScale = new Vector3(0.012f * (1f - f.t / f.life) + 0.004f, len, 1f); }
            }

            // ---- where the finger points: a thin line from the fingertip and a ring at what it will hit
            bool playing = _sw.phase == SwarmPhase.Intro || _sw.phase == SwarmPhase.Play;
            bool line = aim && playing;
            if (line)
            {
                // the same help the bolts get: the monster nearest to the finger's line, inside the cone
                int best = -1; float bs = float.MaxValue; SN.Vector3 o = inp.aimOrigin, ad = inp.aimDir;
                for (int i = 0; i < SwarmSim.MaxMonsters; i++)
                {
                    ref SwarmMonster m = ref _sw.monsters[i];
                    if (!m.alive) continue;
                    SN.Vector3 to = m.pos - o; float d = to.Length();
                    if (d < 0.02f) continue;
                    float ang = Mathf.Acos(Mathf.Clamp(SN.Vector3.Dot(to / d, ad), -1f, 1f));
                    if (ang > SwarmSim.AimCone + Mathf.Atan2(m.radius, d)) continue;
                    float s = ang + d * 0.06f;
                    if (s < bs) { bs = s; best = i; }
                }
                Vector3 end; float ring;
                if (best >= 0) { end = Conv.U(_sw.monsters[best].pos); ring = _sw.monsters[best].radius * 3.0f; }
                else
                {
                    // nothing near the line: it ends on the glass (or a little short of a metre away)
                    float denom = Vector3.Dot(_swAimDir, -_swOut), reach = 0.9f * _swFar;
                    if (denom > 0.05f) reach = Mathf.Min(reach, Vector3.Dot(_swCenter - from, -_swOut) / denom);
                    end = from + _swAimDir * Mathf.Max(0.15f, reach); ring = 0.035f;
                }
                Vector3 ld = end - from; float len = ld.magnitude;
                if (len > 0.06f)
                {
                    ld /= len;
                    Vector3 start = from + ld * 0.025f, mid = (start + end) * 0.5f, toEye = eye - mid; toEye -= ld * Vector3.Dot(toEye, ld);
                    if (toEye.sqrMagnitude > 1e-6f) { v.aimLine.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(-toEye.normalized, -ld)); v.aimLine.transform.localScale = new Vector3(0.006f, len - 0.025f, 1f); }
                    else line = false;
                    v.reticle.transform.SetPositionAndRotation(end - ld * 0.01f, face * Quaternion.Euler(0f, 0f, time * 60f));
                    v.reticle.transform.localScale = Vector3.one * (ring * (1f + 0.08f * Mathf.Sin(time * 9f)));
                    v.reticleDot.transform.SetPositionAndRotation(end - ld * 0.012f, face);
                }
                else line = false;
            }
            if (v.aimLine.enabled != line) { v.aimLine.enabled = line; v.reticle.enabled = line; v.reticleDot.enabled = line; }

            bool mist = _sw.SlowRadius > 0f && playing;
            if (v.mist.enabled != mist) v.mist.enabled = mist;
            if (mist)
            {
                v.mist.transform.SetPositionAndRotation(new Vector3(chest.x, Mathf.Max(_deskY + 0.01f, chest.y - 0.22f), chest.z), Quaternion.Euler(90f, time * 12f, 0f));
                v.mist.transform.localScale = Vector3.one * (_sw.SlowRadius * 2f / 0.9f);
            }

            // ---- a heart was lost: a ring of light goes out from the user, and a soft red ring at the edge of the view
            bool ouch = _swOuchT >= 0f;
            if (ouch) { _swOuchT += dt / 0.45f; if (_swOuchT >= 1f) { _swOuchT = -1f; ouch = false; } }
            if (v.ouch.enabled != ouch) v.ouch.enabled = ouch;
            if (ouch)
            {
                float e = 1f - (1f - _swOuchT) * (1f - _swOuchT);
                v.ouch.transform.SetPositionAndRotation(chest + Vector3.down * 0.08f, Quaternion.Euler(90f, 0f, 0f));
                v.ouch.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, SwarmSim.OuchRadius * 2.1f, e);
                v.ouch.SetColors(new Color(1f, 0.80f, 0.62f, 1f - _swOuchT), new Color(1f, 0.97f, 0.9f, 1f - _swOuchT));
            }
            bool vig = _swVig >= 0f;
            if (vig) { _swVig += dt / 0.5f; if (_swVig >= 1f) { _swVig = -1f; vig = false; } }
            if (v.vignette.enabled != vig) v.vignette.enabled = vig;
            if (vig)
            {
                // (as MrGlassOut's: a ring in front of the eyes whose outer edge lies outside the view - only its soft inner side is seen)
                const float away = 0.45f;
                float tanV = Mathf.Tan(Mathf.Clamp(Cam != null ? Cam.fieldOfView : 90f, 40f, 110f) * 0.5f * Mathf.Deg2Rad), tanH = tanV * (Cam != null ? Mathf.Clamp(Cam.aspect, 0.8f, 2f) : 1f);
                float tanEdge = Input.Simulated ? Mathf.Sqrt(tanH * tanH + tanV * tanV) * 1.03f : Mathf.Tan(50f * Mathf.Deg2Rad);
                v.vignette.transform.SetPositionAndRotation(eye + eyeRot * Vector3.forward * away, eyeRot);
                v.vignette.transform.localScale = Vector3.one * (2f * away * tanEdge);
                float env = Mathf.Clamp01(_swVig / 0.10f) * Mathf.Pow(1f - _swVig, 1.5f);
                var red = new Color(0.93f, 0.36f, 0.36f, 0.90f * env);
                v.vignette.SetColors(red, red);
            }

            SwarmHud(dt, time);
            SwarmCards(dt);
            SwarmResult(dt);
            if (hostOk) SwarmSendState(false);
        }

        /// <summary>The sign over the glass: hearts, level, how far to the next one, the clock; the banner; the boss's bar.</summary>
        void SwarmHud(float dt, float time)
        {
            var v = _swarm;
            if (_swHearts != _sw.hearts * 100 + _sw.maxHearts)
            {
                _swHearts = _sw.hearts * 100 + _sw.maxHearts;
                for (int i = 0; i < v.hearts.Length; i++)
                {
                    bool has = i < _sw.maxHearts;
                    if (v.hearts[i].enabled != has) v.hearts[i].enabled = has;
                    if (has) v.hearts[i].SetColor(i < _sw.hearts ? Color.white : new Color(0.55f, 0.47f, 0.45f, 0.35f));
                }
            }
            if (_swLevel != _sw.level) { _swLevel = _sw.level; v.level.text = "Lv " + _sw.level; }
            int sec = (int)Mathf.Max(0f, _sw.time);
            if (_swClock != sec) { _swClock = sec; v.clock.text = SwarmRules.Clock(sec); }
            int xp = _sw.xp * 1000 / Mathf.Max(1, _sw.XpNeed);
            if (_swXp != xp)
            {
                _swXp = xp;
                const float W = 0.094f, H = 0.007f;
                float w = Mathf.Max(H, W * Mathf.Clamp01(xp / 1000f));
                Ui.SizePanel(v.bar, w, H, H * 0.5f);
                v.bar.transform.localPosition = new Vector3(v.barBg.transform.localPosition.x - W * 0.5f + w * 0.5f, v.barBg.transform.localPosition.y, -0.002f);
            }
            // the boss: its name and how much of it is left, under the sign
            bool boss = _sw.bossOut && !_sw.bossDead && _sw.bossIndex >= 0 && _sw.phase != SwarmPhase.Over;
            if (v.bossBarBg.enabled != boss) { v.bossBarBg.enabled = boss; v.bossBar.enabled = boss; v.bossName.text = boss ? SwarmKind.Names[SwarmKind.Boss] : ""; }
            if (boss)
            {
                ref SwarmMonster b = ref _sw.monsters[_sw.bossIndex];
                int hp = (int)(Mathf.Clamp01(b.hp / Mathf.Max(1f, b.maxHp)) * 200f);
                if (_swBossHp != hp)
                {
                    _swBossHp = hp;
                    const float W = 0.22f, H = 0.010f;
                    float w = Mathf.Max(H, W * hp / 200f);
                    Ui.SizePanel(v.bossBar, w, H, H * 0.5f);
                    v.bossBar.transform.localPosition = new Vector3(-W * 0.5f + w * 0.5f, v.bossBarBg.transform.localPosition.y, -0.002f);
                }
            }
            // the banner: a line that pops up in front of the glass for a moment
            bool ban = _swBannerT >= 0f;
            if (ban) { _swBannerT += dt; if (_swBannerT >= _swBannerLife) { _swBannerT = -1f; ban = false; v.banner.text = ""; } }
            if (v.bannerBg.enabled != ban) v.bannerBg.enabled = ban;
            if (ban)
            {
                float pop = Ui.Pop(Mathf.Clamp01(_swBannerT / 0.25f)) * Mathf.Clamp01((_swBannerLife - _swBannerT) / 0.2f);
                Vector3 at = _swCenter + _swUp * (_swHalfH * 0.45f) + _swOut * 0.07f;
                v.banner.transform.SetPositionAndRotation(at, _swGlassRot);
                v.banner.transform.localScale = Vector3.one * Mathf.Max(0.001f, pop) * _swFar;
                v.bannerBg.transform.SetPositionAndRotation(at - _swOut * 0.002f, _swGlassRot);
                Ui.SizePanel(v.bannerBg, (v.banner.Width + 0.034f) * pop * _swFar, (v.banner.Height + 0.020f) * pop * _swFar, 0.014f * _swFar, 0.0016f * _swFar);
            }
        }

        /// <summary>The three augment cards: within reach in front of the user, poked or pointed at and pinched.</summary>
        void SwarmCards(float dt)
        {
            var v = _swarm;
            if (_sw.phase != SwarmPhase.Choosing) { for (int i = 0; i < v.cards.Length; i++) if (v.cards[i].Shown) v.cards[i].Hide(); return; }
            for (int i = 0; i < v.cards.Length; i++)
            {
                var c = v.cards[i];
                if (!c.Shown) continue;
                Vector3 at = _swCardsAt + _swCardsRight * ((i - 1) * 0.122f) + Vector3.up * (Mathf.Sin(Time.time * 1.6f + i * 1.3f) * 0.003f);
                FaceUser(c.transform, at);
                UpdateButtons(c, dt);
            }
        }

        /// <summary>The card at the end: what the run came to, Play again, Close.</summary>
        void SwarmResult(float dt)
        {
            var v = _swarm;
            if (_sw.phase != SwarmPhase.Over) { if (v.result.Shown) v.result.Hide(); return; }
            if (_swOverT >= 0f) _swOverT += dt;
            if (!_swResultUp && _swOverT > 1.1f)
            {
                _swResultUp = true;
                v.resultTitle.text = _sw.won ? "The gate is sealed!" : "The gate broke through!";
                v.resultLine.text = "Time " + SwarmRules.Clock(_sw.time) + "     Level " + _sw.level + "     Popped " + _sw.kills;
                v.resultScore.text = "Score " + _sw.score + (_swNewBest ? "     New best!" : "     Best " + _swBest);
                v.result.Show();
                Sfx2D("open", 0.5f);
            }
            if (!v.result.Shown) return;
            Vector3 head = Input.HeadPos, fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : -_swOut;
            if (v.result.appear <= 0f) _swCardsAt = head + fwd * 0.45f + Vector3.down * 0.07f;      // (it stays where it came up)
            FaceUser(v.result.transform, _swCardsAt);
            UpdateButtons(v.result, dt);
        }

        // ------------------------------------------------------------------ the PC shows them behind its own glass
        double _swNextSend; readonly SwarmGlass _swState = new SwarmGlass(); bool _swSentOn; int _swBursts; float _swPcSeenAt = -100f;
        /// <summary>The PC client is drawing what is behind the glass (it said so within the last two seconds): the headset leaves those to it.</summary>
        bool SwarmPcDraws => _swMon != null && Time.time - _swPcSeenAt < 2f;

        /// <summary>The PC client's answer (Msg.SwarmState with count -1): it is drawing them inside the monitor.</summary>
        void OnSwarmAck(SwarmGlass g)
        {
            if (g == null || g.count >= 0) return;
            if (Time.time - _swPcSeenAt > 5f) Debug.Log("[MR] swarm: the PC client draws what is behind the glass - the headset draws only what has come out");
            _swPcSeenAt = Time.time;
        }


        /// <summary>Ten times a second, to the PC through the host: what is behind the glass of which screen (Windows/PcSwarm.cs draws it).</summary>
        void SwarmProject(Vector3 point, out float u, out float v, out float scale)
        {
            Vector3 eye = Input.HeadPos - _swCenter, local = point - _swCenter;
            float front = Mathf.Max(0.04f, Vector3.Dot(eye, _swOut));
            scale = front / (front + Mathf.Max(0f, -Vector3.Dot(local, _swOut)));
            Vector3 surface = eye + (local - eye) * scale;
            u = Vector3.Dot(surface, _swRight) / _swHalfW;
            v = Vector3.Dot(surface, _swUp) / _swHalfH;
        }

        void SwarmSendState(bool ended)
        {
            if (_link == null || !_link.Connected || LocalMode) return;
            double now = Time.realtimeSinceStartupAsDouble;
            bool on = _swOn && _swMon != null && !ended;
            if (!on) { if (_swSentOn) { _swSentOn = false; _swState.on = false; _swState.count = 0; _link.Send(Msg.SwarmState, _swState); } return; }
            if (now < _swNextSend) return;
            _swNextSend = now + 1.0 / 25.0;
            _swSentOn = true;
            var s = _swState;
            s.on = true; s.monitorId = _swMon.desc.id; s.phase = _sw.phase; s.count = 0; s.bursts = _swBursts; s.halfW = _swHalfW; s.halfH = _swHalfH;
            s.level = _sw.level; s.hearts = _sw.hearts; s.maxHearts = _sw.maxHearts;
            s.score = _sw.score; s.xp = _sw.xp; s.need = _sw.XpNeed; s.elapsed = _sw.time; s.won = _sw.won;
            for (int i = 0; i < s.choices.Length; i++) s.choices[i] = _sw.choices[i];
            for (int i = 0; i < SwarmSim.MaxMonsters && s.count < SwarmGlass.Max; i++)
            {
                ref SwarmMonster m = ref _sw.monsters[i];
                if (!m.alive || m.state != SwarmState.Inside) continue;
                int k = s.count++;
                s.id[k] = m.id; s.kind[k] = m.kind;
                SwarmProject(Conv.U(m.pos), out s.u[k], out s.v[k], out s.press[k]);
            }
            s.shotCount = 0;
            foreach (var bolt in _sw.bolts)
            {
                if (!bolt.alive) continue;
                Vector3 pos = Conv.U(bolt.pos);
                if (Vector3.Dot(pos - _swCenter, _swOut) >= 0f) continue;
                int k = s.shotCount++;
                SwarmProject(pos, out s.shotU[k], out s.shotV[k], out float scale);
                s.shotRadius[k] = bolt.radius * scale;
            }
            _link.Send(Msg.SwarmState, s);
        }
    }
}
