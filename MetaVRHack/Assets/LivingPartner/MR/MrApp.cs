using System;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using Pose = LivingPartner.Shared.Pose;
using CharacterInfo = LivingPartner.Shared.CharacterInfo;
using SN = System.Numerics;

namespace LivingPartner.MR
{
    /// <summary>
    /// The MR side of the partner (Quest, or the desktop simulator through the same code path). Hands-first: pointing,
    /// pinch confirmation and poke buttons drive everything; no controller is needed and voice is optional.
    /// Lives on the MrCore prefab: the partner, the portal, the pointer, every panel and the pools for monitors, timers
    /// and proposal cards are children of it in the hierarchy and are referenced through the serialized fields below.
    /// Nothing is created or looked up by name at runtime.
    /// </summary>
    public sealed partial class MrApp : MonoBehaviour
    {
        [Header("Character and portal")]
        [SerializeField] PartnerRig _rig;
        [SerializeField] PortalFx _portalFx;
        [SerializeField] SparkFx _sparks;
        [SerializeField] FxQuad _ring;
        [Header("Pointing")]
        [SerializeField] FxQuad _ptrDot;
        [SerializeField] FxQuad _marker;
        [SerializeField] StoryLabel _markerLabel;
        [Header("Captions")]
        [SerializeField] FxQuad _captionBg;
        [SerializeField] StoryLabel _caption;
        [SerializeField] StoryLabel _dbg;
        [Header("Calibrated displays (pool)")]
        [SerializeField] MonView[] _monitorPool;

        /// <summary>Hand / head input of the platform (Quest adapter or simulator). Assigned by that adapter in Awake.</summary>
        [NonSerialized] public IMrInput Input;
        [NonSerialized] public Camera Cam;
        public Func<List<string>> UrlProvider;
        public string Role = Roles.Quest;
        public Func<string> LoadToken = () => "";
        public Action<string> SaveToken = _ => { };
        public HostLink Link => _link;
        public const int Layer = 0;
        /// <summary>Render-to-photon latency compensated during a crossing. 0 for the simulator; a starting estimate on Quest, to be tuned by measurement.</summary>
        public float DisplayLatencyMs = 0f;
        public Vector3 PartnerPos => _pos;
        public Vector3 PartnerHeadTop => _rig != null ? _rig.HeadTopWorld : _pos;
        public float DeskY => _deskY;
        public IEnumerable<MonView> Monitors => _mons.Values;

        HostLink _link;
        readonly PoseMixer _mixer = new PoseMixer();
        readonly PartnerMotor _motor = new PartnerMotor();
        readonly Pose _pose = new Pose();
        readonly CharState _cs = new CharState();
        float _H = 0.10f;

        StateSnapshot _snap;
        TransitionInfo _tr = new TransitionInfo();
        CharacterInfo _ch = new CharacterInfo();
        SettingsMsg _settings = new SettingsMsg();
        readonly Dictionary<string, MonView> _mons = new Dictionary<string, MonView>();
        float _deskY; bool _hasDesk; bool _staleCalibWarned; int _lastMonCount = -1; bool _lastHasDesk;
        Vector3 _home; bool _homeSet;

        enum Mode { Free, OnHand, Hop, InPc, Held }      // Held: picked up by a pinch and hanging from the hand (MrHold)
        Mode _mode = Mode.Free;
        bool _hopToHand; int _hopHand;
        Vector3 _hopFrom, _hopTo; float _hopT, _hopDur, _hopApex; Func<Vector3> _hopTarget; Action _hopDone; Quaternion _hopRotFrom, _hopRotTo;
        Vector3 _pos; Quaternion _rot = Quaternion.identity;
        int _rideHand = -1; float _handLostT, _handSteadyT, _restT; Vector3 _lastPalm, _palmVel, _ridePos; Quaternion _rideRot = Quaternion.identity;

        string _prepTid = ""; bool _approachSent, _readySent, _holdSent; float _approachT; int _approachRev = -1; string _landedTid = "";
        double _nextBeat, _nextStatus, _nextHead, _nextPointer;
        float _crossTau, _crossProgress; string _crossPhase = "";
        float _fps = 72f;

        // pointing / selection
        struct Ptr { public bool valid; public string mon; public float u, v; public int hand; public Vector3 world; public Vector3 from; public int kind; }      // from: where the ray starts; kind: HandState.rayKind
        Ptr _ptr; Vector2 _ptrFiltered; string _ptrMon = "";
        sealed class Selection { public string queryId, mon; public float u, v; public int direction; public TargetInfo result; public float time; public bool auto, analyze; public string prompt = "", arrival = "analyzeOnArrival"; }
        Selection _sel;
        float _captionUntil;

        void Start()
        {
            Application.runInBackground = true;
            _H = _rig.Height;
            _ptrDot.enabled = _marker.enabled = _ring.enabled = _captionBg.enabled = false;
            _markerLabel.text = _caption.text = _dbg.text = "";
            foreach (var mv in _monitorPool) mv.gameObject.SetActive(false);

            _link = new HostLink { Role = Role, DeviceName = SystemInfo.deviceName, Token = LoadToken() };
            _link.ClientId = Role + "-" + SystemInfo.deviceUniqueIdentifier;
            _link.UrlProvider = UrlProvider;
            _link.OnMessage += OnMessage;
            _link.OnSnapshot += OnSnapshot;
            _link.OnTokenIssued += t => SaveToken(t);
            _link.OnConnectionChanged += c => { Debug.Log("[MR] host connection: " + c); _tn = new TennisState(); _tnLocal = null; if (!c) OnHostLost(); };
            if (!LocalAtStart) _link.Start();      // (LocalAtStart: no PC is looked for at all - MrLocalHost)
            InitUi();
            InitVoice();
            InitSound();
            InitRecords();      // the record picker's buttons (MrRecords)
            InitRoomScripts();  // the buttons of the strip over a scripted thing (MrRoomScript)
            InitDocs();         // the boards of the documents the partner makes (MrDocs)
            InitLocal();        // playing without the PC: the card with "Play now", the first steps (MrLocalHost)
        }

        void OnDestroy() { EndGomoku(true); StopTvRally(); _link?.Dispose(); }

        void OnHostLost()
        {
            // Without the host we cannot know whether the PC is drawing. A crossing in flight is rewound locally to the
            // source side (the same rule the host applies), anything else simply waits for the snapshot on reconnect.
            var st = TransitionMachine.Parse(_tr.state);
            if (st == TState.Crossing && _tr.clock != null && _tr.plan != null)
            {
                float tau = _tr.clock.TauAt(_link.HostNow, _tr.plan.Total);
                _tr.clock = new CrossClock { transitionId = _tr.transitionId, anchorHost = _link.HostNow, anchorTau = tau, rate = -1.25f, revision = _tr.clock.revision + 1000 };
                _tr.state = nameof(TState.Recovering);
                Debug.Log("[MR] host lost during crossing: rewinding locally");
            }
        }

        // ------------------------------------------------------------------ host messages
        void OnSnapshot(StateSnapshot s)
        {
            s.world?.games?.RemoveAll(g => g == null || StarterGames.Retired(g.id));
            _snap = s;

            if (s.transition != null && (s.transition.transitionId != _tr.transitionId || s.transition.revision >= _tr.revision)) _tr = s.transition;
            _ch = s.character ?? _ch; _settings = s.settings ?? _settings;
            _rig.SetDesign(_ch.design);
            SyncMonitors(s);
            OnTennisState(s.tennis);
            OnSnapshotUi(s);
        }

