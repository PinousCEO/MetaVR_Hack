using System.Numerics;
using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// Drives the portal transition: Idle -> TargetSelected -> Approaching -> ReceiverReady -> Crossing -> Settling -> Complete
/// (+ Hold / Cancel / Recover). Owns the shared crossing clock and the ownership hand-over, watches renderer heartbeats,
/// window/monitor changes and disconnects, and records measured timing errors for every transition.
/// </summary>
public sealed class Coordinator
{
    readonly Core _core;
    public readonly TransitionMachine Machine = new();
    public TransitionInfo Info => Machine.Info;

    Vector3 _mrFootLocal = new(0, -0.25f, -0.22f); Vector3 _worldUpLocal = Vector3.UnitY;
    float _pcFloorPx; bool _pcFloorKnown;
    float _tauAllThrough;
    double _stateSince, _lastBeatPc, _lastBeatMr, _crossStartedAt;
    double _maxTauErrPc, _maxTauErrMr; int _beatsPc, _beatsMr;
    bool _frozen; float _slowRate = 1f;
    int _loopRemaining; float _loopPx, _loopPy; double _loopNextAt; int _loopDone, _loopFailed;
    readonly List<object> _stateTrace = new();

    public Coordinator(Core core) { _core = core; }

    public void RestoreAfterRestart()
    {
        Machine.Info.owner = _core.State.character.owner;
        Machine.Info.characterId = _core.State.character.characterId;
    }

    string Owner => _core.State.character.owner;

    public void BroadcastTransition()
    {
        _core.Broadcast(Msg.TransitionUpdate, Info);
        // (a step of the crossing: its snapshot follows at once, as it always has, not at the four-a-second pace of everything else)
        _core.MarkDirty(false, urgent: true);
    }

    void Trace(string what, string reason = "")
    {
        _stateSince = HostClock.Now;
        _stateTrace.Add(new { t = HostClock.Now, state = Info.state, what, reason });
        Log.Info($"transition {Info.transitionId} dir={Info.direction} -> {Info.state} owner={Info.owner} ({what}{(reason.Length > 0 ? ": " + reason : "")})");
    }

    // ------------------------------------------------------------------ requests
    public void OnRequest(Client c, TransitionRequest req)
    {
        string fail = null;
        var tgt = req.target;
        if (Machine.IsActive) fail = "another transition is in progress";
        else if (tgt == null || !tgt.valid) fail = "target is not valid";
        else if (HostClock.Now - tgt.resolvedAt > 20) fail = "target is stale; point again";
        else if (!_core.HasPc) fail = "the Windows character client is not running";
        else if (!_core.HasMr) fail = "no MR client is connected";
        else if ((req.direction == 0 ? Env.Mr : Env.Pc) != Owner) fail = $"the partner is currently in {Owner}";
        else if (_core.Pc != null && _core.Pc.suspendedReason.StartsWith("secure desktop")) fail = "the desktop is showing protected UI: " + _core.Pc.suspendedReason;
        else if (req.direction == 0 && tgt.anchorKind == "window" && (!Win32.IsWindow(new IntPtr(tgt.hwnd)) || Win32.IsIconic(new IntPtr(tgt.hwnd)))) fail = "that window is no longer visible";
        if (fail != null)
        {
            Log.Warn("transition request rejected: " + fail);
            _core.Send(c, Msg.TransitionUpdate, new TransitionInfo { transitionId = "rejected-" + req.requestId, state = nameof(TState.Cancelled), reason = fail, owner = Owner, direction = req.direction });
            _core.Say(RejectLine(fail), "worried", 4, fail);
            return;
        }
        _mrFootLocal = req.mrFootLocal; _worldUpLocal = req.worldUpLocal;
        _core.State.lastMrFootLocal = req.mrFootLocal; _core.State.lastWorldUpLocal = req.worldUpLocal;
        _pcFloorPx = tgt.floorPxY; _pcFloorKnown = false;
        string id = "tr-" + Guid.NewGuid().ToString("N")[..10];
        if (!Machine.Begin(id, _core.State.character.characterId, req.direction, Owner, tgt, HostClock.Now)) return;
        _stateTrace.Clear(); _maxTauErrMr = _maxTauErrPc = 0; _beatsMr = _beatsPc = 0; _frozen = false;
        Info.plan = BuildPlan();
        Trace("request accepted");
        Machine.Apply(TEvent.BeginApproach, id, HostClock.Now);
        Trace("approach begins");
        BroadcastTransition();
    }

