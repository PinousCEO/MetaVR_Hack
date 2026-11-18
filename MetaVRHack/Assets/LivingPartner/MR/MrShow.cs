using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Shows: the room's share of what the partner visibly does between the room and the computer ("there should be
    /// more of a link between the room and my computer" - the user, 2026-11-17). The PC says what is on
    /// (StateSnapshot.show, a ShowInfo; every new one has a new seq and is played once):
    ///  - deliver   something that was bought arrives: a parcel flies in an arc out of the middle of that display (from
    ///              above when the display is not known here) to the desk beside the partner in 1.2 s, lands, sits
    ///              for a moment, pops open in sparks and is gone - the thing itself comes with the world as always.
    ///  - look      it reads a part of a display: for as long as that lasts and it is out in the room, it turns to the
    ///              middle of the framed part, thinks, and a row of small dots runs from its head to that spot.
    ///  - publish   a thing is put up in the marketplace ON THE SPOT ("just do it where it is - no need to go into the
    ///              PC", the user, 2026-11-17): while the partner is out in the room the little market stall is set
    ///              up on the desk beside it. Stage "stall": the stall (9 cm tall, a picture that stands on the desk
    ///              and faces the user) pops up out of the desk, the thing's picture hops from the partner's hands
    ///              onto the counter, the partner hammers the stall ready - sparks and a knock on every blow. Stage
    ///              "listed": the price tag swings in, the ribbon "Listed!" pops up over the sign in sparkles, "ta-da",
    ///              the partner waves and cheers. Then the stall sinks away. (While the partner is inside the PC the
    ///              PC client draws the same thing there - PcShow. It no longer carries a parcel to the screen.)
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Shows")]
        [Tooltip("The parcel that arrives (deliver).")]
        [SerializeField] UiSprite _showParcel;
        [Tooltip("The dots that run from its head to the part of a screen it is reading.")]
        [SerializeField] FxQuad[] _showDots;
        [Header("Shows: the market stall on the desk (publish)")]
        [SerializeField] UiSprite _stallSprite;
        [SerializeField] UiSprite _stallItem, _stallRibbon;
        [Tooltip("The price tag hangs from this point and swings round it.")]
        [SerializeField] Transform _stallTagPivot;
        [SerializeField] StoryLabel _stallPrice;
        [Tooltip("What stands for a thing that has no picture of its own.")]
        [SerializeField] Texture2D _stallParcelTex;

        const float DeliverFlight = 1.2f, DeliverSit = 0.65f, DeliverOpen = 0.22f;
        // the stall: how tall its picture is (the stall itself fills it but for a hair), how far above the picture's lower
        // edge its ground line is (as a part of the picture), and how far from the partner it stands
        const float StallSize = 0.092f, StallGround = 0.083f, StallBeside = 0.088f;

        int _showSeq = int.MinValue, _showFakeSeq; string _showKind = "", _showStage = "";
        ShowInfo _showFake; float _showFakeUntil;
        float _dlT = -1f, _dlTrail; Vector3 _dlFrom, _dlTo; bool _dlLanded, _dlOpened; string _dlName = "";
        bool _showLooking; Vector3 _showLookAt; int _showLookSeq = int.MinValue;
        // the stall
        bool _stOn, _stLanded, _stListedDone, _stActs; float _stT, _stListedT = -1f, _stSink = -1f, _stSide = 1f, _stYaw, _stPoseW;
        string _stIcon = ""; int _stPriceVal; Vector3 _stFoot;

        void UpdateShow(float dt, bool hostOk)
        {
            ShowInfo s = _showFake != null && Time.time < _showFakeUntil ? _showFake : hostOk && _snap != null ? _snap.show : null;
            bool fake = s != null && s == _showFake;
            string kind = s != null ? s.kind ?? "" : "", stage = s != null ? s.stage ?? "" : "";
            // how long it lasts: as the PC says (a publish goes on until the PC says something else, a minute at most)
            double age = s != null ? _link.HostNow - s.startedAt : 0;
            bool live = kind.Length > 0 && (fake || (kind == "publish" ? age < 60.0 : age < Mathf.Max(0.5f, s.seconds) + 0.5));
            if (s != null && (s.seq != _showSeq || kind != _showKind || stage != _showStage))
            {
                _showSeq = s.seq; _showKind = kind; _showStage = stage;
                // (one that was long over when the app connected is not played again)
                bool stale = !fake && (kind == "publish" ? !live : _link.HostNow - s.startedAt > Mathf.Max(3f, s.seconds + 1f));
                if (kind.Length > 0)
                {
                    Debug.Log($"[MR] show: {kind}{(stage.Length == 0 ? "" : " (" + stage + ")")} #{s.seq} '{s.name}' at {ShortMon(s.monitorId)} uv=({s.u:0.00},{s.v:0.00}) {s.w:0.00}x{s.h:0.00}, price {s.price}, {s.seconds:0.0} s{(fake ? " - made up here for a capture" : "")}{(stale ? " - long over, not played" : "")}");
                    if (!stale && kind == "deliver") StartDeliver(s);
                    if (!stale && kind == "publish" && (stage == "stall" || stage == "listed"))
                    {
                        // here only while the partner is out in the room: inside the PC the PC client sets the stall up
                        if (_ch.owner == Env.Mr)
                        {
                            if (stage == "stall") StallBegin(s, false);
                            else { if (!_stOn || _stSink >= 0f) StallBegin(s, true); if (_stListedT < 0f) _stListedT = 0f; }
                        }
                        else Debug.Log("[MR] show: the partner is inside the PC - the stall is set up there");
                    }
                }
            }
            // whatever was on that this is not, ends
            if (_stOn && _stSink < 0f && !(live && kind == "publish" && (stage == "stall" || stage == "listed"))) { _stSink = 0f; Debug.Log("[MR] show: the stall goes"); }
            TransitionMachine.Visibility(_tr, out bool _, out bool _, out bool clipped);
            bool inRoom = _ch.owner == Env.Mr && _rig.Visible && !clipped;

            // ---- look: it turns to the part of the screen it is reading
            var lookMon = live && s.kind == "look" ? Mon(s.monitorId) : null;
            bool looking = lookMon != null && inRoom && _mode != Mode.Held && _mode != Mode.Hop;
            if (looking)
            {
                _showLookAt = lookMon.ToWorld(Mathf.Clamp01(s.u + s.w * 0.5f), Mathf.Clamp01(s.v + s.h * 0.5f));
                if (_showLookSeq != s.seq)
                {
                    _showLookSeq = s.seq;
                    if (_mode == Mode.Free && !_motor.HasTarget && _doingNow.Length == 0) _motor.Face(YawTo(_showLookAt - _pos));
                }
                if (_emoteT < 0.3f || _emoteForced == Emote.Think) { _emoteForced = Emote.Think; _emoteT = Mathf.Max(_emoteT, 0.3f); }      // a thinking pose for as long as it reads
            }
            _showLooking = looking;
            Vector3 head = Input.HeadPos;
            for (int i = 0; i < _showDots.Length; i++)
            {
                var q = _showDots[i];
                if (q.enabled != looking) q.enabled = looking;
                if (!looking) continue;
                // from just outside its head to just in front of the glass; the dots travel towards the screen
                Vector3 a = _rig.HeadWorld, b = _showLookAt - lookMon.Z * 0.006f;
                Vector3 dir = (b - a).normalized; a += dir * (Skeleton.HeadRadius * _H + 0.012f);
                float f = Mathf.Repeat(i / (float)_showDots.Length + Time.time * 0.30f, 1f);
                Vector3 at = Vector3.Lerp(a, b, f);
                q.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - head, Vector3.up));
                q.transform.localScale = Vector3.one * (0.0075f + 0.0035f * Mathf.Sin(f * Mathf.PI));
                float alpha = Mathf.Clamp01(f / 0.12f) * Mathf.Clamp01((1f - f) / 0.12f);
                q.SetColors(new Color(1f, 0.90f, 0.62f, 0.95f * alpha), new Color(1f, 0.97f, 0.88f, 0.95f * alpha));
            }

            // ---- the parcel that arrives (deliver)
            Vector3 up = Vector3.up;
            if (_dlT >= 0f)
            {
                _dlT += dt;
                float u = Mathf.Clamp01(_dlT / DeliverFlight);
                Vector3 foot; float w = ParcelQuad, h = ParcelQuad, tilt = 0f;
                if (u < 1f)
                {
                    float k = M.Smooth(u), dist = Vector3.Distance(_dlFrom, _dlTo);
                    foot = Vector3.Lerp(_dlFrom, _dlTo, k) + up * ((0.10f + dist * 0.22f) * 4f * u * (1f - u));
                    float grow = Mathf.Lerp(0.45f, 1f, k); w *= grow; h *= grow;
                    tilt = Mathf.Sin(u * Mathf.PI * 3f) * 16f * (1f - u);
                    _dlTrail -= dt;
                    if (_dlTrail <= 0f) { _dlTrail = 0.05f; _sparks.Trail(foot + up * 0.02f, 0.035f); }
                }
                else
                {
                    foot = _dlTo;
                    float since = _dlT - DeliverFlight;
                    if (!_dlLanded)
                    {
                        _dlLanded = true;
                        Vector3 r = Input.HeadRot * Vector3.right; r.y = 0f;
                        _sparks.Burst(foot + up * 0.004f, r.sqrMagnitude > 1e-4f ? r.normalized : Vector3.right, up, 0.035f, 7);
                        Sfx("land", foot, 0.7f); Sfx("sparkle", foot, 0.6f);
                        if (_ch.owner == Env.Mr && _rig.Visible) { _emoteForced = Emote.Happy; _emoteT = 1.6f; if (_mode == Mode.Free && !_motor.HasTarget && _doingNow.Length == 0) _motor.Face(YawTo(foot - _pos)); }
                    }
                    float sq = Mathf.Sin(Mathf.Clamp01(since / 0.16f) * Mathf.PI);      // it gives as it lands
                    w *= 1f + 0.12f * sq; h *= 1f - 0.20f * sq;
                    if (since >= DeliverSit)
                    {
                        if (!_dlOpened)
                        {
                            _dlOpened = true;
                            _sparks.Burst(foot + up * (ParcelTall * 0.6f), Input.HeadRot * Vector3.right, up, 0.08f, 16);
                            Sfx("pop", foot, 0.7f);
                        }
                        float gone = Mathf.Clamp01((since - DeliverSit) / DeliverOpen);
                        w *= (1f + 0.5f * gone) * (1f - gone); h *= (1f + 0.5f * gone) * (1f - gone);
                        if (gone >= 1f) { _dlT = -1f; Debug.Log($"[MR] show: the parcel{(_dlName.Length > 0 ? " with '" + _dlName + "'" : "")} has been opened"); }
                    }
                }
                if (_dlT >= 0f)
                {
                    if (!_showParcel.enabled) _showParcel.enabled = true;
                    Quaternion face = Quaternion.LookRotation(foot + up * (ParcelTall * 0.5f) - head, up) * Quaternion.Euler(0f, 0f, tilt);
                    _showParcel.transform.SetPositionAndRotation(foot + face * Vector3.up * (h * ParcelFoot), face);
                    _showParcel.transform.localScale = new Vector3(Mathf.Max(0.0005f, w), Mathf.Max(0.0005f, h), 1f);
                    _showParcel.SetLook(0f, -1f);
                }
                else if (_showParcel.enabled) _showParcel.enabled = false;
            }
            else if (_showParcel.enabled) _showParcel.enabled = false;

            UpdateStall(dt);
        }

        void StartDeliver(ShowInfo s)
        {
            Vector3 up = Vector3.up, head = Input.HeadPos;
            Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = _worldFrame ? _worldRight : Vector3.Cross(up, fwd);
            // where it lands: on the desk beside the partner (beside its spot while it is inside the PC)
            if (_ch.owner == Env.Mr && _rig.Visible && _mode != Mode.Held) _dlTo = new Vector3(_pos.x, _deskY, _pos.z) + right * 0.075f;
            else if (_homeSet) _dlTo = new Vector3(_home.x, _deskY, _home.z) + right * 0.075f;
            else _dlTo = new Vector3(head.x, _deskY, head.z) + fwd * 0.45f;
            var mv = Mon(s.monitorId);
            _dlFrom = mv != null ? mv.Origin - mv.Z * 0.015f - mv.Y * (ParcelTall * 0.5f) : _dlTo + up * 0.55f - fwd * 0.10f;
            _dlT = 0f; _dlTrail = 0f; _dlLanded = false; _dlOpened = false; _dlName = s.name ?? "";
            _sparks.Burst(_dlFrom, mv != null ? mv.X : right, up, 0.07f, 12);      // out of the glass
            Sfx("whoosh", _dlFrom, 0.6f);
            Debug.Log($"[MR] show: a parcel{(_dlName.Length > 0 ? " with '" + _dlName + "'" : "")} flies from {(mv != null ? "the middle of " + ShortMon(s.monitorId) : "above (that display is not known here)")} to the desk at {_dlTo.ToString("0.00")}");
        }

        // ------------------------------------------------------------------ publish: the market stall on the desk
        /// <summary>The stall comes up beside the partner (already = it is the "listed" stage and no stall stands yet: it is simply there).</summary>
        void StallBegin(ShowInfo s, bool already)
        {
            Vector3 head = Input.HeadPos;
            // does the partner take part? Only when it stands on the desk with its hands free
            _stActs = _rig.Visible && _mode == Mode.Free && TransitionIdle && _game.Length == 0 && !_tn.active;
            Vector3 beside = _stActs ? new Vector3(_pos.x, _deskY, _pos.z) : _homeSet ? new Vector3(_home.x, _deskY, _home.z) : new Vector3(head.x, _deskY, head.z) + Vector3.forward * 0.45f;
            Vector3 toUser = head - beside; toUser.y = 0f; toUser = toUser.sqrMagnitude > 1e-4f ? toUser.normalized : Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, -toUser);      // to the user's right
            // on the side where there is more room among the things on the desk
            float Room(Vector3 at)
            {
                float room = 1f;
                foreach (var p in _props) if (p.itemId.Length > 0 && p.Visible) { var d = Item(p.itemId); if (d != null) room = Mathf.Min(room, Vector2.Distance(new Vector2(p.shown.x, p.shown.z), new Vector2(at.x, at.z)) - d.sizeM * 0.5f); }
                return room;
            }
            _stSide = Room(beside + right * StallBeside) + 0.01f >= Room(beside - right * StallBeside) ? 1f : -1f;
            _stFoot = beside + right * (_stSide * StallBeside);
            _stOn = true; _stT = already ? 2.4f : 0f; _stListedT = -1f; _stSink = -1f; _stLanded = already; _stListedDone = false;
            _stIcon = s.icon ?? ""; _stPriceVal = s.price;
            if (_stActs)
            {
                // on its feet where it is, turned to the stall - and half to the user, who sees it from the side at its work
                _motor.Stop();
                _pos = new Vector3(_pos.x, _deskY, _pos.z);
                _stYaw = YawTo((_stFoot - _pos).normalized * 0.75f + toUser * 0.45f);
            }
            if (!already) Sfx("whoosh", _stFoot, 0.5f);
            Debug.Log($"[MR] show: the stall comes up on the desk at {_stFoot.ToString("0.00")}, to the user's {(_stSide > 0f ? "right" : "left")} of {(_stActs ? "the partner" : "the partner's spot (it cannot take part just now)")}; '{s.name}' picture '{_stIcon}', price {s.price}{(already ? " (already listed: it is simply there)" : "")}");
        }

        /// <summary>The partner's part in it (called where it would otherwise idle or do something with its things). False: it has no part just now.</summary>
        bool StallPose(float dt)
        {
            bool want = _stOn && _stActs && _stSink < 0f && _ch.owner == Env.Mr && _mode == Mode.Free;
            _stPoseW = Mathf.MoveTowards(_stPoseW, want ? 1f : 0f, dt / 0.25f);
            if (!want) return false;
            float t = Time.time;
            if (_stListedT >= 0f) { if (_stListedT < 1.6f) PoseLib.Wave(_pose, t); else PoseLib.Happy(_pose, t); }
            else if (_stT < 1.25f) GlassPoses.Offer(_pose, t, M.Smooth((_stT - 0.25f) / 0.5f));
            else if (_stT < 1.75f) GlassPoses.Offer(_pose, t, 1f - M.Smooth((_stT - 1.25f) / 0.5f));
            else GlassPoses.Offer(_pose, t, 0.25f);
            // it has turned to the stall; when the thing is listed it turns to the user
            Vector3 toUser = Input.HeadPos - _pos; toUser.y = 0f;
            float yaw = _stListedT >= 0.2f ? YawTo(toUser) : _stYaw;
            _rot = Quaternion.Euler(0f, Mathf.MoveTowardsAngle(_rot.eulerAngles.y, yaw, 420f * dt), 0f);
            _pos.y = _deskY;
            _motor.Teleport(new Vector2(_pos.x, _pos.z), _rot.eulerAngles.y);
            return true;
        }

        /// <summary>Once a frame, after the partner has been posed: the stall and what stands on it.</summary>
        void UpdateStall(float dt)
        {
            if (_stOn)
            {
                _stT += dt;
                if (_stListedT >= 0f) _stListedT += dt;
                if (_stSink >= 0f) { _stSink += dt; if (_stSink > 0.4f) { _stOn = false; _stSink = -1f; if (_stActs && _mode == Mode.Free && _ch.owner == Env.Mr) _motor.Face(YawTo(Input.HeadPos - _pos)); } }
                if (_stSink < 0f && (_stT > 40f || _stListedT > 9f)) _stSink = 0f;      // (the PC never said that it was over)
            }
            bool on = _stOn;
            if (_stallSprite.enabled != on) _stallSprite.enabled = on;
            if (!on)
            {
                if (_stallItem.enabled) _stallItem.enabled = false;
                if (_stallRibbon.enabled) _stallRibbon.enabled = false;
                if (_stallTagPivot.gameObject.activeSelf) _stallTagPivot.gameObject.SetActive(false);
                return;
            }
            Vector3 head = Input.HeadPos, up = Vector3.up;
            float S = StallSize, k = S / 0.11f, H = 0.10f * k;      // (the PC's stall is 11 cm for a partner of 10: everything on it in that measure)
            // it stands on the desk and faces the user (turned about the vertical only)
            Vector3 away = _stFoot - head; away.y = 0f; away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            Quaternion face = Quaternion.LookRotation(away, up);
            Vector3 right = face * Vector3.right, near = -away;
            float rise = _stSink >= 0f ? 1f - M.Smooth(_stSink / 0.35f) : Ui.Pop(Mathf.Clamp01(_stT / 0.45f));
            rise = Mathf.Max(0.001f, rise);
            _stallSprite.transform.SetPositionAndRotation(_stFoot + up * ((0.5f - StallGround) * S * rise), face);
            _stallSprite.transform.localScale = new Vector3(S * (0.9f + 0.1f * rise), S * rise, 1f);
            _stallSprite.SetColor(new Color(1f, 1f, 1f, Mathf.Clamp01(rise * 3f)));
            if (_stT - dt <= 0f && rise < 0.5f) { _sparks.Burst(_stFoot + up * 0.004f, right, up, 0.05f, 8); }      // out of the desk

            // the thing: in the partner's hands, then a hop onto the counter (straight onto it when the partner has no part)
            Texture2D icon = Icon(_stIcon);
            _stallItem.SetTexture(icon != null ? icon : _stallParcelTex);
            float itemSize = 0.36f * H;
            Vector3 counter = _stFoot + right * (-_stSide * 0.04f * H) + up * (0.317f * S + itemSize * 0.40f) + near * 0.006f;
            Vector3 hands = _stActs && _rig.Visible ? Vector3.Lerp(_rig.HandRWorld, _rig.ChestWorld, 0.5f) + right * (_stSide * 0.10f * H) + up * (0.16f * H) + near * 0.012f : counter + up * (0.5f * H);
            Vector3 ip; float iscale = 1f;
            if (_stT < 1.15f) { ip = hands; iscale = Ui.Pop(Mathf.Clamp01((_stT - 0.25f) / 0.3f)); }
            else if (_stT < 1.65f)
            {
                float u = (_stT - 1.15f) / 0.5f;
                ip = Vector3.Lerp(hands, counter, M.Smooth(u)) + up * (0.34f * H * 4f * u * (1f - u));
            }
            else
            {
                ip = counter;
                if (!_stLanded) { _stLanded = true; Sfx("land", counter, 0.5f); _sparks.Burst(counter, right, up, 0.022f, 5); }
                float since = _stT - 1.65f;
                iscale = 1f + 0.12f * Mathf.Exp(-since * 9f) * Mathf.Cos(since * 22f);        // it settles with a little wobble
            }
            bool itemOn = _stT >= 0.25f;
            if (_stallItem.enabled != itemOn) _stallItem.enabled = itemOn;
            _stallItem.transform.SetPositionAndRotation(ip, Quaternion.LookRotation(ip - head, up));
            _stallItem.transform.localScale = Vector3.one * (itemSize * Mathf.Max(0.001f, iscale) * (_stSink >= 0f ? rise : 1f));
            _stallItem.SetColor(new Color(1f, 1f, 1f, _stSink >= 0f ? rise : 1f));

            // listed: the price tag swings in beside the awning, the ribbon pops up over the sign
            bool listed = _stListedT >= 0f;
            if (_stallTagPivot.gameObject.activeSelf != listed) _stallTagPivot.gameObject.SetActive(listed);
            if (_stallRibbon.enabled != listed) _stallRibbon.enabled = listed;
            if (listed)
            {
                float lt = _stListedT, gone = _stSink >= 0f ? rise : 1f;
                if (!_stListedDone)
                {
                    _stListedDone = true;
                    Sfx("ta-da", _stFoot + up * S, 0.8f); Sfx("sparkle", _stFoot + up * S, 0.6f);
                    _sparks.Burst(_stFoot + up * (1.0f * S) + near * 0.01f, right, up, H * 0.42f, 12);
                    _stallPrice.text = _stPriceVal > 0 ? _stPriceVal.ToString() : "Free";
                    Debug.Log($"[MR] show: listed - the price tag '{_stallPrice.text}' swings in, the ribbon comes up");
                }
                _stallTagPivot.SetPositionAndRotation(_stFoot + right * (_stSide * 0.36f * S) + up * (0.60f * S) + near * 0.009f,
                    face * Quaternion.Euler(0f, 0f, 64f * Mathf.Exp(-lt * 3.2f) * Mathf.Cos(lt * 8.5f) * _stSide));
                _stallTagPivot.localScale = Vector3.one * (H * Mathf.Max(0.001f, Ui.Pop(Mathf.Clamp01(lt / 0.25f)) * gone));
                Vector3 ribbonAt = _stFoot + up * (1.02f * S + 0.03f * H * Mathf.Sin(lt * 4f)) + near * 0.011f;
                _stallRibbon.transform.SetPositionAndRotation(ribbonAt, Quaternion.LookRotation(ribbonAt - head, up) * Quaternion.Euler(0f, 0f, 3f * Mathf.Sin(lt * 3.3f)));
                float rp = Mathf.Max(0.001f, Ui.Pop(Mathf.Clamp01((lt - 0.12f) / 0.3f)) * gone);
                _stallRibbon.transform.localScale = new Vector3(1.0f * H * rp, 0.326f * H * rp, 1f);
                if (lt > 0.5f && lt < 3.2f && Mathf.Repeat(lt, 0.55f) < dt) _sparks.Trail(ribbonAt + right * (Mathf.Sin(lt * 9f) * 0.5f * H) + up * 0.008f, 0.04f);      // a sparkle now and then round the ribbon
            }
        }

        /// <summary>
        /// lpctl debug show [seconds] 0 0 deliver|look|stall|listed|off: a show made up here, to see the room's share of it
        /// without the PC staging one (stall: the stall comes up and is hammered at for 4.2 s; listed: the tag and the
        /// ribbon, 3 s - after a stall that still stands, or on one that is simply there).
        /// </summary>
        void DebugShow(string kind, float seconds)
        {
            kind = (kind ?? "").Trim().ToLowerInvariant();
            if (kind.Length == 0 || kind == "off") { _showFakeUntil = 0f; return; }
            bool publish = kind == "stall" || kind == "listed";
            var mv = Mon(_snap?.pc?.monitorId) ?? FirstMon();
            ItemDef withIcon = null;
            foreach (var d in World.items) if (!string.IsNullOrEmpty(d.icon)) { withIcon = d; break; }
            _showFake = new ShowInfo
            {
                kind = publish ? "publish" : kind, stage = publish ? kind : "",
                seq = --_showFakeSeq - 1000000, monitorId = mv != null ? mv.desc.id : "", u = 0.30f, v = 0.22f, w = 0.30f, h = 0.30f,
                name = withIcon != null ? withIcon.name : "Little Cactus", icon = withIcon != null ? withIcon.icon : "", price = 15,
                startedAt = _link.HostNow, seconds = seconds > 0.5f ? seconds : kind == "stall" ? 4.2f : kind == "listed" ? 3f : 5f,
            };
            _showFakeUntil = Time.time + _showFake.seconds;
        }
    }
}