        void OnMessage(Envelope env)
        {
            switch (env.type)
            {
                case Msg.TransitionUpdate:
                {
                    var t = env.Body<TransitionInfo>();
                    if (t == null) break;
                    if (t.transitionId.StartsWith("rejected-")) { Caption("I can't go there: " + t.reason, 4f); Sfx2D("error", 0.5f); _sel = null; break; }
                    if (t.transitionId != _tr.transitionId || t.revision >= _tr.revision) _tr = t;
                    break;
                }
                case Msg.TargetResult: { var t = env.Body<TargetInfo>(); if (t != null) OnTargetResult(t); break; }
                case Msg.Say:
                {
                    var s = env.Body<SayMsg>();
                    if (s != null)
                    {
                        _callCaptionUntil = 0f;
                        Caption(s.text, s.seconds);
                        if (s.basis == "conversation: name") _callCaptionUntil = _captionUntil;
                    }
                    break;
                }
                case Msg.VoiceReply: { var v = env.Body<VoiceReply>(); if (v != null) OnVoiceReply(v); break; }
                case Msg.GameState: { var g = env.Body<TennisState>(); if (g != null) OnTennisState(g); break; }
                case "gomoku.state": OnGomokuState(env.Body<GomokuState>()); break;
                case Msg.GlassState: OnGlassState(env.Body<GlassState>()); break;          // which glass game is on, on which side (MrGlass)
                case Msg.GlassCommand: OnGlassCommand(env.Body<GlassCommand>()); break;    // a card chosen for the game that runs out here
                case Msg.SwarmState: OnSwarmAck(env.Body<SwarmGlass>()); break;            // the PC client draws what is behind the glass (MrSwarm)
                case Msg.GlassOut: OnGlassOut(env.Body<GlassOut>()); break;                // a game inside the PC sent something out of the screen: it flies through the room (MrGlassOut)
                case Msg.IntentResult: { var r = env.body?.ToObject<IntentView>(); if (r != null) OnIntent(r); break; }
                case Msg.ModuleUpdate: case Msg.TaskUpdate: break; // the snapshot that follows carries the state
                case Msg.Debug: { var d = env.Body<DebugCommand>(); if (d != null) OnDebug(d); break; }
                case Msg.EyeRequest: OnEyeRequest(env.Body<EyeRequest>()); break;          // "이거 만들어 줘": ONE picture of what the user holds up (MrEyes)
            }
        }

        sealed class IntentView { public string text = "", kind = ""; public long durationMs; public bool alsoStart, needsPointer; public PointerSample pointer; }

        /// <summary>The display nearest to the partner (null: none is known).</summary>
        MonView NearestMon()
        {
            MonView best = null; float bd = float.MaxValue;
            foreach (var m in _mons.Values) { float d = (m.Origin - _pos).sqrMagnitude; if (d < bd) { bd = d; best = m; } }
            return best;
        }

        void OnIntent(IntentView r)
        {
            switch (r.kind)
            {
                case "enterPortal":
                    // Where it goes in: the spot the host worked out (pointed at on purpose, or looked at, round the
                    // sentence - EnterRules); with none, the screen being looked at NOW, before a hand ray that may
                    // merely brush a display (a resting hand sent it in "at a very strange place").
                    if (r.pointer != null) Select(r.pointer.monitorId, r.pointer.u, r.pointer.v, 0, true, r.alsoStart, r.text);
                    else if (GazeTarget(out string gm, out float gu, out float gv)) Select(gm, gu, gv, 0, true, r.alsoStart, r.text);
                    else if (_ptr.valid) Select(_ptr.mon, _ptr.u, _ptr.v, 0, true, r.alsoStart, r.text);
                    else if (NearestMon() is MonView near) Select(near.desc.id, 0.5f, 0.55f, 0, true, r.alsoStart, r.text);                // nothing pointed or looked at: the middle of the display nearest to the partner
                    else Caption(_mons.Count == 0 ? "I don't know where your screens are yet. Say \"calibrate\" and show me the corners." : "Look at the screen (or point at it) and tell me again.", 5f);
                    break;
                case "lookScreen": case "analyze": case "readFile": case "revise":
                {
                    // Which screen: the one pointed at while speaking, else the one the hand points at now, else the one being
                    // looked at. (None of them: the host takes the window the user is working in.)
                    MonView target = null; float tu = 0.5f, tv = 0.5f;
                    if (r.pointer != null && Mon(r.pointer.monitorId) is MonView pm) { target = pm; tu = r.pointer.u; tv = r.pointer.v; }
                    else if (_ptr.valid && Mon(_ptr.mon) is MonView hm) { target = hm; tu = _ptr.u; tv = _ptr.v; }
                    else if (GazeTarget(out string lookMon, out float lookU, out float lookV) && Mon(lookMon) is MonView gmv) { target = gmv; tu = lookU; tv = lookV; }
                    if (r.kind == "revise" && _ch.owner == Env.Mr)
                    {
                        // asked to make the new version itself: THAT is done inside the screen, so it goes in (and only then)
                        if (target == null) { float bd = float.MaxValue; foreach (var m in _mons.Values) { float d = (m.Origin - Input.HeadPos).sqrMagnitude; if (d < bd) { bd = d; target = m; } } }
                        if (target == null) { Caption("I don't know where your screens are yet. Say \"find my screens\".", 4f); break; }
                        Select(target.desc.id, tu, tv, 0, true, true, r.text);
                        if (_sel != null) _sel.arrival = "reviseOnArrival";
                        break;
                    }
                    // Looking, feedback, reading: from where it is. It turns to the screen; it does not go anywhere.
                    if (target != null && _ch.owner == Env.Mr && _mode == Mode.Free && !_motor.HasTarget) _motor.Face(YawTo(target.ToWorld(tu, tv) - _pos));
                    if (_ch.owner == Env.Mr) { _emoteForced = Emote.Think; _emoteT = 2.5f; }
                    // LOOKING ("what is on this screen?", "which folders are here?") is no longer decided here. The host
                    // works out the display and the spot from the pointer and gaze samples it is sent all the time (it
                    // has them for the moment the words were spoken), captures that display and reads round that spot;
                    // the spot comes back in r.pointer, only for the partner to turn to. The headset used to answer with
                    // the pixel the hand or the head happened to be on when the result arrived, seconds after the
                    // words: "a different display, a completely different part".
                    if (r.kind == "lookScreen") break;
                    // feedback on a window, reading a file, a new version while it is inside the PC: asked for from here, as before
                    var cmd = new TaskCommand { cmd = r.kind == "readFile" ? "read" : r.kind == "revise" ? "revise" : "analyze", prompt = r.text };
                    if (target != null) ScreenMath.UVToPixel(target.desc, tu, tv, out cmd.pxX, out cmd.pxY);
                    _link.Send(Msg.TaskCommand, cmd);
                    break;
                }
                case "playGame":
                    if (r.text == StarterGames.GomokuId) RequestGomoku(r.pointer?.monitorId);
                    else StartGame(r.text, "being asked");
                    break;
                case "stopTvRally": StopTvRally(); break;
                case "act": RoomAct(r.text, "being asked"); break;      // "<item id>|<act>": something a thing's script offers (MrRoomScript)
                case "glassGame": break;      // a glass game asked for by voice: the PC has started it itself (glass.state says where it is played) - nothing to start here
                case "comeHere":
                {
                    // called over: out of the screen if it is in there, otherwise to the desk right in front of the user
                    if (_ch.owner == Env.Pc) { RequestReturnDefault(); break; }
                    Vector3 head = Input.HeadPos, fwd = Input.HeadRot * Vector3.forward; fwd.y = 0;
                    fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
                    Vector3 spot = head + fwd * 0.33f; spot.y = _deskY;
                    if (_homeSet) _worldShift += _home - spot;      // (the things stay where they are)
                    _home = spot; _homeSet = true;
                    if (_mode == Mode.Free) _motor.MoveTo(new Vector2(spot.x, spot.z), true, YawTo(head - spot));
                    RoomStopAll("it was called over");
                    _emoteForced = Emote.Happy; _emoteT = 0.8f;
                    break;
                }
                case "stopTennis": StopRps("being asked"); RoomStopAll("being asked"); break;
                case "returnToMr": RequestReturnDefault(); break;
                case "playTennis": StartTennis("the user asking for it"); break;
                case "calibrate": BeginCalibration(); break;
                case "openMenu": MenuByVoice(true); break;       // "밤비야, 가방 열어 줘": the fan opens in front of the user (MrMenuBoard)
                case "closeMenu": MenuByVoice(false); break;
            }
        }

