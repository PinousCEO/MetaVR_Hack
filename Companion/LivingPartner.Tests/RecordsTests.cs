using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// What the user OWNS and switches on by hand, and what pressing a thing does (2026-11-17, after the first evening
/// with the headset): the surroundings that are installed (world.ambiences) and the records of the record player
/// (world.songs) are found by id or name with no language model in between; music is ONE thing - there is no "Music"
/// skill, and a skill that only plays a tune is a record; a thing on the desk that is pressed is used for what it is
/// for; an older state is put in order when the host starts (MarketRules.Tidy). All of it plain rules, compiled here
/// from the host's own source (WorldRules.cs, MarketCatalog.cs).
/// </summary>
public class RecordsTests
{
    static readonly List<MarketSeedEntry> Seeds = MarketSeed.All();

    /// <summary>What the host does with a state when it starts: the shelf is merged, the Maker puts the built-in skills back, the state is put in order, what is installed joins the world, and what the user owns is listed.</summary>
    static MarketRules.TidyReport Start(MarketState s, WorldState w)
    {
        MarketRules.Merge(s, MarketSeed.All());
        var builtin = WorldDefaults.Skills();
        w.skills.RemoveAll(k => k == null || k.builtin || k.status != "ready" || builtin.Any(b => b.id == k.id));      // (Maker.Restore)
        w.skills.InsertRange(0, builtin);
        var report = MarketRules.Tidy(w, s);
        MarketRules.Sync(w, s);
        MarketRules.Owned(w, s);
        return report;
    }

    static (MarketState s, WorldState w) Fresh()
    {
        var s = new MarketState(); var w = new WorldState();
        Start(s, w);
        return (s, w);
    }

    static ItemDef Thing(string kind, string id = null, string name = null, bool inRoom = true)
    {
        var k = ItemKinds.Find(kind);
        return new ItemDef { id = id ?? "item-" + kind, name = name ?? kind, kind = k.id, uses = (string[])k.uses.Clone(), sizeM = k.sizeM, status = "ready", inRoom = inRoom };
    }

    static SongDef Tune(string id, string name)
    {
        var t = MarketSeed.UmbrellaTune(); t.id = id; t.name = name;
        return t;
    }

    // ------------------------------------------------------------------ surroundings the user owns
    [Fact]
    public void The_surroundings_the_user_owns_are_listed_and_found_without_a_model()
    {
        var (s, w) = Fresh();
        // what came with the partner
        Assert.Equal(new[] { "amb-rain", "amb-night" }, w.ambiences.Select(a => a.id));
        Assert.Null(WorldRules.FindSurroundings(w.ambiences, "amb-peak"));          // on the shelf, not the user's: not switched on by asking for its id
        Assert.Null(WorldRules.FindSurroundings(w.ambiences, "Mountain top"));

        // the Mountain top is bought: from now on it is among them, and found by id, short id or name
        Assert.Equal(Deal.Done, MarketRules.Install(s, "amb-peak", out var peak));
        w.ambience = peak.ambience;                                                 // (switched on once, to show what was got)
        Assert.True(MarketRules.Owned(w, s));
        Assert.Equal(new[] { "amb-rain", "amb-night", "amb-peak" }, w.ambiences.Select(a => a.id));
        foreach (string asked in new[] { "amb-peak", "peak", "Mountain top", "mountain top", " MOUNTAIN-TOP ", "AMB-PEAK" })
            Assert.Equal("amb-peak", WorldRules.FindSurroundings(w.ambiences, asked)?.id);
        Assert.Equal("peak", WorldRules.FindSurroundings(w.ambiences, "amb-peak").pattern);
        Assert.Equal("amb-rain", WorldRules.FindSurroundings(w.ambiences, "rain")?.id);
        Assert.Equal("amb-night", WorldRules.FindSurroundings(w.ambiences, "Night sky")?.id);

        // switched off ("clear") and on again: it STAYS the user's either way
        Assert.True(WorldRules.IsOff("clear")); Assert.True(WorldRules.IsOff("off")); Assert.False(WorldRules.IsOff("amb-peak"));
        w.ambience = null;
        Assert.False(MarketRules.Owned(w, s));                                      // (nothing about what is owned changed)
        Assert.Contains(w.ambiences, a => a.id == "amb-peak");
        w.ambience = WorldRules.FindSurroundings(w.ambiences, "amb-peak");
        Assert.NotNull(w.ambience);
        Assert.False(MarketRules.Owned(w, s));
        Assert.Contains(w.ambiences, a => a.id == w.ambience.id);

        // a description is none of them (that is what a model is for), and nothing is found in nothing
        Assert.Null(WorldRules.FindSurroundings(w.ambiences, "a beach at sunset"));
        Assert.Null(WorldRules.FindSurroundings(w.ambiences, ""));
        Assert.Null(WorldRules.FindSurroundings(null, "amb-peak"));

        // every snapshot carries the list
        var snap = Proto.FromJson<StateSnapshot>(Proto.ToJson(new StateSnapshot { world = w }));
        Assert.Equal(new[] { "amb-rain", "amb-night", "amb-peak" }, snap.world.ambiences.Select(a => a.id));

        // put away in the shop: no longer among them (and it goes off)
        MarketRules.Remove(s, "amb-peak", out var gone);
        Assert.True(MarketRules.Take(w, gone));
        Assert.Null(w.ambience);
        Assert.True(MarketRules.Owned(w, s));
        Assert.DoesNotContain(w.ambiences, a => a.id == "amb-peak");
    }

