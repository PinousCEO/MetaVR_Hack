using System;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using SN = System.Numerics;

namespace LivingPartner.MR
{
    /// <summary>
    /// The room half of the tennis game. The partner plays inside the monitor, whose glass is the net; when its ball
    /// comes out through the glass it is drawn here, in the room, and the user plays it back with an open hand - the
    /// palm is the racket. The ball is not streamed: each stroke is, and this side computes the same path as the PC on
    /// the shared clock and draws the part of it that is in front of the glass. The user's own stroke is shown at once
    /// (the same function the host uses) and then confirmed by the host.
    /// The ball, its shadow, the rings on the hands and the score are objects under MrCore/Tennis.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Tennis (MrCore/Tennis)")]
        [SerializeField] GameObject _tnRoot;
        [SerializeField] BallView _tnBall;
        [SerializeField] FxQuad _tnBallShadow;
        [Tooltip("The racket shown on each tracked palm: 0 right hand, 1 left hand")]
        [SerializeField] FxQuad[] _tnPaddles;
        [SerializeField] FxQuad _tnScoreBg;
        [SerializeField] StoryLabel _tnScore;
        [Tooltip("A court on the desk (the partner is out in the room): its frame - z = 0 is the net - a mat, and the net.")]
        [SerializeField] MonView _tnRoomView;
        [SerializeField] FxQuad _tnMat, _tnNet;
        [Tooltip("The ring that spreads on the glass at the moment the ball goes through it.")]
        [SerializeField] FxQuad _tnRipple;

        TennisState _tn = new TennisState();
        string _tnMode = "";                 // the ball game asked for: "" tennis, "stars", "keeper"
        TennisShot _tnLocal;                 // the user's stroke as this side worked it out, until the host's copy arrives
        int _tnStruckSeq = -1;               // the partner's shot the hand has already met
        float _tnPendingAt = -1f, _tnSettle = -1f, _tnNextStart; int _tnStartTries;
        readonly SN.Vector3[] _tnPalm = new SN.Vector3[2], _tnPalmVel = new SN.Vector3[2]; readonly bool[] _tnPalmOk = new bool[2];
        double _tnPrevT; float _tnSpin, _tnHitFlash;
        int _tnPlanSeq = -1, _tnBounceSeq = -1, _tnBounces; bool _tnPlanOk, _tnHadBall; Vector3 _tnPlanPos, _tnBallAt; double _tnPlanAt;
        Color _tnShadowC; bool _tnColours;
        bool _tnRoomOk; float _tnStep; string _tnPhaseWas = "";
        int _tnCrossSeq = -1; bool _tnCrossOk; double _tnCrossT, _tnPrevDrawT; float _tnRippleT = -1f;
        const float RoomW = 0.40f, RoomH = 0.50f, RoomD = 0.30f;

        public bool TennisActive => _tn.active;

        void OnTennisState(TennisState g)
        {
            if (g == null) { if (_tn.active) { _tn = new TennisState { revision = _tn.revision }; _tnLocal = null; Debug.Log("[MR] tennis: the game ended"); } return; }
            if (g.revision < _tn.revision) return;
            if (g.active != _tn.active) Debug.Log(g.active ? $"[MR] tennis: a game begins on {g.court.monitorId}" : "[MR] tennis: the game ended");
            if (g.active && !_tn.active) { _tnStruckSeq = -1; _tnLocal = null; _tnPendingAt = -1f; }
            if (!g.active && _tn.active && _tn.court.room) _motor.MoveTo(new Vector2(_home.x, _home.z), false);      // the game on the desk is over: it strolls back to its spot
            _tn = g;
            // the host has the stroke now (or the point is over): its word replaces the local one
            if (_tnLocal != null && (!g.active || g.shot == null || g.shot.seq >= _tnLocal.seq)) _tnLocal = null;
        }

        bool TransitionIdle => TransitionMachine.Parse(_tr.state) is TState st && (st == TState.Idle || st == TState.Complete || st == TState.Cancelled);

        /// <summary>The Tennis button of the hand menu: starts a game, or ends the one being played.</summary>
        void ToggleTennis(string how)
        {
            if (_tn.active || _tnPendingAt >= 0f)
            {
                Debug.Log("[MR] tennis: stop asked by " + how);
                _tnPendingAt = -1f;
                if (_tn.active) _link.Send(Msg.GameCommand, new GameCommand { cmd = "stop" });
                return;
            }
            StartTennis(how);
        }

