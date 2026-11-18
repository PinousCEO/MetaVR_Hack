using System.Numerics;
using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// Referee and opponent of the tennis game. The glass of one monitor is the net: the partner plays in a court behind
/// the glass, the user in the room in front of it with a hand as the racket. The host owns the game: it serves for the
/// partner, decides whether the partner can reach a ball and where it sends it, accepts the user's strokes reported by
/// the MR client, counts bounces and keeps the score. The ball itself is never streamed: every stroke is a TennisShot
/// and both clients compute the same path from it on the shared clock (Tennis.Evaluate), each drawing its own side.
/// Runs on the core thread.
/// </summary>
public sealed class TennisGame
{
    readonly Core _core;
    readonly Random _rng = new();
    public TennisState S = new();
    Vector3 _eye = new(0, 0.05f, -0.6f);
    float _px;                      // where the partner is along its baseline right now (it runs at Tennis.PartnerSpeed)
    double _lastTick;
    bool _planned; Vector3 _planPos;
    int _seq;

    public TennisGame(Core core) { _core = core; }

    /// <summary>
    /// The game's state goes to the clients as its own small message (game.state): a serve, a stroke, a point. A whole
    /// snapshot (45 kB, parsed on the headset's main thread in the middle of a rally) is asked for only when a game
    /// begins or ends; any snapshot that goes out meanwhile carries the same state under the same revision.
    /// </summary>
    void Publish(bool snapshot = false) { S.revision++; _core.Broadcast(Msg.GameState, S); if (snapshot) _core.MarkDirty(false); }

    void Phase(string phase, double now, string note = "")
    {
        S.phase = phase; S.phaseAt = now; S.note = note;
        Log.Info($"tennis: {phase} {note} score user {S.scoreUser} - partner {S.scorePartner}");
    }

    public string Start(Client c, GameCommand g)
    {
        if (S.active) return "";
        // Tennis (and its two variations, the shoot-out and the catching game) is a module like any other: without it
        // in the bag no game starts - from the headset's menu, from lpctl or from anywhere else. The partner says so,
        // and asks whether to get it (it is free).
        if (g.mode != "" && g.mode != null && g.mode != "keeper") return "That game is no longer in the bag.";
        if (_core.Shop.MissingGame(g.mode == "keeper" ? "keeper" : "tennis") is { Length: > 0 } missing) return missing;
        if (!_core.IsMr(c)) return "I need the headset on to play tennis.";
        if (_core.Coord.Machine.IsActive) return "Let me finish getting there first.";
        double now = HostClock.Now;
        TennisCourt court; string source;
        if (g.room)
        {
            // out in the room: the court is on the desk, as the headset laid it out between the user and the partner
            if (_core.State.character.owner != Env.Mr) return "I'm inside the screen - we play through the screen then.";
            float h = Math.Clamp(g.height, 0.2f, 0.8f);
            court = new TennisCourt { room = true, monitorId = "", width = Math.Clamp(g.width, 0.25f, 0.7f), height = h, depth = Math.Clamp(g.depth, 0.2f, 0.6f), up = new V3(0, 1, 0), deskHeight = -h * 0.5f, baseline = 0.20f };
            source = "on the desk";
        }
        else
        {
            if (_core.State.character.owner != Env.Pc || _core.Pc == null) return "I have to be inside the screen to play through it.";
            var mon = ScreenMath.FindMonitor(_core.Desktop.Monitors, _core.Pc.monitorId);
            if (mon == null) return "I can't tell which screen I am in.";
            bool sim = _core.Mr != null && _core.Mr.simulated;
            var calib = _core.State.calibrations.FirstOrDefault(k => k.monitorId == mon.id && k.simulated == sim && k.pixelW == mon.bounds.w && k.pixelH == mon.bounds.h);
            ScreenMath.PhysicalSize(mon, calib, out float w, out float h, out source);
            court = new TennisCourt { monitorId = mon.id, width = w, height = h, depth = Math.Clamp(w * 0.85f, 0.40f, 0.60f), up = g.up, deskHeight = Math.Min(g.deskHeight, -h * 0.5f) };
        }
        court.radius = Tennis.BallRadius(g.mode);
        _core.Glass.Stop("tennis starts");       // one game at a time: a glass game (GlassHost.cs) gives way
        S = new TennisState
        {
            active = true, revision = S.revision, mode = g.mode ?? "", rounds = g.mode == "stars" ? 10 : g.mode == "keeper" ? 5 : 0,
            court = court,
            partnerX = 0, partnerZ = Tennis.Baseline(court), serveAt = now + 2.4, partnerHitAt = now + 2.4,
        };
        _eye = g.eye; _px = 0; _planned = false; _lastTick = now;
        Phase("ready", now, S.mode.Length == 0 ? "first to " + Tennis.PointsToWin : S.mode + ", " + S.rounds + " balls");
        Log.Info($"tennis: started {(court.room ? "on the desk" : "on " + court.monitorId)} court {court.width * 1000:0}x{court.height * 1000:0}x{court.depth * 1000:0} mm ({source}) desk {court.deskHeight * 1000:0} mm up {court.up}");
        bool ko = _core.Talk.LastLanguage == "Korean";
        if (S.mode == "stars") _core.Say(ko ? $"별 받기 하자! 내가 던지는 공 {S.rounds}개를 손으로 잡아 봐." : $"Catch! I'll throw {S.rounds} - catch them with your hand.", "happy", 5, "game start");
        else if (S.mode == "keeper") _core.Say(ko ? $"내가 공을 찰게! 화면에서 나오는 축구공을 손바닥으로 막아 줘. {S.rounds}번이야." : $"You’re the keeper! I’ll kick {S.rounds} balls. Block them with your palm.", "happy", 6, "game start");
        else _core.Say($"Let's play! First to {Tennis.PointsToWin}. Hit the ball back with your hand.", "happy", 5, "tennis start");
        Publish(true);
        return "";
    }

