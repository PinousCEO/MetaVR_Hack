using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Picking the partner up. Thumb and finger of either hand, closed on the partner and kept closed for a moment,
    /// take hold of it by the back of its hood - the scruff of its raincoat: it hangs from the pinch, swings under the
    /// hand like a pendulum (lagging behind when the hand moves, swinging back when it stops), arms and legs dangling,
    /// and goes wherever the hand goes. Let go over the desk it drops, lands with a squash and a little bounce, stands
    /// up - and lives there from now on (its home spot, as when it is called over). Let go far from any desk it floats
    /// back to where it lived.
    /// A resting hand closes thumb and finger by itself now and then, so nothing but this starts it: the pinch has to
    /// close within 5 cm of its body and stay closed for 0.15 s. While it is held it is in a mode of its own (Held):
    /// no hop onto an open palm, no workbench, no picking up of things, no hand menu on the hand that holds it.
    /// </summary>
    public sealed partial class MrApp
    {
        // where it is held, in its own measure (its height = 1; x right, y up, z the way it faces): the back of the hood,
        // a little behind and below the top of its head
        static readonly Vector3 HoldGrip = new Vector3(0f, 0.90f, -0.22f);
        const float HoldReach = 0.05f, HoldSettle = 0.15f;      // the guard against a pinch that was not meant: how near, for how long
        const float HoldLean = 10f;                             // held by the scruff it tips forward a little (degrees)
        // the pendulum: how fast it swings (rad/s), how soon it comes to rest, the length that turns the body's
        // sideways lag into an angle (metres), and the most it tilts (degrees)
        const float SwingRate = 10f, SwingDamp = 3.6f, SwingArm = 0.06f, SwingMax = 55f;

        int _holdHand = -1;                  // which hand holds it: 0 right, 1 left, 2 the simulator's scripted point; -1 nobody
        int _holdPhase;                      // 0 not held, 1 hanging from the pinch, 2 falling, 3 landing, 4 floating to where it will stand
        readonly float[] _holdWait = { -1f, -1f };      // per hand: for how long a pinch that closed on it has stayed closed (-1: no such pinch)
        float _holdT, _holdLostT, _holdYaw, _holdSimAt;
        Vector3 _holdPoint, _holdVel;        // where the pinch is (a little smoothed), and how fast it moves
        Vector2 _bob, _bobVel;               // the pendulum: where the body's weight is over the floor (x, z), and how fast it moves
        Vector3 _fallVel, _floatFrom, _floatTo; Quaternion _floatRot = Quaternion.identity;
        bool _dropNewHome;                   // where it comes down becomes its home spot

        /// <summary>How far a point is from the partner's body: a capsule from its feet to the top of its head, a third of its height thick. 0 inside it.</summary>
        float HoldDistance(Vector3 q)
        {
            Vector3 a = _pos, ab = _rig.HeadTopWorld - a;
            float u = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
            return Mathf.Max(0f, Vector3.Distance(q, a + ab * u) - _H / 3f);
        }

        /// <summary>Called every frame the partner is out in the room: does a pinch take hold of it?</summary>
        void UpdateHold(float dt)
        {
            if (_mode != Mode.Held && _holdPhase != 0) { _holdPhase = 0; _holdHand = -1; }      // (something else took over: a crossing)
            if (_mode != Mode.Free) { _holdWait[0] = _holdWait[1] = -1f; return; }
            // only out on the desk with nothing going on: no crossing, no game, no calibration; and not while the same
            // pinch has picked up a thing or turns the menu. (The simulator's scripted hand does not pick it up: there
            // it is the "hold" command.)
            bool may = !Input.Simulated && _ch.owner == Env.Mr && _rig.Visible && TransitionIdle && _game.Length == 0 && !_tn.active && _tnPendingAt < 0f
                       && !_calibrating && _grab < 0 && !_menuDrag;
            for (int h = 0; h < 2; h++)
            {
                var hs = h == 0 ? Input.Right : Input.Left;
                if (!may || !hs.tracked || !hs.pinch) { _holdWait[h] = -1f; continue; }
                Vector3 q = (hs.indexTip + hs.thumbTip) * 0.5f;
                float d = HoldDistance(q);
                if (hs.pinchDown) _holdWait[h] = d <= HoldReach ? 0f : -1f;
                else if (_holdWait[h] >= 0f)
                {
                    _holdWait[h] += dt;
                    if (d > HoldReach + 0.04f) _holdWait[h] = -1f;                       // the hand went on its way
                    else if (_holdWait[h] >= HoldSettle) { HoldBegin(h, q); return; }
                }
            }
        }

        void HoldBegin(int hand, Vector3 pinch)
        {
            _mode = Mode.Held; _holdHand = hand; _holdPhase = 1; _holdT = 0f; _holdLostT = 0f;
            _holdWait[0] = _holdWait[1] = -1f;
            // The hood is where it is held. It starts where the hood is now and is drawn to the fingers in a moment, so
            // that nothing jumps.
            _holdPoint = _pos + _rot * (HoldGrip * _H);
            _holdVel = Vector3.zero;
            _bob = new Vector2(_holdPoint.x, _holdPoint.z); _bobVel = Vector2.zero;
            _holdYaw = _rot.eulerAngles.y;
            _handSteadyT = 0f;
            _motor.Stop();
            _actPhase = 0; _actFor = "";      // (lifted out of whatever it was doing - off its chair: put down again, it walks back to it, it is not suddenly there)
            _sparks.Burst(pinch, Input.HeadRot * Vector3.right, Vector3.up, 0.03f, 6);
            Sfx("squeak", pinch, 0.7f);
            // (the same pinch, as it closed, also asked it to listen: that was not what was meant)
            if (_voice != null && _voice.Listening && Time.time - _talkPinchAt < 0.5f) _voice.StopListening("picked up instead", false);
            Debug.Log("[MR] hold: picked up by " + (hand == 0 ? "the right hand" : hand == 1 ? "the left hand" : "the simulator"));
        }

        /// <summary>The pendulum, one frame on: the body's weight is drawn to the point under the hand and overshoots it. Returns how far it is from under the hand (x, z).</summary>
        Vector2 Swing(float dt, Vector3 from, Vector3 to)
        {
            Vector2 p0 = new Vector2(from.x, from.z), p1 = new Vector2(to.x, to.z);
            Vector2 handVel = (p1 - p0) / Mathf.Max(dt, 1e-4f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 14);      // small steps: a long frame must not make it fly off
            float h = dt / steps;
            for (int k = 1; k <= steps; k++)
            {
                Vector2 p = Vector2.Lerp(p0, p1, k / (float)steps);
                Vector2 acc = -(SwingRate * SwingRate) * (_bob - p) - SwingDamp * (_bobVel - handVel);
                _bobVel += acc * h;
                _bob += _bobVel * h;
            }
            Vector2 off = _bob - p1;
            float most = SwingArm * Mathf.Sin(SwingMax * Mathf.Deg2Rad), far = off.magnitude;
            if (far > most)
            {
                // as far out as it swings: it is carried along from there
                Vector2 n = off / far;
                off = n * most; _bob = p1 + off;
                float outward = Vector2.Dot(_bobVel - handVel, n);
                if (outward > 0f) _bobVel -= n * outward;
            }
            return off;
        }

        /// <summary>Its place and pose while it is held, falling, landing or floating home. (Free calls this instead of everything else it does.)</summary>
        void HeldPose(float dt)
        {
            Vector3 up = Vector3.up;
            float time = Time.time;
            _holdT += dt;
            Quaternion upright = Quaternion.Euler(0f, _holdYaw, 0f);
            switch (_holdPhase)
            {
                case 1:      // hanging from the pinch
                {
                    Vector3 q = _holdPoint; bool letGo = false;
                    if (_holdHand == 2) q = SimHoldPoint();
                    else
                    {
                        var hs = _holdHand == 0 ? Input.Right : Input.Left;
                        if (!hs.tracked) { _holdLostT += dt; letGo = _holdLostT > 0.3f; }      // not seen for a moment: it stays where it was; for longer: let go
                        else { _holdLostT = 0f; if (hs.pinch) q = (hs.indexTip + hs.thumbTip) * 0.5f; else letGo = true; }
                    }
                    Vector3 was = _holdPoint;
                    _holdPoint = Vector3.Lerp(_holdPoint, q, 1f - Mathf.Exp(-dt * Mathf.Lerp(12f, 40f, _holdT / 0.3f)));
                    _holdVel = Vector3.Lerp(_holdVel, (_holdPoint - was) / Mathf.Max(dt, 1e-4f), 0.4f);
                    Vector2 off = Swing(dt, was, _holdPoint);
                    // it turns, slowly, so that the user sees its face
                    Vector3 toUser = Input.HeadPos - _holdPoint; toUser.y = 0f;
                    if (toUser.sqrMagnitude > 1e-4f) _holdYaw = Mathf.MoveTowardsAngle(_holdYaw, YawTo(toUser), 120f * dt);
                    // the body hangs from the hood towards where its weight is
                    Vector3 hang = new Vector3(off.x, -Mathf.Sqrt(Mathf.Max(1e-6f, SwingArm * SwingArm - off.sqrMagnitude)), off.y);
                    _rot = Quaternion.FromToRotation(Vector3.down, hang.normalized) * Quaternion.Euler(HoldLean, _holdYaw, 0f);
                    _pos = _holdPoint - _rot * (HoldGrip * _H);
                    // the swing as the body feels it (to its right, forwards): the limbs trail behind it
                    Vector3 own = Quaternion.Inverse(Quaternion.Euler(0f, _holdYaw, 0f)) * new Vector3(off.x, 0f, off.y);
                    float swingX = Mathf.Asin(Mathf.Clamp(own.x / SwingArm, -1f, 1f)) * Mathf.Rad2Deg, swingZ = Mathf.Asin(Mathf.Clamp(own.z / SwingArm, -1f, 1f)) * Mathf.Rad2Deg;
                    // a fright for the first 0.4 s (wide eyes, arms flung out, legs pedalling), then it settles and waits
                    PoseLib.Dangle(_pose, time, swingX, swingZ, 1f - Mathf.Clamp01((_holdT - 0.4f) / 1.2f));
                    if (letGo) HoldRelease();
                    break;
                }
                case 2:      // let go over the desk: it falls
                {
                    _fallVel.y -= 9f * dt;
                    float keep = Mathf.Exp(-dt * 2f);
                    _fallVel.x *= keep; _fallVel.z *= keep;
                    _pos += _fallVel * dt;
                    _rot = Quaternion.Slerp(_rot, upright, 1f - Mathf.Exp(-dt * 12f));
                    PoseLib.Dangle(_pose, time, 0f, 0f, 1f);
                    if (_pos.y <= _deskY) { _pos.y = _deskY; HoldLand(true); }
                    break;
                }
                case 3:      // on the desk: a squash, a little bounce, and it stands up (the landing of a hop)
                {
                    float a = _holdT < 0.06f ? 1f : 1f - M.Smooth((_holdT - 0.06f) / 0.34f);
                    PoseLib.Land(_pose, a);
                    _pos.y = _deskY + 0.016f * Mathf.Sin(Mathf.Clamp01((_holdT - 0.06f) / 0.22f) * Mathf.PI);
                    _rot = Quaternion.Slerp(_rot, upright, 1f - Mathf.Exp(-dt * 16f));
                    if (_holdT >= 0.46f) HoldEnd();
                    break;
                }
                default:     // let go where there is no desk under it: it floats, gently, to where it will stand
                {
                    float span = Mathf.Clamp(0.9f + Vector3.Distance(_floatFrom, _floatTo) * 0.8f, 1.0f, 2.4f);
                    float u = Mathf.Clamp01(_holdT / span);
                    _pos = Vector3.Lerp(_floatFrom, _floatTo, M.Smooth(u)) + up * (0.04f * Mathf.Sin(u * Mathf.PI));
                    _rot = Quaternion.Slerp(_floatRot, upright, M.Smooth(u * 3f));
                    PoseLib.Balance(_pose, time, 0.6f);
                    if ((Time.frameCount % 6) == 0) _sparks.Trail(_pos + up * (_H * 0.5f), _H * 0.35f);
                    if (u >= 1f) { _pos = _floatTo; HoldLand(false); }
                    break;
                }
            }
        }

        /// <summary>Is this point over the work area - within half a metre of the desk round the partner's home spot or of a screen?</summary>
        bool NearWorkArea(Vector3 p)
        {
            const float reach = 0.85f;      // half a metre beyond a work area that is itself about 35 cm round its middle
            var q = new Vector2(p.x, p.z);
            if (Vector2.Distance(q, new Vector2(_home.x, _home.z)) <= reach) return true;
            foreach (var mv in _mons.Values) if (Vector2.Distance(q, new Vector2(mv.Origin.x, mv.Origin.z)) <= reach) return true;
            return false;
        }

        /// <summary>The pinch opened (or the hand has not been seen for a while).</summary>
        void HoldRelease()
        {
            float above = _pos.y - _deskY;
            bool overDesk = Mathf.Abs(above) <= 0.5f && NearWorkArea(_pos);
            Vector3 spot = new Vector3(_pos.x, _deskY, _pos.z);
            _holdT = 0f; _dropNewHome = overDesk;
            if (overDesk && above > 0.004f)
            {
                // it drops, taking a little of the hand's movement with it
                _holdPhase = 2;
                _fallVel = Vector3.ClampMagnitude(new Vector3(_holdVel.x, 0f, _holdVel.z), 0.4f) + Vector3.up * Mathf.Clamp(_holdVel.y, -1f, 0.6f);
            }
            else if (overDesk && above > -0.03f) { _pos.y = _deskY; HoldLand(false); }      // set down on the desk
            else
            {
                // under the desk's height: up to the spot above it. Far from any desk: back to where it lived.
                _holdPhase = 4; _floatFrom = _pos; _floatRot = _rot;
                _floatTo = overDesk ? spot : new Vector3(_home.x, _deskY, _home.z);
            }
            Debug.Log($"[MR] hold: let go {above * 100f:0} cm above the desk, " + (overDesk ? (_holdPhase == 2 ? "it drops there" : "it is put down there") : "too far from the desk: it floats home"));
        }

        void HoldLand(bool hard)
        {
            _holdPhase = 3; _holdT = 0f;
            Vector3 right = Input.HeadRot * Vector3.right; right.y = 0f;
            _sparks.Burst(_pos + Vector3.up * 0.003f, right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right, Vector3.up, _H * 0.26f, hard ? 7 : 4);
            Sfx("land", _pos, hard ? 0.7f : 0.4f);
        }

        /// <summary>It stands again: back to its usual self, at this spot.</summary>
        void HoldEnd()
        {
            Vector3 spot = new Vector3(_pos.x, _deskY, _pos.z);
            _holdPhase = 0; _holdHand = -1; _mode = Mode.Free;
            _pos = spot; _rot = Quaternion.Euler(0f, _holdYaw, 0f);
            _motor.Teleport(new Vector2(spot.x, spot.z), _holdYaw);
            // where it was put down is where it lives now - the same as when it is called over ("come here")
            if (_dropNewHome) { if (_homeSet) _worldShift += _home - spot; _home = spot; _homeSet = true; Debug.Log($"[MR] hold: its home spot is now {spot.ToString("0.000")}"); }
            _motor.Face(YawTo(Input.HeadPos - spot));
        }

        /// <summary>Something that cannot wait begins (it is sent into a screen): it is on the desk under the hand at once.</summary>
        void HoldAbort()
        {
            if (_mode != Mode.Held) return;
            _pos = new Vector3(_pos.x, _deskY, _pos.z); _rot = Quaternion.Euler(0f, _holdYaw, 0f);
            _holdPhase = 0; _holdHand = -1; _mode = Mode.Free;
            _motor.Teleport(new Vector2(_pos.x, _pos.z), _holdYaw);
        }

        /// <summary>The simulator's "hand": a point 20 cm above the desk in front of the user that moves in a slow figure of eight.</summary>
        Vector3 SimHoldPoint()
        {
            Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float t = (Time.time - _holdSimAt) * 1.1f;
            // (round the middle of the things on the desk, where the simulator's fixed camera looks - 42 cm in front of the
            // head was under the lower edge of its picture)
            Vector3 mid = _worldFrame ? new Vector3(_worldHome.x, _deskY, _worldHome.z) : new Vector3(Input.HeadPos.x, _deskY, Input.HeadPos.z) + fwd * 0.8f;
            return mid + Vector3.up * 0.20f + fwd * (0.05f * Mathf.Sin(2f * t)) + right * (0.14f * Mathf.Sin(t));
        }

        bool OnDebugHold(DebugCommand d)
        {
            // (captures from the PC: lpctl debug hold 1 - it is picked up and carried round in a figure of eight -, hold 0 - let go)
            if (d.cmd != "hold") return false;
            if (d.value > 0.5f)
            {
                if (_mode == Mode.Free && _ch.owner == Env.Mr && _rig.Visible) { _holdSimAt = Time.time; HoldBegin(2, _pos + Vector3.up * (_H * 0.9f)); }
                else Debug.Log("[MR] hold: it can only be picked up while it is out on the desk (it is " + _mode + ", in " + _ch.owner + ")");
            }
            else if (_mode == Mode.Held && _holdPhase == 1) HoldRelease();
            return true;
        }
    }
}
