using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using SN = System.Numerics;

namespace LivingPartner.MR
{
    /// <summary>
    /// Pointing at a display, made visible - and the hands in front of the glass.
    /// "It cannot see where I point" (the user, 2026-11-17): the partner read a different display, or a different part
    /// of it, than the one pointed at - and nothing showed the user where the app THOUGHT they were pointing. Now:
    ///  - WHICH HAND POINTS is decided here (HandPoints): an index finger held out by itself points along the finger;
    ///    a relaxed hand points along the system's pointer pose, but only while it is lifted off the desk (a hand on
    ///    the keyboard or the mouse is not pointing at anything). A hand that is offered palm-up, carries the partner
    ///    or holds a thing does not point.
    ///  - THE RAY AND THE RING: while such a ray meets a calibrated display from the front, a thin soft ray runs from
    ///    the hand to the screen and a cream ring with a brown outline and a centre dot (4 cm) lies on the display
    ///    where it meets it - what Meta's own interface ray does. It stays for 0.3 s when the hand drops, then goes.
    ///  - THE GAZE goes with every pointer sample (MrApp.UpdatePointing): the host decides what was meant.
    ///  - GLASS HAND (Msg.GlassHand, about 30 a second): where the hands are in front of a display - the palm dropped
    ///    straight onto the glass, how far in front of it, how open, how turned - and the pointed spot. The PC draws
    ///    the hands as shadows inside the screen and the pointed spot as a ring on the real screen. One last message
    ///    with nothing valid is sent when the hands leave; nothing is sent otherwise.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Pointer: the ray from the hand and the ring on the display")]
        [SerializeField] FxQuad _ptrRay;
        [Tooltip("The ring where the pointing ray meets a display: this holder is moved, the parts in it are shown / hidden.")]
        [SerializeField] Transform _ptrCursor;
        [Tooltip("Brown outline ring, cream ring, centre dot.")]
        [SerializeField] FxQuad[] _ptrCursorParts;

        // A relaxed hand points only while its palm is this far above the desk; a finger held out by itself, this far.
        const float PointRaised = 0.12f, PointFingerRaised = 0.04f;
        const float PointLinger = 0.3f;                    // how long the ring stays after the hand has stopped pointing
        const float PointRing = 0.040f;                    // the ring's diameter on the display
        const float GlassReach = 0.45f, GlassOver = 0.12f; // "in front of a display": this near to its glass, and at most this far (in u / v) beside its lit area

        float _ptrSeenAt = -10f, _ptrShow; Vector3 _ptrCursorAt, _ptrFromShown; string _ptrCursorMon = "";
        double _nextGlass; bool _glassSent; string _glassMon = "";
        // how fast each palm moves (GlassHandPoint.speed), measured every frame
        readonly HandSpeed[] _palmSpeed = { new HandSpeed(), new HandSpeed() };
        float _nextPtrLog; int _ptrSamples, _glassMsgs; bool _ptrLogNow;
        string _ptrStateWas = "";

        /// <summary>Is this hand pointing just now? (Its ray is then tried against the displays.)</summary>
        bool HandPoints(int h, in HandState hs)
        {
            if (!hs.tracked || hs.rayDir.sqrMagnitude < 0.5f) return false;
            if (Input.Simulated) return true;                                   // the simulator's mouse / scripted ray is a pointing hand
            if (hs.palmUp) return false;                                        // offered to the partner, or the bag a thing is dropped into
            if (_mode == Mode.OnHand && h == _rideHand) return false;           // the partner stands on it
            if (_mode == Mode.Held && h == _holdHand) return false;             // it carries the partner by a pinch
            if (_grab >= 0 && h == _grabHand) return false;                     // it carries a thing
            float above = hs.palmPos.y - _deskY;
            return above > (hs.rayKind == 1 ? PointFingerRaised : PointRaised); // down on the desk (typing, the mouse) or hanging at the side: not pointing
        }

