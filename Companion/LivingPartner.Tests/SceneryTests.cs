using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// The grand surroundings - a mountain top, the seaside, a beach at sunset: whole views that take the place of the
/// room outside the work area ("peak" and "coast" in the headset's dome). The host knows the two patterns, sells the
/// three as modules of the makers' and writes model-made ones safely.
/// </summary>
public class SceneryTests
{
    [Fact]
    public void The_two_views_are_patterns_the_host_knows()
    {
        Assert.Contains("peak", WorldRules.Patterns); Assert.Contains("coast", WorldRules.Patterns);
        Assert.Equal(new[] { "peak", "coast" }, WorldRules.Views);
        foreach (var a in new[] { WorldDefaults.Peak(), WorldDefaults.Coast(), WorldDefaults.SunsetCoast() })
        {
            Assert.Equal("", MarketRules.AmbienceProblem(a));
            Assert.Contains(a.pattern, WorldRules.Views); Assert.Equal(1f, a.dim);
        }
        // every pattern anything that comes with the partner uses is on the list
        foreach (var a in WorldDefaults.Ambiences()) Assert.Contains(a.pattern, WorldRules.Patterns);
    }

    [Fact]
    public void A_view_the_language_model_writes_covers_the_room_whatever_it_says_about_dimming()
    {
        var dusk = WorldRules.Ambience(JObject.Parse(@"{ ""name"": ""노을 바닷가"", ""pattern"": ""Coast"", ""c0"": ""#5B4B9E"", ""c1"": ""#FF9A5C"", ""c2"": ""#F2A07A"", ""dim"": 0.2, ""speed"": 0.8 }"), "amb-x");
        Assert.Equal("coast", dusk.pattern); Assert.Equal(1f, dusk.dim);
        Assert.Equal("#5B4B9E", dusk.c0); Assert.Equal("#FF9A5C", dusk.c1); Assert.Equal("#F2A07A", dusk.c2);
        Assert.Equal("", MarketRules.AmbienceProblem(dusk));
        var peak = WorldRules.Ambience(JObject.Parse(@"{ ""pattern"": ""peak"", ""dim"": 0 }"), "amb-y");
        Assert.NotNull(peak); Assert.Equal(1f, peak.dim);                  // (with no dimming at all the others would be nothing to see: a view always is)
        // the old patterns keep the dimming they were given
        Assert.Equal(0.4f, WorldRules.Ambience(JObject.Parse(@"{ ""pattern"": ""snow"", ""dim"": 0.4 }"), "amb-z").dim, 3);
    }

    [Theory]
    [InlineData("amb-peak", "Mountain top", "산 정상", 30)]
    [InlineData("amb-coast", "Seaside", "바닷가", 30)]
    [InlineData("amb-sunset", "Sunset beach", "노을 해변", 25)]
    public void The_makers_sell_them_as_modules_that_are_not_installed_from_the_start(string id, string name, string koName, int price)
    {
        var e = MarketSeed.All().First(x => x.module.id == id);
        Assert.Equal(ModuleKinds.Ambience, e.module.kind); Assert.Equal(name, e.module.name); Assert.Equal(koName, e.koName); Assert.Equal(price, e.module.price);
        Assert.False(e.preinstalled); Assert.True(e.module.builtin);
        Assert.Equal("", MarketRules.Problem(e.module));
        Assert.True(e.module.summary.Length > 20 && e.module.gives.StartsWith("Nilo can"));
        Assert.True(e.koSummary.Length > 10 && e.koGives.StartsWith("밤비가"));
        Assert.True(e.draw.Length > 20);                                   // its shop picture: drawn through the same queue as every other
        Assert.Contains(koName, e.module.tags);
        // a new partner does not have it; it is on the shelf and wants a picture
        var s = new MarketState();
        MarketRules.Merge(s, MarketSeed.All());
        Assert.DoesNotContain(id, s.installed);
        var wish = Assert.Single(MarketRules.PicturesWanted(s, new WorldState(), MarketSeed.All().ToDictionary(x => x.module.id)), p => p.id == id);
        Assert.False(wish.owned); Assert.Equal("market-" + id, wish.file); Assert.Equal(e.draw, wish.what);
    }

    [Fact]
    public void The_shelf_has_nine_surroundings_now()
    {
        var moods = MarketSeed.All().Where(e => e.module.kind == ModuleKinds.Ambience).Select(e => e.module.id).ToArray();
        Assert.Equal(new[] { "amb-rain", "amb-night", "amb-club", "amb-aurora", "amb-petals", "amb-snow", "amb-peak", "amb-coast", "amb-sunset" }, moods);
        Assert.Equal(34, MarketSeed.All().Count);
    }

