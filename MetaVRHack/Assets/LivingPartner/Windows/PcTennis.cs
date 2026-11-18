using System;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Pose = LivingPartner.Shared.Pose;

namespace LivingPartner.Windows
{
    /// <summary>
    /// The PC half of the tennis game. While a game runs the whole monitor becomes a window into a small court: a box
    /// behind the glass, as wide and as high as the screen, drawn from where the user's head really is (the headset
    /// sends it), so it has real depth. The partner plays on its floor; the glass is the net. The ball is never sent
    /// here frame by frame - each stroke is, and this side computes the same path as the headset on the shared clock
    /// and draws the part of it that is behind the glass.
    /// The court, the ball, the racket and the scoreboard are objects of Scenes/PcCharacter.unity (PcCharacter/Tennis).
    /// </summary>
    public sealed partial class PcApp
    {
        [Header("Tennis court (PcCharacter/Tennis)")]
        [SerializeField] GameObject _tnRoot;
        [Tooltip("0 floor, 1 ceiling, 2 back wall, 3 left wall, 4 right wall")]
        [SerializeField] FxQuad[] _tnWalls;
        [Tooltip("0 left sideline, 1 right sideline, 2 baseline, 3 the line at the glass, 4 centre line")]
        [SerializeField] FxQuad[] _tnLines;
        [SerializeField] BallView _tnBall;
        [SerializeField] FxQuad _tnBallShadow;
        [SerializeField] Transform _tnRacket;
        [SerializeField] FxQuad _tnScoreBg;
        [SerializeField] StoryLabel _tnScore, _tnNote;

        const float TnIntro = 1.0f, TnExit = 0.75f, TnSwing = 0.62f;
        TennisState _tn = new TennisState();
        LayeredOverlay _ovGame;                 // a second layered window, the size of the monitor, only while a game runs
        bool _tnOn, _tnRefused;
        float _tnFade, _tnT, _tnExitT = -1f, _tnStep, _tnYaw = 180f, _tnSx = 1f, _tnSy = 1f, _tnSpin, _tnShownFade = -1f;
        Vector3 _tnHome, _tnPos, _tnExitFrom, _tnBallAt; float _tnHomeYaw;
        readonly Pose _tnTarget = new Pose(), _tnBlend = new Pose();
        Color[] _tnWallA, _tnWallB, _tnLineA, _tnLineB; Color _tnScoreA, _tnScoreB, _tnShadowC;
        int _tnShotSeq = -1, _tnBounces, _tnDropAfter; bool _tnHadBall; double _tnWhiffAt = -1; float _tnDropBy;

        bool TennisWanted => _tn.active && _tn.court != null && _mon != null && _tn.court.monitorId == _mon.id;

        /// <summary>The game as the host has it (null = no game).</summary>
        void OnTennisState(TennisState g)
        {
            if (g != null && g.active && g.court != null && g.court.room) g = new TennisState { revision = g.revision };      // a game on the desk in the room: nothing of it is drawn here
            if (g == null) { if (_tn.active) { _tn = new TennisState { revision = _tn.revision }; Log("tennis: the game ended"); } return; }
            if (g.revision < _tn.revision) return;
            if (g.active != _tn.active) Log(g.active ? $"tennis: a game begins on {g.court.monitorId}, court {g.court.width * 1000:0}x{g.court.height * 1000:0}x{g.court.depth * 1000:0} mm" : "tennis: the game ended");
            _tn = g;
        }

        /// <summary>A point of the court (the host's display frame) in this app's world. The same thing unless the two measured the screen differently.</summary>
        Vector3 CourtToWorld(Vector3 p) => new Vector3(p.x * _tnSx, p.y * _tnSy, p.z);

        static void Put(FxQuad q, Vector3 pos, Quaternion rot, float w, float h)
        {
            q.transform.SetPositionAndRotation(pos, rot);
            Ui.SizePanel(q, w, h, 0f, 0f);
            var p = q.param; p.w = 0.003f; q.SetParam(p);        // walls meet edge to edge: a crisp edge, not the soft one of a floating panel
        }

