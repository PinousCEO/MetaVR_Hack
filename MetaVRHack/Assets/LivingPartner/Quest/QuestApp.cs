using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using LivingPartner.Character;
using LivingPartner.MR;
using LivingPartner.Shared;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Hands;

namespace LivingPartner.Quest
{
    /// <summary>
    /// Quest client adapter (Quest 3 / 3S first). Lives on the root object of Scenes/QuestMR.unity, where the XR Origin, the
    /// tracked passthrough camera, the AR Session and the shared MrCore prefab are authored in the hierarchy. It feeds the
    /// shared MrApp with real hand tracking (XR Hands / OpenXR): palm pose, fingertips, a stabilised pointing ray and pinch.
    /// No controller is needed anywhere. Also samples the real passthrough camera (with permission) for grounded,
    /// on-device observations - nothing is sent anywhere except the short text result to the paired PC.
    /// </summary>
    public sealed class QuestApp : MonoBehaviour, IMrInput, IMrCameraPreview, IMrBoardCamera
    {
        public bool Simulated => false;
        public bool HeadTracked { get; private set; } = true;
        public Vector3 HeadPos => _cam.transform.position;
        public Quaternion HeadRot => _cam.transform.rotation;
        public HandState Left => _left;
        public HandState Right => _right;
        public string SpaceEpoch { get; private set; } = "";

        [Header("Scene references (Scenes/QuestMR.unity)")]
        [Tooltip("Main Camera under XR Origin/Camera Offset (TrackedPoseDriver + ARCameraManager, clear alpha 0 for passthrough).")]
        [SerializeField] Camera _cam;
        [Tooltip("The XR Origin transform: hand joints are reported relative to it.")]
        [SerializeField] Transform _origin;
        [Tooltip("The shared MR app on the MrCore prefab instance.")]
        [SerializeField] MrApp _app;
        [Tooltip("On the XR Origin. Switched on once the user has allowed access to spatial data; casts rays against the real room.")]
        [SerializeField] ARRaycastManager _raycasts;
        [Tooltip("On the Main Camera. Shows passthrough, and hands out the camera's pictures (to find the displays).")]
        [SerializeField] ARCameraManager _cameraManager;

        XRHandSubsystem _hands;
        HandState _left, _right;
        bool _lPinch, _rPinch; Vector3 _lRay, _rRay;
        readonly List<XRHandSubsystem> _handList = new List<XRHandSubsystem>();
        readonly List<XRInputSubsystem> _inputList = new List<XRInputSubsystem>();
        bool _originHooked;
        float _originQuietUntil = 6f;      // Time.realtimeSinceStartup until which origin events belong to the session coming up

        // host discovery
        readonly List<string> _discovered = new List<string>();
        Thread _beaconThread; volatile bool _quit; AndroidJavaObject _multicastLock;

        // passthrough camera observation
        bool _cameraWanted = true; string _cameraStatus = "not started"; Observation _pendingObs; byte[] _obsPixels;
        Unity.Collections.NativeArray<byte> _shotNative, _previewNative, _boardNative;
        // where the left passthrough camera sits in the head (read from the device; these are the values of a Quest 3)
        Vector3 _lensPos = new Vector3(-0.0324f, -0.0171f, 0.0632f); Quaternion _lensRot = new Quaternion(0.09908f, 0.00403f, 0.00083f, 0.99507f);
        bool _lensRead, _cameraAsked, _cameraRestarted; int _shots; float _nextShotLog;
        const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
        const string ScenePermission = "com.oculus.permission.USE_SCENE";      // "spatial data": needed to sense real surfaces
        readonly List<ARRaycastHit> _envHits = new List<ARRaycastHit>();
        float _nextSceneCheck; bool _sceneAsked; int _envCasts, _envHitsCount;

        /// <summary>A ray against the real room (depth). The raycast manager only runs once spatial data is allowed.</summary>
        public bool RaycastEnvironment(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point)
        {
            point = default;
            if (_raycasts == null || !_raycasts.enabled) return false;
            _envCasts++;
            _envHits.Clear();
            if (!_raycasts.Raycast(new Ray(origin, direction), _envHits, UnityEngine.XR.ARSubsystems.TrackableType.Depth) || _envHits.Count == 0) return false;
            var h = _envHits[0];
            if (h.distance > maxDistance) return false;
            point = h.pose.position; _envHitsCount++;
            return true;
        }

