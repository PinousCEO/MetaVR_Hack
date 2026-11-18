using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>What a show sees of the world each time it is ticked (the host fills it in; a test makes it up).</summary>
public sealed class ShowWorld
{
    public string owner = Env.Mr;       // where the partner is
    public bool hasPc, hasMr;           // a PC client / a headset (or the simulator) is connected
    public bool crossing;               // a transition through a screen is under way
}

/// <summary>What the host has to do after a step of a show.</summary>
[Flags]
public enum ShowAct
{
    None = 0,
    SayListed = 4,      // the partner says that the thing is up, with its price
    Changed = 8,        // the stage changed: the clients are told
    Ended = 16,         // the show is over
}

/// <summary>
/// The show of putting a thing up in the marketplace - purely visual, the publishing itself has already happened.
/// It is played WHERE THE PARTNER IS ("just do it where it is; if the order was received inside the PC, do it
/// there" - the user, 2026-11-17): on the desk the headset draws the stall, inside the PC the PC client does. The
/// partner never crosses a screen for it. Two stages:
///   stall   a little market stall where it stands, the thing goes on the shelf          (StallSeconds)
///   listed  the price tag, and the partner says that it is up                           (ListedSeconds)
/// A small timed state machine, ticked from the host's loop with the time given to it (so that a test can run it on
/// a clock of its own). Whatever cuts it short - the partner is sent through a screen, the side that draws it goes
/// away - ends the show quietly, and the line is still said once.
/// </summary>
public sealed class PublishShow
{
    public const string Stall = "stall", Listed = "listed";
    public const double StallSeconds = 4, ListedSeconds = 4.5;

    public string stage = "";           // "" = no show
    public string name = "", icon = "";
    public int price;
    public string side = "";            // where it is played: Env.Mr (on the desk, drawn by the headset) | Env.Pc (inside the PC, drawn by the PC client)
    public bool said;                   // the line has been said
    public double since;                // when the stage began

    public bool Running => stage.Length > 0;

    /// <summary>How long the stage that is on lasts.</summary>
    public double StageSeconds => stage switch { Stall => StallSeconds, Listed => ListedSeconds, _ => 0 };

    ShowAct To(string next, double now) { stage = next; since = now; return ShowAct.Changed; }

    ShowAct End()
    {
        if (!Running) return ShowAct.None;
        stage = "";
        var act = ShowAct.Changed | ShowAct.Ended;
        if (!said) { said = true; act |= ShowAct.SayListed; }      // (whatever cut the show short, the user still hears that it is up)
        return act;
    }

    /// <summary>Is the side that draws the show there, with the partner on it?</summary>
    bool There(ShowWorld w) => side == Env.Pc ? w.hasPc && w.owner == Env.Pc : w.hasMr && w.owner != Env.Pc;

    /// <summary>
    /// A thing was just published. The show starts where the partner is - at "stall", at once. Returns None and stays
    /// off when nobody can draw it there (the partner on the desk and no headset; inside the PC and no PC client),
    /// while it is on its way through a screen, or while another publish show is on: then the caller says its line as before.
    /// </summary>
    public ShowAct Begin(double now, ShowWorld w, string name, string icon, int price)
    {
        if (Running || w == null || w.crossing) return ShowAct.None;
        string where = w.owner == Env.Pc ? Env.Pc : Env.Mr;
        if (where == Env.Pc ? !w.hasPc : !w.hasMr) return ShowAct.None;
        this.name = name ?? ""; this.icon = icon ?? ""; this.price = Math.Max(0, price);
        said = false; side = where;
        return To(Stall, now);
    }

    public ShowAct Tick(double now, ShowWorld w)
    {
        if (!Running) return ShowAct.None;
        if (w.crossing || !There(w)) return End();                  // it was sent somewhere else in the middle, or nobody draws it any more
        switch (stage)
        {
            case Stall:
                if (now - since >= StallSeconds) { said = true; return To(Listed, now) | ShowAct.SayListed; }
                return ShowAct.None;
            case Listed:
                return now - since >= ListedSeconds ? End() : ShowAct.None;
        }
        return End();
    }

    /// <summary>The user sent the partner somewhere (or stopped everything): the show is over, quietly.</summary>
    public ShowAct Cancel() => End();

    public static string ListedLine(string name, int price, bool korean) =>
        price > 0 ? (korean ? $"올렸어! {name}, 별 {price}개!" : $"It's up! {name}, {price} stars!")
                  : (korean ? $"올렸어! {name}, 누구나 무료로 받아 갈 수 있어!" : $"It's up! {name}, free for anyone!");

    /// <summary>What it says as the stall comes up.</summary>
    public static string StartLine(bool korean) => korean ? "올릴게!" : "Putting it up!";
}

/// <summary>
/// What the snapshot's `show` says, out of the shows that may be on at once: reading a part of a display goes before
/// a parcel arriving, and that before the market stall (which is back when the shorter ones are over).
/// Every show has a number of its own (seq); the stages of one publish show keep it. "Nothing" is a show too: kind ""
/// under a new number.
/// </summary>
public sealed class ShowBoard
{
    public readonly ShowInfo Info = new();
    ShowInfo _look, _deliver, _publish, _file;
    double _lookUntil, _deliverUntil;
    int _seq; long _stamp, _shown = -1;
    ShowInfo _top;

    public const float LookSeconds = 8, DeliverSeconds = 3;