    [Fact]
    public void Surroundings_the_user_had_made_are_owned_like_the_bought_ones()
    {
        var (s, w) = Fresh();
        var made = new AmbienceDef { id = "amb-1a2b3c4d", name = "밤바다", pattern = "coast", c0 = "#101A3A", c1 = "#C9D6FF", c2 = "#24406E", dim = 1f };
        s.catalogue.Add(new MarketModule { id = made.id, kind = ModuleKinds.Ambience, name = made.name, author = MarketRules.You, ambience = made });
        s.own.Add(made.id); s.installed.Add(made.id);
        Assert.True(MarketRules.Owned(w, s));
        Assert.Equal("amb-1a2b3c4d", WorldRules.FindSurroundings(w.ambiences, "밤바다")?.id);
        Assert.Equal("amb-1a2b3c4d", w.ambiences[^1].id);
    }

    // ------------------------------------------------------------------ the records of the record player
    [Fact]
    public void The_record_player_holds_three_records_and_what_the_user_gets_or_has_made()
    {
        var (s, w) = Fresh();
        Assert.Equal(new[] { "song-club", "song-lofi", "song-lullaby" }, w.songs.Select(x => x.id));
        Assert.Equal(WorldDefaults.Songs().Select(x => x.id), w.songs.Select(x => x.id));

        // by id, short id or name - never a model
        foreach (string asked in new[] { "song-lofi", "lofi", "Lo-fi", "LO FI", " lo-fi " }) Assert.Equal("song-lofi", WorldRules.FindRecord(w.songs, asked)?.id);
        Assert.Equal("song-lullaby", WorldRules.FindRecord(w.songs, "Lullaby")?.id);
        Assert.Null(WorldRules.FindRecord(w.songs, "song-neon"));                   // the DJ's tune: not the user's yet
        Assert.Null(WorldRules.FindRecord(w.songs, "something upbeat for cleaning"));
        Assert.Null(WorldRules.FindRecord(w.songs, "")); Assert.Null(WorldRules.FindRecord(null, "lofi"));
        Assert.True(WorldRules.IsOff("stop"));

        // the DJ Booth is installed: its own tune is a record as well. Rainy Focus plays Lo-fi, which is one already.
        MarketRules.Install(s, "skill-dj", out var dj); Assert.True(MarketRules.Give(w, dj));
        MarketRules.Install(s, "skill-rainyfocus", out var focus); Assert.True(MarketRules.Give(w, focus));
        Assert.True(MarketRules.Owned(w, s));
        Assert.Equal(new[] { "song-club", "song-lofi", "song-lullaby", "song-neon" }, w.songs.Select(x => x.id));
        Assert.Equal("song-neon", WorldRules.FindRecord(w.songs, "Neon")?.id);

        // a tune the user had made is theirs to play again
        Assert.True(MarketRules.AddRecord(s, Tune("song-1a2b3c4d", "신나는 노래")));
        Assert.False(MarketRules.AddRecord(s, Tune("song-1a2b3c4d", "again")));     // once
        Assert.False(MarketRules.AddRecord(s, WorldDefaults.Lofi()));               // what comes with the partner is a record already
        var broken = Tune("song-broken01", "broken"); broken.melody = new[] { 60, 62, 64 };
        Assert.False(MarketRules.AddRecord(s, broken));                             // nothing unsound gets in
        Assert.False(MarketRules.AddRecord(s, Tune("../evil", "x")));
        Assert.True(MarketRules.Owned(w, s));
        Assert.Equal(new[] { "song-club", "song-lofi", "song-lullaby", "song-1a2b3c4d", "song-neon" }, w.songs.Select(x => x.id));
        Assert.Equal("song-1a2b3c4d", WorldRules.FindRecord(w.songs, "신나는 노래")?.id);

        // what is playing is one of them (the headset marks it by its id), and "stop" takes nothing away
        w.song = WorldRules.FindRecord(w.songs, "song-neon"); w.songAt = 12.5;
        Assert.Contains(w.songs, x => x.id == w.song.id);
        w.song = null;
        Assert.False(MarketRules.Owned(w, s));
        Assert.Equal(5, w.songs.Count);

        // every snapshot carries them, playable as they are
        var snap = Proto.FromJson<StateSnapshot>(Proto.ToJson(new StateSnapshot { world = w }));
        Assert.Equal(w.songs.Select(x => x.id), snap.world.songs.Select(x => x.id));
        Assert.All(snap.world.songs, x => Assert.Equal("", MarketRules.SongProblem(x)));

        // the DJ Booth is put away: its tune goes with it
        MarketRules.Remove(s, "skill-dj", out _); MarketRules.Take(w, dj);
        Assert.True(MarketRules.Owned(w, s));
        Assert.DoesNotContain(w.songs, x => x.id == "song-neon");
    }

    [Fact]
    public void The_users_own_records_have_an_end_and_survive_a_restart()
    {
        var (s, w) = Fresh();
        for (int i = 0; i < WorldRules.MaxOwnRecords + 3; i++) Assert.True(MarketRules.AddRecord(s, Tune("song-own" + i, "Own " + i)));
        Assert.Equal(WorldRules.MaxOwnRecords, s.records.Count);
        Assert.DoesNotContain(s.records, r => r.id == "song-own0");                 // the oldest gave way
        Assert.Contains(s.records, r => r.id == "song-own" + (WorldRules.MaxOwnRecords + 2));
        MarketRules.Owned(w, s);
        Assert.InRange(w.songs.Count, 3, WorldRules.MaxRecords);
        Assert.Equal(w.songs.Count, w.songs.Select(x => x.id).Distinct().Count());

        // saved and read again
        var again = Proto.FromJson<MarketState>(Proto.ToJson(s));
        again.records.Add(null); again.records.Add(Tune("song-own5", "twice")); again.records.Add(new SongDef { id = "song-empty", name = "nothing in it" });
        var w2 = Proto.FromJson<WorldState>(Proto.ToJson(w));
        Start(again, w2);
        Assert.Equal(s.records.Select(r => r.id), again.records.Select(r => r.id));
        Assert.Equal(w.songs.Select(x => x.id), w2.songs.Select(x => x.id));
        // a state file from before the records has none, and that is fine
        var old = new MarketState { records = null }; var w3 = new WorldState();
        Start(old, w3);
        Assert.Empty(old.records); Assert.Equal(3, w3.songs.Count);
    }

