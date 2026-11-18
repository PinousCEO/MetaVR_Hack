using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// A thing arrives where it is SEEN. "I asked for a hammer: the icon was made but the hammer was not" (the
    /// user, 2026-11-17) - it had been made, three centimetres small, somewhere on the desk, and nobody saw it. Now a
    /// thing that comes onto the desk (taken out of the bag: it was known as being in the bag and now stands in the
    /// room; or one whose id the headset has never seen and that stands on the desk) makes an entrance at its place:
    ///  - for 1.2 s it grows from nothing with a bounce, in a ring of sparkles, to "ta-da";
    ///  - for 4 s a ring on the ground round it pulses (6 s for a thing smaller than 5 cm) and its name stands over
    ///    it on the paper strip (the same strip as when a thing is pressed);
    ///  - if it is outside what the user is looking at (more than 35 degrees off the gaze), a small arrow sticker at
    ///    the edge of the view points to it for as long as the name is shown;
    ///  - the partner, when it is out on the desk with nothing to do, turns to it and points.
    /// Nothing arrives like this when the app starts: what is on the desk then is simply there.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("A new thing arrives")]
        [Tooltip("The ring on the ground round a thing that has just arrived.")]
        [SerializeField] FxQuad _arriveRing;
        [SerializeField] FxQuad _arriveGlow;
        [Tooltip("The arrow at the edge of the view that points to it while it is out of sight.")]
        [SerializeField] UiSprite _arriveArrow;

        const float ArriveGrow = 1.2f, ArriveShow = 4f, ArriveShowSmall = 6f, ArriveSmall = 0.05f, ArriveOffGaze = 35f;

        readonly Dictionary<string, string> _knownThings = new Dictionary<string, string>();      // id -> how it was last seen: "making", in the "bag", in the "room"
        bool _knownInit;
        string _arrId = ""; float _arrT = -1f, _arrFor; bool _arrSecond; Vector3 _arrowAt; float _arrowShow;

        /// <summary>Once a frame, when the props have been given their things: is one of them new?</summary>
        void Arrivals(WorldState w)
        {
            // (What the partner has just MADE does not arrive on the desk any more: it goes into the bag - MrCraft - and
            // makes this entrance when the user takes it out. "Objects should only come out when I press them.")
            ItemDef fresh = null;
            foreach (var d in w.items)
            {
                bool known = _knownThings.TryGetValue(d.id, out string was);
                bool ready = d.status != "making" && d.status != "failed";
                if (_knownInit && d.inRoom && ready && (!known || was == "bag")) fresh = d;
                _knownThings[d.id] = !ready ? "making" : d.inRoom ? "room" : "bag";
            }
            _knownInit = true;
            if (fresh != null) Arrive(fresh, false);
        }

        void Arrive(ItemDef d, bool again)
        {
            int i = -1;
            for (int k = 0; k < _props.Length; k++) if (_props[k].itemId == d.id) i = k;
            if (i < 0) return;
            var p = _props[i];
            _arrId = d.id; _arrT = 0f; _arrSecond = false; _arrowShow = 0f;
            _arrFor = d.sizeM < ArriveSmall ? ArriveShowSmall : ArriveShow;
            Vector3 at = ItemPosY(d), mid = at + Vector3.up * (TallOf(p, d) * 0.5f);
            Vector3 right = Input.HeadRot * Vector3.right; right.y = 0f; right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            _sparks.Burst(mid, right, Vector3.up, Mathf.Max(0.07f, d.sizeM * 0.9f), 18);
            Sfx("ta-da", at, 0.8f); Sfx("sparkle", at, 0.6f);
            // its name on the paper strip, for as long as the ring is there
            _pokeLabelProp = i; _pokeLabelSince = Time.time; _pokeLabelUntil = Time.time + ArriveShow;
            // the partner turns to it and points
            if (_ch.owner == Env.Mr && _rig.Visible && _mode == Mode.Free && TransitionIdle && _doingNow.Length == 0 && _game.Length == 0 && !_tn.active && !_motor.HasTarget)
            {
                _motor.Face(YawTo(at - _pos));
                PlayEmote(Emote.Point, 2.2f);
            }
            Vector3 to = mid - Input.HeadPos;
            float off = Vector3.Angle(Input.HeadRot * Vector3.forward, to);
            Debug.Log($"[MR] arrive: '{d.name}' ({d.kind}, {d.sizeM * 100f:0.0} cm) {(again ? "arrives once more (asked for by the PC)" : "comes onto the desk")} at ({d.x:0.000}, {d.z:0.000}){(d.y > 0.001f ? $", {d.y * 100f:0.0} cm up" : "")}: it is {off:0} degrees off the gaze{(off > ArriveOffGaze ? " - an arrow points to it" : "")}; ring for {_arrFor:0} s");
        }

        void UpdateArrive(float dt, bool hostOk)
        {
            ItemDef d = _arrT >= 0f && hostOk && _worldFrame ? Item(_arrId) : null;
            PropView p = d != null ? PropOf(d.id) : null;
            if (_arrT >= 0f) { _arrT += dt; if (p == null || !d.inRoom || _arrT > _arrFor + 0.4f) { _arrT = -1f; d = null; p = null; } }
            bool on = p != null;
            if (_arriveRing.enabled != on) _arriveRing.enabled = on;
            if (_arriveGlow.enabled != on) _arriveGlow.enabled = on;
            float arrowWant = 0f;
            if (on)
            {
                float t = _arrT;
                // it grows from nothing with a bounce (it overshoots by a fifth and settles)
                if (t < ArriveGrow)
                {
                    float u = t / ArriveGrow;
                    float grow = 1f - Mathf.Exp(-6.5f * u) * Mathf.Cos(u * 15f);
                    p.transform.localScale *= Mathf.Max(0.001f, grow);
                    if (!_arrSecond && t > 0.42f)
                    {
                        _arrSecond = true;
                        Vector3 r = Input.HeadRot * Vector3.right; r.y = 0f;
                        _sparks.Burst(p.shown + Vector3.up * (TallOf(p, d) * 0.9f), r.sqrMagnitude > 1e-4f ? r.normalized : Vector3.right, Vector3.up, Mathf.Max(0.05f, d.sizeM * 0.6f), 12);
                    }
                }
                // the ring on the ground: it pulses, and fades at the end
                float a = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((_arrFor - t) / 0.5f);
                float foot = Mathf.Max(0.03f, FootHalf(p, d).magnitude * 2f);
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5.2f);
                Vector3 ground = new Vector3(p.shown.x, _deskY + Mathf.Max(0f, d.y) + 0.002f, p.shown.z);
                _arriveRing.transform.SetPositionAndRotation(ground, Quaternion.Euler(90f, 0f, 0f));
                _arriveRing.transform.localScale = Vector3.one * (Mathf.Max(0.085f, foot * 1.5f + 0.03f) * (0.94f + 0.12f * pulse));
                _arriveRing.SetColors(new Color(1f, 0.80f, 0.36f, (0.65f + 0.35f * pulse) * a), new Color(1f, 0.96f, 0.86f, (0.65f + 0.35f * pulse) * a));
                _arriveRing.SetPhase(t * 0.2f);
                _arriveGlow.transform.SetPositionAndRotation(ground - Vector3.up * 0.0006f, Quaternion.Euler(90f, 0f, 0f));
                _arriveGlow.transform.localScale = Vector3.one * Mathf.Max(0.11f, foot * 1.9f + 0.04f);
                _arriveGlow.SetColors(new Color(1f, 0.90f, 0.58f, 0.42f * a * (0.7f + 0.3f * pulse)), new Color(1f, 0.97f, 0.86f, 0.42f * a));
                // out of sight: the arrow at the edge of the view
                Vector3 mid = p.shown + Vector3.up * (TallOf(p, d) * 0.5f);
                Vector3 local = Quaternion.Inverse(Input.HeadRot) * (mid - Input.HeadPos);
                float off = Vector3.Angle(Vector3.forward, local);
                if (off > ArriveOffGaze && t < ArriveShow)
                {
                    arrowWant = 1f;
                    Vector2 dir = new Vector2(local.x, local.y);
                    dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
                    // (an ellipse inside the view: 27 degrees to the sides, 19 up and down, half a metre away)
                    Vector3 want = new Vector3(dir.x * 0.26f, dir.y * 0.17f, 0.5f) + (Vector3)(dir * (0.008f * Mathf.Sin(t * 7f)));
                    _arrowAt = _arrowShow <= 0.01f ? want : Vector3.Lerp(_arrowAt, want, 1f - Mathf.Exp(-dt * 10f));
                    _arriveArrow.transform.SetPositionAndRotation(Input.HeadPos + Input.HeadRot * _arrowAt, Input.HeadRot * Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg));
                }
            }
            _arrowShow = Mathf.MoveTowards(_arrowShow, arrowWant, dt / 0.2f);
            bool arrow = _arrowShow > 0.01f && on;
            if (_arriveArrow.enabled != arrow) _arriveArrow.enabled = arrow;
            if (arrow)
            {
                _arriveArrow.transform.localScale = Vector3.one * (0.05f * Mathf.Max(0.001f, Ui.Pop(_arrowShow)));
                _arriveArrow.SetColor(new Color(1f, 1f, 1f, _arrowShow));
            }
        }

        /// <summary>lpctl debug arrive 0 0 0 "id or name": that thing arrives once more, as a new thing does (no text: the first thing on the desk).</summary>
        void DebugArrive(string what)
        {
            int i = _snap != null ? FindProp(what) : -1;
            if (i < 0) { Debug.Log($"[MR] arrive: nothing on the desk is called '{what}'"); return; }
            Arrive(Item(_props[i].itemId), true);
        }
    }
}
