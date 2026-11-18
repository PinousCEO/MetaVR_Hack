using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// The marketplace's shelf and its books (MarketCatalog.cs, compiled here from the host's own source): what comes with
/// the partner is sound - every module has one payload that fits its kind, and every tune and every surroundings in it
/// would pass the rules a model-written one has to pass - and installing, removing and wrapping keep the installed
/// list and the world's lists in step. (The stars are the hub's: HubTests.cs.)
/// </summary>
public class MarketTests
{
    static MarketState Fresh()
    {
        var s = new MarketState();
        MarketRules.Merge(s, MarketSeed.All());
        return s;
    }

    static IEnumerable<SongDef> SeedSongs() =>
        MarketSeed.All().Select(e => e.module).SelectMany(m => new[] { m.song, m.skill?.song, m.game?.song }).Where(s => s != null);

    static IEnumerable<AmbienceDef> SeedAmbiences() =>
        MarketSeed.All().Select(e => e.module).SelectMany(m => new[] { m.ambience, m.skill?.ambience, m.game?.ambience }).Where(a => a != null);

    // ------------------------------------------------------------------ the shelf as it comes
    [Fact]
    public void Every_built_in_module_is_sound_and_has_exactly_the_payload_of_its_kind()
    {
        var seeds = MarketSeed.All();
        Assert.NotEmpty(seeds);
        foreach (var e in seeds)
        {
            var m = e.module;
            Assert.True(MarketRules.Problem(m) == "", $"{m.id}: {MarketRules.Problem(m)}");
            Assert.True(m.builtin);
            Assert.Equal(MarketSeed.Author, m.author);
            Assert.False(string.IsNullOrWhiteSpace(m.summary), m.id + " has no summary");
            Assert.False(string.IsNullOrWhiteSpace(m.gives), m.id + " does not say what it gives");
            Assert.Equal("", m.icon);      // no picture at first: the clients show one by kind until one has been drawn
            int payloads = new object[] { m.item, m.skill, m.ambience, m.game, m.ability }.Count(p => p != null);
            Assert.Equal(1, payloads);
            object own = m.kind switch { ModuleKinds.Item => m.item, ModuleKinds.Skill => m.skill, ModuleKinds.Ambience => m.ambience, ModuleKinds.Game => m.game, _ => m.ability };
            Assert.NotNull(own);
            Assert.False(string.IsNullOrWhiteSpace(e.ko), m.id + " has no Korean line");
            Assert.False(string.IsNullOrWhiteSpace(e.en), m.id + " has no English line");
            Assert.False(string.IsNullOrWhiteSpace(e.koName), m.id + " has no Korean name");
            Assert.False(string.IsNullOrWhiteSpace(e.koSummary), m.id + " has no Korean summary");
            Assert.False(string.IsNullOrWhiteSpace(e.koGives), m.id + " does not say in Korean what it gives");
            // what is installed from the start never costs anything
            if (e.preinstalled) Assert.Equal(0, m.price);
        }
    }