    // ------------------------------------------------------------------ music is one thing
    [Fact]
    public void A_skill_that_only_plays_a_tune_is_a_record_and_one_that_does_more_is_a_skill()
    {
        static SkillDef K(params string[] acts) => new SkillDef { id = "skill-x", name = "x", status = "ready", song = MarketSeed.NeonTune(), steps = acts.Select(a => { var p = a.Split(':'); return new SkillStep { act = p[0], arg = p.Length > 1 ? p[1] : "" }; }).ToList() };
        Assert.True(WorldRules.TuneOnly(K("music")));
        Assert.True(WorldRules.TuneOnly(K("music", "emote:happy")));                // (what the old built-in "Music" skill was)
        Assert.True(WorldRules.TuneOnly(K("goto:item-record", "music:song-lofi", "say:들어 봐!", "wait")));
        Assert.True(WorldRules.TuneOnly(WorldDefaults.Music()));
        Assert.False(WorldRules.TuneOnly(K("music", "dance")));                     // it dances: a real skill
        Assert.False(WorldRules.TuneOnly(K("ambience", "music")));                  // it changes the surroundings
        Assert.False(WorldRules.TuneOnly(K("music", "activity:study")));
        Assert.False(WorldRules.TuneOnly(K("music", "rain:on")));
        Assert.False(WorldRules.TuneOnly(K("music:stop")));                         // it plays nothing
        Assert.False(WorldRules.TuneOnly(K("emote:wave", "say:안녕")));
        Assert.False(WorldRules.TuneOnly(K())); Assert.False(WorldRules.TuneOnly(null));
        // what the makers sell as skills are skills: each also changes the surroundings and dances, studies or naps
        foreach (string id in new[] { "skill-dance", "skill-dj", "skill-rainyfocus", "skill-lullaby" })
        {
            var m = Seeds.First(e => e.module.id == id).module;
            Assert.False(MarketRules.IsRecord(m), id);
            Assert.Equal(m.name, MarketRules.Listing(m, false).name);
        }
        Assert.DoesNotContain(Seeds, e => e.module.id == "skill-music");
        Assert.DoesNotContain(WorldDefaults.Skills(), k => k.id == "skill-music");

        // the record it becomes: its own tune under the SKILL'S name; a tune that exists already is that record
        var own = K("music", "emote:happy"); own.id = "skill-5e6f7a8b"; own.name = "아침 음악"; own.song.id = "song-skill-5e6f7a8b"; own.song.name = "Tune";
        var record = WorldRules.RecordOf(own, WorldDefaults.Songs());
        Assert.Equal("song-skill-5e6f7a8b", record.id); Assert.Equal("아침 음악", record.name);
        Assert.NotSame(own.song, record); Assert.Equal(own.song.melody, record.melody); Assert.Equal("Tune", own.song.name);
        var named = K("goto:item-record", "music:song-lofi");
        Assert.Equal("song-lofi", WorldRules.RecordOf(named, WorldDefaults.Songs()).id);
        Assert.Equal("Lo-fi", WorldRules.RecordOf(named, WorldDefaults.Songs()).name);
        Assert.Null(WorldRules.RecordOf(K("music:song-nobody-has"), WorldDefaults.Songs()));
        Assert.Null(WorldRules.RecordOf(K("music", "dance"), WorldDefaults.Songs()));
    }

    [Fact]
    public void A_tune_only_skill_module_is_sold_and_installed_as_a_record()
    {
        var (s, w) = Fresh();
        var k = new SkillDef
        {
            id = "skill-9f8e7d6c", name = "자장가 한 곡", note = "Plays a lullaby.", phrases = new[] { "자장가 한 곡" }, status = "ready", song = Tune("song-skill-9f8e7d6c", "Tune"),
            steps = new List<SkillStep> { new SkillStep { act = "music" }, new SkillStep { act = "emote", arg = "happy", seconds = 2 } },
        };
        var m = new MarketModule { id = k.id, kind = ModuleKinds.Skill, name = "자장가 한 곡", summary = "A lullaby.", gives = "When asked, Nilo puts a tune on.", author = "minsu", price = 5, skill = k };
        Assert.Equal("", MarketRules.Problem(m));
        Assert.True(MarketRules.IsRecord(m));
        // in the shop it is a record, not a skill to learn
        var listing = MarketRules.Listing(m, false);
        Assert.Equal("Record: 자장가 한 곡", listing.name); Assert.Equal(MarketRules.RecordGives, listing.gives);
        Assert.Equal("a new record for the record player.", MarketRules.RecordGives.ToLowerInvariant());
        Assert.True(MarketRules.IsRecord(ModuleKinds.Skill, MarketPack.Detail(m).steps));                    // (what the hub shows of it before it is bought says the same)
        Assert.False(MarketRules.IsRecord(ModuleKinds.Skill, MarketPack.Detail(Seeds.First(e => e.module.id == "skill-dj").module).steps));
        Assert.False(MarketRules.IsRecord(ModuleKinds.Game, MarketPack.Detail(m).steps)); Assert.False(MarketRules.IsRecord(ModuleKinds.Skill, null));

        // installing adds the record; no skill joins the skills - however many skills there are
        for (int i = 0; i < WorldRules.MaxSkills; i++) w.skills.Add(new SkillDef { id = "skill-full" + i, name = "Full " + i, steps = new List<SkillStep> { new SkillStep { act = "dance" } } });
        Assert.Equal("", MarketRules.BagProblem(w, m));
        s.catalogue.Add(m); s.owned.Add(m.id);
        Assert.Equal(Deal.Done, MarketRules.Install(s, m.id, out _));
        Assert.False(MarketRules.Give(w, m));
        Assert.DoesNotContain(w.skills, x => x.id == m.id);
        Assert.True(MarketRules.Owned(w, s));
        var record = Assert.Single(w.songs, x => x.id == "song-skill-9f8e7d6c");
        Assert.Equal("자장가 한 곡", record.name);
        Assert.Equal(record.id, WorldRules.FindRecord(w.songs, "자장가 한 곡")?.id);
        // the next start leaves it exactly so
        Start(s, w);
        Assert.Contains(m.id, s.installed); Assert.DoesNotContain(w.skills, x => x.id == m.id); Assert.Single(w.songs, x => x.id == record.id);
        Assert.Empty(s.records);                                                    // (a record of its module: not one of the user's own)
        // put away: the record goes
        MarketRules.Remove(s, m.id, out _);
        Assert.True(MarketRules.Owned(w, s));
        Assert.DoesNotContain(w.songs, x => x.id == record.id);
    }