    public void Stop(string why)
    {
        if (!S.active) return;
        Log.Info("tennis: stopped (" + why + ")");
        S.active = false; S.shot = null; S.phase = "off"; S.note = "";
        Publish(true);
    }

    public void OnCommand(Client c, GameCommand g)
    {
        switch (g.cmd)
        {
            case "start":
            {
                string refused = Start(c, g);
                if (refused.Length > 0) _core.Say(refused, "worried", 4, "tennis start refused");
                break;
            }
            case "stop": Stop("asked by " + c.Role); break;
            case "hit": OnUserHit(c, g); break;
        }
    }

    void OnUserHit(Client c, GameCommand g)
    {
        double now = HostClock.Now;
        if (!S.active || S.phase != "rally" || S.shot == null || S.shot.by != "partner" || g.seq != S.shot.seq || !_core.IsMr(c)) return;
        if (Math.Abs(g.t - now) > 0.6) return;                                   // a stale report
        var b = Tennis.Evaluate(S.court, S.shot, g.t);
        if (b.dead || !b.Front) return;                                           // it was not there to be struck
        _eye = g.eye;
        if (S.mode == "stars") { Point("user", "caught", now); return; }          // a hand on the ball: caught
        if (S.mode == "keeper") { Point("user", "saved", now); return; }
        // the host places the stroke where ITS ball was at that moment: the same function the client used
        S.shot = Tennis.UserShot(S.court, ++_seq, b.pos, g.swing, g.normal, g.t);
        S.rally++; S.bestRally = Math.Max(S.bestRally, S.rally);
        _planned = Tennis.PlanReturn(S.court, S.shot, _px, now, out double at, out _planPos);
        // nobody returns everything: now and then it swings and misses, more often in a long rally
        if (_planned && _rng.NextDouble() < Math.Min(0.30, 0.08 + 0.02 * S.rally)) _planned = false;
        if (_planned) { S.partnerX = _planPos.X; S.partnerZ = Math.Clamp(_planPos.Z + 0.02f, 0.12f, S.court.depth - 0.07f); S.partnerHitAt = at; }
        else
        {
            // it still runs for the ball, and arrives late or swings at air
            double guess = S.shot.t0 + 1.4;
            var land = Tennis.Evaluate(S.court, S.shot, guess);
            S.partnerX = Math.Clamp(land.pos.X * 0.6f, -S.court.width * 0.5f + 0.06f, S.court.width * 0.5f - 0.06f);
            S.partnerZ = Tennis.Baseline(S.court); S.partnerHitAt = 0;
        }
        Log.Info($"tennis: user stroke #{S.shot.seq} rally {S.rally} from {S.shot.p0} -> partner {(_planned ? $"returns at +{at - now:0.00}s x={_planPos.X:0.00}" : "will miss")}");
        Publish();
    }

