using LivingPartner.Host;
using LivingPartner.MarketHub;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// "Make a game: omok" (2026-11-17) came back as a pack that pops stars. What the host's own rules (MarketCatalog.cs)
/// do about it on the OLD template path: a wish that fits none of the ways of playing makes NO pack, and the partner
/// says so (games of other kinds are to be scripts the partner writes - that maker comes in front of this one; no
/// omok is shipped by hand). The pack the user had made that evening is left exactly as it is. And the wipe game
/// ("Foggy Window") is gone from an older state.
/// </summary>
public class BoardPackTests
{
    const string Id = "game-1a2b3c4d";

    /// <summary>What the model answered that evening, as far as the log tells: a "pop" pack called 오목놀이.</summary>
    static JObject PopAnswer() => JObject.Parse(@"{ ""fits"": true, ""whyNot"": """", ""template"": ""pop"", ""title"": ""오목놀이"", ""howTo"": ""떠오르는 돌을 톡톡 터뜨리고, 별돌을 모아 오목 줄을 이어요."",
        ""enemyName"": ""돌멩이"", ""pickupName"": ""별돌"", ""seconds"": 60, ""speed"": 0.8, ""spawn"": 0.9, ""lives"": 3 }");

    static string Repo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "RUNBOOK.md"))) d = d.Parent;
        Assert.NotNull(d);
        return d.FullName;
    }

    // ------------------------------------------------------------------ honesty: a game is WRITTEN; what never ran is not handed over
    [Fact]
    public void Every_game_made_with_words_is_a_script_and_a_program_that_never_ran_installs_nothing()
    {
        // there is no way of playing to dress any more ("오목 게임" came back as a pack that pops stars): a made game is the template "script"
        Assert.Contains("script", MarketRules.Templates); Assert.Contains("script", MarketRules.GlassTemplates);
        var g = MarketRules.ScriptGame(Id, " 오목놀이 ", "다섯 개를 먼저 이으면 이겨요.", GlassScriptExamples.Omok, new[] { "Stone A", "stone-a", "", "x", "y", "z" });
        Assert.Equal("script", g.template); Assert.Equal("오목놀이", g.title); Assert.Equal(GlassScriptExamples.Omok, g.script); Assert.Equal(Id, g.id);
        Assert.Equal(new[] { "stone-a", "x", "y" }, g.art.Select(a => a.name));     // three pictures at most, each under a name a script can use, none twice
        Assert.Equal("", MarketRules.GameProblem(g));
        Assert.Null(MarketRules.ScriptGame(Id, "", "x", GlassScriptExamples.Omok)); Assert.Null(MarketRules.ScriptGame(Id, "x", "x", "  ")); Assert.Null(MarketRules.ScriptGame(Id, "x", "x", null));
        // a pack that says it is a script and has none is not sound; and there is still no "board" way of playing
        Assert.NotEqual("", MarketRules.GameProblem(new GamePackDef { id = Id, template = "script", title = "x" }));
        Assert.NotEqual("", MarketRules.GameProblem(new GamePackDef { id = Id, template = "board", title = "x" }));
        // the one line the partner says when the program it wrote never got to run: that, and no list of the kinds it "can" make
        string ko = MarketRules.CouldNotWriteLine(true), en = MarketRules.CouldNotWriteLine(false);
        Assert.Contains("안 돌아가", ko); Assert.Contains("couldn't get this one to work", en);
        foreach (string not in new[] { "뱀파이어", "우산", "별 터뜨리기", "vampire", "umbrella", "\n" }) { Assert.DoesNotContain(not, ko); Assert.DoesNotContain(not, en); }
        // the maker's flow is the host's own (read from its source): the writer, the pack of the template "script", the honest line - and no template path
        string market = File.ReadAllText(Path.Combine(Repo(), "Companion", "LivingPartner.Host", "Market.cs"));
        foreach (string word in new[] { "ScriptWriter.WriteAsync(", "MarketRules.ScriptGame(", "MarketRules.CouldNotWriteLine(" }) Assert.Contains(word, market);
        foreach (string gone in new[] { "- fits:", "GameFits(", "CannotBuildLine(", "GameSystem", "GameShape", "- wipe:", "BoardFix", "BoardAsked" }) Assert.DoesNotContain(gone, market);
    }

    // ------------------------------------------------------------------ the pack the user had made that evening
    /// <summary>game-383ce164 '오목놀이' as it stood in the state file on 2026-11-17, 19:53.</summary>
    static MarketModule Evening() => new MarketModule
    {
        id = "game-383ce164", kind = ModuleKinds.Game, name = "오목놀이", summary = "차분한 오목 느낌의 데스크 게임이에요. 돌을 터뜨리며 별돌을 모아 다섯 줄을 완성해요.", gives = "A new game: pop drifting stones and gather star stones.",
        author = "You", icon = "market-game-383ce164.png", price = 0, tags = new[] { "pop", "돌멩이", "별돌" }, publishedUnixMs = 1_800_000_000_000,
        game = new GamePackDef
        {
            id = "game-383ce164", template = "pop", title = "오목놀이", howTo = "떠오르는 돌을 톡톡 터뜨리고, 별돌을 모아 오목 줄을 이어요.", enemyName = "돌멩이", pickupName = "별돌",
            enemyIcon = "market-game-383ce164-enemy.png", pickupIcon = "market-game-383ce164-pickup.png", icon = "market-game-383ce164.png", seconds = 60, speed = 0.8f, spawn = 0.9f, lives = 3,
            song = MarketSeed.UmbrellaTune(), ambience = new AmbienceDef { id = "amb-game-383ce164", name = "오목놀이", pattern = "spots" },
        },
    };

    /// <summary>The state file of that evening, as far as the marketplace's books go: the pack made, installed and the user's own; Foggy Window got from the shop and played.</summary>
    static (MarketState s, WorldState w) EveningState()
    {
        var s = new MarketState();
        MarketRules.Merge(s, MarketSeed.All());
        // (the shelf of the host before this change: it still had the wipe game)
        var wipe = new MarketModule
        {
            id = "game-wipe", kind = ModuleKinds.Game, name = "Foggy Window", summary = "The glass fogs up.", gives = "A new game.", author = MarketSeed.Author, builtin = true, icon = "market-game-wipe.png",
            game = new GamePackDef { id = "game-wipe", template = "wipe", title = "Foggy Window", howTo = "Wipe the fog away and find every star before time runs out.", icon = "market-game-wipe.png", seconds = 60 },
        };
        s.threeGameLibrary = false;
        s.catalogue.Add(wipe); s.catalogue.Add(Evening());
        s.installed.AddRange(new[] { "game-vampire", "game-umbrella", "game-wipe", "game-383ce164" });
        s.own = new List<string> { "game-383ce164" }; s.owned = new List<string> { "game-vampire", "game-umbrella", "game-wipe" };
        s.seeded.Add("game-wipe"); s.fetchLater = new List<string> { "game-wipe" };
        var w = new WorldState();
        w.skills.AddRange(WorldDefaults.Skills());
        w.games.Add(MarketRules.Clone(wipe.game)); w.games.Add(MarketRules.Clone(Evening().game));
        // through the state file and back
        return (Proto.FromJson<MarketState>(Proto.ToJson(s)), Proto.FromJson<WorldState>(Proto.ToJson(w)));
    }

    /// <summary>What the host does with the books when it starts (Market's constructor, in its order).</summary>
    static void Start(MarketState s, WorldState w)
    {
        MarketRules.Merge(s, MarketSeed.All());
        MarketRules.Tidy(w, s);
        MarketRules.Sync(w, s);
        MarketRules.Owned(w, s);
    }

    [Fact]
    public void At_the_next_start_the_users_original_pack_is_preserved_in_an_archive()
    {
        var (s,w) = EveningState();
        Start(s,w);
        var saved = Assert.Single(s.archivedGames, a => a.module.id == "game-383ce164");
        Assert.Equal(Proto.ToJson(Evening()), Proto.ToJson(saved.module));
        Assert.True(saved.mine); Assert.True(saved.installed);
        Assert.DoesNotContain(s.catalogue,m => m.id == saved.module.id || m.id == "game-wipe");
        Assert.Equal(new[] { "game-gomoku", "game-keeper", "game-swarm", "game-tennis" }, w.games.Select(g => g.id).OrderBy(id => id,StringComparer.Ordinal));
        foreach (var id in new[] { "game-383ce164", "game-wipe" }) Assert.False(GlassRules.PickGame(w.games,"",id,"").Found);
        string books = Proto.ToJson(s), world = Proto.ToJson(w.games);
        Start(s,w); Assert.Equal(books, Proto.ToJson(s)); Assert.Equal(world, Proto.ToJson(w.games));
    }

    [Fact]
    public void The_two_player_TV_board_is_shipped_and_the_hub_removes_the_retired_board()
    {
        // The new authenticated TV board has its own ID; retired single-screen board assets stay retired.
        var seeds = MarketSeed.All();
        Assert.DoesNotContain(seeds, x => x.module.id is "game-omok" or "game-tictactoe" or "game-connect4" || x.module.game?.template is "board" or "wipe");
        Assert.Equal(new[] { "game-keeper", "game-tennis", "game-swarm", "game-gomoku" }, seeds.Where(e => e.module.kind == ModuleKinds.Game).Select(e => e.module.id));
        Assert.Equal("gomoku",seeds.Single(x=>x.module.id=="game-gomoku").module.game.template);
        Assert.Null(GlassGames.Create("board")); Assert.Null(GlassGames.Create("wipe"));
        // a hub of this version that starts on older data (Foggy Window on sale; for one run this evening an "Omok" as well): both go off sale
        long clock = 1_000_000;
        var hub = new Hub(new MemoryHubStore(), () => clock += 1000);
        var old = seeds.ToList();
        foreach (var (id, name) in new[] { ("game-wipe", "Foggy Window"), ("game-omok", "Omok"), ("game-umbrella", "Shadow Umbrella") })
        {
            var extra = MarketRules.Clone(seeds.First(x => x.module.id == "game-keeper"));
            extra.module.id = id; extra.module.game.id = id; extra.module.name = name;
            old.Add(extra);
        }
        hub.SeedOfficial(old);
        Assert.Contains(hub.Listings().list, l => l.id == "game-wipe"); Assert.Contains(hub.Listings().list, l => l.id == "game-omok");
        Assert.True(hub.SeedOfficial(seeds) > 0);
        Assert.DoesNotContain(hub.Listings().list, l => l.id is "game-wipe" or "game-omok");
        Assert.DoesNotContain(hub.Listings().list, l => l.id == "game-umbrella");
    }
}