        void OnDebug(DebugCommand d)
        {

            if (d.cmd == "tvRallyStop") { StopTvRally(); return; }
            if (d.cmd == "enter") SelectPixel(d.px, d.py, 0, d.text == "analyze");
            else if (d.cmd == "return") { if (d.px == 0 && d.py == 0) RequestReturnDefault(); else SelectPixel(d.px, d.py, 1, false); }
            else if (d.cmd == "mrHome") { _homeSet = false; }
            else if (d.cmd == "mrLatency") { DisplayLatencyMs = Mathf.Clamp(d.value, 0f, 120f); Debug.Log("[MR] display latency compensation = " + DisplayLatencyMs + " ms"); }
            else if (OnDebugVoice(d)) { }
            else if (OnDebugRoom(d)) { }      // poke, records, show, pointerLog, volumeFake (MrPoke)
            else if (OnDebugRoomScript(d)) { }      // roomScript, roomAct, roomKinds, roomButton (MrRoomScript)
            else if (OnDebugTennis(d)) { }
            else if (OnDebugSwarm(d)) { }     // swarmStart, swarmStop, swarmBot, swarmPick, swarmTime, swarmSkip, swarmInfo ... (MrSwarm)
            else if (OnDebugGlass(d)) { }     // glassBot, glassXp, glassChoose, glassLives, glassPalm, glassInfo (MrGlass)
            else if (OnDebugDocs(d)) { }      // docFake, docPage, docPick, docSend, docClose (MrDocs)
            else if (OnDebugLocal(d)) { }     // localPick, localMode, localSteps, localDesk, localInfo, localSave (MrLocalHost)
            else if (OnDebugWork(d)) { }      // workFake (MrWork), workStrip (MrWorkStrip)
            else if (OnDebugEyes(d)) { }      // eyeLook, eyeCut (MrEyes)
            else OnDebugUi(d);
        }

        // ------------------------------------------------------------------ monitors
        void SyncMonitors(StateSnapshot s)
        {
            bool sim = Input != null && Input.Simulated;
            var seen = new HashSet<string>();
            foreach (var c in s.calibrations)
            {
                if (c.simulated != sim) continue;                       // never mix simulator calibrations with real ones
                if (!sim && Input != null && c.spaceEpoch != Input.SpaceEpoch)
                {
                    // the tracking space was reset since this calibration: do not trust it, ask for a re-calibration instead
                    if (!_staleCalibWarned) { _staleCalibWarned = true; Caption("The room changed - I'll find your screens again.", 3f); }
                    continue;
                }
                var desc = ScreenMath.FindMonitor(s.monitors, c.monitorId);
                if (desc == null || desc.bounds.w != c.pixelW || desc.bounds.h != c.pixelH) continue;
                if (!_mons.TryGetValue(c.monitorId, out var mv))
                {
                    mv = FreeMonitorView();
                    if (mv == null)
                    {
                        if (!_poolWarned) { _poolWarned = true; Debug.LogWarning($"[MR] more than {_monitorPool.Length} calibrated displays: the extra ones are ignored"); }
                        continue;
                    }
                    mv.gameObject.SetActive(true);
                    _mons[c.monitorId] = mv;
                }
                seen.Add(c.monitorId);
                mv.desc = desc; mv.calib = c; mv.frame = MonitorFrame.FromCalibration(c);
                mv.transform.SetPositionAndRotation(mv.Origin, Quaternion.LookRotation(mv.Z, mv.Y));
                mv.outline.transform.localScale = new Vector3(mv.frame.widthM, mv.frame.heightM, 1);
                // on a headset the desk is sensed (and sensed again below the screens once they are known): a height stored
                // with a calibration was once a monitor lying back, and it switched the sensing off for good
                if (c.hasDesk && Input.Simulated) { _deskY = c.deskY; _hasDesk = true; }
            }
            var gone = new List<string>();
            foreach (var k in _mons.Keys) if (!seen.Contains(k)) gone.Add(k);
            foreach (var k in gone) { _mons[k].gameObject.SetActive(false); _mons.Remove(k); }
            // the resting spot depends on where the screens and the desk are: place it again once they are known
            if (_mons.Count != _lastMonCount || _hasDesk != _lastHasDesk)
            {
                _lastMonCount = _mons.Count; _lastHasDesk = _hasDesk;
                bool idle = TransitionMachine.Parse(_tr.state) is TState ts && (ts == TState.Idle || ts == TState.Complete || ts == TState.Cancelled);
                if (_mode == Mode.Free && idle && !_motor.HasTarget) _homeSet = false;
            }
            if (!_hasDesk && !_deskSensed && _mons.Count > 0)
            {
                // no measured desk: use a conservative estimate below the lowest screen and say that it is an estimate
                float low = float.MaxValue;
                foreach (var m in _mons.Values) low = Mathf.Min(low, m.Origin.y - m.frame.heightM * 0.5f);
                _deskY = low - 0.09f;
            }
        }

        MonView Mon(string id) => id != null && _mons.TryGetValue(id, out var m) ? m : null;

        bool _poolWarned;
        MonView FreeMonitorView()
        {
            for (int i = 0; i < _monitorPool.Length; i++) if (!_monitorPool[i].gameObject.activeSelf) return _monitorPool[i];
            return null;
        }

        /// <summary>The desk point in front of a portal where the partner launches from / lands on (a measured surface, not a guess).</summary>
        Vector3 StandPoint(MonView mv, Vector3 portal)
        {
            mv.GAxes(out var gx, out var gy, out var gz);
            float dist = PlanBuilder.LaunchDistance(_H, portal.y - _deskY);
            Vector3 p = portal - gz * dist; p.y = _deskY;
            return p;
        }

        /// <summary>A world point relative to the portal, expressed in the display frame (the frame the crossing plan uses).</summary>
        static V3 PortalLocal(MonView mv, Vector3 portal, Vector3 world) => mv.frame.DirToLocal(Conv.N(world - portal));

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (Input == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(dt, 1e-4f), 0.05f);
            Input.Tick(dt);
            LocalTick();      // without the PC: the stand-in's snapshots are due before the link hands them out (MrLocalHost)
            _link.Pump();
            double now = Time.realtimeSinceStartupAsDouble;
            bool hostOk = _link.Connected && _snap != null;
            UpdateGomoku(hostOk);
            _ring.enabled = false; _portalFx.Begin();
            _rig.ClearClip();
            bool show = false;
            UpdateRoom(dt, hostOk);

