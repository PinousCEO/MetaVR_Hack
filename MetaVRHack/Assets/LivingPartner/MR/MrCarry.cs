using System;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Carrying a thing. "Grabbing and dragging things is good, but I cannot ROTATE them, and when there is a table I
    /// cannot put a thing ON the table - so collisions need handling" (the user, 2026-11-17). A thing that is held by a
    /// pinch (MrWorld picks it up) now also:
    ///  - TURNS with the hand: what the hand has turned about the vertical since the pinch closed is added to the
    ///    thing's yaw, one and a half times (60 degrees of the wrist = 90 degrees; the first 5 degrees do nothing, so
    ///    that a thing that is only carried is not turned). When the OTHER hand pinches within 6 cm of the thing too,
    ///    the thing turns with the line between the two pinches instead (one to one). Let go within 5 degrees of a
    ///    multiple of 15 it settles on that multiple. While it turns, a ring of ticks (one every 15 degrees) lies
    ///    under it, with an arc from where it stood to where it stands now and a dot at its heading.
    ///  - STANDS ON other things: over a thing that can be stood on - a table, or a desk / bed / sofa / box / speaker /
    ///    record player, or anything whose footprint is at least 1.3 times its own - it is lifted to that thing's top
    ///    and a soft ring shows where it will land. Let go there it stands on it: the PC is told how high above the
    ///    desk (WorldCommand.y) and on what (onId), and from then on it goes along when that thing is moved.
    ///  - does NOT end up INSIDE another thing: let go where it would stand in something it cannot stand on, it slides
    ///    out the shortest way (up to eight steps, then the nearest free spot round about) before the PC is told, and
    ///    is seen gliding there. While it is carried nothing is pushed about.
    ///  - moves the partner out of the way: put down where the partner stands, the partner hops aside.
    /// Footprints are boxes (the shape's own width and depth, turned with the thing), 6 % smaller than they are so that
    /// two things can stand side by side.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Carrying a thing")]
        [Tooltip("Under a thing while it is turned: a tick every 15 degrees, the arc it has turned through, a dot at its heading.")]
        [SerializeField] FxQuad _carryTicks;
        [SerializeField] FxQuad _carryArc, _carryDot;
        [Tooltip("On the top of the thing the carried thing would stand on.")]
        [SerializeField] FxQuad _landRing;
        [Tooltip("The soft glow on the open other palm while the carried thing is near enough to be let go into the bag there.")]
        [SerializeField] FxQuad _palmGlow;

        const float CarryYawGain = 1.5f, CarryDeadTurn = 5f, CarrySnapStep = 15f, CarrySnapWithin = 5f;      // degrees
        const float CarrySecondReach = 0.06f, CarryStandRatio = 1.3f, CarryFootShrink = 0.94f;
        const float CarryPalmNear = 0.12f;      // let go this near to the open other palm, a thing goes into the bag

        /// <summary>The simulator's scripted hand may pick things up (there a pinch is otherwise a click of the mouse).</summary>
        [NonSerialized] public bool SimCarry;

        float _grabYaw, _grabYawWant, _grabYaw0;      // the carried thing's yaw as drawn, as asked for, and as it was when picked up (degrees, on the desk)
        float _grabHandTurn, _grabTwoTurn;            // what the hand has turned since then; what two hands have turned it by
        Quaternion _grabHandRot; bool _grabHandRotSet;
        bool _grabTwo; float _grabLineYaw;
        string _grabOn = ""; float _grabOnY;          // what it is over and would stand on, and how high above the desk that top is
        float _grabTurnedAt = -10f, _carryRingShow, _landShow;
        Vector3 _carryRingAt, _landAt; float _carryRingSize, _landSize;
        float _dropYaw, _dropYawShown, _dropY;        // (MrWorld: the thing that was just let go) how it is turned, and how high it stands
        float _palmGlowShow; Vector3 _palmGlowAt, _palmGlowN = Vector3.up; bool _palmOver;

        static bool Has(ItemDef d, string use) => d.uses != null && Array.IndexOf(d.uses, use) >= 0;
        Quaternion DeskRot(float yaw) => Quaternion.LookRotation(_worldFwd, Vector3.up) * Quaternion.Euler(0, yaw, 0);
        /// <summary>A point of the room in the desk's own measure: metres to the user's right of the partner's spot, metres away from the user.</summary>
        Vector2 DeskXZ(Vector3 world) { Vector3 rel = world - _worldHome; return new Vector2(Vector3.Dot(rel, _worldRight), Vector3.Dot(rel, _worldFwd)); }
        Vector3 DeskPoint(Vector2 xz, float y = 0f) => new Vector3(_worldHome.x, _deskY, _worldHome.z) + _worldRight * xz.x + _worldFwd * xz.y + Vector3.up * y;
        /// <summary>Where a thing stands, at its own height (ItemPos is the spot on the desk under it).</summary>
        Vector3 ItemPosY(ItemDef d) => ItemPos(d) + Vector3.up * d.y;

        /// <summary>Half the width and half the depth of a thing's footprint, in metres, in its own frame.</summary>
        Vector2 FootHalf(PropView p, ItemDef d)
        {
            var m = p != null ? p.Mesh : null;
            if (m == null) return Vector2.one * (d.sizeM * 0.5f);
            Bounds b = m.bounds;
            return new Vector2(Mathf.Max(0.08f, b.extents.x + Mathf.Abs(b.center.x)), Mathf.Max(0.08f, b.extents.z + Mathf.Abs(b.center.z))) * d.sizeM;
        }

        /// <summary>How tall a thing stands, in metres.</summary>
        static float TallOf(PropView p, ItemDef d) => Mathf.Max(0.004f, (p != null ? p.height : 1f) * d.sizeM);

        /// <summary>How high above its own foot a thing can be stood on: a table's top, a seat, a mattress - otherwise its top.</summary>
        float StandTop(ItemDef o) => TopOf(o, (Has(o, ItemUses.Sit) || Has(o, ItemUses.Sleep)) && !Has(o, ItemUses.Table));

        /// <summary>The thing at the bottom of the pile a thing stands on (itself when it stands on the desk).</summary>
        ItemDef BaseOf(ItemDef d)
        {
            for (int k = 0; k < 4 && d != null && !string.IsNullOrEmpty(d.onId); k++)
            {
                var b = Item(d.onId);
                if (b == null || !b.inRoom) break;
                d = b;
            }
            return d;
        }

        /// <summary>Does a thing stand on this one - directly, or on something that does?</summary>
        bool StandsOn(ItemDef d, string baseId)
        {
            for (int k = 0; k < 4 && d != null && !string.IsNullOrEmpty(d.onId); k++)
            {
                if (d.onId == baseId) return true;
                d = Item(d.onId);
            }
            return false;
        }

        /// <summary>
        /// Where the partner goes to be AT a thing: gap metres beyond its edge in the direction dir. For a thing that
        /// stands on another (a book on the table) that is the edge of what it stands on - the partner does not walk
        /// through the table to get to the book.
        /// </summary>
        Vector3 UseSpot(ItemDef d, Vector3 dir, float gap)
        {
            dir.y = 0f; dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : -_worldFwd;
            var root = BaseOf(d);
            if (root == d) return ItemPos(d) + dir * (d.sizeM * 0.5f + gap);
            Vector2 half = FootHalf(PropOf(root.id), root), n = new Vector2(Vector3.Dot(dir, _worldRight), Vector3.Dot(dir, _worldFwd));
            float reach = half.x * Mathf.Abs(Vector2.Dot(AxisX(root.yaw), n)) + half.y * Mathf.Abs(Vector2.Dot(AxisZ(root.yaw), n));
            return ItemPos(root) + dir * (reach + gap);
        }

        // ------------------------------------------------------------------ footprints
        static Vector2 AxisX(float yaw) { float r = yaw * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(r), -Mathf.Sin(r)); }
        static Vector2 AxisZ(float yaw) { float r = yaw * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(r), Mathf.Cos(r)); }

        /// <summary>Two boxes on the desk (middle, half sizes, yaw): do they overlap, and what is the shortest move of the first that takes it out of the second?</summary>
        static bool BoxPush(Vector2 ca, Vector2 ha, float ya, Vector2 cb, Vector2 hb, float yb, out Vector2 push)
        {
            Vector2 a0 = AxisX(ya), a1 = AxisZ(ya), b0 = AxisX(yb), b1 = AxisZ(yb), d = ca - cb;
            float best = float.MaxValue; push = Vector2.zero;
            for (int k = 0; k < 4; k++)
            {
                Vector2 n = k == 0 ? a0 : k == 1 ? a1 : k == 2 ? b0 : b1;
                float ra = ha.x * Mathf.Abs(Vector2.Dot(a0, n)) + ha.y * Mathf.Abs(Vector2.Dot(a1, n));
                float rb = hb.x * Mathf.Abs(Vector2.Dot(b0, n)) + hb.y * Mathf.Abs(Vector2.Dot(b1, n));
                float along = Vector2.Dot(d, n), over = ra + rb - Mathf.Abs(along);
                if (over <= 0f) return false;
                if (over < best) { best = over; push = n * (along >= 0f ? over : -over); }
            }
            return true;
        }

        /// <summary>Would thing `self`, standing at `at` (desk measure) turned by `yaw` at height y on `onId`, stand in another thing? push: the way out of the first one found.</summary>
        bool Blocked(int self, ItemDef d, Vector2 at, float yaw, float y, string onId, out Vector2 push)
        {
            push = Vector2.zero;
            var p = _props[self];
            Vector2 half = FootHalf(p, d) * CarryFootShrink; float tall = TallOf(p, d);
            for (int j = 0; j < _props.Length; j++)
            {
                if (j == self) continue;
                var q = _props[j]; var o = q.itemId.Length > 0 ? Item(q.itemId) : null;
                if (o == null || !o.inRoom || o.id == onId || o.id == _heldItem || StandsOn(o, d.id)) continue;      // (what it stands on, and what stands on it, is not in its way)
                float ot = TallOf(q, o);
                if (y >= o.y + ot - 0.003f || o.y >= y + tall - 0.003f) continue;                                    // one is above the other
                if (BoxPush(at, half, yaw, new Vector2(o.x, o.z), FootHalf(q, o) * CarryFootShrink, o.yaw, out push)) return true;
            }
            return false;
        }

        /// <summary>Where a thing that is let go at `at` comes to stand: the nearest spot at which it stands in nothing.</summary>
        Vector2 FreeSpot(int self, ItemDef d, Vector2 at, float yaw, float y, string onId)
        {
            Vector2 c = at;
            for (int it = 0; it < 8; it++)
            {
                if (!Blocked(self, d, c, yaw, y, onId, out Vector2 push)) return c;
                c += push + push.normalized * 0.003f;
            }
            // wedged between things: the nearest free spot on rings round where it was let go
            for (int ring = 1; ring <= 8; ring++)
                for (int k = 0; k < 16; k++)
                {
                    float a = (k + (ring & 1) * 0.5f) / 16f * Mathf.PI * 2f;
                    Vector2 t = at + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (ring * 0.025f);
                    if (!Blocked(self, d, t, yaw, y, onId, out _)) return t;
                }
            return c;
        }

        /// <summary>May `d` be put on top of `o`?</summary>
        bool CanStandOn(ItemDef o, PropView q, ItemDef d, PropView p)
        {
            if (o.status == "making" || o.status == "failed" || StandsOn(o, d.id)) return false;
            Vector2 ho = FootHalf(q, o), hd = FootHalf(p, d);
            float ao = ho.x * ho.y, ad = hd.x * hd.y;
            bool top = Has(o, ItemUses.Table) || o.kind == "desk" || o.kind == "bed" || o.kind == "sofa" || o.kind == "box" || o.kind == "speaker" || o.kind == "record";
            return top && ad <= ao * 3f || ao >= ad * CarryStandRatio;
        }

        /// <summary>The thing under this point of the desk that `self` could stand on (the highest, when there are several); -1: only the desk. topY: how high above the desk it would stand.</summary>
        int StandOn(int self, ItemDef d, Vector2 at, out float topY)
        {
            int best = -1; topY = 0f;
            var p = _props[self];
            for (int j = 0; j < _props.Length; j++)
            {
                if (j == self) continue;
                var q = _props[j]; var o = q.itemId.Length > 0 ? Item(q.itemId) : null;
                if (o == null || !o.inRoom || !q.Visible || o.id == _heldItem || !CanStandOn(o, q, d, p)) continue;
                Vector2 rel = at - new Vector2(o.x, o.z), half = FootHalf(q, o);
                if (Mathf.Abs(Vector2.Dot(rel, AxisX(o.yaw))) > half.x || Mathf.Abs(Vector2.Dot(rel, AxisZ(o.yaw))) > half.y) continue;
                float top = o.y + StandTop(o);
                if (best < 0 || top > topY) { best = j; topY = top; }
            }
            return best;
        }

        // ------------------------------------------------------------------ the open other palm is the bag
        /// <summary>
        /// "Can a placed object not be put back?": a carried thing that is let go at the OPEN other palm goes into the
        /// bag. The palm: seen, not a fist and not pinching, turned up or towards the face; the thing (its middle, or
        /// the pinch that holds it) within CarryPalmNear of it. While that holds the palm glows (UpdateCarryFx).
        /// </summary>
        bool CarryOverPalm(PropView gp, ItemDef gd, in HandState other, in HandState hs)
        {
            if (gp == null || gd == null || !other.tracked || other.pinch) return false;
            if (other.fingersKnown && Bits((other.fingers >> 1) & 15) < 2) return false;
            Vector3 toHead = (Input.HeadPos - other.palmPos).normalized;
            if (Vector3.Dot(other.palmNormal, Vector3.up) < 0.3f && Vector3.Dot(other.palmNormal, toHead) < 0.3f) return false;
            Vector3 palm = other.palmPos + other.palmNormal * 0.02f, mid = gp.shown + Vector3.up * (TallOf(gp, gd) * 0.5f);
            float near = Vector3.Distance(palm, mid);
            if (hs.tracked) near = Mathf.Min(near, Vector3.Distance(palm, (hs.indexTip + hs.thumbTip) * 0.5f));
            return near < CarryPalmNear;
        }

        /// <summary>(MrWorld, when a carried thing was let go at the open other palm and the PC was told to store it.)</summary>
        void CarryStored(ItemDef gd)
        {
            Sfx("coin", _palmGlowAt, 0.25f, 1.3f);
            MenuTookIn(gd.name);      // (the fan, when it is open: its sticker pops, and the line says so)
            Debug.Log($"[MR] put away: '{gd.name}' was carried to the open {(_grabHand == 0 ? "left" : "right")} palm and let go there - it goes back into the bag");
        }

        // ------------------------------------------------------------------ the hand
        static Quaternion HandRot(in HandState hs)
        {
            Vector3 f = hs.palmForward, up = -hs.palmNormal;
            if (f.sqrMagnitude < 1e-6f) return Quaternion.identity;
            if (Vector3.Cross(f, up).sqrMagnitude < 1e-6f) up = Mathf.Abs(f.y) < 0.9f ? Vector3.up : Vector3.forward;
            return Quaternion.LookRotation(f, up);
        }

        /// <summary>How far a hand has to turn for a carried thing to turn by this much (the simulator's scripted hand).</summary>
        public static float CarryHandTurnFor(float thingTurn) => Mathf.Abs(thingTurn) < 0.5f ? 0f : thingTurn / CarryYawGain + Mathf.Sign(thingTurn) * CarryDeadTurn;

        void CarryBegin(int i, int hand, Vector3 pinch)
        {
            var p = _props[i]; var d = Item(p.itemId);
            _grab = i; _grabHand = hand; _grabOffset = p.shown - pinch; _uiConsumedPinch = true;
            _grabLostT = 0f; _dropItem = "";
            _grabYaw = _grabYawWant = _grabYaw0 = d.yaw; _grabHandTurn = 0f; _grabTwoTurn = 0f; _grabHandRotSet = false; _grabTwo = false;
            _grabOn = ""; _grabOnY = 0f; _grabTurnedAt = -10f;
            Sfx("tap", pinch, 0.6f);
            Debug.Log($"[MR] carry: '{d.name}' picked up by the {(hand == 0 ? "right" : "left")} hand at ({d.x:0.000}, {d.z:0.000}), yaw {d.yaw:0}{(d.y > 0.001f ? $", from {d.y * 100f:0.0} cm up on '{Item(d.onId)?.name}'" : "")}");
        }

        /// <summary>The carried thing follows the pinch: where it is, how it is turned, what it is over.</summary>
        void CarryStep(float dt, PropView gp, ItemDef gd, in HandState hs)
        {
            Vector3 pinch = (hs.indexTip + hs.thumbTip) * 0.5f, tip = pinch + _grabOffset;
            var other = _grabHand == 0 ? Input.Left : Input.Right;
            Vector3 otherPinch = (other.indexTip + other.thumbTip) * 0.5f;
            float before = _grabYawWant;
            if (_grabTwo)
            {
                if (!other.tracked || !other.pinch) { _grabTwo = false; _grabHandRotSet = false; Debug.Log("[MR] carry: the second hand let go"); }
                else
                {
                    Vector3 line = otherPinch - pinch; line.y = 0f;
                    if (line.sqrMagnitude > 0.02f * 0.02f) { float ly = YawTo(line); _grabTwoTurn += Mathf.DeltaAngle(_grabLineYaw, ly); _grabLineYaw = ly; }
                }
            }
            else if (other.tracked && other.pinchDown && PokeDistance(gp, gd, otherPinch) <= CarrySecondReach)
            {
                Vector3 line = otherPinch - pinch; line.y = 0f;
                _grabTwo = true; _grabLineYaw = YawTo(line); _uiConsumedPinch = true;
                // (what one hand had turned it by stays; from here the line between the hands turns it)
                _grabTwoTurn = _grabYawWant - _grabYaw0; _grabHandTurn = 0f;
                Sfx("tap", otherPinch, 0.5f);
                Debug.Log("[MR] carry: the other hand took hold too - it turns with the line between the two hands");
            }
            if (!_grabTwo)
            {
                // what the hand has turned about the vertical since the last frame (a jump is a tracking fault, not a turn)
                Quaternion now = HandRot(hs);
                if (_grabHandRotSet)
                {
                    Quaternion dq = now * Quaternion.Inverse(_grabHandRot);
                    float step = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(dq.y, dq.w) * Mathf.Rad2Deg);
                    if (Mathf.Abs(step) < 30f) _grabHandTurn += step;
                }
                _grabHandRot = now; _grabHandRotSet = true;
            }
            float handPart = Mathf.Sign(_grabHandTurn) * Mathf.Max(0f, Mathf.Abs(_grabHandTurn) - CarryDeadTurn) * CarryYawGain;
            _grabYawWant = _grabYaw0 + _grabTwoTurn + handPart;
            if (Mathf.Abs(_grabYawWant - before) > 20f * dt) _grabTurnedAt = Time.time;      // it is being turned (more than 20 degrees a second)
            _grabYaw = Mathf.Lerp(_grabYaw, _grabYawWant, 1f - Mathf.Exp(-dt * 16f));

            // what it is over: lifted onto that top, and never lower than what is under it
            int on = StandOn(_grab, gd, DeskXZ(tip), out float topY);
            string onId = on >= 0 ? _props[on].itemId : "";
            if (onId != _grabOn) { _grabOn = onId; if (on >= 0) Sfx("tick", tip, 0.35f); }
            _grabOnY = topY;
            gp.shown = new Vector3(tip.x, Mathf.Max(_deskY + topY, tip.y - gd.sizeM * 0.3f), tip.z);
        }

        /// <summary>The pinch opened over the desk: where the thing comes to stand, and the PC is told.</summary>
        void CarryRelease(PropView gp, ItemDef gd, WorldState w)
        {
            // how it is turned: on a multiple of 15 degrees when it was let go within 5 degrees of one (and was turned at all)
            float yaw = _grabYawWant, turned = _grabYawWant - _grabYaw0;
            if (Mathf.Abs(turned) > 1f)
            {
                float snap = Mathf.Round(yaw / CarrySnapStep) * CarrySnapStep;
                if (Mathf.Abs(yaw - snap) <= CarrySnapWithin) yaw = snap;
            }
            else yaw = _grabYaw0;
            float yawOut = Mathf.DeltaAngle(0f, yaw);      // -180 .. 180 for the PC
            Vector2 at = DeskXZ(gp.shown);
            int on = StandOn(_grab, gd, at, out float y);
            string onId = on >= 0 ? _props[on].itemId : "";
            Vector2 spot = FreeSpot(_grab, gd, at, yaw, y, onId);
            if (on >= 0 && (spot - at).sqrMagnitude > 1e-8f)
            {
                // pushed along the top by something else that stands there: if that took it off the top, it stands where it was let go after all
                var o = Item(onId); Vector2 rel = spot - new Vector2(o.x, o.z), half = FootHalf(_props[on], o);
                if (Mathf.Abs(Vector2.Dot(rel, AxisX(o.yaw))) > half.x || Mathf.Abs(Vector2.Dot(rel, AxisZ(o.yaw))) > half.y) spot = at;
            }
            float slid = (spot - at).magnitude;
            // Until the PC's answer is here the thing stays where it was let go (it glides to its spot, and settles on its angle).
            _dropItem = gd.id; _dropPos = DeskPoint(spot, y); _dropAt = Time.time; _dropRev = w.revision;
            _dropYaw = yaw; _dropYawShown = _grabYaw; _dropY = y;
            SendPlace(gd.id, spot.x, spot.y, Mathf.Abs(yawOut) < 0.005f ? 0.01f : yawOut, y, onId);
            // (what stands on it goes along and round with it: the PC moves those itself - WorldRules.Carry - the same way
            // as they are drawn here until its answer arrives, see Rides)
            int riders = 0; float turn = yaw - gd.yaw, lift = y - gd.y; Vector2 was = new Vector2(gd.x, gd.z);
            foreach (var r in w.items)
            {
                if (r == gd || !r.inRoom || !StandsOn(r, gd.id)) continue;
                riders++;
                // (a PC app that does not know of things standing on things - see StackFallback - is told where each of them stands now)
                if (!_stackSent.TryGetValue(r.id, out var known) || known.given == null) continue;
                Vector2 off = new Vector2(r.x, r.z) - was, to = spot + AxisX(turn) * off.x + AxisZ(turn) * off.y;
                float ry = Mathf.DeltaAngle(0f, r.yaw + turn);
                SendPlace(r.id, to.x, to.y, Mathf.Abs(ry) < 0.005f ? 0.01f : ry, r.y + lift, r.onId);
            }
            Sfx("land", _dropPos, 0.5f);
            if (slid > 0.004f) Sfx("whoosh", _dropPos, 0.18f, 1.5f);
            Debug.Log($"[MR] carry: '{gd.name}' let go at ({at.x:0.000}, {at.y:0.000}), turned by {turned:0} to yaw {yawOut:0}"
                      + (on >= 0 ? $"; it stands on '{Item(onId).name}', {y * 100f:0.0} cm above the desk" : "; it stands on the desk")
                      + (slid > 0.004f ? $"; it was in another thing's way and slides {slid * 100f:0.0} cm to ({spot.x:0.000}, {spot.y:0.000})" : "")
                      + (riders > 0 ? $"; {riders} thing(s) on it go along" : ""));
            if (y < _H * 0.8f) PartnerMakesRoom(_grab, gd, spot, yaw);
        }

        void SendPlace(string id, float x, float z, float yaw, float y, string onId)
        {
            _link.Send(Msg.World, new WorldCommand { cmd = "place", id = id, x = x, z = z, yaw = yaw, y = y, onId = onId ?? "" });
            if (y > 0f && !string.IsNullOrEmpty(onId)) _stackSent[id] = new StackSent { x = x, z = z, y = y, at = Time.time, on = onId, rev = World.revision };
            else _stackSent.Remove(id);
        }

        // A PC app from before 2026-11-17 does not know that a thing can stand on another: it keeps where the thing was
        // put (x, z) and forgets how high (y, onId). So that a thing put on a table does not sink into it with such a
        // PC, the headset remembers what it sent and goes on drawing the thing up there for as long as the PC has it at
        // that very spot with no height at all. (A PC that knows sends the height back, and nothing is remembered.)
        sealed class StackSent { public float x, z, y, at; public string on; public int rev; public ItemDef given; }      // given: the snapshot's record that was given the height here
        readonly System.Collections.Generic.Dictionary<string, StackSent> _stackSent = new System.Collections.Generic.Dictionary<string, StackSent>();
        readonly System.Collections.Generic.List<string> _stackGone = new System.Collections.Generic.List<string>();

        void StackFallback(WorldState w)
        {
            if (_stackSent.Count == 0) return;
            _stackGone.Clear();
            foreach (var kv in _stackSent)
            {
                var d = Item(kv.Key); var s = kv.Value;
                if (w.revision == s.rev || d != null && ReferenceEquals(d, s.given)) continue;      // (the PC has not answered yet; or this record has been seen to already)
                var under = d != null ? Item(s.on) : null;
                bool same = d != null && d.inRoom && Mathf.Abs(d.x - s.x) < 0.002f && Mathf.Abs(d.z - s.z) < 0.002f && under != null && under.inRoom;
                if (!same && Time.time - s.at < 1f) continue;      // (an answer to something sent before this: the answer to this is still on its way)
                if (!same || d.y > 0f || !string.IsNullOrEmpty(d.onId)) { _stackGone.Add(kv.Key); continue; }
                d.y = s.y; d.onId = s.on; s.given = d;  // (this snapshot's own record of the thing: the next snapshot is looked at again)
            }
            foreach (string id in _stackGone) _stackSent.Remove(id);
        }

        /// <summary>A thing was put down where the partner stands (or where it lives): it hops aside.</summary>
        void PartnerMakesRoom(int self, ItemDef d, Vector2 spot, float yaw)
        {
            Vector2 half = FootHalf(_props[self], d), body = Vector2.one * (_H * 0.30f);
            // its home spot first: it would walk back into the thing
            if (_homeSet && BoxPush(DeskXZ(_home), body, 0f, spot, half, yaw, out Vector2 homePush))
            {
                Vector3 to = DeskPoint(DeskXZ(_home) + homePush + homePush.normalized * 0.02f);
                _worldShift += _home - to; _home = to;      // (the things stay where they are)
            }
            bool there = _ch.owner == Env.Mr && _rig.Visible && _mode == Mode.Free && TransitionIdle && (_doingNow.Length == 0 || _doingNow == "craft");
            if (!there || !BoxPush(DeskXZ(_pos), body, 0f, spot, half, yaw, out Vector2 push)) return;
            Vector3 aside = DeskPoint(DeskXZ(_pos) + push + push.normalized * 0.025f);
            float face = YawTo(Input.HeadPos - aside);
            _motor.Stop();
            _actPhase = 0; _actFor = "";
            _startleT = 0.5f;
            Sfx("squeak", _pos, 0.6f);
            StartHop(_pos, () => aside, 0.03f, Quaternion.Euler(0, face, 0), () => { _motor.Teleport(new Vector2(aside.x, aside.z), face); });
            Debug.Log($"[MR] carry: '{d.name}' was put down where the partner stood - it hops {(push.magnitude + 0.025f) * 100f:0} cm aside");
        }

        /// <summary>Does this thing stand on the thing that is being carried (or was just let go)? Then: where it is and how it is turned, with that thing.</summary>
        bool Rides(ItemDef d, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = default;
            string mover = _grab >= 0 ? _props[_grab].itemId : _dropItem;
            if (mover.Length == 0 || d.id == mover || !StandsOn(d, mover)) return false;
            var md = Item(mover); var mp = PropOf(mover);
            if (md == null || mp == null) return false;
            Quaternion q = Quaternion.AngleAxis((_grab >= 0 ? _grabYaw : _dropYawShown) - md.yaw, Vector3.up);
            pos = mp.shown + q * (ItemPosY(d) - ItemPosY(md));
            rot = q * ItemRot(d);
            return true;
        }

        /// <summary>Once a frame, after the things have been placed: the ring under a thing that is turned, and the ring where it would land.</summary>
        void UpdateCarryFx(float dt)
        {
            var gp = _grab >= 0 ? _props[_grab] : null; var gd = gp != null ? Item(gp.itemId) : null;
            bool carrying = gd != null;
            if (carrying)
            {
                Vector2 half = FootHalf(gp, gd);
                float under = _deskY + _grabOnY;
                _carryRingAt = new Vector3(gp.shown.x, under + 0.0022f, gp.shown.z);
                _carryRingSize = Mathf.Max(0.08f, half.magnitude * 2f * 1.5f + 0.03f);
                _landAt = new Vector3(gp.shown.x, under + 0.0018f, gp.shown.z);
                _landSize = Mathf.Max(0.045f, Mathf.Max(half.x, half.y) * 2f * 1.25f);
            }
            float turned = _grabYaw - _grabYaw0;
            bool turning = carrying && Time.time - _grabTurnedAt < 0.9f && Mathf.Abs(turned) > 1.5f;
            _carryRingShow = Mathf.MoveTowards(_carryRingShow, turning ? 1f : 0f, dt / (turning ? 0.12f : 0.3f));
            bool ring = _carryRingShow > 0.01f;
            if (_carryTicks.enabled != ring) _carryTicks.enabled = ring;
            if (_carryArc.enabled != ring) _carryArc.enabled = ring;
            if (_carryDot.enabled != ring) _carryDot.enabled = ring;
            if (ring)
            {
                float a = _carryRingShow, size = _carryRingSize * (0.92f + 0.08f * a);
                float deskYaw = YawTo(_worldFwd), from = deskYaw + _grabYaw0, to = deskYaw + _grabYaw;
                // the ticks stay where they are (one every 15 degrees, one of them where the thing pointed when it was picked up)
                _carryTicks.transform.SetPositionAndRotation(_carryRingAt, Quaternion.Euler(0f, from, 0f) * Quaternion.Euler(90f, 0f, 0f));
                _carryTicks.transform.localScale = Vector3.one * size;
                _carryTicks.SetColors(new Color(0.29f, 0.18f, 0.15f, 0.80f * a), new Color(0.29f, 0.18f, 0.15f, 0.80f * a));
                // the arc: from where it pointed to where it points now (the picture fills clockwise from its top)
                float sweep = Mathf.Min(360f, Mathf.Abs(turned));
                _carryArc.transform.SetPositionAndRotation(_carryRingAt + Vector3.up * 0.0004f, Quaternion.Euler(0f, turned >= 0f ? from : to, 0f) * Quaternion.Euler(90f, 0f, 0f));
                _carryArc.transform.localScale = Vector3.one * size;
                _carryArc.SetParam(new Vector4(0.66f, 0.09f, sweep / 360f, 0f));
                _carryArc.SetColors(new Color(1f, 0.72f, 0.30f, 0.95f * a), new Color(1f, 0.84f, 0.42f, 0.95f * a));
                Vector3 heading = Quaternion.Euler(0f, to, 0f) * Vector3.forward;
                _carryDot.transform.SetPositionAndRotation(_carryRingAt + heading * (size * 0.5f * 0.66f) + Vector3.up * 0.0008f, Quaternion.Euler(90f, 0f, 0f));
                _carryDot.transform.localScale = Vector3.one * (size * 0.15f);
                _carryDot.SetColors(new Color(1f, 0.97f, 0.90f, a), new Color(1f, 0.97f, 0.90f, a));
            }
            // the open other palm glows while the thing is near enough to be let go into the bag there
            bool over = false;
            if (carrying)
            {
                var other = _grabHand == 0 ? Input.Left : Input.Right;
                over = CarryOverPalm(gp, gd, other, _grabHand == 0 ? Input.Right : Input.Left);
                if (over) { _palmGlowAt = other.palmPos + other.palmNormal * 0.006f; _palmGlowN = other.palmNormal; }
            }
            if (over && !_palmOver) { Sfx("tick", _palmGlowAt, 0.4f, 1.2f); Debug.Log($"[MR] carry: '{gd.name}' is over the open palm - let go there and it goes into the bag"); }
            _palmOver = over;
            _palmGlowShow = Mathf.MoveTowards(_palmGlowShow, over ? 1f : 0f, dt / 0.15f);
            bool glow = _palmGlowShow > 0.01f;
            if (_palmGlow.enabled != glow) _palmGlow.enabled = glow;
            if (glow)
            {
                float beat = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
                _palmGlow.transform.SetPositionAndRotation(_palmGlowAt, Quaternion.LookRotation(-_palmGlowN, Mathf.Abs(_palmGlowN.y) < 0.9f ? Vector3.up : Vector3.forward));
                _palmGlow.transform.localScale = Vector3.one * (0.13f * (0.92f + 0.08f * beat) * (0.7f + 0.3f * _palmGlowShow));
                _palmGlow.SetColors(new Color(1f, 0.82f, 0.40f, (0.55f + 0.25f * beat) * _palmGlowShow), new Color(1f, 0.96f, 0.82f, (0.55f + 0.25f * beat) * _palmGlowShow));
            }
            bool land = carrying && _grabOn.Length > 0 && !over;
            _landShow = Mathf.MoveTowards(_landShow, land ? 1f : 0f, dt / 0.15f);
            bool landOn = _landShow > 0.01f;
            if (_landRing.enabled != landOn) _landRing.enabled = landOn;
            if (landOn)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
                _landRing.transform.SetPositionAndRotation(_landAt, Quaternion.Euler(90f, 0f, 0f));
                _landRing.transform.localScale = Vector3.one * _landSize * (0.96f + 0.06f * pulse) * (0.8f + 0.2f * _landShow);
                _landRing.SetColors(new Color(1f, 0.84f, 0.42f, (0.70f + 0.25f * pulse) * _landShow), new Color(1f, 0.96f, 0.86f, (0.70f + 0.25f * pulse) * _landShow));
                _landRing.SetPhase(Time.time * 0.12f);
            }
        }

        /// <summary>
        /// For the simulator's scripted hand (lpctl debug carry &lt;turn&gt; &lt;x&gt; &lt;z&gt; "thing"): where to take hold of a thing,
        /// where the pinch has to be for the thing to hover 3 cm over the spot (x, z) of the desk - or over the top of
        /// what stands there -, and how far apart finger and thumb have to be to straddle it. False: no such thing.
        /// </summary>
        public bool SimCarryPlan(string what, float x, float z, out Vector3 grip, out Vector3 end, out float straddle)
        {
            grip = end = default; straddle = 0.05f;
            int i = _snap != null && _worldFrame ? FindProp(what) : -1;
            if (i < 0) return false;
            var p = _props[i]; var d = Item(p.itemId);
            Vector2 half = FootHalf(p, d);
            grip = p.shown + Vector3.up * (d.sizeM * 0.35f);
            StandOn(i, d, new Vector2(x, z), out float topY);
            end = DeskPoint(new Vector2(x, z), topY + 0.03f + d.sizeM * 0.65f);
            straddle = Mathf.Max(half.x, half.y) + 0.03f;
            return true;
        }
    }
}