        /// <summary>
        /// Asks for a game. Where the partner is, there it is played: out in the room on a small court on the desk, or -
        /// when it is inside a screen - through that screen's glass. It does not go anywhere for a game.
        /// </summary>
        void StartTennis(string how, string mode = "")
        {
            if (_tn.active || _tnPendingAt >= 0f) return;
            _tnMode = mode ?? "";
            if (!TransitionIdle) { Caption("One moment, I'm on my way.", 3f); return; }
            Debug.Log("[MR] tennis: start asked by " + how + ", the partner is in " + _ch.owner);
            _tnSettle = -1f; _tnStartTries = 0; _tnNextStart = 0f;
            if (_ch.owner == Env.Pc)
            {
                if (_mons.Count == 0) { Caption("I don't know where your screens are yet. Say \"find my screens\".", 4f); return; }
                _tnPendingAt = Time.time; return;
            }
            if (_mode == Mode.OnHand || _mode == Mode.Hop || _mode == Mode.Held) { Caption("Put me down on the desk first!", 3f); Sfx2D("error", 0.5f); return; }
            if (_tnMode == "keeper" && !LocalMode && _snap?.pc != null && NearestMon() is MonView goal)
            {
                if (GazeTarget(out string target, out _, out _) && Mon(target) is MonView lookedAt) goal = lookedAt;
                Select(goal.desc.id, 0.5f, 0.58f, 0, true, false, "");
                _tnPendingAt = Time.time;
                return;
            }
            LayRoomCourt();
            _link.Send(Msg.GameCommand, new GameCommand
            {
                cmd = "start", mode = _tnMode, room = true, width = RoomW, height = RoomH, depth = RoomD,
                eye = _tnRoomView.frame.ToLocal(Conv.N(Input.HeadPos)),
            });
            Debug.Log("[MR] tennis: start sent for a court on the desk");
        }

        /// <summary>Lays the court on the desk in front of the user: the net 36 cm ahead, the partner's half beyond it.</summary>
        void LayRoomCourt()
        {
            Vector3 head = Input.HeadPos, fwd = Input.HeadRot * Vector3.forward; fwd.y = 0;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 toP = _pos - head; toP.y = 0;
            if (toP.sqrMagnitude > 0.04f && Vector3.Dot(toP.normalized, fwd) > 0.5f) fwd = toP.normalized;      // towards where the partner is standing
            Vector3 right = Vector3.Cross(Vector3.up, fwd), up = Vector3.up;
            Vector3 o = new Vector3(head.x, _deskY, head.z) + fwd * 0.36f + up * (RoomH * 0.5f);
            Vector3 hx = right * (RoomW * 0.5f), hy = up * (RoomH * 0.5f);
            _tnRoomView.frame = MonitorFrame.FromCorners(Conv.N(o - hx + hy), Conv.N(o + hx + hy), Conv.N(o - hx - hy), Conv.N(o + hx - hy));
            _tnRoomView.transform.SetPositionAndRotation(o, Quaternion.LookRotation(fwd, up));
            _tnRoomOk = true;
        }

        /// <summary>The partner's side of a game on the desk: it runs along its baseline and swings (the host says where and when).</summary>
        void TennisRoomPose(float dt)
        {
            var mv = _tnRoomView; var c = _tn.court;
            double hostNow = _link.HostNow, drawT = hostNow + DisplayLatencyMs / 1000.0;
            Vector3 stand = Conv.U(mv.frame.ToWorld(new SN.Vector3(_tn.partnerX, c.FloorBack, _tn.partnerZ)));
            Vector3 to = stand - _pos; to.y = 0; float dist = to.magnitude;
            float speed = 0.5f;
            if (_tn.partnerHitAt > hostNow) speed = Mathf.Clamp(dist / Mathf.Max(0.05f, (float)(_tn.partnerHitAt - hostNow) - 0.10f), 0.3f, 1.5f);      // be there when the ball is
            float stepLen = Mathf.Min(dist, speed * dt);
            if (dist > 1e-5f) _pos += to / dist * stepLen;
            _pos.y = _deskY;
            _rot = Quaternion.LookRotation(-mv.Z, Vector3.up);                           // it faces the user across the net
            float us = _tn.partnerHitAt > 0 ? (float)((drawT - _tn.partnerHitAt) / 0.5) + PoseLib.SwingContact : -1f;
            float since = (float)(hostNow - _tn.phaseAt), time = Time.time;
            if (_tn.phase == "over") { if (_tn.scorePartner > _tn.scoreUser) PoseLib.Happy(_pose, time); else PoseLib.Wave(_pose, time); }
            else if (_tn.phase == "point" && _tn.lastPoint == "partner") PoseLib.Happy(_pose, time);
            else if (_tn.phase == "point" && since < 0.7f && !(us >= 0f && us <= 1f)) PoseLib.Startle(_pose, time);
            else if (us >= 0f && us <= 1f && _tn.mode == "keeper") PoseLib.FootballKick(_pose, us, time);
            else if (us >= 0f && us <= 1f) PoseLib.TennisSwing(_pose, us, time);
            else
            {
                PoseLib.TennisReady(_pose, time);
                if (dist > 0.004f) { _tnStep += stepLen / 0.034f; PoseLib.StepInPlace(_pose, _tnStep, 0.85f); }
            }
            _motor.Teleport(new Vector2(_pos.x, _pos.z), _rot.eulerAngles.y);
        }

