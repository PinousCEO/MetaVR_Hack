using System;
using LivingPartner.Character;
using LivingPartner.MR;
using LivingPartner.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LivingPartner.Windows
{
    /// <summary>
    /// The PC half of SCREEN SCAMPS (the headset runs the game: MR/MrSwarm.cs, Shared/Swarm.cs). While monsters are
    /// BEHIND THE GLASS of one of this PC's monitors the headset says so ten times a second (Msg.SwarmState, SwarmGlass:
    /// who, where across the screen, how hard each presses against the glass), and this client draws them there, over
    /// the desktop, in a window of its own as large as that monitor - whether the partner is in the PC or out in the
    /// room, and on whichever monitor it is. They are the same shapes as in the room (SwarmMeshes), as large as there
    /// (the headset sends the size of the lit screen in metres). One that is no longer in the list has come out: a ring
    /// of light spreads on the glass where it was. About twice a second this side answers (count -1), so the headset
    /// knows that it need not draw those itself.
    /// The window is made when the first of them gathers and given back a few seconds after the last has gone.
    /// </summary>
    public sealed partial class PcApp
    {
        [Header("Screen Scamps behind the glass (PcCharacter/Swarm)")]
        [SerializeField] PcSwarmView _swarmView;
        public const int SwarmLayer = 5;                    // (Unity's built-in "UI" layer: nothing else of this app is on it)

        SwarmGlass _swg; double _swgAt = -100, _swgAckAt; LayeredOverlay _ovSwarm; float _swgIdle, _swgGlow; int _swgFrame, _swgRing; bool _swgSaid;
        readonly FxQuad[] _swgShots = new FxQuad[SwarmSim.MaxBolts];
        Mesh[] _swgMesh, _swgFlash;
        readonly SwarmGlass _swgAck = new SwarmGlass { on = true, count = -1 };
        readonly int[] _swgId = new int[SwarmGlass.Max], _swgKind = new int[SwarmGlass.Max], _swgShownKind = new int[SwarmGlass.Max];
        readonly float[] _swgU = new float[SwarmGlass.Max], _swgV = new float[SwarmGlass.Max], _swgTu = new float[SwarmGlass.Max], _swgTv = new float[SwarmGlass.Max],
            _swgPress = new float[SwarmGlass.Max], _swgPhase = new float[SwarmGlass.Max], _swgIn = new float[SwarmGlass.Max];
        readonly bool[] _swgSeen = new bool[SwarmGlass.Max];
        readonly float[] _swgRingT = { -1f, -1f, -1f, -1f, -1f, -1f }, _swgRingSize = new float[6];
        readonly Vector3[] _swgRingAt = new Vector3[6];

        void OnSwarmGlass(SwarmGlass g)
        {
            if (g == null || g.count < 0) return;
            if (g.on && (_swg == null || !_swg.on)) Log($"swarm: monsters gather behind the glass of {g.monitorId} ({g.halfW * 2000f:0} x {g.halfH * 2000f:0} mm as the headset measured it)");
            if (!g.on && _swg != null && _swg.on) Log("swarm: the game is over - nothing is behind the glass any more");
            _swg = g; _swgAt = Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>For lpctl debug pcShot (PcGlass.cs): this window's frame laid into the picture of a monitor.</summary>
        bool SwarmBlend(Color32[] px, int w, int h, RectI area) => _ovSwarm != null && _ovSwarm.BlendInto(px, w, h, area);

        void SwarmDispose() { _ovSwarm?.Dispose(); _ovSwarm = null; }

        /// <summary>Called from LateUpdate: places what is behind the glass, renders it into its own window over that monitor.</summary>
        void SwarmLate()
        {
            var v = _swarmView;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f); double now = Time.realtimeSinceStartupAsDouble;
            bool on = _swg != null && _swg.on && now - _swgAt < 1.5 && _suspended.Length == 0 && _swg.halfW > 0.01f && _swg.halfH > 0.01f;
            MonitorDesc mon = null;
            if (on) foreach (var m in _mons) if (m.id == _swg.monitorId) { mon = m; break; }
            bool rings = false;
            for (int i = 0; i < _swgRingT.Length; i++) if (_swgRingT[i] >= 0f) rings = true;
            if (mon == null || (!on && !rings))
            {
                if (v.root.activeSelf) v.root.SetActive(false);
                for (int i = 0; i < _swgId.Length; i++) _swgId[i] = 0;
                if (_ovSwarm != null)
                {
                    _ovSwarm.Hide(); _swgIdle += dt;
                    if (_swgIdle > 4f && _ovSwarm.Pending == 0) { _ovSwarm.Dispose(); _ovSwarm = null; _swgSaid = false; Log("swarm: the window over the monitor is given back"); }
                }
                return;
            }
            var b = mon.bounds;
            if (_ovSwarm != null && (_ovSwarm.BufferW != b.w || _ovSwarm.BufferH != b.h) && _ovSwarm.Pending == 0) { _ovSwarm.Dispose(); _ovSwarm = null; }
            if (_ovSwarm == null)
            {
                try { _ovSwarm = new LayeredOverlay(b.w, b.h, _encodeMaterial); }
                catch (Exception e) { Log("swarm: the window over the monitor could not be made: " + e.Message); _ovSwarm = null; _swg.on = false; return; }
                Log($"swarm: a window of {b.w}x{b.h}px over {mon.id}");
            }
            _swgIdle = 0f;
            if (_swgMesh == null) SwarmMeshes.Build(out _swgMesh, out _swgFlash);
            if (!v.root.activeSelf) { v.root.SetActive(true); for (int i = 0; i < _swgShownKind.Length; i++) _swgShownKind[i] = -1; }
            // "I am drawing them": the headset then draws only what has come out
            if (on && now >= _swgAckAt) { _swgAckAt = now + 0.5; _link.Send(Msg.SwarmState, _swgAck); }

            float hw = _swg.halfW, hh = _swg.halfH;
            Vector3 origin = v.root.transform.position;
            v.gate.transform.position = origin + Vector3.forward * 1.6f;
            float stageWidth = Mathf.Max(hw * 2f, hh * 2f * (16f / 9f));
            v.gate.transform.localScale = new Vector3(stageWidth, stageWidth * (9f / 16f), 1f);
            // ---- who is behind the glass now
            for (int i = 0; i < _swgSeen.Length; i++) _swgSeen[i] = false;
            int n = on ? Mathf.Min(_swg.count, SwarmGlass.Max) : 0;
            for (int k = 0; k < n; k++)
            {
                int slot = -1;
                for (int i = 0; i < _swgId.Length; i++) if (_swgId[i] == _swg.id[k]) { slot = i; break; }
                if (slot < 0)
                {
                    for (int i = 0; i < _swgId.Length; i++) if (_swgId[i] == 0) { slot = i; break; }
                    if (slot < 0) continue;
                    _swgId[slot] = _swg.id[k]; _swgU[slot] = _swg.u[k]; _swgV[slot] = _swg.v[k]; _swgIn[slot] = 0f; _swgPhase[slot] = (_swg.id[k] * 0.37f) % 6.28f;
                }
                _swgKind[slot] = Mathf.Clamp(_swg.kind[k], 0, SwarmKind.Count - 1); _swgTu[slot] = _swg.u[k]; _swgTv[slot] = _swg.v[k]; _swgPress[slot] = _swg.press[k]; _swgSeen[slot] = true;
            }
            int shown = 0;
            for (int i = 0; i < _swgId.Length; i++)
            {
                var r = v.monsterRend[i];
                if (_swgId[i] != 0 && !_swgSeen[i])
                {
                    // it is not in the list any more: it came out through the glass - a ring of light where it was
                    float rad = SwarmKind.Radius[_swgKind[i]];
                    if (on || rings)
                    {
                        _swgRingAt[_swgRing] = origin + new Vector3(_swgU[i] * hw, _swgV[i] * hh, 0.4f);
                        _swgRingSize[_swgRing] = rad * (_swgKind[i] == SwarmKind.Boss ? 5f : 6.5f); _swgRingT[_swgRing] = 0f; _swgRing = (_swgRing + 1) % _swgRingT.Length;
                        _swgGlow = 1f;
                    }
                    _swgId[i] = 0;
                }
                if (_swgId[i] == 0) { if (r.enabled) r.enabled = false; continue; }
                shown++;
                int kind = _swgKind[i]; float radius = SwarmKind.Radius[kind];
                if (_swgShownKind[i] != kind) { _swgShownKind[i] = kind; v.monsterMesh[i].sharedMesh = _swgMesh[kind]; }
                if (!r.enabled) r.enabled = true;
                float f = 1f - Mathf.Exp(-dt * 12f);
                _swgU[i] = Mathf.Lerp(_swgU[i], _swgTu[i], f); _swgV[i] = Mathf.Lerp(_swgV[i], _swgTv[i], f);
                _swgIn[i] = Mathf.Min(1f, _swgIn[i] + dt * 3.5f); _swgPhase[i] += dt;
                float press = _swgPress[i], ph = _swgPhase[i];
                float wob = Mathf.Sin(ph * (kind == SwarmKind.Boss ? 16f : 11f)) * 0.07f * press;
                float s = radius * 2f * press * (1f + wob) * Mathf.Max(0.001f, Ui.Pop(_swgIn[i]));
                Transform t = v.monsters[i];
                // (further back while it mills about, right at the glass when it presses: the nearer ones are drawn over the others)
                t.SetPositionAndRotation(origin + new Vector3(_swgU[i] * hw, _swgV[i] * hh, 0.9f - 0.4f * press + i * 0.004f),
                    Quaternion.Euler(Mathf.Sin(ph * 2.3f) * 8f, Mathf.Sin(ph * 1.7f) * 14f, Mathf.Sin(ph * 3.1f) * 9f));
                t.localScale = new Vector3(s, s * (1f + 0.06f * Mathf.Sin(ph * 6f)), s);
            }
            for (int i = 0; i < _swgShots.Length; i++)
            {
                var shot = _swgShots[i];
                bool visible = on && i < _swg.shotCount && i < (_swg.shotU?.Length ?? 0)
                    && i < (_swg.shotV?.Length ?? 0) && i < (_swg.shotRadius?.Length ?? 0);
                if (!visible) { if (shot != null) shot.enabled = false; continue; }
                if (shot == null)
                {
                    shot = _swgShots[i] = Instantiate(v.rings[0], v.root.transform);
                    shot.mode = 13;
                    shot.SetColors(new Color(1f, 0.63f, 0.21f), new Color(1f, 0.98f, 0.8f));
                }
                shot.transform.position = origin + new Vector3(_swg.shotU[i] * hw, _swg.shotV[i] * hh, 0.2f);
                bool comet = _swg.shotStar != null && i < _swg.shotStar.Length && _swg.shotStar[i];
                var rim = comet ? new Color(.97f, .35f, .66f) : new Color(1f, .49f, .13f);
                if (!shot.color.Equals(rim)) shot.SetColors(rim, new Color(1f, .98f, .84f));
                shot.transform.localRotation = Quaternion.Euler(0, 0, Time.time * 180f + i * 37f);
                shot.transform.localScale = Vector3.one * (_swg.shotRadius[i] * (comet ? 8.4f : 7f));
                shot.enabled = true;
            }
            // ---- rings where they came out, the glow of a crowd
            for (int i = 0; i < _swgRingT.Length; i++)
            {
                var q = v.rings[i]; bool alive = _swgRingT[i] >= 0f;
                if (alive) { _swgRingT[i] += dt / 0.5f; if (_swgRingT[i] >= 1f) { _swgRingT[i] = -1f; alive = false; } }
                if (q.enabled != alive) q.enabled = alive;
                if (!alive) continue;
                float k = _swgRingT[i], e = 1f - (1f - k) * (1f - k);
                q.transform.position = _swgRingAt[i];
                q.transform.localScale = Vector3.one * Mathf.Lerp(_swgRingSize[i] * 0.25f, _swgRingSize[i], e);
                q.SetColors(new Color(1f, 0.84f, 0.42f, 1f - k), new Color(1f, 0.98f, 0.9f, 1f - k));
            }
            _swgGlow = Mathf.MoveTowards(_swgGlow, 0f, dt * 2.2f);
            bool glow = shown > 0 || _swgGlow > 0.01f;
            if (v.glow.enabled != glow) v.glow.enabled = glow;
            if (glow)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 7f), a = Mathf.Clamp01(shown * 0.035f + 0.06f) * (0.7f + 0.3f * pulse) + _swgGlow * 0.30f;
                v.glow.transform.position = origin + new Vector3(0, 0, 1.2f);
                v.glow.transform.localScale = new Vector3(hw * 2.6f, hh * 2.6f, 1f) * (0.9f + 0.1f * pulse + 0.3f * _swgGlow);
                v.glow.SetColors(new Color(1f, 0.88f, 0.56f, a), new Color(1f, 0.97f, 0.86f, a));
            }
            if (!_swgSaid && shown > 0) { _swgSaid = true; Log($"swarm: {shown} drawn behind the glass of {mon.id}"); }

            // ---- its own window over that monitor: every second frame is enough for what mills about
            if (++_swgFrame % 2 == 0 || !_ovSwarm.Shown)
            {
                var prevTarget = _cam.targetTexture; int prevMask = _cam.cullingMask;
                _cam.transform.SetPositionAndRotation(origin + new Vector3(0, 0, -1f), Quaternion.identity);
                _cam.projectionMatrix = Matrix4x4.Ortho(-hw, hw, -hh, hh, _cam.nearClipPlane, Mathf.Max(_cam.farClipPlane, 5f));
                _cam.cullingMask = 1 << SwarmLayer;
                _cam.targetTexture = _ovSwarm.Color;
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _ovSwarm.Color };
                if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
                else _cam.Render();
                _cam.cullingMask = prevMask; _cam.targetTexture = prevTarget;
                if (_ovSwarm.InsertAfter != Win32.HWND_TOPMOST) { _ovSwarm.InsertAfter = Win32.HWND_TOPMOST; _ovSwarm.ZOrderDirty = true; }
                _ovSwarm.Present(b, b);
            }
        }
    }
}
