using LivingPartner.Host;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// The show of putting a thing up in the marketplace, run on a clock of the test's own: its stages, their time
/// limits, what cuts it short - and what the snapshot's `show` says when several shows are on at once.
/// </summary>
public class ShowTests
{
    static ShowWorld Desk() => new() { owner = Env.Mr, hasPc = true, hasMr = true };
    static ShowWorld Inside() => new() { owner = Env.Pc, hasPc = true, hasMr = true };

    /// <summary>Ticks the show every 50 ms from `from` for `seconds`; returns everything it asked for on the way, and the time it stopped at.</summary>
    static (ShowAct acts, double now) Run(PublishShow p, ShowWorld w, double from, double seconds, Func<PublishShow, bool> until = null)
    {
        ShowAct all = ShowAct.None; double now = from;
        for (double t = 0; t <= seconds + 1e-9; t += 0.05)
        {
            now = from + t;
            all |= p.Tick(now, w);
            if (until != null && until(p)) break;
        }
        return (all, now);
    }

    [Fact]
    public void On_the_desk_it_is_the_stall_and_the_tag_right_where_the_partner_is_and_nobody_crosses_a_screen()
    {
        // ("just do it where it is" - the user, 2026-11-17: the partner used to walk into the PC for this and back)
        var p = new PublishShow(); var w = Desk();
        var act = p.Begin(1000, w, "Chair", "item-1.png", 20);
        Assert.True(p.Running);
        Assert.Equal(ShowAct.Changed, act);                                 // nothing but the stage: nobody is sent anywhere
        Assert.Equal(PublishShow.Stall, p.stage);
        Assert.Equal(Env.Mr, p.side);
        Assert.Equal(PublishShow.StallSeconds, p.StageSeconds);
        Assert.Equal(1000, p.since, 6);

        // the stall stands for four seconds, then the price tag and the line - once
        Assert.Equal(ShowAct.None, Run(p, w, 1000, 3.9).acts);
        act = p.Tick(1000 + PublishShow.StallSeconds + 0.01, w);
        Assert.Equal(ShowAct.Changed | ShowAct.SayListed, act);
        Assert.Equal(PublishShow.Listed, p.stage);
        Assert.Equal(PublishShow.ListedSeconds, p.StageSeconds);
        Assert.True(p.said);

        // four and a half seconds later it is over; the line is not said a second time, and the partner is where it was
        double listed = p.since;
        Assert.Equal(ShowAct.None, Run(p, w, listed, 4.4).acts);
        act = p.Tick(listed + PublishShow.ListedSeconds + 0.01, w);
        Assert.Equal(ShowAct.Changed | ShowAct.Ended, act);
        Assert.False(p.Running);
        Assert.Equal(ShowAct.None, p.Tick(2000, w));
        Assert.Equal(Env.Mr, w.owner);
    }

    [Fact]
    public void Inside_the_pc_it_is_the_same_two_stages_there()
    {
        var p = new PublishShow(); var w = Inside();
        var act = p.Begin(50, w, "Dance", "", 0);
        Assert.Equal(ShowAct.Changed, act);
        Assert.Equal(PublishShow.Stall, p.stage);
        Assert.Equal(Env.Pc, p.side);
        var (acts, now) = Run(p, w, 50, 30, x => !x.Running);
        Assert.Equal(ShowAct.Changed | ShowAct.SayListed | ShowAct.Ended, acts);
        Assert.InRange(now - 50, PublishShow.StallSeconds + PublishShow.ListedSeconds - 0.01, PublishShow.StallSeconds + PublishShow.ListedSeconds + 0.11);
        Assert.Equal(Env.Pc, w.owner);
    }

    [Fact]
    public void There_are_two_stages_and_no_others()
    {
        // every stage a show ever has, on either side, with and without the other client: stall, then listed
        foreach (var w0 in new[] { Desk(), Inside(), new ShowWorld { owner = Env.Mr, hasMr = true }, new ShowWorld { owner = Env.Pc, hasPc = true } })
        {
            var p = new PublishShow(); var seen = new List<string>();
            p.Begin(0, w0, "Chair", "", 3);
            for (double t = 0; t < 20 && p.Running; t += 0.05) { if (seen.Count == 0 || seen[^1] != p.stage) seen.Add(p.stage); p.Tick(t, w0); }
            Assert.Equal(new[] { "stall", "listed" }, seen);
            Assert.False(p.Running);
        }
        Assert.Equal(0, new PublishShow().StageSeconds);
    }