            if (!hostOk)
            {
                // offline: if a crossing was in flight keep evaluating the local rewind, otherwise stay as we were
                var stOff = TransitionMachine.Parse(_tr.state);
                if (stOff == TState.Recovering && _tr.plan != null && _tr.clock != null)
                {
                    show = Crossing(now);
                    if (_tr.clock.TauAt(_link.HostNow, _tr.plan.Total) <= 0.001f) { _tr.state = nameof(TState.Cancelled); _tr.owner = _tr.source; }
                }
                else if (_tr.owner == Env.Mr) show = Free(dt);
            }
            else
            {
                var st = TransitionMachine.Parse(_tr.state);
                TransitionMachine.Visibility(_tr, out bool mrDraws, out bool _, out bool clipped);
                bool pre = st == TState.TargetSelected || st == TState.Approaching || st == TState.ReceiverReady ||
                           (st == TState.Hold && TransitionMachine.Parse(_tr.heldState) != TState.Crossing);
                if (clipped && _tr.plan != null) show = Crossing(now);
                else if (pre && _tr.direction == 0) show = Approach(dt);
                else if (pre && _tr.direction == 1) { ReceiverPrepare(); show = false; _mode = Mode.InPc; }
                else if (mrDraws)
                {
                    if (st == TState.Complete && _tr.direction == 1 && _tr.transitionId != _landedTid && _tr.plan != null) LandFromPlan();
                    if (_mode == Mode.InPc) _mode = Mode.Free;
                    show = Free(dt);
                }
                else _mode = Mode.InPc;
            }

            if (hostOk && _pendingExit.HasValue)
            {
                var pe = _pendingExit.Value;
                if (_snap.pc != null && _snap.pc.monitorId == pe.mon && _snap.pc.mode == "DesktopRoaming") { _pendingExit = null; Select(pe.mon, pe.u, pe.v, 1, true, false, ""); }
                else if (Time.time - _pendingExitAt > 30f) { _pendingExit = null; Caption("I couldn't get over to that screen.", 3f); }
            }
            // hands are the only input: say so clearly if none has been seen (hand tracking off, permission denied...)
            if (Input.Left.tracked || Input.Right.tracked) _handsSeen = true;
            // (again every 20 s until a hand is seen: with the controllers held nothing here can be pressed, and one line shown once was easy to miss)
            else if (!_handsSeen && !Input.Simulated && Application.isFocused && Time.realtimeSinceStartup > _handsWarnAt)      // (not behind a system dialog: the app sees no hands while one is up)
            {
                _handsWarnAt = Time.realtimeSinceStartup + 20f;
                if (!_handsWarned) Debug.Log("[MR] no hand has been seen yet: asking for the controllers to be put down (again every 20 s)");
                _handsWarned = true;
                Caption(HandsLine, 8f);
            }
            _rig.SetVisible(show && !(TvPlaying && _tvGame.phase != TvRallyPhase.Join));
            _portalFx.Step(dt, _link.HostNow);
            _sparks.Step(dt, Quaternion.LookRotation(Input.HeadRot * Vector3.forward, Vector3.up));
            UpdateVoice(dt, hostOk, show);
            UpdateTennis(dt, hostOk);
            UpdateWorld(dt, hostOk);
            UpdateRps(dt);
            UpdateSwarm(dt, hostOk);     // Screen Scamps: the monsters that come out of the screen (MrSwarm)
            UpdatePoke(dt, hostOk);     // a fingertip presses a thing on the desk (MrPoke)
            UpdateRoomScripts(dt, hostOk);      // what the things' scripts do in the room (MrRoomScript)
            UpdateShow(dt, hostOk);      // a parcel that arrives, the look at a screen, the parcel it carries (MrShow)
            UpdatePointing(dt, now, hostOk);
            UpdateUi(dt, now, hostOk, show);
            UpdateLocalUi(dt, hostOk, show);      // without the PC: the first steps and their hint (MrLocalHost)
            UpdateCaption();
            UpdateSound(dt);

            if (hostOk && now >= _nextStatus)
            {
                _nextStatus = now + 0.5;
                _link.Send(Msg.MrStatus, new MrStatus { tracking = Input.HeadTracked, handsTracked = Input.Left.tracked || Input.Right.tracked, onHand = _mode == Mode.OnHand, spaceEpoch = Input.SpaceEpoch, activity = _mode + (_raining ? (_sheltered ? "+rain:sheltered" : "+rain:exposed") : ""), fps = _fps, simulated = Input.Simulated, design = _rig.DesignId });
            }
            if (hostOk && now >= _nextHead && Input.HeadTracked)
            {
                _nextHead = now + 1.0 / 30.0;
                // head position in the frame of the display that matters right now -> the PC renders its side from this viewpoint
                string monId = _tn.active ? _tn.court.monitorId : TransitionMachine.Parse(_tr.state) != TState.Idle && _tr.target != null ? _tr.target.monitorId : (_snap.pc != null ? _snap.pc.monitorId : "");
                var mv = Mon(monId);
                if (mv != null) _link.Send(Msg.HeadPose, new HeadPose { monitorId = monId, eye = mv.frame.ToLocal(Conv.N(Input.HeadPos)), tracked = true, t = _link.HostNow });
            }
            DrawDebug(hostOk);
            if (now >= _nextLinkLog)
            {
                _nextLinkLog = now + (hostOk ? 20.0 : 5.0);
                Debug.Log($"[MR] link: connected={_link.Connected} needsPairing={_link.NeedsPairing} url='{_link.CurrentUrl}' error='{_link.LastError}' snapshot={_snap != null} hands L={Input.Left.tracked} R={Input.Right.tracked} head={Input.HeadTracked} monitors={_mons.Count} mode={_mode} fps={_fps:0}");
            }
        }
        double _nextLinkLog;

        // ------------------------------------------------------------------ free behaviour (owner = MR)
        bool Free(float dt)
        {
            if (!_homeSet) InitHome();
            UpdateHold(dt);                                                             // has a pinch picked it up? (MrHold)
            UpdateHandRide(dt);
            if (_mode == Mode.Held) HeldPose(dt);                                        // it hangs from the hand, falls, lands
            else if (_mode == Mode.Hop) { StepHop(dt); }
            else if (_mode == Mode.OnHand) { RidePose(dt); }
            else if (_tn.active && _tn.court.room && _tnRoomOk) TennisRoomPose(dt);      // a game on the desk: the game moves it
            else if (StagePose(dt)) { }                                                   // something it is doing with its things, or a game of its own
            else
            {
                Emote e = EmoteForState();
                _mixer.Set(e);
                _mixer.Evaluate(Time.time, dt, _pose);
                _motor.Update(dt, _H, _pose);
                LookAtUser(_pose);
                _pos = new Vector3(_motor.Pos.x, _deskY, _motor.Pos.y);
                _rot = Quaternion.Euler(0, _motor.Yaw, 0);
            }
            ApplyRig(true);
            return true;
        }

        void InitHome()
        {
            _homeSet = true;
            Vector3 head = Input.HeadPos;
            if (_mons.Count == 0 && !_hasDesk && !_deskSensed) _deskY = head.y - 0.45f;   // nothing calibrated yet: a seated-desk estimate, never the floor
            Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0; fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
            _worldShift = Vector3.zero;                         // a spot chosen afresh: the things are laid out round it again
            _home = head + fwd * 0.42f;
            if (_deskSensed && _deskHomeValid) _home = _deskHome;      // a spot that really is at desk level (found by sensing)
            // prefer a spot on the desk between the user and the nearest calibrated screen
            MonView best = null; float bd = float.MaxValue;
            foreach (var m in _mons.Values) { float d = (m.Origin - head).sqrMagnitude; if (d < bd) { bd = d; best = m; } }
            if (best != null)
            {
                best.GAxes(out var gx, out var gy, out var gz);
                _home = best.Origin - gz * 0.26f;
            }
            _home.y = _deskY;
            _motor.Teleport(new Vector2(_home.x, _home.z), YawTo(head - _home));
            _pos = _home; _rot = Quaternion.Euler(0, _motor.Yaw, 0);
        }