        void SendTennisStart(MonView mv)
        {
            // what only this side knows about the room: which way is up for that screen, where the desk is, where the user is
            _link.Send(Msg.GameCommand, new GameCommand
            {
                cmd = "start", mode = _tnMode, monitorId = mv.desc.id, up = mv.frame.DirToLocal(SN.Vector3.UnitY),
                deskHeight = _deskY - mv.Origin.y, eye = mv.frame.ToLocal(Conv.N(Input.HeadPos)),
            });
            Debug.Log($"[MR] tennis: start sent for {mv.desc.id} desk {(_deskY - mv.Origin.y) * 1000:0} mm below the screen centre");
        }

        bool OnDebugTennis(DebugCommand d)
        {
            if (d.cmd == "tennisStart") { StartTennis("the PC"); return true; }
            if (d.cmd == "tennisStop") { if (_tn.active || _tnPendingAt >= 0f) ToggleTennis("the PC"); return true; }
            return false;
        }

        /// <summary>
        /// For the simulator's scripted hand: where and when the partner's ball can be met in front of the user.
        /// False when no ball of the partner's is on its way.
        /// </summary>
        public bool TennisIncoming(out Vector3 hitWorld, out double hitAt, out Vector3 towardScreen, out int seq)
        {
            hitWorld = default; hitAt = 0; towardScreen = Vector3.forward; seq = 0;
            var mv = _tn.active ? Mon(_tn.court.monitorId) : null;
            var shot = _tn.shot;
            if (mv == null) return false;
            towardScreen = mv.Z;
            if (_tn.phase != "rally" || shot == null || shot.by != "partner" || _tnLocal != null || shot.seq == _tnStruckSeq) return false;
            seq = shot.seq;
            if (_tnPlanSeq != shot.seq)
            {
                _tnPlanSeq = shot.seq; _tnPlanOk = false;
                var eye = mv.frame.ToLocal(Conv.N(Input.HeadPos));
                float zHit = Mathf.Clamp(eye.Z * 0.62f + 0.07f, -0.43f, -0.23f);
                for (double t = shot.t0; t < shot.t0 + 4.0; t += 0.01)
                {
                    var s = Tennis.Evaluate(_tn.court, shot, t);
                    if (s.dead || s.bouncesFront > 0) break;
                    if (s.crossed && s.pos.Z <= zHit) { _tnPlanOk = true; _tnPlanAt = t; _tnPlanPos = Conv.U(mv.frame.ToWorld(s.pos)); break; }
                }
            }
            hitWorld = _tnPlanPos; hitAt = _tnPlanAt;
            return _tnPlanOk;
        }