    static string RejectLine(string reason) => "I can't go right now: " + reason;

    CrossingPlan BuildPlan()
    {
        var tgt = Info.target;
        float H = _core.State.character.heightM;
        float mpp = tgt.metersPerPixel > 0 ? tgt.metersPerPixel : ScreenMath.DefaultMetersPerPixel;
        float pcFootY = -(_pcFloorPx - tgt.pxY) * mpp;                     // floor is below the portal in pixels => negative metres
        float bottomEdge = (tgt.bottomEdgePxY - tgt.pxY) * mpp;
        // room above the portal: to the top of the lit area, or to the top of the workspace window the partner is entering
        float topPx = tgt.anchorKind == "window" && tgt.windowRect.h > 0 ? Math.Max(tgt.topEdgePxY, tgt.windowRect.y) : tgt.topEdgePxY;
        float headroom = (tgt.pxY - topPx) * mpp;
        var plan = PlanBuilder.Build(Info.transitionId, Info.characterId, Info.direction, H, _mrFootLocal, _worldUpLocal, pcFootY, bottomEdge, headroom);
        plan.monitorId = tgt.monitorId; plan.portalPxX = tgt.pxX; plan.portalPxY = tgt.pxY;
        return plan;
    }

    public void OnEvent(Client c, TransitionEventMsg e)
    {
        if (e.transitionId != Info.transitionId) return; // stale or duplicate event for an old transition
        double now = HostClock.Now;
        bool fromPc = c.Role == Roles.Pc;
        switch (e.kind)
        {
            case "renderBeat":
            {
                double err = Info.clock != null && Info.plan != null ? Math.Abs(e.tau - Info.clock.TauAt(now, Info.plan.Total)) : 0;
                if (fromPc) { _lastBeatPc = now; _beatsPc++; if (Machine.State == TState.Crossing && !_frozen) _maxTauErrPc = Math.Max(_maxTauErrPc, err); }
                else { _lastBeatMr = now; _beatsMr++; if (Machine.State == TState.Crossing && !_frozen) _maxTauErrMr = Math.Max(_maxTauErrMr, err); }
                return;
            }
            case "approachDone":
                // the source reports where it REALLY stands so the crossing starts from there without a root pop
                if (Info.direction == 0 && !fromPc) { _mrFootLocal = e.vec; }
                if (Info.direction == 1 && fromPc) { _pcFloorPx = e.tau; _pcFloorKnown = true; }
                if (Machine.Apply(TEvent.ApproachDone, e.transitionId, now)) { Trace("approach done", fromPc ? "pc" : "mr"); MaybeStart(now); BroadcastTransition(); }
                break;
            case "receiverReady":
                if (Info.direction == 0 && fromPc) { _pcFloorPx = e.tau; _pcFloorKnown = true; }
                if (Info.direction == 1 && !fromPc) { _mrFootLocal = e.vec; }
                if (Machine.Apply(TEvent.ReceiverReady, e.transitionId, now)) { Trace("receiver ready", fromPc ? "pc" : "mr"); MaybeStart(now); BroadcastTransition(); }
                break;
            case "hold":
                if (Machine.State != TState.Crossing && Machine.Apply(TEvent.Hold, e.transitionId, now, e.reason)) { Trace("hold", e.reason); BroadcastTransition(); _core.Say("Hold on, I'll wait. " + e.reason, "worried", 4, e.reason); }
                break;
            case "resume":
                if (Machine.Apply(TEvent.Resume, e.transitionId, now, e.reason)) { Trace("resume", e.reason); MaybeStart(now); BroadcastTransition(); }
                break;
            case "cancel":
                Cancel(e.reason.Length > 0 ? e.reason : "cancelled by " + c.Role);
                break;
        }
    }

