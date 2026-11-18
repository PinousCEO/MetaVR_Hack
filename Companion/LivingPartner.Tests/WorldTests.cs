using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// The partner's world: what it can do with the things on the desk, where a new thing goes, which built-in shape a
/// description sounds like - and the rules that make a song, surroundings or a skill written by a language model safe
/// to hand to the headset (WorldRules, compiled here from the host's own source).
/// </summary>
public class WorldTests
{
    [Fact]
    public void Starting_the_room_packs_stacked_and_worn_items_without_losing_inventory()
    {
        var world = new WorldState();
        var chair = Thing("chair");
        var cup = Thing("cup");
        cup.onId = chair.id;
        cup.y = 0.08f;
        var toy = Thing("toy");
        toy.wornOn = "handR";
        world.items.AddRange(new[] { chair, cup, toy });
        Assert.Equal(3, world.PackRoom());
        Assert.Equal(0, world.PackRoom());
        Assert.Equal(3, world.items.Count);
        Assert.All(world.items, i => { Assert.False(i.inRoom); Assert.Empty(i.wornOn); Assert.Empty(i.onId); Assert.Equal(0f, i.y); });
    }

    [Fact]
    public void Local_play_starts_with_an_empty_desk_on_first_launch_and_after_a_save()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var first = new LivingPartner.MR.LocalWorld(path, () => 1, (_, _) => { });
            Assert.NotEmpty(first.World.items);
            Assert.All(first.World.items, i => Assert.False(i.inRoom));
            first.Handle(Msg.World, new WorldCommand { cmd = "place", id = "item-chair" });
            Assert.True(first.World.items.Single(i => i.id == "item-chair").inRoom);
            first.Flush();
            var next = new LivingPartner.MR.LocalWorld(path, () => 2, (_, _) => { });
            Assert.True(next.Restored);
            Assert.Equal(first.World.items.Count, next.World.items.Count);
            Assert.All(next.World.items, i => Assert.False(i.inRoom));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("keeper")]
    public void Standalone_bag_starts_the_native_ball_game_and_publishes_the_serve(string mode)
    {
        double now = 100;
        TennisState latest = null;
        var local = new LivingPartner.MR.LocalWorld("", () => now, (type, body) => {
            if (type == Msg.GameState) latest = (TennisState)body;
        });
        Assert.Equal(new[] { "game-keeper", "game-tennis", "game-swarm", "game-gomoku" }, local.World.games.Select(g => g.id));
        local.Handle(Msg.GameCommand, new GameCommand { cmd = "start", room = true, mode = mode, width = .55f, height = .45f, depth = .35f, eye = new V3(0, .2f, -.5f) });
        Assert.True(latest.active); Assert.Equal("ready", latest.phase); Assert.Equal(mode,latest.mode);
        Assert.Equal(Tennis.BallRadius(mode), latest.court.radius);
        now += 2.5; local.Tick();
        Assert.Equal("rally", latest.phase); Assert.Equal("partner", latest.shot.by);
        local.Handle(Msg.GameCommand, new GameCommand { cmd = "stop" });
        Assert.False(latest.active); Assert.Equal("off",latest.phase);
    }

    static ItemDef Thing(string kindId, string name = null, bool inRoom = true)
    {
        var k = ItemKinds.Find(kindId);
        return new ItemDef { id = "item-" + (name ?? kindId), name = name ?? kindId, kind = k.id, uses = (string[])k.uses.Clone(), sizeM = k.sizeM, status = "ready", inRoom = inRoom };
    }

    /// <summary>Puts the things out one after the other, each where Arrange says, as the Maker does.</summary>
    static List<ItemDef> Desk(params ItemDef[] things)
    {
        var list = new List<ItemDef>();
        foreach (var t in things) { if (t.inRoom) Activities.Arrange(list, t); list.Add(t); }
        return list;
    }

    // ------------------------------------------------------------------ activities
    [Fact]
    public void A_desk_alone_is_not_enough_to_study()
    {
        var items = Desk(Thing("desk"));
        Assert.Empty(Activities.Possible(items));
        Assert.Null(Activities.Find(items, "study"));
        Assert.Null(Activities.Find(items, "sit"));
    }

    [Fact]
    public void A_desk_and_a_chair_mean_studying_first_and_the_chair_stands_at_the_desk()
    {
        var desk = Thing("desk"); var chair = Thing("chair");
        var items = Desk(desk, chair);
        var can = Activities.Possible(items);
        Assert.Equal("study", can[0].name);
        Assert.Equal(new[] { desk.id, chair.id }, can[0].itemIds);
        Assert.Contains(can, a => a.name == "sit");
        AssertAtTheDesk(desk, chair);
    }

    /// <summary>
    /// The chair is in line with the desk, right at it, squarely turned. On which side of the desk (the user's, or the
    /// far one so that the partner is seen from the front) is the room designer's choice and not pinned down here.
    /// </summary>
    static void AssertAtTheDesk(ItemDef desk, ItemDef chair)
    {
        Assert.Equal(desk.x, chair.x, 4);
        float gap = Math.Abs(chair.z - desk.z);
        Assert.True(gap > 0.01f, "the chair must not stand inside the desk");
        Assert.True(gap < desk.sizeM, $"the chair stands AT the desk, not {gap} m from it");
        Assert.Equal(0f, chair.yaw);
    }

    [Fact]
    public void A_desk_that_comes_after_the_chair_pulls_the_chair_to_it()
    {
        var chair = Thing("chair"); var desk = Thing("desk");
        Desk(chair, desk);
        AssertAtTheDesk(desk, chair);
    }

    [Fact]
    public void A_thing_in_the_bag_does_not_count()
    {
        var items = Desk(Thing("desk"), Thing("chair", inRoom: false));
        Assert.Null(Activities.Find(items, "study"));
        Assert.Null(Activities.Find(items, "sit"));
    }

    [Fact]
    public void Each_kind_of_thing_opens_what_it_is_for()
    {
        var items = Desk(Thing("bed"), Thing("book"), Thing("ball"), Thing("record"));
        var names = Activities.Possible(items).Select(a => a.name).ToList();
        Assert.Equal(new[] { "read", "nap", "sit", "play", "music" }, names);      // a bed can be sat on too; no table, so no study
        Assert.Equal("item-bed", Activities.Find(items, "nap").itemIds[0]);
        Assert.Null(Activities.Find(items, "study"));
        Assert.Null(Activities.Find(null, "nap"));
    }

    [Fact]
    public void Things_that_do_not_belong_together_get_spots_of_their_own()
    {
        var a = Thing("lamp", "lamp-a"); var b = Thing("lamp", "lamp-b"); var c = Thing("plant");
        Desk(a, b, c);
        Assert.True(Math.Abs(a.x - b.x) > 0.01f || Math.Abs(a.z - b.z) > 0.01f);
        Assert.True(Math.Abs(b.x - c.x) > 0.01f || Math.Abs(b.z - c.z) > 0.01f);
        Assert.True(Math.Abs(a.x - c.x) > 0.01f || Math.Abs(a.z - c.z) > 0.01f);
    }

    // ------------------------------------------------------------------ the built-in shapes
    [Theory]
    [InlineData("책상 줄게", "desk")]
    [InlineData("의자 하나 만들어줘", "chair")]
    [InlineData("레코드판 하나 만들어줘", "record")]
    [InlineData("침대도 있으면 좋겠다", "bed")]
    [InlineData("푹신한 소파", "sofa")]
    [InlineData("축구공 줄까?", "ball")]
    [InlineData("A Desk", "desk")]
    [InlineData("here's a comfy chair for you", "chair")]
    [InlineData("make me an old vinyl record player", "record")]
    [InlineData("a little robot", "toy")]
    [InlineData("우산", "box")]
    [InlineData("something nobody has a word for", "box")]
    [InlineData("", "box")]
    [InlineData(null, "box")]
    public void A_description_sounds_like_a_built_in_shape(string text, string kind) => Assert.Equal(kind, ItemKinds.Guess(text).id);

    [Fact]
    public void What_comes_with_the_partner_is_well_formed()
    {
        // the built-in tunes obey the same rules a model-built one is held to
        foreach (var s in WorldDefaults.Songs())
        {
            Assert.InRange(s.bpm, WorldRules.BpmMin, WorldRules.BpmMax);
            Assert.Contains(s.lead, WorldRules.Leads);
            Assert.Equal(s.bars * 16, s.melody.Length);
            Assert.True(s.bass.Length % 16 == 0 && s.chords.Length == s.bars);
            Assert.All(s.melody.Concat(s.bass), n => Assert.True(n <= 0 || (n >= WorldRules.NoteMin && n <= WorldRules.NoteMax), $"{s.id}: note {n}"));
            foreach (string d in new[] { s.kick, s.snare, s.hat }) Assert.True(d.Length == 0 || d.Length == 16 || d.Length == s.bars * 16);
        }
        foreach (var a in WorldDefaults.Ambiences()) Assert.Contains(a.pattern, WorldRules.Patterns);
        foreach (var k in WorldDefaults.Skills())
        {
            Assert.True(k.builtin);
            Assert.All(k.steps, st => Assert.Contains(st.act, WorldRules.Acts));
        }
    }

    // ------------------------------------------------------------------ a tune written by a model
    [Fact]
    public void A_tune_is_clamped_to_what_the_headset_can_play()
    {
        var j = JObject.Parse(@"{ ""name"": ""  Way   too long a name for a little tune badge  "", ""bpm"": 300, ""bars"": 3, ""swing"": 0.9, ""lead"": ""trumpet"", ""minor"": true,
            ""melody"": [60, -1, -7, 0, 130, 24, 100, 5], ""chords"": [48, 200], ""bass"": [], ""kick"": ""x---x---x---x---"", ""snare"": ""----X---"", ""hat"": ""x.x. | x.x."", ""volume"": 9 }");
        var s = WorldRules.Song(j, "song-test");
        Assert.Equal("song-test", s.id);
        Assert.True(s.name.Length <= 24 && !s.name.Contains("  "));
        Assert.Equal(140, s.bpm);
        Assert.Equal(4, s.bars);                         // 3 bars is not a length the headset loops
        Assert.Equal(0.5f, s.swing);
        Assert.Equal("bell", s.lead);                    // an instrument that does not exist
        Assert.True(s.minor);
        Assert.Equal(64, s.melody.Length);               // 8 notes: filled to one bar, repeated over four
        Assert.Equal(new[] { 60, -1, -1, 0, 94, 36, 88, 41 }, s.melody.Take(8).ToArray());   // folded by octaves: 130 -> 94 (still an A#), 24 -> 36, 100 -> 88, 5 -> 41
        Assert.All(s.melody.Skip(8).Take(8), n => Assert.Equal(0, n));
        Assert.Equal(s.melody.Take(16).ToArray(), s.melody.Skip(48).ToArray());
        Assert.Equal(new[] { 48, 92, 48, 92 }, s.chords);
        Assert.Empty(s.bass);
        Assert.Equal("x---x---x---x---", s.kick);
        Assert.Equal("----x-----------", s.snare);       // short: filled to a bar
        Assert.Equal("x-x-x-x---------", s.hat);         // bar lines and spaces dropped, anything but x is a rest
        Assert.Equal(1f, s.volume);
    }

    [Theory]
    [InlineData(10, 60)]
    [InlineData(-5, 60)]
    [InlineData(96, 96)]
    [InlineData(140.4, 140)]
    public void Bpm_stays_between_60_and_140(double asked, int got) => Assert.Equal(got, WorldRules.Song(new JObject { ["bpm"] = asked, ["melody"] = new JArray(60) }, "s").bpm);

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(16, 4)]
    public void A_tune_has_two_or_four_bars(int asked, int got)
    {
        var s = WorldRules.Song(new JObject { ["bars"] = asked, ["melody"] = new JArray(Enumerable.Range(0, 200).Select(i => (object)(60 + i % 5)).ToArray()) }, "s");
        Assert.Equal(got, s.bars);
        Assert.Equal(got * 16, s.melody.Length);          // 200 notes: cut to the length of the tune
    }

    [Fact]
    public void A_hold_with_nothing_to_hold_is_a_rest()
    {
        var s = WorldRules.Song(new JObject { ["bars"] = 2, ["melody"] = new JArray(-1, -1, 62, -1, 0, -1, 64) }, "s");
        Assert.Equal(new[] { 0, 0, 62, -1, 0, 0, 64 }, s.melody.Take(7).ToArray());
        Assert.Equal(32, s.melody.Length);
        // chords the same: one that goes on over the next bar stays a hold, a hold after silence is silence
        var c = WorldRules.Song(new JObject { ["bars"] = 4, ["chords"] = new JArray(-1, 60, -1, 0) }, "s");
        Assert.Equal(new[] { 0, 60, -1, 0 }, c.chords);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, -1)]
    [InlineData(36, 36)]
    [InlineData(96, 96)]
    [InlineData(35, 47)]
    [InlineData(97, 85)]
    [InlineData(1, 37)]
    [InlineData(1000, 88)]
    public void A_note_is_moved_by_octaves_into_range(int n, int got)
    {
        Assert.Equal(got, WorldRules.Note(n));
        if (n > 0) Assert.Equal(n % 12, got % 12);       // it keeps its name
    }

    [Fact]
    public void A_tune_with_nothing_to_hear_is_no_tune()
    {
        Assert.Null(WorldRules.Song(JObject.Parse(@"{ ""bpm"": 100, ""melody"": [0, 0, -1], ""bass"": [], ""chords"": [0, 0], ""kick"": ""----"", ""snare"": """", ""hat"": """" }"), "s"));
        Assert.Null(WorldRules.Song(new JObject(), "s"));
        Assert.Null(WorldRules.Song(null, "s"));
        Assert.Null(WorldRules.Song(new JValue("a happy tune"), "s"));
        // numbers that came as text, a broken entry among them, are still read
        var s = WorldRules.Song(JObject.Parse(@"{ ""bpm"": ""90"", ""bars"": ""2"", ""melody"": [""60"", null, ""x"", 62.4] }"), "s");
        Assert.Equal(90, s.bpm);
        Assert.Equal(new[] { 60, 0, 0, 62 }, s.melody.Take(4).ToArray());
    }

    // ------------------------------------------------------------------ surroundings designed by a model
    [Fact]
    public void Surroundings_are_clamped_to_what_the_headset_can_draw()
    {
        var a = WorldRules.Ambience(JObject.Parse(@"{ ""name"": ""Beach"", ""pattern"": ""Waves"", ""c0"": ""ff00aa"", ""c1"": ""#abc"", ""c2"": ""sunset orange"", ""dim"": 5, ""speed"": 0, ""density"": -2, ""beat"": ""true"" }"), "amb-x");
        Assert.Equal("amb-x", a.id);
        Assert.Equal("none", a.pattern);                 // a pattern the headset does not know: the colours and the dimming remain
        Assert.Equal("#FF00AA", a.c0);
        Assert.Equal("#AABBCC", a.c1);
        Assert.Equal(new AmbienceDef().c2, a.c2);        // not a colour: the default
        Assert.Equal(1f, a.dim);
        Assert.Equal(0.1f, a.speed);
        Assert.Equal(0.1f, a.density);
        Assert.True(a.beat);

        var b = WorldRules.Ambience(JObject.Parse(@"{ ""pattern"": "" Stars "", ""c0"": ""#0c1030ff"", ""dim"": 0.7 }"), "amb-y");
        Assert.Equal("stars", b.pattern);
        Assert.Equal("#0C1030", b.c0);
        Assert.Equal(1f, b.speed);
        Assert.Equal(1f, b.density);
        Assert.False(b.beat);
    }

    [Fact]
    public void Surroundings_that_show_nothing_are_none()
    {
        Assert.Null(WorldRules.Ambience(JObject.Parse(@"{ ""pattern"": ""none"", ""dim"": 0 }"), "a"));
        Assert.Null(WorldRules.Ambience(null, "a"));
        Assert.NotNull(WorldRules.Ambience(JObject.Parse(@"{ ""pattern"": ""rain"", ""dim"": 0 }"), "a"));
    }

    [Theory]
    [InlineData("#4a2f27", "#4A2F27")]
    [InlineData("4A2F27", "#4A2F27")]
    [InlineData(" #FFF ", "#FFFFFF")]
    [InlineData("#12345", "")]
    [InlineData("#GGGGGG", "")]
    [InlineData("rgb(1,2,3)", "")]
    [InlineData(null, "")]
    public void Colours_are_hex_or_nothing(string given, string got) => Assert.Equal(got, WorldRules.Colour(given, ""));

    // ------------------------------------------------------------------ a skill built by a model
    static readonly string[] SongIds = WorldDefaults.Songs().Select(s => s.id).ToArray();
    static readonly string[] AmbienceIds = WorldDefaults.Ambiences().Select(a => a.id).ToArray();

    [Fact]
    public void A_skill_keeps_only_the_acts_that_exist()
    {
        var items = Desk(Thing("record", "레코드판"), Thing("chair", "의자"));
        var j = JObject.Parse(@"{ ""name"": ""집중 모드"", ""note"": ""비 내리고 조용한 노래를 틀어."", ""phrases"": [""집중 모드"", "" 집중 모드 "", ""focus mode"", """"], ""itemId"": ""레코드판"",
            ""steps"": [
                { ""act"": ""goto"", ""arg"": ""레코드판"", ""seconds"": 2 },
                { ""act"": ""order_pizza"", ""arg"": ""large"", ""seconds"": 0 },
                { ""act"": ""rain"", ""arg"": ""true"", ""seconds"": 0 },
                { ""act"": ""music"", ""arg"": """", ""seconds"": 0 },
                { ""act"": ""activity"", ""arg"": ""fly"", ""seconds"": 0 },
                { ""act"": ""emote"", ""arg"": ""backflip"", ""seconds"": 99999 },
                { ""act"": ""say"", ""arg"": """", ""seconds"": 1 },
                { ""act"": ""goto"", ""arg"": ""the moon"", ""seconds"": 1 },
                { ""act"": ""wait"", ""arg"": ""x"", ""seconds"": 0 },
                { ""act"": ""ambience"", ""arg"": ""Rain"", ""seconds"": 0 }
            ],
            ""hasSong"": true, ""song"": { ""name"": """", ""bpm"": 70, ""bars"": 2, ""lead"": ""soft"", ""melody"": [60, -1, 62, -1, 64] },
            ""hasAmbience"": true, ""ambience"": { ""pattern"": ""rain"", ""dim"": 0.5 } }");
        var s = WorldRules.Skill(j, "skill-t", items, SongIds, AmbienceIds);
        Assert.Equal("skill-t", s.id);
        Assert.Equal("ready", s.status);
        Assert.False(s.builtin);
        Assert.Equal(new[] { "집중 모드", "focus mode" }, s.phrases);
        Assert.Equal("item-레코드판", s.itemId);            // named, not by id: found
        Assert.Equal(new[] { "goto:item-레코드판", "rain:on", "music:", "emote:happy", "wait:", "ambience:amb-rain" }, s.steps.Select(st => st.act + ":" + st.arg).ToArray());
        Assert.Equal(600f, s.steps[3].seconds);          // nothing lasts longer than ten minutes
        Assert.Equal(1f, s.steps[4].seconds);            // a wait of no time is a wait of one second
        Assert.NotNull(s.song);
        Assert.Equal("song-skill-t", s.song.id);
        Assert.Equal("집중 모드", s.song.name);            // a tune without a name is called after its skill
        Assert.Null(s.ambience);                         // the step uses the built-in rain, so the skill's own is not kept
    }

    [Fact]
    public void A_skill_with_no_usable_step_is_refused()
    {
        var j = JObject.Parse(@"{ ""name"": ""Pizza"", ""steps"": [ { ""act"": ""order"", ""arg"": ""pizza"", ""seconds"": 0 }, { ""act"": ""activity"", ""arg"": ""cook"", ""seconds"": 0 }, { ""act"": ""say"", ""arg"": ""  "", ""seconds"": 0 } ] }");
        Assert.Null(WorldRules.Skill(j, "skill-t", new List<ItemDef>(), SongIds, AmbienceIds));
        Assert.Null(WorldRules.Skill(new JObject(), "skill-t", null, null, null));
        Assert.Null(WorldRules.Skill(null, "skill-t", null, null, null));
    }

    [Fact]
    public void A_skill_names_tunes_and_surroundings_that_exist_or_brings_its_own()
    {
        JObject Steps(string song, string amb, bool own) => JObject.Parse($@"{{ ""name"": ""t"", ""steps"": [ {{ ""act"": ""music"", ""arg"": ""{song}"", ""seconds"": 0 }}, {{ ""act"": ""ambience"", ""arg"": ""{amb}"", ""seconds"": 0 }}, {{ ""act"": ""dance"", ""arg"": ""wildly"", ""seconds"": 0 }} ],
            ""hasSong"": {(own ? "true" : "false")}, ""song"": {{ ""melody"": [60, 62] }}, ""hasAmbience"": {(own ? "true" : "false")}, ""ambience"": {{ ""pattern"": ""spots"" }} }}");

        var named = WorldRules.Skill(Steps("lofi", "CLUB", true), "k", null, SongIds, AmbienceIds);
        Assert.Equal(new[] { "music:song-lofi", "ambience:amb-club", "dance:" }, named.steps.Select(st => st.act + ":" + st.arg).ToArray());
        Assert.Null(named.song);                         // composed but used by no step: not kept
        Assert.Null(named.ambience);

        var own = WorldRules.Skill(Steps("my own tune", "", true), "k", null, SongIds, AmbienceIds);
        Assert.Equal(new[] { "music:", "ambience:", "dance:" }, own.steps.Select(st => st.act + ":" + st.arg).ToArray());
        Assert.NotNull(own.song);
        Assert.Equal("spots", own.ambience.pattern);

        // nothing of its own and nothing named: the quiet built-in tune, and no change of surroundings at all
        var bare = WorldRules.Skill(Steps("", "", false), "k", null, SongIds, AmbienceIds);
        Assert.Equal(new[] { "music:song-lofi", "dance:" }, bare.steps.Select(st => st.act + ":" + st.arg).ToArray());
        Assert.Null(bare.song);
        Assert.Null(bare.ambience);

        var stop = WorldRules.Skill(Steps("stop", "clear", false), "k", null, SongIds, AmbienceIds);
        Assert.Equal(new[] { "music:stop", "ambience:clear", "dance:" }, stop.steps.Select(st => st.act + ":" + st.arg).ToArray());
    }

    [Fact]
    public void A_skill_has_at_most_eight_steps_and_eight_phrases()
    {
        var j = new JObject
        {
            ["name"] = "long",
            ["phrases"] = new JArray(Enumerable.Range(0, 30).Select(i => (object)("phrase " + i)).ToArray()),
            ["steps"] = new JArray(Enumerable.Range(0, 30).Select(i => (object)new JObject { ["act"] = "emote", ["arg"] = "wave", ["seconds"] = 1 }).ToArray()),
        };
        var s = WorldRules.Skill(j, "k", null, null, null);
        Assert.Equal(WorldRules.MaxSteps, s.steps.Count);
        Assert.Equal(WorldRules.MaxPhrases, s.phrases.Length);
    }

    // ------------------------------------------------------------------ a thing understood by a model
    [Fact]
    public void A_thing_is_a_tenth_of_its_real_size_within_what_fits_a_desk()
    {
        var guess = ItemKinds.Find("box");
        ItemIdea Sized(double cm) => WorldRules.Item(new JObject { ["kind"] = "desk", ["realSizeCm"] = cm }, guess);
        Assert.Equal(0.12f, Sized(120).sizeM, 4);
        Assert.Equal(0.085f, Sized(85).sizeM, 4);
        Assert.Equal(WorldRules.SizeMax, Sized(400).sizeM);         // a wardrobe would not fit the desk
        Assert.Equal(ItemKinds.Find("desk").sizeM, Sized(0).sizeM); // no size given: the built-in shape's
        Assert.Equal(ItemKinds.Find("desk").sizeM, Sized(-30).sizeM);
    }

    [Fact]
    public void A_small_thing_that_is_made_is_never_too_small_to_see()
    {
        // "망치" came out at 3 cm (size=0.030 m, 2026-11-17): the partner is 10 cm tall, and nobody could see it, let alone pick it up
        var guess = ItemKinds.Find("box");
        float Sized(double cm, string kind = "toy") => WorldRules.Item(new JObject { ["kind"] = kind, ["realSizeCm"] = cm }, guess).sizeM;
        Assert.Equal(0.06f, WorldRules.MadeSizeMin);
        Assert.Equal(0.05f, WorldRules.SizeMin);
        Assert.InRange(Sized(30), 0.065f, 0.075f);                  // a hammer: about 7 cm
        Assert.InRange(Sized(10), 0.06f, 0.065f);                   // a mug
        Assert.InRange(Sized(1), 0.06f, 0.061f);                    // a marble: still 6 cm
        Assert.InRange(Sized(60), 0.07f, 0.08f);
        // never under MadeSizeMin, never over SizeMax, and larger things are never smaller than smaller ones
        float last = 0f;
        for (double cm = 0.5; cm <= 500; cm += 0.5)
        {
            float size = Sized(cm);
            Assert.InRange(size, WorldRules.MadeSizeMin, WorldRules.SizeMax);
            Assert.True(size >= last - 1e-6f, $"{cm} cm came out smaller ({size}) than a smaller thing ({last})");
            last = size;
        }
        Assert.Equal(WorldRules.MadeSizeMin, WorldRules.MadeSize(0f)); Assert.Equal(WorldRules.MadeSizeMin, WorldRules.MadeSize(float.NaN)); Assert.Equal(WorldRules.MadeSizeMin, WorldRules.MadeSize(-5f));
        // no size given: the built-in shape's - and that is at least MadeSizeMin too (a made cup is not the shop's 5 cm mug)
        Assert.Equal(WorldRules.MadeSizeMin, WorldRules.Item(new JObject { ["kind"] = "cup" }, guess).sizeM);
        // every built-in shape is at least SizeMin
        foreach (var k in ItemKinds.All) Assert.True(k.sizeM >= WorldRules.SizeMin, k.id);
        // the understanding step tells the model so
        string ask = Maker_ItemAsk();
        Assert.Contains("0.06 m", ask); Assert.Contains("hand tool", ask);
    }

    /// <summary>(Maker.cs is the host's own and is not compiled into the tests: its prompt is read from the source.)</summary>
    static string Maker_ItemAsk() => File.ReadAllText(Path.Combine(RepoRoot(), "Companion", "LivingPartner.Host", "Maker.cs"));
    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "RUNBOOK.md"))) d = d.Parent;
        Assert.NotNull(d);
        return d.FullName;
    }

    [Fact]
    public void Things_from_before_that_are_too_small_are_made_larger_once_and_stay_so()
    {
        // the user's desk on the evening of 2026-11-17
        var items = new List<ItemDef>
        {
            new ItemDef { id = "item-a52d813d", name = "책상", asked = "책상 하나 만들어줘", kind = "desk", sizeM = 0.12f },
            new ItemDef { id = "item-a28c241a", name = "작은 레코드 플레이어", asked = "작은 레코드 플레이어", kind = "record", sizeM = 0.04f },
            new ItemDef { id = "item-book", name = "책", kind = "book", sizeM = 0.045f },
            new ItemDef { id = "item-ball", name = "공", kind = "ball", sizeM = 0.04f },
            new ItemDef { id = "item-1cbabe59", name = "강아지 인형", asked = "강아지 인형", kind = "toy", sizeM = 0.025f },
            new ItemDef { id = "item-1cc0b355", name = "작은 선인장 화분", asked = "작은 선인장 화분", kind = "plant", sizeM = 0.025f },
            new ItemDef { id = "item-d65ad2e4", name = "망치", asked = "망치", kind = "toy", sizeM = 0.03f },
            new ItemDef { id = "item-x", name = "Wardrobe", asked = "a wardrobe", kind = "box", sizeM = 0.9f },
            new ItemDef { id = "item-y", name = "Odd", asked = "odd", kind = "box", sizeM = float.NaN },
            null,
        };
        var notes = WorldRules.GrowSmall(items);
        float Size(string id) => items.First(i => i != null && i.id == id).sizeM;
        Assert.Equal(0.12f, Size("item-a52d813d"));                 // furniture keeps its size
        Assert.Equal(0.05f, Size("item-a28c241a")); Assert.Equal(0.05f, Size("item-book")); Assert.Equal(0.05f, Size("item-ball"));
        Assert.Equal(0.05f, Size("item-1cbabe59")); Assert.Equal(0.05f, Size("item-1cc0b355"));
        Assert.Equal(0.07f, Size("item-d65ad2e4"));                 // the hammer: a hand tool
        Assert.Equal(WorldRules.SizeMax, Size("item-x")); Assert.Equal(WorldRules.SizeMin, Size("item-y"));
        Assert.Equal(8, notes.Count);
        Assert.Contains(notes, n => n.Contains("item-d65ad2e4") && n.Contains("0.030") && n.Contains("0.070"));
        // again: nothing left to do
        Assert.Empty(WorldRules.GrowSmall(items));
        Assert.Empty(WorldRules.GrowSmall(null));
        // a hammer from the shop (nobody asked for it in words) is a thing like any other; a made one that is large enough is left alone
        Assert.False(WorldRules.HandTool(new ItemDef { name = "책" })); Assert.True(WorldRules.HandTool(new ItemDef { name = "Toy hammer" })); Assert.False(WorldRules.HandTool(null));
        var big = new List<ItemDef> { new ItemDef { id = "a", name = "망치", asked = "큰 망치", sizeM = 0.09f }, new ItemDef { id = "b", name = "Hammer", asked = "", sizeM = 0.03f } };
        WorldRules.GrowSmall(big);
        Assert.Equal(0.09f, big[0].sizeM); Assert.Equal(0.05f, big[1].sizeM);
        // a module's thing that is too small is made larger before it is judged (it used to be dropped from the shelf for being "out of range")
        var m = new MarketModule { id = "item-1a2b3c4d", kind = ModuleKinds.Item, name = "망치", item = new ItemDef { id = "item-1a2b3c4d", name = "망치", kind = "toy", uses = new[] { ItemUses.Decor }, sizeM = 0.03f, status = "ready", y = 0.2f, onId = "item-desk" } };
        MarketRules.Normalise(m);
        Assert.Equal(0.05f, m.item.sizeM); Assert.Equal(0f, m.item.y); Assert.Equal("", m.item.onId);
        Assert.Equal("", MarketRules.Problem(m));
    }

    [Fact]
    public void A_thing_is_one_of_the_built_in_shapes_and_for_known_uses_only()
    {
        var chair = ItemKinds.Find("chair");
        var idea = WorldRules.Item(JObject.Parse(@"{ ""name"": ""흔들의자"", ""kind"": ""rocking-chair"", ""uses"": [""sit"", ""SIT"", ""fly"", ""decor""], ""realSizeCm"": 90, ""colour"": ""f4a9a0"", ""note"": ""흔들의자다!"", ""modelPrompt"": ""a rocking chair"", ""iconPrompt"": ""a rocking chair seen from the side"" }"), chair);
        Assert.Equal("흔들의자", idea.name);
        Assert.Equal("chair", idea.kind);                 // a shape that does not exist: the first guess stays
        Assert.Equal(new[] { "sit", "decor" }, idea.uses);
        Assert.Equal("#F4A9A0", idea.color);
        Assert.Equal(0.09f, idea.sizeM, 4);

        var empty = WorldRules.Item(null, chair);         // no answer at all: everything is the guess
        Assert.Equal("chair", empty.kind);
        Assert.Equal(chair.uses, empty.uses);
        Assert.Equal(chair.sizeM, empty.sizeM);
        Assert.Equal("", empty.color);

        var lamp = WorldRules.Item(JObject.Parse(@"{ ""kind"": ""lamp"", ""uses"": [], ""colour"": ""yellowish"" }"), chair);
        Assert.Equal(ItemKinds.Find("lamp").uses, lamp.uses);       // nothing usable named: what that shape is for
        Assert.Equal("", lamp.color);
    }

    // ------------------------------------------------------------------ what the user means
    [Fact]
    public void A_skill_is_found_by_id_name_or_a_phrase_and_the_users_own_comes_first()
    {
        var skills = WorldDefaults.Skills().ToList();
        Assert.Equal("skill-dance", WorldRules.FindSkill(skills, "skill-dance").id);
        Assert.Equal("skill-dance", WorldRules.FindSkill(skills, "dance").id);
        Assert.Equal("skill-dance", WorldRules.FindSkill(skills, "밤비야, 춤 춰!").id);
        Assert.Equal("skill-dance", WorldRules.FindSkill(skills, "춤춰줘").id);               // spaced differently from the phrase
        Assert.Equal("skill-dance", WorldRules.FindSkill(skills, "DANCE").id);
        // there is no "Music" skill any more: music is the record player and its records (a plain wish for music is the world command "music")
        Assert.Null(WorldRules.FindSkill(skills, "노래 틀어줘")); Assert.Null(WorldRules.FindSkill(skills, "MUSIC"));
        Assert.DoesNotContain(skills, k => k.id == "skill-music");
        Assert.Null(WorldRules.FindSkill(skills, "설거지 해 줘"));
        Assert.Null(WorldRules.FindSkill(skills, ""));
        Assert.Null(WorldRules.FindSkill(null, "dance"));

        // the user taught their own way of playing music: asked the same way, that is the one meant
        skills.Add(new SkillDef { id = "skill-mine", name = "레코드 댄스", phrases = new[] { "노래 틀어 줘", "play music" } });
        Assert.Equal("skill-mine", WorldRules.FindSkill(skills, "노래 틀어줘").id);
        Assert.Equal("skill-mine", WorldRules.FindSkill(skills, "레코드 댄스").id);
        Assert.Equal("skill-dance", WorldRules.FindSkill(skills, "Dance").id);               // the built-in one is still there
    }

    [Fact]
    public void A_thing_is_found_by_id_name_or_what_it_is()
    {
        var items = Desk(Thing("desk", "책상"), Thing("chair", "빨간 의자"), Thing("record", "Record player"));
        Assert.Equal("item-책상", WorldRules.FindItem(items, "item-책상").id);
        Assert.Equal("item-책상", WorldRules.FindItem(items, "책상").id);
        Assert.Equal("item-빨간 의자", WorldRules.FindItem(items, "의자").id);                 // part of its name
        Assert.Equal("item-빨간 의자", WorldRules.FindItem(items, "빨간 의자 좀 치워 줘").id);
        Assert.Equal("item-Record player", WorldRules.FindItem(items, "record PLAYER").id);
        Assert.Equal("item-Record player", WorldRules.FindItem(items, "that vinyl thing").id);   // another word for that kind of thing
        Assert.Null(WorldRules.FindItem(items, "침대"));
        Assert.Null(WorldRules.FindItem(items, ""));
    }

    [Theory]
    [InlineData("dance", "dance")]
    [InlineData("STUDY", "study")]
    [InlineData("춤춰 봐", "dance")]
    [InlineData("공부해", "study")]
    [InlineData("앉아 봐", "sit")]
    [InlineData("낮잠 자", "nap")]
    [InlineData("책 읽어", "read")]
    [InlineData("놀자", "play")]
    [InlineData("그만", "stop")]
    [InlineData("원래대로 해 줘", "stop")]
    [InlineData("공부 그만해", "stop")]
    [InlineData("stop", "stop")]
    [InlineData("fly to the moon", "")]
    [InlineData("", "")]
    public void An_activity_is_named_in_a_few_plain_ways(string text, string act) => Assert.Equal(act, WorldRules.ActivityWord(text));

    [Theory]
    [InlineData("clear", true)]
    [InlineData("OFF", true)]
    [InlineData("원래대로", true)]
    [InlineData("다시 원래대로 돌려줘", true)]
    [InlineData("꺼 줘", true)]
    [InlineData("클럽처럼", false)]
    [InlineData("like a beach at sunset", false)]
    public void Taking_it_away_is_told_from_setting_something(string text, bool off) => Assert.Equal(off, WorldRules.IsOff(text));

    [Theory]
    [InlineData("의자", "가")]
    [InlineData("책상", "이")]
    [InlineData("침대", "가")]
    [InlineData("책", "이")]
    [InlineData("장난감", "이")]
    [InlineData("Sofa", "가")]
    public void The_particle_fits_the_word(string word, string particle) => Assert.Equal(particle, WorldRules.Josa(word, "이", "가"));

    [Theory]
    [InlineData("책상 줄게", "desk", "책상")]
    [InlineData("a small desk", "desk", "Desk")]
    [InlineData("빨간 우산 하나 만들어 줘", "box", "빨간 우산")]
    [InlineData("make me a red umbrella please", "box", "red umbrella")]
    [InlineData("here's a kite for you", "box", "kite")]
    [InlineData("", "box", "box")]
    public void A_thing_has_a_label_before_any_model_names_it(string asked, string kind, string name)
    {
        var guess = ItemKinds.Guess(asked);
        Assert.Equal(kind, guess.id);
        Assert.Equal(name, WorldRules.RoughName(asked, guess));
    }

    [Theory]
    [InlineData("item-1a2b3c4d.glb", true)]
    [InlineData("skill-1a2b3c4d.png", true)]
    [InlineData("a_b-c.1.png", true)]
    [InlineData("item.txt", false)]
    [InlineData("item.glb.exe", false)]
    [InlineData("..glb", false)]
    [InlineData(".hidden.png", false)]
    [InlineData("a..png", false)]
    [InlineData("../state.json", false)]
    [InlineData("..\\local.token", false)]
    [InlineData("sub/item.png", false)]
    [InlineData("C:item.png", false)]
    [InlineData("item.png\n", false)]
    [InlineData("아이템.png", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_plain_model_and_icon_file_names_are_served(string name, bool ok) => Assert.Equal(ok, WorldRules.SafeFile(name));
}
