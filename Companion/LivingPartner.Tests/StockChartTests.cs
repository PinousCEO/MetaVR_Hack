using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using Xunit;

namespace LivingPartner.Tests;

public class StockChartTests
{
    [Fact]
    public async Task A_fast_feed_still_shows_the_build_stages_for_at_least_twenty_seconds()
    {
        var progress = new List<float>();
        var watch = Stopwatch.StartNew();
        var chart = await StockChart.MakeAsync("Make me a stock chart for META",
            (_, value) => progress.Add(value), CancellationToken.None,
            (symbol, _) => Task.FromResult(Prices(symbol)));
        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(20));
        Assert.Equal(new[] { .08f, .35f, .65f, .95f }, progress);
        Assert.True(StockChart.IsPack(chart));
    }

    [Theory]
    [InlineData("working", "open my stock chart", true)]
    [InlineData("queued", "open the stock chart for Meta", true)]
    [InlineData("working", "open the stock chart for Apple", false)]
    [InlineData("done", "open my stock chart", false)]
    [InlineData("cancelled", "open my stock chart", false)]
    public void Opening_a_chart_respects_its_active_job(string status, string request, bool pending)
    {
        var job = new WorkJob { kind = "tool", asked = "Make me a stock chart for Meta.", status = status };
        Assert.Equal(pending, StockChart.InProgress(new[] { job }, request) == job);
    }

    [Fact]
    public void Requested_stocks_survive_creation_and_named_open()
    {
        Assert.True(StockChart.CreateRequest("Make me a stock chart for Meta."));
        Assert.Equal(new[] { "META", "AAPL", "NVDA" }, StockChart.Symbols("make me a stock chart"));
        Assert.False(StockChart.CreateRequest("Make a PDF report about a stock chart."));
        Assert.False(StockChart.OpenRequest("Show a screenshot of a stock chart."));
        Assert.Equal(new[] { "META", "AAPL" }, StockChart.Symbols("MAKE ME A STOCK CHART FOR META AND APPLE"));
        Assert.Equal(new[] { "BRK-B" }, StockChart.Symbols("Make a stock chart for ticker BRK-B"));
        Assert.Throws<InvalidDataException>(() => StockChart.Symbols("Make a stock chart for Acme"));
        var meta = StockChart.Build(new[] { Prices("META") });
        var apple = StockChart.Build(new[] { Prices("AAPL") });
        Assert.Same(meta, StockChart.Find(new[] { meta, apple }, "open my stock chart for Meta"));
        Assert.Same(apple, StockChart.Find(new[] { meta, apple }, "open my stock chart"));
    }

    [Fact]
    public void Price_history_excludes_missing_closes_and_a_session_still_in_progress()
    {
        var first = new DateTimeOffset(2026,11,2,15,0,0,TimeSpan.Zero);
        var dates = new JArray(Enumerable.Range(0,8).Select(i => first.AddDays(i).ToUnixTimeSeconds()));
        var feed = JObject.Parse("""{"chart":{"result":[{"meta":{"symbol":"META","currency":"USD","exchangeTimezoneName":"America/New_York","currentTradingPeriod":{"regular":{}}},"indicators":{"quote":[{"close":[600,601,null,603,604,605,606,607]}]}}]}}""");
        var data = feed.SelectToken("chart.result[0]");
        data["timestamp"] = dates;
        data.SelectToken("meta.currentTradingPeriod.regular")["end"] = first.AddDays(7).AddHours(6).ToUnixTimeSeconds();
        var open = StockChart.Read("META",feed,first.AddDays(7).AddHours(1));
        Assert.Equal(6,open.days.Count);
        Assert.Equal(606m,open.days[^1].close);
        Assert.Equal(607m,StockChart.Read("META",feed,first.AddDays(7).AddHours(7)).days[^1].close);
    }

    static StockChart.Series Prices(string ticker)
    {
        var start = new DateOnly(2026,7,1);
        return new(ticker,Enumerable.Range(0,63).Select(i => new StockChart.Day(start.AddDays(i).ToString("yyyy-MM-dd"),100m+i)).ToList());
    }

    [Fact]
    public void Switching_watchlist_tabs_changes_the_visible_price()
    {
        var other = Prices("AAPL");
        other = other with { days = other.days.Select(day => day with { close = day.close + 80m }).ToList() };
        var pack = StockChart.Build(new[] { Prices("META"), other, Prices("NVDA") });
        var game = GlassScriptTests.Game(pack.script, strict:true, touch:true);
        GlassScriptTests.SkipIntro(game);
        game.Step(1f/30f,GlassScriptTests.Hand(.51f,.90f,.08f));
        game.Step(1f/30f,GlassScriptTests.Hand(.51f,.90f,.01f));
        Assert.Contains(game.sprites, s => s.text == "$242.00");
        Assert.DoesNotContain(game.sprites, s => s.text == "$162.00");
        Assert.Empty(game.Error);
    }

    [Fact]
    public void Touching_a_range_and_a_historical_point_changes_the_dated_close()
    {
        var prices = Prices("META");
        var pack = StockChart.Build(new[] { prices });
        Assert.True(GlassDisplay.IsTool(pack));
        var game = GlassScriptTests.Game(pack.script, strict:true, touch:true);
        GlassScriptTests.SkipIntro(game);
        game.Step(1f/30f,null);
        void Tap(float x, float y)
        {
            game.Step(1f/30f,GlassScriptTests.Hand(x,y,.08f));
            game.Step(1f/30f,GlassScriptTests.Hand(x,y,.01f));
        }
        Assert.Contains(game.sprites,s => s.text == "$162.00");
        Tap(game.W/2-.25f,.10f);
        Tap(.111f,.4f);
        Assert.Contains(game.sprites,s => s.text == "$158.00");
        Assert.Contains(game.sprites,s => s.text == prices.days[^5].date);
        Tap(game.W/2+.25f,.10f);
        Tap(.111f,.4f);
        Assert.Contains(game.sprites,s => s.text == "$100.00");
        Assert.Equal(0,game.Dropped);
        Assert.Empty(game.Error);
    }
}