        static Color A(Color c, float a) { c.a *= a; return c; }

        void TennisEnter()
        {
            if (_tnRefused) return;
            var b = _mon.bounds;
            if (_ovGame != null) { _ovGame.Dispose(); _ovGame = null; }
            try { _ovGame = new LayeredOverlay(b.w, b.h, _encodeMaterial); }
            catch (Exception e) { Log("tennis: the court window could not be made: " + e.Message); _ovGame = null; _tnRefused = true; return; }
            var c = _tn.court;
            _tnOn = true; _tnExitT = -1f; _tnT = 0f; _tnFade = 0f; _tnShownFade = -1f; _tnStep = 0f; _tnShotSeq = -1; _tnHadBall = false; _tnWhiffAt = -1;
            _tnHome = new Vector3(_motor.Pos.x, _feetYM + FloorCorr(_feetYM, _motor.Pos.y), _motor.Pos.y); _tnHomeYaw = _motor.Yaw;
            _tnPos = _tnHome; _tnYaw = _tnHomeYaw;
            _tnBlend.CopyFrom(_pose);
            _tnSx = c.width > 0.01f ? _wM / c.width : 1f; _tnSy = c.height > 0.01f ? _hM / c.height : 1f;
            if (_tnWallA == null)
            {
                // the colours the court was authored with: the court fades in and out by their alpha
                _tnWallA = new Color[_tnWalls.Length]; _tnWallB = new Color[_tnWalls.Length];
                for (int i = 0; i < _tnWalls.Length; i++) { _tnWallA[i] = _tnWalls[i].color; _tnWallB[i] = _tnWalls[i].color2; }
                _tnLineA = new Color[_tnLines.Length]; _tnLineB = new Color[_tnLines.Length];
                for (int i = 0; i < _tnLines.Length; i++) { _tnLineA[i] = _tnLines[i].color; _tnLineB[i] = _tnLines[i].color2; }
                _tnScoreA = _tnScoreBg.color; _tnScoreB = _tnScoreBg.color2; _tnShadowC = _tnBallShadow.color;
            }
            // the box behind the glass: as wide and as high as the lit screen, as deep as the host says
            float w = _wM, h = _hM, d = c.depth;
            var flat = Quaternion.Euler(90, 0, 0);
            // the back wall is drawn first and is larger than the opening, the floor and ceiling are wider than the box, the
            // side walls come last: where two of them meet, the one drawn later ends on the one behind it and nothing shows through
            const float over = 0.04f;
            Put(_tnWalls[0], new Vector3(0, -h * 0.5f, d * 0.5f), flat, w + over * 2, d);
            Put(_tnWalls[1], new Vector3(0, h * 0.5f, d * 0.5f), flat, w + over * 2, d);
            Put(_tnWalls[2], new Vector3(0, 0, d), Quaternion.identity, w + over * 2, h + over * 2);
            Put(_tnWalls[3], new Vector3(-w * 0.5f, 0, d * 0.5f), Quaternion.Euler(0, 90, 0), d, h);
            Put(_tnWalls[4], new Vector3(w * 0.5f, 0, d * 0.5f), Quaternion.Euler(0, -90, 0), d, h);
            float fy = -h * 0.5f + 0.0008f, inset = 0.03f, lw = 0.0045f;
            Put(_tnLines[0], new Vector3(-w * 0.5f + inset, fy, d * 0.5f), flat, lw, d - 2 * inset);
            Put(_tnLines[1], new Vector3(w * 0.5f - inset, fy, d * 0.5f), flat, lw, d - 2 * inset);
            Put(_tnLines[2], new Vector3(0, fy, d - inset), flat, w - 2 * inset, lw);
            Put(_tnLines[3], new Vector3(0, fy, inset), flat, w - 2 * inset, lw);
            Put(_tnLines[4], new Vector3(0, fy, d * 0.5f), flat, lw * 0.8f, d - 2 * inset);
            float k = Mathf.Clamp(w / 0.55f, 0.5f, 1.2f);
            _tnScoreBg.transform.SetPositionAndRotation(new Vector3(0, h * 0.5f - 0.050f * k, d - 0.003f), Quaternion.identity);
            Ui.SizePanel(_tnScoreBg, 0.27f * k, 0.070f * k, 0.014f * k, 0.0016f);
            _tnScore.transform.position = new Vector3(0, h * 0.5f - 0.041f * k, d - 0.006f); _tnScore.transform.localScale = Vector3.one * k;
            _tnNote.transform.position = new Vector3(0, h * 0.5f - 0.067f * k, d - 0.006f); _tnNote.transform.localScale = Vector3.one * k;
            _tnScore.text = ""; _tnNote.text = "";
            _tnBall.Visible = false; _tnBallShadow.enabled = false;
            _tnRoot.SetActive(true);
            _cam.targetTexture = _ovGame.Color;
            Log($"tennis: court window {b.w}x{b.h}px on {_mon.id}, box {w * 1000:0}x{h * 1000:0}x{d * 1000:0} mm, the partner comes from ({_tnHome.x:0.000},{_tnHome.y:0.000},{_tnHome.z:0.000})");
        }

