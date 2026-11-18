using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// Shows: the partner visibly doing in the PC what was asked in the room (the snapshot's `show`, Shared/Glass.cs).
///   look     it reads a part of a display (Look.cs): the PC client frames that rectangle on the real screen
///   publish  a thing was put up in the marketplace: a little stall WHERE THE PARTNER IS and the price tag - on the
///            desk the headset draws it, inside the PC the PC client does; the partner does not cross a screen for
///            it (PublishShow in ShowRules.cs is the stage machine)
///   deliver  a thing that was bought or installed arrives: a parcel comes out of a screen to the desk
/// All of it is for the eyes only. The publishing, the installing and the reading have happened (or happen) on their
/// own; a show never holds them up, and when a show cannot be played - or is cut short - nothing else is affected.
/// Ticked from the host's loop on the core thread.
/// </summary>
public sealed class Shows
{
    readonly Core _core;
    readonly ShowBoard _board = new();
    readonly PublishShow _pub = new();
    HashSet<string> _installed;         // the modules that were installed when last looked (a new one that is a thing: a parcel)
    double _nextWatch;

    public Shows(Core core) { _core = core; }

    /// <summary>What the snapshot carries.</summary>
    public ShowInfo Info => _board.Info;
    public object Diagnostics => new { kind = Info.kind, stage = Info.stage, seq = Info.seq, monitor = Info.monitorId, name = Info.name };

    bool Ko => _core.Talk.LastLanguage == "Korean";
    Client MrClient => _core.Clients.Values.FirstOrDefault(c => c.Authed && _core.IsMr(c));

    /// <summary>The displays the headset that is connected knows the place of (a simulator's calibrations are its own).</summary>
    internal List<MonitorDesc> Calibrated()
    {
        bool sim = _core.Mr != null && _core.Mr.simulated;
        return _core.Desktop.Monitors.Where(m => _core.State.calibrations.Any(k => k.monitorId == m.id && k.simulated == sim && k.pixelW == m.bounds.w && k.pixelH == m.bounds.h)).ToList();
    }

    ShowWorld World() => new() { owner = _core.State.character.owner, hasPc = _core.HasPc, hasMr = _core.HasMr, crossing = _core.Coord.Machine.IsActive };

    /// <summary>
    /// The display a show is about when the partner is not inside one: the one it last went through, when the headset
    /// still knows where that is; otherwise the first display it knows. ("" when it knows none.)
    /// </summary>
    string NearestMonitor()
    {
        var known = Calibrated();
        if (_core.State.character.owner == Env.Pc && _core.Pc != null && ScreenMath.FindMonitor(_core.Desktop.Monitors, _core.Pc.monitorId) != null) return _core.Pc.monitorId;
        string last = _core.Coord.Info.target?.monitorId ?? "";
        if (known.Any(m => m.id == last)) return last;
        return known.FirstOrDefault()?.id ?? "";
    }

    void Changed() { if (_board.Refresh(HostClock.Now)) _core.MarkDirty(false, urgent: true); }

    // ------------------------------------------------------------------ look
    /// <summary>The partner starts reading that rectangle of a display (fractions, from its top-left). Returns the look's number.</summary>
    public int LookBegin(string monitorId, float u, float v, float w, float h)
    {
        int id = _board.LookBegin(HostClock.Now, monitorId, u, v, w, h);
        Changed();
        return id;
    }

    /// <summary>The answer was said, or the look came to nothing.</summary>
    public void LookEnd(int id) { _board.LookEnd(id); Changed(); }

    // ------------------------------------------------------------------ file (H6: a document carried into the PC, Docs.cs)
    /// <summary>The partner carries a document: stage going | shown (ShowBoard.File). The display: the one it is in, else the one it goes through.</summary>
    public void File(string stage, string name, string icon, double since, float seconds)
    {
        _board.File(HostClock.Now, stage, name, icon, NearestMonitor(), since, seconds);
        Changed();
    }

    public void FileEnd() { _board.FileEnd(); Changed(); }

    // ------------------------------------------------------------------ operate (round 4: the partner works on the computer, PcOperate.cs)
    /// <summary>A step of operating the computer: the display, the spot it is about to click (hasSpot), the step's short English line, the stage (look | click | type | scroll | done).</summary>
    public void Operate(string monitorId, bool hasSpot, float u, float v, string name, string stage)
    {
        _board.Operate(HostClock.Now, monitorId, hasSpot, u, v, name, stage);
        Changed();
    }

