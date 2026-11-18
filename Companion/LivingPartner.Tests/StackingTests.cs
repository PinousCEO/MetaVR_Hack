using LivingPartner.Host;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// Things that stand on other things (ItemDef.y / onId, WorldCommand.y / onId; the rules are WorldRules.PlaceAt,
/// Carry, Unstack, Settle): what the host takes over from the headset's "place", what moves along when a thing that
/// others stand on is moved, and what comes down when it is put away. And where a thing that was just made is put.
/// </summary>
public class StackingTests
{
    static ItemDef Thing(string kindId, string id = null, bool inRoom = true, float x = 0f, float z = 0f, float yaw = 0f)
    {
        var k = ItemKinds.Find(kindId);
        return new ItemDef { id = id ?? "item-" + kindId, name = kindId, kind = k.id, uses = (string[])k.uses.Clone(), sizeM = k.sizeM, status = "ready", inRoom = inRoom, x = x, z = z, yaw = yaw };
    }

    static (List<ItemDef> items, ItemDef desk, ItemDef book, ItemDef cup) Desk()
    {
        var desk = Thing("desk", x: -0.17f, z: 0.02f); var book = Thing("book", x: 0.10f, z: -0.07f); var cup = Thing("cup", x: 0.20f, z: -0.08f);
        return (new List<ItemDef> { desk, book, cup }, desk, book, cup);
    }

    // ------------------------------------------------------------------ place
    [Fact]
    public void A_thing_put_on_another_keeps_its_height_and_what_it_stands_on()
    {
        var (items, desk, book, _) = Desk();
        WorldRules.PlaceAt(items, book, -0.15f, 0.03f, 30f, 0.052f, "item-desk");
        Assert.True(book.inRoom);
        Assert.Equal(-0.15f, book.x); Assert.Equal(0.03f, book.z); Assert.Equal(30f, book.yaw);
        Assert.Equal(0.052f, book.y); Assert.Equal("item-desk", book.onId);
        Assert.Equal(desk, WorldRules.BaseFor(items, book, "item-desk"));
        Assert.Equal(new[] { book }, WorldRules.Riders(items, desk));
        // back onto the desk itself: no height, on nothing
        WorldRules.PlaceAt(items, book, 0.1f, -0.07f, 0f, 0f, "");
        Assert.Equal(0f, book.y); Assert.Equal("", book.onId);
        Assert.Empty(WorldRules.Riders(items, desk));
        // out of the bag straight onto the table
        var lamp = Thing("lamp", inRoom: false); items.Add(lamp);
        WorldRules.PlaceAt(items, lamp, -0.2f, 0.02f, 0f, 0.05f, "item-desk");
        Assert.True(lamp.inRoom); Assert.Equal("item-desk", lamp.onId); Assert.Equal(0.05f, lamp.y);
        // it travels: in the command, in the state file, in the snapshot
        var cmd = Proto.FromJson<WorldCommand>(Proto.ToJson(new WorldCommand { cmd = "place", id = "item-book", x = 0.1f, z = 0.2f, yaw = 15f, y = 0.05f, onId = "item-desk" }));
        Assert.Equal(0.05f, cmd.y); Assert.Equal("item-desk", cmd.onId);
        var back = Proto.FromJson<ItemDef>(Proto.ToJson(lamp));
        Assert.Equal(0.05f, back.y); Assert.Equal("item-desk", back.onId);
        Assert.Equal(0f, new ItemDef().y); Assert.Equal("", new ItemDef().onId); Assert.Equal("", new WorldCommand().onId);
    }