    // ------------------------------------------------------------------ an older state is put in order
    /// <summary>A state as the host saved it before 2026-11-17 evening: the "Music" skill and "Dust Bunnies" on the shelf and installed, no record player, nothing tidied.</summary>
    static (MarketState s, WorldState w) Before()
    {
        var s = new MarketState();
        MarketRules.Merge(s, MarketSeed.All());
        s.installed.Remove("item-record");                                          // (it was on the shelf then, for 25 stars, and not the user's)
        s.catalogue.Add(new MarketModule { id = "skill-music", kind = ModuleKinds.Skill, name = "Music", author = MarketSeed.Author, builtin = true, skill = WorldDefaults.Music() });
        var dust = new GamePackDef { id = "game-dustbunnies", template = "survival", title = "Dust Bunnies", song = Tune("song-dustbunnies", "Dust Bunnies") };
        s.catalogue.Add(new MarketModule { id = dust.id, kind = ModuleKinds.Game, name = dust.title, author = MarketSeed.Author, builtin = true, price = 30, game = dust });
        foreach (string id in new[] { "skill-music", "game-dustbunnies" }) { s.installed.Add(id); s.owned.Add(id); s.seeded.Add(id); }
        s.fetchLater.Add("game-dustbunnies");
        var w = new WorldState();
        w.skills.Add(WorldDefaults.Dance()); w.skills.Add(WorldDefaults.Music());
        w.games.Add(MarketRules.Clone(dust));
        w.abilities.Add(new AbilityDef { id = "ability-calc", name = "Pocket calculator", prompt = "x" });
        return (s, w);
    }

    [Fact]
    public void The_music_skill_and_dust_bunnies_are_gone_from_an_older_state()
    {
        var (s, w) = Before();
        w.doingSkill = "skill-music"; w.doing = "skill:skill-music";
        w.song = Tune("song-dustbunnies", "Dust Bunnies");
        Start(s, w);
        foreach (string id in MarketSeed.Retired)
        {
            Assert.DoesNotContain(s.catalogue, m => m.id == id);
            Assert.DoesNotContain(id, s.installed); Assert.DoesNotContain(id, s.owned); Assert.DoesNotContain(id, s.fetchLater);
            Assert.Null(MarketRules.Find(s, id));
        }
        Assert.Contains("skill-music", MarketSeed.Retired); Assert.All(StarterGames.RetiredIds, id => Assert.Contains(id, MarketSeed.Retired));
        Assert.Equal(new[] { "skill-dance" }, w.skills.Select(k => k.id));
        Assert.Equal(4, w.games.Count); Assert.All(w.games, g => Assert.True(StarterGames.IsStarter(g.id)));
        Assert.Equal("", w.doingSkill); Assert.Equal("", w.doing);
        // what the Music skill played is still there to play: Lo-fi is a record
        Assert.Contains(w.songs, x => x.id == "song-lofi");
        Assert.DoesNotContain(w.songs, x => x.id == "song-dustbunnies");
        Assert.DoesNotContain(s.catalogue, m => m.id == "game-vampire" || m.game?.template == "survival");
        // and it stays so
        var report = Start(s, w);
        Assert.False(report.Changed);
        Assert.Equal(new[] { "skill-dance" }, w.skills.Select(k => k.id));
    }

    [Fact]
    public void An_older_state_gets_the_record_player_once_unless_it_has_a_thing_for_music()
    {
        // none in the bag: the one that comes with the partner is put into it
        var (s, w) = Before();
        Assert.False(s.recordPlayerGiven);
        var report = Start(s, w);
        Assert.True(s.recordPlayerGiven);
        Assert.Contains("item-record", s.installed);
        var player = Assert.Single(w.items, i => i.id == "item-record");
        Assert.False(player.inRoom); Assert.Equal("ready", player.status); Assert.Contains(ItemUses.Music, player.uses);
        Assert.Contains(report.notes, n => n.Contains("record player"));
        // thrown away afterwards: it stays away
        MarketRules.Remove(s, "item-record", out var m); MarketRules.Take(w, m);
        Start(s, w);
        Assert.DoesNotContain(w.items, i => i.id == "item-record"); Assert.DoesNotContain("item-record", s.installed);

        // the user had one made already ('작은 레코드 플레이어'): nothing is added
        var (s2, w2) = Before();
        w2.items.Add(Thing("record", "item-a28c241a", "작은 레코드 플레이어"));
        Start(s2, w2);
        Assert.True(s2.recordPlayerGiven);
        Assert.Equal(new[] { "item-a28c241a" }, w2.items.Select(i => i.id));
        Assert.DoesNotContain("item-record", s2.installed);

        // a brand-new partner has it in the bag from the first day
        var (s3, w3) = Fresh();
        Assert.Contains("item-record", s3.installed); Assert.True(s3.recordPlayerGiven);
        Assert.False(Assert.Single(w3.items).inRoom);
        Assert.Equal(0, Seeds.First(e => e.module.id == "item-record").module.price);
        Assert.True(Seeds.First(e => e.module.id == "item-record").preinstalled);
    }

