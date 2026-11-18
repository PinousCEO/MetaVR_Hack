using System;
using System.Threading.Tasks;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// THE PARTNER'S EYES - "when I SHOW some real object, Nilo makes it": the host asks for ONE picture of what the
    /// user is holding up (Msg.EyeRequest), and this takes it from the headset's passthrough camera and sends it back
    /// (Msg.EyeFrame). The host looks at it with its vision model and sculpts the thing (Companion Sight.cs).
    ///
    ///  - The camera is the one the app already reads to find the displays (IMrInput.CameraFrame: the left passthrough
    ///    camera through AR Foundation's camera image, permission horizonos.permission.HEADSET_CAMERA with
    ///    android.permission.CAMERA, both in the manifest). No other SDK is used.
    ///  - The permission is asked for NOW, at the first use, never when the app starts. While the user reads the
    ///    system's question the app is paused; when it comes back the picture is taken.
    ///  - While a picture is wanted a small sign "Nilo is looking" stands beside the partner, with a light that
    ///    pulses. It goes out as soon as the one picture is taken, and no further picture is read for this.
    ///  - The full camera view travels with the projected finger or head ray; its longest side is at most what the host asked for (1280).
    ///  - No camera (not allowed, an older system, no pictures, the desktop simulator): the host is told why, and the
    ///    partner says so and asks the user to describe the thing instead.
    /// The device log shows every step as "[MR] eyes: ...".
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Looking at what the user holds up (MrEyes; authored by LPSceneEyes)")]
        [SerializeField] StoryLabel _eyeLabel;
        [SerializeField] FxQuad _eyeBg;
        [SerializeField] FxQuad _eyeDot;

        const string EyeCameraPermission = "horizonos.permission.HEADSET_CAMERA";
        const float EyeSettle = 0.8f;           // the sign is up this long before the picture is taken: time to hold the thing still
        const float EyeWaitAllowed = 8f;        // allowed, and still no picture after this: there is no camera to be had
        const float EyeWaitQuestion = 20f;      // not allowed (yet): the user may be answering the system's question

        EyeRequest _eyeAsk;                     // a picture is wanted
        float _eyeSince, _eyeNextTry, _eyeShown, _eyeFakeUntil = -1f, _eyeAllowedSince = -1f;
        bool _eyePermissionAsked, _eyeEncoding;
        byte[] _eyeBuf;
        EyeFrame _eyeReady;                     // encoded on another thread, sent from Update
        string _eyeReadyLog = "";
        readonly object _eyeLock = new object();

        void OnEyeRequest(EyeRequest r)
        {
            if (r == null || string.IsNullOrEmpty(r.id)) return;
            _eyeAsk = r; _eyeSince = Time.time; _eyeNextTry = Time.time + EyeSettle; _eyePermissionAsked = false; _eyeAllowedSince = -1f;
            Debug.Log($"[MR] eyes: asked for one picture ({r.id}, at most {r.maxSide} px): camera {(Input.Simulated ? "none (simulator)" : EyeAllowed() ? "allowed" : "not allowed yet")}");
            Sfx2D("select", 0.35f);
        }

        static bool EyeAllowed()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(EyeCameraPermission) || UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera);
#else
            return true;
