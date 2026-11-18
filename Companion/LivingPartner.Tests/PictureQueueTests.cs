using LivingPartner.Host;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// Which pictures are still to be drawn, and in which order (MarketRules.PicturesWanted: one image call at a time, so
/// the order is what the user sees) - every game pack and every skill wants a sticker of its own, what the user has
/// comes before the rest of the shelf. And the two small rules of what is typed to the partner at the PC.
/// </summary>
public class PictureQueueTests
{
    static readonly Dictionary<string, MarketSeedEntry> Seeds = MarketSeed.All().ToDictionary(e => e.module.id);

    static (MarketState s, WorldState w) Fresh()
    {
        var s = new MarketState();
        MarketRules.Merge(s, MarketSeed.All());
        var w = new WorldState();
        w.skills.AddRange(WorldDefaults.Skills());         // as the Maker leaves it: dancing comes with the partner
        MarketRules.Sync(w, s);
        return (s, w);
    }

    [Fact]
    public void Everything_the_makers_sell_has_a_drawing_of_its_own()
    {
        foreach (var e in Seeds.Values) Assert.True(e.draw.Length > 20, e.module.id);
        Assert.Contains("racket", Seeds["game-tennis"].draw); Assert.Contains("soccer", Seeds["game-keeper"].draw);
        Assert.Contains("demon", Seeds["game-swarm"].draw);
        Assert.Contains("music notes", Seeds["skill-dance"].draw); Assert.DoesNotContain("game-umbrella", Seeds.Keys);
        Assert.DoesNotContain("game-wipe", Seeds.Keys);
    }

    [Fact]
    public void A_game_pack_without_a_sticker_is_queued_and_one_that_has_it_is_not()
    {
        var (s, w) = Fresh();
        var wanted = MarketRules.PicturesWanted(s, w, Seeds);
        // every official game wants its own picture, under the module's name, drawn from the makers' words
        foreach (string id in new[] { "game-keeper", "game-tennis", "game-swarm", "game-gomoku" })
        {
            var wish = Assert.Single(wanted, p => p.id == id && p.part == "icon");
            Assert.Equal("market-" + id, wish.file); Assert.Equal(Seeds[id].draw, wish.what);
        }
        // the creature's picture is not the game's: a pack whose listing shows its creature still wants a sticker
        var vampire = MarketRules.Find(s, "game-swarm");
        vampire.icon = "market-game-swarm-enemy.png"; vampire.game.enemyIcon = "market-game-swarm-enemy.png"; vampire.game.pickupIcon = "market-game-swarm-pickup.png";
        wanted = MarketRules.PicturesWanted(s, w, Seeds);
        Assert.Contains(wanted, p => p.id == "game-swarm" && p.part == "icon");
        Assert.DoesNotContain(wanted, p => p.id == "game-swarm" && p.part != "icon");
        // with GamePackDef.icon set it wants nothing more
        vampire.game.icon = "market-game-swarm.png"; vampire.icon = "market-game-swarm.png";
        Assert.DoesNotContain(MarketRules.PicturesWanted(s, w, Seeds), p => p.id == "game-swarm");
        // tennis has no creature to draw: only its sticker
        Assert.Single(wanted, p => p.id == "game-tennis");
        // a pack somebody made (no words of the makers): drawn from the pack itself, as "the game"
        var ghosts = new MarketModule { id = "game-1a2b3c4d", kind = ModuleKinds.Game, name = "유령의 밤", game = new GamePackDef { id = "game-1a2b3c4d", template = "survival", title = "유령의 밤", enemyName = "꼬마 유령", pickupName = "사탕" } };
        s.catalogue.Add(ghosts);
        var mine = MarketRules.PicturesWanted(s, w, Seeds).Where(p => p.id == "game-1a2b3c4d").ToList();
        Assert.Equal(new[] { "icon", "enemy", "pickup" }, mine.Select(p => p.part));
        Assert.Contains("꼬마 유령", mine[0].what); Assert.Contains("game controller", mine[0].what);
        Assert.Equal(new[] { "market-game-1a2b3c4d", "market-game-1a2b3c4d-enemy", "market-game-1a2b3c4d-pickup" }, mine.Select(p => p.file));
    }