    [Fact]
    public void A_taught_skill_that_only_played_a_tune_becomes_a_record_when_the_host_starts()
    {
        var (s, w) = Before();
        w.items.Add(Thing("record", "item-a28c241a", "작은 레코드 플레이어"));
        // "아침 음악": walks to the record player and plays its own tune
        w.skills.Add(new SkillDef
        {
            id = "skill-aaaa1111", name = "아침 음악", status = "ready", itemId = "item-a28c241a", icon = "skill-aaaa1111.png", song = Tune("song-skill-aaaa1111", "Tune"),
            steps = new List<SkillStep> { new SkillStep { act = "goto", arg = "item-a28c241a" }, new SkillStep { act = "music" }, new SkillStep { act = "emote", arg = "happy", seconds = 2 } },
        });
        // "로파이 틀기": plays a tune that is a record already
        w.skills.Add(new SkillDef { id = "skill-bbbb2222", name = "로파이 틀기", status = "ready", steps = new List<SkillStep> { new SkillStep { act = "music", arg = "song-lofi" }, new SkillStep { act = "say", arg = "틀었어!" } } });
        // "신나는 음악": plays the club tune AND dances - a real skill, it stays
        w.skills.Add(new SkillDef { id = "skill-54af5757", name = "신나는 음악", status = "ready", itemId = "item-a28c241a", steps = new List<SkillStep> { new SkillStep { act = "goto", arg = "item-a28c241a" }, new SkillStep { act = "music", arg = "song-club" }, new SkillStep { act = "dance" } } });
        w.doingSkill = "skill-aaaa1111"; w.doing = "skill:skill-aaaa1111";

        var report = Start(s, w);
        Assert.Equal(new[] { "skill-dance", "skill-54af5757" }, w.skills.Select(k => k.id));
        var record = Assert.Single(s.records);
        Assert.Equal("song-skill-aaaa1111", record.id); Assert.Equal("아침 음악", record.name);
        Assert.Equal(new[] { "song-club", "song-lofi", "song-lullaby", "song-skill-aaaa1111" }, w.songs.Select(x => x.id));
        Assert.Equal("아침 음악", WorldRules.FindRecord(w.songs, "아침 음악").name);
        Assert.Equal("", w.doingSkill); Assert.Equal("", w.doing);
        Assert.Contains("skill-aaaa1111.png", report.files);                        // its sticker is nobody's any more
        Assert.Equal(2, report.notes.Count(n => n.Contains("only played a tune")));
        // again: nothing left to do, nothing doubled
        Assert.False(Start(s, w).Changed);
        Assert.Single(s.records); Assert.Equal(4, w.songs.Count);
    }

    [Fact]
    public void A_pack_that_is_forgotten_is_gone_from_the_shelf_the_books_and_the_bag()
    {
        // "사탕 유령": a survival pack the user had made, installed, on sale, its tune playing
        var (s, w) = Fresh();
        var pack = new GamePackDef { id = "game-ddeb15b1", template = "survival", title = "사탕 유령", howTo = "유령을 쫓아내요.", enemyName = "꼬마 유령", pickupName = "사탕", icon = "market-game-ddeb15b1.png", song = Tune("song-game-ddeb15b1", "사탕 유령") };
        var m = new MarketModule { id = pack.id, kind = ModuleKinds.Game, name = pack.title, author = MarketRules.You, price = 20, game = pack };
        Assert.Equal("", MarketRules.Problem(m));
        s.catalogue.Add(m); s.own.Add(m.id); s.installed.Add(m.id); s.fetchLater.Add(m.id);
        var keeper = MarketRules.Find(s,"game-keeper");
        MarketRules.Sync(w, s);
        Assert.Equal(5,w.games.Count); Assert.Contains(w.games, g => g.id == pack.id);
        w.song = w.games.First(g => g.id == pack.id).song;

        Assert.True(MarketRules.Forget(w, s, "game-ddeb15b1"));
        Assert.DoesNotContain(s.catalogue, x => x.id == pack.id);
        Assert.DoesNotContain(pack.id, s.installed); Assert.DoesNotContain(pack.id, s.own); Assert.DoesNotContain(pack.id, s.owned); Assert.DoesNotContain(pack.id, s.fetchLater);
        Assert.All(w.games, g => Assert.True(StarterGames.IsStarter(g.id))); Assert.Equal(4, w.games.Count);       // ONE survival pack is left: Vampire Night
        Assert.Null(w.song);
        Assert.Null(MarketRules.Find(s, "사탕 유령", null));
        Assert.DoesNotContain(MarketRules.List(s), l => l.name.Contains("사탕"));
        // it does not come back, and there is nothing to forget twice
        Start(s, w);
        Assert.All(w.games, g => Assert.True(StarterGames.IsStarter(g.id))); Assert.Equal(4, w.games.Count);
        Assert.False(MarketRules.Forget(w, s, "game-ddeb15b1"));
        Assert.False(MarketRules.Forget(w, s, "")); Assert.False(MarketRules.Forget(null, null, "game-vampire"));
        Assert.NotNull(keeper);
    }

