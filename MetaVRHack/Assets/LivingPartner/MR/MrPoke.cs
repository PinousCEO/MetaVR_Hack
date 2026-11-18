using System;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Pressing a thing on the desk. "Pressing the furniture changes nothing" (the user, 2026-11-17): the things could
    /// be picked up by a pinch and nothing else. Now an index fingertip that goes INTO a thing presses it:
    ///  - at once, here: the thing squashes and springs back, a tap and a pop are heard at it, a few sparks, and its
    ///    name comes up on a small strip of paper over it for a second and a half;
    ///  - then: a thing for music opens the record picker over it (MrRecords); any other thing is "used" - the PC is
    ///    told (world command "use"), and the partner goes and sits on it / studies at it / naps / reads / plays, and
    ///    stops when the thing is pressed again.
    /// What counts as a press: the tip of either index finger entering the thing's box (its footprint and height,
    /// 8 mm larger all round, never smaller than 3 cm) while that hand is not pinching (thumb and finger more than
    /// 3 cm apart), is not offered palm-up, is not on a board, and nothing else is going on (no game, the partner not
    /// carried, no calibration). One press per entry - the finger has to leave the box by 2 cm before the same thing
    /// can be pressed again - and at most one press in 0.6 s. What the press DOES waits 0.45 s: if the same hand
    /// closes thumb and finger in that time it was reaching for the thing (or for the partner) to pick it up, and
    /// nothing is asked of the partner.
    /// A fingertip within 5 cm of a thing lights a ring on the desk under it and makes it 4 % larger: "this can be pressed".
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Pressing a thing on the desk")]
        [Tooltip("The ring on the desk under the thing a fingertip is near.")]
        [SerializeField] FxQuad _pokeRing;
        [Tooltip("The thing's name on a strip of paper, for a moment after it was pressed.")]
        [SerializeField] FxQuad _pokeLabelBg;
        [SerializeField] StoryLabel _pokeLabel;
        [Tooltip("Every panel under MrCore (collected when the prefab is authored): a fingertip on one of them is pressing a button, not a thing behind it.")]
        [SerializeField] UiPanel[] _allPanels;
        [Tooltip("\"Put away\": the small button that comes up over a thing that was pressed - it sends the thing back into the bag.")]
        [SerializeField] UiPanel _putStrip;
        [SerializeField] UiButton _putBtn;

        const float PokeGrow = 0.008f, PokeMin = 0.03f, PokeLeave = 0.02f, PokeNear = 0.05f;      // metres
        const float PokeEvery = 0.6f, PokeSquashTime = 0.25f, PokeLabelTime = 1.5f, PokeActDelay = 0.45f;      // seconds

        int[] _pokeIn;                 // per thing: the hands whose fingertip is inside its box (bit 0 right, bit 1 left)
        float[] _pokeT, _pokeHot;      // per thing: seconds since it was pressed; how near a fingertip is (0..1, smoothed)
        float _pokeAt = -10f, _pokeLabelUntil, _pokeLabelSince, _pokeRingShow;
        int _pokeLabelProp = -1, _pokeRingProp = -1;
        string _pokePendingId = ""; float _pokePendingAt; int _pokePendingHand = -1;
        float _pokeSlow = 1f;          // (captures from the PC: the squash drawn out, so that a picture can catch it)
        float _putSince; bool _putArmed;      // when the thing was pressed; the button is up and may be pressed
        const float PutAwayTime = 7f;  // seconds the "Put away" button stays up after the press (longer while a finger is at it)
        string _putId = ""; float _putUntil;

        /// <summary>Is a glass game being played out on the desk (the hands are its input then)?</summary>
        bool GlassGameInRoom => _snap != null && _snap.glass != null && !string.IsNullOrEmpty(_snap.glass.game) && _snap.glass.side == "mr";

        /// <summary>How tall a thing stands on the desk, in metres (never less than the smallest box).</summary>
        static float PokeTall(PropView p, ItemDef d) => Mathf.Max(PokeMin, p.height * d.sizeM);

        /// <summary>How far a point is from a thing's box (its footprint and height as it stands just now). Negative inside.</summary>
        static float PokeDistance(PropView p, ItemDef d, Vector3 q)
        {
            Vector3 l = Quaternion.Inverse(p.transform.rotation) * (q - p.transform.position);
            float half = Mathf.Max(PokeMin, d.sizeM) * 0.5f, tall = PokeTall(p, d) * 0.5f;
            Vector3 dd = new Vector3(Mathf.Abs(l.x) - half, Mathf.Abs(l.y - tall) - tall, Mathf.Abs(l.z) - half);
            float outside = new Vector3(Mathf.Max(dd.x, 0f), Mathf.Max(dd.y, 0f), Mathf.Max(dd.z, 0f)).magnitude;
            return outside > 0f ? outside : Mathf.Max(dd.x, Mathf.Max(dd.y, dd.z));
        }

        /// <summary>Is this fingertip at a board (within 2 cm of its sheet or of one of its buttons, from 6 cm in front of it to 3 cm behind)?</summary>
        bool FingerOnBoard(Vector3 tip)
        {
            foreach (var p in _allPanels)
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                Transform t = p.transform;
                Vector3 l = Quaternion.Inverse(t.rotation) * (tip - t.position);
                if (l.z > -0.06f && l.z < 0.03f && Mathf.Abs(l.x) < p.width * 0.5f + 0.02f && Mathf.Abs(l.y) < p.height * 0.5f + 0.02f) return true;
                foreach (var b in p.buttons)
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    float reach = (b.half.x > 0f ? Mathf.Max(b.half.x, b.half.y) : b.radius) + 0.03f;
                    if ((tip - b.transform.position).sqrMagnitude < reach * reach) return true;
                }
            }
            return false;
        }

        void UpdatePoke(float dt, bool hostOk)
        {
            int n = _props.Length;
            if (_pokeIn == null) { _pokeIn = new int[n]; _pokeT = new float[n]; _pokeHot = new float[n]; for (int i = 0; i < n; i++) _pokeT[i] = 10f; }
            bool world = hostOk && _worldFrame;
            // nothing is pressed while something else is going on with the hands
            bool may = world && !_calibrating && _mode != Mode.Held && _mode != Mode.Hop && _game.Length == 0 && !_tn.active && _tnPendingAt < 0f && !GlassGameInRoom && _grab < 0;
            int nearest = -1; float nearestD = PokeNear;
            for (int i = 0; i < n; i++)
            {
                var p = _props[i]; var d = world && p.itemId.Length > 0 ? Item(p.itemId) : null;
                _pokeT[i] += dt;
                if (d == null || !p.Visible) { _pokeIn[i] = 0; _pokeHot[i] = 0f; continue; }
                bool pressable = may && d.status != "making" && p.appear >= 1f;
                float near = PokeNear;
                for (int h = 0; h < 2; h++)
                {
                    var hs = h == 0 ? Input.Right : Input.Left; int bit = 1 << h;
                    if (!hs.tracked) continue;      // (not seen for a moment: it is where it was - coming back inside the box is not a new press)
                    float dist = PokeDistance(p, d, hs.indexTip);
                    if (dist > PokeGrow + PokeLeave) _pokeIn[i] &= ~bit;
                    else if (dist <= PokeGrow && (_pokeIn[i] & bit) == 0)
                    {
                        _pokeIn[i] |= bit;
                        bool handFree = !hs.pinch && hs.pinchStrength < 0.75f && !hs.palmUp && !(_mode == Mode.OnHand && h == _rideHand)
                                        && _pokeLock[h] == null;      // (that finger is still in a button of a board: MrUiRay)
                        if (pressable && handFree && Time.time - _pokeAt >= PokeEvery && !FingerOnBoard(hs.indexTip)) PokeThing(i, d, hs.indexTip, h);
                    }
                    if (!hs.pinch && !hs.palmUp && dist < near) near = dist;
                }
                bool hot = pressable && near < PokeNear;
                _pokeHot[i] = Mathf.MoveTowards(_pokeHot[i], hot ? 1f : 0f, dt * 8f);
                if (hot && near < nearestD) { nearestD = near; nearest = i; }

                // the squash and the spring back (a quarter of a second), and a touch larger while a fingertip is near;
                // only its size changes - it stays where it stands
                float u = _pokeT[i] / (PokeSquashTime * _pokeSlow);
                if (u < 1f || _pokeHot[i] > 0.001f)
                {
                    float k = u < 1f ? Mathf.Sin(u * 1.5f * Mathf.PI) * (1f - u) * 1.35f : 0f;      // down, back up a little too far, and still
                    float grow = 1f + 0.04f * M.Smooth(_pokeHot[i]);
                    p.transform.localScale = Vector3.Scale(p.transform.localScale, new Vector3((1f + 0.13f * k) * grow, (1f - 0.24f * k) * grow, (1f + 0.13f * k) * grow));
                }
            }

            // the ring on the desk under the thing a fingertip is near
            if (nearest >= 0) _pokeRingProp = nearest;
            _pokeRingShow = Mathf.MoveTowards(_pokeRingShow, nearest >= 0 ? 1f : 0f, dt * 7f);
            var rp = _pokeRingProp >= 0 ? _props[_pokeRingProp] : null; var rd = rp != null && rp.itemId.Length > 0 ? Item(rp.itemId) : null;
            bool ring = world && rd != null && _pokeRingShow > 0.01f;
            if (_pokeRing.enabled != ring) _pokeRing.enabled = ring;
            if (ring)
            {
                _pokeRing.transform.SetPositionAndRotation(new Vector3(rp.shown.x, _deskY + Mathf.Max(0f, rd.y) + 0.0016f, rp.shown.z), Quaternion.Euler(90f, 0f, 0f));      // (on the desk, or on the top the thing stands on)
                _pokeRing.transform.localScale = Vector3.one * (Mathf.Max(PokeMin, rd.sizeM) * 1.55f + 0.02f) * (0.9f + 0.1f * _pokeRingShow);
                _pokeRing.SetColors(new Color(1f, 0.84f, 0.42f, 0.85f * _pokeRingShow), new Color(1f, 0.96f, 0.86f, 0.85f * _pokeRingShow));
                _pokeRing.SetPhase(Time.time * 0.15f);
            }

            // its name, for a second and a half
            var lp = _pokeLabelProp >= 0 ? _props[_pokeLabelProp] : null; var ld = lp != null && lp.itemId.Length > 0 ? Item(lp.itemId) : null;
            bool label = world && ld != null && Time.time < _pokeLabelUntil;
            if (label)
            {
                float pop = Mathf.Max(0.001f, Ui.Pop(Mathf.Clamp01((Time.time - _pokeLabelSince) / 0.18f)) * Mathf.Clamp01((_pokeLabelUntil - Time.time) / 0.15f));
                Vector3 at = lp.shown + Vector3.up * (PokeTall(lp, ld) + 0.020f + _pokeLabel.Height * 0.5f);
                _pokeLabel.transform.localScale = Vector3.one * pop;
                Place(_pokeLabel, at, Shorten(string.IsNullOrEmpty(ld.name) ? ld.kind : ld.name, 22));
                _pokeLabelBg.transform.SetPositionAndRotation(_pokeLabel.transform.position + _pokeLabel.transform.forward * 0.002f, _pokeLabel.transform.rotation);
                Ui.SizePanel(_pokeLabelBg, (_pokeLabel.Width + 0.018f) * pop, (_pokeLabel.Height + 0.009f) * pop, 0.007f * pop, 0.0010f);
            }
            else if (_pokeLabel.text.Length > 0) _pokeLabel.text = "";
            if (_pokeLabelBg.enabled != label) _pokeLabelBg.enabled = label;

            // what the press does, a moment later - unless the hand turned out to be picking something up
            if (_pokePendingId.Length > 0)
            {
                bool grabbing = _grab >= 0 || _mode == Mode.Held;
                if (_pokePendingHand >= 0)
                {
                    var hs = _pokePendingHand == 0 ? Input.Right : Input.Left;
                    grabbing |= hs.tracked && hs.pinch;
                    grabbing |= _holdWait[_pokePendingHand] >= 0f;
                }
                if (grabbing) { Debug.Log("[MR] poke: the hand closed to pick something up - nothing is asked of the partner"); _pokePendingId = ""; }
                else if (Time.time >= _pokePendingAt)
                {
                    var d = world ? Item(_pokePendingId) : null;
                    _pokePendingId = "";
                    if (d != null && d.inRoom) PokeAct(d);
                }
            }
        }

        /// <summary>A thing was pressed: what is seen and heard at once, and what it leads to a moment later.</summary>
        void PokeThing(int i, ItemDef d, Vector3 at, int hand)
        {
            var p = _props[i];
            _pokeAt = Time.time; _pokeT[i] = 0f; if (hand >= 0) _pokeSlow = 1f;
            float tall = PokeTall(p, d);
            Sfx("tap", at, 0.6f);
            Sfx("pop", p.shown + Vector3.up * (tall * 0.5f), 0.5f);
            Vector3 right = Input.HeadRot * Vector3.right; right.y = 0f;
            _sparks.Burst(p.shown + Vector3.up * (tall * 0.75f), right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right, Vector3.up, 0.035f, 6);
            _pokeLabelProp = i; _pokeLabelSince = Time.time; _pokeLabelUntil = Time.time + PokeLabelTime;
            _pokePendingId = d.id; _pokePendingAt = Time.time + PokeActDelay; _pokePendingHand = hand;
            _putId = d.id; _putUntil = Time.time + PutAwayTime; _putSince = Time.time; _putArmed = false;      // "Put away" comes up beside it - once the finger has gone (UpdatePutAway)
            Debug.Log($"[MR] poke: '{d.name}' ({d.kind}, uses {string.Join("+", d.uses ?? new string[0])}) pressed by {(hand == 0 ? "the right index finger" : hand == 1 ? "the left index finger" : "the PC (debug)")}");
        }

        /// <summary>What a press leads to: the records for a thing that plays music, otherwise the partner uses it.</summary>
        void PokeAct(ItemDef d)
        {
            if (RoomScriptPressed(d)) return;      // a thing with a script: its on_use, its buttons (MrRoomScript)
            bool music = d.uses != null && Array.IndexOf(d.uses, ItemUses.Music) >= 0;
            if (music)
            {
                if (_recOpen && _recItem == d.id) CloseRecords("the thing was pressed again");
                else OpenRecords(d);
                return;
            }
            SendWorld("use", d.id);
            Debug.Log($"[MR] poke: asked the PC to use '{d.name}' (world command use, id {d.id})");
        }

        // ------------------------------------------------------------------ "Put away"
        // "Can a placed object not be put back?" (the user, on the headset, 2026-11-17). It could - by pressing its
        // sticker in the bag again, or by carrying it onto the open other palm - but nothing AT the thing said so.
        // Now a thing that is pressed gets a small "Put away" button over it for seven seconds (beside the strip of
        // "Wear" and its acts, when it has one - MrRoomScript); pressing it sends the thing back into the bag.
        void InitPutAway()
        {
            _putBtn.press = () => PutAway(_putId, "its Put away button");
            _putStrip.gameObject.SetActive(false);
        }

        void PutAwayHide() { _putId = ""; _putStrip.Hide(); }

        /// <summary>A thing on the desk goes back into the bag: the PC is told, a small star carries it to the left palm, the fan (when open) says so.</summary>
        void PutAway(string id, string how)
        {
            var d = Item(id); var p = d != null ? PropOf(id) : null;
            if (d == null || !d.inRoom) return;
            Vector3 from = p != null ? p.shown + Vector3.up * (PokeTall(p, d) * 0.5f) : ItemPosY(d);
            SendWorld("store", d.id);
            Sfx("pop", from, 0.6f); Sfx("whoosh", from, 0.2f, 1.3f);
            Vector3 right = Input.HeadRot * Vector3.right; right.y = 0f;
            _sparks.Burst(from, right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right, Vector3.up, 0.05f, 10);
            BagComet(from);
            MenuTookIn(d.name);
            if (_pokePendingId == id) _pokePendingId = "";      // (it is not also used)
            if (_rsStripKey.Length > 0 && RoomEntry(_rsStripKey)?.itemId == id) _rsStripKey = "";
            Debug.Log($"[MR] put away: '{d.name}' goes back into the bag ({how})");
            PutAwayHide();
        }

        /// <summary>Once a frame, with the boards (MrMenuBoard.UpdateMenu): the "Put away" button over the thing that was pressed.</summary>
        void UpdatePutAway(float dt)
        {
            var sp = _putStrip;
            var d = _putId.Length > 0 ? Item(_putId) : null; var prop = d != null ? PropOf(d.id) : null;
            bool busy = _calibrating || _grab >= 0 || _mode == Mode.Held || _game.Length > 0 || _tn.active || GlassGameInRoom;
            if (d == null || prop == null || !d.inRoom || d.status == "making" || busy || Time.time > _putUntil) { if (_putId.Length > 0 || sp.Shown) PutAwayHide(); return; }
            if (_putBtn.hover > 0.5f) _putUntil = Mathf.Max(_putUntil, Time.time + 2f);      // (a finger is on its way to it)
            // BESIDE the thing, on the user's right (not over it: the finger that pressed the thing goes back UP, and in the
            // simulator it pressed a button that had just come up over the thing - twice - so the thing vanished at once);
            // when the thing's own buttons are up (Wear, its acts), at the right end of that strip
            Vector3 toRight = Input.HeadRot * Vector3.right; toRight.y = 0f; toRight = toRight.sqrMagnitude > 1e-4f ? toRight.normalized : Vector3.right;
            Vector3 anchor = prop.shown + toRight * (Mathf.Max(0.03f, d.sizeM * 0.5f) + 0.035f + sp.width * 0.5f) + Vector3.up * (PokeTall(prop, d) * 0.5f + 0.02f);
            var e = _rsStripKey.Length > 0 ? RoomEntry(_rsStripKey) : null;
            if (e != null && e.itemId == d.id && _rsStrip.Shown)
            {
                _putUntil = Mathf.Max(_putUntil, _rsStripUntil);
                Transform st = _rsStrip.transform;
                anchor = st.position + st.right * (_rsStrip.width * 0.5f + sp.width * 0.5f + 0.008f);
            }
            // It only comes up - and can only be pressed - once the press is half a second old AND both index fingertips are
            // well away from where it will stand: a button must never appear under a finger.
            if (!_putArmed)
            {
                bool clear = Time.time - _putSince > 0.5f;
                for (int h = 0; h < 2 && clear; h++) { var hs = h == 0 ? Input.Right : Input.Left; if (hs.tracked && (hs.indexTip - anchor).magnitude < 0.09f) clear = false; }
                if (!clear) { if (sp.Shown) sp.Hide(); _putUntil = Mathf.Max(_putUntil, Time.time + PutAwayTime - 1f); return; }
                _putArmed = true;
            }
            sp.Show();
            FaceUser(sp.transform, anchor);
            UpdateButtons(sp, dt);
        }

        /// <summary>lpctl debug poke [slow] 0 0 "id or name": the same press, without a finger (no text: the first thing on the desk).</summary>
        void DebugPoke(string what, float slow = 1f)
        {
            int i = FindProp(what);
            if (i < 0) { Debug.Log($"[MR] poke: nothing on the desk is called '{what}'"); return; }
            if (_pokeIn == null) return;
            var p = _props[i]; var d = Item(p.itemId);
            _pokeSlow = Mathf.Clamp(slow, 1f, 20f);
            PokeThing(i, d, p.shown + Vector3.up * PokeTall(p, d), -1);
        }

        /// <summary>The thing on the desk with this id, this name or kind, or a name that contains this (no text: the first one). -1: none.</summary>
        int FindProp(string what)
        {
            what = (what ?? "").Trim();
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < _props.Length; i++)
                {
                    var p = _props[i]; var d = p.itemId.Length > 0 ? Item(p.itemId) : null;
                    if (d == null) continue;
                    bool hit = what.Length == 0 ? pass == 0
                        : pass == 0 ? string.Equals(d.id, what, StringComparison.OrdinalIgnoreCase)
                        : pass == 1 ? string.Equals(d.name, what, StringComparison.OrdinalIgnoreCase) || string.Equals(d.kind, what, StringComparison.OrdinalIgnoreCase)
                        : (d.name ?? "").IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (hit) return i;
                }
            return -1;
        }

        /// <summary>
        /// Where the top of a thing on the desk is just now (by id or name); false when there is no such thing. Used by
        /// the simulator's scripted index finger to press a thing the way a hand would ("simPress").
        /// </summary>
        public bool TryGetThingTop(string what, out Vector3 top)
        {
            top = default;
            int i = _snap != null ? FindProp(what) : -1;
            if (i < 0) return false;
            var p = _props[i]; var d = Item(p.itemId);
            top = p.shown + Vector3.up * PokeTall(p, d);
            return true;
        }

        /// <summary>Debug commands of the things added on 2026-11-17 (poke, records, shows, pointer, volume). True when the command was one of them.</summary>
        bool OnDebugRoom(DebugCommand d)
        {
            switch (d.cmd)
            {
                case "poke": DebugPoke(d.text, d.value); return true;      // (value > 1: the squash that many times slower, for a capture)
                case "records": if (d.value > 0.5f) DebugOpenRecords(); else CloseRecords("the PC"); return true;
                case "recordPick": RecordPick((int)d.value); return true;
                case "recordPage": _recPage += d.value < 0f ? -1 : 1; _recShown = ""; _recTouchedAt = Time.time; return true;
                case "show": DebugShow(d.text, d.value); return true;
                case "arrive": DebugArrive(d.text); return true;           // a thing on the desk arrives once more, as a new thing does (MrArrive)
                case "carry": return true;                                // (the simulator's scripted hand carries a thing: MrSim; nothing to do here)
                case "pointerLog": _ptrLogNow = true; return true;
                case "putAway":                                           // lpctl debug putAway 0 0 0 "": the Put away button that is up is pressed; with a name: that thing is put away
                    if (!string.IsNullOrEmpty(d.text)) { int pi = FindProp(d.text); if (pi >= 0) PutAway(_props[pi].itemId, "the PC (debug)"); else Debug.Log($"[MR] put away: nothing on the desk is called '{d.text}'"); }
                    else if (_putStrip.Shown) { bool ok = PressButton(_putStrip, _putBtn); Debug.Log($"[MR] put away: the button pressed by the PC -> {(ok ? "pressed" : "it was not ready")}"); }
                    else Debug.Log("[MR] put away: no button is up (press a thing first)");
                    return true;
                case "volumeFake": SoundProbeFakeVolume((int)d.value); return true;
            }
            return false;
        }
    }
}