    [Fact]
    public void With_nobody_to_draw_it_where_the_partner_is_no_show_begins()
    {
        foreach (var w in new[]
        {
            new ShowWorld { owner = Env.Mr, hasPc = true, hasMr = false },                 // on the desk, and no headset to draw the stall
            new ShowWorld { owner = Env.Pc, hasPc = false, hasMr = true },                 // inside the PC, and no PC client
            new ShowWorld { owner = Env.Mr, hasPc = true, hasMr = true, crossing = true }, // it is on its way through a screen
            new ShowWorld { owner = Env.Pc, hasPc = true, hasMr = true, crossing = true },
        })
        {
            var p = new PublishShow();
            Assert.Equal(ShowAct.None, p.Begin(10, w, "Chair", "", 5));
            Assert.False(p.Running);
            Assert.Equal(ShowAct.None, p.Tick(11, w));
        }
        Assert.Equal(ShowAct.None, new PublishShow().Begin(10, null, "Chair", "", 5));

        // the headset alone is enough on the desk (no PC client, no display it knows), the PC client alone inside
        var only = new PublishShow();
        Assert.Equal(ShowAct.Changed, only.Begin(10, new ShowWorld { owner = Env.Mr, hasMr = true }, "Chair", "", 5));
        only = new PublishShow();
        Assert.Equal(ShowAct.Changed, only.Begin(10, new ShowWorld { owner = Env.Pc, hasPc = true }, "Chair", "", 5));

        // one show at a time: a second thing published meanwhile gets its line the plain way
        var busy = new PublishShow();
        busy.Begin(10, Inside(), "Chair", "", 5);
        Assert.Equal(ShowAct.None, busy.Begin(11, Inside(), "Desk", "", 9));
        Assert.Equal("Chair", busy.name);
    }

    [Fact]
    public void A_stop_or_a_movement_command_cuts_the_show_short_at_any_stage()
    {
        // at the stall: the line is still said, once
        var p = new PublishShow(); var w = Desk();
        p.Begin(0, w, "Chair", "", 20);
        Run(p, w, 0, 2);
        Assert.Equal(ShowAct.Changed | ShowAct.Ended | ShowAct.SayListed, p.Cancel());
        Assert.False(p.Running);
        Assert.Equal(ShowAct.None, p.Cancel());                             // nothing left to cancel

        // after the line was said: only the end
        p = new PublishShow(); w = Inside();
        p.Begin(0, w, "Chair", "", 20);
        Run(p, w, 0, PublishShow.StallSeconds + 0.5);
        Assert.Equal(PublishShow.Listed, p.stage);
        Assert.Equal(ShowAct.Changed | ShowAct.Ended, p.Cancel());
    }

    [Fact]
    public void Sent_through_a_screen_or_the_side_that_draws_it_gone_ends_it_without_touching_anything_else()
    {
        // the user sends it into the screen while the stall stands on the desk: the transition is theirs, the show simply stops
        var p = new PublishShow(); var w = Desk();
        p.Begin(0, w, "Chair", "", 20);
        Run(p, w, 0, 1);
        w.crossing = true;
        Assert.Equal(ShowAct.Changed | ShowAct.Ended | ShowAct.SayListed, p.Tick(1.1, w));
        Assert.False(p.Running);

        // the PC client goes away during the price tag (the show was inside the PC)
        p = new PublishShow(); w = Inside();
        p.Begin(0, w, "Chair", "", 20);
        Run(p, w, 0, PublishShow.StallSeconds + 0.5);
        w.hasPc = false;
        Assert.Equal(ShowAct.Changed | ShowAct.Ended, p.Tick(5, w));

        // the headset is taken off while the stall stands on the desk
        p = new PublishShow(); w = Desk();
        p.Begin(0, w, "Chair", "", 20);
        Run(p, w, 0, 1);
        w.hasMr = false;
        Assert.Equal(ShowAct.Changed | ShowAct.Ended | ShowAct.SayListed, p.Tick(1.1, w));

        // the PC client going away does nothing to a show on the desk - and the headset going away nothing to one inside
        p = new PublishShow(); w = Desk();
        p.Begin(0, w, "Chair", "", 20);
        w.hasPc = false;
        Assert.Equal(ShowAct.None, Run(p, w, 0, 2).acts);
        p = new PublishShow(); w = Inside();
        p.Begin(0, w, "Chair", "", 20);
        w.hasMr = false;
        Assert.Equal(ShowAct.None, Run(p, w, 0, 2).acts);

        // the partner turns up on the other side by another way (lpctl setOwner): the stall is not where it is any more
        p = new PublishShow(); w = Desk();
        p.Begin(0, w, "Chair", "", 20);
        w.owner = Env.Pc;
        Assert.True(p.Tick(0.5, w).HasFlag(ShowAct.Ended));
    }

    [Fact]
    public void The_lines()
    {
        Assert.Equal("올렸어! 의자, 별 20개!", PublishShow.ListedLine("의자", 20, true));
        Assert.Equal("It's up! Chair, 20 stars!", PublishShow.ListedLine("Chair", 20, false));
        Assert.StartsWith("올렸어! 의자", PublishShow.ListedLine("의자", 0, true));
        Assert.DoesNotContain("별 0개", PublishShow.ListedLine("의자", 0, true));
        Assert.Contains("free", PublishShow.ListedLine("Chair", 0, false));
        Assert.Equal("올릴게!", PublishShow.StartLine(true));
        Assert.Equal("Putting it up!", PublishShow.StartLine(false));
    }