    [Fact]
    public void The_same_thing_twice_is_brought_down_to_one_once()
    {
        var (s, w) = Before();
        // a cactus bought from somebody (in the bag), and the user's own small cactus (on the desk, sculpted)
        var bought = Thing("plant", "item-6160a33f", "선인장 화분", inRoom: false); bought.icon = "market-item-6160a33f.png";
        s.catalogue.Add(new MarketModule { id = bought.id, kind = ModuleKinds.Item, name = bought.name, author = "테스트이웃", price = 10, item = MarketRules.Clone(bought) });
        s.installed.Add(bought.id); s.owned.Add(bought.id);
        var own = Thing("plant", "item-1cc0b355", "작은 선인장 화분"); own.icon = "item-1cc0b355.png";
        own.parts = new List<ShapePart> { new ShapePart(), new ShapePart(), new ShapePart() };
        // two lamps of the user's own: the one with a picture stays
        var lampA = Thing("lamp", "item-11111111", "Desk lamp", inRoom: false); lampA.model = "item-11111111.glb";
        var lampB = Thing("lamp", "item-22222222", "Lamp", inRoom: false); lampB.icon = "item-22222222.png";
        // alike in name, different in kind: both stay ("책상" is a desk, "책" a book)
        var desk = Thing("desk", "item-a52d813d", "책상"); var book = Thing("book", "item-book0001", "책");
        // same kind, names that have nothing of each other: both stay
        var umbrella = Thing("toy", "item-d5df29cd", "빨간 우산"); var puppy = Thing("toy", "item-1cbabe59", "강아지 인형");
        w.items.AddRange(new[] { desk, bought, book, umbrella, lampA, puppy, own, lampB });
        // two skills of one name (the one with a sticker stays), one of them tied to the lamp that goes
        w.skills.Add(new SkillDef { id = "skill-00000001", name = "집중 모드", status = "ready", itemId = "item-11111111", steps = new List<SkillStep> { new SkillStep { act = "goto", arg = "item-11111111" }, new SkillStep { act = "rain", arg = "on" } } });
        w.skills.Add(new SkillDef { id = "skill-00000002", name = "집중  모드!", status = "ready", icon = "skill-00000002.png", itemId = "item-11111111", steps = new List<SkillStep> { new SkillStep { act = "goto", arg = "item-11111111" }, new SkillStep { act = "dance" } } });
        // two abilities of one name: the first stays
        var twin = new AbilityDef { id = "ability-1a2b3c4d", name = "pocket CALCULATOR", prompt = "When the user asks you to add numbers, you add them up." };
        s.catalogue.Add(new MarketModule { id = twin.id, kind = ModuleKinds.Ability, name = twin.name, author = MarketRules.You, ability = twin });
        s.own.Add(twin.id); s.installed.Add(twin.id); w.abilities.Add(MarketRules.Clone(twin));

        Assert.True(MarketRules.Alike("선인장 화분", "작은 선인장 화분")); Assert.True(MarketRules.Alike("Lamp", "desk-lamp")); Assert.True(MarketRules.Alike("의자", "의자"));
        Assert.False(MarketRules.Alike("빨간 우산", "강아지 인형")); Assert.False(MarketRules.Alike("", "의자")); Assert.False(MarketRules.Alike("a", "abc"));

        var report = Start(s, w);
        Assert.True(s.duplicatesTidied);
        // the cactus on the desk is kept; the bought one is uninstalled (it stays the user's on the shelf)
        // (and, there being no thing for music among them, the record player that comes with the partner has joined the bag)
        Assert.Equal(new[] { "item-a52d813d", "item-book0001", "item-d5df29cd", "item-1cbabe59", "item-1cc0b355", "item-22222222", "item-record" }, w.items.Select(i => i.id));
        Assert.DoesNotContain("item-6160a33f", s.installed); Assert.Contains("item-6160a33f", s.owned); Assert.Contains(s.catalogue, m => m.id == "item-6160a33f");
        // the lamp of the user's own that went: its files are nobody's any more (a module's files are the shelf's, and stay)
        Assert.Contains("item-11111111.glb", report.files);
        Assert.DoesNotContain(report.files, f => f.StartsWith("market-"));
        // skills: one "집중 모드" is left, and it no longer walks to the lamp that is gone
        var skill = Assert.Single(w.skills, k => !k.builtin);
        Assert.Equal("skill-00000002", skill.id); Assert.Equal("", skill.itemId); Assert.Equal(new[] { "dance" }, skill.steps.Select(st => st.act));
        // abilities: one calculator
        Assert.Equal(new[] { "ability-calc" }, w.abilities.Select(a => a.id));
        Assert.DoesNotContain("ability-1a2b3c4d", s.installed);
        Assert.Equal(4, report.notes.Count(n => n.Contains("kept")));

        // ONCE: what the user makes alike afterwards is theirs to keep
        w.items.Add(Thing("plant", "item-33333333", "선인장"));
        var later = Start(s, w);
        Assert.False(later.Changed);
        Assert.Contains(w.items, i => i.id == "item-33333333"); Assert.Contains(w.items, i => i.id == "item-1cc0b355");
    }

    // ------------------------------------------------------------------ a thing on the desk is pressed
    static string Press(WorldState w, ItemDef item, string owner = Env.Mr) => WorldRules.UseDecision(w, item, owner);

