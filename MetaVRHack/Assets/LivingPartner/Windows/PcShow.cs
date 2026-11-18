using System;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.Windows
{
    /// <summary>
    /// SHOWS: what the partner is visibly doing inside the PC (StateSnapshot.show, a ShowInfo; Shared/Glass.cs) - the
    /// link between what was asked in the room and what happens on the computer. Every new one is played once.
    ///   look     the rectangle it reads is framed on the real screen (a dashed frame in brown ink on a cream line, a
    ///            magnifying glass at its corner, a band of light travelling down inside it); when the partner is in
    ///            this PC on that monitor it walks along its floor until it is under the frame and peers at it.
    ///   publish  stage "stall": a little market stall comes up out of the floor beside the partner, the thing hops
    ///            from its hands onto the counter and it hammers the stall ready; stage "listed": the price tag swings
    ///            in, a ribbon "Listed!", sparkles, "ta-da". Afterwards the stall sinks away.
    ///   deliver  a parcel with the thing's picture bounces up from the partner and flies out through the top edge of
    ///            the monitor (the headset draws it arriving in the room).
    ///   operate  it operates the computer for the user (round 4): it hops to a perch BESIDE the spot (never over it - what is
    ///            clicked stays free), a little paper step under its feet; stage "click": it points at the spot and a soft
    ///            ring pulses there; "type": it types; "scroll" / "look": it watches the spot; a small tag at its side
    ///            says the step ("Clicking the second video"); "done" (or no more word from the host): back down, idle.
    /// The frame and the parcel are drawn in the glass layer (PcShadow.cs: a picture of the whole monitor); the stall
    /// stands in the partner's own small window, beside it.
    /// </summary>
    public sealed partial class PcApp
    {
        [Header("Shows: the frame around what the partner reads (PcCharacter/Glass/Look)")]
        [SerializeField] GameObject _lkRoot;
        [Tooltip("The dashes of the frame, laid along it by the app.")]
        [SerializeField] FxQuad[] _lkDashes;
        [Tooltip("The cream line under the dashes: top, bottom, left, right.")]
        [SerializeField] FxQuad[] _lkGlow;
        [SerializeField] UiSprite _lkSticker, _lkShimmer;
        [Header("Shows: a parcel leaving through the top of the screen (PcCharacter/Glass/Parcel)")]
        [SerializeField] Transform _dvRoot;
        [SerializeField] UiSprite _dvParcel, _dvIcon;
        [SerializeField] UiSprite[] _dvSparks;
        [Header("Shows: the market stall beside the partner (PcCharacter/Show)")]
        [SerializeField] GameObject _stRoot;
        [SerializeField] UiSprite _stStall, _stItem, _stRibbon;
        [SerializeField] Transform _stTagPivot;
        [SerializeField] UiSprite _stTag;
        [SerializeField] StoryLabel _stPrice;
        [Tooltip("What stands for a thing that has no picture of its own.")]
        [SerializeField] Texture2D _stParcelTex;

        [Header("Shows: operating the computer (PcCharacter/Operate, in the partner's own window)")]
        [SerializeField] GameObject _opRoot;
        [SerializeField] FxQuad _opRing, _opRingInner, _opPlate, _opTagBg;
        [SerializeField] StoryLabel _opTag;

        int _showSeq = int.MinValue, _showLocalSeq; string _showKind = "", _showStage = "";
        float _showLocalUntil;
        // look
        string _lkMonId = ""; float _lkU, _lkV, _lkW, _lkH, _lkA, _lkT, _lkUntil; bool _lkOn, _lkWalked;
        bool LookVisible => _lkA > 0.003f;
        // the stall
        bool _stOn, _stPlaced, _stLanded, _stListedDone; float _stT, _stListedT = -1f, _stSink = -1f, _stX, _stSide = 1f;
        string _stIcon = ""; int _stPriceVal;
        // deliver
        float _dvT = -1f; bool _dvFromSet; Vector2 _dvFrom; string _dvIconFile = "";
        // operate
        bool _opOn, _opHop, _opHaveTarget, _opPlaced; string _opMonId = "", _opStage = "", _opName = "", _opTagShown = "\u0001";
        float _opU, _opV, _opUntil, _opRestUntil, _opRingT = -1f, _opHopT, _opHopDur, _opArc, _opSide = 1f, _opA, _opStageT;
        Vector2 _opFrom, _opTo; double _opStartedAt; bool _opSpot; float _opRingAt;
        // what a show asks of the partner's body
        readonly LivingPartner.Shared.Pose _spTarget = new LivingPartner.Shared.Pose();
        float _spW;

        /// <summary>The show as the host has it (in every snapshot).</summary>
        void OnShow(ShowInfo s)
        {
            if (s == null || Time.realtimeSinceStartup < _showLocalUntil) return;
            string kind = s.kind ?? "", stage = s.stage ?? "";
            if (s.seq == _showSeq && kind == _showKind && stage == _showStage && (kind != "operate" || s.startedAt == _opStartedAt)) return;      // (one "operate" keeps its number over all its steps: a step is new when startedAt is)
            // (one that was long over when this client connected is not played again)
            bool stale = kind.Length > 0 && kind != "publish" && s.startedAt > 0 && _link.HostNow - s.startedAt > Mathf.Max(3f, s.seconds + 1f);
            ApplyShow(s, stale, false);
        }

        void ApplyShow(ShowInfo s, bool stale, bool local)
        {
            _showSeq = s.seq; _showKind = s.kind ?? ""; _showStage = s.stage ?? "";
            if (_showKind.Length > 0)
                Log($"show: {_showKind}{(_showStage.Length > 0 ? " (" + _showStage + ")" : "")} #{s.seq} '{s.name}' on {s.monitorId} uv=({s.u:0.00},{s.v:0.00}) {s.w:0.00}x{s.h:0.00} price {s.price}, {s.seconds:0.0} s{(local ? " - made up here by the debug command" : "")}{(stale ? " - long over, not played" : "")}");
            // whatever was on that this is not, ends
            if (_showKind != "look" && _lkOn) { _lkOn = false; Log("show: look ended"); }
            if (_stOn && _stSink < 0f && (_showKind != "publish" || (_showStage != "stall" && _showStage != "listed"))) { _stSink = 0f; Log("show: the stall goes"); }
            if (_opOn && (_showKind != "operate" || _showStage == "done")) OperateEnd(_showKind == "operate" ? "the host says it is done" : "another show");
            if (stale) return;
            switch (_showKind)
            {
                case "operate":
                {
                    if (_showStage == "done") break;
                    if (_ch.owner != Env.Pc) { Log("show: the partner is in the room - nothing to draw of 'operate' here"); break; }
                    var om = ScreenMath.FindMonitor(_mons, s.monitorId) ?? _mon;
                    if (om == null) break;
                    _opStartedAt = s.startedAt;
                    // (stage "look" names no spot: it looks at the screen from where it is - on the floor, or on the perch of the step before)
                    bool spot = _showStage != "look";
                    bool moved = spot && (!_opSpot || om.id != _opMonId || Mathf.Abs(s.u - _opU) > 0.004f || Mathf.Abs(s.v - _opV) > 0.004f);
                    _opMonId = om.id; _opStage = _showStage; _opName = s.name ?? ""; _opStageT = 0f;
                    if (spot) { _opU = Mathf.Clamp01(s.u); _opV = Mathf.Clamp01(s.v); _opSpot = true; }
                    float leftOp = s.startedAt > 0 && !local ? (float)(s.startedAt + s.seconds - _link.HostNow) : s.seconds;
                    _opUntil = Time.realtimeSinceStartup + Mathf.Clamp(leftOp, 1f, 30f) + 6f;      // (between two steps it stays on its perch)
                    if (moved) _opHaveTarget = false;
                    _opOn = true;
                    // the host clicks 0.45 s after it has said so (the moment the partner needs to get to the spot): the ring pulses then
                    if (_opStage == "click") { _opRingAt = Time.realtimeSinceStartup + 0.42f; GlassSfxInit(); }
                    if (_bound != IntPtr.Zero) ComeOut("it operates the computer");
                    if (_mon != null && om.id != _mon.id && _xferTo == null) GoMonitor(om.id);
                    break;
                }
                case "look":
                    _lkMonId = (ScreenMath.FindMonitor(_mons, s.monitorId) ?? _mon)?.id ?? "";
                    _lkU = Mathf.Clamp01(s.u); _lkV = Mathf.Clamp01(s.v); _lkW = Mathf.Clamp(s.w, 0.02f, 1f - _lkU); _lkH = Mathf.Clamp(s.h, 0.02f, 1f - _lkV);
                    float left = s.startedAt > 0 ? (float)(s.startedAt + s.seconds - _link.HostNow) : s.seconds;
                    _lkUntil = Time.realtimeSinceStartup + Mathf.Clamp(left, 0.3f, 30f);
                    _lkOn = true; _lkWalked = false; _lkT = 0f;
                    break;
                case "publish":
                    // (the stall stands beside the partner: here only while it is inside this PC - out in the room the headset builds it on the desk)
                    if (_ch.owner != Env.Pc) { Log("show: the partner is in the room - the stall is the headset's to draw"); break; }
                    if (_showStage == "stall") StallBegin(s, false);
                    else if (_showStage == "listed") { if (!_stOn || _stSink >= 0f) StallBegin(s, true); if (_stListedT < 0f) _stListedT = 0f; }
                    break;
                case "deliver":
                    if (_ch.owner == Env.Pc && !_glOn && !_tnOn && _rig.Visible) { _dvT = 0f; _dvFromSet = false; _dvIconFile = s.icon ?? ""; GlassSfxInit(); GlassSfx("pop"); }
                    break;
            }
        }

        void StallBegin(ShowInfo s, bool already)
        {
            _stOn = true; _stT = already ? 2.4f : 0f; _stListedT = -1f; _stSink = -1f; _stLanded = already; _stListedDone = false;
            _stIcon = s.icon ?? ""; _stPriceVal = s.price;
            // beside the partner, on the side where the monitor has room
            LocalToPx(new Vector3(_motor.Pos.x, 0, 0), out float fpx, out float _);
            _stSide = fpx + 2.2f * _H / Mpp < _mon.bounds.Right - 8 ? 1f : -1f;
            _stX = _motor.Pos.x + _stSide * 0.98f * _H;
            _motor.Stop(); _motor.Face(180f - _stSide * 50f);
            GlassSfxInit();
            if (!already) GlassSfx("whoosh");
        }

        /// <summary>Once a frame, whatever the partner is doing: the shows' own clocks.</summary>
        void ShowStep(float dt)
        {
            float now = Time.realtimeSinceStartup;
            if (_showLocalUntil > 0f && now >= _showLocalUntil) { _showLocalUntil = 0f; ApplyShow(new ShowInfo { seq = _showSeq + 1 }, false, true); }
            if (_lkOn && now > _lkUntil) _lkOn = false;
            _lkA = Mathf.Clamp01(_lkA + (_lkOn ? dt / 0.2f : -dt / 0.3f));
            if (_lkA > 0f) _lkT += dt;
            if (_dvT >= 0f) { _dvT += dt; if (_dvT > 1.6f) _dvT = -1f; }
            if (_opOn && (now > _opUntil || _ch.owner != Env.Pc)) OperateEnd(now > _opUntil ? "the host said no more" : "the partner left for the room");
            if (_opRingAt > 0f && now >= _opRingAt) { _opRingAt = 0f; if (_opOn) { _opRingT = 0f; GlassSfx("tap"); } }
            if (_opRingT >= 0f) { _opRingT += dt; if (_opRingT > 1.5f) _opRingT = -1f; }
            _opStageT += dt;
            _opA = Mathf.MoveTowards(_opA, _opOn ? 1f : 0f, dt / 0.25f);
            if (!_opPlaced && _opRoot.activeSelf) _opRoot.SetActive(false);      // (the partner is not roaming this frame)
            _opPlaced = false;
            if (_stOn)
            {
                _stT += dt;
                if (_stListedT >= 0f) _stListedT += dt;
                if (_stSink >= 0f) { _stSink += dt; if (_stSink > 0.4f) { _stOn = false; _stSink = -1f; _motor.Face(180f); } }
                if (_stT > 40f && _stSink < 0f) _stSink = 0f;      // (the host never said that it was over)
                if (_ch.owner != Env.Pc && _stSink < 0f) { _stSink = 0f; Log("show: the partner left for the room - the stall goes"); }
            }
            if (!_stPlaced && _stRoot.activeSelf) _stRoot.SetActive(false);      // the partner is not roaming this frame: no stall without it
            _stPlaced = false;
        }

        // ------------------------------------------------------------------ look: the frame on the glass
        /// <summary>Lays the frame on the monitor m of the glass layer (hidden when another monitor is being read, or none).</summary>
        void PlaceLook(MonitorDesc m)
        {
            bool on = m != null && _lkA > 0.003f && m.id == _lkMonId;
            if (_lkRoot.activeSelf != on) _lkRoot.SetActive(on);
            if (!on) return;
            var bnd = m.bounds; float px = GlassPx(m), a = _lkA;
            // the rectangle in pixels, kept a little inside the monitor so that the line is never cut by its edge
            float x0 = Mathf.Max(8f, _lkU * bnd.w), y0 = Mathf.Max(8f, _lkV * bnd.h);
            float x1 = Mathf.Min(bnd.w - 8f, (_lkU + _lkW) * bnd.w), y1 = Mathf.Min(bnd.h - 8f, (_lkV + _lkH) * bnd.h);
            if (x1 - x0 < 70f) { float c = Mathf.Clamp((x0 + x1) * 0.5f, 43f, bnd.w - 43f); x0 = c - 35f; x1 = c + 35f; }
            if (y1 - y0 < 70f) { float c = Mathf.Clamp((y0 + y1) * 0.5f, 43f, bnd.h - 43f); y0 = c - 35f; y1 = c + 35f; }
            float w = x1 - x0, h = y1 - y0, r = Mathf.Min(16f, Mathf.Min(w, h) * 0.25f);
            Vector3 mid = GlassAt(m, (x0 + x1) * 0.5f / bnd.w, (y0 + y1) * 0.5f / bnd.h, 0.07f);
            _lkRoot.transform.localPosition = mid;
            float grow = Mathf.Lerp(1.04f, 1f, Ui.Pop(a));                         // it closes in on the spot as it appears

            // the cream line
            Color cream = new Color(1f, 0.973f, 0.918f, 0.80f * a);
            for (int i = 0; i < 4; i++)
            {
                bool horizontal = i < 2;
                var g = _lkGlow[i];
                g.transform.localPosition = new Vector3(horizontal ? 0f : (i == 2 ? -w : w) * 0.5f * grow * px, horizontal ? (i == 0 ? h : -h) * 0.5f * grow * px : 0f, 0.004f);
                float len = ((horizontal ? w : h) * grow - r) * px, thick = 11f * px;
                g.transform.localScale = horizontal ? new Vector3(len, thick, 1f) : new Vector3(thick, len, 1f);
                float asp = horizontal ? len / thick : thick / len;
                g.SetParam(new Vector4(1f, 0f, asp, 0.55f));
                g.SetColors(cream, cream);
            }
            // the dashes: brown ink, four pixels. Each side has a whole number of them and the corners are left open
            // (a straight dash laid round a small corner looked broken), so the frame reads as rounded.
            Color ink = new Color(0.29f, 0.18f, 0.15f, a);
            int budget = _lkDashes.Length, used = 0;
            float total = 2f * (w + h - 4f * r);
            for (int side = 0; side < 4; side++)
            {
                bool horizontal = side < 2;
                float len = (horizontal ? w : h) - 2f * r;
                int n = Mathf.Max(1, Mathf.Min(Mathf.RoundToInt(len / 30f), Mathf.FloorToInt(budget * len / Mathf.Max(1f, total))));
                float pitch = len / n, dash = Mathf.Min(pitch * 0.60f, 22f);
                for (int i = 0; i < n && used < budget; i++)
                {
                    var d = _lkDashes[used++];
                    if (!d.enabled) d.enabled = true;
                    float along = -len * 0.5f + (i + 0.5f) * pitch;
                    Vector2 p = side == 0 ? new Vector2(along, h * 0.5f) : side == 1 ? new Vector2(along, -h * 0.5f) : side == 2 ? new Vector2(-w * 0.5f, along) : new Vector2(w * 0.5f, along);
                    d.transform.localPosition = new Vector3(p.x * grow * px, p.y * grow * px, 0f);
                    d.transform.localRotation = Quaternion.Euler(0, 0, horizontal ? 0f : 90f);
                    d.transform.localScale = new Vector3(dash * px, 4.2f * px, 1f);
                    d.SetParam(new Vector4(1f, 0f, dash / 4.2f, 0.25f));
                    d.SetColors(ink, ink);
                }
            }
            for (int i = used; i < budget; i++) if (_lkDashes[i].enabled) _lkDashes[i].enabled = false;
            // the magnifying glass at the top-right corner (pulled inside when the frame touches the monitor's edge)
            float sx = Mathf.Min(x1 + 10f, bnd.w - 34f) - (x0 + x1) * 0.5f, sy = (y0 + y1) * 0.5f - Mathf.Max(y0 - 10f, 34f);
            _lkSticker.transform.localPosition = new Vector3(sx * px, (sy + 3f * Mathf.Sin(_lkT * 3.1f)) * px, -0.006f);
            _lkSticker.transform.localRotation = Quaternion.Euler(0, 0, 6f * Mathf.Sin(_lkT * 2.3f));
            _lkSticker.transform.localScale = Vector3.one * 76f * px * Mathf.Max(0.001f, Ui.Pop(a));
            _lkSticker.SetColor(new Color(1f, 1f, 1f, a));
            // the band of light that goes down inside, again and again
            float band = Mathf.Clamp(h * 0.30f, 28f, 170f), ph = Mathf.Repeat(_lkT / 1.7f, 1f);
            float by = h * 0.5f - band * 0.5f - ph * Mathf.Max(0f, h - band);
            _lkShimmer.transform.localPosition = new Vector3(0f, by * px, 0.006f);
            _lkShimmer.transform.localScale = new Vector3((w - 10f) * px, band * px, 1f);
            _lkShimmer.SetColor(new Color(1f, 0.97f, 0.86f, 0.40f * a * Mathf.Sin(Mathf.PI * ph)));
        }

        // ------------------------------------------------------------------ deliver: a parcel out through the top
        void PlaceParcel(MonitorDesc m)
        {
            bool on = m != null && _dvT >= 0f && _mon != null && m.id == _mon.id;
            if (_dvRoot.gameObject.activeSelf != on) _dvRoot.gameObject.SetActive(on);
            if (!on)
            {
                for (int i = 0; i < _dvSparks.Length; i++) if (_dvSparks[i].enabled) _dvSparks[i].enabled = false;
                return;
            }
            var bnd = m.bounds; float px = GlassPx(m);
            if (!_dvFromSet)
            {
                _dvFromSet = true;
                Vector3 head = _rig.HeadTopWorld;
                LocalToPx(new Vector3(head.x, head.y, 0), out float hx, out float hy);
                _dvFrom = new Vector2(hx - bnd.x, hy - bnd.y);
            }
            float size = Mathf.Clamp(0.62f * _H / MetresPerPixel(m), 90f, 260f), t = _dvT;
            float x = _dvFrom.x, y = _dvFrom.y - size * 0.35f, scale, rot = 0f;
            if (t < 0.4f) { float u = t / 0.4f; y -= size * 0.45f * Mathf.Sin(u * Mathf.PI * 0.5f); scale = Ui.Pop(u); }
            else
            {
                float u = Mathf.Clamp01((t - 0.4f) / 1.0f), e = u * u;
                float y0 = y - size * 0.45f;
                y = Mathf.Lerp(y0, -size, e); x += 26f * Mathf.Sin(u * 5f) * (1f - u); scale = 1f - 0.2f * e; rot = 14f * Mathf.Sin(u * 7f);
            }
            float alpha = Mathf.Clamp01((1.6f - t) / 0.15f);
            _dvRoot.localPosition = GlassAt(m, x / bnd.w, y / bnd.h, 0.06f);
            _dvRoot.localRotation = Quaternion.Euler(0, 0, rot);
            _dvRoot.localScale = Vector3.one * size * px * Mathf.Max(0.001f, scale);
            _dvParcel.SetColor(new Color(1f, 1f, 1f, alpha));
            var icon = WorldIcon(_dvIconFile);
            bool hasIcon = icon != null;
            if (_dvIcon.enabled != hasIcon) _dvIcon.enabled = hasIcon;
            if (hasIcon) { _dvIcon.SetTexture(icon); _dvIcon.SetColor(new Color(1f, 1f, 1f, alpha)); }
            // a few sparkles left behind on its way
            for (int i = 0; i < _dvSparks.Length; i++)
            {
                float back = 0.09f * (i + 1), tb = t - back;
                bool show = t > 0.45f && tb > 0.4f;
                if (_dvSparks[i].enabled != show) _dvSparks[i].enabled = show;
                if (!show) continue;
                float u = Mathf.Clamp01((tb - 0.4f) / 1.0f), e = u * u;
                float yb = Mathf.Lerp(_dvFrom.y - size * 0.8f, -size, e), xb = _dvFrom.x + 26f * Mathf.Sin(u * 5f) * (1f - u) + (i % 2 == 0 ? -1f : 1f) * size * 0.22f;
                // (the sparkles are not children of the parcel: they stay behind where it was)
                _dvSparks[i].transform.localPosition = GlassAt(m, xb / bnd.w, yb / bnd.h, 0.065f);
                _dvSparks[i].transform.localRotation = Quaternion.Euler(0, 0, t * 140f + i * 40f);
                _dvSparks[i].transform.localScale = Vector3.one * size * px * (0.34f - 0.05f * i);
                _dvSparks[i].SetColor(new Color(1f, 1f, 1f, alpha * (1f - 0.2f * i)));
            }
        }

        // ------------------------------------------------------------------ what a show asks of the partner (called by Roam)
        /// <summary>After the motor has moved the partner: the pose a show asks for is laid over what it was doing.</summary>
        void ShowPose(float dt)
        {
            bool want = false; float t = Time.time;
            double now = Time.realtimeSinceStartupAsDouble;
            if (_opOn && _mon != null && _mon.id == _opMonId && _xferTo == null)
            {
                want = true; _nextWander = now + 6;
                if (_opHop) { PoseLib.Hang(_spTarget); PoseLib.AddLook(_spTarget, 0f, -12f); }
                else if (_opStage == "type") PoseLib.Work(_spTarget, t);
                else if (_opStage == "click")
                {
                    // it points at the spot; the arm gives a little push at the moment of the click
                    PoseLib.Point(_spTarget, t);
                    var arm = _spTarget[Bone.ArmUR]; arm.X = -88f + 7f * Mathf.Sin(_opStageT * 14f) * Mathf.Exp(-_opStageT * 3f); _spTarget[Bone.ArmUR] = arm;
                    PoseLib.AddLook(_spTarget, -_opSide * 18f, 4f);
                }
                else { GlassPoses.Peer(_spTarget, t); PoseLib.AddLook(_spTarget, -_opSide * 22f, _opStage == "scroll" ? 14f * Mathf.Sin(t * 2.2f) : 0f); }
            }
            else if (_stOn && _stSink < 0f)
            {
                want = true; _nextWander = now + 8;
                if (_stListedT >= 0f) { if (_stListedT < 1.6f) PoseLib.Wave(_spTarget, t); else PoseLib.Happy(_spTarget, t); }
                else if (_stT < 1.25f) GlassPoses.Offer(_spTarget, t, M.Smooth((_stT - 0.25f) / 0.5f));
                else if (_stT < 1.75f) GlassPoses.Offer(_spTarget, t, 1f - M.Smooth((_stT - 1.25f) / 0.5f));
                else GlassPoses.Offer(_spTarget, t, 0.25f);
            }
            else if (_dvT >= 0f)
            {
                want = true; _nextWander = now + 6;
                if (_dvT < 0.55f) GlassPoses.Offer(_spTarget, t, 1f); else PoseLib.Wave(_spTarget, t);
            }
            else if (_lkOn && _mon != null && _lkMonId == _mon.id && _xferTo == null)
            {
                _nextWander = now + 6;
                var bnd = _mon.bounds;
                float margin = 0.6f * _H / Mpp;
                float cx = bnd.x + Mathf.Clamp((_lkU + _lkW * 0.5f) * bnd.w, margin, bnd.w - margin), cy = bnd.y + (_lkV + _lkH * 0.5f) * bnd.h;
                float tx = PxToLocal(cx, 0).x;
                if (!_lkWalked)
                {
                    // along its floor until it is under the frame
                    _lkWalked = true;
                    if (Mathf.Abs(tx - _motor.Pos.x) > 0.35f * _H) _motor.MoveTo(new Vector2(tx, PlanBuilder.ResidentDepth(_H)), Mathf.Abs(tx - _motor.Pos.x) > 6f * _H, 180f);
                }
                if (!_motor.HasTarget)
                {
                    want = true;
                    Vector3 at = PxToLocal(cx, cy), head = _rig.HeadWorld;
                    float dx = at.x - head.x, dy = at.y - head.y;
                    GlassPoses.Peer(_spTarget, t);
                    PoseLib.AddLook(_spTarget, Mathf.Clamp(-dx / Mathf.Max(0.02f, Mathf.Abs(dy) + 0.05f) * 40f, -55f, 55f), -Mathf.Clamp(Mathf.Atan2(dy, Mathf.Abs(dx) + 0.08f) * Mathf.Rad2Deg, -10f, 38f));
                }
            }
            _spW = Mathf.MoveTowards(_spW, want ? 1f : 0f, dt / 0.25f);
            if (_spW > 0.001f) LivingPartner.Shared.Pose.Lerp(_pose, _spTarget, M.Smooth(_spW), _pose);
        }

        /// <summary>After the partner's pose has been applied: the stall and what stands on it, beside the partner.</summary>
        void ShowPlace(float dt)
        {
            OperatePlace();
            if (!_stOn) return;
            _stPlaced = true;
            if (!_stRoot.activeSelf) _stRoot.SetActive(true);
            float H = _H, S = 1.1f * H;
            float z = _motor.Pos.y + 0.012f, floor = _floorYM + FloorCorr(_floorYM, z);
            float rise = _stSink >= 0f ? 1f - M.Smooth(_stSink / 0.35f) : Ui.Pop(Mathf.Clamp01(_stT / 0.45f));
            rise = Mathf.Max(0.001f, rise);
            // the stall: it grows out of the floor (its ground line is 0.083 of the picture above its lower edge)
            const float ground = 0.083f;
            _stStall.transform.position = new Vector3(_stX, floor + (0.5f - ground) * S * rise, z);
            _stStall.transform.localScale = new Vector3(S * (0.9f + 0.1f * rise), S * rise, 1f);
            _stStall.SetColor(new Color(1f, 1f, 1f, Mathf.Clamp01(rise * 3f)));

            // the thing: in its hands, then a hop onto the counter
            Texture2D icon = WorldIcon(_stIcon);
            _stItem.SetTexture(icon != null ? icon : _stParcelTex);
            float itemSize = 0.36f * H;
            Vector3 counter = new Vector3(_stX - _stSide * 0.04f * H, floor + 0.317f * S + itemSize * 0.40f, z - 0.02f);
            Vector3 hands = Vector3.Lerp(_rig.HandRWorld, _rig.ChestWorld, 0.5f) + new Vector3(_stSide * 0.10f * H, 0.16f * H, -0.03f);
            Vector3 ip; float iscale = 1f;
            if (_stT < 1.15f) { ip = hands; iscale = Ui.Pop(Mathf.Clamp01((_stT - 0.25f) / 0.3f)); }
            else if (_stT < 1.65f)
            {
                float u = (_stT - 1.15f) / 0.5f;
                ip = Vector3.Lerp(hands, counter, M.Smooth(u)) + Vector3.up * (0.34f * H * 4f * u * (1f - u));
            }
            else
            {
                ip = counter;
                if (!_stLanded) { _stLanded = true; GlassSfx("land"); }
                float since = _stT - 1.65f;
                iscale = 1f + 0.12f * Mathf.Exp(-since * 9f) * Mathf.Cos(since * 22f);        // it settles with a little wobble
            }
            bool itemOn = _stT >= 0.25f;
            if (_stItem.enabled != itemOn) _stItem.enabled = itemOn;
            _stItem.transform.position = ip;
            _stItem.transform.localScale = Vector3.one * itemSize * Mathf.Max(0.001f, iscale) * (_stSink >= 0f ? rise : 1f);
            _stItem.SetColor(new Color(1f, 1f, 1f, _stSink >= 0f ? rise : 1f));

            // listed: the price tag swings in under the awning, the ribbon pops up over the sign
            bool listed = _stListedT >= 0f;
            if (_stTagPivot.gameObject.activeSelf != listed) _stTagPivot.gameObject.SetActive(listed);
            if (_stRibbon.enabled != listed) _stRibbon.enabled = listed;
            if (listed)
            {
                float lt = _stListedT, gone = _stSink >= 0f ? rise : 1f;
                if (!_stListedDone)
                {
                    _stListedDone = true;
                    GlassSfx("ta-da");
                    _sparks.Burst(new Vector3(_stX, floor + 1.0f * S, z - 0.03f), Vector3.right, Vector3.up, H * 0.42f, 9);
                    _stPrice.text = _stPriceVal > 0 ? _stPriceVal.ToString() : "Free";
                }
                _stTagPivot.position = new Vector3(_stX + _stSide * 0.36f * S, floor + 0.60f * S, z - 0.025f);
                _stTagPivot.localRotation = Quaternion.Euler(0, 0, 64f * Mathf.Exp(-lt * 3.2f) * Mathf.Cos(lt * 8.5f) * _stSide);
                _stTagPivot.localScale = Vector3.one * H * Mathf.Max(0.001f, Ui.Pop(Mathf.Clamp01(lt / 0.25f)) * gone);
                _stRibbon.transform.position = new Vector3(_stX, floor + 1.02f * S + 0.03f * H * Mathf.Sin(lt * 4f), z - 0.03f);
                _stRibbon.transform.localRotation = Quaternion.Euler(0, 0, 3f * Mathf.Sin(lt * 3.3f));
                float rp = Mathf.Max(0.001f, Ui.Pop(Mathf.Clamp01((lt - 0.12f) / 0.3f)) * gone);
                _stRibbon.transform.localScale = new Vector3(1.0f * H * rp, 0.326f * H * rp, 1f);
            }
        }

        /// <summary>The stall belongs in the partner's window: its place is added to what that window has to show.</summary>
        void ShowBounds(ref Bounds b)
        {
            if (_opRoot.activeSelf)
            {
                if (_opRing.enabled) b.Encapsulate(new Bounds(_opRing.transform.position, _opRing.transform.localScale));
                if (_opTagBg.enabled) b.Encapsulate(new Bounds(_opTagBg.transform.position, new Vector3(_opTagBg.transform.localScale.x, _opTagBg.transform.localScale.y, 0.001f)));
                if (_opPlate.enabled) b.Encapsulate(new Bounds(_opPlate.transform.position, new Vector3(_opPlate.transform.localScale.x, _opPlate.transform.localScale.y, 0.001f)));
            }
            if (!_stOn || !_stRoot.activeSelf) return;
            float S = 1.1f * _H;
            var stall = new Bounds(new Vector3(_stX, _floorYM + 0.6f * S, _motor.Pos.y), new Vector3(S * 1.04f, S * 1.3f, 0.01f));
            b.Encapsulate(stall);
        }

        // ------------------------------------------------------------------ operate: a perch beside the spot
        void OperateEnd(string why)
        {
            if (!_opOn) return;
            _opOn = false; _opHop = false; _opHaveTarget = false; _opSpot = false; _opRingT = -1f; _opRingAt = 0f;
            _opRestUntil = Time.realtimeSinceStartup + 4f;      // (a moment before it goes back to other work: the next step may be on its way)
            _motor.Face(180f);
            Log("show: operate ended - " + why + "; back down to the floor");
        }

        /// <summary>
        /// Called by Roam after the motor: while it operates the computer the partner is on a perch beside the spot (a hop
        /// takes it there). Returns true while this drives the height of its feet (the floor's fall-to-rest is off).
        /// </summary>
        bool OperateLift(float dt)
        {
            if (!_opOn || !_opSpot || _xferTo != null || _mon == null || _mon.id != _opMonId) return false;
            var bnd = _mon.bounds;
            float sx = bnd.x + _opU * bnd.w, sy = bnd.y + _opV * bnd.h, depth = _motor.Pos.y;
            if (!_opHaveTarget)
            {
                _opHaveTarget = true;
                // beside the spot, on the side of the display's middle - unless there is no room on that side
                float off = 0.78f * _H / Mpp, body = 0.42f * _H / Mpp;
                _opSide = sx > bnd.x + bnd.w * 0.5f ? -1f : 1f;
                if (sx + _opSide * (off + body) < bnd.x + 6 || sx + _opSide * (off + body) > bnd.Right - 6) _opSide = -_opSide;
                Vector3 l = PxToLocal(sx + _opSide * off, sy);
                float top = PxToLocal(0, bnd.y).y - 1.25f * _H;
                float feet = Mathf.Clamp(l.y - 0.50f * _H, _floorYM, Mathf.Max(_floorYM, top));      // (its raised hand is at the height of the spot)
                _opFrom = new Vector2(_motor.Pos.x, _feetYM); _opTo = new Vector2(l.x, feet);
                float dist = (_opTo - _opFrom).magnitude;
                _opHop = dist > 0.10f * _H;
                if (_opHop) { _opHopT = 0f; _opHopDur = Mathf.Clamp(0.26f + dist / (_H * 30f), 0.28f, 0.42f); _opArc = Mathf.Min(0.8f * _H, 0.28f * _H + 0.12f * dist); _motor.Stop(); GlassSfx("whoosh"); }
                Log($"show: operate '{_opStage}' at ({sx:0},{sy:0}) px - the partner {(_opHop ? "hops" : "is")} {(_opSide > 0f ? "right" : "left")} of the spot, feet {(_opTo.y - _floorYM) / Mpp:0} px over the floor");
            }
            float yawWant = 180f + _opSide * 50f;      // turned towards the spot
            _fallV = 0f;
            if (_opHop)
            {
                _opHopT += dt;
                float u = Mathf.Clamp01(_opHopT / _opHopDur), e = M.Smooth(u);
                _motor.Teleport(new Vector2(Mathf.Lerp(_opFrom.x, _opTo.x, e), depth), Mathf.LerpAngle(_motor.Yaw, yawWant, 1f - Mathf.Exp(-dt * 9f)));
                _feetYM = Mathf.Lerp(_opFrom.y, _opTo.y, e) + _opArc * 4f * u * (1f - u);
                if (u >= 1f) { _opHop = false; GlassSfx("land"); _sparks.Burst(new Vector3(_opTo.x, _opTo.y + FloorCorr(_opTo.y, depth) + 0.004f, depth), Vector3.right, Vector3.up, _H * 0.24f, 5); }
                return true;
            }
            _motor.Teleport(new Vector2(_opTo.x, depth), Mathf.LerpAngle(_motor.Yaw, yawWant, 1f - Mathf.Exp(-dt * 9f)));
            _feetYM = _opTo.y;
            return true;
        }

        /// <summary>After the partner's pose has been applied: the ring at the spot, the paper step under its feet, the tag with the step.</summary>
        void OperatePlace()
        {
            bool here = _mon != null && _mon.id == _opMonId && _xferTo == null;
            bool on = here && (_opOn || _opA > 0.001f || _opRingT >= 0f);
            if (!on) return;
            _opPlaced = true;
            if (!_opRoot.activeSelf) _opRoot.SetActive(true);
            var bnd = _mon.bounds;
            Vector3 spot = PxToLocal(bnd.x + _opU * bnd.w, bnd.y + _opV * bnd.h);
            // the ring: two soft pulses at the moment of the click, on the glass
            bool ring = _opRingT >= 0f;
            if (_opRing.enabled != ring) { _opRing.enabled = ring; _opRingInner.enabled = ring; }
            if (ring)
            {
                float u = Mathf.Repeat(_opRingT / 0.7f, 1f), a = (1f - u) * Mathf.Clamp01((1.5f - _opRingT) / 0.2f);
                float size = Mathf.Lerp(0.16f, 0.60f, M.Smooth(u)) * _H;
                _opRing.transform.position = spot + new Vector3(0, 0, 0.0006f); _opRing.transform.localScale = Vector3.one * size;
                _opRingInner.transform.position = spot + new Vector3(0, 0, 0.0004f); _opRingInner.transform.localScale = Vector3.one * size * 0.80f;
                _opRing.SetColors(new Color(0.98f, 0.62f, 0.30f, 0.95f * a), new Color(0.98f, 0.62f, 0.30f, 0.95f * a));
                _opRingInner.SetColors(new Color(1f, 0.97f, 0.90f, 0.95f * a), new Color(1f, 0.97f, 0.90f, 0.95f * a));
            }
            // the paper step it stands on, when it is up in the air
            float z = _motor.Pos.y, corr = FloorCorr(_feetYM, z);
            bool plate = _opOn && _opSpot && !_opHop && _feetYM > _floorYM + 0.03f * _H;
            if (_opPlate.enabled != plate) _opPlate.enabled = plate;
            if (plate)
            {
                _opPlate.transform.position = new Vector3(_motor.Pos.x, _feetYM + corr - 0.026f * _H, z + 0.004f);
                Ui.SizePanel(_opPlate, 0.56f * _H, 0.058f * _H, 0.029f * _H, 0.008f * _H);
            }
            // the tag: over its head, a little to the side away from the spot (over the caption when the partner is saying something)
            bool tag = _opOn && !_opHop && _opName.Length > 0 && _opA > 0.5f;
            if (_opTagBg.enabled != tag) { _opTagBg.enabled = tag; if (!tag) { _opTag.text = ""; _opTagShown = "\u0001"; } }
            if (tag)
            {
                if (_opTagShown != _opName) { _opTagShown = _opName; _opTag.SetWrapped(_opName, 0.125f); _opTag.color = WkInk; }
                float w = Mathf.Clamp(_opTag.Width + 0.016f, 0.03f, 0.145f), h = _opTag.Height + 0.010f;
                Vector3 head = _rig.HeadTopWorld; float d = -_eye.z, k = d > 0.05f ? d / (d + Mathf.Max(0f, head.z)) : 1f;
                var c = new Vector3(_eye.x + (head.x - _eye.x) * k + _opSide * 0.012f, _eye.y + (head.y - _eye.y) * k + 0.010f + h * 0.5f + (_sayBg.enabled ? _sayBg.transform.localScale.y + 0.006f : 0f), 0.0008f);
                _opTagBg.transform.position = c; Ui.SizePanel(_opTagBg, w, h, Mathf.Min(h * 0.45f, 0.010f), 0.0011f);
                _opTag.transform.position = c + new Vector3(0, 0, -0.0004f);
            }
        }

        /// <summary>
        /// lpctl debug show &lt;seconds&gt; &lt;u&gt; &lt;v&gt; look|stall|listed|deliver|click|type|scroll|opdone|off: a show made up here, to see it without the host
        /// staging one (look: the rectangle starts at u, v - 0.30, 0.22 when none is given - and is 0.34 x 0.36 of the monitor).
        /// </summary>
        void DebugShow(DebugCommand d)
        {
            string what = (d.text ?? "").Trim().ToLowerInvariant();
            var s = new ShowInfo { seq = 500000 + ++_showLocalSeq, monitorId = _mon != null ? _mon.id : "", name = "Little Cactus", price = 15, seconds = d.value > 0.5f ? d.value : 6f };
            var world = _link.Snapshot != null ? _link.Snapshot.world : null;
            var withIcon = world != null && world.items != null ? world.items.Find(i => i != null && !string.IsNullOrEmpty(i.icon)) : null;
            if (withIcon != null) { s.icon = withIcon.icon; s.name = withIcon.name; }
            float hold;
            switch (what)
            {
                case "look": s.kind = "look"; s.u = d.px > 0f ? d.px : 0.30f; s.v = d.py > 0f ? d.py : 0.22f; s.w = 0.34f; s.h = 0.36f; hold = s.seconds; break;
                case "stall": s.kind = "publish"; s.stage = "stall"; hold = d.value > 0.5f ? d.value : 4.2f; break;
                case "listed": s.kind = "publish"; s.stage = "listed"; hold = d.value > 0.5f ? d.value : 3f; break;
                case "deliver": s.kind = "deliver"; s.seconds = 3f; hold = 2f; break;
                case "click": case "type": case "scroll": case "opdone":
                    s.kind = "operate"; s.stage = what == "opdone" ? "done" : what; s.u = d.px > 0f ? d.px : 0.5f; s.v = d.py > 0f ? d.py : 0.4f; s.icon = "";
                    s.name = what == "click" ? "Clicking the second video" : what == "type" ? "Typing a search" : what == "scroll" ? "Scrolling down the page" : "";
                    hold = what == "opdone" ? 0.01f : s.seconds; break;
                default: hold = 0.01f; break;
            }
            ApplyShow(s, false, true);
            _showLocalUntil = Time.realtimeSinceStartup + hold;        // until then the host's own (empty) show does not end it
        }
    }
}
