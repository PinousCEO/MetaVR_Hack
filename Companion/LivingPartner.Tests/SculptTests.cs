using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// A thing sculpted by the language model out of simple parts (ItemDef.parts): the rules that make whatever comes
/// back safe to build and bring it to the measure every shape of the partner's world has - longest side 1, standing on
/// y = 0, its middle over the origin (WorldRules.ValidateParts / Sculpt, compiled here from the host's own source).
/// </summary>
public class SculptTests
{
    static ShapePart Part(string shape, float x, float y, float z, float sx, float sy, float sz, string color = "#F4A9A0", float round = 0, float rx = 0, float ry = 0, float rz = 0) =>
        new ShapePart { shape = shape, x = x, y = y, z = z, sx = sx, sy = sy, sz = sz, color = color, round = round, rx = rx, ry = ry, rz = rz };

    /// <summary>A table 2 wide, 1.2 high and 1 deep, standing with its feet at y = 5 and well off to one side: a top and two legs.</summary>
    static List<ShapePart> Table(float scale = 1f)
    {
        var list = new List<ShapePart>
        {
            Part("box", 10f, 6.1f, 3f, 2f, 0.2f, 1f),
            Part("box", 9.2f, 5.5f, 3f, 0.2f, 1.0f, 0.2f),
            Part("box", 10.8f, 5.5f, 3f, 0.2f, 1.0f, 0.2f),
        };
        foreach (var p in list) { p.x *= scale; p.y *= scale; p.z *= scale; p.sx *= scale; p.sy *= scale; p.sz *= scale; }
        return list;
    }

