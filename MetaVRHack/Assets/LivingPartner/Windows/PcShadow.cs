using System;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LivingPartner.Windows
{
    /// <summary>
    /// The user's hands, seen from inside the screen. The headset says where the hands are in front of a display
    /// (Msg.GlassHand, about thirty times a second); here each hand is drawn ON THAT MONITOR as a soft shadow - the
    /// nearer to the glass, the darker, the sharper and the more exactly hand-sized - and the spot the user points at
    /// from further away as a ring. The shadow is the cursor of the glass games (PcGlass.cs), and outside of them the
    /// visible sign that the PC knows where the hand is.
    ///
    /// It is drawn in the GLASS LAYER: a flat picture of one whole monitor (objects under PcCharacter/Glass, on their
    /// own Unity layer), rendered by the overlay camera with an orthographic projection into a second layered window
    /// the size of that monitor. The window is click-through and never takes focus (LayeredOverlay); it exists only
    /// while there is something to draw - a hand, a frame around what the partner reads, a parcel, a game - and is
    /// given back a moment after the last of them is gone, so with no hand near a screen this costs nothing.
    /// In the glass layer the monitor is GU metres high (so that the partner, 0.20 of a game's field tall, keeps its
    /// own size), x to the right and y up from its middle.
    /// </summary>
    public sealed partial class PcApp
    {
        [Header("Hand shadows and the pointing ring (PcCharacter/Glass/Hands)")]
        [Tooltip("Everything of the glass layer: hands, look frame, parcel, the glass games.")]
        [SerializeField] GameObject _glRoot;
        [Tooltip("0 = the right hand, 1 = the left (drawn mirrored).")]
        [SerializeField] Transform[] _hsRoot;
        [SerializeField] UiSprite[] _hsSharp, _hsSoft;
        [SerializeField] Transform _ptRoot;
        [SerializeField] FxQuad _ptOutline, _ptRing, _ptDot;
        [Tooltip("The four ticks that make the ring a CROSSHAIR while a game that is aimed at runs (right, left, up, down).")]
        [SerializeField] FxQuad[] _ptTicks;

        public const int GlassLayer = 1;                    // (Unity's built-in "TransparentFX" layer: nothing else of this app is on it)
        const float HandStale = 0.4f, HandFade = 0.25f, HandFar = 0.45f;
        const float HandArtReach = 0.36f;                   // in glass-hand.png the reach of an open hand (a game's handRadius) is this share of the picture's side
        const float HandArtSide = 0.225f;                   // a real hand: the picture is this many metres across
        static readonly Color HandInk = new Color(0.23f, 0.14f, 0.21f, 1f);      // brown-violet: a storybook shadow, not black
        const float HandMaxAlpha = 0.55f;

        // the glass layer's window
        LayeredOverlay _ovGlass; MonitorDesc _ovGlassMon; float _ovGlassIdle, _ovGlassRetryAt;

        // the hands as the headset (or a debug command, or the bot of a game) last said, and as they are drawn
        GlassHand _gh = new GlassHand(); float _ghAt = -10f; MonitorDesc _ghMon; bool _ghFromHeadset;
        GlassHand _dbgHand; float _dbgHandUntil, _dbgPinchUntil;
        readonly float[] _hsU = new float[2], _hsV = new float[2], _hsDist = new float[2], _hsSpread = { 1f, 1f }, _hsRoll = new float[2], _hsA = new float[2];
        readonly bool[] _hsLive = new bool[2];
        float _ptU, _ptV, _ptA; bool _ptLive;

        /// <summary>Metres of the glass layer per unit of a game's field: the monitor is this high.</summary>
        float GU => _H / GlassGames.PetHeight;
        bool HandsVisible => _hsA[0] > 0.003f || _hsA[1] > 0.003f || _ptA > 0.003f;

        void OnGlassHand(GlassHand h)
        {
            if (h == null || h.hands == null || h.hands.Length < 2 || _glBot) return;      // (while the bot plays, its hand is the hand)
            _gh = h; _ghAt = Time.realtimeSinceStartup; _ghFromHeadset = true;
            _ghMon = ScreenMath.FindMonitor(_mons, h.monitorId) ?? (_glOn ? _glMon : _mon);
        }

        /// <summary>A hand made up here (a debug command, the bot): it is drawn and it plays exactly like one from the headset.</summary>
        void FeedHand(GlassHand h, MonitorDesc m)
        {
            _gh = h; _ghAt = Time.realtimeSinceStartup; _ghMon = m ?? _mon; _ghFromHeadset = false;
        }

        /// <summary>Once a frame: the hands that are drawn follow the newest report, and fade when it grows old.</summary>
        void HandsStep(float dt)
        {
            float now = Time.realtimeSinceStartup;
            if (_dbgHand != null) { if (now < _dbgHandUntil) { _dbgHand.pointPinch = _dbgHand.pointValid && now < _dbgPinchUntil; FeedHand(_dbgHand, _glOn ? _glMon : _mon); } else _dbgHand = null; }
            bool fresh = now - _ghAt < HandStale && _ghMon != null;
            float k = 1f - Mathf.Exp(-dt * 28f);
            for (int i = 0; i < 2; i++)
            {
                var p = _gh.hands[i];
                bool live = fresh && p != null && p.valid && p.dist < HandFar && !float.IsNaN(p.u) && !float.IsNaN(p.v);
                _hsLive[i] = live;
                if (live)
                {
                    float spread = Mathf.Clamp01(p.spread), dist = Mathf.Max(0f, p.dist);
                    if (_hsA[i] <= 0.003f) { _hsU[i] = p.u; _hsV[i] = p.v; _hsDist[i] = dist; _hsSpread[i] = spread; _hsRoll[i] = p.roll; }
                    else
                    {
                        _hsU[i] = Mathf.Lerp(_hsU[i], p.u, k); _hsV[i] = Mathf.Lerp(_hsV[i], p.v, k); _hsDist[i] = Mathf.Lerp(_hsDist[i], dist, k);
                        _hsSpread[i] = Mathf.Lerp(_hsSpread[i], spread, k); _hsRoll[i] = Mathf.LerpAngle(_hsRoll[i], p.roll, k);
                    }
                    _hsA[i] = Mathf.Min(1f, _hsA[i] + dt / 0.10f);
                }
                else _hsA[i] = Mathf.Max(0f, _hsA[i] - dt / HandFade);
            }
            _ptLive = fresh && _gh.pointValid && !float.IsNaN(_gh.pointU) && !float.IsNaN(_gh.pointV);
            if (_ptLive)
            {
                if (_ptA <= 0.003f) { _ptU = _gh.pointU; _ptV = _gh.pointV; }
                else { _ptU = Mathf.Lerp(_ptU, _gh.pointU, k); _ptV = Mathf.Lerp(_ptV, _gh.pointV, k); }
                _ptA = Mathf.Min(1f, _ptA + dt / 0.12f);
            }
            else _ptA = Mathf.Max(0f, _ptA - dt / HandFade);
        }

        /// <summary>A point of a monitor (fractions from its top-left) in the glass layer.</summary>
        Vector3 GlassAt(MonitorDesc m, float u, float v, float z) => new Vector3((u - 0.5f) * m.bounds.w / (float)m.bounds.h * GU, (0.5f - v) * GU, z);
        /// <summary>One pixel of that monitor in the glass layer.</summary>
        float GlassPx(MonitorDesc m) => GU / m.bounds.h;
        float MetresPerPixel(MonitorDesc m)
        {
            var c = _calibs.Find(k => k.monitorId == m.id && k.pixelW == m.bounds.w && k.pixelH == m.bounds.h);
            ScreenMath.PhysicalSize(m, c, out float w, out float _, out string _);
            return Mathf.Max(1e-5f, w / m.bounds.w);
        }

        /// <summary>
        /// Lays the shadows and the ring on the monitor m of the glass layer (hidden when the hands are over another).
        /// In a game the shadow is as large as what the hand touches (GlassGames.HandRadius); otherwise as large as a hand.
        /// </summary>
        void PlaceHands(MonitorDesc m, GlassGame game, GlassInput input, float fade)
        {
            bool here = m != null && _ghMon != null && _ghMon.id == m.id;
            float px = m != null ? GlassPx(m) : 0f;
            for (int i = 0; i < 2; i++)
            {
                float a = here ? _hsA[i] * fade : 0f;
                float near = 1f - Mathf.Clamp01(_hsDist[i] / HandFar);
                float dark = HandMaxAlpha * Mathf.Pow(near, 0.8f) * a;
                bool on = dark > 0.004f;
                if (_hsSharp[i].enabled != on) { _hsSharp[i].enabled = on; _hsSoft[i].enabled = on; }
                if (!on) continue;
                float far = Mathf.Clamp01(_hsDist[i] / HandFar);
                float side;
                if (game != null && input != null)
                {
                    input.spread[i] = _hsSpread[i];
                    side = GlassGames.HandRadius(game, input, i) * FU / HandArtReach;       // the shadow is exactly what touches (FU: the field of a flat game is drawn smaller than the monitor)
                }
                else side = HandArtSide / MetresPerPixel(m) * px * (0.6f + 0.4f * _hsSpread[i]);
                float sharp = Mathf.Clamp01(1f - _hsDist[i] / 0.26f);
                var t = _hsRoot[i];
                t.localPosition = GlassAt(m, _hsU[i], _hsV[i], 0.05f - 0.002f * i);
                t.localRotation = Quaternion.Euler(0, 0, _hsRoll[i]);
                float mirror = i == 1 ? -1f : 1f;
                _hsSharp[i].transform.localScale = new Vector3(side * mirror, side, 1f);
                float grown = side * (1f + 0.55f * far);                                     // further from the glass: larger and fainter, like a real shadow
                _hsSoft[i].transform.localScale = new Vector3(grown * mirror, grown, 1f);
                _hsSharp[i].SetColor(new Color(HandInk.r, HandInk.g, HandInk.b, dark * sharp));
                _hsSoft[i].SetColor(new Color(HandInk.r, HandInk.g, HandInk.b, dark * (1f - 0.72f * sharp)));
            }
            float pa = here ? _ptA * fade : 0f;
            bool ring = pa > 0.004f;
            // In a game that is AIMED at (a script that reads shots() or aim()) the ring is the sight: a crosshair - the ring
            // and four ticks -, steady, closing a little while the trigger is pulled. A script that draws a crosshair of its
            // own (it reads aim()) gets only a small ring in the middle of it, so that there are not two.
            int sight = 0;
            if (ring && game is GlassScript aimed && game.phase != "over") sight = aimed.ReadAim ? 2 : aimed.ReadShots ? 1 : 0;
            bool ticks = ring && sight == 1, dot = ring && sight != 2;
            if (_ptOutline.enabled != ring) { _ptOutline.enabled = ring; _ptRing.enabled = ring; }
            if (_ptDot.enabled != dot) _ptDot.enabled = dot;
            for (int i = 0; i < _ptTicks.Length; i++) if (_ptTicks[i].enabled != ticks) _ptTicks[i].enabled = ticks;
            if (ring)
            {
                _ptRoot.localPosition = GlassAt(m, _ptU, _ptV, 0.04f);
                float size = sight == 2 ? 26f : sight == 1 ? 60f * (_gh.pointPinch ? 0.82f : 1f) : 56f * (1f + 0.07f * Mathf.Sin(Time.time * 5.2f));
                _ptRoot.localScale = Vector3.one * size * px * Mathf.Lerp(1.5f, 1f, Ui.Pop(pa));
                var ink = new Color(0.29f, 0.18f, 0.15f, pa); var cream = new Color(1f, 0.97f, 0.90f, pa);
                _ptOutline.SetColors(ink, ink); _ptRing.SetColors(cream, cream);
                if (dot) _ptDot.SetColors(cream, cream);
                if (ticks) for (int i = 0; i < _ptTicks.Length; i++) _ptTicks[i].SetColors(cream, cream);
            }
        }

        // ------------------------------------------------------------------ the glass layer's window
        /// <summary>Makes sure the layered window of the glass layer covers the monitor m. False when it could not be made.</summary>
        bool GlassOverlayFor(MonitorDesc m)
        {
            if (m == null) return false;
            var b = m.bounds;
            if (_ovGlass != null && (_ovGlassMon == null || _ovGlassMon.id != m.id || _ovGlass.BufferW != b.w || _ovGlass.BufferH != b.h)) { _ovGlass.Dispose(); _ovGlass = null; }
            if (_ovGlass == null)
            {
                if (Time.realtimeSinceStartup < _ovGlassRetryAt) return false;
                try { _ovGlass = new LayeredOverlay(b.w, b.h, _encodeMaterial); }
                catch (Exception e) { Log("glass: the window for " + m.id + " could not be made: " + e.Message); _ovGlass = null; _ovGlassRetryAt = Time.realtimeSinceStartup + 10f; return false; }
                Log($"glass: window {b.w}x{b.h}px on {m.id}");
            }
            _ovGlassMon = m; _ovGlassIdle = 0f;
            return true;
        }

        /// <summary>Renders the glass layer (and, in a game, the partner standing in it) and hands it to its window.</summary>
        void GlassRender(bool withPartner)
        {
            var b = _ovGlassMon.bounds;
            float hh = GU * 0.5f, hw = hh * b.w / b.h;
            var prevTarget = _cam.targetTexture; int prevMask = _cam.cullingMask;
            _cam.transform.SetPositionAndRotation(new Vector3(0, 0, -1f), Quaternion.identity);
            _cam.projectionMatrix = Matrix4x4.Ortho(-hw, hw, -hh, hh, _cam.nearClipPlane, _cam.farClipPlane);
            _cam.cullingMask = withPartner ? (1 << Layer) | (1 << GlassLayer) : 1 << GlassLayer;
            _cam.targetTexture = _ovGlass.Color;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _ovGlass.Color };
            if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
            else _cam.Render();
            _cam.cullingMask = prevMask; _cam.targetTexture = prevTarget;
            if (_ovGlass.InsertAfter != Win32.HWND_TOPMOST) { _ovGlass.InsertAfter = Win32.HWND_TOPMOST; _ovGlass.ZOrderDirty = true; }
            _ovGlass.Present(b, b);
        }

        /// <summary>Nothing of the glass layer is to be seen: its window goes at once, its buffers a moment later.</summary>
        void GlassOverlayRest(float dt)
        {
            if (_glRoot.activeSelf) _glRoot.SetActive(false);
            if (_ovGlass == null) return;
            _ovGlass.Hide();
            _ovGlassIdle += dt;
            if (_ovGlassIdle > 1.5f && _ovGlass.Pending == 0) { _ovGlass.Dispose(); _ovGlass = null; _ovGlassMon = null; Log("glass: window given back"); }
        }

        /// <summary>
        /// Outside of a game: draws what there is on the glass of ONE monitor - the hands' shadows where the hands are,
        /// else the frame around what the partner reads, else a parcel on its way out - in the window of its own.
        /// </summary>
        void GlassFxLate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            MonitorDesc want = null;
            if (_suspended.Length == 0)
            {
                if (HandsVisible && _ghMon != null) want = ScreenMath.FindMonitor(_mons, _ghMon.id);
                if (want == null && LookVisible) want = ScreenMath.FindMonitor(_mons, _lkMonId);
                if (want == null && _dvT >= 0f) want = _mon;
            }
            if (want == null || !GlassOverlayFor(want)) { GlassOverlayRest(dt); return; }
            if (!_glRoot.activeSelf) _glRoot.SetActive(true);
            if (_glGameRoot.activeSelf) _glGameRoot.SetActive(false);
            PlaceHands(want, null, null, 1f);
            PlaceLook(want);
            PlaceParcel(want);
            GlassRender(false);
            GlassDiag("fx");
        }

        double _glDiagAt; float _glFpsMin = 999f, _glFpsSum; int _glFpsN;
        /// <summary>Every two seconds while the glass layer is drawn: what it costs, in the log.</summary>
        void GlassDiag(string what)
        {
            _glFpsMin = Mathf.Min(_glFpsMin, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f)); _glFpsSum += 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f); _glFpsN++;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _glDiagAt) return;
            _glDiagAt = now + 2.0;
            string game = _glOn && _glGame != null ? $" {_glGame.Template} phase={_glGame.phase} score={_glGame.score} lives={_glGame.lives} level={_glGame.level} sprites={_glGame.sprites.Count}" : "";
            Log($"glass diag {what}{game} fps avg={_glFpsSum / Mathf.Max(1, _glFpsN):0} min={_glFpsMin:0} rb={_ovGlass.LastReadbackMs:0.0}ms pending={_ovGlass.Pending} presented={_ovGlass.FramesPresented} hands={_hsA[0]:0.0}/{_hsA[1]:0.0} ring={_ptA:0.0}");
            _glFpsMin = 999f; _glFpsSum = 0f; _glFpsN = 0;
        }

        /// <summary>
        /// lpctl debug glassHand &lt;dist&gt; &lt;u&gt; &lt;v&gt; [right|left|both|fist|point|off]: a hand in front of the partner's
        /// monitor for six seconds, as if the headset had said so (to see the shadow without a headset). "point" adds the
        /// pointing ring at the same spot and keeps the hand away; a negative dist or "off" takes it away.
        /// </summary>
        void DebugHand(DebugCommand d)
        {
            string how = (d.text ?? "").Trim().ToLowerInvariant();
            if (d.value < 0f || how == "off") { _dbgHand = null; _ghAt = -10f; return; }
            var h = new GlassHand { monitorId = (_glOn ? _glMon : _mon)?.id ?? "", t = _link.HostNow };
            bool left = how.Contains("left") || how.Contains("both"), right = !how.Contains("left") || how.Contains("both");
            float spread = how.Contains("fist") ? 0f : 1f;
            if (right) h.hands[0] = new GlassHandPoint { valid = true, u = d.px, v = d.py, dist = d.value, spread = spread, roll = how.Contains("both") ? -12f : 0f };
            if (left) h.hands[1] = new GlassHandPoint { valid = true, u = how.Contains("both") ? d.px - 0.22f : d.px, v = d.py, dist = d.value, spread = spread, roll = how.Contains("both") ? 12f : 0f };
            // "field": px, py are a place of the FIELD of the game that runs (x 0..W, y 0..1 from the floor), not of the monitor
            if (how.Contains("field") && _glOn)
            {
                UvFromField(d.px, d.py, out float fu, out float fv);
                foreach (var p in h.hands) { p.u += fu - d.px; p.v = fv; }
                d.px = fu; d.py = fv;
            }
            // "point": the hand points at that spot from a distance (the ring); "pinch" with it: it pulls the trigger there, once
            // a sign among the words (rock | paper | scissors): what the hands show
            foreach (string word in how.Split(' ')) if (word != "point" && GlassGames.FingersOf(word) >= 0) foreach (var p in h.hands) { p.fingers = GlassGames.FingersOf(word); p.seen = p.valid; }
            if (how.Contains("point")) { h.pointValid = true; h.pointU = d.px; h.pointV = d.py; h.hands[0].valid = false; h.hands[1].valid = false; if (how.Contains("pinch")) _dbgPinchUntil = Time.realtimeSinceStartup + 0.15f; }
            _dbgHand = h; _dbgHandUntil = Time.realtimeSinceStartup + 6f;
            Log($"debug: a hand {(how.Length > 0 ? how : "right")} at ({d.px:0.00}, {d.py:0.00}), {d.value:0.00} m in front of {h.monitorId}");
        }
    }
}