    [Fact]
    public void What_it_is_put_on_has_to_be_a_thing_it_can_stand_on_and_the_height_is_within_reason()
    {
        var (items, desk, book, cup) = Desk();
        var chair = Thing("chair", inRoom: false); items.Add(chair);
        // nothing of that name, itself, a thing that is in the bag: it stands on the desk, whatever height was sent
        foreach (string on in new[] { "item-nothing", "item-book", "item-chair", " ", null })
        {
            WorldRules.PlaceAt(items, book, 0.1f, 0.1f, 0f, 0.07f, on);
            Assert.Equal("", book.onId); Assert.Equal(0f, book.y);
            Assert.Null(WorldRules.BaseFor(items, book, on));
        }
        // a height with nothing under it is no height
        WorldRules.PlaceAt(items, book, 0.1f, 0.1f, 0f, 0.3f, "");
        Assert.Equal(0f, book.y);
        // between 0 and StackMaxY, and a real number
        Assert.Equal(0.4f, WorldRules.StackMaxY);
        WorldRules.PlaceAt(items, book, 0f, 0f, 0f, 9f, "item-desk"); Assert.Equal(WorldRules.StackMaxY, book.y);
        WorldRules.PlaceAt(items, book, 0f, 0f, 0f, -1f, "item-desk"); Assert.Equal(0f, book.y); Assert.Equal("item-desk", book.onId);
        WorldRules.PlaceAt(items, book, 0f, 0f, 0f, float.NaN, "item-desk"); Assert.Equal(0f, book.y);
        WorldRules.PlaceAt(items, book, float.NaN, float.PositiveInfinity, float.NaN, 0.05f, "item-desk");
        Assert.Equal(0f, book.x); Assert.Equal(0f, book.z); Assert.Equal(0f, book.yaw);
        WorldRules.PlaceAt(items, book, 99f, -99f, 725f, 0.05f, "item-desk");
        Assert.Equal(1.5f, book.x); Assert.Equal(-1.5f, book.z); Assert.Equal(5f, book.yaw);
        // never a ring: the desk cannot be put on the book that stands on it - nor on the cup that stands on that book
        WorldRules.PlaceAt(items, book, -0.17f, 0.02f, 0f, 0.05f, "item-desk");
        WorldRules.PlaceAt(items, cup, -0.17f, 0.02f, 0f, 0.07f, "item-book");
        Assert.Equal("item-book", cup.onId);
        Assert.Null(WorldRules.BaseFor(items, desk, "item-book")); Assert.Null(WorldRules.BaseFor(items, desk, "item-cup"));
        WorldRules.PlaceAt(items, desk, -0.17f, 0.02f, 0f, 0.1f, "item-cup");
        Assert.Equal("", desk.onId); Assert.Equal(0f, desk.y);
        Assert.Equal("item-desk", book.onId); Assert.Equal("item-book", cup.onId);
        // nonsense is survived
        WorldRules.PlaceAt(null, null, 0, 0, 0, 0, "x"); WorldRules.PlaceAt(items, null, 0, 0, 0, 0, "x");
        Assert.Empty(WorldRules.Riders(null, desk)); Assert.Empty(WorldRules.Riders(items, null));
    }