    // ------------------------------------------------------------------ what the snapshot says
    [Fact]
    public void A_look_is_shown_until_the_answer_is_said_and_then_nothing_under_a_new_number()
    {
        var b = new ShowBoard();
        Assert.False(b.Refresh(0));
        Assert.Equal("", b.Info.kind);

        int id = b.LookBegin(10, "M2", 0.30f, 0.45f, 0.40f, 0.42f);
        Assert.True(b.Refresh(10));
        Assert.Equal("look", b.Info.kind); Assert.Equal("M2", b.Info.monitorId);
        Assert.Equal(0.30f, b.Info.u); Assert.Equal(0.45f, b.Info.v); Assert.Equal(0.40f, b.Info.w); Assert.Equal(0.42f, b.Info.h);
        Assert.Equal(8f, b.Info.seconds); Assert.Equal(10.0, b.Info.startedAt);
        int seq = b.Info.seq;
        Assert.False(b.Refresh(11));                                        // nothing new: no snapshot for it

        b.LookEnd(id);                                                      // the answer was said
        Assert.True(b.Refresh(13));
        Assert.Equal("", b.Info.kind);
        Assert.True(b.Info.seq > seq);
        Assert.False(b.Refresh(14));
        b.LookEnd(id);                                                      // (ending it twice is harmless)
        Assert.False(b.Refresh(15));
    }

    [Fact]
    public void A_look_that_is_never_ended_is_over_after_its_seconds_and_an_old_end_does_not_stop_a_new_look()
    {
        var b = new ShowBoard();
        int first = b.LookBegin(10, "M1", 0, 0, 1, 1);
        b.Refresh(10);
        Assert.False(b.Refresh(17.9));
        Assert.True(b.Refresh(18.1));
        Assert.Equal("", b.Info.kind);

        int second = b.LookBegin(20, "M1", 0.1f, 0.1f, 0.4f, 0.4f);
        b.Refresh(20);
        b.LookEnd(first);                                                   // the end of the look before
        Assert.False(b.Refresh(20.5));
        Assert.Equal("look", b.Info.kind);
        b.LookEnd(second);
        Assert.True(b.Refresh(21));
    }

    [Fact]
    public void A_parcel_is_shown_for_three_seconds()
    {
        var b = new ShowBoard();
        b.Deliver(100, "M1", "Chair", "market-chair.png");
        Assert.True(b.Refresh(100));
        Assert.Equal("deliver", b.Info.kind); Assert.Equal("Chair", b.Info.name); Assert.Equal("market-chair.png", b.Info.icon); Assert.Equal("M1", b.Info.monitorId);
        Assert.Equal(3f, b.Info.seconds);
        Assert.False(b.Refresh(102.9));
        Assert.True(b.Refresh(103.1));
        Assert.Equal("", b.Info.kind);
    }

    [Fact]
    public void The_stages_of_one_publish_show_keep_its_number_and_a_look_goes_over_it()
    {
        var b = new ShowBoard(); var p = new PublishShow(); var w = Inside();
        p.Begin(0, w, "Chair", "item-1.png", 20);
        b.Publish(0, p, "M1");
        Assert.True(b.Refresh(0));
        Assert.Equal("publish", b.Info.kind); Assert.Equal("stall", b.Info.stage); Assert.Equal("Chair", b.Info.name); Assert.Equal(20, b.Info.price);
        Assert.Equal("item-1.png", b.Info.icon); Assert.Equal(4f, b.Info.seconds); Assert.Equal("M1", b.Info.monitorId);
        int seq = b.Info.seq;

        p.Tick(PublishShow.StallSeconds, w);
        b.Publish(PublishShow.StallSeconds, p, "M1");
        Assert.True(b.Refresh(4));
        Assert.Equal("listed", b.Info.stage); Assert.Equal(seq, b.Info.seq); Assert.Equal((float)PublishShow.ListedSeconds, b.Info.seconds); Assert.Equal(4.0, b.Info.startedAt);

        // a look meanwhile is what is shown ...
        int look = b.LookBegin(4.5, "M2", 0, 0, 0.4f, 0.4f);
        Assert.True(b.Refresh(4.5));
        Assert.Equal("look", b.Info.kind); Assert.NotEqual(seq, b.Info.seq);
        // ... and when it is over the stall is back, under the number it had
        b.LookEnd(look);
        Assert.True(b.Refresh(5));
        Assert.Equal("publish", b.Info.kind); Assert.Equal("listed", b.Info.stage); Assert.Equal(seq, b.Info.seq);

        // the show ends: nothing, under a new number
        p.Tick(4 + PublishShow.ListedSeconds, w);
        Assert.False(p.Running);
        b.Publish(6.5, p, "");
        Assert.True(b.Refresh(6.5));
        Assert.Equal("", b.Info.kind); Assert.Equal("", b.Info.stage); Assert.Equal("", b.Info.name);
        Assert.True(b.Info.seq > seq);
    }
}