    void MaybeStart(double now)
    {
        if (Machine.State != TState.ReceiverReady) return;
        if (!_core.HasPc || !_core.HasMr) return;
        // final plan from the real positions both sides just reported
        Info.plan = BuildPlan();
        _tauAllThrough = PlanEvaluator.TauForProgress(Info.plan, 0.999f) + 0.03f;
        Info.clock = new CrossClock { transitionId = Info.transitionId, anchorHost = now + 0.12, anchorTau = 0, rate = _slowRate, revision = 1 };
        if (_pendingFreeze >= 0) { Info.clock.hasStop = true; Info.clock.stopTau = PlanEvaluator.TauForProgress(Info.plan, _pendingFreeze); _frozen = true; }
        if (Machine.Apply(TEvent.StartCrossing, Info.transitionId, now))
        {
            _crossStartedAt = now; _lastBeatPc = _lastBeatMr = now + 0.30;
            Trace("crossing scheduled", $"variant={(CrossVariant)Info.plan.variant} total={Info.plan.Total:0.00}s allThrough={_tauAllThrough:0.00}s slope={Info.plan.slope:0.00}");
        }
    }

    public void CancelByUser(string reason) { if (Machine.IsActive) Cancel(reason); }

    void Cancel(string reason)
    {
        double now = HostClock.Now;
        var before = Machine.State;
        if (!Machine.Apply(TEvent.Cancel, Info.transitionId, now, reason)) return;
        if (Machine.State == TState.Recovering && Info.clock != null && Info.plan != null)
        {
            // mid-crossing: rewind the shared clock so the body retreats the way it came and ends up whole on the source side
            float tau = Info.clock.TauAt(now, Info.plan.Total);
            Info.clock = new CrossClock { transitionId = Info.transitionId, anchorHost = now, anchorTau = tau, rate = -1.25f, revision = Info.clock.revision + 1 };
        }
        Trace($"cancel from {before}", reason);
        BroadcastTransition();
        if (Machine.State == TState.Cancelled) Finish(false, reason);
    }

    void Finish(bool completed, string reason)
    {
        _core.State.character.owner = Info.owner;
        var rec = new
        {
            t = DateTime.Now.ToString("s"), Info.transitionId, Info.direction, result = completed ? "Complete" : "Cancelled", reason, owner = Info.owner,
            variant = Info.plan != null ? ((CrossVariant)Info.plan.variant).ToString() : "", monitor = Info.target?.monitorId, px = Info.target?.pxX, py = Info.target?.pxY,
            anchor = Info.target?.anchorKind, window = Info.target?.windowTitle,
            planTotal = Info.plan?.Total ?? 0, wallSeconds = HostClock.Now - Info.startedAt,
            maxTauErrPcMs = _maxTauErrPc * 1000, maxTauErrMrMs = _maxTauErrMr * 1000, beatsPc = _beatsPc, beatsMr = _beatsMr,
            mrSimulated = _core.Mr?.simulated ?? false, trace = _stateTrace.ToArray(),
        };
        Log.Jsonl(HostPaths.TransitionLog, rec);
        Log.Info($"transition {Info.transitionId} finished: {(completed ? "Complete" : "Cancelled")} owner={Info.owner} tauErr pc={_maxTauErrPc * 1000:0.0}ms mr={_maxTauErrMr * 1000:0.0}ms beats pc={_beatsPc} mr={_beatsMr}");
        _core.MarkDirty();
        if (_loopRemaining > 0)
        {
            if (completed) _loopDone++; else _loopFailed++;
            _loopRemaining--;
            _loopNextAt = HostClock.Now + 1.2;
            if (_loopRemaining == 0) { Log.Info($"debug loop finished: completed={_loopDone} failed={_loopFailed}"); _core.Broadcast(Msg.DebugReport, new { loopCompleted = _loopDone, loopFailed = _loopFailed }); }
        }
        _core.Tasks.OnTransitionFinished(Info, completed);
        _core.AfterTransition(Info, completed);       // (on the way to the desktop: now the PC client is told to walk it)
    }