        /// <summary>Fills _ptr: the hand that points and where its ray meets a display. A finger held out comes before a relaxed hand, the right hand before the left.</summary>
        void FindPointer()
        {
            int bestRank = 99;
            for (int h = 0; h < 2; h++)
            {
                var hs = h == 0 ? Input.Right : Input.Left;
                if (!HandPoints(h, hs)) continue;
                if (_rayPanel[h] != null) continue;                             // its ray is on a board (MrUiRay): the board is what it points at, not the display behind it
                int rank = hs.rayKind == 1 ? 0 : 1;
                if (rank >= bestRank) continue;
                float best = float.MaxValue; Ptr found = default;
                foreach (var mv in _mons.Values)
                {
                    if (Vector3.Dot(hs.rayDir, mv.Z) < 0.05f) continue;         // only from the front of the display
                    if (!mv.frame.Raycast(Conv.N(hs.rayOrigin), Conv.N(hs.rayDir), out SN.Vector3 local, out float d) || d > best) continue;
                    ScreenMath.LocalToUV(mv.frame.widthM, mv.frame.heightM, local, out float u, out float v);
                    if (u < -0.04f || u > 1.04f || v < -0.04f || v > 1.04f) continue;
                    best = d; found = new Ptr { valid = true, mon = mv.desc.id, u = Mathf.Clamp01(u), v = Mathf.Clamp01(v), hand = h, from = hs.rayOrigin, kind = hs.rayKind };
                }
                if (found.valid) { _ptr = found; bestRank = rank; }
            }
        }

        /// <summary>The ray from the pointing hand and the ring on the display. Called every frame after _ptr has been worked out and stabilised.</summary>
        void DrawPointer(float dt)
        {
            if (_ptr.valid) { _ptrSeenAt = Time.time; _ptrCursorAt = _ptr.world; _ptrCursorMon = _ptr.mon; _ptrFromShown = _ptr.from; }
            var mv = Mon(_ptrCursorMon);
            bool on = mv != null && Time.time - _ptrSeenAt <= PointLinger && !_calibrating;
            _ptrShow = Mathf.MoveTowards(_ptrShow, on ? 1f : 0f, dt / (on ? 0.12f : 0.15f));
            bool show = mv != null && _ptrShow > 0.01f;
            foreach (var q in _ptrCursorParts) if (q.enabled != show) q.enabled = show;
            bool ray = false;
            if (show)
            {
                var hand = _ptr.hand == 0 ? Input.Right : Input.Left;
                bool pinching = _ptr.valid && hand.pinch;
                _ptrCursor.SetPositionAndRotation(_ptrCursorAt - mv.Z * 0.0015f, Quaternion.LookRotation(mv.Z, mv.Y));
                // it pops up, breathes a little, and closes a little while thumb and finger are closed
                _ptrCursor.localScale = Vector3.one * (PointRing * Mathf.Max(0.001f, Ui.Pop(_ptrShow)) * (pinching ? 0.78f : 1f + 0.03f * Mathf.Sin(Time.time * 5f)));
                // the ray: from a little in front of the hand to the ring, turned to the viewer; brightest at the screen,
                // fading out towards the hand (nothing is drawn on the hand itself)
                Vector3 to = _ptrCursorAt - mv.Z * 0.004f, dir = to - _ptrFromShown; float len = dir.magnitude;
                if (_ptr.valid && len > 0.10f)
                {
                    dir /= len;
                    Vector3 start = _ptrFromShown + dir * 0.03f, mid = (start + to) * 0.5f;
                    Vector3 toCam = Input.HeadPos - mid; toCam -= dir * Vector3.Dot(toCam, dir);
                    if (toCam.sqrMagnitude > 1e-6f)
                    {
                        ray = true;
                        _ptrRay.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(-toCam.normalized, -dir));
                        _ptrRay.transform.localScale = new Vector3(0.007f, len - 0.03f, 1f);
                    }
                }
            }
            if (_ptrRay.enabled != ray) _ptrRay.enabled = ray;