    [Fact]
    public void When_a_thing_is_moved_what_stands_on_it_moves_along()
    {
        var (items, desk, book, cup) = Desk();
        WorldRules.PlaceAt(items, book, -0.12f, 0.03f, 10f, 0.05f, "item-desk");       // 5 cm to the right of the desk's middle, 1 cm behind it
        WorldRules.PlaceAt(items, cup, -0.12f, 0.03f, 0f, 0.07f, "item-book");         // and the cup on the book
        var lamp = Thing("lamp", x: 0.3f, z: 0.1f); items.Add(lamp);                  // stands on the desk top of the room: not on the table
        // the desk is carried 30 cm to the right and 10 cm away: the same for the book and for the cup on it
        WorldRules.PlaceAt(items, desk, 0.13f, 0.12f, 0f, 0f, "");
        Assert.Equal(0.18f, book.x, 5); Assert.Equal(0.13f, book.z, 5); Assert.Equal(10f, book.yaw, 4); Assert.Equal(0.05f, book.y, 5); Assert.Equal("item-desk", book.onId);
        Assert.Equal(0.18f, cup.x, 5); Assert.Equal(0.13f, cup.z, 5); Assert.Equal(0.07f, cup.y, 5); Assert.Equal("item-book", cup.onId);
        Assert.Equal(0.3f, lamp.x); Assert.Equal(0.1f, lamp.z);
        // the desk is turned a quarter where it stands: what is on it turns about the DESK'S middle, and by as much itself
        WorldRules.PlaceAt(items, desk, 0.13f, 0.12f, 90f, 0f, "");
        // (5 cm to the right and 1 cm behind, turned a quarter to the right: 1 cm to the right and 5 cm in front)
        Assert.Equal(0.13f + 0.01f, book.x, 4); Assert.Equal(0.12f - 0.05f, book.z, 4); Assert.Equal(100f, book.yaw, 3);
        Assert.Equal(book.x, cup.x, 4); Assert.Equal(book.z, cup.z, 4); Assert.Equal(90f, cup.yaw, 3);
        // turned back and carried at once
        WorldRules.PlaceAt(items, desk, 0f, 0f, 0f, 0f, "");
        Assert.Equal(0.05f, book.x, 4); Assert.Equal(0.01f, book.z, 4); Assert.Equal(10f, book.yaw, 3);
        // moving the book alone leaves the desk where it is, takes the cup along, and takes the book off the table when it is put elsewhere
        WorldRules.PlaceAt(items, book, 0.3f, -0.1f, 10f, 0f, "");
        Assert.Equal(0f, desk.x); Assert.Equal("", book.onId); Assert.Equal(0f, book.y);
        Assert.Equal(0.3f, cup.x, 4); Assert.Equal(-0.1f, cup.z, 4); Assert.Equal(0.02f, cup.y, 4);        // it stood 2 cm above the book's foot, and still does
        Assert.Equal("item-book", cup.onId);
        Assert.Empty(WorldRules.Riders(items, desk));
        // a thing in the bag carries nothing along when it is taken out
        var bed = Thing("bed", inRoom: false); items.Add(bed);
        var toy = Thing("toy", x: 0.4f, z: 0.4f); toy.onId = "item-bed"; toy.y = 0.03f; items.Add(toy);       // (a leftover from an older state)
        WorldRules.PlaceAt(items, bed, 0.2f, 0.3f, 45f, 0f, "");
        Assert.Equal(0.4f, toy.x); Assert.Equal(0.4f, toy.z);
    }

    [Fact]
    public void When_a_thing_is_put_away_or_thrown_away_what_stood_on_it_comes_down_to_the_desk()
    {
        var (items, desk, book, cup) = Desk();
        WorldRules.PlaceAt(items, book, -0.12f, 0.03f, 10f, 0.05f, "item-desk");
        WorldRules.PlaceAt(items, cup, -0.12f, 0.03f, 0f, 0.07f, "item-book");
        var lamp = Thing("lamp"); items.Add(lamp);
        WorldRules.PlaceAt(items, lamp, -0.22f, 0.02f, 0f, 0.05f, "item-desk");
        // the desk goes into the bag: the book and the lamp stand on the desk top where they were, the cup still on the book
        Assert.Equal(2, WorldRules.Unstack(items, desk));
        desk.inRoom = false;
        Assert.Equal(-0.12f, book.x, 5); Assert.Equal(0.03f, book.z, 5); Assert.Equal(0f, book.y); Assert.Equal("", book.onId);
        Assert.Equal(0f, lamp.y); Assert.Equal("", lamp.onId); Assert.Equal(-0.22f, lamp.x, 5);
        Assert.Equal("item-book", cup.onId); Assert.Equal(0.02f, cup.y, 5); Assert.Equal(-0.12f, cup.x, 5);
        Assert.True(book.inRoom && lamp.inRoom && cup.inRoom);
        Assert.Equal(0, WorldRules.Unstack(items, desk));              // nothing left on it
        // the book is thrown away: the cup stands on the desk top
        Assert.Equal(1, WorldRules.Unstack(items, book));
        items.Remove(book);
        Assert.Equal(0f, cup.y); Assert.Equal("", cup.onId);
        Assert.Equal(0, WorldRules.Unstack(null, book)); Assert.Equal(0, WorldRules.Unstack(items, null));
    }

