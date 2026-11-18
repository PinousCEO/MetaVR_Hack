using System;
using System.Collections.Generic;
using System.IO;
using LivingPartner.Character;
using LivingPartner.MR;
using LivingPartner.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LivingPartner.Windows
{
    /// <summary>
    /// DESKTOP MR SIMULATOR (development/verification tool, NOT a Quest). It runs the very same MrApp code as the headset,
    /// with a virtual head and scripted/mouse-driven hands, so protocol, state machine, clipping and timing can be exercised
    /// against the real Windows character client and the real host. Everything it reports is flagged simulated.
    /// Mouse: move = right-hand pointing ray, left button = pinch. Keys: 1 offer hand, 2 umbrella, 3 hand beside, 4 hide hand.
    /// </summary>
    public sealed class MrSim : MonoBehaviour, IMrInput
    {
        public bool Simulated => true;
        public bool HeadTracked => _tracked;
        public Vector3 HeadPos => _cam.transform.position;
        public Quaternion HeadRot => _cam.transform.rotation;
        public HandState Left => _left;
        public HandState Right => _right;
        public string SpaceEpoch => "sim-space-1";
        public Observation PollObservation() => null;   // the simulator has no camera: it never fabricates observations
        public void SetCameraEnabled(bool on) { }
        public bool RaycastEnvironment(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point) { point = default; return false; }
        public bool CameraFrame(ref byte[] rgba, out CameraShot shot) { shot = default; return false; }      // the simulator has no camera   // the simulator has no room to sense
        public string Diagnostics => "simulator hand=" + _handMode + (Time.time < _pointUntil ? " pointing " + _pointMon : "");
        string _pointMon = ""; float _pointU, _pointV, _pointUntil; int _pinchFrames; bool _pinchWas;
        float _pinchPartnerUntil, _pinchPartnerAt;
        Vector3 _touchPos; float _touchUntil, _touchPinchAt;

        [Header("Scene references (Scenes/MrSimulator.unity)")]
        [SerializeField] Camera _cam;
        [SerializeField] MrApp _app;
        [SerializeField] FxQuad _handVis;
        [Tooltip("The scripted right index finger, while it is at the menu on the left palm.")]
        [SerializeField] FxQuad _fingerVis;
        [SerializeField] FxQuad _deskVis;
        [Tooltip("Backdrops shown behind each calibrated display, so the 'inside' of the screen is visible in the simulator.")]
        [SerializeField] FxQuad[] _screenPool;

        HandState _left, _right;
        IntPtr _hwnd; bool _tracked = true, _prevDown;
        string _handMode = "none"; float _handT;
        readonly Dictionary<string, FxQuad> _screens = new Dictionary<string, FxQuad>();
        int _screensUsed;
        string _pendingShot = ""; int _shotDelay;

        void Awake()
        {
            Win32.EnsureDpiAware();
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; Application.runInBackground = true;
            if (!Application.isEditor)
            {
                Win32.EnumThreadWindows(Win32.GetCurrentThreadId(), (h, l) => { if (Win32.WindowClass(h) == "UnityWndClass") _hwnd = h; return true; }, IntPtr.Zero);
                int x = int.Parse(Ui.Arg("-lp-sim-x", "40")), y = int.Parse(Ui.Arg("-lp-sim-y", "40")), w = int.Parse(Ui.Arg("-lp-sim-w", "1000")), h2 = int.Parse(Ui.Arg("-lp-sim-h", "700"));
                Screen.SetResolution(w, h2, FullScreenMode.Windowed);
                if (_hwnd != IntPtr.Zero) Win32.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            }
            _handVis.gameObject.SetActive(false); _fingerVis.gameObject.SetActive(false);
            foreach (var s in _screenPool) s.gameObject.SetActive(false);

            // the shared MR app (MrCore prefab instance in this scene) gets its input from this simulator
            _app.Input = this; _app.Cam = _cam; _app.Role = Roles.Sim;
            // -lp-no-token: start like a device that has never been paired (shows the numpad).
            // -lp-sim-pair <keys>: the scripted fingertip then types these keys on it (digits, c = connect, e = erase).
            if (Ui.HasArg("-lp-no-token")) _app.LoadToken = () => ""; else _app.LoadToken = PcApp.ReadLocalToken;
            _pairKeys = Ui.Arg("-lp-sim-pair", "");
            // Playing without the PC (MrLocalHost): -lp-local = from the start, no host is looked for at all; -lp-local-ask = offered
            // as the headset offers it (when no paired host answers); -lp-local-script <file> = debug commands fed to the app at
            // given times ("seconds cmd value px py text" - lpctl cannot reach an app without a host); -lp-local-file <file> =
            // where the local world is kept (a check does not touch the real one).
            _app.LocalAtStart = Ui.HasArg("-lp-local"); _app.OfferLocal = Ui.HasArg("-lp-local-ask");
            _app.LocalScript = Ui.Arg("-lp-local-script", ""); _app.LocalFile = Ui.Arg("-lp-local-file", "");
            _app.UrlProvider = () => new List<string> { $"ws://127.0.0.1:{Ui.Arg("-lp-port", Proto.DefaultPort.ToString())}/ws" };
        }

        bool _hooked;
        void HookCommands()
        {
            if (_hooked || _app.Link == null) return;
            _hooked = true;
            _app.Link.OnMessage += env =>
            {
                if (env.type != Msg.Debug) return;
                var d = env.Body<DebugCommand>(); if (d == null) return;
                if (d.cmd == "simHand") { _handMode = d.text; _handT = 0; Debug.Log("[SIM] hand mode -> " + _handMode); }
                else if (d.cmd == "simPoint") { _pointMon = d.text; _pointU = d.px; _pointV = d.py; _pointUntil = Time.time + (d.value > 0 ? d.value : 4f); Debug.Log($"[SIM] scripted pointing at {_pointMon} uv=({_pointU:0.00},{_pointV:0.00})"); }
                else if (d.cmd == "simPinch") { _pinchFrames = 3; Debug.Log("[SIM] scripted pinch"); }
                else if (d.cmd == "simPinchPartner") { _pinchPartnerUntil = Time.time + 1.0f; _pinchPartnerAt = Time.time + 0.4f; _pinchFrames = 0; Debug.Log("[SIM] scripted hand points at the partner and pinches"); }
                else if (d.cmd == "simTouch") { _touchPos = new Vector3(d.px, d.py, d.value); _touchUntil = Time.time + 1.2f; _pinchFrames = 0; _touchPinchAt = Time.time + 0.35f; Debug.Log("[SIM] scripted fingertip at " + _touchPos.ToString("0.000")); }
                else if (d.cmd == "simTracking") { _tracked = d.value > 0.5f; Debug.Log("[SIM] head tracking -> " + _tracked); }
                // lpctl debug glassHand <dist> <u> <v> on|off: the right hand held open in front of the main display, its palm
                // over (u, v) and dist metres from the glass.  lpctl debug pointAt 0 <u> <v> on|off: it points at (u, v) from a distance.
                else if (d.cmd == "simPress")
                {
                    if (_app.TryGetThingTop(d.text, out _pressTop)) { _pressFrom = Time.time; _pressUntil = Time.time + 1.1f; _pressPinch = d.value > 0.5f; Debug.Log($"[SIM] scripted index finger presses '{d.text}' at {_pressTop.ToString("0.000")}" + (_pressPinch ? " and closes thumb and finger on it" : "")); }
                    else Debug.Log($"[SIM] nothing on the desk is called '{d.text}'");
                }
                else if (d.cmd == "glassHand") { _glassOn = d.text != "off"; SimDisplay(d.text); _aimOn = false; _glassDist = d.value; _glassU = d.px; _glassV = d.py; Debug.Log($"[SIM] hand in front of the glass: {(_glassOn ? $"{_glassDist:0.00} m, uv=({_glassU:0.00},{_glassV:0.00})" : "off")}"); }
                else if (d.cmd == "pointAt") { _aimOn = d.text != "off"; SimDisplay(d.text); _glassOn = false; _glassU = d.px; _glassV = d.py; Debug.Log($"[SIM] hand pointing at the main display: {(_aimOn ? $"uv=({_glassU:0.00},{_glassV:0.00})" : "off")}"); }
                // the menu on the open LEFT palm (the hand itself: simHand palmOpen / palmClosed / palmAway / palmFlat):
                //   palmMove <x> <y> <z> "path"   where the hand is, in metres from its usual place (right, up, away from the eyes);
                //       path "" = held there, "drift" = it glides 12 cm to and fro at 0.3 m/s, "jitter" = held there with 2 mm of
                //       tracking noise, "lost" = not seen for 0.25 s out of every 2 s, "lostLong" = not seen for 0.8 s out of every 3 s
                //   palmFinger <dist>             the right index fingertip dist metres in front of the middle of the fan ("off": away)
                //   palmPress <i>                 the right index finger comes from 15 cm in front of sticker i, presses it, goes back ("chip": the Stop chip)
                else if (d.cmd == "palmMove") { _palmOff = new Vector3(d.value, d.px, d.py); _palmPath = d.text ?? ""; _handT = 0f; Debug.Log($"[SIM] the left palm: {_palmOff.ToString("0.000")} from its place, path '{_palmPath}'"); }
                else if (d.cmd == "palmFinger") { _fingerOn = d.text != "off"; _fingerDist = d.value; Debug.Log($"[SIM] scripted right index fingertip {(_fingerOn ? $"{_fingerDist * 100f:0} cm in front of the fan" : "taken away")}"); }
                else if (d.cmd == "palmPress") { _fpChip = d.text == "chip"; _fpIndex = (int)d.value; _fpFrom = Time.time; _fpUntil = Time.time + 1.1f; Debug.Log($"[SIM] scripted right index finger goes to {(_fpChip ? "the Stop chip" : "sticker " + _fpIndex)} on the left palm"); }
                else if (d.cmd == "simShot") { _pendingShot = d.text; _shotDelay = 2; }
                else if (d.cmd == "simQuit") Application.Quit();
                else if (d.cmd == "simEye") { _cam.transform.position = new Vector3(d.px, d.py, d.value); _cam.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0.96f, 0.62f) - _cam.transform.position); }
                // lpctl debug simLook 0 <yaw> <pitch>: the head turns where it is - so many degrees to the right of where it looked at the start, and up (0 0: back)
                else if (d.cmd == "simLook") { if (!_lookSet) { _lookSet = true; _lookBase = _cam.transform.rotation; } _cam.transform.rotation = Quaternion.AngleAxis(d.px, Vector3.up) * _lookBase * Quaternion.Euler(-d.py, 0f, 0f); Debug.Log($"[SIM] the head turns {d.px:0} degrees to the right, {d.py:0} up"); }
                // lpctl debug carry <turn> <x> <z> "thing": the scripted hand picks that thing up by a pinch, carries it to the spot
                // (x, z) of the desk (metres to the right of the partner's spot, metres away from the user) while the wrist turns
                // so that the thing turns by <turn> degrees, holds it there for a moment and lets go - a real pinch, as MrApp sees it
                // lpctl debug carryPalm 0 0 0 "thing": the same, but it carries the thing to the open left palm and lets go there
                else if (d.cmd == "carry" || d.cmd == "carryPalm")
                {
                    _carryToPalm = d.cmd == "carryPalm";
                    if (_app.SimCarryPlan(d.text, d.px, d.py, out _carryGrip, out _carryEnd, out _carryStraddle)) { _carryFrom = Time.time; _carryUntil = Time.time + CarryTotal; _carryTurn = MrApp.CarryHandTurnFor(d.value); _pinchWas = false; Debug.Log($"[SIM] scripted hand carries '{d.text}' from {_carryGrip.ToString("0.000")} to {_carryEnd.ToString("0.000")}, the wrist turning by {_carryTurn:0} degrees"); }
                    else Debug.Log($"[SIM] nothing on the desk is called '{d.text}'");
                }
            };
        }

        public void Tick(float dt)
        {
            HookCommands();
            // Unity re-centres the window when the resolution change lands, so the requested position is re-applied for a moment
            if (_placeFrames == 4) PcApp.ReturnFocus();     // do not keep the foreground from what the user was doing
            if (_hwnd != IntPtr.Zero && _placeFrames++ < 90 && _placeFrames % 10 == 0)
                Win32.SetWindowPos(_hwnd, IntPtr.Zero, int.Parse(Ui.Arg("-lp-sim-x", "40")), int.Parse(Ui.Arg("-lp-sim-y", "40")), 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            // --- right hand = mouse ray + click, unless a scripted hand pose is active
            _right = default; _left = default;
            bool down = false; Ray ray = new Ray(_cam.transform.position, _cam.transform.forward); bool inside = false;
            if (_hwnd != IntPtr.Zero && Win32.GetCursorPos(out var cp))
            {
                var origin = new Win32.POINT(0, 0);
                Win32.ClientToScreen(_hwnd, ref origin);
                Win32.GetClientRect(_hwnd, out var cr);
                float mx = cp.x - origin.x, my = cp.y - origin.y;
                inside = mx >= 0 && my >= 0 && mx < cr.right && my < cr.bottom;
                if (inside)
                {
                    ray = _cam.ScreenPointToRay(new Vector3(mx / Mathf.Max(1, cr.right) * _cam.pixelWidth, (1f - my / Mathf.Max(1, cr.bottom)) * _cam.pixelHeight, 0));
                    down = (Win32.GetAsyncKeyState(0x01) & 0x8000) != 0 && Win32.GetForegroundWindow() == _hwnd;
                    if (Win32.GetForegroundWindow() == _hwnd)
                    {
                        if ((Win32.GetAsyncKeyState('1') & 1) != 0) _handMode = "offer";
                        if ((Win32.GetAsyncKeyState('2') & 1) != 0) _handMode = "umbrella";
                        if ((Win32.GetAsyncKeyState('3') & 1) != 0) _handMode = "beside";
                        if ((Win32.GetAsyncKeyState('4') & 1) != 0) _handMode = "none";
                    }
                }
            }
#if UNITY_EDITOR
            // Play mode in the Editor (no player window of our own): the Game view mouse is the pointing hand
            if (_hwnd == IntPtr.Zero)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null)
                {
                    Vector2 mp = mouse.position.ReadValue();
                    inside = mp.x >= 0 && mp.y >= 0 && mp.x < _cam.pixelWidth && mp.y < _cam.pixelHeight;
                    if (inside) { ray = _cam.ScreenPointToRay(new Vector3(mp.x, mp.y, 0)); down = mouse.leftButton.isPressed; }
                }
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.digit1Key.wasPressedThisFrame) _handMode = "offer";
                    if (kb.digit2Key.wasPressedThisFrame) _handMode = "umbrella";
                    if (kb.digit3Key.wasPressedThisFrame) _handMode = "beside";
                    if (kb.digit4Key.wasPressedThisFrame) _handMode = "none";
                }
            }
