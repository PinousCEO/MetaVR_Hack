using LivingPartner.Host;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// "I made it! Shall I put it up in the marketplace?" (MakeOffer in MarketCatalog.cs, the rules Market.cs keeps to):
/// only what the partner made is offered, one offer at a time, and it ends with a yes, a no, three minutes of
/// silence or the thing going away. And the answers that are understood in plain words.
/// </summary>
public class OfferTests
{
    [Fact]
    public void What_the_partner_made_is_offered_at_a_price_that_fits_its_kind()
    {
        var offer = new MakeOffer();
        Assert.False(offer.Open);
        Assert.True(offer.Begin("item-1a2b3c4d", "빨간 의자", ModuleKinds.Item, Came.Made, 100));
        Assert.True(offer.Open); Assert.Equal("item-1a2b3c4d", offer.id); Assert.Equal("빨간 의자", offer.name); Assert.Equal(10, offer.price); Assert.Equal(1, offer.seq);
        Assert.Equal(10, MakeOffer.Suggest(ModuleKinds.Item)); Assert.Equal(15, MakeOffer.Suggest(ModuleKinds.Skill)); Assert.Equal(10, MakeOffer.Suggest(ModuleKinds.Ambience));
        Assert.Equal(15, MakeOffer.Suggest(ModuleKinds.Ability)); Assert.Equal(25, MakeOffer.Suggest(ModuleKinds.Game));
        Assert.InRange(MakeOffer.Suggest("something new"), 0, MakeOffer.MaxSuggested);
        // an answer needs no name for 25 seconds after the question, and the question stands for three minutes
        Assert.True(offer.Listening(100)); Assert.True(offer.Listening(125)); Assert.False(offer.Listening(125.1)); Assert.True(offer.Open);
    }

    [Fact]
    public void What_was_bought_installed_or_imported_is_never_offered()
    {
        var offer = new MakeOffer();
        foreach (var how in new[] { Came.Bought, Came.Installed, Came.Imported })
        {
            Assert.False(offer.Begin("item-chair", "Chair", ModuleKinds.Item, how, 100));
            Assert.False(offer.Open); Assert.Equal(0, offer.seq);
        }
        Assert.False(offer.Begin("", "nothing", ModuleKinds.Item, Came.Made, 100));
        // nor does getting something from the shop disturb a question that is open
        Assert.True(offer.Begin("game-1a2b3c4d", "유령 잡기", ModuleKinds.Game, Came.Made, 100));
        Assert.False(offer.Begin("item-bed", "Bed", ModuleKinds.Item, Came.Bought, 110));
        Assert.Equal("game-1a2b3c4d", offer.id); Assert.Equal(25, offer.price); Assert.Equal(1, offer.seq);
    }

    [Fact]
    public void Yes_publishes_at_the_price_named_or_the_suggested_one_and_no_keeps_it()
    {
        var offer = new MakeOffer();
        offer.Begin("skill-1a2b3c4d", "집중 모드", ModuleKinds.Skill, Came.Made, 100);
        var a = offer.Answer("", "yes", 0);                    // no price named: the one the partner suggested
        Assert.True(a.answered); Assert.True(a.publish); Assert.Equal("skill-1a2b3c4d", a.id); Assert.Equal(15, a.price);
        Assert.False(offer.Open);                              // over, either way
        Assert.False(offer.Answer("", "yes", 0).answered);     // and it cannot be answered twice

        offer.Begin("skill-1a2b3c4d", "집중 모드", ModuleKinds.Skill, Came.Made, 200);
        a = offer.Answer("skill-1a2b3c4d", "yes", 20);
        Assert.True(a.publish); Assert.Equal(20, a.price);
        offer.Begin("skill-1a2b3c4d", "집중 모드", ModuleKinds.Skill, Came.Made, 300);
        Assert.Equal(MarketRules.MaxPrice, offer.Answer("", "yes", 5000).price);
        offer.Begin("skill-1a2b3c4d", "집중 모드", ModuleKinds.Skill, Came.Made, 400);
        a = offer.Answer("", "yes free", 0);                   // nothing only when they said so
        Assert.True(a.publish); Assert.Equal(0, a.price);

        offer.Begin("skill-1a2b3c4d", "집중 모드", ModuleKinds.Skill, Came.Made, 500);
        a = offer.Answer("", "no", 30);
        Assert.True(a.answered); Assert.False(a.publish); Assert.Equal("skill-1a2b3c4d", a.id); Assert.Equal(0, a.price);
        Assert.False(offer.Open);
        Assert.Equal(5, offer.seq);                            // every offer had a number of its own
    }

