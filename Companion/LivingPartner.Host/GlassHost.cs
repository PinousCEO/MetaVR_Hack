using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// Glass games on the host (Shared/Glass.cs is the contract; GlassRules.cs the rules). The host does not simulate:
///   glass.command start   it finds the pack in the bag (world.games) by id, title or way of playing, decides the
///                         side - inside the PC when the partner is there, otherwise on the desk - gives the run its
///                         number and tells everybody (glass.state, and the snapshot's `glass`);
///   glass.state           from the side that runs the game: phase, score, lives ... are taken over and passed on to
///                         the others (at most eight a second, at once when the phase changes); at "over" the best
///                         score of the pack is remembered, the partner says the result and the game is cleared;
///   glass.hand            from the headset: passed on to the PC clients as it is (not logged, not kept);
///   glass.command choose  passed on to the side that runs the game; stop ends it.
///   glass.out             from the side that runs the game: something flies OUT of the glass - passed on to the headsets
///   glass.out.result      from a headset: what became of it (blocked | hit | missed) - passed on to the running side
///                         (neither is logged per message: they are counted, and the count is said twice a minute).
/// A glass game and tennis exclude each other, and the side that runs a game going away ends it. A CROSSING does not
/// end the game any more: the game FOLLOWS the partner - the run is paused (phase "moving") while the partner goes
/// through the screen and the same pack begins again on the other side with its score, level and lives
/// (GlassRules.Resume). Only when the crossing fails does the game end.
/// Runs on the core thread.
/// </summary>
public sealed class GlassHost
{
    /// <summary>The game is cleared this long after it reported "over" (the result is read, the last picture stays a moment).</summary>
    public const double OverSeconds = 6;
    /// <summary>The side that runs a game reports about five times a second: when nothing has come for this long, nobody is playing it.</summary>
    public const double SilentSeconds = 20;

    readonly Core _core;
    /// <summary>What the snapshot carries, and what glass.state sends.</summary>
    public readonly GlassState S = new();
    double _lastReport, _lastRelay, _overAt = -1;
    bool _relayPending; Client _reporter;
    GlassRules.Carry _carry;            // the run is paused: the partner is on its way through the screen
    double _movingSince;
    int _outs, _outsDropped, _verdicts, _verdictsDropped, _countsSaid; double _countsAt;
    /// <summary>How long a paused run waits for the crossing to settle.</summary>
    public const double MovingSeconds = 90;

    public GlassHost(Core core)
    {
        _core = core;
        // (run numbers of one host run never repeat those of the run before: a client that kept an old state fits nothing)
        S.runId = (int)(HostClock.UnixMs / 1000 % 1000000) * 100;
        _core.State.glassBest ??= new();
    }

    public bool Active => S.game.Length > 0;
    bool Ko => _core.Talk.LastLanguage == "Korean";
    public object Diagnostics => new { game = S.game, pack = S.packId, side = S.side, monitor = S.monitorId, run = S.runId, phase = S.phase, score = S.score, best = S.best };

    void Tell(bool snapshot = true)
    {
        _lastRelay = HostClock.Now; _relayPending = false;
        _core.Broadcast(Msg.GlassState, S);
        if (snapshot) _core.MarkDirty(false, urgent: true);
    }

    public void OnCommand(Client c, GlassCommand g)
    {
        string how = string.IsNullOrWhiteSpace(g.how) ? c?.Role ?? "" : WorldRules.Short(g.how, 40);
        switch (g.cmd)
        {
            case "start":
            {
                var r = Start(c, g.game, how);
                if (r.line.Length > 0) _core.Say(r.line, r.ok ? "happy" : "worried", 4, r.ok ? "game: glass start" : "game: glass start refused");
                break;
            }
            case "stop": Stop("asked (" + how + ")"); break;
            case "choose":
                if (!Active || _carry != null) break;
                Log.Info($"glass: choose {g.pick} ({how}) -> the {S.side} side");
                _core.Broadcast(Msg.GlassCommand, new GlassCommand { cmd = "choose", game = S.packId, pick = Math.Clamp(g.pick, 0, 2), how = how }, o => o != c && GlassRules.Runs(S.side, o.Role));
                break;
            default: Log.Warn($"glass: unknown command '{g.cmd}'"); break;
        }
    }