        static float YawTo(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        void LookAtUser(Pose p)
        {
            // it looks at what the user points at - except in a conversation, where it looks at the person it is talking with
            Vector3 target = _showLooking ? _showLookAt      // it is reading a part of a screen (MrShow): that is what it looks at
                : _ptr.valid && _sel == null && !TalkLook.Active(_ch.talk) ? _ptr.world : Input.HeadPos;
            Vector3 headPos = _pos + Vector3.up * (0.75f * _H);
            Vector3 local = Quaternion.Inverse(_rot) * (target - headPos);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            PoseLib.AddLook(p, yaw, pitch);
            if (_mode == Mode.Free && _motor.Arrived && Mathf.Abs(yaw) > 75f) _motor.Face(_motor.Yaw + yaw); // turn the body, not just the neck
        }

        void ApplyRig(bool shadowOnDesk)
        {
            _rig.transform.SetPositionAndRotation(_pos, _rot);
            _rig.Apply(_pose);
            if (TalkLook.Active(_ch.talk)) _rig.SetGlow(TalkLook.Color(_ch.talk), 1f);
            else _rig.SetGlow(StatusColors.For(_ch.activity), StatusColors.Lit(_ch.activity));
            if (_mode == Mode.OnHand) _rig.SetShadow(false, Vector3.zero, Vector3.up, 0);
            else _rig.SetShadow(shadowOnDesk, new Vector3(_pos.x, _deskY, _pos.z), Vector3.up, Mathf.Max(0, _pos.y - _deskY));
            Vector3 l = new Vector3(-0.35f, 0.8f, -0.5f);
            var mv = Mon(_tr.target?.monitorId) ?? FirstMon();
            if (mv != null) { mv.GAxes(out var gx, out var gy, out var gz); l = gx * -0.35f + gy * 0.8f + gz * -0.5f; }
            _rig.SetLightDir(l);
        }

        MonView FirstMon() { foreach (var m in _mons.Values) return m; return null; }

        // ------------------------------------------------------------------ hops (hand <-> desk)
        void StartHop(Vector3 from, Func<Vector3> to, float apex, Quaternion rotTo, Action done)
        {
            _mode = Mode.Hop; _hopFrom = from; _hopTarget = to; _hopTo = to();
            float dist = Vector3.Distance(from, _hopTo);
            _hopDur = Mathf.Clamp(0.32f + dist * 1.6f, 0.38f, 0.95f); _hopT = 0; _hopApex = apex + dist * 0.35f; _hopDone = done;
            _hopRotFrom = _rot; _hopRotTo = rotTo;
            Sfx("whoosh", from, 0.22f, 1.35f);      // a small jump
        }

        void StepHop(float dt)
        {
            _hopT += dt;
            _hopTo = _hopTarget();                                  // the landing spot may be a moving hand
            float prep = 0.16f, u = Mathf.Clamp01((_hopT - prep) / _hopDur);
            if (_hopT < prep) { PoseLib.Crouch(_pose, M.Smooth(_hopT / prep)); _pos = _hopFrom; }
            else
            {
                float k = M.Smooth(u);
                _pos = Vector3.Lerp(_hopFrom, _hopTo, k) + Vector3.up * (_hopApex * 4f * u * (1f - u));
                _rot = Quaternion.Slerp(_hopRotFrom, _hopRotTo, k);
                if (u < 0.75f) { PoseLib.Tuck(_pose); var tmp = new Pose(); PoseLib.Crouch(tmp, 1f); Pose.Lerp(tmp, _pose, M.Smooth(u / 0.25f) * 0.55f, _pose); }
                else PoseLib.Land(_pose, M.Smooth((u - 0.75f) / 0.25f));
            }
            if (u >= 1f)
            {
                Vector3 cr = Input.HeadRot * Vector3.right; cr.y = 0;
                _sparks.Burst(_pos + Vector3.up * 0.003f, cr.sqrMagnitude > 1e-4f ? cr.normalized : Vector3.right, Vector3.up, _H * 0.26f, 5);
                Sfx("land", _pos, 0.5f);
                var d = _hopDone; _hopDone = null; _mode = Mode.Free; d?.Invoke();
            }
        }

        // ------------------------------------------------------------------ MR -> PC approach
        bool Approach(float dt)
        {
            var tgt = _tr.target; var plan = _tr.plan;
            var mv = Mon(tgt?.monitorId);
            if (tgt == null || plan == null || mv == null) return Free(dt);
            HoldAbort();      // sent into a screen while it is held in the hand: it is on the desk at once, and goes
            if (_prepTid != _tr.transitionId) { _prepTid = _tr.transitionId; _approachSent = false; _readySent = false; _holdSent = false; _approachT = 0; _approachRev = -1; if (!_homeSet) InitHome(); }
            if (!Input.HeadTracked)
            {
                // tracking lost: hold where we are instead of walking through space we can no longer place
                if (!_holdSent) { _holdSent = true; SendEvent("hold", "MR tracking lost"); }
                ApplyRig(true); return true;
            }
            if (_holdSent) { _holdSent = false; SendEvent("resume", "MR tracking restored"); }

            mv.GAxes(out var gx, out var gy, out var gz);
            Vector3 portal = mv.ToWorld(tgt.u, tgt.v);
            float footY = _deskY - portal.y;
            float dist = PlanBuilder.LaunchDistance(_H, -footY);
            Vector3 launch = portal - gz * dist; launch.y = _deskY;
            float faceYaw = YawTo(gz);
            ShowRing(portal, mv, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f));

            if (_mode == Mode.OnHand)
            {
                // leave the hand: hop down to the desk directly below, then walk (a verified surface, not a guessed path)
                Vector3 below = new Vector3(_pos.x, _deskY, _pos.z);
                _rideHand = -1;
                StartHop(_pos, () => below, 0.04f, Quaternion.Euler(0, YawTo(launch - below), 0), () => { _motor.Teleport(new Vector2(below.x, below.z), YawTo(launch - below)); });
            }
            if (_mode == Mode.Hop) { StepHop(dt); ApplyRig(true); return true; }

            _approachT += dt;
            if (_approachRev != _tr.revision && (_approachRev < 0 || Vector2.Distance(new Vector2(launch.x, launch.z), _lastLaunch) > 0.004f))
            { _approachRev = _tr.revision; _approachSent = false; _lastLaunch = new Vector2(launch.x, launch.z); _moveIssued = false; }

            // 1) acknowledge the user, 2) look at the destination, 3) turn and go
            if (_approachT < 0.45f) { _mixer.Set(Emote.Idle); _motor.Face(YawTo(Input.HeadPos - _pos)); }
            else if (_approachT < 1.15f) { _mixer.Set(Emote.Point); _motor.Face(YawTo(portal - _pos)); }
            else
            {
                _mixer.Set(Emote.Idle);
                if (!_moveIssued) { _moveIssued = true; float far = Vector2.Distance(_motor.Pos, _lastLaunch); _motor.MoveTo(_lastLaunch, far > 4.5f * _H, faceYaw); }
            }
            _mixer.Evaluate(Time.time, dt, _pose);
            _motor.Update(dt, _H, _pose);
            if (_approachT >= 0.45f)
            {
                Vector3 hp = _pos + Vector3.up * (0.75f * _H);
                Vector3 local = Quaternion.Inverse(Quaternion.Euler(0, _motor.Yaw, 0)) * (portal - hp);
                PoseLib.AddLook(_pose, Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg);
            }
            _pos = new Vector3(_motor.Pos.x, _deskY, _motor.Pos.y);
            _rot = Quaternion.Euler(0, _motor.Yaw, 0);
            ApplyRig(true);

            if (_approachSent && !_tr.approachDone && Time.realtimeSinceStartup - _sentAt > 1.2f) _approachSent = false;   // the host lost it (reconnect): confirm again
            if (!_approachSent && _moveIssued && _motor.Arrived && Vector2.Distance(_motor.Pos, _lastLaunch) < 0.004f)
            {
                _sentAt = Time.realtimeSinceStartup;
                _approachSent = true;
                float actualDist = Vector3.Dot(portal - _pos, gz);
                // report where the partner REALLY stands (portal frame) so the crossing starts from exactly here
                _link.Send(Msg.TransitionEvent, new TransitionEventMsg { transitionId = _tr.transitionId, kind = "approachDone", from = Role, tau = footY, progress = actualDist, vec = PortalLocal(mv, portal, _pos) });
                Debug.Log($"[MR] approachDone footY={footY:0.000} dist={actualDist:0.000}");
            }
            return true;
        }
        Vector2 _lastLaunch; bool _moveIssued; float _sentAt;

