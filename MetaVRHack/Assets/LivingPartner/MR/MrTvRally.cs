using System;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    public sealed partial class MrApp
    {
        const string TvTemplate = "tv-rally";
        readonly GamePackDef _tvPack = new GamePackDef { id = "game-tv-rally", template = TvTemplate, title = "TV Rally",
            howTo = "Connect a MacBook to a TV. One player reaches into the rings and the Quest player returns the ball." };
        [SerializeField] GameObject _tvRoot;
        [SerializeField] StoryLabel _tvLabel;
        [SerializeField] FxQuad _tvPaper;
        [SerializeField] FxQuad[] _tvRings;
        [SerializeField] BallView _tvBall;
        TvRallyServer _tvServer;
        TvRally _tvGame;
        readonly TvMotion _tvMotion = new TvMotion();
        readonly byte[] _tvSamples = new byte[TvMotion.Count * 2];
        byte[] _tvPixels;
        Vector3 _tvScreen, _tvCenter, _tvRight, _tvCatch, _tvHit, _tvPreviousCamera;
        Quaternion _tvPreviousRotation;
        int _tvMarks, _tvFrames;
        bool _tvAnnounced, _tvViewGood, _tvNeutral;
        float _tvReadAt, _tvPublishAt, _tvCameraAt;
        string _tvNotice = "", _tvEpoch;

        [Serializable] sealed class TvSession { public string url; }
        [Serializable] sealed class TvStatus
        {
            public string phase, notice;
            public int side, score, rally, marks;
            public float elapsed;
        }
        readonly TvStatus _tvStatus = new TvStatus();
        bool TvPlaying => _tvServer != null;

        void TvEnsurePack(StateSnapshot s)
        {
            if (s?.world == null) return;
            if (s.world.games == null) s.world.games = new List<GamePackDef>();
            if (!s.world.games.Exists(g => g != null && g.template == TvTemplate)) s.world.games.Insert(0, _tvPack);
        }

        void StartTvRally()
        {
            if (TvPlaying) { _tvAnnounced = false; return; }
            var page = Resources.Load<TextAsset>("TvRallyDisplay");
            var portrait = Resources.Load<TextAsset>("TvRallyNilo");
            if (_tvRoot == null || page == null || portrait == null) { Caption("TV Rally display assets are missing.", 5f); return; }
            TvRallyServer session;
            try { session = new TvRallyServer(page.text, portrait.bytes); }
            catch (Exception e) { Caption("Connect the Quest and TV computer to the same Wi-Fi, then reopen TV Rally.", 7f); Debug.LogWarning("[MR] TV Rally: " + e.Message); return; }
            StopGames("starting TV Rally");
            if (_tn.active || _tnPendingAt >= 0f) ToggleTennis("starting TV Rally");
            _tvServer = session;
            // TV Rally registers its own two anchors. Suspend any ordinary monitor scan while both people play.
            if (_lookCal) FinishFinding("");
            else if (_calibrating) EndCalibration();
            _tvGame = new TvRally(); _tvAnnounced = false; _tvPublishAt = 0f;
            _tvRoot.SetActive(true); _tvBall.SetLightDir(new Vector3(-0.35f, 0.8f, -0.4f));
            ResetTvTracking();
        }

        void StopTvRally()
        {
            if (!TvPlaying) return;
            if (_tvAnnounced && _link != null && _link.Connected && !LocalMode) _link.Send("tv.session", new TvSession { url = "" });
            _tvServer.Dispose(); _tvServer = null; _tvGame = null; _tvPixels = null;
            _tvRoot.SetActive(false); _tvMotion.Clear();
        }

        void ResetTvTracking()
        {
            _tvMarks = _tvFrames = 0; _tvReadAt = 0f; _tvCameraAt = -10f;
            _tvViewGood = _tvNeutral = false; _tvNotice = ""; _tvEpoch = Input.SpaceEpoch;
            _tvMotion.Clear();
        }

        Vector3 TvGazePoint(float fallback, float maxDepth)
        {
            Vector3 direction = Input.HeadRot * Vector3.forward;
            if (Input.RaycastEnvironment(Input.HeadPos, direction, maxDepth, out var p) && Vector3.Distance(Input.HeadPos, p) > 0.8f) return p;
            return Input.HeadPos + direction * fallback;
        }

        void UpdateTvRally(float dt)
        {
            if (!TvPlaying) return;
            if (_link != null && _link.Connected && !_link.NeedsPairing && !LocalMode)
            {
                if (!_tvAnnounced) { _link.Send("tv.session", new TvSession { url = _tvServer.Url }); _tvAnnounced = true; }
            }
            else _tvAnnounced = false;
            var old = _tvGame.phase;
            bool startWithHand = (old == TvRallyPhase.Join || old == TvRallyPhase.Won) && _tvServer.ViewerPresent &&
                Application.isFocused && Input.HeadTracked && Input.Right.tracked && Input.Right.pinchDown;
            if (_tvServer.TakeReady() || startWithHand || (_tvMarks > 0 && _tvEpoch != Input.SpaceEpoch)) { _tvGame.Join(); ResetTvTracking(); }
            if (!_tvServer.ViewerPresent && _tvGame.phase != TvRallyPhase.Join) { _tvGame = new TvRally(); ResetTvTracking(); }
            var phase = _tvGame.phase;
            if (phase != TvRallyPhase.Join && phase != TvRallyPhase.Won && Application.isFocused && Input.HeadTracked)
            {
                int reached = 0;
                if ((phase == TvRallyPhase.Align || phase == TvRallyPhase.Reach || phase == TvRallyPhase.Serve) && Time.unscaledTime >= _tvReadAt)
                {
                    _tvReadAt = Time.unscaledTime + 0.11f;
                    reached = TvReadMotion();
                }
                if (phase == TvRallyPhase.Align && !startWithHand && _tvMarks < 2 && Input.Right.pinchDown && Time.unscaledTime - _tvCameraAt < 0.5f)
                {
                    if (_tvMarks == 0) _tvScreen = TvGazePoint(2.4f, 4.5f);
                    else
                    {
                        _tvCenter = TvGazePoint(1.6f, 2.2f);
                        _tvRight = Vector3.Cross(Vector3.up, _tvCenter - Input.HeadPos).normalized;
                    }
                    _tvMarks++; _tvFrames = 0; Sfx2D("tap", 0.65f);
                }
                if (phase == TvRallyPhase.Volley || phase == TvRallyPhase.Return || phase == TvRallyPhase.Miss || _tvViewGood)
                {
                    Vector3 ball = TvBallPosition();
                    bool hit = phase == TvRallyPhase.Volley && (TvContact(Input.Left, ball) || TvContact(Input.Right, ball));
                    if (hit) _tvHit = ball;
                    _tvGame.Step(dt, reached, hit);
                }
            }
            if (_tvGame.phase != old)
            {
                if (_tvGame.phase == TvRallyPhase.Serve) _tvNeutral = false;
                if (_tvGame.phase == TvRallyPhase.Volley)
                {
                    Vector3 f = Input.HeadRot * Vector3.forward; f.y = 0f; f.Normalize();
                    _tvCatch = Input.HeadPos + f * 0.43f + Vector3.down * 0.23f + Vector3.Cross(Vector3.up, f) * (_tvGame.side * 0.08f);
                    Sfx2D("bounce", 0.75f);
                }
                if (_tvGame.phase == TvRallyPhase.Return) Sfx2D("bounce", 0.85f);
                if (_tvGame.phase == TvRallyPhase.Won) Sfx2D("success", 0.85f);
            }
            string line = TvHelp();
            DrawTvRally(line);
            if (Time.unscaledTime >= _tvPublishAt || _tvGame.phase != old)
            {
                _tvPublishAt = Time.unscaledTime + 0.115f;
                _tvStatus.phase = _tvGame.phase.ToString(); _tvStatus.marks = _tvMarks; _tvStatus.notice = _tvNotice;
                _tvStatus.score = _tvGame.score; _tvStatus.rally = _tvGame.rally; _tvStatus.side = _tvGame.side; _tvStatus.elapsed = _tvGame.elapsed;
                _tvServer.Publish(JsonUtility.ToJson(_tvStatus));
            }
        }

        static bool TvContact(HandState hand, Vector3 ball) => hand.tracked &&
            ((hand.palmPos - ball).sqrMagnitude < 0.0169f || (hand.indexTip - ball).sqrMagnitude < 0.01f);

        Vector3 TvRing(int side) => _tvCenter + _tvRight * (0.35f * side) + Vector3.up * 0.10f;
        int TvReadMotion()
        {
            _tvViewGood = false;
            if (!_settings.cameraEnabled) { _tvNotice = "Enable the camera in Bag settings."; return 0; }
            CameraShot shot;
            bool available = Input is IMrCameraPreview preview ? preview.CameraPreview(ref _tvPixels, out shot) : Input.CameraFrame(ref _tvPixels, out shot);
            if (!available)
            {
                _tvFrames = 0;
                _tvNotice = Input.Simulated ? "Body tracking requires Quest 3 or Quest 3S."
                    : Input is IMrCameraPreview device && !device.HasCameraPermission ? "Allow camera access for Yonder in Quest app permissions."
                    : "Put on your headset and enable passthrough.";
                return 0;
            }
            bool rapid = Time.unscaledTime - _tvCameraAt < 0.4f && (Quaternion.Angle(shot.rotation, _tvPreviousRotation) > 6f || Vector3.Distance(shot.position, _tvPreviousCamera) > 0.06f);
            _tvCameraAt = Time.unscaledTime; _tvPreviousRotation = shot.rotation; _tvPreviousCamera = shot.position;
            _tvNotice = "";
            if (_tvMarks < 2) return 0;
            if (rapid || !SampleTvRings(shot)) { _tvFrames = 0; _tvNotice = "Keep the rings on both sides of the other player in view."; return 0; }
            _tvViewGood = true;
            if (_tvGame.phase == TvRallyPhase.Align)
            {
                if (++_tvFrames >= 8) { _tvMotion.Calibrate(_tvSamples); _tvGame.Aligned(); }
                return 0;
            }
            if (!_tvMotion.calibrated) return 0;
            if (TvHandCoversGate(shot, Input.Left) || TvHandCoversGate(shot, Input.Right)) return 0;
            int read = _tvMotion.Read(_tvSamples, _tvGame.side);
            if (_tvMotion.IsNeutral) _tvNeutral = true;
            return _tvNeutral ? read : 0;
        }

        bool TvHandCoversGate(CameraShot shot, HandState hand)
        {
            if (!hand.tracked) return false;
            Vector3 gate = TvRing(_tvGame.side) - shot.position;
            return Vector3.Angle(gate, hand.palmPos - shot.position) < 12f || Vector3.Angle(gate, hand.indexTip - shot.position) < 9f;
        }

        // Project a grid in the playmate's shoulder plane into each new camera pose. The view may rotate between readings.
        bool SampleTvRings(CameraShot shot)
        {
            var inv = Quaternion.Inverse(shot.rotation);
            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 center = TvRing(ring == 0 ? -1 : 1);
                for (int row = 0; row < TvMotion.Grid; row++)
                    for (int col = 0; col < TvMotion.Grid; col++)
                    {
                        Vector3 at = center + _tvRight * ((col - 4) * 0.036f) + Vector3.up * ((row - 4) * 0.036f);
                        Vector3 ray = inv * (at - shot.position);
                        if (ray.z <= 0.25f) return false;
                        int x = Mathf.RoundToInt(shot.cx + shot.fx * ray.x / ray.z), y = Mathf.RoundToInt(shot.cy - shot.fy * ray.y / ray.z);
                        if (x < 0 || y < 0 || x >= shot.width || y >= shot.height) return false;
                        int offset = (y * shot.width + x) * 4;
                        _tvSamples[ring * TvMotion.Count + row * TvMotion.Grid + col] = (byte)((54 * _tvPixels[offset] + 183 * _tvPixels[offset + 1] + 19 * _tvPixels[offset + 2]) >> 8);
                    }
            }
            return true;
        }

        string TvHelp()
        {
            switch (_tvGame.phase)
            {
                case TvRallyPhase.Join: return _tvServer.ViewerPresent ? "TV connected. Pinch to begin." :
                    "TV Rally · Open on your TV or computer\n" + _tvServer.Url + "\nUse the same Wi-Fi as your Quest.";
                case TvRallyPhase.Align:
                    if (_tvNotice.Length > 0) return _tvNotice;
                    if (_tvMarks == 0) return "Look at the center of the TV and pinch.";
                    if (_tvMarks == 1) return "Look at the other player’s chest and pinch. Ask them to lower their arms.";
                    return "Lower your arms and hold still.";
                case TvRallyPhase.Serve:
                case TvRallyPhase.Reach: return _tvNotice.Length > 0 ? _tvNotice : "TV player: reach for the bright ring.  " + _tvGame.score + " / 6";
                case TvRallyPhase.Volley: return "Quest player: return the ball.";
                case TvRallyPhase.Return: return "Back to Nilo!  " + _tvGame.score + " / 6";
                case TvRallyPhase.Miss: return "Lower your arms. Nilo will serve.";
                default: return "Six returns! Pinch to play again.";
            }
        }

        Vector3 TvBallPosition()
        {
            Vector3 ring = TvRing(_tvGame.side);
            switch (_tvGame.phase)
            {
                case TvRallyPhase.Serve: return TvArc(_tvScreen, ring, _tvGame.elapsed, 0.13f);
                case TvRallyPhase.Volley: return TvArc(ring, _tvCatch, _tvGame.elapsed / 1.18f, 0.13f);
                case TvRallyPhase.Return: return TvArc(_tvHit, _tvScreen, _tvGame.elapsed / 0.85f, 0.22f);
                default: return ring;
            }
        }
        static Vector3 TvArc(Vector3 a, Vector3 b, float t, float height)
        {
            t = Mathf.Clamp01(t);
            return Vector3.Lerp(a, b, t) + Vector3.up * (height * 4f * t * (1f - t));
        }

        void DrawTvRally(string line)
        {
            var phase = _tvGame.phase;
            Vector3 label = Input.HeadPos + Input.HeadRot * new Vector3(0f, -0.19f, 0.74f);
            Place(_tvLabel, label, line);
            _tvPaper.transform.SetPositionAndRotation(label + _tvLabel.transform.forward * 0.003f, _tvLabel.transform.rotation);
            Ui.SizePanel(_tvPaper, _tvLabel.Width + 0.034f, _tvLabel.Height + 0.020f, 0.012f, 0.001f);
            _tvBall.Visible = phase == TvRallyPhase.Serve || phase == TvRallyPhase.Reach || phase == TvRallyPhase.Volley || phase == TvRallyPhase.Return;
            if (_tvBall.Visible) _tvBall.Place(TvBallPosition(), Quaternion.Euler(Time.time * 160f, Time.time * 90f, 15f), 0.080f);
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool on = _tvMarks == 2 && (phase == TvRallyPhase.Align || ((phase == TvRallyPhase.Serve || phase == TvRallyPhase.Reach) && side == _tvGame.side));
                _tvRings[i].enabled = on;
                if (on) { FaceUser(_tvRings[i].transform, TvRing(side)); _tvRings[i].transform.localScale = Vector3.one * (0.27f + 0.016f * Mathf.Sin(7f * Time.time)); }
            }
        }
    }
}
