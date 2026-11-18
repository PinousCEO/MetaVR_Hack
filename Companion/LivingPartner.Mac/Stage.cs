using System.Diagnostics;
using System.Net;
using System.Numerics;
using LivingPartner.Shared;
using LivingPartner.Host;

namespace LivingPartner.Mac;

record ScreenInput(string Id, int Width, int Height, double Inches, bool Active);

// Both the web renderer and the Quest use one clock and the project's original portal geometry.
sealed partial class Stage
{
    public readonly object Sync = new();
    readonly Stopwatch time = Stopwatch.StartNew();
    readonly StateSnapshot snap = new() { sessionId = Guid.NewGuid().ToString("N"), pc = new(), settings = new() { micEnabled = false, voiceReplies = false } };
    readonly TransitionMachine machine = new();
    public readonly GomokuTable Table = new();
    readonly CharState pose = new();
    readonly Pairing pairing;
    readonly string dataDir;
    MonitorDesc screen;
    Peer headset;
    Vector3 mrFoot, up;
    FlashItem flash;
    string device = "", session = "";
    double flashUntil, lastBrowser, lastSnapshot;
    long sequence;
    double Now => time.Elapsed.TotalSeconds;
    TransitionInfo T => machine.Info;

    public Stage(Pairing pairing, string data)
    {
        this.pairing = pairing; dataDir = data;
        snap.settings.micEnabled = File.Exists(modelPath); snap.settings.voiceReplies = File.Exists(Path.Combine(dataDir, "speak-replies"));
        InitGames(); InitWorkshop();
    }
    public void SetScreen(ScreenInput s)
    {
        if (!s.Active) { ClearScreen(); return; }
        if (s.Width is < 240 or > 16384 || s.Height is < 180 or > 16384 || !double.IsFinite(s.Inches) || s.Inches is < 0 or > 400)
            throw new ArgumentException("Choose a valid screen size.");
        var display = Displays.Active().FirstOrDefault(d => d.Id == s.Id);
        if (display == null && s.Id != "mac-tv") throw new ArgumentException("Select your display again.");
        double w = 0, h = 0;
        if (s.Inches >= 5)
        {
            double mmPerPixel = s.Inches * 25.4 / Math.Sqrt((double)s.Width * s.Width + (double)s.Height * s.Height);
            w = s.Width * mmPerPixel; h = s.Height * mmPerPixel;
        }
        else if (display?.WidthMm > 80 && display.HeightMm > 50) { w = display.WidthMm; h = display.HeightMm; }
        if (screen == null || screen.id != s.Id || screen.bounds.w != s.Width || screen.bounds.h != s.Height ||
            Math.Abs(screen.physWidthM - w / 1000) > .005) ClearScreen();
        var rect = new RectI(0, 0, s.Width, s.Height);
        screen = new MonitorDesc { id = s.Id, stableId = s.Id, label = display?.Name ?? "TV", index = 1, primary = true,
            bounds = rect, work = rect, physWidthM = (float)(w / 1000), physHeightM = (float)(h / 1000), refreshHz = 60 };
        snap.monitors.Clear(); snap.monitors.Add(screen);
        snap.pc.monitorId = screen.id; lastBrowser = Now;
    }
    void ClearScreen()
    {
        Table.Stop();
        StopNativeGames(false);
        EndScreenGame();
        screen = null; flash = null; snap.monitors.Clear(); snap.calibrations.Clear(); snap.calibratingMonitorId = "";
        snap.pc.monitorId = ""; ResetOwner();
    }
    void ResetOwner() { lastSpot = null; machine.Restore(null); snap.character.owner = Env.Mr; snap.pc.overlayVisible = false; }