    public void OperateEnd() { _board.OperateEnd(); Changed(); }

    // ------------------------------------------------------------------ deliver
    /// <summary>A thing arrived (bought, got or installed): for three seconds a parcel comes out of a screen.</summary>
    public void Deliver(string name, string icon)
    {
        string monitor = NearestMonitor();
        Log.Info($"show: deliver '{name}' out of {(monitor.Length > 0 ? monitor : "(no display known)")}");
        _board.Deliver(HostClock.Now, monitor, name, icon);
        Changed();
    }

    // ------------------------------------------------------------------ publish
    /// <summary>
    /// A thing of the user's has just been put up in the marketplace (Market.Publish, after the hub said yes).
    /// true: the show is played where the partner is, and the partner says "올렸어! ..." in it; false: nothing can be
    /// shown just now, and the caller says its line as before.
    /// </summary>
    public bool Publish(string id, string name, int price)
    {
        double now = HostClock.Now;
        string icon = _core.Shop.View.list.FirstOrDefault(l => l.id == id)?.icon ?? "";
        var act = _pub.Begin(now, World(), name, icon, price);
        if (!_pub.Running) return false;
        Log.Info($"show: publish '{name}' ({id}, {price} stars): the stall {(_pub.side == Env.Pc ? "inside the PC, where the partner is" : "on the desk, where the partner is")}");
        Apply(act, now);
        if (_pub.Running) _core.Say(PublishShow.StartLine(Ko), "happy", 3, "world: publish show");
        return true;
    }

    /// <summary>An intent that moves the partner (go in, come out, cancel) from the user: a show that needs it where it stands is over.</summary>
    public void UserMoved(string kind)
    {
        if (!_pub.Running || !(kind is "enterPortal" or "returnToMr" or "cancel" or "stop")) return;
        Log.Info($"show: publish cut short at '{_pub.stage}' (the user asked for {kind})");
        Apply(_pub.Cancel(), HostClock.Now);
    }

    void Apply(ShowAct act, double now)
    {
        if (act == ShowAct.None) return;
        if (act.HasFlag(ShowAct.Changed) && _pub.Running) Log.Info($"show: publish -> {_pub.stage}");
        if (act.HasFlag(ShowAct.SayListed)) _core.Say(PublishShow.ListedLine(_pub.name, _pub.price, Ko), "happy", 4, "world: publish show");
        if (act.HasFlag(ShowAct.Ended)) Log.Info("show: publish over");
        if (act.HasFlag(ShowAct.Changed) || act.HasFlag(ShowAct.Ended))
        {
            // the display the stall is on: the one the partner is in. On the desk there is none - the headset draws the stall beside the partner.
            string monitor = _pub.Running && _pub.side == Env.Pc ? NearestMonitor() : "";
            _board.Publish(now, _pub, monitor);
            Changed();
        }
    }

    /// <summary>The source an intent carries when a show asks for it (not the user). No show sends the partner anywhere any more; kept for Core.ExecuteIntent.</summary>
    public const string ShowSource = "show";

    public void Tick(double now)
    {
        if (_pub.Running) Apply(_pub.Tick(now, World()), now);
        if (now >= _nextWatch)
        {
            _nextWatch = now + 0.25;
            WatchInstalls();
            Changed();          // (a look or a parcel whose time is up)
        }
    }

    /// <summary>
    /// A module that has just become installed and is a THING (it is in the bag now): a parcel. Seen from the
    /// marketplace's books rather than told by it - whoever installed it (the headset's shop board, the voice, the
    /// page, something paid for earlier and fetched now). What was installed when the host started is no news.
    /// </summary>
    void WatchInstalls()
    {
        var now = _core.State.market?.installed;
        if (now == null) return;
        if (_installed == null) { _installed = new HashSet<string>(now); return; }
        if (now.Count == _installed.Count && now.All(_installed.Contains)) return;
        foreach (string id in now)
        {
            if (_installed.Contains(id)) continue;
            var item = _core.State.world.items.FirstOrDefault(i => i.id == id);
            if (item != null) Deliver(item.name, item.icon);
        }
        _installed = new HashSet<string>(now);
    }
}