    [Fact]
    public void An_unanswered_offer_ends_by_itself_after_three_minutes()
    {
        var offer = new MakeOffer();
        offer.Begin("ability-1a2b3c4d", "퀴즈", ModuleKinds.Ability, Came.Made, 1000);
        Assert.False(offer.Expire(1000)); Assert.False(offer.Expire(1179.9)); Assert.True(offer.Open);
        Assert.True(offer.Expire(1180));                       // once
        Assert.False(offer.Open); Assert.Equal("", offer.id); Assert.Equal(0, offer.price);
        Assert.False(offer.Expire(1181));
        Assert.False(offer.Answer("", "yes", 0).answered);     // too late: nothing is published
        Assert.Equal(1, offer.seq);                            // (the number stays: the next offer is a new one)
    }

    [Fact]
    public void A_newer_offer_replaces_an_older_one()
    {
        var offer = new MakeOffer();
        offer.Begin("item-1a2b3c4d", "의자", ModuleKinds.Item, Came.Made, 100);
        offer.Begin("game-5e6f7a8b", "유령 잡기", ModuleKinds.Game, Came.Made, 130);
        Assert.Equal("game-5e6f7a8b", offer.id); Assert.Equal(25, offer.price); Assert.Equal(2, offer.seq);
        Assert.False(offer.Expire(285));                       // the three minutes are the newer one's
        // an answer meant for the older one (a card still on show somewhere) answers nothing
        Assert.False(offer.Answer("item-1a2b3c4d", "yes", 0).answered);
        Assert.True(offer.Open);
        Assert.True(offer.Answer("game-5e6f7a8b", "yes", 0).publish);
    }

    [Fact]
    public void An_offer_for_something_that_is_gone_is_dropped()
    {
        var offer = new MakeOffer();
        offer.Begin("item-1a2b3c4d", "의자", ModuleKinds.Item, Came.Made, 100);
        Assert.False(offer.Drop("item-other")); Assert.True(offer.Open);
        Assert.True(offer.Drop("item-1a2b3c4d")); Assert.False(offer.Open);
        Assert.False(offer.Drop("item-1a2b3c4d"));
    }

    [Theory]
    [InlineData("응", true, 0, false)]
    [InlineData("응!", true, 0, false)]
    [InlineData("그래 올려줘", true, 0, false)]
    [InlineData("공개해", true, 0, false)]
    [InlineData("좋아, 올려 줘.", true, 0, false)]
    [InlineData("20별에 올려줘", true, 20, false)]
    [InlineData("응 30별", true, 30, false)]
    [InlineData("별 15개에 팔아줘", true, 15, false)]
    [InlineData("무료로 올려", true, 0, true)]
    [InlineData("yes", true, 0, false)]
    [InlineData("Sure, put it up", true, 0, false)]
    [InlineData("publish it for 20 stars", true, 20, false)]
    [InlineData("아니", false, 0, false)]
    [InlineData("아니야", false, 0, false)]
    [InlineData("됐어", false, 0, false)]
    [InlineData("그냥 둬", false, 0, false)]
    [InlineData("아니, 됐어", false, 0, false)]
    [InlineData("아니야 괜찮아", false, 0, false)]
    [InlineData("no, not now", false, 0, false)]
    [InlineData("아니 올리지 마", false, 0, false)]
    [InlineData("no thanks", false, 0, false)]
    [InlineData("Not now.", false, 0, false)]
    public void A_plain_answer_is_understood_without_a_language_model(string said, bool yes, int price, bool free)
    {
        var a = MakeOffer.Understand(said);
        Assert.NotNull(a);
        Assert.Equal(yes, a.Value.yes); Assert.Equal(price, a.Value.price); Assert.Equal(free, a.Value.free);
    }

    [Theory]
    [InlineData("")]
    [InlineData("어")]                                        // thinking aloud is not a yes
    [InlineData("그냥")]
    [InlineData("이거")]
    [InlineData("아니, 그거 말고 의자 꺼내 줘")]              // begins like a no and is a request
    [InlineData("네가 해")]
    [InlineData("좋아하는 노래 틀어줘")]
    [InlineData("그래도 비는 꺼줘")]
    [InlineData("상점 열어줘")]
    [InlineData("nothing")]
    [InlineData("okay google")]
    [InlineData("오늘 점심 뭐 먹지")]
    [InlineData("응 그리고 타이머 5분 맞춰줘 그리고 비도 내려줘")]
    public void Anything_that_is_more_than_an_answer_is_left_to_the_model(string said)
    {
        Assert.Null(MakeOffer.Understand(said));
    }
}