    void Point(string winner, string why, double now)
    {
        if (winner == "user") S.scoreUser++; else S.scorePartner++;
        S.lastPoint = winner; S.shot = null; S.partnerHitAt = 0; S.partnerX = 0; S.partnerZ = Tennis.Baseline(S.court);
        S.round++;
        bool over = S.mode.Length == 0 ? S.scoreUser >= Tennis.PointsToWin || S.scorePartner >= Tennis.PointsToWin : S.round >= S.rounds;
        Phase(over ? "over" : "point", now, why);
        bool ko = _core.Talk.LastLanguage == "Korean";
        if (over && S.mode == "stars")
            _core.Say(ko ? $"{S.rounds}개 중에 {S.scoreUser}개 받았어! {(S.scoreUser >= S.rounds * 0.7 ? "잘한다!" : "한 번 더 할래?")}" : $"You caught {S.scoreUser} of {S.rounds}! {(S.scoreUser >= S.rounds * 0.7 ? "Nice hands!" : "Again?")}", "happy", 5, "game over");
        else if (over && S.mode == "keeper")
            _core.Say(ko ? $"내가 찬 {S.rounds}개의 공 중에 {S.scoreUser}개를 막았어!" : $"You saved {S.scoreUser} of my {S.rounds} shots!", "happy", 5, "game over");
        else if (over)
            _core.Say(S.scoreUser > S.scorePartner ? $"You win, {S.scoreUser} to {S.scorePartner}! That was fun." : $"I win this one, {S.scorePartner} to {S.scoreUser}! Shall we play again?", "happy", 5, "tennis over");
        Publish();
    }

    public void Tick(double now)
    {
        if (!S.active) return;
        float dt = (float)Math.Clamp(now - _lastTick, 0, 0.1); _lastTick = now;
        _px += Math.Clamp(S.partnerX - _px, -Tennis.PartnerSpeed * dt, Tennis.PartnerSpeed * dt);
        // the game needs both sides: the partner inside the PC and a headset (or the simulator) for the user's half
        bool there = S.court.room ? _core.State.character.owner == Env.Mr : _core.HasPc && _core.State.character.owner == Env.Pc;
        if (!_core.HasMr || !there) { Stop("a side of the game went away"); return; }
        switch (S.phase)
        {
            case "ready":
                if (now >= S.serveAt)
                {
                    S.partnerX = _px; S.partnerZ = Tennis.Baseline(S.court);
                    var from = Tennis.ServePoint(S.court, _px);
                    S.shot = Tennis.PartnerShot(S.court, ++_seq, from, _eye, (float)(_rng.NextDouble() * 1.2 - 0.6) * (S.mode == "stars" ? 1.5f : S.mode == "keeper" ? 0.4f : 1f), now);
                    S.rally = 0; _planned = false;
                    Phase("rally", now, "serve");
                    Publish();
                }
                break;
            case "rally":
            {
                if (S.shot == null) { Phase("ready", now); S.serveAt = now + 1.2; S.partnerHitAt = S.serveAt; Publish(); break; }
                if (S.shot.by == "user" && _planned && now >= S.partnerHitAt)
                {
                    // the partner's stroke, from where the ball is at this moment
                    var at = Tennis.Evaluate(S.court, S.shot, now);
                    float spread = Math.Min(1f, 0.5f + 0.08f * S.rally);
                    S.shot = Tennis.PartnerShot(S.court, ++_seq, at.pos, _eye, (float)(_rng.NextDouble() * 2 - 1) * spread, now);
                    S.rally++; S.bestRally = Math.Max(S.bestRally, S.rally);
                    _planned = false;
                    Publish();
                    break;
                }
                var b = Tennis.Evaluate(S.court, S.shot, now);
                if (b.dead || now > S.shot.t0 + 8)
                {
                    if (S.shot.by == "partner") Point("partner", S.mode == "stars" ? "dropped" : b.why.Length > 0 ? b.why : "missed", now);          // the user did not get it back (stars: did not catch it)
                    else if (b.crossed && b.why == "out") Point("partner", "out", now);                             // over the net, but beyond the lines
                    else Point(b.crossed ? "user" : "partner", b.crossed ? (S.mode == "keeper" ? "goal" : "out of reach") : b.why, now);          // the user's stroke: in and unreturned, or never made it in
                }
                break;
            }
            case "point":
                if (now - S.phaseAt > 1.9) { S.serveAt = now + 1.3; S.partnerHitAt = S.serveAt; Phase("ready", now); Publish(); }
                break;
            case "over":
                if (now - S.phaseAt > 4.5) Stop("game over");
                break;
        }
    }
}