    public object View()
    {
        lastBrowser = Now;
        var target = T.target;
        double x = target?.u ?? .5, y = .90, opacity = T.owner == Env.Pc ? 1 : 0;
        double h = screen == null ? .22 : Math.Clamp(.10 / Math.Max(.04, screen.physHeightM), .035, .50);
        if (target != null && screen != null)
        {
            h = snap.character.heightM / (target.metersPerPixel * screen.bounds.h);
            y = target.floorPxY / screen.bounds.h;
            if (T.plan != null && T.clock != null && machine.State == TState.Crossing)
            {
                PlanEvaluator.Evaluate(T.plan, T.clock.TauAt(Now, T.plan.Total), pose);
                float through = PlanEvaluator.Through(T.plan, pose).fraction;
                opacity = T.direction == 0 ? through : 1 - through;
                x = (target.pxX + pose.RootPos.X / target.metersPerPixel) / screen.bounds.w;
                y = (target.pxY - pose.RootPos.Y / target.metersPerPixel) / screen.bounds.h;
            }
        }
        return new { pairCode = pairing.Code, connected = headset != null, device, active = screen != null,
            calibrated = snap.calibrations.Count > 0, tableMonitor = Table.Wanted, owner = T.owner, transition = T.state,
            x, y, height = h, opacity, portal = machine.IsActive, u = target?.u ?? .5, v = target?.v ?? .55,
            flash = flash != null && Now < flashUntil ? new[] { flash.r, flash.g, flash.b } : null, game = NativeGameFrame() ?? GameFrame(),
            speech = Now < speechUntil ? speech : "", mic = snap.settings.micEnabled, speak = snap.settings.voiceReplies, work = WorkFrame() };
    }