    [Fact]
    public void Pressing_a_thing_uses_it_for_what_it_is_for()
    {
        var w = new WorldState();
        ItemDef chair = Thing("chair"), bed = Thing("bed"), sofa = Thing("sofa"), book = Thing("book"), ball = Thing("ball"), toy = Thing("toy"), lamp = Thing("lamp"),
                plant = Thing("plant"), cup = Thing("cup"), cake = Thing("food"), box = Thing("box"), record = Thing("record"), speaker = Thing("speaker");
        w.items.AddRange(new[] { chair, bed, sofa, book, ball, toy, lamp, plant, cup, cake, box, record, speaker });
        Assert.Equal("sit", Press(w, chair));                   // a seat
        Assert.Equal("nap", Press(w, bed));                     // a bed is slept in (though one can sit on its edge)
        Assert.Equal("sit", Press(w, sofa));                    // a sofa is sat on (though one can doze on it)
        Assert.Equal("read", Press(w, book));
        Assert.Equal("play", Press(w, ball)); Assert.Equal("play", Press(w, toy));
        Assert.Equal("light", Press(w, lamp));                  // nothing to do with these but to enjoy them: a reaction, different for each
        Assert.Equal("decor", Press(w, plant)); Assert.Equal("decor", Press(w, box));
        Assert.Equal("eat", Press(w, cup)); Assert.Equal("eat", Press(w, cake));
        Assert.Equal("music:play", Press(w, record)); Assert.Equal("music:play", Press(w, speaker));

        // with a desk out as well: the chair is for studying - and so is the desk; the sofa and the bed are what they were
        var desk = Thing("desk"); w.items.Add(desk);
        Assert.Equal("study", Press(w, chair)); Assert.Equal("study", Press(w, desk));
        Assert.Equal("sit", Press(w, sofa)); Assert.Equal("nap", Press(w, bed));
        // a desk in the bag does not make a chair a study chair
        desk.inRoom = false;
        Assert.Equal("sit", Press(w, chair));

        // a desk with no seat out: it says what is missing
        var bare = new WorldState(); var lone = Thing("desk"); bare.items.Add(lone); bare.items.Add(Thing("chair", inRoom: false));
        Assert.Equal("missing:study", Press(bare, lone));

        // a thing of the user's own making is used for what the model said it is for, whatever its shape
        var guitar = Thing("toy", "item-04879770", "작은 통기타"); guitar.uses = new[] { ItemUses.Music, ItemUses.Decor }; w.items.Add(guitar);
        Assert.Equal("music:play", Press(w, guitar));
        var cushion = Thing("box", "item-cushion1", "방석"); cushion.uses = new[] { ItemUses.Sit }; w.items.Add(cushion);
        Assert.Equal("sit", Press(w, cushion));
        var odd = Thing("box", "item-odd00001", "?"); odd.uses = new string[0]; w.items.Add(odd);
        Assert.Equal("decor", Press(w, odd));
        // every use there is leads somewhere
        Assert.All(ItemKinds.All, k => Assert.NotEqual("", Press(w, Thing(k.id, "item-any-" + k.id))));
    }

    [Fact]
    public void Pressed_again_while_it_is_doing_exactly_that_with_that_thing_it_stops()
    {
        var w = new WorldState();
        ItemDef first = Thing("chair", "item-chair-a", "의자"), second = Thing("chair", "item-chair-b", "빨간 의자"), ball = Thing("ball"), record = Thing("record");
        w.items.AddRange(new[] { first, second, ball, record });
        Assert.Equal("sit", Press(w, second));
        // the chair that was pressed is the one it sits on, though another stands in front of it in the list
        Assert.Equal("item-chair-a", Activities.Find(w.items, "sit").itemIds[0]);
        Assert.True(WorldRules.Prefer(w.items, second, WorldRules.UseIn("sit", second)));
        Assert.Equal("item-chair-b", Activities.Find(w.items, "sit").itemIds[0]);
        Assert.False(WorldRules.Prefer(w.items, second, WorldRules.UseIn("sit", second)));        // (it is the first already)
        Assert.Equal(4, w.items.Count);
        w.doing = "sit"; w.doingAt = 3;
        Assert.True(WorldRules.DoingWith(w, second)); Assert.False(WorldRules.DoingWith(w, first));
        Assert.Equal("stop", Press(w, second));                 // again: it gets up
        Assert.Equal("sit", Press(w, first));                   // the other chair: it moves over
        Assert.Equal("play", Press(w, ball));                   // another thing: it turns to that
        // a desk comes out while it sits: the chair is for studying now, which it is not doing yet
        var desk = Thing("desk"); w.items.Add(desk);
        Assert.Equal("study", Press(w, second));
        w.doing = "study";
        Assert.Equal("stop", Press(w, second)); Assert.Equal("stop", Press(w, desk));             // either of the two it studies with
        // while a skill runs nothing is "exactly that": the thing is simply used
        w.doingSkill = "skill-dance"; w.doing = "skill:skill-dance";
        Assert.Equal("study", Press(w, second));
        w.doingSkill = ""; w.doing = "";
        Assert.Equal("study", Press(w, second));

        // a thing for music (the headset opens the records for it; should "use" arrive all the same): the first record, or off
        Assert.Equal("music:play", Press(w, record));
        w.song = WorldDefaults.Lofi();
        Assert.Equal("music:stop", Press(w, record));
        Assert.Equal("study", Press(w, second));                // (the tune is the room's: it changes nothing about the chair)
    }