        void UpdateTennis(float dt, bool hostOk)
        {
            // ---- a game that was asked for starts once the partner stands inside the screen
            if (_tnPendingAt >= 0f)
            {
                if (_tn.active || !hostOk) _tnPendingAt = -1f;
                else if (Time.time - _tnPendingAt > 40f || _tnStartTries >= 3) { _tnPendingAt = -1f; Caption("I couldn't get ready to play.", 4f); }
                else if (TransitionIdle && _ch.owner == Env.Mr && _sel == null && Time.time - _tnPendingAt > 4f) _tnPendingAt = -1f;      // it never went in
                else if (TransitionIdle && _ch.owner == Env.Pc && _snap.pc != null && _snap.pc.overlayVisible && (_snap.pc.mode == "DesktopRoaming" || _snap.pc.mode == "WorkspaceBound") && Mon(_snap.pc.monitorId) is MonView there)
                {
                    if (_tnSettle < 0f) _tnSettle = Time.time + 0.7f;                // let it land and look round first
                    else if (Time.time >= _tnSettle && Time.time >= _tnNextStart) { _tnNextStart = Time.time + 2.5f; _tnStartTries++; SendTennisStart(there); }
                }
                else _tnSettle = -1f;
            }

            bool room = _tn.active && _tn.court.room;
            var mv = hostOk && _tn.active ? (room ? (_tnRoomOk ? _tnRoomView : null) : Mon(_tn.court.monitorId)) : null;
            bool on = mv != null;
            if (_tnRoot.activeSelf != on) _tnRoot.SetActive(on);
            if (!on) { _tnPalmOk[0] = _tnPalmOk[1] = false; _tnHadBall = false; _tnRippleT = -1f; return; }
            if (!_tnColours) { _tnColours = true; _tnShadowC = _tnBallShadow.color; }
            var c = _tn.court;
            double hostNow = _link.HostNow, drawT = hostNow + DisplayLatencyMs / 1000.0;
            if (_tnPrevT <= 0 || hostNow - _tnPrevT > 0.25) _tnPrevT = hostNow - dt;
            _tnHitFlash = Mathf.Max(0f, _tnHitFlash - dt * 3f);
            // a point is over, or the game: heard where the court is
            if (_tn.phase != _tnPhaseWas)
            {
                if (_tn.phase == "point") Sfx(_tn.lastPoint == "partner" ? "lose" : "win", mv.Origin, 0.4f);
                else if (_tn.phase == "over") Sfx(_tn.scoreUser > _tn.scorePartner ? "win" : "lose", mv.Origin, 0.8f);
                _tnPhaseWas = _tn.phase;
            }

            // ---- the hands, in the frame of the screen: where each palm is, how it moves, which way it faces
            var prevPalm = new SN.Vector3[2]; var normal = new SN.Vector3[2]; bool[] had = { _tnPalmOk[0], _tnPalmOk[1] };
            for (int h = 0; h < 2; h++)
            {
                var hs = h == 0 ? Input.Right : Input.Left;
                prevPalm[h] = _tnPalm[h];
                if (!hs.tracked) { _tnPalmOk[h] = false; _tnPalmVel[h] = default; if (_tnPaddles[h].enabled) _tnPaddles[h].enabled = false; continue; }
                var p = mv.frame.ToLocal(Conv.N(hs.palmPos));
                normal[h] = mv.frame.DirToLocal(Conv.N(hs.palmNormal));
                if (_tnPalmOk[h] && dt > 1e-4f) _tnPalmVel[h] = SN.Vector3.Lerp(_tnPalmVel[h], (p - _tnPalm[h]) / dt, 0.5f); else { _tnPalmVel[h] = default; prevPalm[h] = p; }
                _tnPalm[h] = p; _tnPalmOk[h] = true;
                // the racket the hand is: a ring round the palm
                var ring = _tnPaddles[h];
                if (!ring.enabled) ring.enabled = true;
                Vector3 fwd = hs.palmForward.sqrMagnitude > 1e-4f ? hs.palmForward : Vector3.up;
                ring.transform.SetPositionAndRotation(hs.palmPos + hs.palmNormal * 0.012f, Quaternion.LookRotation(hs.palmNormal, fwd));
                ring.transform.localScale = Vector3.one * Tennis.PaddleRadius * 2f * (1f + 0.25f * _tnHitFlash);
                ring.SetPhase(Time.time * 0.15f);
            }

            // ---- the ball
            var shot = _tnLocal ?? _tn.shot;
            bool keeper = _tn.mode == "keeper";
            bool inPlay = shot != null && _tn.phase == "rally" && !(keeper && shot.seq == _tnStruckSeq);
            Vector3 right = Input.HeadRot * Vector3.right; right.y = 0; right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            if (inPlay && shot.by == "partner" && shot.seq != _tnStruckSeq)
            {
                // did a palm meet it since the last frame? (checked at several moments in between, so a fast swing cannot pass through it)
                const int steps = 4;
                for (int k = 1; k <= steps && _tnLocal == null && shot.seq != _tnStruckSeq; k++)
                {
                    float f = k / (float)steps;
                    double tk = _tnPrevT + (hostNow - _tnPrevT) * f;
                    var sk = Tennis.Evaluate(c, shot, tk);
                    if (sk.dead || !sk.crossed || !sk.Front) continue;
                    for (int h = 0; h < 2; h++)
                    {
                        if (!_tnPalmOk[h] || !had[h]) continue;
                        var palm = SN.Vector3.Lerp(prevPalm[h], _tnPalm[h], f);
                        if (!Tennis.PaddleHits(sk.pos, palm, normal[h], Tennis.BallRadius(_tn.mode))) continue;
                        _tnStruckSeq = shot.seq;
                        if (!keeper && _tn.mode != "stars") _tnLocal = Tennis.UserShot(c, shot.seq + 1, sk.pos, _tnPalmVel[h], normal[h], tk);      // stars: a hand on the ball is a catch, nothing flies back
                        _link.Send(Msg.GameCommand, new GameCommand { cmd = "hit", seq = shot.seq, pos = sk.pos, swing = _tnPalmVel[h], normal = normal[h], t = tk, eye = mv.frame.ToLocal(Conv.N(Input.HeadPos)) });
                        _sparks.Burst(Conv.U(mv.frame.ToWorld(sk.pos)), right, Vector3.up, _tn.mode == "stars" ? 0.05f : 0.035f, _tn.mode == "stars" ? 12 : 7);
                        _tnHitFlash = 1f;
                        Sfx("hit", Conv.U(mv.frame.ToWorld(sk.pos)), 0.8f);
                        Debug.Log($"[MR] tennis: {(h == 0 ? "right" : "left")} hand met ball #{shot.seq} at ({sk.pos.X:0.000},{sk.pos.Y:0.000},{sk.pos.Z:0.000}) swing {_tnPalmVel[h].Length():0.00} m/s");
                        if (_tnLocal != null) shot = _tnLocal; else inPlay = false;
                        break;
                    }
                }
            }
            _tnPrevT = hostNow;

            // ---- the moment the ball goes through the glass: a ring spreads on the glass exactly there, exactly then.
            //      (Worked out from the stroke itself - when its path meets the glass - not noticed a frame or two late.)
            if (shot != null && shot.seq != _tnCrossSeq)
            {
                _tnCrossSeq = shot.seq;
                if (shot.by == "partner") Sfx("hit", room ? _pos + Vector3.up * (_H * 0.6f) : mv.Origin, 0.45f);      // the partner's stroke, from where it plays
                _tnCrossOk = !room && Tennis.PassesGlass(c, shot, out _tnCrossT);
                if (_tnCrossOk)
                {
                    double lo = _tnCrossT - 0.012, hi = _tnCrossT; bool startFront = shot.p0.z < 0;
                    for (int it = 0; it < 7; it++) { double mid = (lo + hi) * 0.5; var bm = Tennis.Evaluate(c, shot, mid); if (bm.crossed && bm.Front != startFront) hi = mid; else lo = mid; }
                    _tnCrossT = hi;
                }
            }
            if (inPlay && _tnCrossOk && shot.seq == _tnCrossSeq && _tnPrevDrawT < _tnCrossT && drawT >= _tnCrossT)
            {
                var at = Tennis.Evaluate(c, shot, _tnCrossT).pos; at.Z = 0;
                Vector3 onGlass = Conv.U(mv.frame.ToWorld(at)) - mv.Z * 0.003f;
                _tnRippleT = 0f;
                _tnRipple.transform.SetPositionAndRotation(onGlass, Quaternion.LookRotation(mv.Z, mv.Y));
                _sparks.Burst(onGlass, mv.X, mv.Y, 0.05f, 11);
                Sfx("pop", onGlass, 0.4f);
            }
            _tnPrevDrawT = drawT;
            if (_tnRippleT >= 0f)
            {
                _tnRippleT += dt / 0.5f;
                bool alive = _tnRippleT < 1f;
                if (_tnRipple.enabled != alive) _tnRipple.enabled = alive;
                if (alive)
                {
                    float e = 1f - (1f - _tnRippleT) * (1f - _tnRippleT);
                    _tnRipple.transform.localScale = Vector3.one * Mathf.Lerp(0.03f, 0.20f, e);
                    _tnRipple.SetColors(new Color(1f, 0.90f, 0.55f, 1f - _tnRippleT), new Color(1f, 0.98f, 0.9f, 1f - _tnRippleT));
                }
                else _tnRippleT = -1f;
            }
            else if (_tnRipple.enabled) _tnRipple.enabled = false;

            // ---- a court on the desk: its mat and its net
            if (_tnMat.enabled != room) _tnMat.enabled = room;
            if (_tnNet.enabled != room) _tnNet.enabled = room;
            if (room)
            {
                Vector3 net = mv.Origin - Vector3.up * (c.height * 0.5f);
                _tnMat.transform.SetPositionAndRotation(net + mv.Z * (c.depth * 0.5f) + Vector3.up * 0.0015f, Quaternion.LookRotation(Vector3.down, mv.Z));
                Ui.SizePanel(_tnMat, c.width, c.depth, 0.02f, 0.004f);
                _tnNet.transform.SetPositionAndRotation(net + Vector3.up * (Tennis.NetHeight * 0.5f), Quaternion.LookRotation(mv.Z, Vector3.up));
                Ui.SizePanel(_tnNet, c.width + 0.02f, Tennis.NetHeight, 0.006f, 0.003f);
            }

            bool show = false; Vector3 world = default;
            if (inPlay)
            {
                var s = Tennis.Evaluate(c, shot, drawT);
                world = Conv.U(mv.frame.ToWorld(s.pos));
                show = room || s.pos.Z < Tennis.BallRadius(_tn.mode);
                if (show)
                {
                    Vector3 vel = Conv.U(mv.frame.DirToWorld(s.vel));
                    _tnSpin += vel.magnitude / Tennis.BallRadius(_tn.mode) * Mathf.Rad2Deg * dt * 0.35f;
                    Vector3 axis = Vector3.Cross(Vector3.up, vel); axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
                    _tnBall.Football(_tn.mode == "keeper");
                    _tnBall.Place(world, Quaternion.AngleAxis(_tnSpin, axis), Tennis.BallRadius(_tn.mode) * 2f);
                    _tnBall.SetClip(mv.Origin, mv.Z, room ? 0f : -1f);          // through a screen this side draws what is in front of the glass; on the desk, all of it
                    mv.GAxes(out var gx, out var gy, out var gz);
                    _tnBall.SetLightDir(gx * -0.35f + gy * 0.8f + gz * -0.5f);
                    if (_tnBounceSeq != shot.seq) { _tnBounceSeq = shot.seq; _tnBounces = s.bouncesFront + s.bouncesBack; }
                    if (s.bouncesFront + s.bouncesBack > _tnBounces) { _tnBounces = s.bouncesFront + s.bouncesBack; _sparks.Burst(new Vector3(world.x, _deskY + 0.003f, world.z), right, Vector3.up, 0.024f, 4); Sfx("bounce", world, 0.6f); }
                }
            }
            if (_tnHadBall && !inPlay && Vector3.Dot(_tnBallAt - mv.Origin, mv.Z) < -0.01f) _sparks.Burst(_tnBallAt, right, Vector3.up, 0.03f, 6);     // the point is over: the ball is gone
            _tnHadBall = inPlay; if (inPlay) _tnBallAt = world;
            _tnBall.Visible = show;
            float above = world.y - _deskY;
            bool shadow = show && above > -0.005f && (room || Vector3.Dot(world - mv.Origin, mv.Z) < -0.006f);
            if (_tnBallShadow.enabled != shadow) _tnBallShadow.enabled = shadow;
            if (shadow)
            {
                _tnBallShadow.transform.SetPositionAndRotation(new Vector3(world.x, _deskY + 0.0012f, world.z), Quaternion.Euler(90, 0, 0));
                _tnBallShadow.transform.localScale = Vector3.one * Tennis.BallRadius(_tn.mode) * 2f * (1.5f + Mathf.Max(0f, above) * 3.5f);
                var sc = _tnShadowC; sc.a /= 1f + Mathf.Max(0f, above) * 6f; _tnBallShadow.SetColor(sc);
            }

            // ---- the score, above the screen
            string line = Tennis.ScoreLine(_tn);
            Place(_tnScore, room ? mv.Origin + Vector3.up * 0.03f + mv.Z * (c.depth + 0.04f) : mv.Origin + mv.Y * (mv.frame.heightM * 0.5f + 0.125f) - mv.Z * 0.01f, line);
            _tnScoreBg.transform.SetPositionAndRotation(_tnScore.transform.position + _tnScore.transform.forward * 0.002f, _tnScore.transform.rotation);
            float bw = Mathf.Clamp(_tnScore.Width + 0.034f, 0.12f, 0.40f), bh = _tnScore.Height + 0.020f;
            Ui.SizePanel(_tnScoreBg, bw, bh, Mathf.Min(bh * 0.4f, 0.016f), 0.0014f);
        }
    }
}