            // one line in the log when the pointing begins, changes display or ends, and a summary now and then:
            // a device log then says what the app took the hand to be pointing at
            string state = _ptr.valid ? _ptr.mon + "/" + _ptr.hand + "/" + _ptr.kind : "";
            if (state != _ptrStateWas)
            {
                _ptrStateWas = state;
                if (_ptr.valid) Debug.Log($"[MR] pointer: the {(_ptr.hand == 0 ? "right" : "left")} hand points at {ShortMon(_ptr.mon)} uv=({_ptr.u:0.00},{_ptr.v:0.00}) by {RayKindName(_ptr.kind)}");
                else Debug.Log("[MR] pointer: no hand is pointing at a display");
            }
            if (_ptrLogNow || (Time.time >= _nextPtrLog && (_ptr.valid || _glassSent)))
            {
                _ptrLogNow = false; _nextPtrLog = Time.time + 5f;
                bool gz = GazeTarget(out string gm, out float gu, out float gv);
                Debug.Log($"[MR] pointer: hand {(_ptr.valid ? $"{ShortMon(_ptr.mon)} uv=({_ptr.u:0.00},{_ptr.v:0.00}) {RayKindName(_ptr.kind)}" : "none")}; gaze {(gz ? $"{ShortMon(gm)} uv=({gu:0.00},{gv:0.00})" : "none")}; {_ptrSamples} samples and {_glassMsgs} glass-hand messages sent so far{(_glassSent ? ", hands at " + ShortMon(_glassMon) : "")}");
            }
        }

        static string ShortMon(string id) => string.IsNullOrEmpty(id) ? "-" : id.Replace("\\\\.\\", "");
        static string RayKindName(int kind) => kind == 1 ? "the index finger" : kind == 2 ? "the system pointer pose" : kind == 3 ? "shoulder-through-knuckle" : kind == 4 ? "the simulator's ray" : "a scripted ray";

        /// <summary>How open a hand is: 0 a fist, 1 an open hand (from how far the fingertips are from the middle of the palm).</summary>
        static float HandSpread(in HandState hs)
        {
            if (!hs.fingersKnown) return 1f;
            float sum = Vector3.Distance(hs.indexTip, hs.palmPos); int n = 1;
            if (hs.middleTip.sqrMagnitude > 1e-6f) { sum += Vector3.Distance(hs.middleTip, hs.palmPos); n++; }
            if (hs.ringTip.sqrMagnitude > 1e-6f) { sum += Vector3.Distance(hs.ringTip, hs.palmPos); n++; }
            if (hs.littleTip.sqrMagnitude > 1e-6f) { sum += Vector3.Distance(hs.littleTip, hs.palmPos); n++; }
            return Mathf.Clamp01((sum / n - 0.045f) / 0.045f);      // about 3.5 cm for a fist, 10 cm for an open hand
        }

        /// <summary>
        /// The hand over a display: the middle of its palm dropped straight onto the glass. False when it is not tracked,
        /// further than GlassReach in front of the glass (or behind it), or beside the lit area by more than GlassOver.
        /// </summary>
        bool GlassPoint(MonView mv, in HandState hs, GlassHandPoint p, out float dist)
        {
            dist = 0f;
            if (!hs.tracked) return false;
            SN.Vector3 l = mv.frame.ToLocal(Conv.N(hs.palmPos));
            dist = -l.Z;                                             // +z runs INTO the screen
            ScreenMath.LocalToUV(mv.frame.widthM, mv.frame.heightM, l, out float u, out float v);
            // (seen, wherever it is: a game inside the display may ask what a hand shows that is too far from the glass to act)
            if (p != null) { p.seen = true; p.u = u; p.v = v; p.dist = Mathf.Max(0f, dist); p.fingers = hs.fingersKnown ? hs.fingers : -1; }
            if (dist < -0.03f || dist > GlassReach || u < -GlassOver || u > 1f + GlassOver || v < -GlassOver || v > 1f + GlassOver) return false;
            if (p != null)
            {
                p.valid = true; p.u = u; p.v = v; p.dist = Mathf.Max(0f, dist);
                p.spread = HandSpread(hs);
                // how the hand is turned in the plane of the glass: 0 = fingers up, counter-clockwise as the user sees it (90 = fingers to the left)
                p.roll = Mathf.Atan2(-Vector3.Dot(hs.palmForward, mv.X), Vector3.Dot(hs.palmForward, mv.Y)) * Mathf.Rad2Deg;
                p.pinch = hs.pinch;
            }
            return true;
        }

        /// <summary>Tells the PC where the hands are in front of a display, and the pointed spot (about 30 times a second while there is something to tell).</summary>
        void SendGlassHand(double now, bool hostOk)
        {
            for (int h = 0; h < 2; h++)
            {
                var hs = h == 0 ? Input.Right : Input.Left;
                if (hs.tracked) _palmSpeed[h].Add(Time.unscaledTime, hs.palmPos.x, hs.palmPos.y, hs.palmPos.z); else _palmSpeed[h].Lost();
            }
            if (!hostOk) { _glassSent = false; return; }
            if (now < _nextGlass) return;
            _nextGlass = now + 1.0 / 30.0;
            // which display: the one that is pointed at; otherwise the one a hand is nearest to
            MonView target = _ptr.valid ? Mon(_ptr.mon) : null;
            if (target == null && !_calibrating)
            {
                float best = float.MaxValue;
                foreach (var mv in _mons.Values)
                    for (int h = 0; h < 2; h++)
                        if (GlassPoint(mv, h == 0 ? Input.Right : Input.Left, null, out float dist) && dist < best) { best = dist; target = mv; }
            }
            // a game is played INSIDE a display: it hears of every hand that is seen, however far from the glass (it may ask
            // what a hand shows or whether it moves: rock paper scissors, red light). Such a hand is `seen`, not `valid`.
            if (target == null && !_calibrating && (Input.Right.tracked || Input.Left.tracked))
            {
                var gl = GlassNow;
                if (gl.game.Length > 0 && gl.side == Env.Pc && gl.phase != "over") target = Mon(gl.monitorId);
            }
            if (target == null)
            {
                // the hands have left: one last message with nothing valid, then silence
                if (_glassSent) { _glassSent = false; _link.Send(Msg.GlassHand, new GlassHand { monitorId = _glassMon, t = _link.HostNow }); _glassMsgs++; Debug.Log("[MR] glass hand: the hands have left " + ShortMon(_glassMon)); }
                return;
            }
            var g = new GlassHand { monitorId = target.desc.id, t = _link.HostNow };
            for (int h = 0; h < 2; h++) { GlassPoint(target, h == 0 ? Input.Right : Input.Left, g.hands[h], out float _); g.hands[h].speed = g.hands[h].seen ? _palmSpeed[h].Speed : 0f; }
            if (_ptr.valid && _ptr.mon == g.monitorId)
            {
                // (the pinch of the hand that POINTS goes along: inside the screen it is the trigger of a game that is aimed at)
                g.pointValid = true; g.pointU = _ptr.u; g.pointV = _ptr.v; g.pointHand = _ptr.hand;
                g.pointPinch = (_ptr.hand == 0 ? Input.Right : Input.Left).pinch;
            }
            if (_glassSent && _glassMon != g.monitorId) { _link.Send(Msg.GlassHand, new GlassHand { monitorId = _glassMon, t = g.t }); _glassMsgs++; }      // (it moved on to another display: the one it left is told)
            if (!_glassSent || _glassMon != g.monitorId)
                Debug.Log($"[MR] glass hand: at {ShortMon(g.monitorId)} - right {(g.hands[0].valid ? $"uv=({g.hands[0].u:0.00},{g.hands[0].v:0.00}) {g.hands[0].dist * 100f:0} cm, spread {g.hands[0].spread:0.0}, roll {g.hands[0].roll:0}, shows '{GlassGames.Shape(g.hands[0].fingers)}'" : g.hands[0].seen ? $"seen {g.hands[0].dist * 100f:0} cm away, shows '{GlassGames.Shape(g.hands[0].fingers)}'" : "-")}, left {(g.hands[1].valid ? $"uv=({g.hands[1].u:0.00},{g.hands[1].v:0.00}) {g.hands[1].dist * 100f:0} cm" : "-")}, pointing {(g.pointValid ? $"uv=({g.pointU:0.00},{g.pointV:0.00})" : "-")}");
            _link.Send(Msg.GlassHand, g);
            _glassSent = true; _glassMon = g.monitorId; _glassMsgs++;
        }
    }
}
