using LivingPartner.Host;
using LivingPartner.MarketHub;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// The marketplace hub without its web server (Hub.cs, compiled here from the hub's own source, on a store that lives
/// in memory): accounts and nicknames, what buying does to two people's stars, what a seller may do with a listing,
/// who may fetch what, the limits - and how a module travels as a .bambi file (MarketPack.cs).
/// </summary>
public class HubTests
{
    static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3, 4 };
    static readonly byte[] Glb = { (byte)'g', (byte)'l', (byte)'T', (byte)'F', 2, 0, 0, 0, 16, 0, 0, 0, 9, 9, 9, 9 };

    long _clock = 1_000_000;
    readonly MemoryHubStore _store = new();
    Hub NewHub() => new Hub(_store, () => _clock += 1000);

    static HubRegistered Join(Hub hub, string nickname)
    {
        var a = hub.Register(nickname);
        Assert.True(a.Ok, a.error);
        return a.value;
    }

    static MarketModule Ability(string id, string name = "칭찬 요정") => new MarketModule
    {
        id = id, kind = ModuleKinds.Ability, name = name, summary = "기운이 없을 때 한마디 해 줘요.", gives = "Nilo can cheer you up.", tags = new[] { "칭찬", "cheer" },
        ability = new AbilityDef { id = id, name = name, prompt = "When the user says they are tired or asks for a compliment, you answer with one warm, specific sentence about them.", examples = new[] { "칭찬해 줘", "cheer me up" } },
    };

    static MarketModule Sofa(string id) => MarketRules.Wrap(new ItemDef
    {
        id = id, name = "빨간 소파", kind = "sofa", uses = new[] { ItemUses.Sit, ItemUses.Sleep }, sizeM = 0.15f, color = "#F4A9A0", note = "소파다! 이제 앉아서 쉴 수 있겠다.", status = "ready",
        model = "item-local.glb", icon = "item-local.png", inRoom = true, x = 0.2f,
    }, 0, 5000);

    static HubPublish Offer(MarketModule m, int price, params (string name, byte[] data)[] files) =>
        new HubPublish { module = m, price = price, files = files.ToDictionary(f => f.name, f => Convert.ToBase64String(f.data)) };

    // ------------------------------------------------------------------ accounts
    [Fact]
    public void A_new_account_has_a_nickname_a_token_and_300_stars()
    {
        var hub = NewHub();
        var me = Join(hub, "  민수  ");
        Assert.Equal("민수", me.nickname);
        Assert.Equal(300, me.stars);
        Assert.Equal(64, me.token.Length);                 // 32 random bytes, as hex
        Assert.StartsWith("u-", me.userId);
        var seen = hub.Me(me.token);
        Assert.True(seen.Ok);
        Assert.Equal("민수", seen.value.nickname); Assert.Equal(300, seen.value.stars); Assert.Equal(0, seen.value.sales); Assert.Equal(0, seen.value.earned);
        Assert.Empty(seen.value.bought); Assert.Empty(seen.value.liked); Assert.Empty(seen.value.listings);
        // the token itself is never kept: only what it hashes to
        Assert.DoesNotContain(me.token, _store.ReadIndex());
        Assert.Equal(401, hub.Me("").status);
        Assert.Equal(401, hub.Me("not-a-token").status);
        Assert.Equal("unauthorized", hub.Me(null).error);
        // two accounts never share a token
        Assert.NotEqual(me.token, Join(hub, "Jiwoo").token);
    }

    [Theory]
    [InlineData("민수", "")]
    [InlineData("BambiFriend-4821", "")]
    [InlineData("밤비친구-4821", "")]
    [InlineData("Nilo Friend 482100", "nickname-invalid")]            // 17
    [InlineData("별_수집가", "")]
    [InlineData("ab", "")]
    [InlineData("열여섯글자짜리닉네임이에요열여섯", "")]            // 16 characters
    [InlineData("a", "nickname-invalid")]
    [InlineData("", "nickname-invalid")]
    [InlineData("   ", "nickname-invalid")]
    [InlineData("열일곱글자짜리닉네임이에요열일곱글", "nickname-invalid")]      // 17
    [InlineData("min<su>", "nickname-invalid")]
    [InlineData("민수!", "nickname-invalid")]
    [InlineData("--", "nickname-invalid")]
    [InlineData("a/b", "nickname-invalid")]
    [InlineData("Nilo Studio", "nickname-reserved")]
    [InlineData("bambi  studio", "nickname-reserved")]
    [InlineData("BAMBI_STUDIO", "nickname-reserved")]
    [InlineData("BambiStudio", "nickname-reserved")]
    public void A_nickname_is_2_to_16_letters_digits_spaces_dashes_and_the_makers_name_is_reserved(string nickname, string problem)
    {
        Assert.Equal(problem, HubRules.NicknameProblem(nickname));
        var a = NewHub().Register(nickname);
        Assert.Equal(problem, a.error);
        Assert.Equal(problem == "", a.Ok);
    }

    [Fact]
    public void A_nickname_belongs_to_one_person_whatever_the_case()
    {
        var hub = NewHub();
        var minsu = Join(hub, "Minsu");
        var again = hub.Register("minsu");
        Assert.Equal("nickname-taken", again.error); Assert.Equal(409, again.status);
        Assert.Equal("nickname-taken", hub.Register("MINSU ").error);
        var jiwoo = Join(hub, "지우");

        // renaming: to a free name yes, to somebody else's no, to one's own in other letters yes
        Assert.Equal("nickname-taken", hub.Rename(jiwoo.token, "minsu").error);
        Assert.Equal("nickname-reserved", hub.Rename(jiwoo.token, "Nilo Studio").error);
        Assert.Equal("nickname-invalid", hub.Rename(jiwoo.token, "x").error);
        Assert.Equal("unauthorized", hub.Rename("nobody", "새이름").error);
        Assert.Equal("MinSu", hub.Rename(minsu.token, "MinSu").value.nickname);
        Assert.Equal("별지기", hub.Rename(jiwoo.token, "별지기").value.nickname);
        // the old name is free again, the new one is taken
        Assert.Equal("nickname-taken", hub.Register("별지기").error);
        Assert.True(hub.Register("지우").Ok);
    }

    // ------------------------------------------------------------------ buying and selling
    [Fact]
    public void Buying_moves_the_price_from_the_buyer_to_the_seller_in_one_step()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var buyer = Join(hub, "지우");
        var listing = hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 20));
        Assert.True(listing.Ok, listing.error + " " + listing.detail);
        Assert.Equal("민수", listing.value.seller); Assert.Equal(20, listing.value.price); Assert.True(listing.value.listed); Assert.False(listing.value.official);

        var bought = hub.Buy(buyer.token, "ability-cheer01");
        Assert.True(bought.Ok);
        Assert.True(bought.value.ok); Assert.False(bought.value.already); Assert.Equal(20, bought.value.price); Assert.Equal(280, bought.value.stars);

        var b = hub.Me(buyer.token).value; var s = hub.Me(seller.token).value;
        Assert.Equal(280, b.stars); Assert.Equal(320, s.stars);
        Assert.Equal(600, b.stars + s.stars);              // stars are neither made nor lost
        Assert.Equal(1, s.sales); Assert.Equal(20, s.earned);
        Assert.Equal(new[] { "ability-cheer01" }, b.bought);
        var purchase = Assert.Single(b.purchases);
        Assert.Equal("칭찬 요정", purchase.name); Assert.Equal("민수", purchase.seller); Assert.Equal(20, purchase.price); Assert.True(purchase.listed);
        var mine = Assert.Single(s.listings);
        Assert.Equal(1, mine.installs); Assert.Equal(20, mine.earned);
        Assert.Equal(1, hub.Listing("ability-cheer01").value.installs);
        Assert.Equal(0, hub.Listing("ability-cheer01").value.earned);       // what a listing earned is the seller's to know
    }

    [Fact]
    public void What_is_owned_already_is_not_paid_for_twice()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var buyer = Join(hub, "지우");
        hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 20));
        Assert.True(hub.Buy(buyer.token, "ability-cheer01").Ok);
        var again = hub.Buy(buyer.token, "ability-cheer01");
        Assert.True(again.Ok);
        Assert.True(again.value.already); Assert.Equal(0, again.value.price); Assert.Equal(280, again.value.stars);
        Assert.Equal(280, hub.Me(buyer.token).value.stars);
        Assert.Equal(320, hub.Me(seller.token).value.stars);
        Assert.Equal(1, hub.Me(seller.token).value.sales);
        Assert.Equal(1, hub.Listing("ability-cheer01").value.installs);
        Assert.Single(hub.Me(buyer.token).value.bought);
    }

    [Fact]
    public void Nobody_buys_their_own_listing()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수");
        hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 20));
        var a = hub.Buy(seller.token, "ability-cheer01");
        Assert.Equal("own-listing", a.error); Assert.Equal(403, a.status);
        Assert.Equal(300, hub.Me(seller.token).value.stars);
        Assert.Equal(0, hub.Listing("ability-cheer01").value.installs);
    }

    [Fact]
    public void Not_enough_stars_says_how_many_are_missing_and_takes_none()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var buyer = Join(hub, "지우");
        hub.Publish(seller.token, Offer(Ability("ability-dear0001"), 999));
        hub.Publish(seller.token, Offer(Ability("ability-exact001", "딱 맞는 능력"), 300));
        var a = hub.Buy(buyer.token, "ability-dear0001");
        Assert.Equal("not-enough-stars", a.error); Assert.Equal(402, a.status); Assert.Equal(699, a.missing);
        Assert.Equal(300, hub.Me(buyer.token).value.stars);
        Assert.Empty(hub.Me(buyer.token).value.bought);
        Assert.Equal(300, hub.Me(seller.token).value.stars);
        // exactly enough is enough, and then there is nothing left
        Assert.True(hub.Buy(buyer.token, "ability-exact001").Ok);
        Assert.Equal(0, hub.Me(buyer.token).value.stars);
        Assert.Equal(600, hub.Me(seller.token).value.stars);
        Assert.Equal("gone", hub.Buy(buyer.token, "no-such-listing").error);
        Assert.Equal("unauthorized", hub.Buy("", "ability-exact001").error);
    }

    [Fact]
    public void Something_free_is_simply_got_and_counted()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var buyer = Join(hub, "지우");
        hub.Publish(seller.token, Offer(Ability("ability-free0001"), 0));
        var a = hub.Buy(buyer.token, "ability-free0001");
        Assert.True(a.Ok); Assert.Equal(0, a.value.price); Assert.Equal(300, a.value.stars);
        Assert.Equal(1, hub.Listing("ability-free0001").value.installs);
        Assert.Equal(1, hub.Me(seller.token).value.sales); Assert.Equal(0, hub.Me(seller.token).value.earned);
        Assert.Equal(300, hub.Me(seller.token).value.stars);
    }

    [Fact]
    public void The_seller_changes_the_price_and_the_next_buyer_pays_the_new_one()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var first = Join(hub, "지우"); var second = Join(hub, "하늘");
        hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 20));
        Assert.True(hub.Buy(first.token, "ability-cheer01").Ok);

        var edited = hub.Edit(seller.token, "ability-cheer01", new HubEdit { price = 35, summary = "  새 설명이에요.  " });
        Assert.True(edited.Ok);
        Assert.Equal(35, edited.value.price); Assert.Equal("새 설명이에요.", edited.value.summary);
        Assert.Equal(35, hub.Buy(second.token, "ability-cheer01").value.price);
        Assert.Equal(265, hub.Me(second.token).value.stars);
        Assert.Equal(280, hub.Me(first.token).value.stars);                // what was paid earlier stays as it was
        Assert.Equal(355, hub.Me(seller.token).value.stars);
        Assert.Equal(new[] { 20, 35 }, new[] { hub.Me(first.token).value.purchases[0].price, hub.Me(second.token).value.purchases[0].price });
        // the module that is handed out says the same as the listing
        var module = Proto.FromJson<MarketModule>(hub.Package(second.token, "ability-cheer01").value);
        Assert.Equal(35, module.price); Assert.Equal("새 설명이에요.", module.summary);

        // only the seller, and only a price the shop allows
        Assert.Equal("not-yours", hub.Edit(first.token, "ability-cheer01", new HubEdit { price = 1 }).error);
        Assert.Equal("price-invalid", hub.Edit(seller.token, "ability-cheer01", new HubEdit { price = 1000 }).error);
        Assert.Equal("price-invalid", hub.Edit(seller.token, "ability-cheer01", new HubEdit { price = -1 }).error);
        Assert.Equal("unauthorized", hub.Edit("", "ability-cheer01", new HubEdit { price = 1 }).error);
        Assert.Equal("gone", hub.Edit(seller.token, "nothing", new HubEdit { price = 1 }).error);
        Assert.Equal(35, hub.Listing("ability-cheer01").value.price);
        // a price of 0 is a price too (nothing left out means nothing changes)
        Assert.Equal(35, hub.Edit(seller.token, "ability-cheer01", new HubEdit()).value.price);
        Assert.Equal(0, hub.Edit(seller.token, "ability-cheer01", new HubEdit { price = 0 }).value.price);
    }

    [Fact]
    public void Taken_off_sale_it_is_gone_for_newcomers_and_stays_with_those_who_have_it()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var owner = Join(hub, "지우"); var late = Join(hub, "하늘");
        hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 20));
        Assert.True(hub.Buy(owner.token, "ability-cheer01").Ok);

        Assert.Equal("not-yours", hub.Unlist(owner.token, "ability-cheer01").error);
        var off = hub.Unlist(seller.token, "ability-cheer01");
        Assert.True(off.Ok); Assert.False(off.value.listed);
        Assert.Empty(hub.Listings().list);
        Assert.Equal("gone", hub.Buy(late.token, "ability-cheer01").error);
        Assert.Equal(300, hub.Me(late.token).value.stars);
        Assert.Equal("not-owned", hub.Package(late.token, "ability-cheer01").error);

        // the one who bought it: still theirs, still fetchable, and buying "again" is still free
        var mine = hub.Me(owner.token).value;
        Assert.Equal(new[] { "ability-cheer01" }, mine.bought);
        Assert.False(Assert.Single(mine.purchases).listed);
        Assert.True(hub.Package(owner.token, "ability-cheer01").Ok);
        Assert.True(hub.Buy(owner.token, "ability-cheer01").value.already);
        Assert.Equal(280, hub.Me(owner.token).value.stars);
        // the seller still sees it among their things, with what it brought in
        var kept = Assert.Single(hub.Me(seller.token).value.listings);
        Assert.False(kept.listed); Assert.Equal(1, kept.installs); Assert.Equal(20, kept.earned);

        // put up again: back on sale under the same id, with its history
        var back = hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 25));
        Assert.True(back.value.listed); Assert.Equal(1, back.value.installs); Assert.Equal(25, back.value.price);
        Assert.Equal(25, hub.Buy(late.token, "ability-cheer01").value.price);
    }

    [Fact]
    public void A_package_is_for_its_seller_its_buyers_and_for_anyone_when_it_is_free()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var buyer = Join(hub, "지우"); var other = Join(hub, "하늘");
        hub.Publish(seller.token, Offer(Ability("ability-paid0001"), 20));
        hub.Publish(seller.token, Offer(Ability("ability-free0001", "무료 능력"), 0));

        Assert.Equal(401, hub.Package("", "ability-paid0001").status);
        var no = hub.Package(other.token, "ability-paid0001");
        Assert.Equal("not-owned", no.error); Assert.Equal(403, no.status);
        Assert.True(hub.Package(seller.token, "ability-paid0001").Ok);
        hub.Buy(buyer.token, "ability-paid0001");
        var got = hub.Package(buyer.token, "ability-paid0001");
        Assert.True(got.Ok);
        var m = Proto.FromJson<MarketModule>(got.value);
        Assert.Equal("ability-paid0001", m.id); Assert.Equal("민수", m.author); Assert.False(m.builtin);
        Assert.Contains("warm, specific sentence", m.ability.prompt);

        // free: anyone, even without an account
        Assert.True(hub.Package(other.token, "ability-free0001").Ok);
        Assert.True(hub.Package("", "ability-free0001").Ok);
        Assert.Equal("gone", hub.Package(buyer.token, "nothing").error);

        // the seller is renamed: what is handed out carries the name they go by today
        hub.Rename(seller.token, "민수네 공방");
        Assert.Equal("민수네 공방", Proto.FromJson<MarketModule>(hub.Package(buyer.token, "ability-paid0001").value).author);
        Assert.Equal("민수네 공방", hub.Listing("ability-paid0001").value.seller);
        // what the shop shows before buying never includes what is paid for
        string shown = Proto.ToJson(hub.Listings(detail: true));
        Assert.DoesNotContain("warm, specific sentence", shown);
        Assert.Contains("칭찬해 줘", shown);
    }

    [Fact]
    public void A_listing_keeps_its_files_under_names_of_the_hubs_making_and_a_model_is_part_of_what_is_sold()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var buyer = Join(hub, "지우"); var other = Join(hub, "하늘");
        var a = hub.Publish(seller.token, Offer(Sofa("item-1a2b3c4d"), 30, ("item-local.png", Png), ("item-local.glb", Glb), ("stray.png", Png)));
        Assert.True(a.Ok, a.error + " " + a.detail);
        string icon = a.value.icon;
        Assert.StartsWith("item-1a2b3c4d-", icon); Assert.EndsWith(".png", icon);
        Assert.True(HubRules.SafeFile(icon));
        var m = Proto.FromJson<MarketModule>(hub.Package(seller.token, "item-1a2b3c4d").value);
        Assert.Equal(icon, m.icon); Assert.Equal(icon, m.item.icon);
        Assert.StartsWith("item-1a2b3c4d-", m.item.model); Assert.EndsWith(".glb", m.item.model);
        Assert.False(m.item.inRoom); Assert.Equal(0f, m.item.x);
        // only what the module names is kept
        Assert.Equal(2, _store.FileNames.Count());

        // the picture is for everyone, the model for those who may have the thing
        Assert.True(hub.FileOf("", icon).Ok);
        Assert.Equal("image/png", hub.FileOf("", icon).value.contentType);
        Assert.Equal(Png, hub.FileOf("", icon).value.data);
        Assert.Equal(401, hub.FileOf("", m.item.model).status);
        Assert.Equal("not-owned", hub.FileOf(other.token, m.item.model).error);
        Assert.True(hub.FileOf(seller.token, m.item.model).Ok);
        hub.Buy(buyer.token, "item-1a2b3c4d");
        Assert.Equal(Glb, hub.FileOf(buyer.token, m.item.model).value.data);
        Assert.Equal(404, hub.FileOf(buyer.token, "nothing.png").status);
        Assert.Equal(404, hub.FileOf(buyer.token, "..\\hub.json").status);

        // a new version without files: the ones it has are kept; with another picture: the old one is gone
        var again = Proto.FromJson<MarketModule>(hub.Package(seller.token, "item-1a2b3c4d").value);
        Assert.Equal(icon, hub.Publish(seller.token, Offer(again, 30)).value.icon);
        byte[] png2 = Png.ToArray(); png2[^1] = 99;
        again = Sofa("item-1a2b3c4d"); again.item.model = "";
        var v2 = hub.Publish(seller.token, Offer(again, 30, ("item-local.png", png2)));
        Assert.NotEqual(icon, v2.value.icon);
        Assert.Equal(404, hub.FileOf("", icon).status);
        Assert.Equal(new[] { v2.value.icon }, _store.FileNames);
        Assert.Equal(1, v2.value.installs);                // how often it was got stays with the listing
    }

    [Fact]
    public void A_listing_belongs_to_whoever_put_it_up()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var other = Join(hub, "하늘");
        Assert.True(hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 20)).Ok);
        var taken = hub.Publish(other.token, Offer(Ability("ability-cheer01", "남의 것"), 1));
        Assert.Equal("id-taken", taken.error); Assert.Equal(409, taken.status);
        Assert.Equal("칭찬 요정", hub.Listing("ability-cheer01").value.name);
        Assert.Equal("unauthorized", hub.Publish("", Offer(Ability("ability-other001"), 1)).error);
        // whatever the module says about itself, it is the seller's and it did not come with the partner
        var liar = Ability("ability-liar0001"); liar.author = "Nilo Studio"; liar.builtin = true; liar.installs = 5000;
        var a = hub.Publish(other.token, Offer(liar, 5));
        Assert.Equal("하늘", a.value.seller); Assert.False(a.value.official); Assert.Equal(0, a.value.installs);
        Assert.False(Proto.FromJson<MarketModule>(hub.Package(other.token, "ability-liar0001").value).builtin);
    }

    [Fact]
    public void The_limits_hold()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수");
        // the price
        Assert.Equal("price-invalid", hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 1000)).error);
        Assert.Equal("price-invalid", hub.Publish(seller.token, Offer(Ability("ability-cheer01"), -1)).error);
        Assert.True(hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 999)).Ok);
        // a module that is not sound
        var broken = Ability("ability-broken01"); broken.ability = null;
        var a = hub.Publish(seller.token, Offer(broken, 1));
        Assert.Equal("module-invalid", a.error); Assert.Equal(400, a.status); Assert.NotEqual("", a.detail);
        Assert.Equal("module-invalid", hub.Publish(seller.token, Offer(Ability("../../evil"), 1)).error);
        Assert.Equal("module-invalid", hub.Publish(seller.token, Offer(Ability("ability-" + new string('x', 40)), 1)).error);
        Assert.Equal("bad-request", hub.Publish(seller.token, null).error);
        Assert.Equal("bad-request", hub.Publish(seller.token, new HubPublish()).error);
        // a module of more than 300 kB (a skill with a great many words to say)
        var fat = MarketRules.Wrap(new SkillDef
        {
            id = "skill-fat00001", name = "말 많은 스킬", note = "말이 많다.", phrases = Enumerable.Range(0, 2500).Select(i => new string('가', 60) + i).ToArray(),
            steps = new List<SkillStep> { new SkillStep { act = "dance" } },
        }, 0, 1);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(Proto.ToJson(fat)) > HubRules.MaxModuleJson);
        // ... is cut down to what a skill may be, like everything that comes from somewhere else (it used to be turned away as "too-large": nothing that has been through the cleaners can be that large any more)
        var elsewhere = new Hub(new MemoryHubStore());
        var chatty = Join(elsewhere, "민수");
        Assert.True(elsewhere.Publish(chatty.token, Offer(fat, 1)).Ok);
        string kept = elsewhere.Package(chatty.token, "skill-fat00001").value;
        Assert.InRange(Proto.FromJson<MarketModule>(kept).skill.phrases.Length, 1, WorldRules.MaxPhrases);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(kept) < 4000);
        // files: at most eight, plain names, what their names say; a picture 600 kB, a model 16 MB
        byte[] poster = new byte[HubRules.MaxPictureBytes + 1]; Array.Copy(Png, poster, Png.Length);
        var fatPicture = hub.Publish(seller.token, Offer(Sofa("item-1a2b3c4d"), 1, ("item-local.png", poster)));
        Assert.Equal("too-large", fatPicture.error); Assert.Equal(413, fatPicture.status);
        Assert.False(MarketPack.LooksRight("item-local.png", poster));       // (nor is it taken out of a .bambi file, or kept when a hub hands it out)
        Assert.True(MarketPack.LooksRight("item-local.png", poster[..HubRules.MaxPictureBytes]));
        var nine = Enumerable.Range(0, 9).Select(i => ($"f{i}.png", Png)).ToArray();
        Assert.Equal("too-many-files", hub.Publish(seller.token, Offer(Sofa("item-1a2b3c4d"), 1, nine)).error);
        Assert.Equal("file-invalid", hub.Publish(seller.token, Offer(Sofa("item-1a2b3c4d"), 1, ("..\\evil.png", Png))).error);
        Assert.Equal("file-invalid", hub.Publish(seller.token, Offer(Sofa("item-1a2b3c4d"), 1, ("page.html", Png))).error);
        Assert.Equal("file-invalid", hub.Publish(seller.token, Offer(Sofa("item-1a2b3c4d"), 1, ("item-local.png", Glb))).error);      // a model under a picture's name
        var notBase64 = Offer(Sofa("item-1a2b3c4d"), 1); notBase64.files["item-local.png"] = "*** not base64 ***";
        Assert.Equal("file-invalid", hub.Publish(seller.token, notBase64).error);
        byte[] huge = new byte[HubRules.MaxFileBytes + 1]; Array.Copy(Glb, huge, Glb.Length);
        var tooBig = hub.Publish(seller.token, Offer(Sofa("item-1a2b3c4d"), 1, ("item-local.glb", huge)));
        Assert.Equal("too-large", tooBig.error); Assert.Equal(413, tooBig.status);
        // none of the refused ones left anything behind
        Assert.Single(hub.Listings().list);
        Assert.Empty(_store.FileNames);
    }

    [Fact]
    public void A_like_is_given_and_taken_back()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var fan = Join(hub, "지우");
        hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 20));
        var on = hub.Like(fan.token, "ability-cheer01");
        Assert.True(on.value.liked); Assert.Equal(1, on.value.likes);
        Assert.Equal(new[] { "ability-cheer01" }, hub.Me(fan.token).value.liked);
        Assert.Equal(1, hub.Listing("ability-cheer01").value.likes);
        var off = hub.Like(fan.token, "ability-cheer01");
        Assert.False(off.value.liked); Assert.Equal(0, off.value.likes);
        Assert.Empty(hub.Me(fan.token).value.liked);
        Assert.Equal("unauthorized", hub.Like("", "ability-cheer01").error);
        Assert.Equal("gone", hub.Like(fan.token, "nothing").error);
    }

    // ------------------------------------------------------------------ looking round
    [Fact]
    public void The_shop_is_searched_sorted_and_narrowed_down()
    {
        var hub = NewHub();
        var minsu = Join(hub, "민수"); var jiwoo = Join(hub, "지우"); var buyer = Join(hub, "하늘");
        hub.Publish(minsu.token, Offer(Ability("ability-cheer01", "칭찬 요정"), 20));
        hub.Publish(jiwoo.token, Offer(Ability("ability-quiz0001", "퀴즈 대장"), 5));
        hub.Publish(jiwoo.token, Offer(Sofa("item-1a2b3c4d"), 50));
        hub.Buy(buyer.token, "ability-quiz0001");

        Assert.Equal(new[] { "ability-quiz0001", "item-1a2b3c4d", "ability-cheer01" }, hub.Listings().list.Select(l => l.id));                 // most got first, then the newest
        Assert.Equal(new[] { "item-1a2b3c4d", "ability-quiz0001", "ability-cheer01" }, hub.Listings(sort: "new").list.Select(l => l.id));
        Assert.Equal(new[] { "ability-quiz0001", "ability-cheer01", "item-1a2b3c4d" }, hub.Listings(sort: "price").list.Select(l => l.id));
        Assert.Equal(new[] { "item-1a2b3c4d" }, hub.Listings(kind: ModuleKinds.Item).list.Select(l => l.id));
        Assert.Equal(new[] { "ability-quiz0001", "item-1a2b3c4d" }, hub.Listings(seller: "지우").list.Select(l => l.id));
        Assert.Equal(new[] { "ability-quiz0001" }, hub.Listings(q: "퀴즈").list.Select(l => l.id));
        Assert.Equal(new[] { "ability-cheer01" }, hub.Listings(q: "CHEER 민수").list.Select(l => l.id));     // a tag and the seller's name
        Assert.Empty(hub.Listings(q: "submarine").list);
        Assert.Single(hub.Listings(limit: 1).list);
        // the short list carries no contents; the long one does
        Assert.All(hub.Listings().list, l => Assert.Null(l.detail));
        Assert.Equal(new[] { "칭찬해 줘", "cheer me up" }, hub.Listings(q: "칭찬", detail: true).list[0].detail.examples);
        Assert.Equal("sofa", hub.Listing("item-1a2b3c4d").value.detail.shape);

        var profile = hub.Profile("지우");
        Assert.True(profile.Ok);
        Assert.Equal("지우", profile.value.nickname); Assert.Equal(2, profile.value.listings.Count); Assert.Equal(1, profile.value.installs); Assert.False(profile.value.official);
        Assert.Equal(404, hub.Profile("아무도").status);
    }

    [Fact]
    public void The_revision_goes_up_with_every_change_and_only_then()
    {
        var hub = NewHub();
        long r0 = hub.Revision;
        var seller = Join(hub, "민수");
        long r1 = hub.Revision;
        Assert.True(r1 > r0);
        hub.Listings(); hub.Me(seller.token); hub.Profile("민수"); hub.Buy(seller.token, "nothing");
        Assert.Equal(r1, hub.Revision);
        hub.Publish(seller.token, Offer(Ability("ability-cheer01"), 20));
        Assert.True(hub.Revision > r1);
        Assert.Equal(hub.Revision, hub.Listings().revision);
        Assert.Equal(hub.Revision, hub.Me(seller.token).value.revision);
        long r2 = hub.Revision;
        hub.Edit(seller.token, "ability-cheer01", new HubEdit { price = 20 });       // the price it has already: nothing changed
        hub.Rename(seller.token, "민수");
        Assert.Equal(r2, hub.Revision);
    }

    // ------------------------------------------------------------------ the official shop, and what is kept
    [Fact]
    public void The_official_shop_lists_everything_that_comes_with_the_partner_under_Bambi_Studio()
    {
        var hub = NewHub();
        var seeds = MarketSeed.All();
        Assert.True(hub.SeedOfficial(seeds) > 0);
        var list = hub.Listings(limit: 1000).list;
        Assert.Equal(seeds.Count, list.Count);
        Assert.All(list, l => { Assert.Equal("Nilo Studio", l.seller); Assert.True(l.official); Assert.True(l.listed); });
        foreach (var e in seeds) Assert.Equal(e.module.price, list.First(l => l.id == e.module.id).price);
        Assert.Equal("swarm", hub.Listing("game-swarm").value.template);
        Assert.Equal("gomoku",hub.Listing("game-gomoku").value.template);
        Assert.Equal("Neon",hub.Listing("skill-dj").value.detail.song);
        Assert.True(hub.Profile("bambi studio").value.official);
        // a second start changes nothing
        long revision = hub.Revision;
        Assert.Equal(0, hub.SeedOfficial(seeds));
        Assert.Equal(revision, hub.Revision);

        // buying from the makers costs what it says; what is free is anyone's
        var me = Join(hub, "지우");
        Assert.Equal(30, hub.Buy(me.token, "amb-peak").value.price);
        Assert.Equal(270, hub.Me(me.token).value.stars);
        Assert.Equal(0, hub.Buy(me.token, "game-swarm").value.price);          // (every game is free: it is "got", and its makers see that it was taken)
        Assert.Equal(270, hub.Me(me.token).value.stars);
        Assert.Equal("Demon Gate", Proto.FromJson<MarketModule>(hub.Package(me.token, "game-swarm").value).game.title);
        Assert.True(hub.Package("", "item-chair").Ok);
        Assert.Equal("not-owned", hub.Package(me.token, "skill-dj").error);
        // the tune of a listing can be listened to before buying; a listing without one has none
        Assert.Contains("\"bpm\":126", hub.Song("skill-dj").value);
        Assert.Equal(404, hub.Song("item-chair").status);
        // nobody signs in as the makers, and nobody takes one of their ids
        Assert.Equal("id-taken", hub.Publish(me.token, Offer(Ability("ability-calc"), 1)).error);

        // a module the makers no longer sell goes off sale; those who have it keep it
        Assert.True(hub.SeedOfficial(seeds.Where(e => e.module.id != "game-swarm")) > 0);
        Assert.DoesNotContain(hub.Listings(limit: 1000).list, l => l.id == "game-swarm");
        Assert.True(hub.Package(me.token, "game-swarm").Ok);
        // and when it is sold again it is back with how often it was got
        hub.SeedOfficial(seeds);
        Assert.Equal(1, hub.Listing("game-swarm").value.installs);
    }

    [Fact]
    public void Everything_is_still_there_after_a_restart()
    {
        var hub = NewHub();
        var seller = Join(hub, "민수"); var buyer = Join(hub, "지우");
        hub.Publish(seller.token, Offer(Sofa("item-1a2b3c4d"), 30, ("item-local.png", Png)));
        hub.Buy(buyer.token, "item-1a2b3c4d");
        hub.Like(buyer.token, "item-1a2b3c4d");

        var again = NewHub();                               // the same store, a new process
        Assert.Equal(270, again.Me(buyer.token).value.stars);
        Assert.Equal(330, again.Me(seller.token).value.stars);
        var l = Assert.Single(again.Listings().list);
        Assert.Equal("민수", l.seller); Assert.Equal(1, l.installs); Assert.Equal(1, l.likes);
        Assert.True(again.FileOf("", l.icon).Ok);
        Assert.True(again.Package(buyer.token, "item-1a2b3c4d").Ok);
        Assert.Equal("nickname-taken", again.Register("민수").error);
        Assert.True(again.Buy(buyer.token, "item-1a2b3c4d").value.already);
    }

    // ------------------------------------------------------------------ a module as a file
    [Fact]
    public void A_module_travels_as_a_bambi_file_with_the_files_it_names()
    {
        var m = Sofa("item-1a2b3c4d");
        byte[] file = MarketPack.Pack(m, new Dictionary<string, byte[]> { ["item-local.png"] = Png, ["item-local.glb"] = Glb });
        Assert.Equal("", MarketPack.Unpack(file, out var back, out var files));
        Assert.Equal("item-1a2b3c4d", back.id); Assert.Equal("빨간 소파", back.name); Assert.Equal(ModuleKinds.Item, back.kind);
        Assert.Equal("item-local.png", back.icon); Assert.Equal("item-local.glb", back.item.model);
        Assert.Equal(new[] { "item-local.glb", "item-local.png" }, files.Keys.OrderBy(k => k));
        Assert.Equal(Png, files["item-local.png"]); Assert.Equal(Glb, files["item-local.glb"]);

        // a file the module names and the zip does not carry: the module does without it
        Assert.Equal("", MarketPack.Unpack(MarketPack.Pack(m, new Dictionary<string, byte[]> { ["item-local.png"] = Png }), out back, out files));
        Assert.Equal("", back.item.model); Assert.Equal("item-local.png", back.item.icon);
        Assert.Single(files);
        // a picture that is not a picture is left out
        Assert.Equal("", MarketPack.Unpack(MarketPack.Pack(m, new Dictionary<string, byte[]> { ["item-local.png"] = Glb }), out back, out files));
        Assert.Equal("", back.icon); Assert.Empty(files);

        // what is not a module file is told apart, and never throws
        Assert.NotEqual("", MarketPack.Unpack(null, out back, out _)); Assert.Null(back);
        Assert.NotEqual("", MarketPack.Unpack(new byte[100], out back, out _));
        Assert.NotEqual("", MarketPack.Unpack(System.Text.Encoding.UTF8.GetBytes("this is a text file, not a module at all"), out back, out _));
        var broken = Sofa("item-1a2b3c4d"); broken.item = null;
        Assert.NotEqual("", MarketPack.Unpack(MarketPack.Pack(broken, null), out back, out _));
        Assert.Null(back);
    }

    [Fact]
    public void A_module_from_somewhere_else_is_made_safe_and_can_be_given_a_new_id()
    {
        var m = Sofa("item-1a2b3c4d");
        m.builtin = true; m.installs = 77; m.item.inRoom = true; m.item.x = 3; m.item.status = "making"; m.icon = "..\\..\\secret.png"; m.item.model = "model.exe";
        Assert.Equal("", MarketPack.Sanitise(m));
        Assert.False(m.builtin); Assert.Equal(0, m.installs); Assert.False(m.item.inRoom); Assert.Equal(0f, m.item.x); Assert.Equal("ready", m.item.status);
        Assert.Equal("", m.icon); Assert.Equal("", m.item.model); Assert.Equal("item-local.png", m.item.icon);

        var skill = MarketRules.Wrap(new SkillDef { id = "skill-9f8e7d6c", name = "집중 모드", note = "비를 내린다.", itemId = "item-of-the-seller", builtin = true, steps = new List<SkillStep> { new SkillStep { act = "rain", arg = "on" } } }, 0, 1);
        Assert.Equal("", MarketPack.Sanitise(skill));
        Assert.Equal("", skill.skill.itemId); Assert.False(skill.skill.builtin);

        MarketPack.ReId(m, "item-new00001");
        Assert.Equal("item-new00001", m.id); Assert.Equal("item-new00001", m.item.id);
        Assert.Equal("", MarketRules.Problem(m));
        var game = MarketRules.Clone(MarketSeed.All().First(e => e.module.id == "game-swarm").module);
        MarketPack.ReId(game, "game-new00001");
        Assert.Equal("game-new00001", game.game.id);
        Assert.Equal("", MarketRules.Problem(game));
        Assert.Equal(new[] { "icon", "icon", "enemy", "pickup" }, MarketPack.FileRefs(game).Select(r => r.role));       // (the listing's picture, the pack's own sticker, its creature, its pick-up)
        Assert.Equal(new[] { "icon", "icon", "model" }, MarketPack.FileRefs(m).Select(r => r.role));
    }

    [Theory]
    [InlineData("127.0.0.1:47620", "http://127.0.0.1:47620")]
    [InlineData("http://192.168.0.12:47620/", "http://192.168.0.12:47620")]
    [InlineData("https://market.example.com/some/path?x=1", "https://market.example.com")]
    [InlineData("  http://localhost:47620  ", "http://localhost:47620")]
    [InlineData("ftp://example.com", "")]
    [InlineData("", "")]
    [InlineData("not a url", "")]
    public void A_hub_address_is_kept_as_scheme_host_and_port(string typed, string kept)
    {
        Assert.Equal(kept, HubRules.CleanUrl(typed));
    }
}
