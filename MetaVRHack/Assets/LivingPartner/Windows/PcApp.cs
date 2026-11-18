using System;
using System.Collections.Generic;
using System.IO;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using Pose = LivingPartner.Shared.Pose;
using CharacterInfo = LivingPartner.Shared.CharacterInfo;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SN = System.Numerics;

namespace LivingPartner.Windows
{
    /// <summary>
    /// The Windows character client: an independent transparent desktop app (not a browser page).
    /// Modes: DesktopRoaming (walks the desktop on the taskbar line of a monitor), WorkspaceBound (lives inside a chosen
    /// program window: follows its move/resize, hides when it is minimised, sits directly above it in z-order so other
    /// windows cover it naturally), plus the portal receiver/sender roles driven by the Companion Host.
    /// The Unity world of this app IS the current display frame: metres, origin at the display centre, +z into the screen.
    /// Lives on the root object of Scenes/PcCharacter.unity. The partner prefab, the overlay camera, the portal and every
    /// label / pill are objects in that scene, referenced through the serialized fields below.
    /// </summary>
    public sealed partial class PcApp : MonoBehaviour
    {
        const int Layer = 0;
        [Header("Character and camera")]
        [SerializeField] PartnerRig _rig;
        [SerializeField] Camera _cam;
        [Tooltip("Converts the rendered frame to the premultiplied BGRA the layered window expects (Art/Materials/LPEncode.mat).")]
        [SerializeField] Material _encodeMaterial;
        [Header("Portal")]
        [SerializeField] PortalFx _portalFx;
        [SerializeField] SparkFx _sparks;
        [SerializeField] FxQuad _ring;
        [Header("Status pill and caption")]
        [SerializeField] FxQuad _statusDot;
        [SerializeField] StoryLabel _statusLabel;
        [SerializeField] FxQuad _sayBg;
        [SerializeField] StoryLabel _sayLabel;
        [SerializeField] MeshRenderer _sayLabelRenderer;
        [Header("Debug overlay")]
        [SerializeField] FxQuad _floorLine;
        [SerializeField] FxQuad _dbgCross;
        [SerializeField] StoryLabel _dbgLabel;

        HostLink _link;
        LayeredOverlay _ov;
        readonly PoseMixer _mixer = new PoseMixer();
        readonly PartnerMotor _motor = new PartnerMotor();
        readonly Pose _pose = new Pose();
        readonly CharState _cs = new CharState();

        List<MonitorDesc> _mons = new List<MonitorDesc>();
        MonitorDesc _mon;
        float _wM, _hM; string _sizeSource = "default";
        float _H = 0.10f;
        bool _standalone;
        double _nextMonRefresh, _nextStatus, _nextBeat, _nextSecureCheck, _nextWander;
        uint _pid;

        string _mode = "Hidden";
        string _suspended = "";
        float _floorYM;
        float _fallV;                 // vertical speed while dropping to a new floor (m/s)
        float _feetYM;
        IntPtr _bound; RectI _boundRect; bool _boundMin; string _boundTitle = "";
        bool _initialised;

        TransitionInfo _tr = new TransitionInfo();
        CharacterInfo _ch = new CharacterInfo();
        SettingsMsg _settings = new SettingsMsg();
        List<MonitorCalibration> _calibs = new List<MonitorCalibration>();
        string _calibratingMonitor = "";
        string _prepTid = ""; int _prepFrames; bool _readySent, _approachSent, _holdSent; string _landedTid = "";
        HeadPose _head; double _headAt = -10;
        Vector3 _eye = new Vector3(0, 0, -0.6f);

        float _sayUntil;
        CrossPhase _lastPhase = CrossPhase.Done; float _trailT; string _lastActivity = "idle";
        RectI _dest, _clip; bool _drawSomething;
        float _fps; float _doneUntil;
        float _crossTau, _crossProgress; string _crossPhase = ""; float _latency = 0.04f;
        StreamWriter _log;
        VoiceIo _voice; double _listenT0;
        string _lastLoggedState = "";

        void Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Win32.EnsureDpiAware();
            _pid = Win32.GetCurrentProcessId();
            _standalone = Ui.HasArg("-lp-standalone");
            // -lp-park: every window this client shows is put far off the displays (tests on a PC somebody is working at); see LayeredOverlay.ParkX and `lpctl debug pcShot`
            LayeredOverlay.ParkX = Ui.HasArg("-lp-park") ? -30000 : 0;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LivingAIPartner", "logs");
                Directory.CreateDirectory(dir);
                _log = new StreamWriter(Path.Combine(dir, "pc-client.log"), false) { AutoFlush = true };
            }
            catch { }
            HideUnityMainWindow();
            ReturnFocus();

            RefreshMonitors(true);
            _mon = _mons.Find(m => m.primary) ?? (_mons.Count > 0 ? _mons[0] : null);
            string monArg = Ui.Arg("-lp-monitor", null);
            if (monArg != null) { var wanted = _mons.Find(m => m.id.EndsWith(monArg, StringComparison.OrdinalIgnoreCase)); if (wanted != null) _mon = wanted; }
            UpdateMonitorMetrics();

            _H = _rig.Height;
            _rig.SetLightDir(new Vector3(-0.35f, 0.8f, -0.5f));

            float minMpp = 1f;
            foreach (var m in _mons) { ScreenMath.PhysicalSize(m, null, out float w, out float _, out string _); minMpp = Mathf.Min(minMpp, w / m.bounds.w); }
            int size = Mathf.Clamp(Mathf.CeilToInt(2.5f * _H / minMpp / 64f) * 64, 384, 1408);
            _ov = new LayeredOverlay(size, size, _encodeMaterial);
            Log($"overlay buffer {size}x{size}px, monitors={_mons.Count}, minMpp={minMpp:0.000000}");

            // the overlay camera is authored in the scene; only its render target exists at runtime (sized from the displays)
            _cam.targetTexture = _ov.Color; _cam.enabled = false;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            _ring.enabled = _statusDot.enabled = _floorLine.enabled = _dbgCross.enabled = _sayBg.enabled = false;
            _statusLabel.text = _dbgLabel.text = _sayLabel.text = "";
            _sayLabelRenderer.enabled = false;

            _link = new HostLink { Role = Roles.Pc, DeviceName = Environment.MachineName };
            _link.UrlProvider = () =>
            {
                string port = Ui.Arg("-lp-port", Proto.DefaultPort.ToString());
                _link.Token = ReadLocalToken();
                return new List<string> { $"ws://127.0.0.1:{port}/ws" };
            };
            _link.OnMessage += OnMessage;
            _link.OnSnapshot += OnSnapshot;
            // voice on the PC: used when no headset is connected (the tray's Talk item), and for speaking while the partner is in here
            _voice = _rig.Voice;
            _voice.SetSpatial(false);
            _voice.OnListening = (on, reason) => { if (on) _listenT0 = _link.HostNow; _link.Send(Msg.VoiceListen, new VoiceListen { on = on, reason = reason }); Log("voice: listening " + (on ? "started" : "stopped: " + reason)); };
            _voice.OnClip = (pcm, rate, seconds, source) =>
            {
                double t1 = _link.HostNow;
                _link.Send(Msg.VoiceClip, new VoiceClip { clipId = Guid.NewGuid().ToString("N"), sampleRate = rate, pcm16 = Convert.ToBase64String(pcm), t0 = source == "microphone" ? _listenT0 : t1 - Math.Min(seconds, 4.0), t1 = t1, source = source });
                Log($"voice: sent {seconds:0.0}s ({source})");
            };
            _link.OnConnectionChanged += c => { Log("host connection: " + c); _tn = new TennisState(); GlassOnConnection(); };      // a game does not outlive the connection
            _link.Start();