    /// <summary>
    /// Without a language model: "오목 하자", "뱀파이어 서바이벌 하자", "play Shadow Umbrella", "틱택토 시작". Returns the line
    /// said; null when the sentence is not a wish for a glass game (one in the bag, or one of the makers' that could be got).
    /// </summary>
    public string Plain(Client c, string text)
    {
        string t = (text ?? "").Trim();
        if (t.Length == 0 || t.Length > 60) return null;
        if (!System.Text.RegularExpressions.Regex.IsMatch(t, @"하자|할래|해\s*보자|시작|켜\s*줘|틀어\s*줘|\bplay\b|\bstart\b|let'?s", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return null;
        var games = _core.State.world.games;
        var pick = GlassRules.PickGame(games, "", "", t);
        string what;
        if (pick.Found) { if (!GlassRules.IsGlass(games.First(g => g.id == pick.packId).template)) return null; what = pick.packId; }
        else if (pick.offer.Length > 0) what = pick.offer;
        else return null;
        var r = Start(c, what, "words");
        if (r.line.Length > 0) _core.Say(r.line, r.ok ? "happy" : "worried", 4, r.ok ? "game: glass start" : "game: glass start refused");
        return r.line;
    }

    /// <summary>
    /// Starts the glass game `what` names (a pack's id, a title, a way of playing). ok = it is on; line = what the
    /// partner has to say about it (the caller says it): "좋아, ... 하자!", or why not - not in the bag yet (then the
    /// marketplace's own line, which asks whether to get it), tennis is on, no side can run it.
    /// </summary>
    public (bool ok, string line) Start(Client c, string what, string how)
    {
        var w = _core.State.world;
        var pack = GlassRules.FindPack(w.games, what);
        if (pack == null)
        {
            // not in the bag: the marketplace knows whether it sells it, and asks whether to get it
            string id = (what ?? "").Trim();
            if (!id.StartsWith("game-", StringComparison.Ordinal)) id = GlassRules.PickGame(w.games, "", "", id).offer;
            string missing = id.Length > 0 ? _core.Shop.MissingGame(id) : "";
            Log.Info($"glass: start '{WorldRules.Short(what, 40)}' ({how}): no such glass game in the bag{(missing.Length > 0 ? " - offered from the marketplace" : "")}");
            return (false, missing.Length > 0 ? missing : Ko ? "그런 게임은 아직 없어." : "I don't have a game like that yet.");
        }
        string side = GlassRules.Side(_core.State.character.owner, _core.HasPc, _core.HasMr, _core.Coord.Machine.IsActive);
        string problem = GlassRules.StartProblem(S, pack, _core.Tennis.S.active, side);
        if (problem.Length > 0)
        {
            Log.Info($"glass: start {pack.id} ({how}) refused: {(problem == "playing" ? (_core.Tennis.S.active ? "tennis is on" : "it is running already") : $"no side can run it (the partner is in {_core.State.character.owner}, PC client {(_core.HasPc ? "connected" : "not connected")}, no headset)")}");
            return (false, problem == "playing" ? "We are already playing!" : "I need the headset on for that.");
        }
        string monitor = "";
        if (side == GlassRules.Pc)
        {
            var mons = _core.Desktop.Monitors;
            monitor = _core.Pc != null && ScreenMath.FindMonitor(mons, _core.Pc.monitorId) != null ? _core.Pc.monitorId : (mons.FirstOrDefault(m => m.primary) ?? mons.FirstOrDefault())?.id ?? "";
        }
        if (Active) Log.Info($"glass: {S.packId} run {S.runId} gives way to {pack.id}");
        GlassRules.Begin(S, pack, side, monitor, GlassRules.Best(_core.State.glassBest, pack.id));
        _overAt = -1; _reporter = null; _lastReport = HostClock.Now; _carry = null;
        _outs = _outsDropped = _verdicts = _verdictsDropped = _countsSaid = 0; _countsAt = HostClock.Now;
        Log.Info($"glass: started {pack.id} '{pack.title}' ({pack.template}) on the {side} side{(monitor.Length > 0 ? " on " + monitor : "")}, run {S.runId}, best so far {S.best} ({how})");
        Tell();
        if (side == GlassRules.Mr && _core.HasPc && _core.HasMr && !_core.Coord.Machine.IsActive &&
            pack.script?.Contains("shoot_out(", StringComparison.Ordinal) == true)
        {
            var calibrated = _core.Shows.Calibrated();
            string previous = _core.Coord.Info.target?.monitorId;
            var screen = calibrated.FirstOrDefault(m => m.id == previous) ?? calibrated.FirstOrDefault(m => m.primary) ?? calibrated.FirstOrDefault();
            if (screen != null)
            {
                var pointer = new PointerSample { monitorId = screen.id, u = 0.5f, v = 0.58f, valid = true, t = HostClock.Now };
                _core.Broadcast(Msg.IntentResult, new IntentResultMsg { kind = "enterPortal", pointer = pointer }, _core.IsMr);
            }
        }
        if (StockChart.IsPack(pack)) return (true, Ko ? "주가 차트를 열었어." : $"Here's your {pack.title}.");
        return (true, GlassRules.StartLine(pack.title, Ko));
    }

    public void Stop(string why)
    {
        if (!Active) return;
        Log.Info($"glass: {S.packId} run {S.runId} ended ({why}); score {S.score}, level {S.level}, {S.seconds:0} s{Counts()}");
        GlassRules.Clear(S);
        _overAt = -1; _reporter = null; _carry = null;
        Tell();
    }

    /// <summary>glass.state from a client: only the side that runs the game is heard, and only about the run that is on.</summary>
    public void OnState(Client c, GlassState report)
    {
        if (_carry != null || !GlassRules.Accept(S, report, c.Role)) return;
        double now = HostClock.Now;
        bool phase = GlassRules.Merge(S, report);
        _lastReport = now; _reporter = c;
        if (phase)
        {
            Log.Info($"glass: {S.packId} run {S.runId} -> {S.phase} (score {S.score}, lives {S.lives}, level {S.level}{(S.choices.Length > 0 ? ", choices: " + string.Join(" | ", S.choices) : "")})");
            if (S.phase == "over") Over(now);
            _core.MarkDirty(false);
        }
        if (GlassRules.RelayNow(_lastRelay, now, phase)) Relay(now); else _relayPending = true;
    }

    void Relay(double now)
    {
        _lastRelay = now; _relayPending = false;
        var from = _reporter;
        _core.Broadcast(Msg.GlassState, S, o => o != from);
    }

    void Over(double now)
    {
        _overAt = now;
        bool best = GlassRules.NewBest(_core.State.glassBest, S.packId, S.score);
        S.best = GlassRules.Best(_core.State.glassBest, S.packId);
        if (best) { Log.Info($"glass: a new best for {S.packId}: {S.score}"); _core.MarkDirty(); }
        _core.Say(GlassRules.OverLine(S, Ko, best), "happy", 5, "game: glass over");
    }

    /// <summary>glass.hand: from the headset (or, for a check without one, from lpctl on this PC) to the PC clients, as it is.</summary>
    public void OnHand(Client c, GlassHand h)
    {
        if (!(_core.IsMr(c) || (c.Loopback && c.Role == Roles.Tool))) return;
        if (!GlassRules.HandFresh(h.t, HostClock.Now)) return;
        _core.Broadcast(Msg.GlassHand, h, o => o.Role == Roles.Pc);
    }

    string Counts() => _outs + _outsDropped + _verdicts + _verdictsDropped == 0 ? "" : $"; through the glass: {_outs} sent out to the headsets ({_outsDropped} dropped), {_verdicts} verdict(s) back ({_verdictsDropped} dropped)";

    /// <summary>glass.out: from the side that runs the game (or, for a check, from lpctl on this PC) to every headset. Not for the run that is on: dropped.</summary>
    public void OnOut(Client c, GlassOut o)
    {
        bool check = c.Loopback && c.Role == Roles.Tool && Active && o.runId == S.runId;
        if (!check && !GlassRules.OutTaken(S, o.runId, c.Role)) { _outsDropped++; return; }
        _outs++;
        _core.Broadcast(Msg.GlassOut, o, x => x != c && _core.IsMr(x));
    }

    /// <summary>glass.out.result: from a headset to the side that runs the game.</summary>
    public void OnOutResult(Client c, GlassOutResult r)
    {
        if (!GlassRules.OutResultTaken(S, r, c.Role)) { _verdictsDropped++; return; }
        _verdicts++;
        _core.Broadcast(Msg.GlassOutResult, r, x => x != c && GlassRules.Runs(S.side, x.Role));
    }

    /// <summary>The run is paused for a crossing: when the crossing has settled the pack begins again where the partner is now; when it failed the game ends.</summary>
    void Follow(double now)
    {
        if (_core.Coord.Machine.IsActive)
        {
            if (now - _movingSince > MovingSeconds) Stop($"the crossing did not settle within {MovingSeconds:0} s");
            return;
        }
        var carry = _carry;
        string side = GlassRules.Side(_core.State.character.owner, _core.HasPc, _core.HasMr, false);
        var pack = _core.State.world.games.FirstOrDefault(g => g != null && g.id == carry.packId);
        string problem = pack == null ? "the pack is not in the bag any more" : GlassRules.ResumeProblem(carry, side);
        if (problem.Length > 0) { Stop(problem); return; }
        string monitor = "";
        if (side == GlassRules.Pc)
        {
            var mons = _core.Desktop.Monitors;
            monitor = _core.Pc != null && ScreenMath.FindMonitor(mons, _core.Pc.monitorId) != null ? _core.Pc.monitorId : (mons.FirstOrDefault(m => m.primary) ?? mons.FirstOrDefault())?.id ?? "";
        }
        int was = S.runId;
        GlassRules.Resume(S, pack, side, monitor, GlassRules.Best(_core.State.glassBest, pack.id), carry);
        _carry = null; _overAt = -1; _reporter = null; _lastReport = now;
        Log.Info($"glass: {pack.id} '{pack.title}' FOLLOWED the partner: run {was} on the {carry.side} side goes on as run {S.runId} on the {side} side{(monitor.Length > 0 ? " on " + monitor : "")} with score {S.score}, level {S.level}, lives {S.lives}, {S.seconds:0} s");
        Tell();
    }

    public void Tick(double now)
    {
        if (!Active) return;
        if (_relayPending && now - _lastRelay >= GlassRules.RelayGap) Relay(now);
        if (_overAt >= 0) { if (now - _overAt > OverSeconds) Stop("game over"); return; }
        if (now - _countsAt > 30) { _countsAt = now; if (_outs + _outsDropped + _verdicts + _verdictsDropped != _countsSaid) { _countsSaid = _outs + _outsDropped + _verdicts + _verdictsDropped; Log.Info($"glass: {S.packId} run {S.runId} ({S.phase}, score {S.score}){Counts()}"); } }
        // THE GAME FOLLOWS THE PARTNER: a crossing pauses the run, and when it has settled the same pack goes on on the other side
        if (_carry != null) { Follow(now); return; }
        if (_core.Coord.Machine.IsActive)
        {
            _carry = GlassRules.Pause(S);
            if (_carry == null) { Stop("the partner is going through the screen"); return; }
            _movingSince = now; _reporter = null; _relayPending = false;
            Log.Info($"glass: {S.packId} run {S.runId} is PAUSED on the {_carry.side} side - the partner is going through the screen (score {_carry.score}, level {_carry.level}, lives {_carry.lives}, {_carry.seconds:0} s go along)");
            Tell();
            return;
        }
        bool there = S.side == GlassRules.Pc ? _core.HasPc && _core.State.character.owner == Env.Pc : _core.HasMr;
        if (!there) { Stop("the side that runs it went away"); return; }
        if (now - _lastReport > SilentSeconds) Stop($"the {S.side} side has not reported for {SilentSeconds:0} s");
    }
}
