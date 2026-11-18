using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace LivingPartner.MR
{
    public sealed partial class MrApp
    {
        // The host owns the stones and the TV shows both players the same cursor. The Quest
        // supplies its tracked ray on black's turn and local camera observations on white's.
        [Serializable, UnityEngine.Scripting.Preserve]
        sealed class GomokuState
        {
            public bool active;
            public int turn, round, revision;
            public string monitorId = "", status = "";
        }

        [Serializable, UnityEngine.Scripting.Preserve]
        sealed class GomokuFrame
        {
            public int seq, round, revision, w, h;
            public string monitorId;
            public float fx, fy, cx, cy, widthM, heightM;
            public float[] center, right, up;
            public float[][][] own;
            public byte[] jpeg;
        }

        GomokuState _gomoku;
        int _gomokuSeq, _gomokuSkippedRound, _gomokuCaptureEpoch, _gomokuPendingEpoch;
        float _gomokuRequestedUntil, _gomokuNextAim, _gomokuNextCapture, _gomokuCapturedAt;
        Vector2 _gomokuLastAim;
        float _gomokuLastAimAt = -1f;
        double _gomokuImageTime;
        Quaternion _gomokuPreviousRotation;
        Vector3 _gomokuPreviousPosition;
        bool _gomokuCameraAllowed, _gomokuAimShown;
        string _gomokuCameraStatus = "", _gomokuSpace = "";
        Task<GomokuFrame> _gomokuEncoding;
        byte[] _gomokuPixels, _gomokuFlipped;
        bool GomokuOn => _gomoku != null || _gomokuRequestedUntil > 0f;

        void RequestGomoku(string selected = null)
        {
            if (_gomoku != null) return;
            if (LocalMode || !_link.Connected || _link.NeedsPairing)
            {
                Caption("Connect the Mac or Windows companion to open the board.", 5f);
                return;
            }
            if (_gomokuRequestedUntil > Time.unscaledTime) return;
            MonView screen = null;
            float best = float.PositiveInfinity;
            if (!string.IsNullOrEmpty(selected)) screen = Mon(selected);
            else foreach (var m in _mons.Values)
            {
                float rank = SwarmRules.ScreenScore(Conv.N(Input.HeadPos), Conv.N(Input.HeadRot * Vector3.forward),
                    m.frame.origin, m.frame.x, m.frame.y, -m.frame.z, m.frame.widthM, m.frame.heightM, false);
                if (rank < best) { best = rank; screen = m; }
            }
            if (screen == null) { Caption("Calibrate the TV, then look at the screen for Gomoku.", 5f); return; }
            _gomokuSkippedRound = 0;
            _gomokuRequestedUntil = Time.unscaledTime + 7f;
            _link.Send("gomoku.start", new { monitorId = screen.desc.id });
        }

        void OnGomokuState(GomokuState state)
        {
            if (state == null) return;
            if (!state.active)
            {
                if (_gomoku == null || state.round == _gomoku.round || state.round == 0) EndGomoku(false);
                return;
            }
            if (LocalMode || state.round <= 0 || state.round == _gomokuSkippedRound || state.turn < 0 || state.turn > 2) return;
            if (_gomoku != null && _gomoku.round == state.round && state.revision < _gomoku.revision) return;
            var old = _gomoku;
            if (old == null || old.round != state.round)
            {
                _gomokuRequestedUntil = 0f;
                _gomoku = null;
                StopGames("Start Gomoku");
                if (_tn.active || _tnPendingAt >= 0f) ToggleTennis("Start Gomoku");
                _gomokuCameraAllowed = false; _gomokuSeq = 0; _gomokuSpace = Input.SpaceEpoch;
            }
            if (old == null || old.round != state.round || old.revision != state.revision || old.turn != state.turn)
            {
                _gomokuCaptureEpoch++; _gomokuAimShown = false; _gomokuLastAimAt = -1f; _gomokuCameraStatus = ""; _gomokuImageTime = 0;
                if (old == null || old.round != state.round)
                    Caption("Black: point at an intersection and pinch.", 3f);
            }
            _gomoku = state; _gomokuRequestedUntil = 0f;
        }

        void EndGomoku(bool notify)
        {
            if (!GomokuOn) return;
            if (notify && _gomoku != null && _link != null && _link.Connected)
            {
                _gomokuSkippedRound = _gomoku.round;
                _link.Send("gomoku.stop", new { _gomoku.round, _gomoku.monitorId });
            }
            _gomoku = null; _gomokuRequestedUntil = 0f; _gomokuCaptureEpoch++; _gomokuCameraAllowed = false;
            _gomokuPixels = null; _gomokuFlipped = null; _gomokuAimShown = false;
        }

        void UpdateGomoku(bool hostOk)
        {
            float now = Time.unscaledTime;
            if (_gomokuEncoding != null && _gomokuEncoding.IsCompleted)
            {
                var work = _gomokuEncoding;
                _gomokuEncoding = null;
                if (work.IsFaulted) { _ = work.Exception; if (_gomoku != null) GomokuCameraStatus("warming"); }
                else if (work.Result != null && _gomoku != null && _gomoku.turn == 2 && hostOk && _gomokuCameraAllowed &&
                    _gomokuCaptureEpoch == _gomokuPendingEpoch && _settings.cameraEnabled && Input.HeadTracked &&
                    Input.SpaceEpoch == _gomokuSpace && Application.isFocused && now - _gomokuCapturedAt < .5f)
                    _link.SendPreview("gomoku.frame", work.Result);
            }
            if (!hostOk || _link.NeedsPairing || LocalMode) { EndGomoku(false); _gomokuSkippedRound = 0; return; }
            if (_gomoku == null)
            {
                if (_gomokuRequestedUntil > 0f && now > _gomokuRequestedUntil)
                {
                    _gomokuRequestedUntil = 0f; Caption("Open Gomoku on the TV in fullscreen.", 5f);
                }
                return;
            }
            if (Input.SpaceEpoch != _gomokuSpace || _calibrating)
            {
                EndGomoku(true); Caption("Calibrate the TV again, then open Gomoku.", 4f); return;
            }
            MonView screen = Mon(_gomoku.monitorId);
            if (screen == null || !Input.HeadTracked || !Application.isFocused)
            {
                if (_gomoku.turn == 2) GomokuCameraStatus("tracking");
                else ClearGomokuAim();
                return;
            }
            if (_gomoku.turn == 1) UpdateGomokuAim(screen, now);
            else if (_gomoku.turn == 2) UpdateGomokuCamera(screen, now);
        }

        void ClearGomokuAim()
        {
            if (!_gomokuAimShown || _gomoku == null) return;
            _gomokuAimShown = false;
            SendGomokuAim(-1, -1, false);
        }

        void SendGomokuAim(float u, float v, bool commit) =>
            _link.Send("gomoku.input", new { seq = ++_gomokuSeq, _gomoku.round, _gomoku.revision, side = "wearer", u, v, commit, _gomoku.monitorId });

        void UpdateGomokuAim(MonView screen, float now)
        {
            var h = Input.Right;
            // Pinching bends the pointing finger. Submit the intersection both players
            // just saw, even if the hand ray changes on the pinch-down frame.
            if (h.tracked && h.pinchDown && _gomokuAimShown && now - _gomokuLastAimAt < .22f)
            {
                _gomokuCameraAllowed = true;
                SendGomokuAim(_gomokuLastAim.x, _gomokuLastAim.y, true);
                return;
            }
            if (!h.tracked || h.rayDir.sqrMagnitude < .25f || !screen.frame.Raycast(Conv.N(h.rayOrigin), Conv.N(h.rayDir), out var point, out _))
            { ClearGomokuAim(); return; }
            ScreenMath.LocalToUV(screen.frame.widthM, screen.frame.heightM, point, out float u, out float v);
            if (u < 0 || u > 1 || v < 0 || v > 1) { ClearGomokuAim(); return; }
            if (!h.pinchDown && now < _gomokuNextAim) return;
            _gomokuNextAim = now + .065f; _gomokuAimShown = true;
            _gomokuLastAim = new Vector2(u, v); _gomokuLastAimAt = now;
            if (h.pinchDown) _gomokuCameraAllowed = true;
            SendGomokuAim(u, v, h.pinchDown);
        }

        void GomokuCameraStatus(string status)
        {
            if (_gomoku == null || _gomokuCameraStatus == status) return;
            _gomokuCameraStatus = status;
            _link.Send("gomoku.camera", new { _gomoku.round, _gomoku.revision, _gomoku.monitorId, status });
            if (status == "disabled") Caption("Enable the camera in Bag settings to see the other player’s hand.", 5f);
            else if (status == "permission") Caption("Allow headset camera access in Quest settings.", 5f);
            else if (status == "consent") Caption("Pinch once with your right hand to read the other player’s move.", 5f);
        }

        void UpdateGomokuCamera(MonView screen, float now)
        {
            if (!_settings.cameraEnabled) { GomokuCameraStatus("disabled"); return; }
            if (!_gomokuCameraAllowed && Input.Right.tracked && Input.Right.pinchDown) _gomokuCameraAllowed = true;
            if (!_gomokuCameraAllowed) { GomokuCameraStatus("consent"); return; }
            if (_gomokuEncoding != null || now < _gomokuNextCapture) return;
            _gomokuNextCapture = now + .12f;
            if (!(Input is IMrBoardCamera camera) || !camera.BoardCameraFrame(ref _gomokuPixels, out var shot))
            {
                GomokuCameraStatus(Input is IMrCameraPreview preview && !preview.HasCameraPermission ? "permission" : "warming");
                return;
            }
            if (shot.timestamp > 0 && shot.timestamp <= _gomokuImageTime) return;
            bool moved = _gomokuImageTime > 0 && shot.timestamp - _gomokuImageTime < .35 &&
                (Quaternion.Angle(shot.rotation, _gomokuPreviousRotation) > 3.5f ||
                 Vector3.Distance(shot.position, _gomokuPreviousPosition) > .045f);
            _gomokuImageTime = shot.timestamp;
            _gomokuPreviousRotation = shot.rotation; _gomokuPreviousPosition = shot.position;
            if (moved) { GomokuCameraStatus("tracking"); return; }
            var inverse = Quaternion.Inverse(shot.rotation);
            float[] Optical(Vector3 relative) => GomokuCamera.Optical(inverse, relative);
            var own = new List<float[][]>(2);
            void AddHand(HandState hand)
            {
                if (hand.tracked) own.Add(new[] { Optical(hand.wrist - shot.position), Optical(hand.palmPos - shot.position), Optical(hand.indexTip - shot.position) });
            }
            AddHand(Input.Left); AddHand(Input.Right);
            var frame = new GomokuFrame {
                seq = ++_gomokuSeq, round = _gomoku.round, revision = _gomoku.revision, monitorId = screen.desc.id,
                w = shot.width, h = shot.height, fx = shot.fx, fy = shot.fy, cx = shot.cx, cy = shot.cy,
                center = Optical(screen.Origin - shot.position), right = Optical(screen.X), up = Optical(screen.Y),
                widthM = screen.frame.widthM, heightM = screen.frame.heightM, own = own.ToArray()
            };
            var pixels = _gomokuPixels;
            if (_gomokuFlipped == null || _gomokuFlipped.Length != pixels.Length) _gomokuFlipped = new byte[pixels.Length];
            var flipped = _gomokuFlipped;
            _gomokuCapturedAt = now; _gomokuPendingEpoch = _gomokuCaptureEpoch;
            GomokuCameraStatus("ready");
            // Keep the measured top-left image convention after Unity's bottom-up JPEG encoder.
            // A single pending encode owns both buffers until it returns.
            _gomokuEncoding = Task.Run(() => {
                frame.jpeg = GomokuCamera.EncodeJpeg(pixels, flipped, frame.w, frame.h);
                return frame.jpeg != null && frame.jpeg.Length > 100 && frame.jpeg.Length <= 350000 ? frame : null;
            });
        }

    }

    /// <summary>Maps tracked geometry and the corresponding camera pixels into the TV detector's optical coordinates.</summary>
    public static class GomokuCamera
    {
        public static float[] Optical(Quaternion worldToCamera, Vector3 relative)
        {
            Vector3 p = worldToCamera * relative;
            return new[] { p.x, -p.y, p.z };
        }

        public static byte[] EncodeJpeg(byte[] source, byte[] bottomUp, int width, int height)
        {
            int row = width * 4;
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(source, y * row, bottomUp, (height - 1 - y) * row, row);
            return ImageConversion.EncodeArrayToJPG(bottomUp, GraphicsFormat.R8G8B8A8_SRGB, (uint)width, (uint)height, 0, 76);
        }
    }
}
