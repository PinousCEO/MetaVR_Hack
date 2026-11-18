using System;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Pressing the buttons of every board, the two ways the headset's own menus are pressed:
    ///  - TOUCH: an index fingertip goes through the face of a button from the front, inside its outline. A finger
    ///    within 3.5 cm in front of a button lights it up; one press per poke - the finger has to be drawn back out
    ///    (2 cm in front of the face) before that hand can press anything again, so a finger resting in a button, a hand
    ///    coming from behind the board, or a hand sweeping sideways through a row of buttons presses one thing at most.
    ///  - RAY AND PINCH: a hand further than 30 cm from every open board that points at one gets a thin soft ray and a
    ///    round cursor on the board; the button under the cursor lights up, and closing thumb and finger presses it
    ///    (on the closing itself, and only when the cursor has rested on that button for a moment).
    ///  - TOUCH OF A BUTTON THAT FLOATS OVER A HAND (the Stop chip on the open left palm): the fingertip coming within the
    ///    button's radius of its middle, from any side - such a button moves with the hand it is on, and is reached
    ///    for from wherever the other hand happens to be. One press per touch here too.
    /// Nothing reacts to a hand that is merely near.
    /// The menu's fan is not a flat sheet but a piece of a cylinder (MenuBoardView): a ray meets that curve
    /// (MrMenuBoard.FanRay), and a hand's distance from it is measured to the curve.
    /// Each board still calls UpdateButtons once a frame while it is shown; the rays are worked out once a frame
    /// (UiRayBegin) from the boards that were shown in the frame before, and drawn after all of them (UiRayEnd).
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Pressing from a distance (MrCore/UiRay): one ray and one cursor per hand, right then left")]
        [SerializeField] FxQuad[] _uiRays;
        [SerializeField] FxQuad[] _uiCursors;

        // the face of a button in its own space (a little in front of its quad), how far in front of that a fingertip
        // that went through has to be drawn back before it can press again, and how near a finger lights a button up
        const float PokeFace = -0.004f, PokeBack = 0.02f, PokeHover = 0.035f, PokeDeep = 0.04f;
        // pointing: how far from every board a hand has to be (and, once it points, how near it may come), how far a
        // ray reaches, and for how long the cursor has to have been on a button before a pinch presses it
        const float RayFar = 0.30f, RayNear = 0.25f, RayReach = 2.5f, RayDwell = 0.10f, RayMargin = 0.02f;

        // per hand: 0 the right one, 1 the left one
        readonly UiButton[] _pokeLock = new UiButton[2];      // the button this fingertip went through and has not come back out of
        readonly float[] _pokeLockAt = new float[2];
        readonly bool[] _rayMode = new bool[2];               // far enough from every board to point
        readonly UiPanel[] _rayPanel = new UiPanel[2];        // the board the hand's ray is on in this frame (null: none)
        readonly Vector3[] _rayAt = new Vector3[2], _rayFrom = new Vector3[2];
        readonly UiButton[] _rayHot = new UiButton[2], _rayHotWas = new UiButton[2];      // the button under the cursor: this frame, the frame before
        readonly float[] _rayHotT = new float[2];
        readonly List<UiPanel> _uiSeen = new List<UiPanel>(), _uiOpen = new List<UiPanel>();      // the boards that can be pointed at: updated in this frame / in the last one
        // a pointing hand made up for a check from the PC (lpctl debug menuRay): where it points, until when, when it pinches
        Vector3 _dbgRayTo; float _dbgRayUntil = -1f, _dbgRayPinchAt = -1f; bool _dbgRayPinched;

        /// <summary>The hand as the boards see it (0 right, 1 left).</summary>
        HandState UiHand(int h)
        {
            if (h == 0 && Time.time < _dbgRayUntil)
            {
                var s = new HandState { tracked = true };
                s.rayOrigin = Input.HeadPos + Input.HeadRot * new Vector3(0.20f, -0.20f, 0.08f);
                s.rayDir = (_dbgRayTo - s.rayOrigin).normalized;
                s.indexTip = s.thumbTip = s.rayOrigin; s.palmPos = s.wrist = s.rayOrigin - s.rayDir * 0.08f;
                s.palmNormal = Vector3.down; s.palmForward = s.rayDir;
                bool pinch = Time.time >= _dbgRayPinchAt;
                s.pinch = pinch; s.pinchDown = pinch && !_dbgRayPinched; s.pinchStrength = pinch ? 1f : 0f;
                return s;
            }
            return h == 0 ? Input.Right : Input.Left;
        }

        /// <summary>A hand that is offered palm-up, or carries the partner or a thing, is not pressing anything.</summary>
        bool HandBusy(int h, in HandState hs)
        {
            if (hs.palmUp) return true;
            if (_mode == Mode.OnHand && h == _rideHand) return true;
            if (_mode == Mode.Held && h == _holdHand) return true;
            if (_grab >= 0 && h == _grabHand) return true;
            return false;
        }

        /// <summary>A hand whose palm is turned to the user's face is being looked at, not pointed with (the headset's own menus do the same).</summary>
        bool PalmToFace(in HandState hs) => Vector3.Dot(hs.palmNormal, (Input.HeadPos - hs.palmPos).normalized) > 0.35f;

        /// <summary>Where a world point is on a board: x, y in metres from its middle, z in front (-) or behind (+) its paper.</summary>
        static Vector3 OnBoard(UiPanel p, Vector3 world) => Quaternion.Inverse(p.transform.rotation) * (world - p.transform.position);

        /// <summary>How far a point is from the sheet of a board (0 on it).</summary>
        float BoardDistance(UiPanel p, Vector3 world)
        {
            if (p == _menu.panel) return FanDistance(world);      // (curved: MrMenuBoard)
            Vector3 l = OnBoard(p, world);
            float dx = Mathf.Max(0f, Mathf.Abs(l.x) - p.width * 0.5f), dy = Mathf.Max(0f, Mathf.Abs(l.y) - p.height * 0.5f);
            return Mathf.Sqrt(dx * dx + dy * dy + l.z * l.z);
        }

        /// <summary>Once a frame, before any board is updated: which board each hand points at.</summary>
        void UiRayBegin(float dt)
        {
            _uiOpen.Clear();
            foreach (var p in _uiSeen) if (p != null && p.Shown && p.appear >= 1f) _uiOpen.Add(p);
            _uiSeen.Clear();
            for (int h = 0; h < 2; h++)
            {
                // how long the cursor has been on the same button
                _rayHotWas[h] = _rayHot[h]; _rayHot[h] = null;
                _rayHotT[h] = _rayHotWas[h] != null ? _rayHotT[h] + dt : 0f;
                _rayPanel[h] = null;
                var hs = UiHand(h);
                bool made = h == 0 && Time.time < _dbgRayUntil;
                // a finger that went through a button: free again once it is back out in front of it (or the button is gone)
                var lk = _pokeLock[h];
                if (lk != null)
                {
                    bool gone = !lk.gameObject.activeInHierarchy;
                    if (gone ? Time.time - _pokeLockAt[h] > 0.4f
                             : !hs.tracked ? Input.Simulated || Time.time - _pokeLockAt[h] > 0.15f      // (a hand that comes back into view cannot press by appearing: a press needs the tip seen in front of the face one frame and behind it the next)
                             : Input.Simulated ? Vector3.Distance(hs.indexTip, lk.transform.position) > Mathf.Max(lk.radius, Mathf.Max(lk.half.x, lk.half.y)) * 1.3f
                             : lk == _menu.chip ? Vector3.Distance(hs.indexTip, lk.transform.position) > lk.radius + 0.025f      // (touched from any side: it is left on any side)
                             : (Quaternion.Inverse(lk.transform.rotation) * (hs.indexTip - lk.transform.position)).z < PokeFace - PokeBack)
                        _pokeLock[h] = null;
                }
                // (HandPoints, MrGlassHand: a hand lying on the desk or hanging at the side is not pointing at anything)
                if (!hs.tracked || hs.rayDir.sqrMagnitude < 0.5f || HandBusy(h, hs) || !HandPoints(h, hs) || PalmToFace(hs) || (h == 1 && PalmMenuUp) || (_menuDrag && _menuDragHand == h)) { _rayMode[h] = false; continue; }
                // far enough from every board? (the simulator's mouse hand always points)
                float nearest = float.MaxValue;
                foreach (var p in _uiOpen) nearest = Mathf.Min(nearest, BoardDistance(p, hs.indexTip));
                _rayMode[h] = (Input.Simulated && !made) || nearest > (_rayMode[h] ? RayNear : RayFar);
                if (!_rayMode[h]) continue;
                // the nearest board the ray meets, from its front
                float best = RayReach;
                foreach (var p in _uiOpen)
                {
                    if (p == _menu.panel)
                    {
                        // the fan is curved: the ray meets the curve, not a plane through its middle
                        if (!FanRay(hs.rayOrigin, hs.rayDir, RayMargin, out float far, out Vector3 at) || far >= best) continue;
                        best = far; _rayPanel[h] = p; _rayAt[h] = at; _rayFrom[h] = hs.rayOrigin;
                        continue;
                    }
                    Transform pt = p.transform;
                    float den = Vector3.Dot(hs.rayDir, pt.forward);
                    if (den < 0.2f) continue;
                    float along = Vector3.Dot(pt.position - hs.rayOrigin, pt.forward) / den;
                    if (along <= 0.02f || along >= best) continue;
                    Vector3 hit = hs.rayOrigin + hs.rayDir * along, l = OnBoard(p, hit);
                    if (Mathf.Abs(l.x) > p.width * 0.5f + RayMargin || Mathf.Abs(l.y) > p.height * 0.5f + RayMargin) continue;
                    best = along; _rayPanel[h] = p; _rayAt[h] = hit; _rayFrom[h] = hs.rayOrigin;
                }
                // (a pinch while pointing at a board belongs to that board: it does not also send the partner somewhere)
                if (_rayPanel[h] != null && hs.pinchDown) _uiConsumedPinch = true;
            }
        }

        /// <summary>Once a frame, after every board: the rays and the cursors.</summary>
        void UiRayEnd(float dt)
        {
            Vector3 head = Input.HeadPos;
            for (int h = 0; h < 2; h++)
            {
                var ray = _uiRays[h]; var cur = _uiCursors[h];
                var p = _rayPanel[h];
                bool on = p != null && p.Shown;
                if (ray.enabled != on) ray.enabled = on;
                if (cur.enabled != on) cur.enabled = on;
                if (!on) continue;
                var hs = UiHand(h);
                Quaternion lie = p == _menu.panel ? FanFacing(_rayAt[h]) : p.transform.rotation;      // (on the fan the cursor lies on the curve)
                Vector3 n = lie * Vector3.forward, hit = _rayAt[h] - n * 0.006f;
                Vector3 dir = hit - _rayFrom[h]; float len = dir.magnitude;
                if (len < 1e-3f) { ray.enabled = false; continue; }
                dir /= len;
                // the ray: from a hand's width in front of the hand to just short of the cursor, strongest at the cursor
                Vector3 a = _rayFrom[h] + dir * Mathf.Min(0.07f, len * 0.3f), b = hit - dir * 0.010f, mid = (a + b) * 0.5f;
                Vector3 face = mid - head; face -= dir * Vector3.Dot(face, dir);
                ray.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(face.sqrMagnitude > 1e-6f ? face.normalized : n, dir));
                ray.transform.localScale = new Vector3(0.0034f, Vector3.Distance(a, b), 1f);
                bool hot = _rayHot[h] != null;
                float alpha = hot ? 0.90f : 0.60f;
                ray.SetColors(new Color(1f, 0.97f, 0.88f, alpha), new Color(1f, 0.97f, 0.88f, 0f));
                // the cursor: a small round paper dot with an ink edge; it draws together as the fingers close
                float size = Mathf.Lerp(0.0165f, 0.0095f, Mathf.Clamp01(hs.pinch ? 1f : hs.pinchStrength * 0.6f));
                cur.transform.SetPositionAndRotation(hit, lie);
                Ui.SizePanel(cur, size, size, size * 0.5f, 0.0016f);
                Color fill = hs.pinch ? new Color(1f, 0.80f, 0.36f, 1f) : hot ? new Color(1f, 0.90f, 0.62f, 0.98f) : new Color(1f, 0.99f, 0.95f, 0.96f);
                cur.SetColors(fill, fill);
            }
            if (Time.time >= _dbgRayPinchAt && Time.time < _dbgRayUntil) _dbgRayPinched = true;
        }

        /// <summary>Is this point of a board (given in the button's own space) on the button? A rectangle for pills, cards and tiles, a disc for round buttons.</summary>
        static bool OnButton(UiButton b, Vector3 local, float grow = 1f)
        {
            if (b.half.x > 0f && b.half.y > 0f) return Mathf.Abs(local.x) < b.half.x * grow && Mathf.Abs(local.y) < b.half.y * grow;
            return new Vector2(local.x, local.y).magnitude < b.radius * 0.9f * grow;
        }

        /// <summary>
        /// Does this fingertip press the button in this frame? A press is the tip going THROUGH the face of the button,
        /// from the side the user is on, inside the button's outline, moving into it rather than along it. Being near
        /// it is not a press (first run on the headset, 2026-11-16: the fingers of the hand that held the menu, and
        /// fingers on their way to another button, pressed whatever they passed). The simulator's scripted fingertip
        /// is put on the button without travelling there, so for it the tip being on the button is the press.
        /// </summary>
        bool Pokes(in HandState hs, Vector3 prevTip, UiButton b, out bool hover, out float depth)
        {
            Transform button = b.transform;
            Quaternion inv = Quaternion.Inverse(button.rotation);
            Vector3 cur = inv * (hs.indexTip - button.position), prev = inv * (prevTip - button.position);      // the button's space: +z is away from the user
            depth = cur.z;
            bool over = OnButton(b, cur);
            hover = over && cur.z > -PokeHover && cur.z < 0.015f;
            if (Input.Simulated) return over && Mathf.Abs(cur.z) < 0.02f;
            if (!(prev.z < PokeFace && cur.z >= PokeFace && cur.z < PokeDeep)) return false;       // did not go through the face in this frame
            Vector3 step = cur - prev;
            if (step.sqrMagnitude > 0.0064f || prev.z < -0.12f) return false;                      // a jump of the tracking, not a finger
            if (step.z < 0.35f * new Vector2(step.x, step.y).magnitude) return false;              // sliding along the board, not pushing into it
            Vector3 through = Vector3.Lerp(prev, cur, Mathf.InverseLerp(prev.z, cur.z, PokeFace));    // where it went through the face
            return OnButton(b, through);
        }

        /// <summary>Presses a button now (a finger, a pinch on the ray, or a check from the PC): it dips, it is heard, it does what it does.</summary>
        bool PressButton(UiPanel p, UiButton b)
        {
            if (b.cool > 0f || b.intro < 0.95f) return false;
            b.cool = 0.4f; b.punch = 1f;
            // every press is heard (a finger merely near a button is not): a soft tick - or, on the menu, a pop where the tile is
            if (p == _menu.panel || p == _menu.chipPanel) Sfx("pop", b.transform.position, 0.6f); else Sfx2D("tap", 0.5f);
            try { b.press?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            return true;
        }

        /// <summary>
        /// Called once a frame by every board that is shown: moves the board's pop-in on, lights its buttons up under a
        /// finger or the cursor, and presses them. skipHand (0 right, 1 left) is a hand that cannot press this board -
        /// the one a panel sits on. pointable = false: only touch presses it. floating = true: its buttons float over a
        /// hand and are pressed by a fingertip coming within their radius, from any side (the Stop chip on the open left palm).
        /// </summary>
        void UpdateButtons(UiPanel p, float dt, int skipHand = -1, bool pointable = true, bool floating = false)
        {
            if (!p.Shown) return;
            p.Animate(dt, Time.time);
            if (pointable) _uiSeen.Add(p);
            Vector3 panelAt = p.transform.position; float moved = (panelAt - p.lastPos).magnitude; bool settled = p.appear >= 1f && moved < 0.012f; p.lastPos = panelAt;
            var btns = p.buttons;
            for (int bi = 0; bi < btns.Length; bi++)
            {
                var b = btns[bi];
                b.cool -= dt;
                if (!b.gameObject.activeInHierarchy) continue;
                Transform bt = b.transform;
                bool hit = false, hover = false;
                for (int h = 0; h < 2; h++)
                {
                    if (h == skipHand) continue;
                    var hs = UiHand(h);
                    if (!hs.tracked) continue;
                    if (!HandBusy(h, hs))
                    {
                        bool poke, near;
                        if (floating)
                        {
                            float away = Vector3.Distance(hs.indexTip, bt.position);
                            near = away < b.radius + PokeHover; poke = away < b.radius;
                        }
                        else poke = Pokes(hs, h == 0 ? _lastTipR : _lastTipL, b, out near, out float _);
                        if (_pokeLock[h] != null) { if (_pokeLock[h] == b) hover = true; }      // (that finger is still in a button: it lights that one up and presses nothing)
                        else
                        {
                            if (near) hover = true;
                            if (poke)
                            {
                                // one poke, one press: whatever this finger went through, it has to come back out first
                                _pokeLock[h] = b; _pokeLockAt[h] = Time.time;
                                // (a floating button: it has come up, and neither it nor the fingertip is being swung past the other)
                                if (floating ? Input.Simulated || (p.appear >= 0.6f && moved < 0.02f && (hs.indexTip - (h == 0 ? _lastTipR : _lastTipL)).sqrMagnitude < 0.03f * 0.03f) : settled || Input.Simulated) hit = true;
                            }
                        }
                    }
                    if (pointable && _rayPanel[h] == p && OnButton(b, Quaternion.Inverse(bt.rotation) * (_rayAt[h] - bt.position), 1f + b.hoverGrow * b.hover))
                    {
                        hover = true;
                        if (_rayHotWas[h] != b) _rayHotT[h] = 0f;
                        _rayHot[h] = b;
                        if (hs.pinchDown && p.appear >= 1f && (Input.Simulated && Time.time >= _dbgRayUntil || _rayHotT[h] >= RayDwell)) hit = true;
                    }
                }
                b.hover = Mathf.MoveTowards(b.hover, hover ? 1f : 0f, dt * 9f);
                b.punch = Mathf.MoveTowards(b.punch, 0f, dt * 4.5f);
                float s = 1f + b.hoverGrow * M.Smooth(b.hover) - 0.14f * Mathf.Sin(b.punch * Mathf.PI);
                if (!b.homeSet) { b.home = bt.localPosition; b.homeSet = true; }
                bt.localPosition = b.home + new Vector3(0f, b.bob, 0f);
                bt.localScale = Vector3.one * (s * b.size * Mathf.Max(0.001f, Ui.Pop(b.intro)));
                b.bg.phase = Time.time * 1.7f + bt.localPosition.x * 9f;
                b.bg.SetColors(Color.Lerp(b.color, Color.white, 0.30f * b.hover), Color.Lerp(b.color2, b.color, 0.5f * b.hover));
                if (hit) PressButton(p, b);
            }
        }
    }
}