            if (_mon != null)
            {
                _floorYM = FloorYForDesktop(_mon);
                _feetYM = _floorYM;
                _motor.Teleport(new Vector2(0f, PlanBuilder.ResidentDepth(_H)), 180f);
            }
        }

        internal static string ReadLocalToken()
        {
            try
            {
                // (LP_HOME: the data folder of a trial host - the client it starts inherits the variable and finds that host's token)
                string home = Environment.GetEnvironmentVariable("LP_HOME");
                string p = !string.IsNullOrEmpty(home) ? Path.Combine(Path.GetFullPath(home), "local.token") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LivingAIPartner", "local.token");
                return File.Exists(p) ? File.ReadAllText(p).Trim() : "";
            }
            catch { return ""; }
        }

        /// <summary>Starting a player briefly activates its window. Hand the foreground back to what the user was using.</summary>
        public static void ReturnFocus()
        {
            if (Application.isEditor) return;
            if (long.TryParse(Ui.Arg("-lp-return-focus", "0"), out long h) && h != 0 && Win32.IsWindow(new IntPtr(h))) Win32.SetForegroundWindow(new IntPtr(h));
        }

        void HideUnityMainWindow()
        {
            if (Application.isEditor) return;
            // The Unity player window is only a render host; the visible character lives in the layered overlay window.
            Win32.EnumThreadWindows(Win32.GetCurrentThreadId(), (h, l) =>
            {
                if (Win32.WindowClass(h) == "UnityWndClass")
                {
                    long ex = Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE).ToInt64();
                    Win32.SetWindowLongPtr(h, Win32.GWL_EXSTYLE, new IntPtr((ex | Win32.WS_EX_TOOLWINDOW) & ~Win32.WS_EX_APPWINDOW));
                    Win32.ShowWindow(h, Win32.SW_HIDE);
                }
                return true;
            }, IntPtr.Zero);
        }

        void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            _link?.Dispose();
            ClearCalibrationMarkers();
            _ovGame?.Dispose();
            _ovGlass?.Dispose();
            DocsDispose();      // the pad that takes a click on the file it holds (PcDocs.cs)
            WorkDispose();      // the work queue panel's window and its pad (PcWork.cs)
            SwarmDispose();     // the window of the monsters behind the glass (PcSwarm.cs)
            _ov?.Dispose();
            try { _log?.Dispose(); } catch { }
        }

        void Log(string s)
        {
            try { _log?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {s}"); } catch { }
            Debug.Log("[PC] " + s);
        }

        // ------------------------------------------------------------------ host messages
        void OnSnapshot(StateSnapshot s)
        {
            if (s.transition != null && s.transition.revision >= _tr.revision || s.transition?.transitionId != _tr.transitionId) _tr = s.transition ?? _tr;
            _ch = s.character ?? _ch; _settings = s.settings ?? _settings; _calibs = s.calibrations ?? _calibs;
            _calibratingMonitor = s.calibratingMonitorId ?? "";
            OnTennisState(s.tennis);
            OnGlassState(s.glass);      // a glass game (PcGlass.cs)
            OnShow(s.show);             // what the partner is visibly doing in here (PcShow.cs)
            _rig.SetDesign(_ch.design);
            if (Mathf.Abs(_ch.heightM - _H) > 1e-4f && _ch.heightM > 0.02f) { /* height changes need a restart of the overlay buffer */ }
            UpdateMonitorMetrics();
        }

        void OnMessage(Envelope env)
        {
            switch (env.type)
            {
                case Msg.TransitionUpdate:
                    var t = env.Body<TransitionInfo>();
                    if (t != null && (t.transitionId != _tr.transitionId || t.revision >= _tr.revision)) _tr = t;
                    break;
                case Msg.HeadPose:
                    var h = env.Body<HeadPose>();
                    if (h != null && h.tracked) { _head = h; _headAt = Time.realtimeSinceStartupAsDouble; }
                    break;
                case Msg.Say:
                    // what the partner says is also shown on the desktop, so it works without the headset on
                    var say = env.Body<SayMsg>();
                    if (say != null) { _sayLabel.SetWrapped(say.text, 0.135f); _sayUntil = Time.time + Mathf.Clamp(say.seconds, 2f, 14f); _sayText = say.text ?? ""; }
                    break;
                case Msg.GameState:
                    var game = env.Body<TennisState>();
                    if (game != null) OnTennisState(game);
                    break;
                case Msg.GlassHand: OnGlassHand(env.Body<GlassHand>()); break;         // the user's hands in front of a display (PcShadow.cs)
                case Msg.GlassState: OnGlassState(env.Body<GlassState>()); break;
                case Msg.GlassOutResult: OnGlassOutResult(env.Body<GlassOutResult>()); break;      // what became of something a game sent out of the screen (PcGlass.cs)
                case Msg.SwarmState: OnSwarmGlass(env.Body<SwarmGlass>()); break;   // Screen Scamps: the monsters behind the glass of a monitor (PcSwarm.cs)
                case Msg.PcCommand: OnPcCommand(env.Body<PcCommand>()); break;      // "come to the desktop"
                case Msg.GlassCommand:
                    var gc = env.Body<GlassCommand>();
                    if (gc != null && gc.cmd == "choose" && _glOn) _glGame.Choose(gc.pick);
                    break;
                case Msg.VoiceReply:
                    var vr = env.Body<VoiceReply>();
                    if (vr != null && vr.playOn == Env.Pc) { try { _voice.Play(Convert.FromBase64String(vr.pcm16 ?? ""), vr.sampleRate); Log($"voice: playing {vr.seconds:0.0}s of speech"); } catch { } }
                    break;
                case Msg.Debug:
                    var d = env.Body<DebugCommand>();
                    if (d != null && d.cmd == "pcBindForeground") BindTo(Win32.GetForegroundWindow());
                    if (d != null && d.cmd == "pcUnbind") Unbind("debug");
                    if (d != null && d.cmd == "pcWalkToPx") WalkToPixel(d.px);
                    if (d != null && d.cmd == "pcGoMonitor") GoMonitor(d.text);
                    if (d != null && d.cmd == "pcQuit") Application.Quit();
                    if (d != null) GlassDebug(d);       // glassBot, glassXp, glassStart, glassHand, show (PcGlass.cs)
                    if (d != null) DocsDebug(d);        // docFake file, docClick (PcDocs.cs)
                    if (d != null) WorkDebug(d);        // workFake, workClick, workDrag (PcWork.cs)
                    if (d != null && d.cmd == "pcBindHwnd" && long.TryParse((d.text ?? "").Trim(), out long bindTo)) BindTo(new IntPtr(bindTo));      // a window by its handle (tests)
                    if (d != null && d.cmd == "pcDesktop") OnPcCommand(new PcCommand { cmd = "desktop", monitorId = d.text ?? "" });
                    // with a headset (or the simulator) connected, its microphone is the one that listens
                    bool mrConnected = _link.Snapshot != null && (_link.Snapshot.connectedRoles.Contains(Roles.Quest) || _link.Snapshot.connectedRoles.Contains(Roles.Sim));
                    if (d != null && d.cmd == "listen" && !mrConnected)
                    {
                        if (_voice.Listening) _voice.StopListening("ended from the tray", true);
                        else if (!_voice.StartListening()) { _sayLabel.SetWrapped(_voice.Problem, 0.135f); _sayUntil = Time.time + 4f; Log("voice: " + _voice.Problem); }
                    }
                    if (d != null && d.cmd == "listenCancel") _voice.StopListening("cancelled", false);
                    if (d != null && d.cmd == "voiceFile" && !mrConnected && !_voice.SendFile(d.text)) Log("voice file: " + _voice.Problem);
                    break;
            }
        }

        // ------------------------------------------------------------------ monitors
        void RefreshMonitors(bool force)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!force && now < _nextMonRefresh) return;
            _nextMonRefresh = now + 1.5;
            var list = Win32.EnumerateMonitors();
            if (list.Count == 0) return;
            _mons = list;
            if (_mon != null)
            {
                var same = ScreenMath.FindMonitor(_mons, _mon.id);
                if (same == null)
                {
                    // the display we were on was disconnected: move to the primary display instead of vanishing
                    Log("monitor removed: " + _mon.id + " -> relocating to primary");
                    _mon = _mons.Find(m => m.primary) ?? _mons[0];
                    Unbind("monitor removed");
                    _motor.Teleport(new Vector2(0, PlanBuilder.ResidentDepth(_H)), 180f);
                    _floorYM = _feetYM = FloorYForDesktop(_mon);
                }
                else _mon = same;
                UpdateMonitorMetrics();
            }
        }

        void UpdateMonitorMetrics()
        {
            if (_mon == null) return;
            var c = _calibs.Find(k => k.monitorId == _mon.id && k.pixelW == _mon.bounds.w && k.pixelH == _mon.bounds.h);
            ScreenMath.PhysicalSize(_mon, c, out _wM, out _hM, out _sizeSource);
        }

        float Mpp => _wM / _mon.bounds.w;

        /// <summary>
        /// The partner stands a few centimetres BEHIND the glass, so from the viewer's real eye position its feet would not
        /// line up with the floor line drawn on the glass (parallax). This is the vertical offset that makes a point at
        /// depth z look as if it stands exactly on floorY for the current eye.
        /// </summary>
        float FloorCorr(float floorY, float z) { float d = -_eye.z; return d > 0.05f ? (floorY - _eye.y) * z / d : 0f; }
        Vector3 PxToLocal(float px, float py) => Conv.U(ScreenMath.PixelToLocal(_mon, _wM, _hM, px, py));
        void LocalToPx(Vector3 l, out float px, out float py) => ScreenMath.LocalToPixel(_mon, _wM, _hM, Conv.N(l), out px, out py);
        float FloorYForDesktop(MonitorDesc m) => PxToLocalFor(m, 0, m.work.Bottom).y;
        Vector3 PxToLocalFor(MonitorDesc m, float px, float py)
        {
            var c = _calibs.Find(k => k.monitorId == m.id && k.pixelW == m.bounds.w && k.pixelH == m.bounds.h);
            ScreenMath.PhysicalSize(m, c, out float w, out float h, out string _);
            return Conv.U(ScreenMath.PixelToLocal(m, w, h, px, py));
        }

        void SwitchMonitor(MonitorDesc m, float keepPx)
        {
            if (m == null || m == _mon) return;
            _mon = m; UpdateMonitorMetrics();
            var l = PxToLocal(keepPx, 0);
            _motor.Teleport(new Vector2(l.x, _motor.Pos.y), _motor.Yaw);
        }

        // ------------------------------------------------------------------ workspace binding
        public void BindTo(IntPtr hwnd)
        {
            if (!Win32.IsWorkspaceCandidate(hwnd, _pid)) { Log("bind rejected (not a workspace window)"); return; }
            _bound = hwnd; _boundTitle = Win32.WindowTitle(hwnd); _boundMin = Win32.IsIconic(hwnd);
            Win32.FrameBounds(hwnd, out _boundRect);
            _ov.ZOrderDirty = true;
            Log($"WorkspaceBound -> hwnd=0x{hwnd.ToInt64():X} '{_boundTitle}' rect={_boundRect}");
        }

        void Unbind(string why)
        {
            if (_bound == IntPtr.Zero) return;
            Log("unbind workspace: " + why);
            _bound = IntPtr.Zero; _boundMin = false; _ov.InsertAfter = Win32.HWND_TOPMOST; _ov.ZOrderDirty = true;
        }

        /// <summary>
        /// Out of the window it lives in, onto the desktop of this display: the window was minimised, closed or hidden, the
        /// user asked ("come to the desktop"), or it has work to do out there. A small hop, then it walks the taskbar line.
        /// </summary>
        void ComeOut(string why)
        {
            if (_bound == IntPtr.Zero) return;
            Unbind(why);
            if (_mon == null) return;
            float desk = FloorYForDesktop(_mon);
            // the hop: from where its feet are (never from under the desktop's floor), up a little, then the ordinary fall to the taskbar line
            _feetYM = Mathf.Max(_feetYM, desk) + 0.004f; _fallV = -0.62f; _hopOut = true;
            var b = _mon.bounds; float margin = 0.6f * _H / Mpp;
            LocalToPx(new Vector3(_motor.Pos.x, 0, 0), out float fpx, out float _);
            float px = Mathf.Clamp(fpx, b.x + margin, b.Right - margin);
            // ... and a few steps along the taskbar, towards the middle of the display
            float to = Mathf.Clamp(px + (px < b.x + b.w * 0.5f ? 1f : -1f) * 1.6f * _H / Mpp, b.x + margin, b.Right - margin);
            _motor.Teleport(new Vector2(PxToLocal(px, 0).x, PlanBuilder.ResidentDepth(_H)), 180f);
            _walkOutX = PxToLocal(to, 0).x;
            _nextWander = Time.realtimeSinceStartupAsDouble + 8;
            _nextStatus = 0;      // the host hears of the new mode at once
            GlassSfx("whoosh");
            Log($"came out onto the desktop of {_mon.id} at x={px:0} px - {why}");
        }
        bool _hopOut; float _walkOutX = float.NaN;

        /// <summary>Host -> PC (Msg.PcCommand). desktop: leave the window it is bound to (also when that is in front) and walk the desktop.</summary>
        void OnPcCommand(PcCommand c)
        {
            if (c == null) return;
            if (c.cmd != "desktop") { Log($"pc.command '{c.cmd}': not known here"); return; }
            var m = string.IsNullOrEmpty(c.monitorId) ? null : _mons.Find(x => x.id.EndsWith(c.monitorId, StringComparison.OrdinalIgnoreCase));
            Log($"pc.command desktop{(m != null ? " on " + m.id : "")}: mode {_mode}, bound=0x{_bound.ToInt64():X}{(_ch.owner != Env.Pc ? " - the partner is not inside this PC" : "")}");
            if (_opOn) OperateEnd("asked to come to the desktop");
            if (m != null && _mon != null && m.id != _mon.id && _xferTo == null) GoMonitor(m.id);      // (leaves the window too, and walks over to that display)
            else if (_bound != IntPtr.Zero) ComeOut("asked to come to the desktop");
            else if (_mon != null && _xferTo == null && _ch.owner == Env.Pc)
            {
                // already out: a few steps, so that it is seen to have heard
                var b = _mon.bounds; float margin = 0.6f * _H / Mpp;
                LocalToPx(new Vector3(_motor.Pos.x, 0, 0), out float fpx, out float _);
                float to = Mathf.Clamp(fpx + (fpx < b.x + b.w * 0.5f ? 1f : -1f) * 1.6f * _H / Mpp, b.x + margin, b.Right - margin);
                _motor.MoveTo(new Vector2(PxToLocal(to, 0).x, PlanBuilder.ResidentDepth(_H)), false, 180f);
                _nextWander = Time.realtimeSinceStartupAsDouble + 8;
            }
            _nextStatus = 0;
        }

        void WalkToPixel(float px)
        {
            if (_mon == null) return;
            _motor.MoveTo(new Vector2(PxToLocal(px, 0).x, PlanBuilder.ResidentDepth(_H)), Mathf.Abs(PxToLocal(px, 0).x - _motor.Pos.x) > 6f * _H, 180f);
        }

        string _fullscreenNote = ""; double _nextFsCheck; bool _fsCached;
        bool FullscreenAppOnMonitor()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextFsCheck) return _fsCached;
            _nextFsCheck = now + 0.5;
            _fsCached = false;
            IntPtr fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Win32.GetShellWindow()) return false;
            string cls = Win32.WindowClass(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            if (!Win32.FrameBounds(fg, out RectI r)) return false;
            var b = _mon.bounds;
            _fsCached = r.x <= b.x && r.y <= b.y && r.Right >= b.Right && r.Bottom >= b.Bottom;
            return _fsCached;
        }

        /// <summary>Updates floor, clip rectangle and z-order for the current mode. Returns false when nothing may be shown.</summary>
        bool UpdateWorkspace()
        {
            float insetPx = 4f;
            if (_bound != IntPtr.Zero)
            {
                // The window it lives in is gone from the screen: it does not vanish with it (it did - "I minimise the window and
                // it does not come out to the desktop", the user, 2026-11-18) - it hops out onto the desktop of this display.
                if (!Win32.IsWindow(_bound)) { ComeOut("its window was closed"); }
                else if (Win32.IsIconic(_bound)) { ComeOut("its window was minimised"); }
                else if (!Win32.IsWindowVisible(_bound) || Win32.IsCloaked(_bound)) { ComeOut("its window was hidden"); }
                else
                {
                    _boundMin = false;
                    if (Win32.FrameBounds(_bound, out RectI r))
                    {
                        if (r.x != _boundRect.x || r.y != _boundRect.y || r.w != _boundRect.w || r.h != _boundRect.h)
                        {
                            // the window carries its resident: shift with it, then keep it inside the new width
                            LocalToPx(new Vector3(_motor.Pos.x, 0, 0), out float fpx, out float _);
                            float rel = _boundRect.w > 0 ? (fpx - _boundRect.x) / _boundRect.w : 0.5f;
                            float npx = r.x + Mathf.Clamp01(rel) * r.w;
                            var nm = ScreenMath.FindMonitor(_mons, r.x + r.w * 0.5f, r.y + r.h * 0.5f);
                            if (nm != null && nm.id != _mon.id) SwitchMonitor(nm, npx);
                            float nx = PxToLocal(npx, 0).x;
                            _motor.Teleport(new Vector2(nx, _motor.Pos.y), _motor.Yaw);
                            _boundRect = r;
                        }
                        float margin = 0.3f * _H / Mpp;
                        float minX = PxToLocal(r.x + margin, 0).x, maxX = PxToLocal(r.Right - margin, 0).x;
                        if (maxX > minX) _motor.Pos = new Vector2(Mathf.Clamp(_motor.Pos.x, minX, maxX), _motor.Pos.y);
                        _floorYM = PxToLocal(0, r.Bottom - insetPx).y;
                        _clip = RectI.Intersect(r, _mon.bounds);
                        // sit directly above the workspace window: anything above it also covers the partner
                        IntPtr above = Win32.GetWindow(_bound, Win32.GW_HWNDPREV);
                        while (above != IntPtr.Zero && (above == _ov.Hwnd || !Win32.IsWindowVisible(above))) above = Win32.GetWindow(above, Win32.GW_HWNDPREV);
                        IntPtr want = above == IntPtr.Zero ? Win32.HWND_TOP : above;
                        IntPtr belowUs = Win32.GetWindow(_ov.Hwnd, Win32.GW_HWNDNEXT);
                        while (belowUs != IntPtr.Zero && !Win32.IsWindowVisible(belowUs)) belowUs = Win32.GetWindow(belowUs, Win32.GW_HWNDNEXT);
                        bool topmost = (Win32.GetWindowLongPtr(_ov.Hwnd, Win32.GWL_EXSTYLE).ToInt64() & Win32.WS_EX_TOPMOST) != 0;
                        bool targetTopmost = (Win32.GetWindowLongPtr(_bound, Win32.GWL_EXSTYLE).ToInt64() & Win32.WS_EX_TOPMOST) != 0;
                        if (topmost && !targetTopmost) Win32.SetWindowPos(_ov.Hwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                        if (belowUs != _bound || _ov.ZOrderDirty)
                        {
                            _ov.InsertAfter = want; _ov.ZOrderDirty = true;
                            if (_ov.Shown) Win32.SetWindowPos(_ov.Hwnd, want, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
                        }
                        _mode = "WorkspaceBound";
                        return true;
                    }
                }
            }
            // (not while a portal transition is running: the user explicitly asked for that, and it must be able to finish)
            var tsNow = TransitionMachine.Parse(_tr.state);
            bool transitionActive = tsNow != TState.Idle && tsNow != TState.Complete && tsNow != TState.Cancelled;
            if (!transitionActive && FullscreenAppOnMonitor())
            {
                // a full-screen app (game, video, presentation) owns this display: stay out of its way until it leaves
                _mode = "Suspended"; _fullscreenNote = "full-screen app in the foreground on this display";
                return false;
            }
            _fullscreenNote = "";
            _mode = "DesktopRoaming";
            _floorYM = FloorYForDesktop(_mon);
            _clip = _mon.bounds;
            if (_ov.InsertAfter != Win32.HWND_TOPMOST) { _ov.InsertAfter = Win32.HWND_TOPMOST; _ov.ZOrderDirty = true; }
            return true;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            _link.Pump();
            RefreshMonitors(false);
            if (_mon == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(dt, 1e-4f), 0.05f);
            double now = Time.realtimeSinceStartupAsDouble;

            if (now >= _nextSecureCheck)
            {
                _nextSecureCheck = now + 0.5;
                // UAC secure desktop / lock screen: pause instead of trying to draw over protected UI
                _suspended = Win32.IsSecureDesktopActive() ? "secure desktop (UAC / lock screen)" : "";
            }

            bool hostOk = _link.Connected && _link.Snapshot != null;
            _drawSomething = false; _portalFx.Begin();
            _ring.enabled = false; _statusDot.enabled = false; _statusLabel.text = ""; _dbgCross.enabled = false; _floorLine.enabled = false;
            bool showCharacter = false, tennisRan = false, glassRan = false;
            _rig.ClearClip();
            if (!TennisWanted) _tnRefused = false;
            HandsStep(dt); ShowStep(dt); GlassSfxPump();      // the hands in front of the glass, the shows' clocks, the sound kit
            DocsStep(dt);                                     // a file the partner carried in: is it held up, was it clicked (PcDocs.cs)
            WorkStep(dt);                                     // the work queue panel: the list, the mouse on it (PcWork.cs)

            if (_suspended.Length > 0) { _mode = "Suspended"; }
            else if (!hostOk)
            {
                if (_standalone) { showCharacter = Roam(dt, true); }
                else _mode = "Hidden";
            }
            else
            {
                var st = TransitionMachine.Parse(_tr.state);
                TransitionMachine.Visibility(_tr, out bool _, out bool pcDraws, out bool clipped);
                if (st.ToString() != _lastLoggedState) { _lastLoggedState = st.ToString(); Log($"transition {_tr.transitionId} state={st} owner={_tr.owner} dir={_tr.direction} rev={_tr.revision}"); }
                bool pre = st == TState.TargetSelected || st == TState.Approaching || st == TState.ReceiverReady ||
                           (st == TState.Hold && TransitionMachine.Parse(_tr.heldState) != TState.Crossing);
                if (clipped && _tr.plan != null) showCharacter = Crossing(now);
                else if (pre && _tr.direction == 0) { PrepareReceive(); showCharacter = false; }
                else if (pre && _tr.direction == 1) showCharacter = ApproachExit(dt);
                else if (pcDraws)
                {
                    if (_tr.transitionId != _landedTid && st == TState.Complete && _tr.direction == 0 && _tr.plan != null) LandFromPlan();
                    // a game of tennis takes the whole monitor: the court replaces the desktop floor until it is over
                    if (TennisWanted && !_tnOn && !_glOn) TennisEnter();
                    tennisRan = _tnOn;
                    // a glass game takes the monitor too, but leaves the desktop to be seen (one game at a time)
                    if (GlassWanted && !_glOn && !_tnOn) GlassEnter();
                    glassRan = _glOn;
                    showCharacter = _tnOn ? TennisFrame(dt) : _glOn ? GlassFrame(dt) : Roam(dt, false);
                }
                else { _mode = "Hidden"; if (_bound != IntPtr.Zero && st != TState.Hold) Unbind("the partner left the PC"); }
            }
            if (_tnOn && !tennisRan) TennisLeave("the partner cannot play here now");
            if (_glOn && !glassRan) GlassLeave("the partner cannot play here now");

            _rig.SetVisible(showCharacter);
            DrawSay(showCharacter && _mode != "Crossing" && !_glOn);      // (in a glass game its line is written on the field: PcGlass.cs)
            _portalFx.Step(dt, hostOk ? _link.HostNow : Time.realtimeSinceStartupAsDouble);
            _sparks.Step(dt, Quaternion.identity);
            if (_portalFx.Visible) _drawSomething = true;      // let the portal fade out instead of vanishing
            if (showCharacter) _drawSomething = true;
            if (_calibratingMonitor.Length > 0) DrawCalibrationMarkers(); else ClearCalibrationMarkers();

            if (hostOk && now >= _nextStatus)
            {
                _nextStatus = now + 0.2;
                LocalToPx(new Vector3(_rig.transform.position.x, _feetYM, 0), out float fx, out float fy);
                _link.Send(Msg.PcStatus, new PcStatus
                {
                    overlayVisible = _ov.Shown || (_tnOn && _ovGame != null && _ovGame.Shown) || (_glOn && _ovGlass != null && _ovGlass.Shown), mode = _mode, monitorId = _mon.id, pxX = fx, pxY = fy,
                    boundHwnd = _bound.ToInt64(), boundMinimized = _boundMin, suspendedReason = _suspended.Length > 0 ? _suspended : (_mode == "Suspended" ? _fullscreenNote : ""), fps = _fps, design = _rig.DesignId,
                });
            }
        }

        void LateUpdate()
        {
            if (_mon == null || _ov == null) return;
            Diag();
            WorkLate();                                                       // the work queue panel: a window of its own, with or without the partner (PcWork.cs)
            SwarmLate();                                                      // Screen Scamps: the monsters behind the glass, a window of their own (PcSwarm.cs)
            if (_tnOn && _ovGame != null && _suspended.Length == 0) { GlassFxLate(); TennisLate(); return; }
            TennisDropWindow();
            if (_glOn && _suspended.Length == 0) { GlassLate(); return; }     // a glass game: the whole monitor, the partner in it
            GlassFxLate();                                                    // hands' shadows, the frame of a look, a parcel: a window of their own, only while there is one of them
            if (!_drawSomething || _suspended.Length > 0) { _cam.enabled = false; _ov.Hide(); return; }
            // --- eye: tracked headset position when fresh, otherwise straight in front of the character
            Bounds b = new Bounds(_portalFx.Visible ? _portalFx.Position : _ring.transform.position, Vector3.one * _H);
            if (_rig.Visible && _mode == "Crossing")
            {
                // follow only the part that is already inside the screen; before anything is through, stay on the portal
                var part = _rig.BoneBoundsOnSide(Vector3.zero, Vector3.forward, 0.25f * _H, out bool any);
                if (any) b = part; else _ring.enabled = true;
            }
            else if (_rig.Visible) b = _rig.BoneBounds();
            if (_ring.enabled || _portalFx.Visible) b.Encapsulate(new Bounds(_portalFx.Position, new Vector3(_portalFx.Extent, _portalFx.Extent, 0.001f)));
            if (_sayBg.enabled) b.Encapsulate(new Bounds(_sayBg.transform.position, new Vector3(_sayBg.transform.localScale.x, _sayBg.transform.localScale.y, 0.001f)));
            ShowBounds(ref b);      // the market stall beside it (PcShow.cs)
            DocsBounds(ref b);      // the file in its hands (PcDocs.cs)
            bool tracked = _head != null && _head.monitorId == _mon.id && Time.realtimeSinceStartupAsDouble - _headAt < 0.6;
            Vector3 targetEye = tracked ? Conv.U(_head.eye) : new Vector3(b.center.x, b.center.y, -0.6f);
            if (targetEye.z > -0.08f) targetEye.z = -0.08f;
            _eye = Vector3.Lerp(_eye, targetEye, tracked ? 0.6f : 1f);

            // --- destination rectangle: projection of the body bounds through the eye onto the display plane
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            float d = -_eye.z;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float z = Mathf.Max(c.z, 0f);
                float k = d / (d + z);
                var p = new Vector2(_eye.x + (c.x - _eye.x) * k, _eye.y + (c.y - _eye.y) * k);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            Vector2 centre = (min + max) * 0.5f;
            LocalToPx(new Vector3(centre.x, centre.y, 0), out float cpx, out float cpy);
            int w = _ov.BufferW, h = _ov.BufferH;
            _dest = new RectI(Mathf.RoundToInt(cpx - w * 0.5f), Mathf.RoundToInt(cpy - h * 0.5f), w, h);
            Vector3 tl = PxToLocal(_dest.x, _dest.y), br = PxToLocal(_dest.Right, _dest.Bottom);
            ScreenMath.OffAxis(Conv.N(_eye), tl.x, br.x, br.y, tl.y, _cam.nearClipPlane, out float l, out float r, out float bt, out float tp);
            _cam.transform.SetPositionAndRotation(_eye, Quaternion.identity);
            _cam.projectionMatrix = Matrix4x4.Frustum(l, r, bt, tp, _cam.nearClipPlane, _cam.farClipPlane);
            _pendingRender = true;

            if (_settings.debugOverlay)
            {
                _dbgLabel.transform.position = new Vector3(tl.x + 0.004f, tl.y - 0.004f, 0.001f);
                _dbgLabel.text = $"PC {_mode} mon={_mon.id.Replace("\\\\.\\", "")} src={_sizeSource}\nstate={_tr.state} owner={_tr.owner}\ntau={_crossTau:0.000} prog={_crossProgress * 100f:0}% {_crossPhase}\nfps={_fps:0} rb={_ov.LastReadbackMs:0.0}ms comp={_latency * 1000f:0}ms eye={(tracked ? "tracked" : "default")}";
            }
            else _dbgLabel.text = "";

            // The Unity player window is hidden (it is only a render host), and the engine does not render cameras for a
            // window that is never presented. So the overlay camera is rendered explicitly, then handed to the layered window.
            if (_pendingRender)
            {
                _pendingRender = false;
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _ov.Color };
                if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
                else _cam.Render();
                _ov.Present(_dest, _clip.IsEmpty ? _mon.bounds : _clip);
            }
        }
        bool _pendingRender;

        double _nextDiag;
        void Diag()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextDiag || _mode == "Hidden") return;
            _nextDiag = now + 2.0;
            Log($"diag mode={_mode} draw={_drawSomething} cam={_cam.enabled} req={_ov.Requests} presented={_ov.FramesPresented} pending={_ov.Pending} rbErr={_ov.ReadbackErrors} empty={_ov.EmptyFrames} shown={_ov.Shown} dest={_dest} clip={_clip} eye={_eye} rb={_ov.LastReadbackMs:0.0}ms fps={_fps:0}");
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            // presentation is driven explicitly from LateUpdate (see there)
        }

        // ------------------------------------------------------------------ moving between displays inside the PC
        // This is its own state ("MonitorTransfer"), separate from the PC <-> MR portal: the partner walks off one display
        // and onto its Windows neighbour (side by side), or leaps / drops through the shared edge (stacked displays).
        MonitorDesc _xferTo, _xferFrom; int _xferPhase; bool _xferVertical; int _xferDir; float _xferT; float _riseV;

        static RectI Union(RectI a, RectI b)
        {
            int x = Mathf.Min(a.x, b.x), y = Mathf.Min(a.y, b.y);
            return new RectI(x, y, Mathf.Max(a.Right, b.Right) - x, Mathf.Max(a.Bottom, b.Bottom) - y);
        }

        void GoMonitor(string id)
        {
            if (_mon == null || string.IsNullOrEmpty(id) || _xferTo != null) return;
            var m = _mons.Find(x => x.id.EndsWith(id, StringComparison.OrdinalIgnoreCase));
            if (m == null || m.id == _mon.id) return;
            Unbind("moving to another display");
            var a = _mon.bounds; var b = m.bounds;
            _xferTo = m; _xferFrom = _mon; _xferPhase = 0; _xferT = 0;
            _xferVertical = b.x < a.Right && b.Right > a.x;
            if (_xferVertical)
            {
                _xferDir = (b.y + b.h * 0.5f) < (a.y + a.h * 0.5f) ? +1 : -1;      // +1: the other display is above
                // go through the shared edge where BOTH displays have pixels: walk to that column first if needed
                float margin = 0.8f * _H / Mpp;
                float lo = Mathf.Max(a.x, b.x) + margin, hi = Mathf.Min(a.Right, b.Right) - margin;
                LocalToPx(new Vector3(_motor.Pos.x, 0, 0), out float fpx, out float _);
                if (hi > lo && (fpx < lo || fpx > hi)) _motor.MoveTo(new Vector2(PxToLocal(Mathf.Clamp(fpx, lo, hi), 0).x, PlanBuilder.ResidentDepth(_H)), false, 180f);
            }
            else
            {
                _xferDir = (b.x + b.w * 0.5f) > (a.x + a.w * 0.5f) ? +1 : -1;
                _motor.MoveTo(new Vector2(_xferDir * (_wM * 0.5f + 0.8f * _H), PlanBuilder.ResidentDepth(_H)), true, null);
            }
            Log($"MonitorTransfer {_mon.id} -> {m.id} ({(_xferVertical ? (_xferDir > 0 ? "up through the top edge" : "down through the bottom edge") : (_xferDir > 0 ? "out the right edge" : "out the left edge"))})");
        }

        /// <summary>Advances a display-to-display move. Returns true while it drives the vertical position itself.</summary>
        bool StepMonitorTransfer(float dt)
        {
            if (_xferTo == null) return false;
            _mode = "MonitorTransfer";
            float depth = PlanBuilder.ResidentDepth(_H);
            if (!_xferVertical)
            {
                if (_xferPhase == 0 && _motor.Arrived)
                {
                    _mon = _xferTo; UpdateMonitorMetrics(); _floorYM = _feetYM = FloorYForDesktop(_mon);
                    _clip = _mon.bounds;                                       // from this frame on only the new display is drawn
                    float start = -_xferDir * (_wM * 0.5f + 0.8f * _H);        // out of sight beyond the edge it comes in through
                    _motor.Teleport(new Vector2(start, depth), _xferDir > 0 ? 90f : -90f);
                    _motor.MoveTo(new Vector2(-_xferDir * (_wM * 0.5f - 1.6f * _H), depth), false, 180f);
                    _xferPhase = 1;
                }
                else if (_xferPhase == 1 && _motor.Arrived) EndTransfer();
                return false;
            }
            // Stacked displays: the Windows desktop is continuous across the shared edge, so the body travels straight
            // through it in pixel space, and both displays are drawn while it straddles the edge.
            _clip = Union(_xferFrom.bounds, _xferTo.bounds);
            if (_xferPhase == 0)
            {
                if (!_motor.Arrived) return false;                 // still walking to the column both displays share
                _xferT += dt;
                PoseLib.Crouch(_pose, M.Smooth(_xferT / 0.3f));
                if (_xferT >= 0.3f) { _xferPhase = 1; _riseV = _xferDir > 0 ? 1.9f : -0.3f; }
                return true;
            }
            PoseLib.Hang(_pose);
            if (_xferPhase == 1)
            {
                if (_xferDir < 0) _riseV -= 4.5f * dt;
                _feetYM += _riseV * dt;
                bool crossed = _xferDir > 0 ? _feetYM > _hM * 0.5f : _feetYM < -_hM * 0.5f;
                if (crossed)
                {
                    // hand-over at the edge: the same pixel expressed in the other display's frame, so nothing moves on screen
                    Vector3 before = new Vector3(_motor.Pos.x, _feetYM, 0);
                    LocalToPx(before, out float px, out float py);
                    _mon = _xferTo; UpdateMonitorMetrics(); _floorYM = FloorYForDesktop(_mon);
                    Vector3 after = PxToLocal(px, py);
                    _motor.Teleport(new Vector2(after.x, depth), 180f);
                    _feetYM = after.y;
                    _eye += new Vector3(after.x - before.x, after.y - before.y, 0);
                    _xferPhase = 2;
                }
                return true;
            }
            if (_xferDir > 0)
            {
                if (_feetYM > _floorYM) _riseV -= 45f * dt;      // came up through the edge: a small hop above the floor
                if (_riseV > 0.05f) { _feetYM += _riseV * dt; return true; }
                EndTransfer();                                   // the ordinary fall-to-floor in Roam finishes the landing
                return false;
            }
            _riseV -= 4.5f * dt;                                 // came down through the top edge: keep falling to the floor
            _feetYM += _riseV * dt;
            if (_feetYM > _floorYM) return true;
            _feetYM = _floorYM;
            EndTransfer();
            return false;
        }
        void EndTransfer()
        {
            Log("MonitorTransfer finished on " + _mon.id);
            _xferTo = null; _xferFrom = null; _riseV = 0; _fallV = 0;
            _nextWander = Time.realtimeSinceStartupAsDouble + 10;
        }

        // ------------------------------------------------------------------ behaviours
        bool Roam(float dt, bool offline)
        {
            if (!UpdateWorkspace()) return false;
            if (!_initialised) { _initialised = true; _feetYM = _floorYM; }
            double now = Time.realtimeSinceStartupAsDouble;
            string activity = offline ? "idle" : _ch.activity;
            // emote follows REAL task state reported by the host (never a local timer pretending to work)
            _mixer.Voice = _voice.SpeakLevel;
            if (TalkLook.Active(_ch.talk) && !offline) _mixer.Set(TalkLook.Emote(_ch.talk));
            else if (activity == "analysis" || activity == "test") _mixer.Set(Emote.Think);
            else if (activity == "modify") _mixer.Set(Emote.Work);
            else if (activity == "error") _mixer.Set(Emote.Startle);
            else if (activity == "done") _mixer.Set(Emote.Happy);
            else _mixer.Set(Emote.Idle);

            if (activity == "idle" && _xferTo == null && _motor.Arrived && now >= _nextWander)
            {
                _nextWander = now + UnityEngine.Random.Range(14f, 30f);
                float margin = 0.6f * _H;
                float minX, maxX;
                if (_bound != IntPtr.Zero) { minX = PxToLocal(_boundRect.x, 0).x + margin; maxX = PxToLocal(_boundRect.Right, 0).x - margin; }
                else { minX = -_wM * 0.5f + margin; maxX = _wM * 0.5f - margin; }
                if (maxX > minX && _initialised && _fallV == 0)
                    _motor.MoveTo(new Vector2(Mathf.Clamp(_motor.Pos.x + UnityEngine.Random.Range(-4f, 4f) * _H, minX, maxX), PlanBuilder.ResidentDepth(_H)), false, 180f);
            }

            if (activity != _lastActivity)
            {
                if (activity == "done") _sparks.Burst(_rig.HeadTopWorld + new Vector3(0, 0.01f, -0.01f), Vector3.right, Vector3.up, _H * 0.34f, 11);
                _lastActivity = activity;
            }
            _mixer.Evaluate(Time.time, dt, _pose);
            _motor.Update(dt, _H, _pose);
            if (!offline) ShowPose(dt);      // a show asks something of its body: peering at a frame, hammering at its stall (PcShow.cs)
            if (!offline) DocsPose(dt);      // it holds up a file it carried in (PcDocs.cs)
            if (!offline) WorkPose(dt);      // it works at the queue panel (PcWork.cs)
            bool lifted = StepMonitorTransfer(dt);
            if (!lifted && !offline) lifted = OperateLift(dt);      // on a perch beside the spot it clicks at (PcShow.cs)
            if (_hopOut && !lifted && Mathf.Abs(_feetYM - _floorYM) <= 1e-4f)
            {
                // landed after the hop out of its window: a few steps along the taskbar line
                _hopOut = false;
                _sparks.Burst(new Vector3(_motor.Pos.x, _floorYM + FloorCorr(_floorYM, _motor.Pos.y) + 0.004f, _motor.Pos.y), Vector3.right, Vector3.up, _H * 0.30f, 6);
                if (!float.IsNaN(_walkOutX) && !_motor.HasTarget && !_opOn && _wkPcJob == null) _motor.MoveTo(new Vector2(_walkOutX, PlanBuilder.ResidentDepth(_H)), false, 180f);
                _walkOutX = float.NaN;
            }
            // floor changes (window resized / unbound) are followed by a short fall, never a pop
            if (lifted) { }
            else if (Mathf.Abs(_feetYM - _floorYM) > 1e-4f)
            {
                if (_feetYM > _floorYM) { _fallV += 3.5f * dt; _feetYM = Mathf.Max(_floorYM, _feetYM - _fallV * dt); if (_feetYM <= _floorYM) { _fallV = 0; } if (_fallV < 0f) PoseLib.Crouch(_pose, 0.35f); }
                else { _feetYM = Mathf.MoveTowards(_feetYM, _floorYM, 0.6f * dt); _fallV = 0; }
            }
            else _fallV = 0;
            float corr = FloorCorr(_feetYM, _motor.Pos.y);
            _rig.transform.SetPositionAndRotation(new Vector3(_motor.Pos.x, _feetYM + corr, _motor.Pos.y), Quaternion.Euler(0, _motor.Yaw, 0));
            _rig.Apply(_pose);
            if (!offline) ShowPlace(dt);     // the stall and what stands on it, beside the partner
            if (!offline) DocsPlace(dt);     // the file in its hands, and the pad that takes a click on it
            if (TalkLook.Active(_ch.talk) && !offline) _rig.SetGlow(TalkLook.Color(_ch.talk), 1f);
            else _rig.SetGlow(StatusColors.For(activity), StatusColors.Lit(activity));
            _rig.SetShadow(_feetYM > _floorYM - 0.1f * _H, new Vector3(_motor.Pos.x, _floorYM + corr, _motor.Pos.y), Vector3.up, _feetYM - _floorYM);   // no shadow while the body is below this floor (passing through a display edge)
            DrawStatus(activity, offline ? "" : TalkLook.Label(_ch.talk));
            DrawDebug(Vector3.zero, false);
            return true;
        }

        /// <summary>Small caption above the head, drawn on the glass plane where the head appears from the viewer's position.</summary>
        void DrawSay(bool visible)
        {
            bool on = visible && Time.time < _sayUntil && _sayLabel.text.Length > 0;
            if (_sayBg.enabled != on) { _sayBg.enabled = on; _sayLabelRenderer.enabled = on; }
            if (!on) return;
            Vector3 head = _rig.HeadTopWorld; float d = -_eye.z; float k = d / (d + Mathf.Max(0f, head.z));
            Vector3 p = new Vector3(_eye.x + (head.x - _eye.x) * k, _eye.y + (head.y - _eye.y) * k, 0.0008f);
            float w = Mathf.Clamp(_sayLabel.Width + 0.016f, 0.03f, 0.17f), h = _sayLabel.Height + 0.010f;      // measured text, not a character count
            Vector3 c = p + new Vector3(0, 0.012f + h * 0.5f, 0);
            _sayBg.transform.position = c; Ui.SizePanel(_sayBg, w, h, Mathf.Min(h * 0.45f, 0.012f), 0.0012f); Ui.SetFxPhase(_sayBg, Time.time);
            _sayLabel.transform.position = c + new Vector3(0, 0, -0.0004f);
        }

        void DrawStatus(string activity, string talkLabel)
        {
            // the conversation (listening / thinking) is shown in the same pill as the task state, and comes first
            string label = talkLabel.Length > 0 ? talkLabel : StatusColors.Label(activity);
            if (label.Length == 0) return;
            var c = talkLabel.Length > 0 ? TalkLook.Color(_ch.talk) : StatusColors.For(activity);
            var p = new Vector3(_motor.Pos.x + 0.55f * _H, _floorYM + 0.10f * _H, 0.002f);
            // a small gradient pill beside the feet: unobtrusive, but always the REAL task state
            Color.RGBToHSV(c, out float hue, out float sat, out float _);
            Color top = Color.HSVToRGB(hue, 0.26f, 1f), bottom = Color.HSVToRGB(hue, 0.34f, 0.96f);      // a soft pastel of the state colour
            _statusLabel.text = label; _statusLabel.color = Ui.Pal.Ink;
            float pw = _statusLabel.Width + 0.012f, ph = 0.0125f;
            Vector3 pp = p + new Vector3(pw * 0.5f, 0, 0);
            _statusDot.enabled = true; _statusDot.transform.position = pp; Ui.SizePanel(_statusDot, pw, ph, ph * 0.5f, 0.0008f);
            Ui.SetFxColors(_statusDot, top, bottom); Ui.SetFxPhase(_statusDot, Time.time * 1.5f);
            _statusLabel.anchor = TextAnchor.MiddleCenter;
            _statusLabel.transform.position = pp + new Vector3(0, 0, -0.0004f);
        }

        void DrawDebug(Vector3 portal, bool hasPortal)
        {
            if (!_settings.debugOverlay) return;
            _floorLine.enabled = true;
            _floorLine.transform.position = new Vector3(_rig.transform.position.x, _floorYM, 0.001f);
            _floorLine.transform.localScale = new Vector3(_H * 1.6f, 0.0012f, 1);
            if (hasPortal)
            {
                _dbgCross.enabled = true; _dbgCross.transform.position = portal + new Vector3(0, 0, 0.0005f);
                _dbgCross.transform.localScale = Vector3.one * PlanBuilder.ClearanceRadius(_H) * 2f;
            }
        }

        MonitorDesc PlanMonitor()
        {
            string id = _tr.plan != null && !string.IsNullOrEmpty(_tr.plan.monitorId) ? _tr.plan.monitorId : _tr.target?.monitorId;
            return ScreenMath.FindMonitor(_mons, id) ?? _mon;
        }

        void ShowRing(Vector3 portal, float pulse)
        {
            _portalFx.Show(portal + new Vector3(0, 0, 0.0002f), Quaternion.identity, PlanBuilder.ClearanceRadius(_H), pulse, _mode == "Crossing");
            _ring.transform.position = portal + new Vector3(0, 0, 0.0002f);
            // a thin ring the size of the body's passage: it marks the boundary, it does not cover the body
            _ring.transform.localScale = Vector3.one * PlanBuilder.ClearanceRadius(_H) * 2.0f * (1f + 0.04f * Mathf.Sin(Time.time * 6f));
            _drawSomething = true;
        }

        /// <summary>MR -> PC, before the crossing: bring the overlay up at the portal and only then report ReceiverReady.</summary>
        void PrepareReceive()
        {
            var tgt = _tr.target; if (tgt == null) return;
            var m = ScreenMath.FindMonitor(_mons, tgt.monitorId); if (m == null) return;
            if (m.id != _mon.id) { _mon = m; UpdateMonitorMetrics(); }
            Unbind("new transition");
            _clip = _mon.bounds; _mode = "Receiving";
            if (_ov.InsertAfter != Win32.HWND_TOPMOST) { _ov.InsertAfter = Win32.HWND_TOPMOST; _ov.ZOrderDirty = true; }
            Vector3 portal = PxToLocal(tgt.pxX, tgt.pxY);
            ShowRing(portal, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
            DrawDebug(portal, true);
            if (_prepTid != _tr.transitionId) { _prepTid = _tr.transitionId; _prepFrames = _ov.FramesPresented; _readySent = false; _holdSent = false; _approachSent = false; }

            IntPtr hwnd = new IntPtr(tgt.hwnd);
            bool windowTarget = tgt.anchorKind == "window" && hwnd != IntPtr.Zero;
            if (windowTarget && (!Win32.IsWindow(hwnd) || Win32.IsIconic(hwnd)))
            {
                // the workspace disappeared while the partner is on its way: hold at a safe point, do not improvise another screen
                if (!_holdSent) { _holdSent = true; _readySent = false; Send("hold", Win32.IsWindow(hwnd) ? "target window minimised" : "target window closed"); }
                return;
            }
            if (_holdSent) { _holdSent = false; Send("resume", "target window restored"); }
            if (_readySent && !_tr.receiverReady && Time.realtimeSinceStartup - _sentAt > 1.2f) _readySent = false;   // the host lost it (reconnect): confirm again
            if (!_readySent && _ov.Shown && _ov.FramesPresented >= _prepFrames + 2)
            {
                _sentAt = Time.realtimeSinceStartup;
                float floorPx = _mon.work.Bottom;
                if (windowTarget && Win32.FrameBounds(hwnd, out RectI r)) floorPx = Mathf.Min(r.Bottom - 4f, _mon.bounds.Bottom);
                _readySent = true;
                // report the floor where the partner will LOOK grounded from the viewer's position (see FloorCorr)
                float fyM = PxToLocal(0, floorPx).y; fyM += FloorCorr(fyM, PlanBuilder.ResidentDepth(_H));
                LocalToPx(new Vector3(0, fyM, 0), out float _, out floorPx);
                _link.Send(Msg.TransitionEvent, new TransitionEventMsg { transitionId = _tr.transitionId, kind = "receiverReady", from = Roles.Pc, tau = floorPx, progress = tgt.pxX });
                Log($"receiverReady sent tid={_tr.transitionId} floorPx={floorPx} frames={_ov.FramesPresented}");
            }
        }

        /// <summary>PC -> MR, before the crossing: actually walk along the floor to the launch point under the exit.</summary>
        bool ApproachExit(float dt)
        {
            var tgt = _tr.target; if (tgt == null) return Roam(dt, false);
            if (_prepTid != _tr.transitionId) { _prepTid = _tr.transitionId; _approachSent = false; _readySent = false; _holdSent = false; }
            var m = ScreenMath.FindMonitor(_mons, tgt.monitorId);
            if (m != null && m.id != _mon.id) { /* exit on another display: host only offers exits on the current one */ }
            float targetX = PxToLocal(tgt.pxX, 0).x;
            if (_bound != IntPtr.Zero && (tgt.pxX < _boundRect.x + 0.3f * _H / Mpp || tgt.pxX > _boundRect.Right - 0.3f * _H / Mpp)) Unbind("exit is outside the workspace window");
            if (!_approachSent && (!_motor.HasTarget || Mathf.Abs(_motor.Pos.x - targetX) > 0.002f) && Mathf.Abs(_motor.Pos.x - targetX) > 0.002f)
                _motor.MoveTo(new Vector2(targetX, PlanBuilder.ResidentDepth(_H)), Mathf.Abs(_motor.Pos.x - targetX) > 5f * _H, 180f);
            bool ok = Roam(dt, false);
            _nextWander = Time.realtimeSinceStartupAsDouble + 20;
            Vector3 portal = PxToLocal(tgt.pxX, tgt.pxY);
            ShowRing(portal, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
            DrawDebug(portal, true);
            if (!ok)
            {
                if (!_holdSent) { _holdSent = true; Send("hold", "workspace window minimised"); }
                return false;
            }
            if (_holdSent) { _holdSent = false; Send("resume", "workspace window restored"); }
            if (_approachSent && !_tr.approachDone && Time.realtimeSinceStartup - _sentAt > 1.2f) _approachSent = false;
            if (!_approachSent && _motor.Arrived && Mathf.Abs(_motor.Pos.x - targetX) < 0.003f && Mathf.Abs(_feetYM - _floorYM) < 1e-4f)
            {
                _sentAt = Time.realtimeSinceStartup;
                _approachSent = true;
                LocalToPx(new Vector3(_motor.Pos.x, _feetYM + FloorCorr(_feetYM, _motor.Pos.y), 0), out float fx, out float fy);
                _link.Send(Msg.TransitionEvent, new TransitionEventMsg { transitionId = _tr.transitionId, kind = "approachDone", from = Roles.Pc, tau = fy, progress = fx });
                Log($"approachDone sent tid={_tr.transitionId} feetPx=({fx:0},{fy:0})");
            }
            return true;
        }

        float _sentAt;
        void Send(string kind, string reason) =>
            _link.Send(Msg.TransitionEvent, new TransitionEventMsg { transitionId = _tr.transitionId, kind = kind, from = Roles.Pc, reason = reason });

        /// <summary>During the crossing both renderers evaluate the same plan on the shared clock; this side keeps z &gt; 0.</summary>
        bool Crossing(double now)
        {
            var plan = _tr.plan; var clock = _tr.clock;
            var m = PlanMonitor();
            if (m != null && m.id != _mon.id) { _mon = m; UpdateMonitorMetrics(); }
            _mode = "Crossing"; _clip = _mon.bounds;
            if (_ov.InsertAfter != Win32.HWND_TOPMOST) { _ov.InsertAfter = Win32.HWND_TOPMOST; _ov.ZOrderDirty = true; }
            float tau = clock != null ? clock.TauAt(_link.HostNow, plan.Total) : 0f;
            if (TransitionMachine.Parse(_tr.state) == TState.Settling && clock == null) tau = plan.Total;
            // Pixels rendered now reach the desktop one read-back later. The plan is deterministic, so this side draws the pose
            // the body will have at that moment (measured read-back + about half a compositor frame), keeping the halves in step.
            _latency = Mathf.Lerp(_latency, Mathf.Clamp(_ov.LastReadbackMs, 0f, 80f) / 1000f + 0.008f, 0.1f);
            float tauDraw = clock != null ? clock.TauAt(_link.HostNow + _latency, plan.Total) : tau;
            PlanEvaluator.Evaluate(plan, tauDraw, _cs);
            Vector3 portal = PxToLocal(plan.portalPxX, plan.portalPxY);
            // the plan is already in the display frame, which is this app's world: no further transform
            Vector3 pos = portal + Conv.U(_cs.RootPos);
            Quaternion rot = Conv.U(_cs.RootRot);
            _rig.transform.SetPositionAndRotation(pos, rot);
            _rig.Apply(_cs.Pose);
            _rig.SetClip(Vector3.zero, Vector3.forward, +1f);       // PC side = inside the screen (z > 0)
            _rig.SetGlow(StatusColors.For("idle"), 0f);
            float floorY = portal.y + plan.pcFoot.y;
            _rig.SetShadow(true, new Vector3(pos.x, floorY, portal.z + PlanBuilder.ResidentDepth(_H)), Vector3.up, Mathf.Max(0, pos.y - floorY));
            _crossTau = tau; _crossProgress = PlanEvaluator.CrossingProgress(plan, _cs); _crossPhase = _cs.Phase.ToString();
            if (_cs.Phase != _lastPhase)
            {
                Vector3 feet = new Vector3(pos.x, floorY + 0.004f, PlanBuilder.ResidentDepth(_H));
                if (_cs.Phase == CrossPhase.Land && plan.direction == 0) _sparks.Burst(feet, Vector3.right, Vector3.up, _H * 0.42f);
                if (_cs.Phase == CrossPhase.FlightIn && plan.direction == 1) _sparks.Burst(feet, Vector3.right, Vector3.up, _H * 0.34f, 7);
                _lastPhase = _cs.Phase;
            }
            if (_cs.Phase == CrossPhase.FlightIn || _cs.Phase == CrossPhase.Thread || _cs.Phase == CrossPhase.FlightOut)
            {
                _trailT -= Time.unscaledDeltaTime;
                Vector3 chest = _rig.ChestWorld;
                if (_trailT <= 0f && chest.z > 0.012f) { _trailT = 0.05f; _sparks.Trail(chest - Conv.U(M.Rot(_cs.RootRot, new SN.Vector3(0, 0.25f * _H, 0))), _H * 0.42f); }
            }
            float pulse = 1f - Mathf.Abs(_crossProgress * 2f - 1f);
            float after = _crossProgress >= 0.999f ? Mathf.Clamp01(1f - (tau - plan.ThreadEnd) / 0.45f) : 1f;   // gone shortly after the last foot
            if (after > 0.01f) ShowRing(portal, Mathf.Clamp01(0.25f + pulse) * after);
            _floorYM = _feetYM = floorY - FloorCorr(floorY, PlanBuilder.ResidentDepth(_H));
            DrawDebug(portal, true);
            if (now >= _nextBeat)
            {
                _nextBeat = now + 0.1;
                _link.Send(Msg.TransitionEvent, new TransitionEventMsg
                {
                    transitionId = _tr.transitionId, kind = "renderBeat", from = Roles.Pc, tau = tau, progress = _crossProgress,
                    visibleFraction = _ov.Shown ? 1f : 0f,
                });
            }
            return true;
        }

        /// <summary>After MR -> PC completes: continue from exactly where the plan ended (no pop), then bind the workspace.</summary>
        void LandFromPlan()
        {
            _landedTid = _tr.transitionId;
            var plan = _tr.plan;
            var m = PlanMonitor();
            if (m != null && m.id != _mon.id) { _mon = m; UpdateMonitorMetrics(); }
            Vector3 portal = PxToLocal(plan.portalPxX, plan.portalPxY);
            Vector3 foot = portal + Conv.U(plan.pcFoot);
            _motor.Teleport(new Vector2(foot.x, foot.z), 180f);
            _floorYM = _feetYM = foot.y - FloorCorr(foot.y, foot.z); _fallV = 0; _initialised = true;
            _nextWander = Time.realtimeSinceStartupAsDouble + 12;
            var tgt = _tr.target;
            if (tgt != null && tgt.anchorKind == "window" && tgt.hwnd != 0) BindTo(new IntPtr(tgt.hwnd));
            Log($"landed in PC at px=({plan.portalPxX:0},{plan.portalPxY:0}) monitor={_mon.id} bound=0x{_bound.ToInt64():X}");
        }

        // ------------------------------------------------------------------ calibration markers
        IntPtr[] _cornerWins;
        string _cornerMon = "";

        /// <summary>
        /// Shows four small native marker windows exactly at the lit pixel corners of the display being calibrated, so the
        /// user touches real pixels (not the bezel). Plain topmost popup windows: no rendering buffer of display size needed.
        /// </summary>
        void DrawCalibrationMarkers()
        {
            var m = ScreenMath.FindMonitor(_mons, _calibratingMonitor);
            if (m == null) { ClearCalibrationMarkers(); return; }
            if (_cornerWins != null && _cornerMon == m.id) return;
            ClearCalibrationMarkers();
            _cornerMon = m.id;
            int s = Mathf.Max(18, Mathf.RoundToInt(0.008f / (m.physWidthM > 0.01f ? m.physWidthM / m.bounds.w : ScreenMath.DefaultMetersPerPixel)));
            var pts = new[] { new Vector2Int(m.bounds.x, m.bounds.y), new Vector2Int(m.bounds.Right - s, m.bounds.y), new Vector2Int(m.bounds.x, m.bounds.Bottom - s), new Vector2Int(m.bounds.Right - s, m.bounds.Bottom - s) };
            _cornerWins = new IntPtr[4];
            const uint SS_WHITERECT = 0x6;
            for (int i = 0; i < 4; i++)
            {
                _cornerWins[i] = Win32.CreateWindowEx(Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE, "Static", "LivingPartnerCalib",
                    Win32.WS_POPUP | SS_WHITERECT, pts[i].x + LayeredOverlay.ParkX, pts[i].y, s, s, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                Win32.SetWindowPos(_cornerWins[i], Win32.HWND_TOPMOST, pts[i].x + LayeredOverlay.ParkX, pts[i].y, s, s, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
            }
            Log($"calibration markers shown on {m.id} bounds={m.bounds} marker={s}px");
        }

        void ClearCalibrationMarkers()
        {
            if (_cornerWins == null) return;
            foreach (var h in _cornerWins) if (h != IntPtr.Zero) Win32.DestroyWindow(h);
            _cornerWins = null; _cornerMon = "";
        }
    }
}