    public bool Looking => _look != null;

    /// <summary>The partner reads that rectangle (fractions of the display, from its top-left). Returns the look's number (for LookEnd).</summary>
    public int LookBegin(double now, string monitorId, float u, float v, float w, float h)
    {
        _look = new ShowInfo { kind = "look", seq = ++_seq, monitorId = monitorId ?? "", u = u, v = v, w = w, h = h, startedAt = now, seconds = LookSeconds };
        _lookUntil = now + LookSeconds; _stamp++;
        return _look.seq;
    }

    /// <summary>The answer was said (or the look came to nothing).</summary>
    public void LookEnd(int seq) { if (_look != null && (seq == 0 || _look.seq == seq)) { _look = null; _stamp++; } }

    public void Deliver(double now, string monitorId, string name, string icon)
    {
        _deliver = new ShowInfo { kind = "deliver", seq = ++_seq, monitorId = monitorId ?? "", name = name ?? "", icon = icon ?? "", startedAt = now, seconds = DeliverSeconds };
        _deliverUntil = now + DeliverSeconds; _stamp++;
    }

    /// <summary>The publish show as it stands (null: it is over). monitorId: the display the partner is in when it is inside the PC ("" on the desk).</summary>
    public void Publish(double now, PublishShow p, string monitorId)
    {
        if (p == null || !p.Running) { if (_publish != null) { _publish = null; _stamp++; } return; }
        _publish ??= new ShowInfo { kind = "publish", seq = ++_seq };
        _publish.stage = p.stage; _publish.name = p.name; _publish.icon = p.icon; _publish.price = p.price;
        _publish.monitorId = monitorId ?? ""; _publish.startedAt = p.since; _publish.seconds = (float)p.StageSeconds;
        _stamp++;
    }

    /// <summary>
    /// (H6) The partner carries a document into the PC (DeliverShow, Docs.cs): kind "file", stage going (it crosses the
    /// screen with the sheet in its hands) | shown (it holds it up inside the PC, to be clicked). name: the file's name;
    /// icon: the picture of its first page, "/docs/&lt;id&gt;/page-1-s.png". One show keeps its number over both stages.
    /// </summary>
    public void File(double now, string stage, string name, string icon, string monitorId, double since, float seconds)
    {
        _file ??= new ShowInfo { kind = "file", seq = ++_seq };
        _file.stage = stage ?? ""; _file.name = name ?? ""; _file.icon = icon ?? ""; _file.monitorId = monitorId ?? ""; _file.startedAt = since; _file.seconds = seconds;
        _stamp++;
    }

    public void FileEnd() { if (_file != null) { _file = null; _stamp++; } }

    /// <summary>
    /// (Round 4) The partner OPERATES the computer (PcOperate.cs): kind "operate". One show keeps its number over all
    /// the steps of one run; every step changes startedAt, so that the clients are told. monitorId, u, v: the spot it
    /// is about to click or scroll at (hasSpot false: the spot of the step before stays); name: the step's short
    /// English line; stage: look | click | type | scroll | done.
    /// </summary>
    public void Operate(double now, string monitorId, bool hasSpot, float u, float v, string name, string stage)
    {
        _operate ??= new ShowInfo { kind = "operate", seq = ++_seq, u = 0.5f, v = 0.5f };
        if (!string.IsNullOrEmpty(monitorId)) _operate.monitorId = monitorId;
        if (hasSpot) { _operate.u = u; _operate.v = v; }
        _operate.name = name ?? ""; _operate.stage = stage ?? ""; _operate.startedAt = now; _operate.seconds = 60;
        _operateUntil = now + 75; _stamp++;
    }

    public void OperateEnd() { if (_operate != null) { _operate = null; _stamp++; } }
    public bool Operating => _operate != null;
    ShowInfo _operate; double _operateUntil;

    /// <summary>Brings Info up to date. Returns whether it changed (then the clients get a snapshot).</summary>
    public bool Refresh(double now)
    {
        if (_look != null && now > _lookUntil) { _look = null; _stamp++; }
        if (_deliver != null && now > _deliverUntil) { _deliver = null; _stamp++; }
        if (_operate != null && now > _operateUntil) { _operate = null; _stamp++; }       // (a run that never said it was over)
        var top = _operate ?? _look ?? _file ?? _deliver ?? _publish;
        if (ReferenceEquals(top, _top) && _stamp == _shown) return false;
        bool same = ReferenceEquals(top, _top);
        _shown = _stamp;
        if (top == null)
        {
            _top = null;
            if (Info.kind.Length == 0) return false;
            Copy(new ShowInfo { kind = "", seq = ++_seq, startedAt = now, seconds = 0 });
            return true;
        }
        // (the same show, and nothing of it changed - another layer underneath did: nothing to tell)
        if (same && Info.stage == top.stage && Info.startedAt == top.startedAt && Info.monitorId == top.monitorId) return false;
        _top = top;
        Copy(top);
        return true;
    }

    void Copy(ShowInfo s)
    {
        Info.kind = s.kind; Info.seq = s.seq; Info.monitorId = s.monitorId; Info.u = s.u; Info.v = s.v; Info.w = s.w; Info.h = s.h;
        Info.name = s.name; Info.icon = s.icon; Info.price = s.price; Info.startedAt = s.startedAt; Info.seconds = s.seconds; Info.stage = s.stage;
    }
}