        void TennisLeave(string why)
        {
            if (!_tnOn) return;
            _tnOn = false; _tnExitT = -1f;
            _tnRoot.SetActive(false); _tnBall.Visible = false; _tnBallShadow.enabled = false;
            _cam.targetTexture = _ov.Color;
            // the court window goes once the small window shows the partner again, so there is no frame without it
            _tnDropAfter = _ov.FramesPresented + 2; _tnDropBy = Time.realtimeSinceStartup + 0.5f;
            _fallV = 0; _nextWander = Time.realtimeSinceStartupAsDouble + 8;
            Log("tennis: court closed (" + why + ")");
        }

        void CourtAlpha(float a)
        {
            if (Mathf.Abs(a - _tnShownFade) < 1e-4f) return;
            _tnShownFade = a;
            for (int i = 0; i < _tnWalls.Length; i++) _tnWalls[i].SetColors(A(_tnWallA[i], a), A(_tnWallB[i], a));
            for (int i = 0; i < _tnLines.Length; i++) _tnLines[i].SetColors(A(_tnLineA[i], a), A(_tnLineB[i], a));
            _tnScoreBg.SetColors(A(_tnScoreA, a), A(_tnScoreB, a));
            _tnScore.color = A(Ui.Pal.Ink, a); _tnNote.color = A(Ui.Pal.TextDim, a);
        }

        /// <summary>A hop from one spot to another: gather, fly in an arc, land. Fills the target pose, moves the feet.</summary>
        void TennisHop(Vector3 from, Vector3 to, float u, float apex)
        {
            const float prep = 0.18f;
            if (u < prep) { PoseLib.Crouch(_tnTarget, M.Smooth(u / prep)); _tnPos = from; return; }
            float v = Mathf.Clamp01((u - prep) / (1f - prep)), k = M.Smooth(v);
            _tnPos = Vector3.Lerp(from, to, k) + Vector3.up * (apex * 4f * v * (1f - v));
            if (v < 0.75f) PoseLib.Tuck(_tnTarget); else PoseLib.Land(_tnTarget, M.Smooth((v - 0.75f) / 0.25f));
        }