    [Fact]
    public void An_older_or_damaged_state_is_put_in_order()
    {
        var (items, desk, book, cup) = Desk();
        var chair = Thing("chair", inRoom: false); items.Add(chair);
        book.onId = "item-gone"; book.y = 0.05f;                       // what it stood on is gone
        cup.onId = "item-chair"; cup.y = 0.04f;                        // what it stood on is in the bag
        chair.onId = "item-desk"; chair.y = 0.05f;                     // in the bag itself: it stands on nothing
        var lamp = Thing("lamp"); lamp.onId = "item-lamp"; lamp.y = 0.1f; items.Add(lamp);          // on itself
        var a = Thing("toy", "item-a"); var b = Thing("ball", "item-b"); a.onId = "item-b"; a.y = 0.03f; b.onId = "item-a"; b.y = 0.03f; items.Add(a); items.Add(b);      // a ring
        var plant = Thing("plant"); plant.onId = null; plant.y = float.NaN; items.Add(plant);
        var food = Thing("food"); food.onId = "item-desk"; food.y = 7f; items.Add(food);            // far too high
        var record = Thing("record"); record.onId = "item-desk"; record.y = 0.05f; items.Add(record);     // as it should be
        items.Add(null);
        Assert.Equal(7, WorldRules.Settle(items));
        foreach (var i in new[] { book, cup, chair, lamp, a, plant }) { Assert.Equal(0f, i.y); Assert.Equal("", i.onId); }
        Assert.Equal("item-a", b.onId); Assert.Equal(0.03f, b.y);     // (the ring is cut at one place: b stands on a, a on the desk)
        Assert.Equal("item-desk", food.onId); Assert.Equal(2f * WorldRules.StackMaxY, food.y);
        Assert.Equal("item-desk", record.onId); Assert.Equal(0.05f, record.y);
        Assert.Equal(0, WorldRules.Settle(items));
        Assert.Equal(0, WorldRules.Settle(null));
    }

    [Fact]
    public void The_partner_goes_on_using_things_that_stand_on_a_table()
    {
        // a book on the desk can still be read, a toy on the bed played with, the record player on the table used - and the desk with a chair studied at
        var desk = Thing("desk", x: -0.17f, z: 0.02f); var chair = Thing("chair", x: -0.17f, z: 0.08f);
        var book = Thing("book"); var record = Thing("record");
        var items = new List<ItemDef> { desk, chair, book, record };
        WorldRules.PlaceAt(items, book, -0.15f, 0.02f, 0f, 0.05f, "item-desk");
        WorldRules.PlaceAt(items, record, -0.2f, 0.02f, 0f, 0.05f, "item-desk");
        var can = Activities.Possible(items).Select(x => x.name).ToList();
        Assert.Contains("study", can); Assert.Contains("read", can); Assert.Contains("sit", can); Assert.Contains("music", can);
        Assert.Contains("item-book", Activities.Find(items, "read").itemIds);
        var w = new WorldState { items = items };
        Assert.Equal("read", WorldRules.UseDecision(w, book, Env.Mr));
        Assert.Equal("music:play", WorldRules.UseDecision(w, record, Env.Mr));
        Assert.Equal("study", WorldRules.UseDecision(w, desk, Env.Mr));
        w.doing = "read";
        Assert.True(WorldRules.DoingWith(w, book));
        Assert.Equal("stop", WorldRules.UseDecision(w, book, Env.Mr));
        // the table is put away: the book comes down and can still be read; studying is over
        WorldRules.Unstack(items, desk); desk.inRoom = false;
        Assert.Equal(0f, book.y);
        Assert.NotNull(Activities.Find(items, "read")); Assert.Null(Activities.Find(items, "study"));
    }