#endif
        }

        /// <summary>Called once a frame (MrUi): takes the picture when one is wanted, sends what was encoded, shows the sign.</summary>
        void UpdateEyes(float dt)
        {
            // what the other thread finished: sent from here
            EyeFrame ready = null; string readyLog = null;
            lock (_eyeLock) { if (_eyeReady != null) { ready = _eyeReady; readyLog = _eyeReadyLog; _eyeReady = null; } }
            if (ready != null)
            {
                _eyeEncoding = false;
                _link.Send(Msg.EyeFrame, ready);
                Debug.Log("[MR] eyes: " + readyLog + (ready.ok ? "; sent - the camera is not read any more" : ""));
            }

            if (_eyeAsk != null && !_eyeEncoding && Time.time >= _eyeNextTry)
            {
                _eyeNextTry = Time.time + 0.25f;
                float waited = Time.time - _eyeSince;
                if (Input.Simulated) { if (waited > 1.4f) EyeNone("simulator", "the desktop simulator has no camera"); }
                else if (!EyeAllowed())
                {
                    // asked for here, at the moment it is first needed (the app's own start never asks)
                    if (!_eyePermissionAsked)
                    {
                        _eyePermissionAsked = true;
                        Debug.Log("[MR] eyes: asking for the headset camera (first use) - the system shows its question now");
#if UNITY_ANDROID && !UNITY_EDITOR
                        UnityEngine.Android.Permission.RequestUserPermissions(new[] { EyeCameraPermission, UnityEngine.Android.Permission.Camera });
#endif
                    }
                    else if (waited > EyeWaitQuestion) EyeNone("denied", "the headset camera is not allowed");
                }
                else
                {
                    if (_eyeAllowedSince < 0f) _eyeAllowedSince = Time.time;
                    if (Input.CameraFrame(ref _eyeBuf, out CameraShot shot) && shot.width > 0 && shot.height > 0) EyeTake(shot);
                    else if (Time.time - _eyeAllowedSince > EyeWaitAllowed) EyeNone("nocamera", "allowed, but no picture arrives (" + Input.Diagnostics.Replace("\n", " ") + ")");
                }
            }
            DrawEyeSign(dt);
        }

        void EyeNone(string why, string log)
        {
            var ask = _eyeAsk; _eyeAsk = null;
            if (ask == null) return;
            Debug.Log($"[MR] eyes: no picture: {why} ({log}) - the host is told, the partner asks for a description instead");
            _link.Send(Msg.EyeFrame, new EyeFrame { id = ask.id, ok = false, why = why });
        }

        /// <summary>The one picture: the room view and target ray, encoded on another thread (the frame rate must not notice), sent from Update.</summary>
        void EyeTake(CameraShot shot)
        {
            var ask = _eyeAsk; _eyeAsk = null;
            Vector3 origin = Input.HeadPos, direction = Input.HeadRot * Vector3.forward;
            bool pointed = false;
            for (int i = 0; i < 2; i++)
            {
                var hand = i == 0 ? Input.Right : Input.Left;
                if (!hand.tracked || hand.rayKind != 1 || hand.rayDir.sqrMagnitude < .5f) continue;
                origin = hand.rayOrigin; direction = hand.rayDir.normalized;
                pointed = true;
                break;
            }
            if (!Input.RaycastEnvironment(origin, direction, 5f, out Vector3 hit))
                hit = origin + direction * (pointed ? .8f : 1.5f);
            Vector3 local = Quaternion.Inverse(shot.rotation) * (hit - shot.position);
            float aimX = .5f, aimY = .5f;
            if (EyeMath.Project(local.x, local.y, local.z, shot.fx, shot.fy, shot.cx, shot.cy, out float px, out float py))
            {
                aimX = Mathf.Clamp01(px / shot.width); aimY = Mathf.Clamp01(py / shot.height);
            }
            int maxSide = Mathf.Clamp(ask.maxSide, 256, 1280), iw = shot.width, ih = shot.height;
            byte[] rgba = _eyeBuf; _eyeBuf = null;      // (handed to the other thread: the next look takes a buffer of its own)
            float took = Time.time - _eyeSince;
            string id = ask.id;
            _eyeEncoding = true;
            Sfx2D("select", 0.5f, 1.3f);
            Task.Run(() =>
            {
                EyeFrame f; string log;
                try
                {
                    byte[] cut = EyeMath.Cut(rgba, iw, ih, 0, 0, iw, ih, maxSide, out int cw, out int ch);
                    byte[] jpg = cut == null ? null : ImageConversion.EncodeArrayToJPG(cut, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, (uint)cw, (uint)ch, 0, 82);
                    if (jpg == null || jpg.Length < 200) { f = new EyeFrame { id = id, ok = false, why = "nocamera" }; log = "the picture could not be made a JPEG"; }
                    else
                    {
                        f = new EyeFrame { id = id, ok = true, jpeg = Convert.ToBase64String(jpg), width = cw, height = ch, aimX = aimX, aimY = aimY, pointed = pointed };
                        log = $"room view {iw}x{ih} -> {cw}x{ch}; {(pointed ? "finger" : "head")} target ({aimX:F2},{aimY:F2}); JPEG {jpg.Length / 1024} KB";
                    }
                }
                catch (Exception e) { f = new EyeFrame { id = id, ok = false, why = "nocamera" }; log = "the picture could not be made a JPEG: " + e.Message; }
                lock (_eyeLock) { _eyeReady = f; _eyeReadyLog = log; }
            });
        }

        /// <summary>The sign beside the partner (in front of the user when the partner is inside the PC) for as long as a picture is wanted: "Nilo is looking" and a pulsing light.</summary>
        void DrawEyeSign(float dt)
        {
            if (_eyeLabel == null || _eyeBg == null || _eyeDot == null) return;
            bool on = _eyeAsk != null || _eyeEncoding || Time.time < _eyeFakeUntil;
            _eyeShown = Mathf.MoveTowards(_eyeShown, on ? 1f : 0f, dt / (on ? 0.16f : 0.22f));
            if (_eyeShown <= 0.001f)
            {
                if (_eyeLabel.text.Length > 0) _eyeLabel.text = "";
                if (_eyeBg.enabled) _eyeBg.enabled = false;
                if (_eyeDot.enabled) _eyeDot.enabled = false;
                return;
            }
            Vector3 head = Input.HeadPos, at;
            if (_ch.owner == Env.Mr && _rig.Visible)
            {
                Vector3 away = _rig.HeadWorld - head; away.y = 0f;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
                at = _rig.HeadWorld + Vector3.Cross(Vector3.up, away) * 0.105f + Vector3.up * 0.012f;
            }
            else
            {
                Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
                at = head + fwd * 0.45f + Vector3.down * 0.16f;
            }
            float pop = Mathf.Max(0.001f, Ui.Pop(_eyeShown));
            _eyeLabel.transform.localScale = Vector3.one * pop;
            Place(_eyeLabel, at, "Nilo is looking");
            float width = _eyeLabel.Width + 0.040f, height = _eyeLabel.Height + 0.012f;
            _eyeBg.enabled = true;
            _eyeBg.transform.SetPositionAndRotation(at + _eyeLabel.transform.forward * 0.002f - _eyeLabel.transform.right * 0.008f * pop, _eyeLabel.transform.rotation);
            Ui.SizePanel(_eyeBg, width * pop, height * pop, height * 0.5f * pop, 0.0011f);
            // the light at the left end of the sign: it pulses while the camera is being read
            _eyeDot.enabled = true;
            float pulse = 0.82f + 0.18f * Mathf.Sin(Time.time * 7.5f);
            _eyeDot.transform.SetPositionAndRotation(at - _eyeLabel.transform.right * (_eyeLabel.Width * 0.5f + 0.012f) * pop - _eyeLabel.transform.forward * 0.001f, _eyeLabel.transform.rotation);
            _eyeDot.transform.localScale = Vector3.one * (0.011f * pulse * pop);
        }

        /// <summary>The PC is gone: nothing is wanted any more.</summary>
        void EyesHostLost() { _eyeAsk = null; DrawEyeSign(Time.deltaTime); }

        /// <summary>
        /// lpctl debug eyeLook [seconds]: the sign alone for that long (a check of how it looks; no picture is taken).
        /// lpctl debug eyeCut: the cut-and-encode path on a made-up picture (colour bars), sent to the log - not to the host.
        /// </summary>
        bool OnDebugEyes(DebugCommand d)
        {
            if (d.cmd == "eyeLook") { _eyeFakeUntil = Time.time + (d.value > 0.2f ? d.value : 6f); Debug.Log("[MR] eyes: the sign is shown for a check (no picture is taken)"); return true; }
            if (d.cmd == "eyeCut")
            {
                const int W = 640, H = 480;
                var img = new byte[W * H * 4];
                for (int r = 0; r < H; r++) for (int c = 0; c < W; c++) { int o = (r * W + c) * 4; img[o] = (byte)(c * 255 / W); img[o + 1] = (byte)(r * 255 / H); img[o + 2] = (byte)(r < H / 2 ? 255 : 0); img[o + 3] = 255; }
                var box = new EyeMath.Box(); box.Add(300, 200); box.Add(360, 260);
                EyeMath.Crop(W, H, box, out int x, out int y, out int w, out int h, 200);
                byte[] cut = EyeMath.Cut(img, W, H, x, y, w, h, 1280, out int cw, out int ch);
                byte[] jpg = ImageConversion.EncodeArrayToJPG(cut, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, (uint)cw, (uint)ch, 0, 82);
                string path = string.IsNullOrEmpty(d.text) ? "" : d.text;
                if (path.Length > 0) try { System.IO.File.WriteAllBytes(path, jpg); } catch (Exception e) { Debug.Log("[MR] eyes: " + e.Message); }
                Debug.Log($"[MR] eyes: check: a made-up picture {W}x{H} cut to {cw}x{ch} at ({x}, {y}), JPEG {jpg.Length} bytes{(path.Length > 0 ? " -> " + path : "")}");
                return true;
            }
            return false;
        }
    }
}