        /// <summary>One frame of the game on this side. Returns whether the partner is shown.</summary>
        bool TennisFrame(float dt)
        {
            var c = _tn.court; double hostNow = _link.HostNow;
            bool leaving = !TennisWanted;
            if (leaving && _tnExitT < 0f) { _tnExitT = 0f; _tnExitFrom = _tnPos; }
            if (!leaving && _tnExitT >= 0f) { _tnExitT = -1f; _tnT = TnIntro; }          // another game began before it had left: stay
            _mode = "Tennis"; _tnT += dt;
            _tnFade = Mathf.Clamp01(_tnFade + (leaving ? -dt / 0.5f : dt / 0.55f));
            CourtAlpha(_tnFade);
            // pixels drawn now reach the desktop one read-back later: draw the ball where it will be then
            _latency = Mathf.Lerp(_latency, Mathf.Clamp(_ovGame.LastReadbackMs, 0f, 90f) / 1000f + 0.008f, 0.1f);
            double drawT = hostNow + _latency;
            float floorY = -_hM * 0.5f, time = Time.time;
            bool playing = !leaving && _tnT >= TnIntro;

            // ---- the ball: the stroke the host announced, evaluated on the shared clock
            bool haveBall = false, inPlay = false; BallSample ball = default; Vector3 ballW = default;
            if (playing && _tn.shot != null && _tn.phase == "rally")
            {
                ball = Tennis.Evaluate(c, _tn.shot, drawT); ballW = CourtToWorld(Conv.U(ball.pos)); haveBall = inPlay = true;
                if (_tn.shot.seq != _tnShotSeq) { _tnShotSeq = _tn.shot.seq; _tnBounces = ball.bouncesBack; _tnWhiffAt = -1; }
                if (ball.bouncesBack > _tnBounces)
                {
                    _tnBounces = ball.bouncesBack;
                    _sparks.Burst(new Vector3(ballW.x, floorY + 0.003f, ballW.z), Vector3.right, Vector3.up, 0.022f, 4);
                }
                // a ball the partner will not get to: it still swings at it as the ball goes by
                if (_tn.shot.by == "user" && _tn.partnerHitAt <= 0 && _tnWhiffAt < 0 && ball.crossed && ball.pos.Z > _tn.partnerZ - 0.08f) _tnWhiffAt = hostNow;
            }
            else if (playing && _tn.phase == "ready")
            {
                // in its hand before the serve, then a small toss up to where the racket meets it
                float u = Mathf.Clamp01(1f - (float)(_tn.serveAt - drawT) / 0.55f);
                Vector3 p = Conv.U(Tennis.ServePoint(c, _tn.partnerX));
                if (_tn.mode != "keeper") p.y += -0.020f * (1f - u) + 0.045f * Mathf.Sin(u * Mathf.PI);
                ballW = CourtToWorld(p); ball.pos = Conv.N(p); haveBall = true;
            }
            if (_tnHadBall && !inPlay && _tnBallAt.z > 0.01f && _tnFade > 0.5f) _sparks.Burst(_tnBallAt, Vector3.right, Vector3.up, 0.03f, 6);    // the point is over: the ball is gone
            _tnHadBall = inPlay; if (inPlay) _tnBallAt = ballW;

            bool showBall = haveBall && ball.pos.Z > -Tennis.BallRadius(_tn.mode);
            _tnBall.Visible = showBall;
            if (showBall)
            {
                Vector3 vel = Conv.U(ball.vel);
                _tnSpin += vel.magnitude / Tennis.BallRadius(_tn.mode) * Mathf.Rad2Deg * dt * 0.35f;
                Vector3 axis = Vector3.Cross(Vector3.up, vel); axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
                _tnBall.Football(_tn.mode == "keeper");
                _tnBall.Place(ballW, Quaternion.AngleAxis(_tnSpin, axis), Tennis.BallRadius(_tn.mode) * 2f);
                _tnBall.SetClip(Vector3.zero, Vector3.forward, +1f);           // this side draws what is behind the glass
            }
            bool shadow = showBall && ball.pos.Z > 0.006f && _tnFade > 0.5f;
            if (_tnBallShadow.enabled != shadow) _tnBallShadow.enabled = shadow;
            if (shadow)
            {
                float hgt = Mathf.Max(0f, ballW.y - floorY);
                _tnBallShadow.transform.SetPositionAndRotation(new Vector3(ballW.x, floorY + 0.0014f, ballW.z), Quaternion.Euler(90, 0, 0));
                _tnBallShadow.transform.localScale = Vector3.one * Tennis.BallRadius(_tn.mode) * 2f * (1.5f + hgt * 3.5f);
                _tnBallShadow.SetColor(A(_tnShadowC, 1f / (1f + hgt * 9f)));
            }

            // ---- the partner
            Vector3 stand = CourtToWorld(new Vector3(_tn.partnerX + 0.03f, c.FloorBack, _tn.partnerZ));     // the racket hand is on the ball's line, the body beside it
            bool grounded = false;
            if (_tnExitT >= 0f)
            {
                _tnExitT += dt;
                float u = Mathf.Clamp01(_tnExitT / TnExit);
                TennisHop(_tnExitFrom, _tnHome, u, 0.05f);
                _tnYaw = Mathf.MoveTowardsAngle(_tnYaw, _tnHomeYaw, 360f * dt);
                _tnBlend.CopyFrom(_tnTarget);
                if (u >= 1f) { TennisLeave("the game is over"); return Roam(dt, false); }
            }
            else if (_tnT < TnIntro)
            {
                // from where it stood on the desktop back to its baseline, as the court appears around it
                TennisHop(_tnHome, CourtToWorld(new Vector3(0.03f, c.FloorBack, Tennis.PartnerDepth)), _tnT / TnIntro, 0.06f);
                _tnYaw = Mathf.MoveTowardsAngle(_tnYaw, 180f, 360f * dt);
                _tnBlend.CopyFrom(_tnTarget);
            }
            else
            {
                grounded = true;
                Vector3 to = stand - _tnPos; to.y = 0; float dist = to.magnitude;
                float speed = 0.5f;
                if (_tn.partnerHitAt > hostNow) speed = Mathf.Clamp(dist / Mathf.Max(0.05f, (float)(_tn.partnerHitAt - hostNow) - 0.10f), 0.3f, 1.5f);   // be there when the ball is
                float stepLen = Mathf.Min(dist, speed * dt);
                if (dist > 1e-5f) _tnPos += to / dist * stepLen;
                _tnPos.y = stand.y;
                bool moving = dist > 0.004f;
                // the stroke is timed backwards from the moment the host says the racket meets the ball
                double anchor = _tn.partnerHitAt > 0 ? _tn.partnerHitAt : (_tnWhiffAt > 0 ? _tnWhiffAt + TnSwing * 0.2 : -1);
                float us = anchor > 0 ? (float)((drawT - anchor) / TnSwing) + PoseLib.SwingContact : -1f;
                float since = (float)(hostNow - _tn.phaseAt);
                float lean = 0f;
                if (_tn.phase == "over") { if (_tn.scorePartner > _tn.scoreUser) PoseLib.Happy(_tnTarget, time); else PoseLib.Wave(_tnTarget, time); }
                else if (_tn.phase == "point" && _tn.lastPoint == "partner") PoseLib.Happy(_tnTarget, time);
                else if (_tn.phase == "point" && since < 0.7f && !(us >= 0f && us <= 1f)) PoseLib.Startle(_tnTarget, time);
                else if (us >= 0f && us <= 1f && _tn.mode == "keeper") PoseLib.FootballKick(_tnTarget, us, time);
                else if (us >= 0f && us <= 1f) PoseLib.TennisSwing(_tnTarget, us, time);
                else
                {
                    PoseLib.TennisReady(_tnTarget, time);
                    if (moving)
                    {
                        _tnStep += stepLen / 0.034f;
                        PoseLib.StepInPlace(_tnTarget, _tnStep, 0.85f);
                        lean = Mathf.Clamp(to.x / Mathf.Max(dist, 1e-4f), -1f, 1f);
                        _tnTarget[Bone.Spine].Z += 9f * lean;                     // facing the user (-z), its left is the world's +x
                    }
                }
                _tnYaw = Mathf.MoveTowardsAngle(_tnYaw, 180f - 22f * lean, 300f * dt);
                Pose.Lerp(_tnBlend, _tnTarget, 1f - Mathf.Exp(-dt * 24f), _tnBlend);
            }
            Quaternion rot = Quaternion.Euler(0, _tnYaw, 0);
            _pose.CopyFrom(_tnBlend);
            if (grounded && haveBall && _tn.phase != "over")
            {
                // its eyes follow the ball, on whichever side of the glass it is
                Vector3 local = Quaternion.Inverse(rot) * (ballW - (_tnPos + Vector3.up * (0.75f * _H)));
                PoseLib.AddLook(_pose, Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg);
            }
            _rig.transform.SetPositionAndRotation(_tnPos, rot);
            _rig.Apply(_pose);
            if (TalkLook.Active(_ch.talk)) _rig.SetGlow(TalkLook.Color(_ch.talk), 1f); else _rig.SetGlow(StatusColors.For("idle"), 0f);
            _rig.SetShadow(grounded, new Vector3(_tnPos.x, floorY, _tnPos.z), Vector3.up, 0f);

            // the racket: in the right hand, its face the way the hand faces
            var hand = _rig.Bones[(int)Bone.ArmLR];
            Vector3 along = -hand.up;
            if (_tnRacket.gameObject.activeSelf != (_tn.mode != "keeper")) _tnRacket.gameObject.SetActive(_tn.mode != "keeper");
            _tnRacket.SetPositionAndRotation(hand.position + along * (Skeleton.ArmLower * _H), Quaternion.LookRotation(hand.forward, along));
            _tnRacket.localScale = Vector3.one * Mathf.Max(0.001f, Ui.Pop(_tnFade));

            // ---- the scoreboard on the back wall
            _tnScore.text = $"You  {_tn.scoreUser} : {_tn.scorePartner}  Bambi";
            string note = "";
            if (!leaving)
                switch (_tn.phase)
                {
                    case "ready": note = _tn.scoreUser + _tn.scorePartner == 0 ? "First to " + Tennis.PointsToWin : "Get ready ..."; break;
                    case "rally": note = _tn.rally >= 2 ? "Rally " + _tn.rally : ""; break;
                    case "point": note = _tn.lastPoint == "user" ? "Point to you!" : "Point to Bambi!"; break;
                    case "over": note = _tn.scoreUser > _tn.scorePartner ? "You win!" : "Bambi wins!"; break;
                }
            if (_tn.mode.Length > 0)
            {
                // the other ball games: the same board, their own words
                string[] two = Tennis.ScoreLine(_tn).Split('\n');
                _tnScore.text = two[0]; note = leaving || two.Length < 2 ? "" : two[1];
            }
            _tnNote.text = note;
            return true;
        }