    // ------------------------------------------------------------------ tick / watchdogs
    public void Tick(double now)
    {
        var s = Machine.State;
        string id = Info.transitionId;
        if (s == TState.Crossing && Info.clock != null && Info.plan != null)
        {
            float tau = Info.clock.TauAt(now, Info.plan.Total);
            if (!_frozen)
            {
                // A disconnected renderer is gone for certain and is acted on at once. A missing heartbeat only counts
                // after a short grace period following a (re)start of the clock.
                bool grace = now <= Info.clock.anchorHost + 0.6;
                bool pcStale = !_core.HasPc || (!grace && now - _lastBeatPc > 0.7), mrStale = !_core.HasMr || (!grace && now - _lastBeatMr > 0.7);
                string dst = Info.destination;
                bool dstStale = dst == Env.Pc ? pcStale : mrStale, srcStale = dst == Env.Pc ? mrStale : pcStale;
                if (dstStale && tau < _tauAllThrough)
                {
                    // the receiving renderer stopped drawing: never leave the body half-shown, pull it back to the source side
                    Cancel($"{dst} renderer stopped responding during the crossing");
                    return;
                }
                if (srcStale && tau < Info.plan.ThreadStart)
                {
                    Cancel("source renderer stopped responding before the body reached the portal");
                    return;
                }
                // source lost after the body entered the portal: the destination finishes alone (it will own the whole body)
            }
            if (tau >= _tauAllThrough && Machine.Apply(TEvent.CrossingFinished, id, now)) { Trace("last foot through; ownership moved"); _core.State.character.owner = Info.owner; BroadcastTransition(); }
        }
        else if (s == TState.Settling && Info.clock != null && Info.plan != null)
        {
            if (Info.clock.TauAt(now, Info.plan.Total) >= Info.plan.Total - 1e-3 || now - _stateSince > 6)
            {
                if (Machine.Apply(TEvent.Settled, id, now)) { Trace("settled"); BroadcastTransition(); Finish(true, ""); }
            }
        }
        else if (s == TState.Recovering && Info.clock != null && Info.plan != null)
        {
            if (Info.clock.TauAt(now, Info.plan.Total) <= 1e-3 || now - _stateSince > 6)
            {
                if (Machine.Apply(TEvent.RecoverDone, id, now)) { Trace("recovered to source"); BroadcastTransition(); Finish(false, Info.reason); }
            }
        }
        else if (s == TState.Approaching || s == TState.TargetSelected || s == TState.ReceiverReady)
        {
            if (now - _stateSince > 45) Cancel("approach timed out");
            else if (s == TState.ReceiverReady) MaybeStartAndBroadcast(now);
            else WatchTargetWindow(now);
        }
        else if (s == TState.Hold)
        {
            if (now - _stateSince > 30) Cancel("held too long: " + Info.reason);
        }

        if (_loopRemaining > 0 && !Machine.IsActive && now >= _loopNextAt && _core.HasMr && _core.HasPc)
        {
            _loopNextAt = now + 8;
            bool enter = Owner == Env.Mr;
            _core.Broadcast(Msg.Debug, new DebugCommand { cmd = enter ? "enter" : "return", px = _loopPx, py = _loopPy }, c => _core.IsMr(c));
        }
    }

    void MaybeStartAndBroadcast(double now)
    {
        MaybeStart(now);
        if (Machine.State == TState.Crossing) BroadcastTransition();
    }

    double _lastWindowCheck;
    void WatchTargetWindow(double now)
    {
        if (now - _lastWindowCheck < 0.1) return;
        _lastWindowCheck = now;
        var tgt = Info.target;
        if (tgt == null || tgt.anchorKind != "window" || Info.direction != 0) return;
        var hwnd = new IntPtr(tgt.hwnd);
        if (!Win32.IsWindow(hwnd)) { Cancel("the target window was closed"); return; }
        if (Win32.IsIconic(hwnd)) return; // the PC client reports hold/resume for minimise/restore
        if (!Win32.FrameBounds(hwnd, out RectI r)) return;
        if (r.x == tgt.windowRect.x && r.y == tgt.windowRect.y && r.w == tgt.windowRect.w && r.h == tgt.windowRect.h) return;
        // the window moved: a window-anchored portal follows it (same relative point), the MR side re-routes to the new place
        var q = new TargetQuery { queryId = tgt.queryId, usePixel = true, px = r.x + tgt.winU * r.w, py = r.y + tgt.winV * r.h, direction = 0 };
        var nt = _core.Desktop.Resolve(q, enterWindow: true);
        if (!nt.valid) { Cancel("the target window moved somewhere the partner cannot enter: " + nt.reason); return; }
        if (nt.hwnd != tgt.hwnd) { Cancel("the target window is now covered by another window"); return; }
        Info.target = nt; Info.approachDone = false; Info.receiverReady = false;
        _pcFloorPx = nt.floorPxY;
        Info.plan = BuildPlan(); Info.revision++; Info.updatedAt = now;
        Trace("target window moved; portal re-anchored", $"px=({nt.pxX:0},{nt.pxY:0}) monitor={nt.monitorId}");
        BroadcastTransition();
    }