    // ------------------------------------------------------------------ the places are painted and animated views, not photographs
    [Fact]
    public void A_place_that_was_a_photograph_for_one_evening_is_the_painted_view_again()
    {
        // (2026-11-17: the three places were made 360-degree photographs for an hour, and the user's state was started once with that. The user wants them rendered and animated.)
        foreach (var a in WorldDefaults.Ambiences()) { Assert.NotEqual("photo", a.pattern); Assert.Equal("", a.image); Assert.Equal(0f, a.yaw); }
        Assert.Equal("peak", WorldDefaults.Peak().pattern); Assert.Equal("coast", WorldDefaults.Coast().pattern); Assert.Equal("coast", WorldDefaults.SunsetCoast().pattern);
        Assert.DoesNotContain("photo", WorldRules.Patterns);
        foreach (var e in MarketSeed.All().Where(e => e.module.kind == ModuleKinds.Ambience)) { Assert.DoesNotContain("photograph", e.module.summary); Assert.DoesNotContain("사진", e.koSummary); }
        // the state as that one start left it: Mountain top and Seaside owned as photographs, the Seaside switched on
        var s = new MarketState();
        MarketRules.Merge(s, MarketSeed.All());
        foreach (var m in s.catalogue.Where(m => m.id is "amb-peak" or "amb-coast")) { m.ambience.pattern = "photo"; m.ambience.image = m.id == "amb-peak" ? "table_mountain_1" : "spiaggia_di_mondello"; m.icon = "market-" + m.id + ".png"; }
        s.installed.AddRange(new[] { "amb-peak", "amb-coast" }); s.owned = new List<string> { "amb-peak", "amb-coast" };
        var w = new WorldState { ambience = new AmbienceDef { id = "amb-coast", name = "Seaside", pattern = "photo", image = "spiaggia_di_mondello", dim = 1f } };
        w.ambiences.Add(new AmbienceDef { id = "amb-peak", name = "Mountain top", pattern = "photo", image = "table_mountain_1", dim = 1f });
        w.ambiences.Add(new AmbienceDef { id = "amb-coast", name = "Seaside", pattern = "photo", image = "spiaggia_di_mondello", dim = 1f });
        s = Proto.FromJson<MarketState>(Proto.ToJson(s)); w = Proto.FromJson<WorldState>(Proto.ToJson(w));

        // the next start (Market's constructor): the shelf is the makers' own again, what is owned follows it, and what is ON follows what is owned
        MarketRules.Merge(s, MarketSeed.All());
        MarketRules.Tidy(w, s);
        MarketRules.Sync(w, s);
        Assert.True(MarketRules.Owned(w, s));
        Assert.True(MarketRules.CurrentPlace(w));
        var peak = w.ambiences.Single(a => a.id == "amb-peak"); var coast = w.ambiences.Single(a => a.id == "amb-coast");
        Assert.Equal("peak", peak.pattern); Assert.Equal("", peak.image);
        Assert.Equal("coast", coast.pattern); Assert.Equal("", coast.image);
        Assert.Equal("coast", w.ambience.pattern); Assert.Equal("", w.ambience.image); Assert.Equal("amb-coast", w.ambience.id);       // what is on right now, too
        Assert.Equal(Proto.ToJson(WorldDefaults.Coast()), Proto.ToJson(w.ambience));
        // what was bought stays bought, with its picture in the shop
        Assert.Contains("amb-peak", s.installed); Assert.Contains("amb-peak", s.owned);
        Assert.Equal("market-amb-peak.png", s.catalogue.Single(m => m.id == "amb-peak").icon);
        // again: nothing changes
        Assert.False(MarketRules.Owned(w, s)); Assert.False(MarketRules.CurrentPlace(w));
        // surroundings that are nobody's module (designed with words) are left alone
        w.ambience = new AmbienceDef { id = "amb-1a2b3c4d", name = "밤바다", pattern = "coast", dim = 1f };
        Assert.False(MarketRules.CurrentPlace(w));
        Assert.Equal("밤바다", w.ambience.name);
        w.ambience = null;
        Assert.False(MarketRules.CurrentPlace(w));
        // a pattern called "photo" is none the headset draws: from a model or from somebody else's module it becomes "none", and carries no picture
        var cleaned = WorldRules.Ambience(JObject.Parse(@"{ ""pattern"": ""photo"", ""image"": ""table_mountain_1"", ""yaw"": 40, ""dim"": 0.7 }"), "amb-x");
        Assert.Equal("none", cleaned.pattern); Assert.Equal("", cleaned.image); Assert.Equal(0f, cleaned.yaw);
        Assert.NotEqual("", MarketRules.AmbienceProblem(new AmbienceDef { id = "amb-x", name = "x", pattern = "photo", image = "table_mountain_1" }));
    }
}