        void UpdateScenePermission()
        {
            if (_raycasts == null || _raycasts.enabled || Time.realtimeSinceStartup < _nextSceneCheck) return;
            _nextSceneCheck = Time.realtimeSinceStartup + 1.5f;
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission)) { _raycasts.enabled = true; Debug.Log("[Quest] spatial data allowed: sensing real surfaces"); }
            else if (!_sceneAsked && Time.realtimeSinceStartup > 4f) { _sceneAsked = true; Debug.Log("[Quest] asking for access to spatial data (to find the desk)"); UnityEngine.Android.Permission.RequestUserPermission(ScenePermission); }
        }

        void Awake()
        {
            Application.targetFrameRate = -1;
            SpaceEpoch = PlayerPrefs.GetString("lp.spaceEpoch", "");
            if (SpaceEpoch.Length == 0) NewEpoch("first run");

            // the shared MR app (MrCore prefab instance in this scene) gets its input from this adapter
            _app.Input = this; _app.Cam = _cam; _app.Role = Roles.Quest;
            _app.DisplayLatencyMs = 30f;   // starting estimate only; NOT measured on a headset yet (lpctl debug mrLatency <ms> to tune)
            _app.LoadToken = () => PlayerPrefs.GetString("lp.token", "");
            _app.SaveToken = t => { PlayerPrefs.SetString("lp.token", t); PlayerPrefs.Save(); };
            _app.UrlProvider = Urls;
            _app.OfferLocal = true;      // no paired PC found within a few seconds: "Play now" (the local world) or "Connect my PC" (MrLocalHost)
            Debug.Log($"[Quest] started: space epoch {SpaceEpoch}, paired token stored={PlayerPrefs.GetString("lp.token", "").Length > 0}, last host '{PlayerPrefs.GetString("lp.lastUrl", "")}'");
            StartBeaconListener();
            StartCoroutine(CameraLoop());
        }

        void OnDestroy()
        {
            _quit = true;
            try { _multicastLock?.Call("release"); } catch { }
            if (_shotNative.IsCreated) _shotNative.Dispose();
            if (_previewNative.IsCreated) _previewNative.Dispose();
            if (_boardNative.IsCreated) _boardNative.Dispose();
        }

        /// <summary>
        /// The runtime announces the tracking origin whenever a session starts or resumes (on a Quest 3: three times in
        /// the first two seconds). That is the same room in the same coordinates, so saved calibrations stay valid.
        /// Only an announcement in the middle of a running session - the user recentred, or the boundary was redrawn -
        /// starts a new epoch. If the room really is laid out differently after a restart, the monitor outline shows
        /// it (it no longer sits on the screen) and the user calibrates again.
        /// </summary>
        void OnOriginUpdated()
        {
            if (Time.realtimeSinceStartup < _originQuietUntil) { Debug.Log("[Quest] tracking origin announced as the session comes up: calibrations kept (epoch " + SpaceEpoch + ")"); return; }
            NewEpoch("tracking origin updated (recentre / boundary change)");
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) _originQuietUntil = Time.realtimeSinceStartup + 6f;      // the headset was put back on
            Debug.Log("[Quest] " + (paused ? "paused (headset off or another app)" : "resumed"));
            if (_app != null) _app.OnHeadsetPause(paused);
        }

        void NewEpoch(string why)
        {
            SpaceEpoch = Guid.NewGuid().ToString("N").Substring(0, 12);
            PlayerPrefs.SetString("lp.spaceEpoch", SpaceEpoch); PlayerPrefs.Save();
            Debug.Log($"[Quest] tracking space epoch -> {SpaceEpoch} ({why}); saved monitor calibrations must be re-verified");
        }

        // ------------------------------------------------------------------ host discovery
        List<string> Urls()
        {
            var list = new List<string>();
            lock (_discovered) list.AddRange(_discovered);
            string last = _lastUrl;
            if (!string.IsNullOrEmpty(last) && !list.Contains(last)) list.Add(last);
            list.Add($"ws://127.0.0.1:{Proto.DefaultPort}/ws");   // USB development: adb reverse tcp:47610 tcp:47610
            return list;
        }
        string _lastUrl = "";

        void StartBeaconListener()
        {
            _lastUrl = PlayerPrefs.GetString("lp.lastUrl", "");
            try
            {
                // Android drops broadcast packets for apps that do not hold a multicast lock
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    _multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "living-partner");
                    _multicastLock.Call("acquire");
                }
            }
            catch (Exception e) { Debug.Log("[Quest] multicast lock unavailable: " + e.Message); }
            _beaconThread = new Thread(() =>
            {
                try
                {
                    using (var udp = new UdpClient(new IPEndPoint(IPAddress.Any, Proto.BeaconPort)))
                    {
                        udp.Client.ReceiveTimeout = 1000;
                        while (!_quit)
                        {
                            try
                            {
                                IPEndPoint from = null;
                                var bytes = udp.Receive(ref from);
                                var parts = Encoding.UTF8.GetString(bytes).Split(' ');
                                if (parts.Length >= 2 && parts[0] == Proto.BeaconMagic && int.TryParse(parts[1], out int port))
                                {
                                    string url = $"ws://{from.Address}:{port}/ws";
                                    lock (_discovered) if (!_discovered.Contains(url)) _discovered.Insert(0, url);
                                }
                            }
                            catch (SocketException) { }
                        }
                    }
                }
                catch (Exception e) { Debug.Log("[Quest] beacon listener stopped: " + e.Message); }
            }) { IsBackground = true, Name = "lp-beacon" };
            _beaconThread.Start();
        }

        // ------------------------------------------------------------------ per frame
        public void Tick(float dt)
        {
            if (_app.Link != null && _app.Link.Connected && _app.Link.CurrentUrl != _lastUrl && !_app.Link.CurrentUrl.Contains("127.0.0.1"))
            { _lastUrl = _app.Link.CurrentUrl; PlayerPrefs.SetString("lp.lastUrl", _lastUrl); PlayerPrefs.Save(); }

            UpdateScenePermission();
            if (!_originHooked)
            {
                SubsystemManager.GetSubsystems(_inputList);
                foreach (var s in _inputList) { s.trackingOriginUpdated += _ => OnOriginUpdated(); _originHooked = true; }
            }
            var headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (headDevice.isValid && headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked)) HeadTracked = tracked;

            if (_hands == null || !_hands.running)
            {
                SubsystemManager.GetSubsystems(_handList);
                _hands = null;
                foreach (var h in _handList) if (h.running) { _hands = h; break; }
            }
            if (_hands == null) { _left = default; _right = default; return; }
            _left = Read(_hands.leftHand, -1f, ref _lPinch, ref _lRay, dt);
            _right = Read(_hands.rightHand, 1f, ref _rPinch, ref _rRay, dt);
        }

        bool Joint(XRHand hand, XRHandJointID id, out Vector3 pos, out Quaternion rot)
        {
            var j = hand.GetJoint(id);
            if (j.TryGetPose(out UnityEngine.Pose p)) { pos = _origin.TransformPoint(p.position); rot = _origin.rotation * p.rotation; return true; }
            pos = default; rot = Quaternion.identity; return false;
        }

        /// <summary>A finger counts as stretched when its tip is this many times further from the wrist than its own knuckle.</summary>
        const float StretchRatio = 1.40f;

        bool Stretched(XRHand hand, XRHandJointID knuckleId, XRHandJointID tipId, Vector3 wrist)
        {
            if (!Joint(hand, knuckleId, out Vector3 knuckle, out _) || !Joint(hand, tipId, out Vector3 tip, out _)) return false;
            float toKnuckle = Vector3.Distance(wrist, knuckle);
            // (1.40: at 1.55 a hand held open but relaxed - the fingers a little bent, as an open hand is - read as curled, and the menu on the palm did not come up)
            return toKnuckle > 0.02f && Vector3.Distance(wrist, tip) > toKnuckle * StretchRatio;
        }

        HandState Read(XRHand hand, float side, ref bool pinchHeld, ref Vector3 rayDir, float dt)
        {
            var s = new HandState();
            if (!hand.isTracked) { pinchHeld = false; return s; }
            if (!Joint(hand, XRHandJointID.Palm, out Vector3 palm, out Quaternion palmRot)) return s;
            if (!Joint(hand, XRHandJointID.IndexTip, out Vector3 indexTip, out _)) return s;
            if (!Joint(hand, XRHandJointID.ThumbTip, out Vector3 thumbTip, out _)) return s;
            // (no wrist: not a hand that can be read - with the wrist at (0, 0, 0) every finger read as curled and the hand as a fist)
            if (!Joint(hand, XRHandJointID.Wrist, out Vector3 wrist, out _)) { pinchHeld = false; return s; }
            if (!Joint(hand, XRHandJointID.IndexProximal, out Vector3 knuckle, out _)) knuckle = palm;
            s.tracked = true;
            s.palmPos = palm;
            s.palmNormal = -(palmRot * Vector3.up);          // palm joint: +Y leaves the back of the hand, so the palm faces -Y
            s.palmForward = palmRot * Vector3.forward;       // towards the fingers
            s.indexTip = indexTip; s.thumbTip = thumbTip; s.wrist = wrist;
            // which fingers are stretched out (for rock-paper-scissors): a stretched finger's tip is far from the wrist
            // compared with its own knuckle, a curled one is not
            s.fingersKnown = true;
            Joint(hand, XRHandJointID.MiddleTip, out s.middleTip, out _); Joint(hand, XRHandJointID.RingTip, out s.ringTip, out _); Joint(hand, XRHandJointID.LittleTip, out s.littleTip, out _);
            if (Stretched(hand, XRHandJointID.IndexProximal, XRHandJointID.IndexTip, wrist)) s.fingers |= 2;
            if (Stretched(hand, XRHandJointID.MiddleProximal, XRHandJointID.MiddleTip, wrist)) s.fingers |= 4;
            if (Stretched(hand, XRHandJointID.RingProximal, XRHandJointID.RingTip, wrist)) s.fingers |= 8;
            if (Stretched(hand, XRHandJointID.LittleProximal, XRHandJointID.LittleTip, wrist)) s.fingers |= 16;

            float d = Vector3.Distance(indexTip, thumbTip);
            bool pinch = pinchHeld ? d < 0.032f : d < 0.018f;   // hysteresis so a held pinch does not flicker
            s.pinch = pinch; s.pinchDown = pinch && !pinchHeld; s.pinchStrength = 1f - Mathf.Clamp01((d - 0.015f) / 0.06f);
            pinchHeld = pinch;

            // Pointing ray. "It cannot see where I point" (2026-11-17): the ray used to run from an ESTIMATED shoulder
            // through the knuckle, whatever the hand was doing - a line nobody can see or feel. Now it is what the hand
            // itself shows:
            //   1  an index finger held out by itself (the fingers beside it curled): along that finger, from its
            //      knuckle through its tip, starting at the tip;
            //   2  any other hand: the system's own pointer pose (Meta's aim pose - the ray its interface draws);
            //   3  the shoulder line as before, only when the system gives no pointer pose.
            // The direction is smoothed and is held still while thumb and finger close for a pinch (PointingRay.Step).
            // Whether a hand is pointing AT ALL is decided by the app (MrApp.HandPoints), which also draws the ray.
            bool fingerOut = (s.fingers & 2) != 0 && (s.fingers & 4) == 0 && (s.fingers & 8) == 0;
            System.Numerics.Vector3 want; Vector3 origin; int kind;
            if (fingerOut && (indexTip - knuckle).sqrMagnitude > 0.03f * 0.03f) { want = Conv.N((indexTip - knuckle).normalized); origin = indexTip; kind = 1; }
            else if (AimPose(side, out Vector3 aimPos, out Vector3 aimDir)) { want = Conv.N(aimDir); origin = aimPos; kind = 2; }
            else
            {
                var shoulder = PointingRay.Shoulder(Conv.N(_cam.transform.position), _cam.transform.eulerAngles.y, side);
                want = PointingRay.Aim(shoulder, Conv.N(knuckle), Conv.N(indexTip), Conv.N(s.palmForward)); origin = knuckle; kind = 3;
            }
            rayDir = Conv.U(PointingRay.Step(Conv.N(rayDir), want, d, dt));
            s.rayOrigin = origin; s.rayDir = rayDir; s.rayKind = kind;
            int slot = side < 0f ? 0 : 1;
            if (_rayKindWas[slot] != kind && Time.realtimeSinceStartup >= _rayKindLogAt[slot])
            {
                _rayKindWas[slot] = kind; _rayKindLogAt[slot] = Time.realtimeSinceStartup + 2f;      // (not a line for every flicker between two kinds)
                Debug.Log($"[Quest] pointing ray of the {(side < 0f ? "left" : "right")} hand: {(kind == 1 ? "along the index finger (held out by itself)" : kind == 2 ? "the system's pointer pose" : "shoulder through knuckle (the system gives no pointer pose)")}");
            }
            return s;
        }
        readonly int[] _rayKindWas = new int[2]; readonly float[] _rayKindLogAt = new float[2];

        /// <summary>The system's pointer pose of a hand (Meta Hand Tracking Aim), in the room. False when the system has none for it just now.</summary>
        bool AimPose(float side, out Vector3 position, out Vector3 direction)
        {
            position = default; direction = default;
            var aim = side < 0f ? MetaAimHand.left : MetaAimHand.right;
            if (aim == null || !aim.added) return false;
            var flags = (MetaAimFlags)aim.aimFlags.ReadValue();
            if ((flags & MetaAimFlags.Valid) == 0) return false;
            Quaternion rot = aim.deviceRotation.ReadValue();
            if (rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f) return false;
            position = _origin.TransformPoint(aim.devicePosition.ReadValue());
            direction = (_origin.rotation * rot) * Vector3.forward;
            return direction.sqrMagnitude > 0.5f;
        }

        // ------------------------------------------------------------------ the headset's camera
        public void SetCameraEnabled(bool on)
        {
            if (_cameraWanted && !on) { _cameraStatus = "observations stopped by the user"; Debug.Log("[Quest] camera observations stopped (user setting)"); }
            _cameraWanted = on;
        }

        public Observation PollObservation() { var o = _pendingObs; _pendingObs = null; return o; }

        public string Diagnostics => $"hands sub={(_hands != null ? "ok" : "none")} L={(_left.tracked ? (_left.pinch ? "pinch" : _left.palmUp ? "palmUp" : "ok") : "-")} R={(_right.tracked ? (_right.pinch ? "pinch" : _right.palmUp ? "palmUp" : "ok") : "-")} head={(HeadTracked ? "ok" : "lost")}\ncam={_cameraStatus} epoch={SpaceEpoch}";

        /// <summary>
        /// Where the camera sits in the head. Android describes every camera of the device; the passthrough cameras of a
        /// Quest carry their place and direction there (translation and rotation of the lens).
        /// </summary>
        void ReadLensPose()
        {
            _lensRead = true;
            string from = "typical Quest 3 values (the device did not say)";
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var manager = activity.Call<AndroidJavaObject>("getSystemService", "camera"))
                using (var keys = new AndroidJavaClass("android.hardware.camera2.CameraCharacteristics"))
                using (var keyT = keys.GetStatic<AndroidJavaObject>("LENS_POSE_TRANSLATION"))
                using (var keyR = keys.GetStatic<AndroidJavaObject>("LENS_POSE_ROTATION"))
                {
                    string[] ids = manager.Call<string[]>("getCameraIdList");
                    foreach (string id in ids)
                    {
                        using (var ch = manager.Call<AndroidJavaObject>("getCameraCharacteristics", id))
                        {
                            float[] t = ch.Call<float[]>("get", keyT), r = ch.Call<float[]>("get", keyR);
                            if (t == null || r == null || t.Length < 3 || r.Length < 4) continue;
                            Debug.Log($"[Quest] camera {id}: lens at ({t[0]:0.0000}, {t[1]:0.0000}, {t[2]:0.0000}), turned ({r[0]:0.00000}, {r[1]:0.00000}, {r[2]:0.00000}, {r[3]:0.00000})");
                            if (t[0] >= 0f || from.StartsWith("camera")) continue;      // the left one is the one whose pictures arrive
                            _lensPos = new Vector3(t[0], t[1], -t[2]);
                            _lensRot = Quaternion.Inverse(new Quaternion(-r[0], -r[1], r[2], r[3])) * Quaternion.Euler(180f, 0f, 0f);
                            from = "camera " + id;
                        }
                    }
                }
            }
            catch (Exception e) { Debug.Log("[Quest] the camera's place in the head could not be read: " + e.Message); }
            Debug.Log($"[Quest] camera in the head: at {_lensPos.ToString("0.0000")}, looking {(_lensRot * Vector3.forward).ToString("0.000")} ({from})");
        }

        /// <summary>The newest picture of the left passthrough camera, as bytes, and where the camera was.</summary>
        public bool CameraFrame(ref byte[] rgba, out CameraShot shot)
            => ReadCamera(ref rgba, out shot, 0, ref _shotNative);

        public bool CameraPreview(ref byte[] rgba, out CameraShot shot)
            => ReadCamera(ref rgba, out shot, 256, ref _previewNative);

        public bool BoardCameraFrame(ref byte[] rgba, out CameraShot shot)
            => ReadCamera(ref rgba, out shot, 768, ref _boardNative);

        public bool HasCameraPermission => UnityEngine.Android.Permission.HasUserAuthorizedPermission(HeadsetCameraPermission)
            || UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera);

        bool ReadCamera(ref byte[] rgba, out CameraShot shot, int maxWidth, ref Unity.Collections.NativeArray<byte> pixels)
        {
            shot = default;
            if (!HasCameraPermission)
            {
                _cameraStatus = "headset camera permission not granted";
                if (!_cameraAsked) { _cameraAsked = true; Debug.Log("[Quest] asking for the headset camera (to find the displays)"); UnityEngine.Android.Permission.RequestUserPermissions(new[] { HeadsetCameraPermission, UnityEngine.Android.Permission.Camera }); }
                return false;
            }
            if (_cameraManager == null || _cameraManager.subsystem == null || !_cameraManager.subsystem.running) { _cameraStatus = "camera not running"; return false; }
            if (!_lensRead) ReadLensPose();
            if (!_cameraManager.TryAcquireLatestCpuImage(out UnityEngine.XR.ARSubsystems.XRCpuImage image))
            {
                _cameraStatus = "no picture from the camera";
                // allowed only after the camera was started (the permission dialog): it has to be started again, once
                if (!_cameraRestarted && Time.realtimeSinceStartup > 8f) { _cameraRestarted = true; Debug.Log("[Quest] no camera pictures: starting the camera again"); StartCoroutine(RestartCamera()); }
                return false;
            }
            using (image)
            {
                var conv = new UnityEngine.XR.ARSubsystems.XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, UnityEngine.XR.ARSubsystems.XRCpuImage.Transformation.None);
                if (maxWidth > 0 && image.width > maxWidth)
                    conv.outputDimensions = new Vector2Int(maxWidth, Mathf.Max(1, Mathf.RoundToInt((float)maxWidth * image.height / image.width)));
                int size = image.GetConvertedDataSize(conv);
                if (size != conv.outputDimensions.x * conv.outputDimensions.y * 4) { _cameraStatus = $"unexpected picture size {size} for {image.width}x{image.height}"; return false; }
                if (!pixels.IsCreated || pixels.Length != size) { if (pixels.IsCreated) pixels.Dispose(); pixels = new Unity.Collections.NativeArray<byte>(size, Unity.Collections.Allocator.Persistent); }
                if (rgba == null || rgba.Length != size) rgba = new byte[size];
                image.Convert(conv, pixels);
                pixels.CopyTo(rgba);
                shot.width = conv.outputDimensions.x; shot.height = conv.outputDimensions.y;
                shot.timestamp = image.timestamp;
                // the lens: from the camera itself when it says so, otherwise the known values of this camera
                float fx = 865.65f, fy = 865.65f, cx = 643.28f, cy = 640.90f, rw = 1280f, rh = 1280f; string from = "known values";
                if (_cameraManager.TryGetIntrinsics(out UnityEngine.XR.ARSubsystems.XRCameraIntrinsics k) && k.focalLength.x > 100f && k.resolution.x > 0)
                { fx = k.focalLength.x; fy = k.focalLength.y; cx = k.principalPoint.x; cy = k.principalPoint.y; rw = k.resolution.x; rh = k.resolution.y; from = "from the camera"; }
                // a picture that is a centred cut of, or a scaled, sensor picture
                float scale = image.width / rw;
                shot.fx = fx * scale; shot.fy = fy * scale;
                shot.cx = cx * scale; shot.cy = cy * scale - (rh * scale - image.height) * 0.5f;
                shot.fx *= (float)shot.width / image.width; shot.cx *= (float)shot.width / image.width;
                shot.fy *= (float)shot.height / image.height; shot.cy *= (float)shot.height / image.height;
                shot.position = _cam.transform.TransformPoint(_lensPos);
                shot.rotation = _cam.transform.rotation * _lensRot;
                _shots++;
                _cameraStatus = $"pictures {image.width}x{image.height}";
                if (Time.realtimeSinceStartup > _nextShotLog)
                {
                    _nextShotLog = Time.realtimeSinceStartup + 20f;
                    Debug.Log($"[Quest] camera picture {image.width}x{image.height} {image.format}, lens f=({shot.fx:0.0}, {shot.fy:0.0}) c=({shot.cx:0.0}, {shot.cy:0.0}) ({from}, for {rw:0}x{rh:0}), {_shots} so far");
                }
            }
            return true;
        }

        IEnumerator RestartCamera()
        {
            _cameraManager.enabled = false;
            yield return null;
            _cameraManager.enabled = true;
        }

        IEnumerator CameraLoop()
        {
            yield return new WaitForSeconds(12f);
            while (true)
            {
                // a look at the room now and then, for a remark about it (nothing leaves the headset but that remark)
                // Only with a PC that is connected and paired: the camera is for finding the PC's screens. Playing without the
                // PC - or while the card with "Play now" is still up - the camera is never asked for (CameraFrame asks).
                bool withPc = _app != null && _app.Link != null && _app.Link.Connected && !_app.Link.NeedsPairing && !_app.LocalMode;
                if (_cameraWanted && withPc && !_app.FindingScreens && CameraFrame(ref _obsPixels, out CameraShot shot)) Analyse(_obsPixels, shot.width, shot.height);
                yield return new WaitForSeconds(25f);
            }
        }

        /// <summary>Very small, honest scene statistics from a real camera frame. Only confident, mild statements are produced.</summary>
        void Analyse(byte[] px, int w, int h)
        {
            if (w < 32 || h < 32) return;
            double lum = 0; int n = 0; int edges = 0, edgeN = 0;
            int step = Mathf.Max(2, w / 160);
            for (int y = 0; y < h; y += step)
            {
                int prev = -1;
                for (int x = 0; x < w; x += step)
                {
                    int o = (y * w + x) * 4;
                    int l = (px[o] * 54 + px[o + 1] * 183 + px[o + 2] * 19) >> 8;
                    lum += l; n++;
                    if (y > h * 0.45f)          // the rows start at the top: this is the lower part of the view (desk area)
                    {
                        if (prev >= 0) { edgeN++; if (Mathf.Abs(l - prev) > 26) edges++; }
                        prev = l;
                    }
                }
            }
            float mean = (float)(lum / Math.Max(1, n)) / 255f;
            float edge = edgeN > 0 ? edges / (float)edgeN : 0f;
            string detail = $"meanLuma={mean:0.000} lowerEdgeDensity={edge:0.000} frame={w}x{h} device=passthrough-left";
            double t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            Debug.Log("[Quest] camera frame stats " + detail);
            if (mean < 0.16f)
                _pendingObs = new Observation { source = "passthrough-camera", t = t, confidence = Mathf.Clamp(0.55f + (0.16f - mean) * 3f, 0.55f, 0.9f), fact = "It looks rather dark around you. How about a little more light, if you like?", detail = detail };
            else if (edge > 0.24f)
                _pendingObs = new Observation { source = "passthrough-camera", t = t, confidence = Mathf.Clamp(0.5f + (edge - 0.24f) * 1.5f, 0.5f, 0.8f), fact = "There seem to be quite a few things on the desk. Shall we tidy up together later, if you like?", detail = detail };
        }
    }

}