#endif
            _handT += dt;
            Vector3 partner = _app.PartnerPos, head = _app.PartnerHeadTop;
            float deskY = _app.DeskY;
            // The LEFT hand held in view, for the menu on the open palm (MrMenuBoard): "palmOpen" = open, its palm to the
            // face; "palmClosed" = the same hand closed to a fist; "palmAway" = open, its back to the face; "palmFlat" =
            // open, lying flat, palm down. Where it is and how it moves: palmMove (see HookCommands).
            bool palmMode = _handMode.StartsWith("palm");
            if (palmMode)
            {
                Transform ct = _cam.transform;
                Vector3 off = _palmOff; bool seen = true;
                if (_palmPath == "drift") off.x += Mathf.PingPong(_handT * 0.3f, 0.12f) - 0.06f;
                else if (_palmPath == "jitter") off += UnityEngine.Random.insideUnitSphere * 0.002f;
                else if (_palmPath == "lost") seen = Mathf.Repeat(_handT, 2f) > 0.25f;
                else if (_palmPath == "lostLong") seen = Mathf.Repeat(_handT, 3f) > 0.8f;
                if (seen)
                {
                    Vector3 palm = ct.position + ct.rotation * (new Vector3(-0.13f, -0.13f, 0.40f) + off), toHead = (ct.position - palm).normalized;
                    bool fist = _handMode == "palmClosed";
                    Vector3 normal = _handMode == "palmAway" ? -toHead : _handMode == "palmFlat" ? Vector3.down : toHead;
                    Vector3 fingers = _handMode == "palmFlat" ? ct.forward : ct.up; fingers = (fingers - normal * Vector3.Dot(fingers, normal)).normalized;
                    Vector3 across = Vector3.Cross(normal, fingers);
                    float reach = fist ? 0.25f : 1f;
                    _left.tracked = true; _left.palmPos = palm; _left.palmNormal = normal; _left.palmForward = fingers;
                    _left.wrist = palm - fingers * 0.07f; _left.indexTip = palm + fingers * 0.09f * reach + normal * (fist ? 0.02f : 0f); _left.thumbTip = palm + across * 0.06f * reach;
                    _left.middleTip = palm + fingers * 0.10f * reach; _left.ringTip = palm + fingers * 0.09f * reach; _left.littleTip = palm + fingers * 0.075f * reach;
                    _left.fingersKnown = true; _left.fingers = fist ? 0 : 31; _left.rayOrigin = palm; _left.rayDir = Vector3.zero;
                }
            }
            bool scripted = _handMode != "none" && _handMode != "drop" && !palmMode;
            if (scripted)
            {
                Vector3 palm; Vector3 normal = Vector3.up; Vector3 fwd = Vector3.forward;
                switch (_handMode)
                {
                    case "offer": palm = (_offerSet ? _offerPos : (_offerPos = new Vector3(partner.x + 0.10f, deskY + 0.05f, partner.z - 0.06f))); _offerSet = true; break;
                    case "carry": palm = _offerPos + new Vector3(0.10f * Mathf.Sin(_handT * 1.3f), 0.08f + 0.05f * Mathf.Sin(_handT * 0.9f), 0.06f * Mathf.Cos(_handT * 1.1f)); break;
                    case "tilt": palm = _offerPos + Vector3.up * 0.08f; normal = new Vector3(0.98f, 0.15f, 0).normalized; break;
                    case "umbrella": palm = head - RainDirSim * 0.07f; normal = Vector3.down; break;     // palm directly up-wind of the head
                    case "beside": palm = head + new Vector3(0.11f, -0.02f, 0); normal = Vector3.down; break; // close, but NOT in the rain's path
                    case "tennis": case "tennisMix": case "tennisMiss":
                    {
                        // an open hand that waits low on the right and swings through the ball when the partner's stroke comes out
                        // (tennis = plays every ball, tennisMix = lets about one in three go, tennisMiss = never reaches)
                        Vector3 want = _cam.transform.position + _cam.transform.rotation * new Vector3(0.20f, -0.30f, 0.42f);
                        normal = Vector3.forward; fwd = Vector3.up;
                        if (_app.TennisIncoming(out Vector3 hit, out double at, out Vector3 zDir, out int seq))
                        {
                            normal = zDir;
                            bool play = _handMode == "tennis" || (_handMode == "tennisMix" && (seq * 7 + 3) % 10 >= 3);
                            if (play)
                            {
                                float dtTo = (float)(at - _app.Link.HostNow);
                                Vector3 side = Vector3.Cross(Vector3.up, zDir).normalized * ((seq & 1) == 0 ? 0.45f : -0.45f);
                                want = hit - (zDir + side).normalized * Mathf.Clamp(dtTo, -0.12f, 0.35f) * 0.9f;       // through the meeting point, towards the screen
                            }
                        }
                        else if (_app.TennisActive) normal = zDir;
                        _tennisPalm = _tennisSet ? Vector3.MoveTowards(_tennisPalm, want, 2.4f * dt) : want; _tennisSet = true;
                        palm = _tennisPalm;
                        break;
                    }
                    default: palm = partner + Vector3.up * 0.3f; break;
                }
                if (_handMode != "offer" && _handMode != "carry" && _handMode != "tilt") _offerSet = false;
                _right.tracked = true; _right.palmPos = palm; _right.palmNormal = normal; _right.palmForward = fwd;
                _right.wrist = palm - fwd * 0.07f; _right.indexTip = palm + fwd * 0.09f; _right.thumbTip = palm + new Vector3(-0.05f, 0, 0.03f);
                _right.rayOrigin = palm; _right.rayDir = Vector3.zero;
                _handVis.gameObject.SetActive(true);
                _handVis.transform.SetPositionAndRotation(palm - normal * 0.008f, Quaternion.LookRotation(fwd, normal.y < 0 ? Vector3.up : normal));
            }
            else
            {
                _offerSet = false;
                _handVis.gameObject.SetActive(false);
                if (inside)
                {
                    _right.tracked = true; _right.rayOrigin = ray.origin + ray.direction * 0.25f; _right.rayDir = ray.direction; _right.rayKind = 4;
                    _right.indexTip = _right.rayOrigin; _right.thumbTip = _right.rayOrigin; _right.palmPos = _right.rayOrigin - ray.direction * 0.08f;
                    _right.palmNormal = Vector3.down; _right.palmForward = ray.direction; _right.wrist = _right.palmPos;
                    _right.pinch = down; _right.pinchDown = down && !_prevDown; _right.pinchStrength = down ? 1 : 0;
                }
            }
            // scripted pointing hand (overrides the mouse): a ray from a hand position towards a point on a calibrated display
            if (!scripted && Time.time < _pointUntil)
            {
                foreach (var mv in _app.Monitors)
                {
                    if (!mv.desc.id.EndsWith(_pointMon, StringComparison.OrdinalIgnoreCase)) continue;
                    Vector3 hand = _cam.transform.position + _cam.transform.rotation * new Vector3(0.16f, -0.22f, 0.28f);
                    Vector3 target = mv.ToWorld(_pointU, _pointV);
                    bool p = _pinchFrames > 0; if (_pinchFrames > 0) _pinchFrames--;
                    _right = default; _right.tracked = true; _right.rayOrigin = hand; _right.rayDir = (target - hand).normalized; _right.rayKind = 4;
                    _right.indexTip = hand; _right.thumbTip = hand; _right.palmPos = hand - _right.rayDir * 0.08f; _right.palmNormal = Vector3.down; _right.palmForward = _right.rayDir; _right.wrist = _right.palmPos;
                    _right.pinch = p; _right.pinchDown = p && !_pinchWas; _right.pinchStrength = p ? 1 : 0; _pinchWas = p;
                    break;
                }
            }
            // scripted hand at the main display (the primary one; the first when none is): held open in front of its glass,
            // or pointing at a spot on it with the index finger from a distance. Stays until switched off.
            if (!scripted && (_glassOn || _aimOn) && Time.time >= _pointUntil && Time.time >= _pinchPartnerUntil && Time.time >= _touchUntil)
            {
                MonView main = null;
                foreach (var mv in _app.Monitors) if (main == null || (mv.desc.primary && !main.desc.primary)) main = mv;
                if (_glassMon.Length > 0) foreach (var mv in _app.Monitors) if (mv.desc.id.IndexOf(_glassMon, System.StringComparison.OrdinalIgnoreCase) >= 0) main = mv;
                if (main != null)
                {
                    Vector3 spot = main.ToWorld(_glassU, _glassV);
                    _right = default; _right.tracked = true; _right.fingersKnown = true;
                    if (_glassOn)
                    {
                        // an open hand, palm to the glass, fingers up
                        Vector3 palm = spot - main.Z * _glassDist, upv = main.Y, rightv = main.X;
                        _right.palmPos = palm; _right.palmNormal = main.Z; _right.palmForward = upv; _right.wrist = palm - upv * 0.07f;
                        _right.indexTip = palm + upv * 0.095f + rightv * -0.020f; _right.middleTip = palm + upv * 0.105f; _right.ringTip = palm + upv * 0.095f + rightv * 0.020f; _right.littleTip = palm + upv * 0.078f + rightv * 0.040f;
                        _right.thumbTip = palm + rightv * -0.060f + upv * 0.030f; _right.fingers = _glassFingers;
                        // (a sign: the fingers that are not stretched are curled into the palm)
                        if ((_glassFingers & 2) == 0) _right.indexTip = palm + upv * 0.030f - main.Z * 0.02f;
                        if ((_glassFingers & 4) == 0) _right.middleTip = palm + upv * 0.032f - main.Z * 0.02f;
                        if ((_glassFingers & 8) == 0) _right.ringTip = palm + upv * 0.030f + rightv * 0.010f - main.Z * 0.02f;
                        if ((_glassFingers & 16) == 0) _right.littleTip = palm + upv * 0.026f + rightv * 0.020f - main.Z * 0.02f;
                        _right.rayOrigin = palm; _right.rayDir = Vector3.zero;      // (an open hand at the glass is not pointing)
                        _handVis.gameObject.SetActive(true);
                        _handVis.transform.SetPositionAndRotation(palm + upv * 0.02f, Quaternion.LookRotation(upv, -main.Z));
                    }
                    else
                    {
                        // the index finger held out towards the spot, the other fingers curled
                        Vector3 knuckle = _cam.transform.position + _cam.transform.rotation * new Vector3(0.17f, -0.20f, 0.30f);
                        Vector3 dir = (spot - knuckle).normalized, palm = knuckle - dir * 0.045f - Vector3.up * 0.012f;
                        _right.palmPos = palm; _right.palmNormal = Vector3.down; _right.palmForward = dir; _right.wrist = palm - dir * 0.06f;
                        _right.indexTip = knuckle + dir * 0.085f; _right.thumbTip = palm + Vector3.Cross(Vector3.up, dir) * -0.045f;
                        _right.middleTip = palm + dir * 0.030f - Vector3.up * 0.025f; _right.ringTip = palm + dir * 0.025f - Vector3.up * 0.027f; _right.littleTip = palm + dir * 0.018f - Vector3.up * 0.026f;
                        _right.fingers = 2;
                        _right.rayOrigin = _right.indexTip; _right.rayDir = dir; _right.rayKind = 1;
                        _handVis.gameObject.SetActive(true);
                        _handVis.transform.SetPositionAndRotation(palm, Quaternion.LookRotation(dir, Vector3.up));
                    }
                }
            }
            // scripted hand that points at the partner itself and pinches (asks it to talk)
            if (!scripted && Time.time < _pinchPartnerUntil)
            {
                if (_pinchPartnerAt > 0 && Time.time >= _pinchPartnerAt) { _pinchFrames = 3; _pinchPartnerAt = 0; }
                Vector3 hand = _cam.transform.position + _cam.transform.rotation * new Vector3(0.16f, -0.22f, 0.28f);
                Vector3 target = Vector3.Lerp(partner, head, 0.6f);
                bool p = _pinchFrames > 0; if (_pinchFrames > 0) _pinchFrames--;
                _right = default; _right.tracked = true; _right.rayOrigin = hand; _right.rayDir = (target - hand).normalized;
                _right.indexTip = hand; _right.thumbTip = hand; _right.palmPos = hand - _right.rayDir * 0.08f; _right.palmNormal = Vector3.down; _right.palmForward = _right.rayDir; _right.wrist = _right.palmPos;
                _right.pinch = p; _right.pinchDown = p && !_pinchWas; _right.pinchStrength = p ? 1 : 0; _pinchWas = p;
            }
            // scripted typing on the pairing numpad: one fingertip touch per key, held for about half a second
            if (_pairStep < _pairKeys.Length && Time.time >= _pairNextAt)
            {
                char k = _pairKeys[_pairStep];
                int index = k >= '0' && k <= '9' ? k - '0' : k == 'e' ? 10 : k == 'c' ? 11 : -1;
                if (index < 0) _pairStep++;
                else if (_app.TryGetPairButton(index, out Vector3 keyPos))
                {
                    _touchPos = keyPos; _touchUntil = Time.time + 0.45f; _touchPinchAt = 0; _pinchFrames = 0;
                    _pairStep++; _pairNextAt = Time.time + (k == 'c' ? 2.2f : 0.95f);
                    Debug.Log($"[SIM] scripted fingertip pressed numpad key {_pairStep}/{_pairKeys.Length}" + (k == 'c' ? " (connect)" : ""));
                }
            }
            // scripted fingertip touch + pinch at a world position (drives the real calibration flow)
            if (!scripted && Time.time < _touchUntil)
            {
                if (_touchPinchAt > 0 && Time.time >= _touchPinchAt) { _pinchFrames = 3; _touchPinchAt = 0; }
                bool p = _pinchFrames > 0; if (_pinchFrames > 0) _pinchFrames--;
                _right = default; _right.tracked = true; _right.indexTip = _touchPos; _right.thumbTip = _touchPos; _right.palmPos = _touchPos + new Vector3(0, -0.02f, -0.08f);
                _right.palmNormal = Vector3.down; _right.palmForward = Vector3.forward; _right.wrist = _right.palmPos; _right.rayOrigin = _touchPos; _right.rayDir = Vector3.zero;
                _right.pinch = p; _right.pinchDown = p && !_pinchWas; _right.pinchStrength = p ? 1 : 0; _pinchWas = p;
            }
            // scripted index finger that PRESSES a thing on the desk the way a hand does (lpctl debug simPress [1] 0 0 "name"):
            // it comes down from 7 cm above the thing into it in 0.4 s, stays 0.4 s and goes back up. With value 1 thumb
            // and finger close while it is in the thing - a hand that picks the thing up, which is not a press.
            if (!scripted && Time.time < _pressUntil)
            {
                float t = Time.time - _pressFrom;
                float h = t < 0.4f ? Mathf.Lerp(0.07f, -0.012f, t / 0.4f) : t < 0.8f ? -0.012f : Mathf.Lerp(-0.012f, 0.07f, (t - 0.8f) / 0.3f);
                Vector3 tip = _pressTop + Vector3.up * h;
                bool p = _pressPinch && t > 0.5f && t < 0.75f;
                _right = default; _right.tracked = true; _right.indexTip = tip; _right.thumbTip = p ? tip : tip + new Vector3(-0.05f, 0.03f, -0.02f);
                _right.palmPos = tip + new Vector3(0, 0.035f, -0.08f); _right.palmNormal = Vector3.down; _right.palmForward = Vector3.forward; _right.wrist = _right.palmPos - Vector3.forward * 0.07f;
                _right.rayOrigin = tip; _right.rayDir = Vector3.zero;
                _right.pinch = p; _right.pinchDown = p && !_pinchWas; _right.pinchStrength = p ? 1 : 0; _pinchWas = p;
                _handVis.gameObject.SetActive(true);
                _handVis.transform.SetPositionAndRotation(_right.palmPos, Quaternion.LookRotation(Vector3.forward, Vector3.up));
            }
            // scripted hand that CARRIES a thing (lpctl debug carry ...): finger and thumb come down either side of the thing
            // (0.4 s), close on it (a pinch; 0.3 s), carry it in a low arc to the spot while the wrist turns (1.5 s), hold
            // it there (1.6 s: time for a capture), open, and go up again (0.4 s).
            _app.SimCarry = !scripted && Time.time < _carryUntil;
            if (_app.SimCarry)
            {
                float t = Time.time - _carryFrom, tGrip = 0.4f, tGo = 0.7f, tHold = 2.2f, tOpen = 3.8f;
                if (_carryToPalm && _left.tracked) _carryEnd = _left.palmPos + _left.palmNormal * 0.05f;      // (to the open left palm)
                float u = Mathf.Clamp01((t - tGo) / (tHold - tGo)), k = u * u * (3f - 2f * u);
                Vector3 pinch = t < tGrip ? _carryGrip + Vector3.up * Mathf.Lerp(0.07f, 0f, t / tGrip)
                    : t < tOpen ? Vector3.Lerp(_carryGrip, _carryEnd, k) + Vector3.up * (0.05f * 4f * u * (1f - u))
                    : _carryEnd + Vector3.up * Mathf.Lerp(0f, 0.07f, (t - tOpen) / 0.4f);
                bool p = t >= tGrip && t < tOpen;
                Vector3 fwd = Quaternion.Euler(0f, _carryTurn * k, 0f) * Vector3.forward, side = Vector3.Cross(Vector3.up, fwd);
                _right = default; _right.tracked = true;
                _right.indexTip = p ? pinch : pinch + side * _carryStraddle; _right.thumbTip = p ? pinch : pinch - side * _carryStraddle;
                _right.palmPos = pinch - fwd * 0.08f + Vector3.up * 0.035f; _right.palmNormal = Vector3.down; _right.palmForward = fwd; _right.wrist = _right.palmPos - fwd * 0.07f;
                _right.rayOrigin = pinch; _right.rayDir = Vector3.zero;
                _right.pinch = p; _right.pinchDown = p && !_pinchWas; _right.pinchStrength = p ? 1 : 0; _pinchWas = p;
                _handVis.gameObject.SetActive(true);
                _handVis.transform.SetPositionAndRotation(_right.palmPos, Quaternion.LookRotation(fwd, Vector3.up));
            }
            // The right index finger at the menu on the left palm (palmFinger, palmPress), and what is seen of the two hands.
            if (palmMode)
            {
                Transform ct = _cam.transform;
                Vector3 tip = default; bool finger = false;
                if (Time.time < _fpUntil)
                {
                    // it comes from 15 cm in front of the sticker (0.5 s), goes a little way through it, stays, and goes back
                    float t = Time.time - _fpFrom;
                    float dist = t < 0.5f ? Mathf.Lerp(0.15f, -0.006f, t / 0.5f) : t < 0.75f ? -0.006f : Mathf.Lerp(-0.006f, 0.15f, (t - 0.75f) / 0.35f);
                    Vector3 at = default, toUser = default; bool have;
                    if (_fpChip) { have = _app.TryGetChip(out at); toUser = (ct.position - at).normalized; }
                    else have = _app.TryGetSticker(_fpIndex, out at, out toUser);
                    if (have) { tip = at + toUser * dist; finger = true; }
                }
                else if (_fingerOn && _app.TryGetFan(out Vector3 mid, out Vector3 fanToUser)) { tip = mid + fanToUser * _fingerDist; finger = true; }
                if (finger)
                {
                    _right = default; _right.tracked = true; _right.indexTip = tip; _right.thumbTip = tip + ct.rotation * new Vector3(0.03f, -0.03f, -0.03f);
                    _right.palmPos = tip + ct.rotation * new Vector3(0.04f, -0.05f, -0.08f); _right.palmNormal = Vector3.down; _right.palmForward = ct.forward;
                    _right.wrist = _right.palmPos; _right.rayOrigin = tip; _right.rayDir = Vector3.zero;
                }
                _handVis.gameObject.SetActive(_left.tracked);
                if (_left.tracked) _handVis.transform.SetPositionAndRotation(_left.palmPos - _left.palmNormal * 0.008f, Quaternion.LookRotation(_left.palmForward, _left.palmNormal));
                bool showFinger = _right.tracked && (finger || _app.SimCarry);
                _fingerVis.gameObject.SetActive(showFinger);
                // (a short rod that ends at the fingertip, coming from the lower right)
                Vector3 along = ct.rotation * new Vector3(-0.35f, 0.40f, 0.85f).normalized;
                if (showFinger) _fingerVis.transform.SetPositionAndRotation(_right.indexTip - along * 0.035f, Quaternion.LookRotation(along, ct.up));
            }
            else if (_fingerVis.gameObject.activeSelf) _fingerVis.gameObject.SetActive(false);
            _prevDown = down;

            // --- visuals: desk and screen backdrops (so the "inside" of each real display is visible in the simulator)
            _deskVis.transform.SetPositionAndRotation(new Vector3(0, deskY - 0.0005f, 0.42f), Quaternion.Euler(90, 0, 0));
            _deskVis.transform.localScale = new Vector3(1.9f, 0.9f, 1);
            foreach (var mv in _app.Monitors)
            {
                if (!_screens.TryGetValue(mv.desc.id, out var r))
                {
                    if (_screensUsed >= _screenPool.Length) continue;
                    _screens[mv.desc.id] = r = _screenPool[_screensUsed++];
                    r.gameObject.SetActive(true);
                }
                r.transform.SetPositionAndRotation(mv.Origin + mv.Z * 0.0015f, Quaternion.LookRotation(mv.Z, mv.Y));
                r.transform.localScale = new Vector3(mv.frame.widthM, mv.frame.heightM, 1);
            }
            if (_pendingShot.Length > 0 && --_shotDelay <= 0)
            {
                try { SaveCameraFrame(_pendingShot); Debug.Log("[SIM] screenshot " + _pendingShot); } catch (Exception e) { Debug.LogWarning(e.Message); }
                _pendingShot = "";
            }
        }
        // Windows can stop presenting a hidden window while the simulated world continues to run.
        void SaveCameraFrame(string path)
        {
            int width = Screen.width, height = Screen.height;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                RenderPipeline.SubmitRenderRequest(_cam, request);
                RenderTexture.active = target;
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (pixels != null) Destroy(pixels);
            }
        }

        Vector3 _offerPos; bool _offerSet; int _placeFrames;
        Vector3 _tennisPalm; bool _tennisSet;
        bool _glassOn, _aimOn; float _glassDist = 0.2f, _glassU = 0.5f, _glassV = 0.5f;
        // (text other than on / off names the display, by a part of its id: "DISPLAY3". It stays until another is named; "main" = the primary one again.)
        string _glassMon = "";
        int _glassFingers = 31;      // what the hand at the glass shows (finger bits): open unless a sign was named
        /// <summary>The words after glassHand / pointAt: a display ("DISPLAY3", "main") and, for the hand at the glass, a sign (rock | paper | scissors).</summary>
        void SimDisplay(string text)
        {
            _glassFingers = 31;
            foreach (string word in (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word == "on" || word == "off") continue;
                int f = LivingPartner.Shared.GlassGames.FingersOf(word);
                if (f >= 0 && word.ToLowerInvariant() != "point") _glassFingers = f | (f == 30 ? 1 : 0);
                else _glassMon = word == "main" ? "" : word;
            }
        }
        Vector3 _pressTop; float _pressFrom, _pressUntil; bool _pressPinch;
        const float CarryTotal = 4.2f;
        Vector3 _carryGrip, _carryEnd; float _carryFrom, _carryUntil, _carryTurn, _carryStraddle;
        Quaternion _lookBase; bool _lookSet;
        Vector3 _palmOff; string _palmPath = "";      // the left palm of the palm modes: where it is, how it moves
        bool _fingerOn; float _fingerDist = 0.06f;    // the scripted right index fingertip held in front of the fan
        bool _fpChip, _carryToPalm; int _fpIndex; float _fpFrom, _fpUntil;      // ... and on its way to press a sticker (or the Stop chip); a carry that ends at the palm
        string _pairKeys = ""; int _pairStep; float _pairNextAt = 2f;
        static readonly Vector3 RainDirSim = new Vector3(0.12f, -1f, 0.05f).normalized;
    }

}
