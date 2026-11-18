using LivingPartner.Host;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>The host's part in a glass game: which pack, which side of the glass, whose reports count, what is passed on, the best score.</summary>
public class GlassHostTests
{
    static GamePackDef Pack(string id, string template, string title, int lives = 3) => new() { id = id, template = template, title = title, lives = lives };
    static List<GamePackDef> Bag() => new()
    {
        Pack("game-pop", "pop", "Star Pop"),
        Pack("game-dustbunnies", "survival", "Dust Bunnies"),
        Pack("game-vampire", "survival", "Vampire Night"),
        Pack("game-umbrella", "umbrella", "Shadow Umbrella"),
        Pack("game-hail", "umbrella", "Hail Day"),
    };

    // ------------------------------------------------------------------ which pack
    [Theory]
    [InlineData("game-vampire", null)]
    [InlineData("Vampire Night", null)]
    [InlineData("vampire night", null)]
    [InlineData("survival", null)]                  // the way of playing: the makers' own pack of that kind first
    [InlineData("뱀파이어 서바이벌 하자", null)]
    [InlineData("밤비야, 뱀파이어 서바이벌 하자!", null)]
    [InlineData("vampire", null)]
    [InlineData("그림자 우산 하자", null)]
    [InlineData("Shadow Umbrella", null)]
    [InlineData("umbrella", null)]
    [InlineData("Hail Day", "game-hail")]
    [InlineData("let's play hail day", "game-hail")]
    [InlineData("hail", "game-hail")]                         // a word that its title begins with
    [InlineData("let's play Dust Bunnies", null)]
    [InlineData("game-dustbunnies", null)]
    public void A_glass_game_is_found_by_id_title_or_way_of_playing(string what, string id)
    {
        Assert.Equal(id, GlassRules.FindPack(Bag(), what)?.id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("game-pop")]            // in the bag, but not a glass game
    [InlineData("Star Pop")]
    [InlineData("테니스 하자")]
    [InlineData("가위바위보")]
    [InlineData("game-nothing")]
    [InlineData("창문 닦기 하자")]       // the wipe game is gone: its words name nothing any more
    [InlineData("Foggy Window")]
    [InlineData("wipe")]
    [InlineData("오목 하자")]            // a game nobody has is not replaced by one that is there
    [InlineData("틱택토 하자")]
    [InlineData("board")]
    [InlineData("체스 하자")]
    public void What_is_no_glass_game_is_not_found(string what)
    {
        Assert.Null(GlassRules.FindPack(Bag(), what));
    }

    [Fact]
    public void A_game_that_is_not_in_the_bag_is_not_replaced_by_another()
    {
        var bag = Bag();
        bag.RemoveAll(g => g.id == "game-vampire");
        // "vampire" means that game: Dust Bunnies, played the same way, is not it ...
        Assert.Null(GlassRules.FindPack(bag, "뱀파이어 서바이벌 하자"));
        Assert.Null(GlassRules.FindPack(bag, "Vampire Night"));
        // ... but a wish for the way of playing alone is content with what there is
        Assert.Null(GlassRules.FindPack(bag, "survival"));
        Assert.Null(GlassRules.FindPack(bag, "서바이벌 하자"));
        Assert.Null(GlassRules.FindPack(new List<GamePackDef>(), "survival"));
        Assert.Null(GlassRules.FindPack(null, "survival"));
        // what the marketplace is asked for when it is missing
        Assert.Equal("", GlassRules.PackOf(GlassRules.TemplateNamed("뱀파이어 서바이벌 하자")));
        Assert.Empty(GlassRules.PackOf(GlassRules.TemplateNamed("Shadow Umbrella")));
        Assert.Equal("", GlassRules.TemplateNamed("창문 닦기 하자")); Assert.Equal("", GlassRules.TemplateNamed("오목 하자"));
        Assert.Equal(new[] { "survival", "umbrella" }, GlassRules.Templates.Take(2));
        Assert.False(GlassRules.IsGlass("wipe")); Assert.False(GlassRules.IsGlass("board"));
        Assert.Equal("", GlassRules.PackOf(GlassRules.TemplateNamed("체스 하자")));
    }

    // ------------------------------------------------------------------ which side
    [Fact]
    public void Inside_the_pc_the_pc_runs_it_otherwise_the_headset()
    {
        Assert.Equal("pc", GlassRules.Side(Env.Pc, hasPc: true, hasMr: true, crossing: false));
        Assert.Equal("pc", GlassRules.Side(Env.Pc, hasPc: true, hasMr: false, crossing: false));       // no headset needed to watch it play inside
        Assert.Equal("mr", GlassRules.Side(Env.Mr, hasPc: true, hasMr: true, crossing: false));
        Assert.Equal("mr", GlassRules.Side(Env.Pc, hasPc: true, hasMr: true, crossing: true));         // on its way through the screen: not inside yet
        Assert.Equal("mr", GlassRules.Side(Env.Pc, hasPc: false, hasMr: true, crossing: false));       // nobody to draw it inside
        Assert.Equal("", GlassRules.Side(Env.Mr, hasPc: true, hasMr: false, crossing: false));         // on the desk with no headset: refused
        Assert.Equal("", GlassRules.Side(Env.Pc, hasPc: false, hasMr: false, crossing: false));
    }

    [Fact]
    public void The_running_side_is_the_pc_client_or_the_headset_or_its_simulator()
    {
        Assert.True(GlassRules.Runs("pc", Roles.Pc));
        Assert.False(GlassRules.Runs("pc", Roles.Quest));
        Assert.True(GlassRules.Runs("mr", Roles.Quest));
        Assert.True(GlassRules.Runs("mr", Roles.Sim));
        Assert.False(GlassRules.Runs("mr", Roles.Pc));
        Assert.False(GlassRules.Runs("mr", Roles.Tool));
        Assert.False(GlassRules.Runs("", Roles.Pc));
    }

    // ------------------------------------------------------------------ start, stop, one game at a time
    [Fact]
    public void A_start_sets_what_the_host_decides_and_counts_the_run_up()
    {
        var s = new GlassState { runId = 41 };
        GlassRules.Begin(s, Pack("game-vampire", "survival", "Vampire Night", lives: 4), "pc", @"\\.\DISPLAY2", best: 120);
        Assert.Equal("survival", s.game); Assert.Equal("game-vampire", s.packId); Assert.Equal("Vampire Night", s.title);
        Assert.Equal("pc", s.side); Assert.Equal(@"\\.\DISPLAY2", s.monitorId);
        Assert.Equal(42, s.runId); Assert.Equal("intro", s.phase);
        Assert.Equal(0, s.score); Assert.Equal(4, s.lives); Assert.Equal(1, s.level); Assert.Equal(120, s.best);

        // on the desk no display is named
        GlassRules.Begin(s, Pack("game-hail", "umbrella", "Hail Day"), "mr", @"\\.\DISPLAY2", best: 0);
        Assert.Equal("mr", s.side); Assert.Equal("", s.monitorId); Assert.Equal(43, s.runId);

        // stop: no game, and the run number stays so that a late report fits nothing
        GlassRules.Clear(s);
        Assert.Equal("", s.game); Assert.Equal("", s.packId); Assert.Equal("", s.side); Assert.Equal("", s.phase);
        Assert.Equal(43, s.runId);
        Assert.Equal("glass: -", GlassRules.Line(s));
    }

    [Fact]
    public void Tennis_and_a_glass_game_exclude_each_other_and_the_same_game_is_not_started_twice()
    {
        var vampire = Pack("game-vampire", "survival", "Vampire Night");
        var none = new GlassState();
        Assert.Equal("", GlassRules.StartProblem(none, vampire, tennisOn: false, side: "pc"));
        Assert.Equal("playing", GlassRules.StartProblem(none, vampire, tennisOn: true, side: "pc"));      // while tennis runs: refused
        Assert.Equal("headset", GlassRules.StartProblem(none, vampire, tennisOn: false, side: ""));

        var on = new GlassState();
        GlassRules.Begin(on, vampire, "mr", "", 0);
        Assert.Equal("playing", GlassRules.StartProblem(on, vampire, false, "mr"));                       // it is on already
        Assert.Equal("", GlassRules.StartProblem(on, Pack("game-hail", "umbrella", "Hail Day"), false, "mr"));      // another one takes its place
        on.phase = "over";
        Assert.Equal("", GlassRules.StartProblem(on, vampire, false, "mr"));                              // "again!" while the result is still up
    }

    // ------------------------------------------------------------------ reports from the side that runs it
    static GlassState Running(string side = "pc")
    {
        var s = new GlassState { runId = 6 };
        GlassRules.Begin(s, Pack("game-vampire", "survival", "Vampire Night"), side, "M1", 50);
        return s;
    }

    [Fact]
    public void Only_the_running_side_is_heard_and_only_about_the_run_that_is_on()
    {
        var s = Running("pc");
        var ok = new GlassState { runId = s.runId, phase = "play", score = 12, lives = 2, level = 2, seconds = 14.5f };
        Assert.True(GlassRules.Accept(s, ok, Roles.Pc));
        Assert.False(GlassRules.Accept(s, ok, Roles.Quest));                 // the headset watches a game that runs inside
        Assert.False(GlassRules.Accept(s, ok, Roles.Tool));
        Assert.False(GlassRules.Accept(s, new GlassState { runId = s.runId - 1, phase = "play" }, Roles.Pc));     // a report of the run before
        Assert.False(GlassRules.Accept(s, new GlassState { runId = s.runId, phase = "dancing" }, Roles.Pc));     // no such phase
        Assert.False(GlassRules.Accept(s, null, Roles.Pc));
        Assert.False(GlassRules.Accept(new GlassState { runId = s.runId }, ok, Roles.Pc));                         // no game is on

        var desk = Running("mr");
        Assert.True(GlassRules.Accept(desk, new GlassState { runId = desk.runId, phase = "intro" }, Roles.Sim));
        Assert.False(GlassRules.Accept(desk, new GlassState { runId = desk.runId, phase = "intro" }, Roles.Pc));
    }

    [Fact]
    public void A_report_changes_how_the_game_stands_but_never_which_game_it_is()
    {
        var s = Running("pc");
        int run = s.runId;
        var rep = new GlassState
        {
            runId = run, game = "umbrella", packId = "game-other", title = "Other", side = "mr", monitorId = "M9", best = 9999,
            phase = "choose", score = 31, lives = 2, level = 3, seconds = 40f,
            choices = new[] { "Garlic", "Lantern", "Boots", "A fourth" }, choiceNotes = new[] { "slows them", new string('x', 200), "run faster" },
            banner = "Level 3!\nPick one", result = "",
        };
        Assert.True(GlassRules.Merge(s, rep));                              // the phase changed
        Assert.Equal("survival", s.game); Assert.Equal("game-vampire", s.packId); Assert.Equal("Vampire Night", s.title);
        Assert.Equal("pc", s.side); Assert.Equal("M1", s.monitorId); Assert.Equal(run, s.runId); Assert.Equal(50, s.best);
        Assert.Equal("choose", s.phase); Assert.Equal(31, s.score); Assert.Equal(2, s.lives); Assert.Equal(3, s.level); Assert.Equal(40f, s.seconds);
        Assert.Equal(new[] { "Garlic", "Lantern", "Boots" }, s.choices);    // three at most
        Assert.Equal(80, s.choiceNotes[1].Length);
        Assert.Equal("Level 3! Pick one", s.banner);

        rep.score = 33; rep.seconds = float.NaN; rep.lives = -4;
        Assert.False(GlassRules.Merge(s, rep));                             // the same phase
        Assert.Equal(33, s.score); Assert.Equal(40f, s.seconds); Assert.Equal(0, s.lives);
    }

    [Fact]
    public void After_over_no_report_is_taken_any_more()
    {
        var s = Running("mr");
        Assert.True(GlassRules.Merge(s, new GlassState { runId = s.runId, phase = "over", score = 77, result = "77점! 잘했어." }));
        Assert.Equal("over", s.phase);
        Assert.False(GlassRules.Accept(s, new GlassState { runId = s.runId, phase = "play", score = 0 }, Roles.Quest));
    }

    [Fact]
    public void Reports_are_passed_on_at_most_eight_times_a_second_but_a_phase_change_at_once()
    {
        double last = 10.0; int relayed = 0;
        for (int i = 1; i <= 30; i++)                                       // a side that reports thirty times in one second
        {
            double now = 10.0 + i / 30.0;
            if (GlassRules.RelayNow(last, now, false)) { relayed++; last = now; }
        }
        Assert.InRange(relayed, 6, 8);
        Assert.False(GlassRules.RelayNow(10.0, 10.05, false));
        Assert.True(GlassRules.RelayNow(10.0, 10.05, true));                // the phase changed
        Assert.True(GlassRules.RelayNow(10.0, 10.2, false));                // five a second all go through
    }

    [Fact]
    public void A_hand_report_is_passed_on_only_while_it_is_fresh()
    {
        Assert.True(GlassRules.HandFresh(99.8, 100.0));
        Assert.True(GlassRules.HandFresh(99.5, 100.0));
        Assert.False(GlassRules.HandFresh(99.4, 100.0));                    // older than half a second: dropped
        Assert.True(GlassRules.HandFresh(100.3, 100.0));                    // the headset's clock a little ahead
        Assert.True(GlassRules.HandFresh(0, 100.0));                        // not stamped
    }

    // ------------------------------------------------------------------ the best score, and what is said
    [Fact]
    public void The_best_score_is_kept_for_each_pack()
    {
        var best = new Dictionary<string, int>();
        Assert.Equal(0, GlassRules.Best(best, "game-vampire"));
        Assert.True(GlassRules.NewBest(best, "game-vampire", 40));
        Assert.False(GlassRules.NewBest(best, "game-vampire", 40));         // equalled, not beaten
        Assert.False(GlassRules.NewBest(best, "game-vampire", 12));
        Assert.True(GlassRules.NewBest(best, "game-vampire", 41));
        Assert.True(GlassRules.NewBest(best, "game-omok", 7));
        Assert.False(GlassRules.NewBest(best, "game-omok", 0));
        Assert.False(GlassRules.NewBest(best, "", 100));
        Assert.False(GlassRules.NewBest(null, "game-omok", 100));
        Assert.Equal(41, GlassRules.Best(best, "game-vampire"));
        Assert.Equal(7, GlassRules.Best(best, "game-omok"));
        // it survives the state file
        var again = Proto.FromJson<Dictionary<string, int>>(Proto.ToJson(best));
        Assert.Equal(41, GlassRules.Best(again, "game-vampire"));
    }

    [Fact]
    public void The_partner_says_the_games_own_result_or_one_made_from_the_score()
    {
        var s = Running();
        s.phase = "over"; s.score = 64; s.level = 4;
        Assert.Equal("끝! 64점이야, 레벨 4까지 갔어. 최고 기록이야!", GlassRules.OverLine(s, true, true));
        Assert.Equal("That's it! 64 points, up to level 4.", GlassRules.OverLine(s, false, false));
        s.result = " 해가 떴어! 밤비를 지켜 냈어. ";
        Assert.Equal("해가 떴어! 밤비를 지켜 냈어.", GlassRules.OverLine(s, true, true));
        Assert.Equal("좋아, Vampire Night 하자!", GlassRules.StartLine("Vampire Night", true));
        Assert.Equal("Okay, let's play Hail Day!", GlassRules.StartLine("Hail Day", false));
    }

    [Fact]
    public void The_state_travels_whole_in_its_message_and_in_the_snapshot()
    {
        var s = Running("pc");
        GlassRules.Merge(s, new GlassState { runId = s.runId, phase = "choose", score = 5, lives = 3, level = 2, seconds = 21f, choices = new[] { "A", "B", "C" }, choiceNotes = new[] { "a", "b", "c" }, banner = "Pick one" });
        var env = Proto.Parse(Proto.Pack(Msg.GlassState, s));
        Assert.Equal("glass.state", env.type);
        var back = env.Body<GlassState>();
        Assert.Equal("survival", back.game); Assert.Equal("pc", back.side); Assert.Equal("M1", back.monitorId); Assert.Equal(s.runId, back.runId);
        Assert.Equal("choose", back.phase); Assert.Equal(new[] { "A", "B", "C" }, back.choices); Assert.Equal("Pick one", back.banner); Assert.Equal(50, back.best);

        var snap = Proto.FromJson<StateSnapshot>(Proto.ToJson(new StateSnapshot { glass = s, show = new ShowInfo { kind = "look", seq = 3, monitorId = "M1", u = 0.1f, v = 0.2f, w = 0.4f, h = 0.4f, seconds = 8 } }));
        Assert.Equal("game-vampire", snap.glass.packId);
        Assert.Equal("look", snap.show.kind); Assert.Equal(0.4f, snap.show.w);

        // a hand report and a command, as the clients send them
        var hand = new GlassHand { monitorId = "M1", t = 12.5, pointValid = true, pointU = 0.3f, pointV = 0.6f };
        hand.hands[0] = new GlassHandPoint { valid = true, u = 0.4f, v = 0.5f, dist = 0.12f, spread = 0.8f, pinch = true };
        var h2 = Proto.Parse(Proto.Pack(Msg.GlassHand, hand)).Body<GlassHand>();
        Assert.True(h2.hands[0].valid); Assert.False(h2.hands[1].valid); Assert.Equal(0.12f, h2.hands[0].dist); Assert.True(h2.pointValid);
        var cmd = Proto.Parse(Proto.Pack(Msg.GlassCommand, new GlassCommand { cmd = "choose", pick = 2, how = "the bag" })).Body<GlassCommand>();
        Assert.Equal("choose", cmd.cmd); Assert.Equal(2, cmd.pick);
    }

    // ------------------------------------------------------------------ which game a wish means (GlassRules.PickGame)
    /// <summary>The user's bag on the evening of 2026-11-17: their own '오목놀이' is the pack the maker gave them then - a "pop" game.</summary>
    static List<GamePackDef> Evening() => new()
    {
        Pack("game-vampire", "survival", "Vampire Night"),
        Pack("game-tennis", "tennis", "Tennis"),
        Pack("game-umbrella", "umbrella", "Shadow Umbrella"),
        Pack("game-rps", "rps", "Rock Paper Scissors"),
        Pack("game-383ce164", "pop", "오목놀이"),
    };

    static readonly string[] OpenAiGames = { "", "rps", "pop", "redlight", "keeper", "stars", "vampire", "umbrella" };

    [Fact]
    public void Omok_asked_for_never_starts_vampire_night()
    {
        // The exact case: "좋아, 밤비야 오목 해보자." - the model answered playGame with thing = '오목놀이' and, next to
        // it, game = "vampire" (the field has to be one of a list). The host took game first: Vampire Night started.
        var bag = Evening();
        var pick = GlassRules.PickGame(bag, "vampire", "오목놀이", "좋아, 밤비야 오목 해보자.");
        Assert.Equal("game-383ce164", pick.packId);
        Assert.Equal("its title", pick.how);
        // it is a game of the headset's own (a "pop" pack): the headset starts it by the pack's id - the host starts no glass game for it
        Assert.Equal("game-383ce164", pick.name);
        Assert.Null(GlassRules.FindPack(bag, "오목")); Assert.Null(GlassRules.FindPack(bag, "오목 해보자")); Assert.Null(GlassRules.FindPack(bag, "오목놀이"));
        // whatever the model put into game - and when it gave no words at all, or no model was asked
        foreach (string game in OpenAiGames)
            Assert.Equal("game-383ce164", GlassRules.PickGame(bag, game, "오목놀이", "좋아, 밤비야 오목 해보자.").packId);
        Assert.Equal("game-383ce164", GlassRules.PickGame(bag, "", "", "좋아, 밤비야 오목 해보자.").packId);
        Assert.Equal("game-383ce164", GlassRules.PickGame(bag, "", "", "오목 하자").packId);
        Assert.Equal("game-383ce164", GlassRules.PickGame(bag, "", "", "밤비야, 오목놀이 하자!").packId);
        Assert.Equal("game-383ce164", GlassRules.PickGame(bag, "", "오목", "우리 그거 하자").packId);
        // a pack of that title that IS a glass game (whatever way of playing it has) is found by the glass host the same way (lpctl glass start "오목", the menu)
        var glass = Evening(); glass[4] = Pack("game-383ce164", "umbrella", "오목놀이");
        Assert.Equal("game-383ce164", GlassRules.PickGame(glass, "vampire", "오목놀이", "좋아, 밤비야 오목 해보자.").packId);
        Assert.Equal("game-383ce164", GlassRules.FindPack(glass, "오목")?.id);
        Assert.Equal("game-383ce164", GlassRules.FindPack(glass, "오목 해보자")?.id);
        // and with no pack of that name in the bag: nothing is started, nothing is offered - the partner says it has no such game
        var none = Evening(); none.RemoveAll(g => g.id == "game-383ce164");
        foreach (string game in OpenAiGames)
        {
            var p = GlassRules.PickGame(none, game, "오목", "좋아, 밤비야 오목 해보자.");
            Assert.False(p.Found, $"game '{game}': {p.packId} was started for omok"); Assert.Equal(StarterGames.GomokuId, p.offer);
            p = GlassRules.PickGame(none, game, "오목놀이", "오목 해보자");
            Assert.False(p.Found); Assert.Equal(StarterGames.GomokuId,p.offer);
        }
    }

    [Fact]
    public void The_title_goes_before_the_kind_and_the_kind_before_the_models_choice()
    {
        var bag = Evening();
        bag.Add(StarterGames.Keeper()); bag.Add(StarterGames.Defense());
        bag.Add(Pack("game-ghosts", "script", "Ghost Hour"));
        Assert.Equal("game-ghosts", GlassRules.PickGame(bag, "keeper", "Ghost Hour", "play ghost hour").packId);
        Assert.Equal("game-383ce164", GlassRules.PickGame(bag,"swarm","오목","오목 하자").packId);
        Assert.Equal("game-swarm", GlassRules.PickGame(bag,"tennis","","악마의 문 하자").packId);
        Assert.Equal("game-keeper", GlassRules.PickGame(bag,"swarm","","골키퍼 하자").packId);
        Assert.Equal("game-keeper", GlassRules.PickGame(bag,"keeper","","다시 하자").packId);
        Assert.Empty(GlassRules.PickGame(bag,"vampire","","다시 하자").packId);
    }

    [Fact]
    public void Nothing_named_in_the_bag_starts_nothing()
    {
        var bag = Evening();
        // a game nobody has: no pack, no offer - the partner says it does not have that game
        foreach (var (game, thing, said) in new[] { ("", "체스", "체스 하자"), ("vampire", "체스", "체스 하자"), ("pop", "Chess", "let's play chess"), ("", "", "우리 뭐 하고 놀까?"), ("", "", ""), ("umbrella", "레이싱 게임", "레이싱 게임 하자"),
                                                    ("", "", "틱택토 하자"), ("vampire", "사목", "사목 하자"), ("rps", "틱택토", "틱택토 하자") })
        {
            var pick = GlassRules.PickGame(bag, game, thing, said);
            Assert.False(pick.Found, $"'{said}' ({game} / {thing}) started {pick.packId}");
            Assert.Equal("", pick.offer);
        }
        foreach (var name in new[] { "그림자 우산", "무궁화 꽃이 피었습니다", "Vampire Night", "TV Rally", "Screen Scamps", "별 터뜨리기" })
        {
            var pick = GlassRules.PickGame(bag, "", "", name);
            Assert.False(pick.Found); Assert.Empty(pick.offer);
        }
        Assert.Equal("game-keeper", GlassRules.PickGame(null, "keeper", "", "").offer);
        Assert.False(GlassRules.PickGame(bag, "", "", "paper airplane game").Found);
        Assert.False(GlassRules.PickGame(bag, "", "", "게임 하자").Found);
    }
}
