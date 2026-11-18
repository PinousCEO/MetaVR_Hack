using LivingPartner.Host;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// "I pressed Fish Fishing in the marketplace - it was already there - and it said it PUT IT UP in the marketplace."
/// (2026-11-18, host log 11:00:22: `market: publish from quest: 'game-ec153e2f' price=10` - the user's own game, not in
/// the bag, shown on the shop board; the board's only button for it was Sell.)
/// The rules both sides are held to: ShopPress (Shared/Market.cs: what the board's one button does) and
/// MarketRules.PublishMeans (what the host makes of a `publish` command).
/// </summary>
public class W2MarketTests
{
    static MarketListing Card(bool mine = false, bool installed = false, bool listed = false, bool owned = false, int price = 0) =>
        new() { id = "game-ec153e2f", kind = ModuleKinds.Game, name = "Fish Fishing", mine = mine, installed = installed, listed = listed, owned = owned, price = price };

    // ------------------------------------------------------------------ the board's one button
    [Theory]
    [InlineData(false)]     // the tab "All" / "Games" ...
    [InlineData(true)]      // the tab "Mine"
    public void The_users_own_thing_that_is_not_in_the_bag_is_installed_by_a_press_on_every_tab(bool sellTab)
    {
        Assert.Equal(ShopPress.Install, ShopPress.What(Card(mine: true), 300, true, sellTab));
        // ... also when it is on sale already (the very thing of the complaint, after it had been put up)
        Assert.Equal(ShopPress.Install, ShopPress.What(Card(mine: true, listed: true, price: 10), 300, true, sellTab));
        Assert.False(ShopPress.SellControls(Card(mine: true), sellTab));
    }

    [Fact]
    public void The_users_own_thing_in_the_bag_is_sold_from_the_Mine_tab_only()
    {
        var notOnSale = Card(mine: true, installed: true);
        var onSale = Card(mine: true, installed: true, listed: true, price: 10);
        Assert.Equal(ShopPress.Have, ShopPress.What(notOnSale, 300, true, false));
        Assert.Equal(ShopPress.Have, ShopPress.What(onSale, 300, true, false));
        Assert.Equal(ShopPress.Sell, ShopPress.What(notOnSale, 300, true, true));
        Assert.Equal(ShopPress.StopSelling, ShopPress.What(onSale, 300, true, true));
        Assert.False(ShopPress.SellControls(notOnSale, false));
        Assert.True(ShopPress.SellControls(notOnSale, true));
    }

    [Fact]
    public void No_card_is_sold_by_a_press_outside_the_Mine_tab()
    {
        foreach (bool mine in new[] { false, true })
            foreach (bool installed in new[] { false, true })
                foreach (bool listed in new[] { false, true })
                    foreach (bool owned in new[] { false, true })
                        foreach (int price in new[] { 0, 10, 900 })
                        {
                            var card = Card(mine, installed, listed, owned, price);
                            string what = ShopPress.What(card, 300, true, false);
                            Assert.True(what != ShopPress.Sell && what != ShopPress.StopSelling, $"mine={mine} installed={installed} listed={listed} owned={owned} price={price} -> {what}");
                            // and on the tab "Mine" only what IS the user's and in the bag
                            string onMine = ShopPress.What(card, 300, true, true);
                            if (onMine is ShopPress.Sell or ShopPress.StopSelling) Assert.True(mine && installed);
                        }
    }

    [Fact]
    public void What_is_somebody_elses_behaves_as_it_always_did()
    {
        Assert.Equal(ShopPress.None, ShopPress.What(null, 300, true, false));
        Assert.Equal(ShopPress.Remove, ShopPress.What(Card(installed: true, owned: true), 300, true, false));
        Assert.Equal(ShopPress.Install, ShopPress.What(Card(owned: true, listed: true, price: 20), 300, true, false));
        Assert.Equal(ShopPress.Get, ShopPress.What(Card(listed: true), 300, true, false));
        Assert.Equal(ShopPress.Offline, ShopPress.What(Card(listed: true, price: 20), 300, false, false));
        Assert.Equal(ShopPress.Need, ShopPress.What(Card(listed: true, price: 400), 300, true, false));
        Assert.Equal(ShopPress.Buy, ShopPress.What(Card(listed: true, price: 20), 300, true, true));
    }

    // ------------------------------------------------------------------ what the host makes of `publish`
    [Fact]
    public void The_logged_press_puts_the_game_back_into_the_bag_and_sells_nothing()
    {
        // `market: publish from quest: 'game-ec153e2f' price=10`: no mark, the user's own, not installed
        Assert.Equal("install", MarketRules.PublishMeans(fromHeadset: true, mark: "", mine: true, installed: false));
        Assert.Equal("install", MarketRules.PublishMeans(true, null, true, false));
    }

    [Fact]
    public void A_publish_from_a_headset_without_the_Sell_mark_never_publishes()
    {
        foreach (bool mine in new[] { false, true })
            foreach (bool installed in new[] { false, true })
                foreach (string mark in new[] { "", null, " ", "mr", "publish", "yes" })
                    Assert.NotEqual("publish", MarketRules.PublishMeans(true, mark, mine, installed));
        Assert.Equal("have", MarketRules.PublishMeans(true, "", true, true));
        Assert.Equal("have", MarketRules.PublishMeans(true, "", false, false));
    }

    [Fact]
    public void The_explicit_Sell_control_the_page_lpctl_and_a_sentence_still_publish()
    {
        Assert.Equal("publish", MarketRules.PublishMeans(true, ShopPress.SellMark, true, true));
        Assert.Equal("publish", MarketRules.PublishMeans(true, " SELL ", true, true));
        // the page on the PC and lpctl (not a headset): their button and their command say "sell" in so many words
        Assert.Equal("publish", MarketRules.PublishMeans(false, "", true, true));
        Assert.Equal("publish", MarketRules.PublishMeans(false, "", true, false));
        Assert.Equal("publish", MarketRules.PublishMeans(false, null, false, false));      // (Publish itself then says why not)
    }
}