    public string Command(string cmd)
    {
        if (cmd == "speech-on" || cmd == "speech-off")
        {
            bool enabled = cmd == "speech-on";
            string file = Path.Combine(dataDir, "speak-replies");
            if (enabled) File.WriteAllText(file, "1"); else File.Delete(file);
            snap.settings.voiceReplies = enabled; return "";
        }
        if (headset == null) return "Connect your headset first.";
        if (cmd.StartsWith("game:")) return GameButton(cmd[5..]);
        if (cmd == "scan") { Send(headset, Msg.IntentResult, new { kind = "calibrate", text = "" }); return ""; }
        if (cmd != "enter" && cmd != "return") return "Unknown command.";
        if (screen == null || snap.calibrations.Count == 0) return "Set the TV to fullscreen, then calibrate it from your headset.";
        if (machine.IsActive) return "Nilo is crossing the screen.";
        if ((cmd == "enter") != (T.owner == Env.Mr)) return "Nilo is already there.";
        var pointer = cmd == "enter" && lastSpot?.monitorId == screen.id && Now - spotAt <= 8
            ? lastSpot : new PointerSample { valid = true, monitorId = screen.id, u = .5f, v = .55f };
        Send(headset, Msg.IntentResult, new { kind = cmd == "enter" ? "enterPortal" : "returnToMr", text = "", alsoStart = false,
            pointer });
        return "";
    }
    void Send(Peer p, string kind, object body) => p?.Queue(Proto.Pack(kind, body, seq: ++sequence, t: Now));
    void Snapshot()
    {
        if (headset == null) return;
        lastSnapshot = Now;
        snap.seq = sequence + 1; snap.hostTime = Now; snap.transition = T; snap.character.owner = T.owner;
        snap.pc.overlayVisible = T.owner == Env.Pc;
        if (T.target != null) { snap.pc.pxX = T.target.pxX; snap.pc.pxY = T.target.floorPxY; }
        snap.connectedRoles.Clear(); snap.connectedRoles.Add(Roles.Quest);
        if (screen != null) snap.connectedRoles.Add(Roles.Pc);
        Send(headset, Msg.Snapshot, snap);
    }
    public void Tick()
    {
        Table.Update(headset!=null,screen==null ? [] : [new(screen.id,screen.label,snap.calibrations.Any(c=>c.monitorId==screen.id))],
            s=>Send(headset,"gomoku.state",s), id=>{StopNativeGames(true);StopGlassGames();Send(headset,Msg.IntentResult,new {kind="playGame",text="game-gomoku",pointer=new PointerSample{valid=true,monitorId=id,u=.5f,v=.5f}});}, null);
        if (screen != null && Now - lastBrowser > 12) ClearScreen();
        int revision = T.revision;
        if (machine.State == TState.Crossing && Now >= T.clock.anchorHost + T.plan.Total)
            machine.Apply(TEvent.CrossingFinished, T.transitionId, Now);
        if (machine.State == TState.Settling && Now - T.updatedAt > .25) machine.Apply(TEvent.Settled, T.transitionId, Now);
        if (machine.IsActive && Now - T.startedAt > 45) { machine.Apply(TEvent.Cancel, T.transitionId, Now, "timeout"); machine.Apply(TEvent.RecoverDone, T.transitionId, Now); }
        TickNativeGames(); TickGames(); TickWorkshop();
        if (revision != T.revision || Now - lastSnapshot >= (machine.IsActive ? .20 : .85)) Snapshot();
    }
    public void Lost(Peer p)
    {
        CameraDisconnected(p);
        if (headset != p) return;
        Table.Disconnect();
        EndScreenGame();
        StopNativeGames(false);
        headset = null; flash = null; device = ""; ResetOwner();
    }
    public void Message(Peer p, Envelope e)
    {
        if (e == null || e.v != Proto.Version) return;
        if (e.type == Msg.PairRequest)
        {
            var token = pairing.Pair(p.Address.ToString(), e.Body<PairRequest>()?.code);
            Send(p, Msg.PairResult, new PairResult { ok = token.Length > 0, token = token, reason = token.Length == 0 ? "Check the code on the Mac" : "" }); return;
        }
        if (e.type == Msg.Hello)
        {
            var hello = e.Body<Hello>();
            if (hello?.role != Roles.Quest || hello.protocol != Proto.Version || !pairing.Trusted(hello.token))
            { Send(p, Msg.Reject, new Reject { reason = "pairing-required" }); return; }
            if (headset != null && headset != p) headset.Abort();
            if (session != hello.roomSession || session.Length == 0) { snap.calibrations.Clear(); ResetOwner(); }
            headset = p; device = hello.deviceName; session = hello.roomSession ?? "";
            Send(p, Msg.Welcome, new Welcome { sessionId = snap.sessionId, hostTime = Now, hostName = Environment.MachineName }); Snapshot(); return;
        }
        if (p != headset) return;
        if(e.type.StartsWith("gomoku.",StringComparison.Ordinal)){Table.Packet(e);return;}
        if (e.type == Msg.EyeFrame) { GotPhoto(p, e.Body<EyeFrame>()); return; }
        if (e.type == Msg.VoiceClip) { Hear(p, e.Body<VoiceClip>()); return; }
        if (e.type == Msg.Utterance) { Dispatch(e.Body<Utterance>()?.text); return; }
        if (NativeGameMessage(e)) return;
        if (GameMessage(e)) return;
        if (WorkMessage(e)) return;
        switch (e.type)
        {
            case Msg.Ping: Send(p, Msg.Pong, new Pong { c = e.Body<Ping>()?.c ?? 0, h = Now }); break;
            case Msg.MrStatus:
                snap.mr = e.Body<MrStatus>();
                if (!string.IsNullOrEmpty(snap.mr?.spaceEpoch) && snap.calibrations.Any(c => c.spaceEpoch.Length > 0 && c.spaceEpoch != snap.mr.spaceEpoch))
                { snap.calibrations.Clear(); ResetOwner(); Snapshot(); }
                break;
            case Msg.CalibBegin: snap.calibratingMonitorId = e.Body<MonitorCalibration>()?.monitorId ?? ""; break;
            case Msg.CalibEnd: snap.calibratingMonitorId = ""; break;
            case Msg.CalibClear: snap.calibrations.Clear(); break;
            case Msg.CalibFlash: flash = e.Body<CalibFlash>()?.items?.Find(i => i.monitorId == screen?.id); flashUntil = Now + 1; break;
            case Msg.CalibCommit: Calibrate(e.Body<MonitorCalibration>()); Snapshot(); break;
            case Msg.TargetQuery: Send(p, Msg.TargetResult, Resolve(e.Body<TargetQuery>())); break;
            case Msg.TransitionRequest: Begin(e.Body<TransitionRequest>()); break;
            case Msg.TransitionEvent: Motion(e.Body<TransitionEventMsg>()); break;
        }
    }
    public bool Authenticated(Peer p) => p == headset;
    public bool MayFetch(IPAddress remote) => remote != null && (IPAddress.IsLoopback(remote) ||
        headset?.Address.MapToIPv6().Equals(remote.MapToIPv6()) == true);
    void Calibrate(MonitorCalibration k)
    {
        if (k == null || screen == null || k.monitorId != screen.id) return;
        var f = MonitorFrame.FromCalibration(k);
        if (!(f.widthM is > .05f and < 10 && f.heightM is > .05f and < 10)) return;
        k.widthM = f.widthM; k.heightM = f.heightM; k.pixelW = screen.bounds.w; k.pixelH = screen.bounds.h;
        k.residualM = f.Residual(k.tl, k.tr, k.bl, k.br);
        if (!float.IsFinite(k.residualM) || k.residualM > Math.Max(.05f, f.widthM * .12f)) return;
        k.stableId = screen.stableId; k.savedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        snap.calibrations.Clear(); snap.calibrations.Add(k); snap.calibratingMonitorId = ""; flash = null;
    }
    TargetInfo Resolve(TargetQuery q)
    {
        var t = new TargetInfo { queryId = q?.queryId ?? "", resolvedAt = Now, reason = "TV unavailable" };
        if (q == null || screen == null || q.monitorId != screen.id && !(q.usePixel && string.IsNullOrEmpty(q.monitorId))) return t;
        int w = screen.bounds.w, h = screen.bounds.h;
        float u = q.usePixel ? q.px / w : q.u, v = q.usePixel ? q.py / h : q.v;
        if (!float.IsFinite(u) || !float.IsFinite(v)) return t;
        t.valid = true; t.reason = ""; t.monitorId = screen.id; t.u = Math.Clamp(u,0,1); t.v = Math.Clamp(v,0,1);
        t.reqPxX = t.pxX = t.u * (w - 1); t.reqPxY = t.pxY = t.v * (h - 1);
        t.floorPxY = h * .90f; t.bottomEdgePxY = h; t.topEdgePxY = 0;
        ScreenMath.PhysicalSize(screen, snap.calibrations.FirstOrDefault(), out float widthM, out _, out t.sizeSource);
        t.metersPerPixel = widthM / w; return t;
    }
    void Begin(TransitionRequest r)
    {
        var selected = r?.target;
        if (selected == null || r.direction is < 0 or > 1 || !selected.valid || snap.calibrations.Count == 0) return;
        var target = Resolve(new TargetQuery { monitorId = selected.monitorId, u = selected.u, v = selected.v });
        if (!target.valid || !machine.Begin("mac-" + Guid.NewGuid().ToString("N")[..10], snap.character.characterId, r.direction, T.owner, target, Now)) return;
        mrFoot = r.mrFootLocal; up = r.worldUpLocal; BuildPlan();
        machine.Apply(TEvent.BeginApproach, T.transitionId, Now);
        machine.Apply(r.direction == 0 ? TEvent.ReceiverReady : TEvent.ApproachDone, T.transitionId, Now);
        Send(headset, Msg.TransitionUpdate, T); Snapshot();
    }
    void BuildPlan()
    {
        var t = T.target; float m = t.metersPerPixel;
        T.plan = PlanBuilder.Build(T.transitionId, T.characterId, T.direction, snap.character.heightM, mrFoot, up,
            (t.pxY - t.floorPxY) * m, (t.bottomEdgePxY - t.pxY) * m, t.pxY * m);
        T.plan.monitorId = t.monitorId; T.plan.portalPxX = t.pxX; T.plan.portalPxY = t.pxY;
    }
    void Motion(TransitionEventMsg e)
    {
        if (e == null || e.transitionId != T.transitionId) return;
        if (e.kind == "approachDone" && T.direction == 0 || e.kind == "receiverReady" && T.direction == 1)
        {
            bool changed = machine.Apply(T.direction == 0 ? TEvent.ApproachDone : TEvent.ReceiverReady, T.transitionId, Now);
            if (!changed) return;
            mrFoot = e.vec; BuildPlan();
        }
        else if (e.kind == "cancel") { machine.Apply(TEvent.Cancel, T.transitionId, Now, e.reason); machine.Apply(TEvent.RecoverDone, T.transitionId, Now); }
        else if (e.kind == "hold" && machine.State != TState.Crossing) machine.Apply(TEvent.Hold, T.transitionId, Now);
        else if (e.kind == "resume") machine.Apply(TEvent.Resume, T.transitionId, Now);
        else return;
        if (machine.State == TState.ReceiverReady)
        { T.clock = new CrossClock { transitionId = T.transitionId, anchorHost = Now + .12, rate = 1, revision = 1 }; machine.Apply(TEvent.StartCrossing, T.transitionId, Now); }
        Send(headset, Msg.TransitionUpdate, T); Snapshot();
    }
}