    // ------------------------------------------------------------------ where a thing that was just made goes
    [Fact]
    public void A_thing_that_was_just_made_stands_in_plain_sight_right_in_front_of_the_home_spot()
    {
        // the user's desk on the evening of 2026-11-17 (furniture and things about, the hammer still to come)
        var items = new List<ItemDef>
        {
            Thing("desk", "item-a52d813d", x: -0.14f, z: 0.20f), Thing("chair", "item-a3c3e3a6", x: -0.27f, z: 0.04f), Thing("record", "item-a28c241a", x: -0.17f, z: 0.22f),
            Thing("toy", "item-d5df29cd", x: -0.26f, z: 0.23f), Thing("toy", "item-1cbabe59", x: 0.08f, z: 0.13f),
        };
        items[0].sizeM = 0.12f; items[3].sizeM = 0.10f;
        var hammer = new ItemDef { id = "item-d65ad2e4", name = "망치", kind = "toy", uses = new[] { ItemUses.Decor }, sizeM = 0.07f, status = "making", inRoom = true, y = 0.3f, onId = "item-a52d813d" };
        Activities.Arrange(items, hammer);
        items.Add(hammer);
        // in front of the home spot: towards the user (z < 0), within reach of the partner, on the desk itself
        Assert.True(hammer.z < 0f, $"it went to z = {hammer.z}");
        Assert.InRange(MathF.Abs(hammer.x), 0f, 0.31f); Assert.InRange(hammer.z, -0.13f, -0.05f);
        Assert.Equal(0f, hammer.y); Assert.Equal("", hammer.onId);
        Assert.Equal(0.10f, hammer.x, 5); Assert.Equal(-0.07f, hammer.z, 5);       // the first of the front spots
        // nothing stands between it and the user, and nothing stands on its spot
        foreach (var i in items.Where(i => i != hammer))
        {
            bool overlaps = MathF.Abs(i.x - hammer.x) < (i.sizeM + hammer.sizeM) * 0.5f && MathF.Abs(i.z - hammer.z) < (i.sizeM + hammer.sizeM) * 0.5f;
            bool hides = i.z < hammer.z && MathF.Abs(i.x - hammer.x) < (i.sizeM + hammer.sizeM) * 0.5f;
            Assert.False(overlaps || hides, i.id);
        }
        // the next things made go beside it, each on a free spot in front, never two on one
        var made = new List<ItemDef>();
        for (int n = 0; n < Activities.FrontSpots - 1; n++)
        {
            var t = new ItemDef { id = "item-new" + n, name = "thing", kind = "toy", uses = new[] { ItemUses.Decor }, sizeM = 0.06f, status = "ready", inRoom = true };
            Activities.Arrange(items, t); items.Add(t); made.Add(t);
            Assert.True(t.z < 0f, $"thing {n} went to z = {t.z}");
        }
        var spots = made.Append(hammer).Select(t => (t.x, t.z)).ToList();
        Assert.Equal(spots.Count, spots.Distinct().Count());
        // the front is full: the next goes to the arc round the home spot (never onto another thing)
        var late = new ItemDef { id = "item-late", name = "thing", kind = "toy", uses = new[] { ItemUses.Decor }, sizeM = 0.06f, status = "ready", inRoom = true };
        Activities.Arrange(items, late);
        Assert.DoesNotContain((late.x, late.z), spots);
        // a spot behind something large is not "in plain sight": with a sofa right in front of a front spot that spot is passed over
        var room = new List<ItemDef> { Thing("sofa", x: 0.10f, z: -0.16f) };
        var small = new ItemDef { id = "item-s", name = "cup", kind = "cup", uses = new[] { ItemUses.Eat }, sizeM = 0.06f, inRoom = true };
        Activities.Arrange(room, small);
        Assert.NotEqual(0.10f, small.x);
        Assert.Equal(-0.10f, small.x, 5); Assert.Equal(-0.07f, small.z, 5);
        // furniture keeps its own places: the desk left of the home spot, the chair behind it
        var fresh = new List<ItemDef>();
        var d = Thing("desk"); Activities.Arrange(fresh, d); fresh.Add(d);
        var c = Thing("chair"); Activities.Arrange(fresh, c); fresh.Add(c);
        Assert.Equal(-0.17f, d.x); Assert.Equal(0.02f, d.z);
        Assert.Equal(d.x, c.x); Assert.True(c.z > d.z);
    }
}