    public void OnPcStatus() { }

    public void OnClientJoined(string role)
    {
        // A reconnecting client gets the authoritative state in the snapshot; nothing is restarted or duplicated.
        // A transition that was only held because that client dropped continues from where it was held.
        if (Machine.State == TState.Hold && Info.reason.EndsWith("disconnected") && _core.HasPc && _core.HasMr)
        {
            // the side that came back re-confirms with its real, current position before anything moves through the portal
            bool mrBack = role is Roles.Quest or Roles.Sim;
            bool sourceBack = (Info.source == Env.Mr) == mrBack;
            if (sourceBack) Info.approachDone = false; else Info.receiverReady = false;
            if (Machine.Apply(TEvent.Resume, Info.transitionId, HostClock.Now, role + " reconnected")) { Trace("resume", role + " reconnected"); BroadcastTransition(); }
        }
    }

    public void OnClientLost(string role)
    {
        if (!Machine.IsActive) return;
        var s = Machine.State;
        bool mr = role is Roles.Quest or Roles.Sim;
        if (mr && _core.HasMr) return;
        if (!mr && role != Roles.Pc) return;
        if (s == TState.Approaching || s == TState.TargetSelected || s == TState.ReceiverReady)
        {
            if (Machine.Apply(TEvent.Hold, Info.transitionId, HostClock.Now, role + " disconnected")) { Trace("hold", role + " disconnected"); BroadcastTransition(); }
        }
        // during Crossing the heartbeat watchdog in Tick decides (rewind or let the destination finish)
    }

    // ------------------------------------------------------------------ portal debug mode
    public void OnDebug(DebugCommand d)
    {
        double now = HostClock.Now;
        switch (d.cmd)
        {
            case "loop":
                _loopRemaining = Math.Max(1, (int)d.value) ; _loopPx = d.px; _loopPy = d.py; _loopNextAt = now; _loopDone = _loopFailed = 0;
                Log.Info($"debug loop: {_loopRemaining} crossings via px=({d.px},{d.py})");
                break;
            case "stopLoop": _loopRemaining = 0; break;
            case "slow": _slowRate = d.value <= 0 ? 1f : Math.Clamp(d.value, 0.05f, 2f); Reanchor(now, _slowRate, false, 0); break;
            case "freeze":
                if (Info.plan != null && Machine.State == TState.Crossing)
                {
                    float stop = PlanEvaluator.TauForProgress(Info.plan, Math.Clamp(d.value, 0f, 1f));
                    _frozen = true; Reanchor(now, stop >= Info.clock.TauAt(now, Info.plan.Total) ? Math.Abs(_slowRate) : -Math.Abs(_slowRate), true, stop);
                }
                else _pendingFreeze = Math.Clamp(d.value, 0f, 1f);
                break;
            case "play": _frozen = false; _pendingFreeze = -1; Reanchor(now, _slowRate, false, 0); break;
            case "rewind": Cancel("debug rewind"); break;
        }
    }
    float _pendingFreeze = -1;

    void Reanchor(double now, float rate, bool hasStop, float stopTau)
    {
        if (Info.clock == null || Info.plan == null || Machine.State != TState.Crossing) return;
        float tau = Info.clock.TauAt(now, Info.plan.Total);
        Info.clock = new CrossClock { transitionId = Info.transitionId, anchorHost = now, anchorTau = tau, rate = rate, hasStop = hasStop, stopTau = stopTau, revision = Info.clock.revision + 1 };
        Info.revision++; Info.updatedAt = now;
        _lastBeatPc = _lastBeatMr = now;
        BroadcastTransition();
    }
}