    [Fact]
    public void What_the_user_has_is_drawn_before_the_rest_of_the_shelf()
    {
        var (s, w) = Fresh();
        // the user got tennis, has a thing of their own in the bag and a skill they taught
        MarketRules.Install(s, "game-tennis", out var tennis); MarketRules.Give(w, tennis);
        w.items.Add(new ItemDef { id = "item-1a2b3c4d", name = "우산", kind = "box", uses = new[] { ItemUses.Decor }, status = "ready" });
        w.items.Add(new ItemDef { id = "item-being-made", name = "기타", kind = "box", status = "making" });       // not ready: nothing to draw yet
        w.skills.Add(new SkillDef { id = "skill-5e6f7a8b", name = "집중 모드", status = "ready", steps = new List<SkillStep> { new SkillStep { act = "dance" } } });
        var wanted = MarketRules.PicturesWanted(s, w, Seeds);
        var owned = wanted.TakeWhile(p => p.owned).ToList();
        Assert.All(wanted.Skip(owned.Count), p => Assert.False(p.owned));                  // owned first, in one block
        // the bag (the record player that comes with the partner, then the user's own thing), then the skills (dancing too), then the games, then whatever else is installed
        Assert.Equal(new[] { "item-record", "item-1a2b3c4d", "skill-dance", "skill-5e6f7a8b", "game-keeper", "game-tennis", "game-swarm", "game-gomoku", "amb-rain", "amb-night", "ability-calc" }, owned.Select(p => p.id));
        Assert.Equal("market-item-record", owned[0].file); Assert.Equal(Seeds["item-record"].draw, owned[0].what);
        Assert.Equal("item-1a2b3c4d", owned[1].file);                                       // a thing of the world's own: under its own name
        Assert.Equal("market-skill-dance", owned[2].file); Assert.Equal(Seeds["skill-dance"].draw, owned[2].what);
        Assert.Equal("skill-5e6f7a8b", owned[3].file); Assert.Contains("집중 모드", owned[3].what);
        Assert.DoesNotContain(wanted, p => p.id == "item-being-made");
        // the rest: everything else on the shelf, each once
        var rest = wanted.Skip(owned.Count).ToList();
        Assert.DoesNotContain(rest, p => p.id == "game-rps"); Assert.Contains(rest, p => p.id == "item-chair"); Assert.Contains(rest, p => p.id == "ability-translate"); Assert.Contains(rest, p => p.id == "amb-aurora");
        Assert.Equal(wanted.Count, wanted.Select(p => p.id + "/" + p.part).Distinct().Count());
        // what is being drawn by the Maker at this moment is left alone
        Assert.DoesNotContain(MarketRules.PicturesWanted(s, w, Seeds, id => id == "item-1a2b3c4d"), p => p.id == "item-1a2b3c4d");

        // once every picture is there, nothing is wanted
        foreach (var m in s.catalogue) { m.icon = "market-" + m.id + ".png"; if (m.game != null) { m.game.icon = m.icon; m.game.enemyIcon = "e.png"; m.game.pickupIcon = "p.png"; } if (m.skill != null) m.skill.icon = m.icon; }
        foreach (var i in w.items) i.icon = i.id + ".png";
        foreach (var k in w.skills) k.icon = "x.png";
        foreach (var g in w.games) g.icon = "x.png";
        Assert.Empty(MarketRules.PicturesWanted(s, w, Seeds));
    }

    [Fact]
    public void A_sticker_the_shelf_has_stays_over_a_restart()
    {
        // (the makers' modules are made anew from code at every start: the pictures drawn for them must not be lost with that)
        var (s, _) = Fresh();
        MarketRules.Find(s, "game-tennis").game.icon = "market-game-tennis.png"; MarketRules.Find(s, "game-tennis").icon = "market-game-tennis.png";
        MarketRules.Find(s, "skill-dance").skill.icon = "market-skill-dance.png"; MarketRules.Find(s, "skill-dance").icon = "market-skill-dance.png";
        var loaded = Proto.FromJson<MarketState>(Proto.ToJson(s));
        MarketRules.Merge(loaded, MarketSeed.All());
        Assert.Equal("market-game-tennis.png", MarketRules.Find(loaded, "game-tennis").game.icon); Assert.Equal("market-game-tennis.png", MarketRules.Find(loaded, "game-tennis").icon);
        Assert.Equal("market-skill-dance.png", MarketRules.Find(loaded, "skill-dance").skill.icon);
        // and a pack's own sticker is one of the files that travel with it (to the hub, into a .bambi file)
        var pack = MarketRules.Find(loaded, "game-tennis");
        Assert.Contains(MarketPack.FileRefs(pack), r => r.role == "icon" && r.name == "market-game-tennis.png");
        pack = MarketRules.Clone(pack); pack.game.icon = "..\\..\\secret.png";
        MarketPack.Sanitise(pack);
        Assert.Equal("", pack.game.icon);                      // a name with a path in it never passes
    }

    // ------------------------------------------------------------------ typed to the partner at the PC
    [Fact]
    public void What_is_typed_at_the_PC_is_carried_out_through_the_headset_when_there_is_one()
    {
        Assert.Equal(Roles.Quest, WorldRules.TypedThrough(new[] { Roles.Pc, Roles.Tool, Roles.Quest }));
        Assert.Equal(Roles.Sim, WorldRules.TypedThrough(new[] { Roles.Tool, Roles.Sim, Roles.Pc }));
        Assert.Equal(Roles.Quest, WorldRules.TypedThrough(new[] { Roles.Sim, Roles.Quest }));
        Assert.Equal(Roles.Pc, WorldRules.TypedThrough(new[] { Roles.Tool, Roles.Pc }));
        // nobody the partner lives with is connected (only a tool, or nothing at all): the host answers on its own
        Assert.Equal("", WorldRules.TypedThrough(new[] { Roles.Tool }));
        Assert.Equal("", WorldRules.TypedThrough(new string[0])); Assert.Equal("", WorldRules.TypedThrough(null));
    }

    [Fact]
    public void A_typed_sentence_is_one_line_of_at_most_500_characters()
    {
        Assert.Equal("작은 화분 하나 만들어줄래?", WorldRules.TypedText("  작은 화분 하나\n만들어줄래?  "));
        Assert.Equal("", WorldRules.TypedText("")); Assert.Equal("", WorldRules.TypedText("   \n ")); Assert.Equal("", WorldRules.TypedText(null));
        Assert.Equal(500, WorldRules.TypedText(new string('가', 5000)).Length);
    }
}