    [Fact]
    public void Ids_are_unique_and_can_be_file_names()
    {
        var ids = MarketSeed.All().Select(e => e.module.id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (string id in ids) Assert.True(WorldRules.SafeFile("market-" + id + "-pickup.png"), id);
    }

    [Fact]
    public void The_shelf_holds_what_was_promised()
    {
        var seeds = MarketSeed.All();
        string[] Of(string kind) => seeds.Where(e => e.module.kind == kind).Select(e => e.module.id).ToArray();
        Assert.Equal(new[] { "game-keeper", "game-tennis", "game-swarm", "game-gomoku" }, Of(ModuleKinds.Game));       // (the last four are SCRIPTS: the same kind of module the partner writes)
        // (no "Music" skill: music is the record player and its records)
        Assert.Equal(new[] { "skill-dance", "skill-dj", "skill-rainyfocus", "skill-lullaby" }, Of(ModuleKinds.Skill));
        Assert.Equal(WorldDefaults.Ambiences().Select(a => a.id).OrderBy(x => x), Of(ModuleKinds.Ambience).OrderBy(x => x));
        Assert.Equal(new[] { "ability-calc", "ability-translate", "ability-ideas", "ability-proofread" }, Of(ModuleKinds.Ability));
        // one thing of every built-in shape (but the plain box)
        Assert.Equal(ItemKinds.All.Where(k => k.id != "box").Select(k => "item-" + k.id), Of(ModuleKinds.Item));

        var pre = seeds.Where(e => e.preinstalled).Select(e => e.module.id).OrderBy(x => x).ToArray();
        // (no game among them: tennis and the others are free in the shop, and got from there)
        Assert.Equal(new[] { "ability-calc", "amb-night", "amb-rain", "game-gomoku", "game-keeper", "game-swarm", "game-tennis", "item-record", "skill-dance" }, pre);
        Assert.All(seeds.Where(e => e.module.game != null), e => Assert.Equal(0, e.module.price));
        // the desk, the chair and the ball are free, and no thing costs more than 30 stars
        foreach (var e in seeds.Where(e => e.module.kind == ModuleKinds.Item))
        {
            Assert.InRange(e.module.price, 0, 30);
            Assert.False(string.IsNullOrWhiteSpace(e.draw), e.module.id + " has nothing to draw its icon from");
            var kind = ItemKinds.Find(e.module.item.kind);
            Assert.Equal(kind.uses, e.module.item.uses);
        }
        foreach (string free in new[] { "item-desk", "item-chair", "item-ball", "item-record" }) Assert.Equal(0, seeds.First(e => e.module.id == free).module.price);
        Assert.All(seeds.Where(e => e.module.game != null), e => Assert.NotEmpty(e.module.game.howTo));
    }

    [Fact]
    public void Every_tune_on_the_shelf_passes_the_rules_for_a_model_written_one_unchanged()
    {
        var songs = SeedSongs().ToList();
        Assert.Equal(4, songs.Count);      // four skills carry a tune
        foreach (var s in songs)
        {
            Assert.True(MarketRules.SongProblem(s) == "", $"{s.id}: {MarketRules.SongProblem(s)}");
            int steps = s.bars * 16;
            Assert.Equal(steps, s.melody.Length);
            Assert.True(s.bass.Length == 0 || s.bass.Length == steps || s.bass.Length == 16, s.id + ": the bass is neither one bar nor the whole tune");
            Assert.Equal(s.bars, s.chords.Length);
            // what WorldRules would make of it is the tune itself: nothing clamped or cut (a one-bar bass is only written out for every bar)
            var back = WorldRules.Song(JObject.FromObject(s), s.id);
            Assert.NotNull(back);
            Assert.Equal(s.melody, back.melody); Assert.Equal(s.chords, back.chords);
            Assert.Equal(s.bass.Length == 0 ? s.bass : Enumerable.Range(0, steps).Select(i => s.bass[i % s.bass.Length]).ToArray(), back.bass);
            Assert.Equal(s.kick, back.kick); Assert.Equal(s.snare, back.snare); Assert.Equal(s.hat, back.hat);
            Assert.Equal(s.bpm, back.bpm); Assert.Equal(s.lead, back.lead); Assert.Equal(s.minor, back.minor);
            // and the synth makes sound of it
            float[] pcm = SongSynth.Render(s, out float seconds);
            Assert.True(seconds > 2f, s.id + " is too short to be a tune");
            Assert.Contains(pcm, v => Math.Abs(v) > 0.05f);
        }
    }

    [Fact]
    public void Every_surroundings_on_the_shelf_passes_the_rules_unchanged()
    {
        foreach (var a in SeedAmbiences())
        {
            Assert.True(MarketRules.AmbienceProblem(a) == "", $"{a.id}: {MarketRules.AmbienceProblem(a)}");
            var back = WorldRules.Ambience(JObject.FromObject(a), a.id);
            Assert.NotNull(back);
            Assert.Equal(a.pattern, back.pattern); Assert.Equal(a.c0, back.c0); Assert.Equal(a.c1, back.c1); Assert.Equal(a.c2, back.c2);
            Assert.Equal(a.dim, back.dim, 3); Assert.Equal(a.speed, back.speed, 3); Assert.Equal(a.density, back.density, 3);
        }
    }

    [Fact]
    public void A_module_that_is_not_sound_is_told_apart()
    {
        MarketModule Chair() => MarketRules.Clone(MarketSeed.All().First(e => e.module.id == "item-chair").module);
        var m = Chair(); m.skill = WorldDefaults.Dance();
        Assert.Contains("more than one", MarketRules.Problem(m));
        m = Chair(); m.item = null;
        Assert.NotEqual("", MarketRules.Problem(m));
        m = Chair(); m.kind = ModuleKinds.Skill;
        Assert.NotEqual("", MarketRules.Problem(m));
        m = Chair(); m.id = "../chair";
        Assert.NotEqual("", MarketRules.Problem(m));
        m = Chair(); m.item.id = "item-other";
        Assert.NotEqual("", MarketRules.Problem(m));
        m = Chair(); m.price = -5;
        Assert.NotEqual("", MarketRules.Problem(m));

        var song = MarketSeed.VampireTune(); song.melody = song.melody.Take(40).ToArray();
        Assert.Contains("40 steps", MarketRules.SongProblem(song));
        song = MarketSeed.VampireTune(); song.bass = song.bass.Take(48).ToArray();      // three bars under a tune of four: it would not come round with it
        Assert.Contains("48 steps", MarketRules.SongProblem(song));
        song = MarketSeed.NeonTune(); song.kick = "x--x";
        Assert.NotEqual("", MarketRules.SongProblem(song));
        song = MarketSeed.UmbrellaTune(); song.melody[3] = 140;
        Assert.NotEqual("", MarketRules.SongProblem(song));
    }

    // ------------------------------------------------------------------ the shelf over time
    [Fact]
    public void A_new_partner_has_300_stars_and_what_comes_installed()
    {
        var s = Fresh();
        Assert.Equal(300, s.stars);
        Assert.Equal(MarketSeed.All().Count, s.catalogue.Count);
        Assert.Equal(MarketSeed.All().Where(e => e.preinstalled).Select(e => e.module.id), s.installed);
        Assert.DoesNotContain("game-vampire", s.installed);
        Assert.DoesNotContain("item-chair", s.installed);
    }

    [Fact]
    public void Merging_again_changes_nothing_and_a_removed_built_in_stays_removed()
    {
        var s = Fresh();
        Assert.Equal(Deal.Done, MarketRules.Remove(s, "skill-dance", out _));
        Assert.Equal(Deal.Done, MarketRules.Install(s, "item-bed", out _));
        int stars = s.stars; var installed = s.installed.ToList(); int count = s.catalogue.Count;
        MarketRules.Merge(s, MarketSeed.All());
        MarketRules.Merge(s, MarketSeed.All());
        Assert.Equal(stars, s.stars);
        Assert.Equal(installed, s.installed);
        Assert.Equal(count, s.catalogue.Count);
        Assert.DoesNotContain("skill-dance", s.installed);
    }

    [Fact]
    public void Merging_refreshes_what_is_built_in_keeps_its_pictures_and_leaves_the_users_own_alone()
    {
        var s = Fresh();
        // a picture was drawn for the chair and for the bats; somebody edited a built-in text in the state file; a built-in module no longer exists
        MarketRules.Find(s, "item-chair").icon = "market-item-chair.png";
        MarketRules.Find(s, "game-swarm").game.enemyIcon = "market-game-swarm-enemy.png";
        MarketRules.Find(s, "item-desk").gives = "tampered";
        s.catalogue.Add(new MarketModule { id = "item-gone", kind = ModuleKinds.Item, name = "Gone", builtin = true, item = new ItemDef { id = "item-gone", uses = new[] { ItemUses.Decor } } });
        s.installed.Add("item-gone");
        var mine = MarketRules.Wrap(new ItemDef { id = "item-1a2b3c4d", name = "우산", kind = "box", uses = new[] { ItemUses.Decor }, sizeM = 0.06f, status = "ready", note = "우산이다!" }, 5, 1000);
        s.catalogue.Add(mine); s.installed.Add(mine.id);

        MarketRules.Merge(s, MarketSeed.All());
        Assert.Equal("market-item-chair.png", MarketRules.Find(s, "item-chair").icon);
        Assert.Equal("market-game-swarm-enemy.png", MarketRules.Find(s, "game-swarm").game.enemyIcon);
        Assert.NotEqual("tampered", MarketRules.Find(s, "item-desk").gives);
        Assert.Null(MarketRules.Find(s, "item-gone"));
        Assert.DoesNotContain("item-gone", s.installed);
        var kept = MarketRules.Find(s, "item-1a2b3c4d");
        Assert.NotNull(kept);
        Assert.Equal("우산", kept.name); Assert.Equal(5, kept.price); Assert.False(kept.builtin);
        Assert.Contains("item-1a2b3c4d", s.installed);
        // the user's own come after what is built in
        Assert.Equal("item-1a2b3c4d", s.catalogue[^1].id);
    }

    // ------------------------------------------------------------------ installing on this PC (paying is the hub's business: HubTests.cs)
    [Fact]
    public void Installing_and_removing_only_keep_the_list_of_what_is_installed()
    {
        var s = Fresh();
        MarketRules.Remove(s, "game-swarm", out _);
        Assert.Equal(Deal.Done, MarketRules.Install(s, "game-swarm", out var m));
        Assert.Equal("Demon Gate", m.name);
        Assert.Contains("game-swarm", s.installed);
        Assert.Equal(300, s.stars);                        // no stars change hands here any more

        Assert.Equal(Deal.Already, MarketRules.Install(s, "game-swarm", out _));
        Assert.Single(s.installed, id => id == "game-swarm");

        // removing uninstalls and gives nothing back: what was bought stays the user's
        Assert.Equal(Deal.Done, MarketRules.Remove(s, "game-swarm", out _));
        Assert.Equal(300, s.stars);
        Assert.DoesNotContain("game-swarm", s.installed);
        Assert.Equal(Deal.NotInstalled, MarketRules.Remove(s, "game-swarm", out _));

        Assert.Equal(Deal.Unknown, MarketRules.Install(s, "no-such-module", out _));
        Assert.Equal(Deal.Unknown, MarketRules.Remove(s, "no-such-module", out _));
    }

    [Fact]
    public void A_state_file_from_before_the_hub_keeps_what_was_the_users_and_what_was_installed()
    {
        // as the host wrote it before there was a hub: no `own`, no `owned`; everything not built in was made by the user
        var s = new MarketState { own = null, owned = null };
        MarketRules.Merge(s, MarketSeed.All());
        var mine = MarketRules.Wrap(new ItemDef { id = "item-1a2b3c4d", name = "우산", kind = "box", uses = new[] { ItemUses.Decor }, sizeM = 0.06f, status = "ready" }, 0, 1000);
        s.catalogue.Add(mine); s.installed.Add(mine.id); s.installed.Add("amb-peak");
        s.own = null; s.owned = null;
        MarketRules.Merge(s, MarketSeed.All());
        Assert.Equal(new[] { "item-1a2b3c4d" }, s.own);
        Assert.Contains("amb-peak", s.owned);          // paid for with the old stars: theirs for good
        Assert.DoesNotContain("item-1a2b3c4d", s.owned);
        // and from then on the two lists are kept as they are
        s.owned.Add("item-bought"); s.own.Add("item-gone");
        MarketRules.Merge(s, MarketSeed.All());
        Assert.Contains("item-bought", s.owned);           // (bought on the hub; this PC need not hold a copy of it)
        Assert.DoesNotContain("item-gone", s.own);          // what is not on the shelf cannot be the user's own module
    }

    [Fact]
    public void Everything_the_makers_sell_costs_more_than_a_newcomer_has()
    {
        var s = Fresh();
        int total = s.catalogue.Sum(m => m.price);
        Assert.True(total > MarketRules.StartStars, "the shop should make the user choose");
        Assert.Equal(HubRules.StartStars, MarketRules.StartStars);
        Assert.Equal(HubRules.MaxPrice, MarketRules.MaxPrice);
    }

    // ------------------------------------------------------------------ no game comes with the partner
    [Fact]
    public void A_fresh_bag_has_the_three_native_games_without_placing_any_items()
    {
        var s = Fresh(); var w = new WorldState();
        MarketRules.Sync(w, s);
        Assert.True(s.threeGameLibrary);
        Assert.Equal(new[] { "game-keeper", "game-tennis", "game-swarm", "game-gomoku" }, w.games.Select(g => g.id));
        Assert.All(w.games, g => { Assert.Contains(g.id, s.installed); Assert.Equal(0, MarketRules.Find(s, g.id).price); });
        Assert.Contains("skill-dance", s.installed); Assert.Contains("ability-calc",s.installed);
        Assert.DoesNotContain(w.items, i => i.inRoom);
    }

    [Fact]
    public void Existing_private_games_are_archived_intact_once_and_removed_starters_stay_removed()
    {
        var s = new MarketState { own = new(), owned = new() };
        var old = new MarketModule { id = "game-mine", name = "My game", kind = ModuleKinds.Game, game = new GamePackDef { id = "game-mine", title = "My game", template = "script", script = GlassScriptExamples.Shooter, icon = "my-cover.png" } };
        s.catalogue.Add(old); s.installed.Add(old.id); s.own.Add(old.id); s.owned.Add(old.id); s.fetchLater = new() { old.id };
        MarketRules.Merge(s, MarketSeed.All());
        var copy = Assert.Single(s.archivedGames);
        Assert.Equal(Proto.ToJson(old), Proto.ToJson(copy.module));
        Assert.True(copy.installed && copy.owned && copy.mine);
        Assert.DoesNotContain(old.id, s.fetchLater); Assert.DoesNotContain(old.id, s.installed); Assert.Null(MarketRules.Find(s, old.id));
        MarketRules.Remove(s, "game-tennis", out _);
        var loaded = Proto.FromJson<MarketState>(Proto.ToJson(s));
        MarketRules.Merge(loaded, MarketSeed.All());
        Assert.Single(loaded.archivedGames); Assert.DoesNotContain("game-tennis", loaded.installed);
        Assert.Equal(Proto.ToJson(copy), Proto.ToJson(loaded.archivedGames[0]));
    }

    [Fact]
    public void A_game_that_is_not_in_the_bag_is_not_played()
    {
        var s = Fresh(); var w = new WorldState();
        MarketRules.Sync(w,s);
        foreach (string mode in new[] { "tennis", "keeper", "swarm" }) Assert.True(MarketRules.CanPlay(w,mode));
        Assert.Equal("game-keeper", MarketRules.GameModule("keeper"));
        MarketRules.Remove(s, "game-keeper", out _); MarketRules.Sync(w,s);
        Assert.False(MarketRules.CanPlay(w,"keeper")); Assert.True(MarketRules.CanPlay(w,"tennis"));
        Assert.False(MarketRules.HasGame(null, "game-tennis")); Assert.False(MarketRules.HasGame(w, ""));
        Assert.False(MarketRules.CanPlay(w,"game-vampire"));
        Assert.Equal("", MarketRules.GameModule("game-../x"));
        Assert.True(MarketRules.CanPlay(w, ""));
    }

    [Theory]
    [InlineData("응", true)]
    [InlineData("응 받아줘", true)]
    [InlineData("받아와", true)]
    [InlineData("그래, 받아 줘!", true)]
    [InlineData("좋아 깔아줘", true)]
    [InlineData("yes please", true)]
    [InlineData("sure, get it", true)]
    [InlineData("아니", false)]
    [InlineData("아니, 됐어", false)]
    [InlineData("나중에", false)]
    [InlineData("no thanks", false)]
    public void The_answer_to_shall_I_get_it_is_understood_in_plain_words(string said, bool get)
    {
        Assert.Equal(get, GameWish.Understand(said));
    }

    [Theory]
    [InlineData("")]
    [InlineData("어")]
    [InlineData("받아쓰기 하자")]
    [InlineData("아니 그거 말고 가위바위보 하자")]
    [InlineData("오늘 뭐 먹지")]
    public void What_is_not_an_answer_to_it_is_left_alone(string said)
    {
        Assert.Null(GameWish.Understand(said));
    }

    [Fact]
    public void The_question_about_a_game_is_open_for_a_while_and_answered_once()
    {
        var wish = new GameWish();
        Assert.False(wish.Open); Assert.False(wish.Listening(0));
        wish.Ask("game-tennis", "Tennis", 100);
        Assert.True(wish.Open); Assert.True(wish.Listening(100)); Assert.True(wish.Listening(125)); Assert.False(wish.Listening(126));
        Assert.True(wish.Fresh(200)); Assert.False(wish.Fresh(221));
        Assert.Equal("game-tennis", wish.Take());
        Assert.False(wish.Open); Assert.False(wish.Listening(101)); Assert.Equal("", wish.Take());
    }

    // ------------------------------------------------------------------ the world
    [Fact]
    public void At_start_up_everything_installed_is_in_the_world_once()
    {
        var s = Fresh();
        var w = new WorldState();
        w.skills.AddRange(WorldDefaults.Skills());        // as the Maker leaves it
        MarketRules.Install(s, "game-tennis", out _); MarketRules.Install(s, "game-pop", out _);        // (two games the user got from the shop)
        Assert.True(MarketRules.Sync(w, s));
        Assert.Equal(new[] { "game-keeper", "game-tennis", "game-swarm", "game-gomoku" }, w.games.Select(g => g.id));
        Assert.Equal(new[] { "ability-calc" }, w.abilities.Select(a => a.id));
        Assert.Equal(new[] { "skill-dance" }, w.skills.Select(k => k.id));
        Assert.Equal(new[] { "item-record" }, w.items.Select(i => i.id));       // the record player comes with the partner
        Assert.False(w.items[0].inRoom);
        // a second time: nothing to do, nothing doubled
        Assert.False(MarketRules.Sync(w, s));
        Assert.Equal(4, w.games.Count); Assert.Single(w.skills); Assert.Single(w.items);
    }

    [Fact]
    public void A_removed_built_in_skill_does_not_come_back_and_the_users_own_things_are_left_alone()
    {
        var s = Fresh();
        foreach (string id in MarketSeed.OnceBundledGames) MarketRules.Install(s, id, out _);        // (the user got all four from the shop ...)
        MarketRules.Remove(s, "skill-dance", out _);
        MarketRules.Remove(s, "game-rps", out _);                                                     // (... and put one away again)
        var w = new WorldState();
        w.skills.AddRange(WorldDefaults.Skills());        // the Maker puts it back at every start
        w.skills.Add(new SkillDef { id = "skill-own1", name = "집중 모드", steps = new List<SkillStep> { new SkillStep { act = "rain", arg = "on" } } });
        w.items.Add(new ItemDef { id = "item-deadbeef", name = "우산", kind = "box", uses = new[] { ItemUses.Decor } });
        w.games.Add(new GamePackDef { id = "game-stale", template = "pop", title = "Stale" });
        MarketRules.Sync(w, s);
        Assert.Equal(new[] { "skill-own1" }, w.skills.Select(k => k.id));
        Assert.Equal(new[] { "item-deadbeef", "item-record" }, w.items.Select(i => i.id));
        Assert.DoesNotContain(w.games, g => g.id == "game-rps");
        Assert.DoesNotContain(w.games, g => g.id == "game-stale");      // a game exists only as an installed module
        Assert.Equal(4, w.games.Count);
    }

    [Fact]
    public void Installing_gives_each_kind_its_place_in_the_world_and_removing_takes_it_back()
    {
        var s = Fresh();
        var w = new WorldState();
        w.skills.AddRange(WorldDefaults.Skills());
        MarketRules.Sync(w, s);
        w.skills.Add(new SkillDef { id = "skill-own1", name = "own" });

        // a thing: into the bag, ready, one copy
        MarketRules.Install(s, "item-chair", out var chair);
        Assert.True(MarketRules.Give(w, chair, 1234, "의자", "의자다!"));
        var item = Assert.Single(w.items, i => i.id != "item-record");
        Assert.Equal("item-chair", item.id); Assert.Equal("의자", item.name); Assert.Equal("의자다!", item.note);
        Assert.False(item.inRoom); Assert.Equal("ready", item.status); Assert.Equal(1234, item.madeUnixMs);
        Assert.Equal(new[] { ItemUses.Sit }, item.uses);
        Assert.False(MarketRules.Give(w, chair));
        Assert.Single(w.items, i => i.id == "item-chair");
        // the copy in the world is the world's: changing it leaves the shelf as it was
        item.name = "changed"; item.uses[0] = "table";
        Assert.Equal("Chair", chair.item.name); Assert.Equal(ItemUses.Sit, chair.item.uses[0]);

        // a skill: among the skills (a bought one after the built-in ones and the user's)
        MarketRules.Install(s, "skill-dj", out var dj);
        Assert.True(MarketRules.Give(w, dj));
        Assert.Equal(new[] { "skill-dance", "skill-own1", "skill-dj" }, w.skills.Select(k => k.id));
        Assert.NotNull(w.skills[^1].song); Assert.NotNull(w.skills[^1].ambience);

        // a game: on the Play page, with everything the headset reads from the pack
        var keeper = MarketRules.Find(s, "game-keeper");
        Assert.True(MarketRules.Take(w, keeper));
        Assert.True(MarketRules.Give(w, keeper)); Assert.False(MarketRules.Give(w, keeper));
        var pack = w.games[^1];
        Assert.Equal("game-keeper", pack.id); Assert.Equal("keeper", pack.template); Assert.Equal("Goalkeeper", pack.title);

        // an ability: among the abilities
        MarketRules.Install(s, "ability-translate", out var tr);
        Assert.True(MarketRules.Give(w, tr));
        Assert.Equal(new[] { "ability-calc", "ability-translate" }, w.abilities.Select(a => a.id));

        // surroundings are not kept in the world; taking them away turns them off when they are on
        var aurora = MarketRules.Find(s, "amb-aurora");
        Assert.False(MarketRules.Give(w, aurora));
        w.ambience = aurora.ambience;
        Assert.True(MarketRules.Take(w, aurora));
        Assert.Null(w.ambience);

        // and back
        w.doingSkill = "skill-dj"; w.doing = "skill:skill-dj";
        Assert.True(MarketRules.Take(w, chair)); Assert.DoesNotContain(w.items, i => i.id == "item-chair");
        Assert.True(MarketRules.Take(w, dj)); Assert.DoesNotContain(w.skills, k => k.id == "skill-dj"); Assert.Equal("", w.doingSkill); Assert.Equal("", w.doing);
        Assert.True(MarketRules.Take(w, keeper)); Assert.DoesNotContain(w.games, g => g.id == "game-keeper");
        Assert.True(MarketRules.Take(w, tr)); Assert.Equal(new[] { "ability-calc" }, w.abilities.Select(a => a.id));
        Assert.False(MarketRules.Take(w, tr));
        // one of the makers' own games is got and removed like any other
        MarketRules.Install(s, "game-tennis", out var tennis);
        Assert.False(MarketRules.Give(w, tennis)); Assert.Contains(w.games, g => g.id == "game-tennis");
        Assert.True(MarketRules.Take(w, tennis));
        Assert.Equal(new[]{"game-swarm","game-gomoku"},w.games.Select(g=>g.id));
    }

    // ------------------------------------------------------------------ publishing
    [Fact]
    public void Publishing_wraps_a_thing_of_the_users_into_a_module_of_theirs()
    {
        var item = new ItemDef
        {
            id = "item-1a2b3c4d", name = "빨간 소파", asked = "빨간 소파 하나 만들어 줘", kind = "sofa", uses = new[] { ItemUses.Sit, ItemUses.Sleep }, sizeM = 0.15f, color = "#F4A9A0",
            note = "소파다! 이제 앉아서 쉴 수 있겠다.", status = "ready", model = "item-1a2b3c4d.glb", icon = "item-1a2b3c4d.png", inRoom = true, x = 0.2f, z = 0.1f, yaw = 20,
        };
        var m = MarketRules.Wrap(item, 12, 5000);
        Assert.Equal("", MarketRules.Problem(m));
        Assert.Equal("item-1a2b3c4d", m.id); Assert.Equal(ModuleKinds.Item, m.kind); Assert.Equal("빨간 소파", m.name);
        Assert.Equal(MarketRules.You, m.author); Assert.False(m.builtin); Assert.Equal(12, m.price); Assert.Equal(5000, m.publishedUnixMs);
        Assert.Equal("item-1a2b3c4d.png", m.icon);
        Assert.Equal("소파다! 이제 앉아서 쉴 수 있겠다.", m.summary);
        Assert.Equal("Nilo can sit on it and nap on it.", m.gives);
        // the payload is a copy that goes into a bag, not the thing on the desk
        Assert.NotSame(item, m.item);
        Assert.False(m.item.inRoom); Assert.Equal(0f, m.item.x); Assert.Equal("item-1a2b3c4d.glb", m.item.model);
        item.name = "changed";
        Assert.Equal("빨간 소파", m.item.name);

        var listing = MarketRules.Listing(m, true);
        Assert.True(listing.mine); Assert.False(listing.builtin); Assert.True(listing.installed); Assert.Equal("", listing.template);

        // a silly price is brought into range
        Assert.Equal(MarketRules.MaxPrice, MarketRules.Wrap(item, 1_000_000, 0).price);
        Assert.Equal(0, MarketRules.Wrap(item, -3, 0).price);

        // kept on this PC as a module, it can be removed and installed again like any other, and comes back into the bag
        var s = Fresh();
        s.catalogue.Add(m); s.installed.Add(m.id);
        var w = new WorldState();
        Assert.Equal(Deal.Done, MarketRules.Remove(s, m.id, out _));
        Assert.Equal(Deal.Done, MarketRules.Install(s, m.id, out _));
        Assert.Equal(300, s.stars);                      // one's own costs nothing, whatever price it is sold at
        Assert.True(MarketRules.Give(w, m));
        Assert.Equal("빨간 소파", w.items[0].name);
    }

    [Fact]
    public void Publishing_wraps_a_taught_skill_and_says_what_it_does()
    {
        var skill = new SkillDef
        {
            id = "skill-9f8e7d6c", name = "집중 모드", asked = "앞으로 집중 모드라고 하면 비 내리고 조용한 노래 틀어줘", note = "비를 내리고 조용한 노래를 튼다.", phrases = new[] { "집중 모드", "focus mode" },
            steps = new List<SkillStep> { new SkillStep { act = "rain", arg = "on" }, new SkillStep { act = "music", arg = "song-lofi" }, new SkillStep { act = "activity", arg = "study" } },
        };
        var m = MarketRules.Wrap(skill, 0, 1);
        Assert.Equal("", MarketRules.Problem(m));
        Assert.Equal(ModuleKinds.Skill, m.kind); Assert.Equal("skill-9f8e7d6c", m.id); Assert.Equal(MarketRules.You, m.author);
        Assert.Equal("When asked, Nilo lets it rain on the desk, puts a tune on and studies.", m.gives);
        Assert.Equal(new[] { "집중 모드", "focus mode" }, m.tags);
        Assert.NotSame(skill, m.skill);
    }

    // ------------------------------------------------------------------ what the clients see
    [Fact]
    public void The_listing_puts_what_is_installed_first_then_goes_by_kind()
    {
        var s = Fresh();
        MarketRules.Install(s, "item-chair", out _);
        var list = MarketRules.List(s);
        Assert.Equal(s.catalogue.Count, list.Count);
        int firstOff = list.FindIndex(l => !l.installed);
        Assert.Equal(s.installed.Count, firstOff);
        Assert.All(list.Skip(firstOff), l => Assert.False(l.installed));
        foreach (var part in new[] { list.Take(firstOff).ToList(), list.Skip(firstOff).ToList() })
        {
            var kinds = part.Select(l => Array.IndexOf(ModuleKinds.All, l.kind)).ToList();
            Assert.Equal(kinds.OrderBy(k => k), kinds);
        }
        Assert.Equal("item-chair", list[0].id);                    // things come first among what is installed
        foreach (var l in list.Where(l => l.kind == ModuleKinds.Game)) Assert.Contains(l.template, MarketRules.Templates);
        Assert.Equal("swarm", list.First(l => l.id == "game-swarm").template);
        Assert.All(list.Where(l => l.kind != ModuleKinds.Game), l => Assert.Equal("", l.template));
        Assert.All(list, l => { Assert.True(l.builtin); Assert.False(l.mine); });
    }

    [Theory]
    [InlineData("item-chair", "item-chair")]
    [InlineData("Chair", "item-chair")]
    [InlineData("의자", "item-chair")]
    [InlineData("의자 사줘", "item-chair")]
    [InlineData("Demon Gate", "game-swarm")]
    [InlineData("악마의 문 깔아줘", "game-swarm")]
    [InlineData("디펜스", "game-swarm")]
    [InlineData("the record player please", "item-record")]
    [InlineData("레코드 플레이어", "item-record")]
    [InlineData("번역기", "ability-translate")]
    [InlineData("DJ booth", "skill-dj")]
    [InlineData("오로라", "amb-aurora")]
    [InlineData("pocket calculator", "ability-calc")]
    public void A_module_is_found_by_what_people_call_it(string said, string id)
    {
        var s = Fresh();
        Assert.Equal(id, MarketRules.Find(s, said, null)?.id);
    }

    [Fact]
    public void Nothing_is_found_for_something_that_is_not_on_the_shelf()
    {
        var s = Fresh();
        Assert.Null(MarketRules.Find(s, "submarine", null));
        Assert.Null(MarketRules.Find(s, "", null));
        Assert.Null(MarketRules.Find(s, "  ", null));
    }

    [Fact]
    public void Among_several_that_fit_the_preferred_ones_come_first()
    {
        var s = Fresh();
        // three places are a "view": the first on the shelf is meant - unless it is installed already and the user is buying
        Assert.Equal("amb-peak", MarketRules.Find(s, "view", null).id);
        Assert.Equal("amb-peak", MarketRules.Find(s, "view", m => !s.installed.Contains(m.id)).id);
        MarketRules.Install(s, "amb-peak", out _);
        Assert.Equal("amb-coast", MarketRules.Find(s, "view", m => !s.installed.Contains(m.id)).id);
        Assert.Equal("amb-peak", MarketRules.Find(s, "view", m => s.installed.Contains(m.id)).id);
        // there is ONE survival game, and nothing answers to the "Music" skill or to "Dust Bunnies" any more
        Assert.Null(MarketRules.Find(s, "서바이벌", null));
        Assert.Null(MarketRules.Find(s, "skill-music")); Assert.Null(MarketRules.Find(s, "game-dustbunnies")); Assert.Null(MarketRules.Find(s, "Dust Bunnies", null));
        Assert.DoesNotContain(s.catalogue, m => m.name == "Music" || m.name == "Dust Bunnies");
    }

    // ------------------------------------------------------------------ what a model wrote
    [Fact]
    public void A_game_the_partner_wrote_is_a_sound_module_of_the_template_script()
    {
        var g = MarketRules.ScriptGame("game-test1234", " 유령의 밤 ", "유령을 쫓아내!", GlassScriptExamples.Find("catch").lua);
        Assert.NotNull(g);
        Assert.Equal("script", g.template); Assert.Equal("유령의 밤", g.title); Assert.Equal("유령을 쫓아내!", g.howTo);
        Assert.Equal("", MarketRules.GameProblem(g));
        var m = new MarketModule { id = g.id, kind = ModuleKinds.Game, name = g.title, game = g };
        Assert.Equal("", MarketRules.Problem(m));
        Assert.Equal("", ScriptRules.Problem(m));          // ... and its program runs
        Assert.Equal("script", MarketRules.Listing(m, true).template);
        Assert.Null(MarketRules.ScriptGame("game-test1234", "", "", g.script));
        // a program longer than a module may carry is not a module
        g.script = new string('-', ScriptRules.MaxScriptBytes + 1);
        Assert.Contains("too long", MarketRules.GameProblem(g));
    }

    [Fact]
    public void A_model_written_ability_needs_a_name_and_real_instructions()
    {
        var a = MarketRules.Ability(JObject.Parse(@"{ ""name"": ""끝말잇기"", ""prompt"": ""When the user wants to play word chain, you answer with a word that starts with the last syllable of theirs."", ""examples"": [""끝말잇기 하자"", ""끝말잇기 하자"", ""word chain"", """"] }"), "ability-x1");
        Assert.NotNull(a);
        Assert.Equal("ability-x1", a.id);
        Assert.Equal(new[] { "끝말잇기 하자", "word chain" }, a.examples);
        Assert.Null(MarketRules.Ability(JObject.Parse(@"{ ""name"": """", ""prompt"": ""long enough instructions for the partner"" }"), "a"));
        Assert.Null(MarketRules.Ability(JObject.Parse(@"{ ""name"": ""X"", ""prompt"": ""short"" }"), "a"));
        string huge = new string('a', 5000);
        var cut = MarketRules.Ability(new JObject { ["name"] = "Long", ["prompt"] = huge }, "ability-long");
        Assert.Equal(MarketRules.MaxAbilityPrompt, cut.prompt.Length);
    }