    // ------------------------------------------------------------------ the common measure
    [Fact]
    public void The_whole_is_brought_to_the_common_measure()
    {
        var parts = WorldRules.ValidateParts(Table(), out float height);
        Assert.Equal(3, parts.Count);
        // longest side (the width, 2) becomes 1; so the height 1.2 becomes 0.6
        Assert.Equal(0.6f, height, 3);
        var top = parts[0];
        Assert.Equal(1f, top.sx, 3); Assert.Equal(0.1f, top.sy, 3); Assert.Equal(0.5f, top.sz, 3);
        // its middle over the origin, standing on the ground
        Assert.Equal(0f, top.x, 3); Assert.Equal(0f, top.z, 3); Assert.Equal(0.55f, top.y, 3);
        Assert.Equal(-0.4f, parts[1].x, 3); Assert.Equal(0.4f, parts[2].x, 3);
        Assert.Equal(0.25f, parts[1].y, 3);         // a leg 0.5 high stands on y = 0
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(1f)]
    [InlineData(250f)]
    public void The_unit_the_sculptor_worked_in_does_not_matter(float scale)
    {
        var a = WorldRules.ValidateParts(Table(), out float ha);
        var b = WorldRules.ValidateParts(Table(scale), out float hb);
        Assert.Equal(ha, hb, 3);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].x, b[i].x, 2); Assert.Equal(a[i].y, b[i].y, 2); Assert.Equal(a[i].z, b[i].z, 2);
            Assert.Equal(a[i].sx, b[i].sx, 2); Assert.Equal(a[i].sy, b[i].sy, 2); Assert.Equal(a[i].sz, b[i].sz, 2);
        }
    }

    [Fact]
    public void Checking_twice_changes_nothing()
    {
        float seat = 5.9f, top = 6.2f;
        var once = WorldRules.ValidateParts(Table(), ref seat, ref top, out float h1);
        float seat1 = seat, top1 = top;
        var twice = WorldRules.ValidateParts(once, ref seat, ref top, out float h2);
        Assert.Equal(h1, h2, 3); Assert.Equal(seat1, seat, 3); Assert.Equal(top1, top, 3);
        for (int i = 0; i < once.Count; i++)
        {
            Assert.Equal(once[i].x, twice[i].x, 3); Assert.Equal(once[i].y, twice[i].y, 3); Assert.Equal(once[i].sx, twice[i].sx, 3);
            Assert.Equal(once[i].color, twice[i].color); Assert.Equal(once[i].round, twice[i].round, 3);
        }
    }

    [Fact]
    public void Numbers_are_short_on_the_wire()
    {
        var list = Table();
        list[0].x = 10.0123456f; list[1].rz = 12.3456f; list[0].round = 0.33333f;
        var parts = WorldRules.ValidateParts(list, out _);
        foreach (var p in parts)
            foreach (float v in new[] { p.x, p.y, p.z, p.sx, p.sy, p.sz })
                Assert.Equal(Math.Round(v, 3), (double)v, 5);
        Assert.Equal(12.3f, parts[1].rz, 4);
        Assert.Equal(0.33f, parts[0].round, 4);
        // an item's shape stays a small part of a snapshot
        var item = new ItemDef { id = "item-x", parts = parts };
        Assert.True(Proto.ToJson(item).Length < 1200, "three parts: " + Proto.ToJson(item).Length + " characters");
    }

    [Fact]
    public void A_part_travels_without_its_zeros_and_arrives_whole()
    {
        var part = Part("cone", 0f, 0.5f, 0f, 0.3f, 1f, 0.3f, "#FFE08A", round: 0.5f, rx: 180f);
        string json = Proto.ToJson(part);
        Assert.DoesNotContain("\"x\"", json); Assert.DoesNotContain("\"z\"", json); Assert.DoesNotContain("\"ry\"", json); Assert.DoesNotContain("\"rz\"", json);
        var back = Proto.FromJson<ShapePart>(json);
        Assert.Equal("cone", back.shape); Assert.Equal("#FFE08A", back.color);
        Assert.Equal(0f, back.x); Assert.Equal(0.5f, back.y); Assert.Equal(0f, back.z);
        Assert.Equal(0.3f, back.sx); Assert.Equal(1f, back.sy); Assert.Equal(0.3f, back.sz);
        Assert.Equal(180f, back.rx); Assert.Equal(0f, back.ry); Assert.Equal(0f, back.rz); Assert.Equal(0.5f, back.round);
        // an item made before things could be sculpted has no parts, and reads as such
        var old = Proto.FromJson<ItemDef>(@"{ ""id"": ""item-old"", ""kind"": ""chair"" }");
        Assert.NotNull(old.parts); Assert.Empty(old.parts); Assert.Equal(0f, old.seat); Assert.Equal(0f, old.top);
    }

    // ------------------------------------------------------------------ what is dropped, cut and clamped
    [Fact]
    public void Parts_that_cannot_be_built_are_dropped()
    {
        var list = Table();
        list.Add(Part("teapot", 10, 6, 3, 1, 1, 1));                    // a shape the headset does not know
        list.Add(Part("box", float.NaN, 6, 3, 1, 1, 1));                // numbers that are none
        list.Add(Part("box", 10, 6, 3, float.PositiveInfinity, 1, 1));
        list.Add(Part("sphere", 10, 6, 3, 1, 1, 1, rx: float.NaN));
        list.Add(null);
        var parts = WorldRules.ValidateParts(list, out float height);
        Assert.Equal(3, parts.Count);
        Assert.Equal(0.6f, height, 3);                                  // and what was dropped did not count for the measure
    }

    [Theory]
    [InlineData("Cube", "box")]
    [InlineData("ball", "sphere")]
    [InlineData("ring", "torus")]
    [InlineData("pill", "capsule")]
    [InlineData("CYLINDER", "cylinder")]
    [InlineData("frustum", "cone")]
    public void Other_words_for_the_same_shape_are_understood(string said, string shape)
    {
        var list = Table(); list[0].shape = said;
        Assert.Equal(shape, WorldRules.ValidateParts(list, out _)[0].shape);
    }

    [Fact]
    public void Fewer_than_three_parts_are_no_sculpture()
    {
        Assert.Null(WorldRules.ValidateParts(null, out _));
        Assert.Null(WorldRules.ValidateParts(new List<ShapePart>(), out _));
        Assert.Null(WorldRules.ValidateParts(Table().Take(2).ToList(), out _));
        var broken = Table(); broken[2].shape = "cloud";
        Assert.Null(WorldRules.ValidateParts(broken, out _));
        // nothing with a size at all
        Assert.Null(WorldRules.ValidateParts(new List<ShapePart> { Part("box", 0, 0, 0, 0, 0, 0), Part("box", 0, 0, 0, 0, 0, 0), Part("box", 0, 0, 0, 0, 0, 0) }, out _));
    }

    [Fact]
    public void At_most_forty_parts_are_kept_the_first_ones()
    {
        var list = new List<ShapePart>();
        for (int i = 0; i < 90; i++) list.Add(Part("box", i * 0.1f, 0.5f, 0, 0.1f, 1f, 0.1f));
        var parts = WorldRules.ValidateParts(list, out _);
        Assert.Equal(ShapePart.Max, parts.Count);
        Assert.Equal(40, ShapePart.Max);
        // the row of forty posts is 4 long: that is the longest side now
        Assert.Equal(1f, parts[39].x - parts[0].x + parts[0].sx, 2);
    }

    [Fact]
    public void Nothing_is_thinner_than_the_ink_line_can_bear()
    {
        var list = Table();
        list.Add(Part("cylinder", 10, 6.3f, 3, 0.001f, 0.2f, 0.001f));      // a hair standing on the table
        list.Add(Part("box", 10, 6.2f, 3, -0.5f, 0.0f, 0.5f));               // a sheet with no thickness, and a negative width
        var parts = WorldRules.ValidateParts(list, out _);
        Assert.Equal(5, parts.Count);
        foreach (var p in parts) { Assert.True(p.sx >= WorldRules.PartThin - 0.001f); Assert.True(p.sy >= WorldRules.PartThin - 0.001f); Assert.True(p.sz >= WorldRules.PartThin - 0.001f); }
        Assert.Equal(0.25f, parts[4].sx, 3);
    }

    [Fact]
    public void Each_shape_keeps_to_its_own_rules()
    {
        var list = Table();
        list[0].round = 7f; list[1].round = -3f;
        list.Add(Part("torus", 10, 6.3f, 3, 1f, 0.9f, 1f, round: 0.5f));       // a ring thicker than its own radius
        list.Add(Part("cone", 10, 6.5f, 3, 0.5f, 0.5f, 0.5f, round: 2f));
        list.Add(Part("sphere", 10, 6.5f, 3, 0.5f, 0.5f, 0.5f, round: 0.7f, rz: 725f));
        var parts = WorldRules.ValidateParts(list, out _);
        Assert.Equal(1f, parts[0].round);
        Assert.Equal(0.1f, parts[1].round, 3);                                  // a toy has no knife edges
        Assert.True(parts[3].sy <= parts[3].sx * 0.5f + 0.001f);
        Assert.Equal(0f, parts[3].round);
        Assert.Equal(0.95f, parts[4].round, 3);                                 // a cone never becomes a cylinder
        Assert.Equal(0f, parts[5].round);
        Assert.Equal(5f, parts[5].rz, 3);                                       // two whole turns and five degrees
    }

    // ------------------------------------------------------------------ colours
    [Theory]
    [InlineData("#F4A9A0", "#F4A9A0")]      // a storybook colour stays what it is
    [InlineData("f4a9a0", "#F4A9A0")]
    [InlineData("sage", "#9BD3A0")]         // a palette colour by its name
    [InlineData(" Wood ", "#E9C99A")]
    [InlineData("#000000", "#4A2F27")]      // black is the ink brown
    [InlineData("#FFFFFF", "#FFFFFF")]
    public void Colours_are_storybook_colours(string given, string got) => Assert.Equal(got, WorldRules.PartColour(given, 0));

    [Fact]
    public void The_palette_is_made_of_storybook_colours()
    {
        foreach (var (name, hex) in WorldRules.Palette) Assert.True(WorldRules.Storybook(hex) == hex, name + " " + hex + " is not left as it is: " + WorldRules.Storybook(hex));
    }

    [Fact]
    public void A_neon_colour_is_taken_back_and_a_missing_one_comes_from_the_palette()
    {
        string red = WorldRules.PartColour("#FF0000", 0);
        Assert.StartsWith("#FF", red);
        Assert.NotEqual("#FF0000", red);
        int green = Convert.ToInt32(red.Substring(3, 2), 16);
        Assert.InRange(green, 0x30, 0x80);                      // still clearly red, no longer pure
        // nothing readable: a soft palette colour, the same one each time for the same part
        string a = WorldRules.PartColour("shiny", 3), b = WorldRules.PartColour(null, 3), other = WorldRules.PartColour("", 4);
        Assert.Equal(a, b); Assert.NotEqual(a, other);
        Assert.Contains(WorldRules.Palette, p => p.hex == a);
        Assert.Matches("^#[0-9A-F]{6}$", WorldRules.PartColour("", 1000));
    }

    // ------------------------------------------------------------------ turned parts are measured as they are turned
    [Fact]
    public void A_rod_laid_down_is_measured_lying()
    {
        // a rod 2 long and 0.2 thick, laid along x (rz = 90), on two small feet
        var list = new List<ShapePart>
        {
            Part("capsule", 0, 0.3f, 0, 0.2f, 2f, 0.2f, rz: 90),
            Part("box", -0.5f, 0.1f, 0, 0.2f, 0.2f, 0.2f),
            Part("box", 0.5f, 0.1f, 0, 0.2f, 0.2f, 0.2f),
        };
        var parts = WorldRules.ValidateParts(list, out float height);
        Assert.Equal(0.2f, height, 3);                  // (0.4 high of 2 long), not the 2 it would be standing up
        Assert.Equal(1f, parts[0].sy, 3);               // its own length is still its sy: it is the part that is turned, not its numbers
        Assert.Equal(90f, parts[0].rz);
        Assert.Equal(0.15f, parts[0].y, 3);
    }

    [Fact]
    public void A_cone_upside_down_still_stands_on_the_ground()
    {
        // its point down, at y = 0; the base ring 1 wide up at y = 1; two balls beside it so that it is a sculpture
        var list = new List<ShapePart>
        {
            Part("cone", 0, 0.5f, 0, 1f, 1f, 1f, rx: 180),
            Part("sphere", 0, 1.0f, 0, 0.4f, 0.4f, 0.4f),
            Part("sphere", 0, 0.5f, 0, 0.2f, 0.2f, 0.2f),
        };
        var parts = WorldRules.ValidateParts(list, out float height);
        Assert.Equal(1f, height, 2);            // the ball on top reaches 1.2: that is the longest side
        Assert.Equal(0.5f / 1.2f, parts[0].y, 2);
        // leaning 45 degrees, a thin post reaches less high and further sideways
        var post = new List<ShapePart> { Part("cylinder", 0, 0, 0, 0.1f, 2f, 0.1f, rz: 45), Part("sphere", 0, 0, 0, 0.3f, 0.3f, 0.3f), Part("sphere", 0, 0, 0, 0.2f, 0.2f, 0.2f) };
        WorldRules.ValidateParts(post, out float leaning);
        Assert.Equal(1f, leaning, 2);           // as high as it is wide
    }

    // ------------------------------------------------------------------ seat and top
    [Fact]
    public void Seat_and_top_follow_the_thing_into_the_common_measure()
    {
        // the table stood with its feet at y = 5 and its top surface at y = 6.2
        float seat = 0f, top = 6.2f;
        WorldRules.ValidateParts(Table(), ref seat, ref top, out float height);
        Assert.Equal(0f, seat);                 // not given stays not given
        Assert.Equal(0.6f, top, 3);
        seat = 99f; top = -4f;                  // nonsense is held inside the thing
        WorldRules.ValidateParts(Table(), ref seat, ref top, out height);
        Assert.Equal(height, seat, 3); Assert.Equal(0f, top);
        seat = float.NaN; top = 5.6f;
        WorldRules.ValidateParts(Table(), ref seat, ref top, out height);
        Assert.Equal(0f, seat); Assert.Equal(0.3f, top, 3);
    }

    // ------------------------------------------------------------------ nothing floats
    [Fact]
    public void A_part_that_touches_nothing_is_found_and_brought_in()
    {
        var list = Table();
        list.Add(Part("sphere", 10f, 7.0f, 3f, 0.3f, 0.3f, 0.3f));       // a ball hanging 0.65 above the table top
        var s = new Sculpture { parts = WorldRules.ValidateParts(list, out float h), height = h };
        s.loose = WorldRules.LooseParts(s.parts);
        Assert.Equal(new[] { 3 }, s.loose);
        Assert.Empty(WorldRules.LooseParts(WorldRules.ValidateParts(Table(), out _)));

        float xBefore = s.parts[3].x;
        Assert.Equal(1, WorldRules.Join(s));
        Assert.Empty(s.loose);
        var ball = s.parts[3]; var top = s.parts[0];
        Assert.Equal(xBefore, ball.x, 2);                                // it came straight down
        float gap = (ball.y - ball.sy / 2) - (top.y + top.sy / 2);
        Assert.InRange(gap, -0.02f, 0.02f);                              // onto the table top, not into the middle of it
        Assert.Equal(1f, Math.Max(s.height, 1f), 3);
    }

    [Fact]
    public void Parts_that_are_turned_towards_each_other_touch()
    {
        // two leaning posts that meet at the top like an A, on a bar: their boxes along the axes would overlap anyway,
        // so the third post is put where only a turned box can tell: beside the A, parallel to one leg, clear of it
        var a = new List<ShapePart>
        {
            Part("box", -0.25f, 0.5f, 0, 0.1f, 1.2f, 0.1f, rz: -30),
            Part("box", 0.25f, 0.5f, 0, 0.1f, 1.2f, 0.1f, rz: 30),
            Part("box", 0, 0.05f, 0, 1.2f, 0.1f, 0.1f),
            Part("box", 0.62f, 0.75f, 0, 0.1f, 0.5f, 0.1f, rz: 30),
        };
        var parts = WorldRules.ValidateParts(a, out _);
        Assert.Equal(new[] { 3 }, WorldRules.LooseParts(parts));
    }

    // ------------------------------------------------------------------ the model's answer
    [Fact]
    public void An_answer_becomes_a_sculpture_or_says_why_not()
    {
        var j = JObject.Parse(@"{ ""plan"": ""a stool"", ""parts"": [
            { ""name"": ""seat"", ""shape"": ""cylinder"", ""x"": 0, ""y"": 0.9, ""z"": 0, ""sx"": 1, ""sy"": 0.2, ""sz"": 1, ""rx"": 0, ""ry"": 0, ""rz"": 0, ""round"": 0.5, ""color"": ""#F4A9A0"" },
            { ""name"": ""leg"", ""shape"": ""capsule"", ""x"": ""-0.3"", ""y"": 0.42, ""z"": 0, ""sx"": 0.12, ""sy"": 0.84, ""sz"": 0.12, ""colour"": ""walnut"" },
            { ""name"": ""leg"", ""shape"": ""capsule"", ""x"": 0.3, ""y"": 0.42, ""z"": 0, ""sx"": 0.12, ""sy"": 0.84, ""sz"": 0.12, ""color"": ""walnut"" },
            { ""name"": ""no place"", ""shape"": ""box"", ""sx"": 0.1, ""sy"": 0.1, ""sz"": 0.1 },
            ""nonsense"" ],
          ""seat"": 1.0, ""top"": 0 }");
        var s = WorldRules.Sculpt(j, out string why);
        Assert.NotNull(s); Assert.Equal("", why);
        Assert.Equal(3, s.parts.Count);                     // the part without a place and the stray text are gone
        Assert.Equal(1f, s.height, 3); Assert.Equal(1f, s.seat, 3); Assert.Equal(0f, s.top);
        Assert.Equal(-0.3f, s.parts[1].x, 3);               // a number written as text is a number
        Assert.Equal("#B98B63", s.parts[1].color);
        Assert.Empty(s.loose);

        Assert.Null(WorldRules.Sculpt(null, out why)); Assert.NotEqual("", why);
        Assert.Null(WorldRules.Sculpt(JObject.Parse(@"{ ""parts"": ""a chair"" }"), out why)); Assert.NotEqual("", why);
        Assert.Null(WorldRules.Sculpt(JObject.Parse(@"{ ""parts"": [ { ""shape"": ""box"", ""x"": 0, ""y"": 0, ""z"": 0, ""sx"": 1, ""sy"": 1, ""sz"": 1 } ] }"), out why));
        Assert.Contains("1 of 1", why);
    }

    [Fact]
    public void The_examples_the_sculptor_is_shown_keep_the_rules_they_teach()
    {
        Assert.True(WorldRules.SculptExamples.Length >= 2);
        foreach (var (what, answer) in WorldRules.SculptExamples)
        {
            var j = JObject.Parse(answer);
            var s = WorldRules.Sculpt(j, out string why);
            Assert.True(s != null, what + ": " + why);
            int n = ((JArray)j["parts"]).Count;
            Assert.Equal(n, s.parts.Count);
            Assert.InRange(n, 6, 28);
            Assert.True(s.loose.Count == 0, what + ": part " + string.Join(", ", s.loose.Select(i => j["parts"][i]["name"])) + " touches nothing");
            // written to the measure already: standing on the ground, 1 high (all three are upright things), centred - the check moved nothing
            Assert.Equal(1f, s.height, 2);
            for (int i = 0; i < n; i++)
            {
                var p = j["parts"][i];
                Assert.Equal((float)p["x"], s.parts[i].x, 2); Assert.Equal((float)p["y"], s.parts[i].y, 2); Assert.Equal((float)p["z"], s.parts[i].z, 2);
                Assert.Equal((float)p["sx"], s.parts[i].sx, 2);
                Assert.Equal((string)p["color"], s.parts[i].color);         // and its colours are storybook colours as they stand
                Assert.True(Math.Min((float)p["sx"], Math.Min((float)p["sy"], (float)p["sz"])) >= 0.04f, what + ": " + p["name"] + " is thinner than the style allows");
            }
            Assert.Contains(s.parts, p => p.y - p.sy / 2 < 0.011f);
            Assert.InRange(s.seat, 0f, s.height); Assert.InRange(s.top, 0f, s.height);
        }
        Assert.True(WorldRules.Sculpt(JObject.Parse(WorldRules.SculptExamples[0].answer), out _).seat > 0.4f, "the chair can be sat on");
    }
}