        /// <summary>Draws the court for the whole monitor, through the glass, from where the user's head is.</summary>
        void TennisLate()
        {
            bool tracked = _head != null && _head.monitorId == _mon.id && Time.realtimeSinceStartupAsDouble - _headAt < 0.6;
            Vector3 targetEye = tracked ? Conv.U(_head.eye) : new Vector3(0, 0.06f, -0.62f);
            if (targetEye.z > -0.15f) targetEye.z = -0.15f;
            _eye = Vector3.Lerp(_eye, targetEye, tracked ? 0.6f : 0.12f);
            var b = _mon.bounds; _dest = b; _clip = b;
            Vector3 tl = PxToLocal(b.x, b.y), br = PxToLocal(b.Right, b.Bottom);
            ScreenMath.OffAxis(Conv.N(_eye), tl.x, br.x, br.y, tl.y, _cam.nearClipPlane, out float l, out float r, out float bt, out float tp);
            _cam.transform.SetPositionAndRotation(_eye, Quaternion.identity);
            _cam.projectionMatrix = Matrix4x4.Frustum(l, r, bt, tp, _cam.nearClipPlane, _cam.farClipPlane);
            _dbgLabel.text = "";
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _ovGame.Color };
            if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
            else _cam.Render();
            if (_ovGame.InsertAfter != Win32.HWND_TOPMOST) { _ovGame.InsertAfter = Win32.HWND_TOPMOST; _ovGame.ZOrderDirty = true; }
            _ovGame.Present(b, b);
            if (_ov.Shown && _ovGame.FramesPresented >= 2) _ov.Hide();      // the small window stays until the court shows the partner
        }

        /// <summary>After a game: lets go of the court window once the ordinary overlay is back on screen.</summary>
        void TennisDropWindow()
        {
            if (_ovGame == null || _tnOn) return;
            if (_ov.FramesPresented < _tnDropAfter && Time.realtimeSinceStartup < _tnDropBy) return;
            _ovGame.Dispose(); _ovGame = null;
        }
    }
}