    [Fact]
    public void Inside_the_PC_the_partner_cannot_use_the_things_on_the_desk()
    {
        var w = new WorldState();
        foreach (var k in ItemKinds.All) w.items.Add(Thing(k.id));
        Assert.All(w.items, i => Assert.Equal("inside", Press(w, i, Env.Pc)));
        Assert.All(w.items, i => Assert.NotEqual("inside", Press(w, i, Env.Mr)));
        // and what is not on the desk, or not finished, is not used either
        var packed = Thing("chair", "item-packed01", inRoom: false); w.items.Add(packed);
        Assert.Equal("bag", Press(w, packed));
        var raw = Thing("chair", "item-raw00001"); raw.status = "making"; w.items.Add(raw);
        Assert.Equal("making", Press(w, raw)); Assert.Equal("making", Press(w, raw, Env.Pc));
        Assert.Equal("", Press(w, null)); Assert.Equal("", WorldRules.UseDecision(null, packed, Env.Mr));
        // the passing skill of a thing being looked at is nobody's to ask for
        w.skills.Add(new SkillDef { id = WorldRules.UseSkillId, name = "Lamp", builtin = true, status = "use", phrases = new[] { "lamp" } });
        Assert.Null(WorldRules.FindSkill(w.skills, "Lamp")); Assert.Null(WorldRules.FindSkill(w.skills, WorldRules.UseSkillId));
        MarketRules.Owned(w, new MarketState());
        Assert.Equal(3, w.songs.Count);
    }

    // ------------------------------------------------------------------ the glass games on the shelf
    [Fact]
    public void The_previous_survival_game_is_absent_from_the_shelf_bag_and_offers()
    {
        var (s,w) = Fresh();
        Assert.DoesNotContain(Seeds, e => e.module.id == "game-vampire" || e.module.id == "game-umbrella");
        Assert.All(w.games, g => Assert.True(StarterGames.IsStarter(g.id)));
        Assert.Equal(4,w.games.Count);
        Assert.Null(MarketRules.Find(s, "뱀파이어 나이트", null));
        Assert.Empty(GlassRules.PickGame(w.games, "", "", "Vampire Night").offer);
    }

    [Fact]
    public void A_game_made_with_words_is_a_script_and_the_old_ways_of_playing_are_the_makers_own()
    {
        Assert.Equal(new[] { "tennis", "keeper", "swarm", "gomoku", "rps", "pop", "redlight", "survival", "umbrella", "script" }, MarketRules.Templates);
        // a pack of one of the makers' ways of playing (one from before games were scripts) is still sound, and carries no program
        foreach (string template in new[] { "survival", "umbrella", "pop" })
        {
            var g = new GamePackDef { id = "game-1a2b3c4d", template = template, title = "우박 우산", howTo = "손으로 우박을 막아요.", enemyName = "우박", pickupName = "별" };
            Assert.Equal("", MarketRules.GameProblem(g));
            var m = new MarketModule { id = g.id, kind = ModuleKinds.Game, name = g.title, game = g };
            g.script = "function update(dt) end";       // (nothing would run it: taken out when the module comes from somewhere else)
            Assert.Equal("", MarketPack.Sanitise(m)); Assert.Equal("", m.game.script);
        }
        Assert.NotEqual("", MarketRules.GameProblem(new GamePackDef { id = "game-x1", template = "chess", title = "x" }));
        Assert.NotEqual("", MarketRules.GameProblem(new GamePackDef { id = "game-x1", template = "wipe", title = "x" }));      // the wipe game is gone
        Assert.NotEqual("", MarketRules.GameProblem(new GamePackDef { id = "game-x1", template = "board", title = "x" }));     // and no board game took its place
    }

    // ------------------------------------------------------------------ "did it hear me?"
    [Fact]
    public void What_was_heard_and_what_was_made_of_it_travel_with_the_snapshot()
    {
        // the words: one line, 80 characters at most
        Assert.Equal("밤비야 노래 틀어 줘", WorldRules.Heard("  밤비야\n노래   틀어 줘 "));
        string longOne = string.Concat(Enumerable.Repeat("가나다라마바사아자차", 12));
        Assert.Equal(80, WorldRules.Heard(longOne).Length); Assert.Equal(WorldRules.HeardMax, WorldRules.Heard(longOne).Length);
        Assert.Equal("", WorldRules.Heard(null));
        // what was made of them
        Assert.Equal("not for me", WorldRules.HeardNote("voice:microphone-open", "not for me"));      // its name was not said
        Assert.Equal("", WorldRules.HeardNote("voice", "none"));                                       // a plain answer
        Assert.Equal("", WorldRules.HeardNote("voice", "unknown")); Assert.Equal("", WorldRules.HeardNote("voice", null));
        Assert.Equal("playMusic", WorldRules.HeardNote("voice", "playMusic"));                        // the action taken
        Assert.Equal("enterPortal", WorldRules.HeardNote("voice:test-file", "enterPortal"));
        // what was typed or sent by a tool was not HEARD: there is nothing to note
        Assert.Null(WorldRules.HeardNote("companion-text", "playMusic")); Assert.Null(WorldRules.HeardNote("", "none")); Assert.Null(WorldRules.HeardNote(null, "none"));

        var ch = new CharacterInfo { heard = WorldRules.Heard("밤비야 산 정상으로 가자"), heardNote = WorldRules.HeardNote("voice:microphone-open", "setAmbience"), heardAt = 1234.5 };
        var json = JObject.Parse(Proto.ToJson(new StateSnapshot { character = ch }));
        Assert.Equal("밤비야 산 정상으로 가자", (string)json["character"]["heard"]);
        Assert.Equal("setAmbience", (string)json["character"]["heardNote"]);
        Assert.Equal(1234.5, (double)json["character"]["heardAt"]);
        var back = Proto.FromJson<StateSnapshot>(json.ToString()).character;
        Assert.Equal(ch.heard, back.heard); Assert.Equal(ch.heardNote, back.heardNote); Assert.Equal(ch.heardAt, back.heardAt);
        // nothing heard yet: empty, not missing
        var fresh = new CharacterInfo();
        Assert.Equal("", fresh.heard); Assert.Equal("", fresh.heardNote); Assert.Equal(0, fresh.heardAt);
    }
}
