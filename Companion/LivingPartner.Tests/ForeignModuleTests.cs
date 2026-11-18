using LivingPartner.Host;
using LivingPartner.MarketHub;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// A module that comes from somewhere else - the hub, a .bambi file - is somebody else's text and numbers: it is made
/// safe exactly as what a language model wrote (MarketPack.Sanitise: every text has an end, every list a length), and
/// nothing that is not a number passes for one (MarketRules.Problem). Also here: what the hub does when a price is not
/// the one the buyer saw, how it says that an account is not known, and the limits of the bag.
/// </summary>
public class ForeignModuleTests
{
    static MarketModule Seed(string id) => MarketRules.Clone(MarketSeed.All().First(e => e.module.id == id).module);

    static MarketModule ImportedGame()
    {
        var m = Seed("game-keeper");
        m.game.song = MarketSeed.VampireTune();
        m.game.ambience = WorldDefaults.NightSky();
        return m;
    }

    static MarketModule Skill(string id, SkillDef k)
    {
        k.id = id;
        return new MarketModule { id = id, kind = ModuleKinds.Skill, name = "A skill", summary = "s", gives = "g", skill = k };
    }

    // ------------------------------------------------------------------ numbers that are not numbers
    [Fact]
    public void A_number_that_is_not_one_is_never_in_range()
    {
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var game = ImportedGame(); game.game.seconds = bad;
            Assert.NotEqual("", MarketRules.Problem(game));
            game = ImportedGame(); game.game.speed = bad;
            Assert.NotEqual("", MarketRules.Problem(game));
            game = ImportedGame(); game.game.spawn = bad;
            Assert.NotEqual("", MarketRules.Problem(game));
            game = ImportedGame(); game.game.song.swing = bad;
            Assert.NotEqual("", MarketRules.Problem(game));
            game = ImportedGame(); game.game.song.volume = bad;
            Assert.NotEqual("", MarketRules.Problem(game));
            game = ImportedGame(); game.game.ambience.dim = bad;
            Assert.NotEqual("", MarketRules.Problem(game));
            var mood = Seed("amb-aurora"); mood.ambience.speed = bad;
            Assert.NotEqual("", MarketRules.Problem(mood));
            mood = Seed("amb-aurora"); mood.ambience.density = bad;
            Assert.NotEqual("", MarketRules.Problem(mood));
            var chair = Seed("item-chair"); chair.item.sizeM = bad;
            Assert.NotEqual("", MarketRules.Problem(chair));
            chair = Seed("item-chair"); chair.item.seat = bad;
            Assert.NotEqual("", MarketRules.Problem(chair));
            var dj = Seed("skill-dj"); dj.skill.steps[2].seconds = bad;
            Assert.NotEqual("", MarketRules.Problem(dj));
        }
        // and the two that had no range at all: how long a step lasts, how loud a tune is
        var slow = Seed("skill-dj"); slow.skill.steps[2].seconds = 1e9f;
        Assert.NotEqual("", MarketRules.Problem(slow));
        slow = Seed("skill-dj"); slow.skill.steps[2].seconds = -1f;
        Assert.NotEqual("", MarketRules.Problem(slow));
        var loud = Seed("skill-dj"); loud.skill.song.volume = 40f;
        Assert.NotEqual("", MarketRules.Problem(loud));
    }

    [Fact]
    public void What_is_not_a_number_does_not_survive_the_cleaners_either()
    {
        // a game from the hub with a tune and surroundings full of nonsense: the payloads are cleaned as model output is
        var game = ImportedGame();
        game.game.song.volume = float.NaN; game.game.song.swing = float.PositiveInfinity; game.game.song.bpm = 9000;
        game.game.song.melody = Enumerable.Range(0, 5000).Select(i => 60 + i % 12).ToArray();
        game.game.ambience.dim = float.NaN; game.game.ambience.speed = float.NegativeInfinity; game.game.ambience.c1 = "javascript:alert(1)";
        Assert.Equal("", MarketPack.Sanitise(game));
        Assert.InRange(game.game.song.volume, 0.3f, 1f); Assert.InRange(game.game.song.swing, 0f, 0.5f); Assert.Equal(WorldRules.BpmMax, game.game.song.bpm);
        Assert.Equal(64, game.game.song.melody.Length);
        Assert.InRange(game.game.ambience.dim, 0f, 1f); Assert.InRange(game.game.ambience.speed, 0.1f, 3f);
        Assert.Matches("^#[0-9A-F]{6}$", game.game.ambience.c1);
        // the numbers of the game itself are not guessed at: a round of NaN seconds is no game
        var broken = ImportedGame(); broken.game.seconds = float.NaN;
        Assert.NotEqual("", MarketPack.Sanitise(broken));
    }

    // ------------------------------------------------------------------ texts without an end
    [Fact]
    public void Over_long_texts_and_lists_are_cut_to_what_a_module_may_carry()
    {
        string wall = new string('가', 20_000);
        var skill = Skill("skill-1a2b3c4d", new SkillDef
        {
            name = wall, note = wall, asked = wall, phrases = Enumerable.Range(0, 300).Select(i => i + wall).ToArray(), itemId = "item-of-the-seller", builtin = true,
            steps = Enumerable.Range(0, 50).Select(i => new SkillStep { act = "say", arg = i + wall, seconds = 99_999f }).ToList(),
        });
        skill.name = wall; skill.summary = wall; skill.gives = wall; skill.author = wall; skill.version = wall; skill.tags = Enumerable.Range(0, 500).Select(i => i + wall).ToArray();
        Assert.Equal("", MarketPack.Sanitise(skill));
        Assert.True(skill.name.Length <= 40 && skill.summary.Length <= 200 && skill.gives.Length <= 160 && skill.author.Length <= 48 && skill.version.Length <= 12);
        Assert.True(skill.tags.Length <= 8 && skill.tags.All(t => t.Length <= 24));
        var k = skill.skill;
        Assert.True(k.name.Length <= 20 && k.note.Length <= 160 && k.asked.Length <= 300);
        Assert.Equal(WorldRules.MaxPhrases, k.phrases.Length); Assert.True(k.phrases.All(p => p.Length <= 60));
        Assert.Equal(WorldRules.MaxSteps, k.steps.Count);                                  // too many steps: the first eight
        Assert.True(k.steps.All(st => st.arg.Length <= 160 && st.seconds >= 0 && st.seconds <= 600));
        Assert.Equal("", k.itemId); Assert.False(k.builtin);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(Proto.ToJson(skill)) < 20_000);

        var game = ImportedGame();
        game.game.title = wall; game.game.howTo = wall; game.game.enemyName = wall; game.game.pickupName = wall; game.game.song.name = wall; game.game.ambience.name = wall;
        Assert.Equal("", MarketPack.Sanitise(game));
        Assert.Equal(40, game.game.title.Length); Assert.Equal(160, game.game.howTo.Length); Assert.Equal(24, game.game.enemyName.Length); Assert.Equal(24, game.game.pickupName.Length);
        Assert.True(game.game.song.name.Length <= 24 && game.game.ambience.name.Length <= 24);

        var ability = Seed("ability-translate");
        ability.ability.name = wall; ability.ability.examples = Enumerable.Range(0, 40).Select(i => i + wall).ToArray();
        Assert.Equal("", MarketPack.Sanitise(ability));
        Assert.Equal(40, ability.ability.name.Length);
        Assert.Equal(6, ability.ability.examples.Length); Assert.True(ability.ability.examples.All(e => e.Length <= 80));
        // (its instructions are never cut - a cut instruction says something else: too long is turned away)
        ability = Seed("ability-translate"); ability.ability.prompt = new string('x', MarketRules.MaxAbilityPrompt + 1);
        Assert.NotEqual("", MarketPack.Sanitise(ability));
    }

    [Fact]
    public void A_skill_from_somewhere_else_keeps_only_steps_the_headset_can_carry_out()
    {
        var skill = Skill("skill-1a2b3c4d", new SkillDef
        {
            name = "Trouble", note = "n",
            steps = new List<SkillStep>
            {
                new SkillStep { act = "format-disk", arg = "c:" }, new SkillStep { act = "goto", arg = "item-only-the-seller-has" }, new SkillStep { act = "activity", arg = "fly" },
                new SkillStep { act = "emote", arg = "explode" }, new SkillStep { act = "music", arg = "song-lofi" }, new SkillStep { act = "dance", arg = "<b>" },
            },
        });
        Assert.Equal("", MarketPack.Sanitise(skill));
        Assert.Equal(new[] { "emote:happy", "music:song-lofi", "dance:" }, skill.skill.steps.Select(st => st.act + ":" + st.arg));
        // nothing left to do: no skill
        var empty = Skill("skill-1a2b3c4d", new SkillDef { name = "Nothing", steps = new List<SkillStep> { new SkillStep { act = "format-disk" } } });
        Assert.NotEqual("", MarketPack.Sanitise(empty));
    }

    [Fact]
    public void Problem_alone_turns_away_what_is_far_too_long_or_too_many()
    {
        // (whatever reaches it without having been through Sanitise)
        var game = ImportedGame(); game.game.title = new string('t', 41);
        Assert.NotEqual("", MarketRules.Problem(game));
        game = ImportedGame(); game.game.howTo = new string('h', 161);
        Assert.NotEqual("", MarketRules.Problem(game));
        game = ImportedGame(); game.game.enemyName = new string('e', 25);
        Assert.NotEqual("", MarketRules.Problem(game));
        var ability = Seed("ability-calc"); ability.ability.name = new string('a', 41);
        Assert.NotEqual("", MarketRules.Problem(ability));
        ability = Seed("ability-calc"); ability.ability.examples = Enumerable.Repeat("example", 7).ToArray();
        Assert.NotEqual("", MarketRules.Problem(ability));
        ability = Seed("ability-calc"); ability.ability.examples = new[] { new string('e', 81) };
        Assert.NotEqual("", MarketRules.Problem(ability));
        var dj = Seed("skill-dj"); dj.skill.phrases = Enumerable.Range(0, 9).Select(i => "phrase " + i).ToArray();
        Assert.NotEqual("", MarketRules.Problem(dj));
        dj = Seed("skill-dj"); dj.skill.steps = Enumerable.Range(0, 9).Select(_ => new SkillStep { act = "dance" }).ToList();
        Assert.NotEqual("", MarketRules.Problem(dj));
        dj = Seed("skill-dj"); dj.skill.steps[0] = new SkillStep { act = "say", arg = new string('s', 201) };
        Assert.NotEqual("", MarketRules.Problem(dj));
        dj = Seed("skill-dj"); dj.skill.song.name = new string('n', 61);
        Assert.NotEqual("", MarketRules.Problem(dj));
        var chair = Seed("item-chair"); chair.summary = new string('s', 321);
        Assert.NotEqual("", MarketRules.Problem(chair));
        chair = Seed("item-chair"); chair.tags = Enumerable.Range(0, 13).Select(i => "t" + i).ToArray();
        Assert.NotEqual("", MarketRules.Problem(chair));
        chair = Seed("item-chair"); chair.item.parts = Enumerable.Range(0, ShapePart.Max + 1).Select(_ => new ShapePart()).ToList();
        Assert.NotEqual("", MarketRules.Problem(chair));
    }

    [Fact]
    public void Everything_that_comes_with_the_partner_passes_the_cleaners_and_does_the_same_afterwards()
    {
        foreach (var seed in MarketSeed.All())
        {
            var m = MarketRules.Clone(seed.module);
            Assert.True(MarketPack.Sanitise(m) == "", $"{seed.module.id}: {MarketPack.Sanitise(MarketRules.Clone(seed.module))}");
            Assert.Equal(seed.module.name, m.name); Assert.Equal(seed.module.summary, m.summary); Assert.Equal(seed.module.gives, m.gives); Assert.Equal(seed.module.tags, m.tags);
            if (seed.module.skill != null)
            {
                Assert.Equal(seed.module.skill.steps.Select(st => $"{st.act}/{st.arg}/{st.seconds}"), m.skill.steps.Select(st => $"{st.act}/{st.arg}/{st.seconds}"));
                Assert.Equal(seed.module.skill.phrases, m.skill.phrases); Assert.Equal(seed.module.skill.name, m.skill.name);
                Assert.Equal(seed.module.skill.song?.bpm, m.skill.song?.bpm); Assert.Equal(seed.module.skill.ambience?.pattern, m.skill.ambience?.pattern);
            }
            if (seed.module.game != null) { Assert.Equal(seed.module.game.title, m.game.title); Assert.Equal(seed.module.game.howTo, m.game.howTo); Assert.Equal(seed.module.game.template, m.game.template); Assert.Equal(seed.module.game.song?.bpm, m.game.song?.bpm); }
            if (seed.module.ability != null) { Assert.Equal(seed.module.ability.prompt, m.ability.prompt); Assert.Equal(seed.module.ability.examples, m.ability.examples); }
            if (seed.module.ambience != null) { Assert.Equal(seed.module.ambience.pattern, m.ambience.pattern); Assert.Equal(seed.module.ambience.c0, m.ambience.c0); Assert.Equal(seed.module.ambience.id, m.ambience.id); }
            // cleaning twice changes nothing more
            string once = Proto.ToJson(m);
            Assert.Equal("", MarketPack.Sanitise(m));
            Assert.Equal(once, Proto.ToJson(m));
        }
    }

    // ------------------------------------------------------------------ the bag has an end
    [Fact]
    public void A_full_bag_and_a_full_list_of_skills_take_nothing_more_from_the_shop()
    {
        var w = new WorldState();
        var bed = Seed("item-bed");
        Assert.Equal("", MarketRules.BagProblem(w, bed));
        for (int i = 0; i < WorldRules.MaxItems; i++) w.items.Add(new ItemDef { id = "item-" + i, name = "Thing " + i, status = "ready" });
        Assert.Equal("items", MarketRules.BagProblem(w, bed));
        w.items[0].id = bed.id;                                // (it is there already: nothing would be added)
        Assert.Equal("", MarketRules.BagProblem(w, bed));
        Assert.Equal("", MarketRules.BagProblem(w, Seed("ability-calc")));

        var lullaby = Seed("skill-lullaby");
        w.skills.AddRange(WorldDefaults.Skills());             // what came with the partner does not count
        for (int i = 0; i < WorldRules.MaxSkills - 1; i++) w.skills.Add(new SkillDef { id = "skill-" + i, name = "Skill " + i });
        Assert.Equal("", MarketRules.BagProblem(w, lullaby));
        w.skills.Add(new SkillDef { id = "skill-last", name = "One more" });
        Assert.Equal("skills", MarketRules.BagProblem(w, lullaby));
    }

    // ------------------------------------------------------------------ two listings of one name
    sealed class Offer { public string id = "", name = ""; public string[] tags = new string[0]; }

    [Fact]
    public void Everything_that_fits_equally_well_is_found_not_only_the_first()
    {
        var list = new List<Offer>
        {
            new Offer { id = "item-a", name = "Chair" }, new Offer { id = "item-b", name = "chair" }, new Offer { id = "item-c", name = "Arm chair", tags = new[] { "chair" } },
            new Offer { id = "item-d", name = "Throne", tags = new[] { "의자" } }, new Offer { id = "item-e", name = "Stool", tags = null },
        };
        List<Offer> All(string text, bool tags = true) => MarketRules.Matches(list, text, o => o.id, o => o.name, o => tags ? o.tags : null);
        Assert.Equal(new[] { "item-a", "item-b" }, All("Chair").Select(o => o.id));                   // the same name twice: both, and nothing that merely has it as a tag
        Assert.Equal(new[] { "item-b" }, All("item-b").Select(o => o.id));                             // an id is one thing
        Assert.Equal(new[] { "item-d" }, All("의자").Select(o => o.id));                               // by a tag ...
        Assert.Empty(All("의자", false));                                                              // ... unless tags are not to count (a seller's own words, when it would cost stars)
        Assert.Equal(new[] { "item-c" }, All("the arm chair please").Select(o => o.id));               // the longest name in the sentence
        Assert.Equal(new[] { "item-a", "item-b" }, All("buy me a chair", false).Select(o => o.id));
        Assert.Empty(All("sofa")); Assert.Empty(All(""));
        // Match is the first of them, as it always was
        Assert.Equal("item-a", MarketRules.Match(list, "Chair", o => o.id, o => o.name, o => o.tags).id);
        Assert.Equal("item-b", MarketRules.Match(list, "Chair", o => o.id, o => o.name, o => o.tags, o => o.id == "item-b").id);
    }

    // ------------------------------------------------------------------ the hub: the price that was seen, the account that is gone
    static HubPublish Selling(string id, int price) => new HubPublish
    {
        price = price,
        module = new MarketModule
        {
            id = id, kind = ModuleKinds.Ability, name = "칭찬 요정", summary = "s", gives = "g",
            ability = new AbilityDef { id = id, name = "칭찬 요정", prompt = "When the user says they are tired, you answer with one warm sentence about them.", examples = new[] { "칭찬해 줘" } },
        },
    };

    [Fact]
    public void A_price_that_changed_since_the_buyer_saw_it_is_not_charged()
    {
        var hub = new Hub(new MemoryHubStore());
        var seller = hub.Register("민수").value; var buyer = hub.Register("지우").value;
        Assert.True(hub.Publish(seller.token, Selling("ability-cheer01", 20)).Ok);
        Assert.True(hub.Edit(seller.token, "ability-cheer01", new HubEdit { price = 250 }).Ok);            // ... between the buyer's look and their word
        var no = hub.Buy(buyer.token, "ability-cheer01", 20);
        Assert.Equal(HubRules.PriceChanged, no.error); Assert.Equal(409, no.status); Assert.Equal("250", no.detail);
        Assert.Equal(300, hub.Me(buyer.token).value.stars); Assert.Empty(hub.Me(buyer.token).value.bought);
        Assert.Equal(300, hub.Me(seller.token).value.stars);
        // free when they looked, priced now: the same
        Assert.Equal(HubRules.PriceChanged, hub.Buy(buyer.token, "ability-cheer01", 0).error);
        // the price they saw is the price they pay
        var yes = hub.Buy(buyer.token, "ability-cheer01", 250);
        Assert.True(yes.Ok); Assert.Equal(250, yes.value.price); Assert.Equal(50, yes.value.stars);
        // theirs already: nothing to charge, whatever price is named
        Assert.True(hub.Buy(buyer.token, "ability-cheer01", 1).value.already);
        // a client from before names no price and buys at what it costs now
        var old = hub.Register("서연").value;
        Assert.Equal(250, hub.Buy(old.token, "ability-cheer01").value.price);
    }

    [Fact]
    public void The_hub_says_which_hub_it_is_and_when_an_account_is_not_known()
    {
        var store = new MemoryHubStore();
        var hub = new Hub(store);
        Assert.StartsWith("hub-", hub.HubId);
        var me = hub.Register("민수").value;
        Assert.Equal(hub.HubId, new Hub(store).HubId);                         // the same data: the same hub, also after a restart
        Assert.NotEqual(hub.HubId, new Hub(new MemoryHubStore()).HubId);        // other data (or the same wiped): another hub
        // a token no account has: "unauthorized", and in so many words that the account is not known
        var gone = hub.Me("0123456789abcdef");
        Assert.Equal(401, gone.status); Assert.Equal("unauthorized", gone.error); Assert.Equal(HubRules.UnknownAccount, gone.detail);
        Assert.Equal(HubRules.UnknownAccount, hub.Buy("0123456789abcdef", "anything").detail);
        Assert.Equal(HubRules.UnknownAccount, hub.Publish("0123456789abcdef", Selling("ability-cheer01", 1)).detail);
        // no token at all is not "your account is gone"
        Assert.Equal("", hub.Me("").detail); Assert.Equal("", hub.Me(null).detail);
        Assert.True(hub.Me(me.token).Ok);
    }
}