    // ------------------------------------------------------------------ a tune as a file
    [Fact]
    public void A_tune_is_written_as_a_16_bit_mono_wav_of_whole_passes()
    {
        var song = MarketSeed.UmbrellaTune();
        float[] pass = SongSynth.Render(song, out float one);
        byte[] wav = MarketRules.Wav(song, 2, out float seconds);
        Assert.Equal(one * 2, seconds, 3);
        Assert.Equal(44 + pass.Length * 2 * 2, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));                          // PCM
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));                          // mono
        Assert.Equal(SongSynth.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
        // the second pass is the first again, sample for sample
        int half = pass.Length * 2;
        Assert.True(wav.AsSpan(44, half).SequenceEqual(wav.AsSpan(44 + half, half)));
        Assert.Equal((short)Math.Round(Math.Clamp(pass[1000], -1f, 1f) * 32767f), BitConverter.ToInt16(wav, 44 + 2000));
    }

    [Theory]
    [InlineData("Vampire Night", "Vampire-Night")]
    [InlineData("잔잔한 피아노", "잔잔한-피아노")]
    [InlineData("a/b\\c:d*e?", "a-b-c-d-e")]
    [InlineData("...", "tune")]
    [InlineData("", "tune")]
    public void A_tunes_name_becomes_a_file_name(string name, string file)
    {
        Assert.Equal(file, MarketRules.FileName(name, "tune"));
    }
}