        void SendEvent(string kind, string reason) =>
            _link.Send(Msg.TransitionEvent, new TransitionEventMsg { transitionId = _tr.transitionId, kind = kind, from = Role, reason = reason });

        void ShowRing(Vector3 portal, MonView mv, float pulse)
        {
            bool crossingNow = TransitionMachine.Parse(_tr.state) == TState.Crossing;
            _portalFx.Show(portal - mv.Z * 0.0008f, Quaternion.LookRotation(mv.Z, mv.Y), PlanBuilder.ClearanceRadius(_H), pulse, crossingNow);
            _ring.transform.SetPositionAndRotation(portal - mv.Z * 0.0006f, Quaternion.LookRotation(mv.Z, mv.Y));
            _ring.transform.localScale = Vector3.one * PlanBuilder.ClearanceRadius(_H) * 2.0f * (1f + 0.04f * Mathf.Sin(Time.time * 6f));
            Ui.SetFxColor(_ring, new Color(Ui.Pal.Orange.r, Ui.Pal.Orange.g, Ui.Pal.Orange.b, Mathf.Min(1f, pulse * 4f) * (0.22f + 0.38f * pulse)));
        }

        // ------------------------------------------------------------------ PC -> MR receive
        void ReceiverPrepare()
        {
            var tgt = _tr.target; var mv = Mon(tgt?.monitorId);
            if (tgt == null) return;
            if (_prepTid != _tr.transitionId) { _prepTid = _tr.transitionId; _approachSent = false; _readySent = false; _holdSent = false; }
            if (mv == null || !Input.HeadTracked)
            {
                // the receiving side is not really ready: say so instead of letting the PC copy disappear
                if (!_holdSent) { _holdSent = true; _readySent = false; SendEvent("hold", mv == null ? "that display is not calibrated in MR" : "MR tracking lost"); }
                return;
            }
            if (_holdSent) { _holdSent = false; SendEvent("resume", "MR ready"); }
            Vector3 portal = mv.ToWorld(tgt.u, tgt.v);
            ShowRing(portal, mv, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
            if (_readySent && !_tr.receiverReady && Time.realtimeSinceStartup - _sentAt > 1.2f) _readySent = false;
            if (!_readySent)
            {
                _readySent = true; _sentAt = Time.realtimeSinceStartup;
                _link.Send(Msg.TransitionEvent, new TransitionEventMsg { transitionId = _tr.transitionId, kind = "receiverReady", from = Role, vec = PortalLocal(mv, portal, StandPoint(mv, portal)) });
            }
        }

        // ------------------------------------------------------------------ crossing (both directions)
        bool Crossing(double now)
        {
            var plan = _tr.plan; var mv = Mon(plan.monitorId) ?? Mon(_tr.target?.monitorId);
            if (mv == null) return false;
            float tau = _tr.clock != null ? _tr.clock.TauAt(_link.HostNow, plan.Total) : 0f;
            // draw the pose for the moment these pixels reach the eye (display latency; to be measured on the headset)
            float tauDraw = _tr.clock != null ? _tr.clock.TauAt(_link.HostNow + DisplayLatencyMs / 1000f, plan.Total) : tau;
            PlanEvaluator.Evaluate(plan, tauDraw, _cs);
            ScreenMath.PixelToUV(mv.desc, plan.portalPxX, plan.portalPxY, out float u, out float v);
            Vector3 portal = mv.ToWorld(u, v);
            mv.GAxes(out var gx, out var gy, out var gz);
            // the plan is in the display frame: map it into the room through the calibrated monitor frame
            Vector3 g = Conv.U(_cs.RootPos);
            _pos = portal + mv.X * g.x + mv.Y * g.y + mv.Z * g.z;
            _rot = Quaternion.LookRotation(mv.Z, mv.Y) * Conv.U(_cs.RootRot);
            _pose.CopyFrom(_cs.Pose);
            _mode = Mode.Free; _rideHand = -1;
            _rig.transform.SetPositionAndRotation(_pos, _rot);
            _rig.Apply(_pose);
            _rig.SetClip(portal, mv.Z, -1f);                 // MR side = in front of the screen
            _rig.SetGlow(StatusColors.For("idle"), 0f);
            _rig.SetShadow(true, new Vector3(_pos.x, _deskY, _pos.z), Vector3.up, Mathf.Max(0, _pos.y - _deskY));
            _rig.SetLightDir(gx * -0.35f + gy * 0.8f + gz * -0.5f);
            _crossTau = tau; _crossProgress = PlanEvaluator.CrossingProgress(plan, _cs); _crossPhase = _cs.Phase.ToString();
            Vector3 camRight = Input.HeadRot * Vector3.right; camRight.y = 0; camRight = camRight.sqrMagnitude > 1e-4f ? camRight.normalized : Vector3.right;
            if (_cs.Phase != _lastPhase)
            {
                Vector3 feet = new Vector3(_pos.x, _deskY + 0.004f, _pos.z);
                if (_cs.Phase == CrossPhase.Land && plan.direction == 1) _sparks.Burst(feet, camRight, Vector3.up, _H * 0.42f);
                if (_cs.Phase == CrossPhase.FlightIn && plan.direction == 0) _sparks.Burst(feet, camRight, Vector3.up, _H * 0.34f, 7);
                // what is heard of a crossing on this side: the rush of air as it goes through the glass, and its feet on the desk when it has come out
                if (_cs.Phase == CrossPhase.Thread) Sfx("whoosh", portal, 0.8f);
                if (_cs.Phase == CrossPhase.Land && plan.direction == 1) Sfx("land", feet, 0.6f);
                _lastPhase = _cs.Phase;
            }
            if (_cs.Phase == CrossPhase.FlightIn || _cs.Phase == CrossPhase.Thread || _cs.Phase == CrossPhase.FlightOut)
            {
                _trailT -= Time.unscaledDeltaTime;
                Vector3 chest = _rig.ChestWorld;
                if (_trailT <= 0f && Vector3.Dot(chest - portal, mv.Z) < -0.012f) { _trailT = 0.05f; _sparks.Trail(chest - _rot * new Vector3(0, 0.25f * _H, 0), _H * 0.42f); }
            }
            float after = _crossProgress >= 0.999f ? Mathf.Clamp01(1f - (tau - plan.ThreadEnd) / 0.45f) : 1f;
            if (after > 0.01f) ShowRing(portal, mv, Mathf.Clamp01(0.25f + 1f - Mathf.Abs(_crossProgress * 2f - 1f)) * after);
            // keep the walking state in step with the body so nothing pops when the crossing ends or is rewound
            Vector3 mrFoot = portal + mv.X * plan.mrFoot.x + mv.Y * plan.mrFoot.y + mv.Z * plan.mrFoot.z;
            _motor.Teleport(new Vector2(mrFoot.x, mrFoot.z), plan.direction == 0 ? YawTo(gz) : YawTo(-gz));
            if (_link.Connected && now >= _nextBeat)
            {
                _nextBeat = now + 0.1;
                _link.Send(Msg.TransitionEvent, new TransitionEventMsg { transitionId = _tr.transitionId, kind = "renderBeat", from = Role, tau = tau, progress = _crossProgress, visibleFraction = 1f });
            }
            return true;
        }

        void LandFromPlan()
        {
            _landedTid = _tr.transitionId;
            var plan = _tr.plan; var mv = Mon(plan.monitorId);
            if (mv == null) return;
            ScreenMath.PixelToUV(mv.desc, plan.portalPxX, plan.portalPxY, out float u, out float v);
            Vector3 portal = mv.ToWorld(u, v);
            mv.GAxes(out var gx, out var gy, out var gz);
            Vector3 foot = portal + mv.X * plan.mrFoot.x + mv.Y * plan.mrFoot.y + mv.Z * plan.mrFoot.z;
            _motor.Teleport(new Vector2(foot.x, foot.z), YawTo(-gz));
            _pos = new Vector3(foot.x, _deskY, foot.z);
            _mode = Mode.Free;
            if (_homeSet) _motor.MoveTo(new Vector2(_home.x, _home.z), false, YawTo(Input.HeadPos - _home));
            OnReturnedToMr();
        }

        // ------------------------------------------------------------------ pointing and target selection
        void UpdatePointing(float dt, double now, bool hostOk)
        {
            _ptr = default;
            FindPointer();      // which hand is pointing, and where its ray meets a display (MrGlassHand)
            if (_ptr.valid)
            {
                // stabilise the pointer (hand tremor) without making it lag behind deliberate motion
                var raw = new Vector2(_ptr.u, _ptr.v);
                if (_ptrMon != _ptr.mon) { _ptrMon = _ptr.mon; _ptrFiltered = raw; }
                float speed = (raw - _ptrFiltered).magnitude / Mathf.Max(dt, 1e-4f);
                _ptrFiltered = Vector2.Lerp(_ptrFiltered, raw, Mathf.Clamp01(dt * Mathf.Lerp(7f, 30f, Mathf.Clamp01(speed * 2f))));
                _ptr.u = _ptrFiltered.x; _ptr.v = _ptrFiltered.y;
                var mv = Mon(_ptr.mon);
                _ptr.world = mv.ToWorld(_ptr.u, _ptr.v);
            }
            else _ptrMon = "";
            DrawPointer(dt);      // the ray from the hand and the ring on the display (MrGlassHand); the small dot is no longer used

            // What the user points at AND what they look at, 15 times a second - also when no hand points (the gaze
            // alone is something to go by). The host decides from these what was meant when something is said.
            if (hostOk && now >= _nextPointer)
            {
                _nextPointer = now + 1.0 / 15.0;
                var hs = _ptr.hand == 0 ? Input.Right : Input.Left;
                var sample = new PointerSample { t = _link.HostNow, monitorId = _ptr.mon ?? "", u = _ptr.u, v = _ptr.v, pinch = _ptr.valid && hs.pinch, valid = _ptr.valid };
                if (Input.HeadTracked && GazeTarget(out string gazeMon, out float gazeU, out float gazeV)) { sample.gazeValid = true; sample.gazeMonitorId = gazeMon; sample.gazeU = gazeU; sample.gazeV = gazeV; }
                _link.Send(Msg.PointerSample, sample);
                _ptrSamples++;
            }
            SendGlassHand(now, hostOk);      // the hands in front of a display, for the PC to draw (MrGlassHand)

            // (during a game the hands are rackets: a pinch on the screen must not send the partner anywhere)
            bool busy = GomokuOn || _tn.active || GlassHoldsPinch || (TransitionMachine.Parse(_tr.state) is TState s && s != TState.Idle && s != TState.Complete && s != TState.Cancelled);
            var hand = _ptr.hand == 0 ? Input.Right : Input.Left;
            // (pointing and pinching sends the partner somewhere only in the simulator. On a headset a relaxed hand closes
            // finger and thumb by itself now and then: "it comes out without being asked" was exactly that. There it is told
            // by voice, or with the hand menu.)
            if (Input.Simulated && hostOk && !busy && !_uiConsumedPinch && _talkPinchFrame != Time.frameCount && _ptr.valid && hand.pinchDown && !_calibrating)
            {
                var mv = Mon(_ptr.mon);
                bool again = _sel != null && _sel.result != null && _sel.result.valid && _sel.mon == _ptr.mon && Time.time - _sel.time < 8f &&
                             Vector3.Distance(_ptr.world, mv.ToWorld(_sel.result.u, _sel.result.v)) < 0.06f;
                if (again) Confirm();
                else Select(_ptr.mon, _ptr.u, _ptr.v, _ch.owner == Env.Mr ? 0 : 1, false, false, "");
            }
            if (_sel != null && !_sel.auto && Time.time - _sel.time > 8f) _sel = null;

            // preview marker: small and explicit, shows the RESOLVED point (and says so when it had to move)
            if (_sel != null && _sel.result != null && Mon(_sel.result.monitorId) is MonView smv)
            {
                Vector3 p = smv.ToWorld(_sel.result.u, _sel.result.v);
                _marker.enabled = true;
                _marker.transform.SetPositionAndRotation(p - smv.Z * 0.0012f, Quaternion.LookRotation(smv.Z, smv.Y));
                _marker.transform.localScale = Vector3.one * PlanBuilder.ClearanceRadius(_H) * 2.4f;
                Ui.SetFxPhase(_marker, Time.time * 0.22f);
                Ui.SetFxColor(_marker, _sel.result.valid ? (_sel.result.adjusted ? new Color(0.99f, 0.83f, 0.56f, 0.95f) : new Color(0.71f, 0.86f, 0.72f, 0.95f)) : new Color(0.93f, 0.52f, 0.50f, 0.95f));
                string verb = _sel.direction == 0 ? "go in" : "come out";
                string text = !_sel.result.valid ? "Can't go here: " + _sel.result.reason
                    : (_sel.result.adjusted ? $"Moved the spot: {_sel.result.reason}\n" : "") + $"Pinch again to {verb}" + (_sel.result.anchorKind == "window" ? $"\n[{Short(_sel.result.windowTitle, 26)}]" : "");
                Place(_markerLabel, p - smv.Z * 0.02f + smv.Y * (PlanBuilder.ClearanceRadius(_H) + 0.03f), text);
            }
            else { _marker.enabled = false; _markerLabel.text = ""; }
        }

        static string Short(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

        (string mon, float u, float v)? _pendingExit; float _pendingExitAt; bool _handsSeen, _handsWarned; float _handsWarnAt = 12f;
        CrossPhase _lastPhase = CrossPhase.Done; float _trailT;

        void Select(string mon, float u, float v, int direction, bool auto, bool analyze, string prompt)
        {
            if (direction == 1 && _ch.owner == Env.Pc && _snap?.pc != null && !string.IsNullOrEmpty(mon) && _snap.pc.monitorId != mon && _snap.pc.monitorId.Length > 0)
            {
                // the exit is on another display: the partner first walks there inside the PC (its own state), then comes out
                _pendingExit = (mon, u, v); _pendingExitAt = Time.time;
                _link.Send(Msg.Debug, new DebugCommand { cmd = "pcGoMonitor", text = mon });
                Caption("I'll walk over to that screen first.", 3f);
                return;
            }
            if ((direction == 0 ? Env.Mr : Env.Pc) != _ch.owner) { Caption(direction == 0 ? "I'm inside the PC right now." : "I'm already out here.", 3f); return; }
            _sel = new Selection { queryId = Guid.NewGuid().ToString("N"), mon = mon, u = u, v = v, direction = direction, time = Time.time, auto = auto, analyze = analyze, prompt = prompt };
            _link.Send(Msg.TargetQuery, new TargetQuery { queryId = _sel.queryId, monitorId = mon, u = u, v = v, direction = direction });
        }

        void SelectPixel(float px, float py, int direction, bool analyze)
        {
            // same path as a pointed selection whenever the pixel lies on a known display
            var known = _snap != null ? ScreenMath.FindMonitor(_snap.monitors, px, py) : null;
            if (known != null)
            {
                ScreenMath.PixelToUV(known, px, py, out float ku, out float kv);
                Select(known.id, ku, kv, direction, true, analyze, "");
                return;
            }
            _sel = new Selection { queryId = Guid.NewGuid().ToString("N"), mon = "", direction = direction, time = Time.time, auto = true, analyze = analyze };
            _link.Send(Msg.TargetQuery, new TargetQuery { queryId = _sel.queryId, usePixel = true, px = px, py = py, direction = direction });
        }

        void RequestReturnDefault()
        {
            if (_ch.owner != Env.Pc || _snap?.pc == null) { Caption("I'm already here.", 3f); return; }
            var mv = Mon(_snap.pc.monitorId);
            if (mv == null) { Caption("That monitor isn't calibrated yet.", 4f); return; }
            float mpp = mv.frame.widthM / mv.desc.bounds.w;
            SelectPixel(_snap.pc.pxX, _snap.pc.pxY - 1.7f * _H / mpp, 1, false);
        }

        void OnTargetResult(TargetInfo t)
        {
            if (_sel == null || t.queryId != _sel.queryId) return;
            _sel.result = t; _sel.mon = t.monitorId; _sel.time = Time.time;
            if (t.valid && Mon(t.monitorId) == null) { t.valid = false; t.reason = "that display is not calibrated in MR yet"; }
            if (_sel.auto && t.valid) Confirm();
            else if (_sel.auto) { Caption("I can't go there: " + t.reason, 4f); }
        }

        void Confirm()
        {
            var t = _sel.result; var mv = Mon(t.monitorId);
            if (t == null || !t.valid || mv == null) return;
            Vector3 portal = mv.ToWorld(t.u, t.v);
            float footY = _deskY - portal.y;
            var req = new TransitionRequest
            {
                requestId = Guid.NewGuid().ToString("N"), direction = _sel.direction, target = t,
                mrFootLocal = PortalLocal(mv, portal, StandPoint(mv, portal)), worldUpLocal = mv.frame.DirToLocal(SN.Vector3.UnitY),
            };
            if (_sel.direction == 0 && _sel.analyze) _link.Send(Msg.TaskCommand, new TaskCommand { cmd = _sel.arrival, prompt = _sel.prompt });
            _link.Send(Msg.TransitionRequest, req);
            Debug.Log($"[MR] transition requested dir={_sel.direction} mon={t.monitorId} px=({t.pxX:0},{t.pxY:0}) adjusted={t.adjusted} footY={footY:0.000}");
            _sel = null;
        }

        // ------------------------------------------------------------------ captions / debug
        public void Caption(string text, float seconds)
        {
            if (Time.time < _callCaptionUntil) return;
            if (text != _captionShown) { _captionShown = text; _captionPop = 0f; _captionSpark = true; _caption.transform.localScale = Vector3.one; }
            _caption.SetWrapped(text, 0.36f); _captionUntil = Time.time + seconds;
        }
        string _captionShown = ""; float _captionPop = 1f; bool _captionSpark;
        float _callCaptionUntil;

        void UpdateCaption()
        {
            if (Time.time > _captionUntil) { _caption.text = ""; _captionShown = ""; _captionBg.enabled = false; return; }
            Vector3 p;
            if (Time.time < _callCaptionUntil) p = Input.HeadPos + Input.HeadRot * new Vector3(0, -0.08f, 0.55f);
            else if (_rig.Visible) p = _rig.HeadTopWorld + Vector3.up * (0.040f + _caption.Height * 0.5f);
            else
            {
                var mv = Mon(_snap?.pc?.monitorId) ?? FirstMon();
                p = mv != null ? mv.Origin + mv.Y * (mv.frame.heightM * 0.5f + 0.03f) - mv.Z * 0.02f : Input.HeadPos + Input.HeadRot * new Vector3(0, _calibrating ? -0.21f : -0.12f, 0.5f);   // lower while the user has to see the corners of a screen
            }
            p += Vector3.up * _captionLift;      // (room under it for the strip that shows what was heard: MrHelp)
            // (the card with "Play now" is up: the line goes BELOW the card, not over its buttons - MrLocalHost)
            if (_lc == LcState.Ask && _welcome.Shown && _wlSet && !_rig.Visible) p =_wlPos + Vector3.down * (_welcome.height * 0.5f + 0.032f + _caption.Height * 0.5f);
            Place(_caption, p, null);
            if (_captionSpark) { _captionSpark = false; _sparks.Burst(p, Input.HeadRot * Vector3.right, Vector3.up, 0.05f, 8); }      // a new line arrives with a little sparkle
            // the bubble is sized from the measured text, not from a character count
            _captionPop = Mathf.Min(1f, _captionPop + Time.deltaTime / 0.32f);
            _caption.transform.localScale = Vector3.one * Mathf.Max(0.001f, Ui.Pop(_captionPop));      // it pops up; the bubble follows the measured text
            float cw = Mathf.Clamp(_caption.Width + 0.046f, 0.07f, 0.42f), chh = _caption.Height + 0.030f;
            _captionBg.enabled = _caption.text.Length > 0;
            _captionBg.transform.SetPositionAndRotation(_caption.transform.position + _caption.transform.forward * 0.002f, _caption.transform.rotation);
            Ui.SizePanel(_captionBg, cw, chh, Mathf.Min(chh * 0.5f, 0.024f), 0.0020f); Ui.SetFxPhase(_captionBg, Time.time);
        }

        /// <summary>Positions a label and turns it to face the user.</summary>
        void Place(StoryLabel tm, Vector3 pos, string text)
        {
            if (text != null) tm.text = text;
            Vector3 to = pos - Input.HeadPos;
            tm.transform.SetPositionAndRotation(pos, to.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(to.normalized, Vector3.up) : Quaternion.identity);
        }

        void DrawDebug(bool hostOk)
        {
            bool on = _settings.debugOverlay;
            foreach (var mv in _mons.Values) mv.outline.enabled = on || _ptr.valid || _calibrating || _sel != null || Time.time < _showOutlinesUntil;
            if (!on) { _dbg.text = ""; return; }
            Vector3 p = Input.HeadPos + Input.HeadRot * new Vector3(-0.16f, 0.10f, 0.45f);
            Place(_dbg, p, $"MR {_mode} host={(hostOk ? "ok" : "LOST")} sim={Input.Simulated}\nstate={_tr.state} owner={_tr.owner} dir={_tr.direction}\ntau={_crossTau:0.000} prog={_crossProgress * 100f:0}% {_crossPhase}\nclock synced={_link.Clock.Synced} rtt={(_link.Clock.Rtt < 10 ? _link.Clock.Rtt * 1000 : 0):0.0}ms fps={_fps:0}\nmons={_mons.Count} desk={_deskY:0.000}{(_hasDesk ? "" : " (estimated)")}\n{Input.Diagnostics}");
        }
    }
}
