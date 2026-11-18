using LivingPartner.Shared;
using MoonSharp.Interpreter;
using Xunit;
using Xunit.Abstractions;

namespace LivingPartner.Tests
{
    /// <summary>
    /// What scripts need to play at the BOUNDARY between the screen and the room, and to look like the app:
    /// things that leave the field and the verdicts that come back (shoot_out / out_events / inside), pointing and
    /// firing (aim / shots / hit_at), the UI kit (panel / button / display / toggle) - and the three examples that
    /// are the user's own requests (a football game through the glass, a shooting game, a calculator), each played
    /// here by a bot that means it. Everything is driven the way a renderer drives a game: hands and verdicts in,
    /// sprites and GlassOuts out.
    /// </summary>
    public class GlassScriptBoundaryTests
    {
        readonly ITestOutputHelper o;
        public GlassScriptBoundaryTests(ITestOutputHelper o) { this.o = o; }

        const float Dt = 1f / 30f;

        static GlassScript Game(string lua, bool touch = false, bool inside = false, bool rounded = false, bool strict = false)
        {
            var g = new GlassScript { touch = touch, inside = inside, Rounded = rounded, Strict = strict };
            g.Start(new GamePackDef { template = "script", script = lua }, 1);
            Assert.True(g.Error == "", g.Error);
            return g;
        }

        static GlassScript Playing(string lua, bool touch = false, bool inside = false, bool rounded = false)
        {
            var g = Game(lua, touch, inside, rounded);
            GlassScriptTests.SkipIntro(g);
            return g;
        }

        static void Step(GlassScript g, GlassInput input = null, int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                g.Step(Dt, input);
                Assert.True(g.Error == "", g.Error);
                GlassScriptTests.AssertSane(g);
            }
        }

        static GlassInput Aim(float x, float y, bool fire = false, int hand = 1)
        {
            var i = new GlassInput();
            i.aimValid[hand] = true; i.aimX[hand] = x; i.aimY[hand] = y; i.fire[hand] = fire;
            return i;
        }

        static string Str(GlassScript g, string name) { return LuaSandbox.Text(g.Box.Get(name)); }
        static double Num(GlassScript g, string name) { return g.Box.Get(name).Number; }

        // ------------------------------------------------------------------ through the glass

        const string OutScript = @"ids = ''  ev = ''  go = false  read = true  art = nil  speed = nil  size = nil  ox = 0.5  oy = 0.5
function update(dt)
  if go then ids = ids .. shoot_out(ox, oy, art, speed, size) .. ' '  go = false end
  if read then for _, e in ipairs(out_events()) do ev = ev .. e.id .. ':' .. e.what .. ' ' end end
  disc(0.5, 0.5, 0.1, 'pink')
end";

        static GlassOut Send(GlassScript g)
        {
            int before = g.outs.Count;
            g.Box.Set("go", true);
            Step(g);
            return g.outs.Count > before ? g.outs[g.outs.Count - 1] : null;
        }

        [Fact]
        public void Inside_says_where_the_game_is_played()
        {
            const string src = "function update(dt) where = inside() end";
            var a = Game(src, inside: true); Step(a);
            var b = Game(src, inside: false); Step(b);
            Assert.True(a.Box.Get("where").Boolean); Assert.False(b.Box.Get("where").Boolean);
            Assert.Equal(DataType.Boolean, b.Box.Get("where").Type);
        }

        [Fact]
        public void Shoot_out_puts_a_GlassOut_in_the_list_with_what_was_asked_for()
        {
            var g = Game(OutScript);
            // before the game begins nothing leaves the field
            Assert.Null(Send(g)); Assert.Equal("0 ", Str(g, "ids")); Assert.Empty(g.outs);
            GlassScriptTests.SkipIntro(g);
            g.Box.Set("ids", "");
            var a = Send(g);
            Assert.NotNull(a);
            Assert.Equal((1, GlassArt.Orb, -1, GlassScript.OutSpeed, GlassScript.OutSize), (a.id, a.art, a.color, a.speed, a.size));
            Assert.Equal(0.5f, a.x, 4); Assert.Equal(0.5f, a.y, 4); Assert.Equal(0, a.runId);       // (the running side fills in the run)
            Assert.Equal(GlassScript.OutSpeed, new GlassOut().speed); Assert.Equal(GlassScript.OutSize, new GlassOut().size);
            // a sticker, a colour (a tinted disc), another word for a sticker, the place and the numbers kept within reason
            g.Box.Set("art", "star"); g.Box.Set("speed", 100); g.Box.Set("size", 0); g.Box.Set("ox", -5); g.Box.Set("oy", 7);
            var b = Send(g);
            Assert.Equal((2, GlassArt.Star, -1, 4f, 0.02f, 0f, 1f), (b.id, b.art, b.color, b.speed, b.size, b.x, b.y));
            g.Box.Set("art", "pink"); g.Box.Set("speed", 0.7); g.Box.Set("size", 0.09); g.Box.Set("ox", 99); g.Box.Set("oy", 0.25);
            var c = Send(g);
            Assert.Equal((3, GlassArt.Disc, 0xF6A5B8, 0.7f, 0.09f, g.W, 0.25f), (c.id, c.art, c.color, c.speed, c.size, c.x, c.y));
            g.Box.Set("art", 0x00FF00); Assert.Equal((GlassArt.Disc, 0x00FF00), (Send(g).art, g.outs[3].color));
            g.Box.Set("art", "football"); Assert.True(GlassArt.IsFootball(Send(g).art));
            g.Box.Set("art", "gold"); Assert.Equal((GlassArt.Gold, -1), (Send(g).art, g.outs[5].color));         // a sticker's own name comes before the colour of that name
            Assert.Equal("1 2 3 4 5 6 ", Str(g, "ids"));
            Assert.Empty(g.Box.Notes);
            Assert.True(g.UsedOuts); Assert.Equal(6, g.OutsSent);
            // something that is neither: an orb, and a note for the writer
            foreach (var x in g.outs.ToArray()) g.OutResult(x.id, "missed");
            g.outs.Clear();
            g.Box.Set("art", "dragon");
            Assert.Equal(GlassArt.Orb, Send(g).art);
            Assert.Contains(g.Box.Notes, n => n.Contains("shoot_out") && n.Contains("dragon"));
        }

        [Fact]
        public void At_most_six_are_on_their_way_and_forty_leave_in_a_minute()
        {
            var g = Playing(OutScript);
            for (int i = 0; i < 8; i++) Send(g);
            Assert.Equal("1 2 3 4 5 6 0 0 ", Str(g, "ids"));
            Assert.Equal(6, g.outs.Count); Assert.Equal(2, g.OutsRefused);
            Assert.Contains(g.Box.Notes, n => n.Contains("shoot_out") && n.Contains("at most 6") && n.Contains("40 in a minute"));
            // one has arrived: there is room for one more
            g.OutResult(3, "blocked");
            Assert.NotNull(Send(g)); Assert.Null(Send(g));
            Assert.Equal("1 2 3 4 5 6 0 0 7 0 ", Str(g, "ids"));
            // forty in a minute, even when each is answered at once
            var h = Playing(OutScript);
            int sent = 0;
            for (int i = 0; i < 60; i++) { var x = Send(h); if (x != null) { sent++; h.OutResult(x.id, "missed"); h.outs.Clear(); } }
            Assert.Equal(GlassScript.MaxOutsMinute, sent);
            for (int i = 0; i < 61 * 4; i++) h.Step(0.25f, null);                // a minute later
            Assert.NotNull(Send(h));
            // a renderer that never takes the list does not make it grow for ever
            var k = Playing(OutScript);
            for (int i = 0; i < 400; i++) { k.Box.Set("go", true); k.Step(0.25f, null); }
            Assert.InRange(k.outs.Count, 1, 16);
        }

        [Fact]
        public void Every_verdict_arrives_exactly_once_and_one_that_never_comes_is_a_miss()
        {
            var g = Playing(OutScript);
            Send(g); Send(g); Send(g);
            g.outs.Clear();                                         // (the renderer took them)
            Step(g, null, 5);
            Assert.Equal("", Str(g, "ev"));
            g.OutResult(2, "blocked");
            g.OutResult(2, "hit");                                  // a second verdict for the same thing does not count
            g.OutResult(99, "hit");                                 // nor one for something that was never sent
            g.OutResult(1, " HIT ");
            Step(g);
            Assert.Equal("2:blocked 1:hit ", Str(g, "ev"));
            Step(g, null, 10);
            Assert.Equal("2:blocked 1:hit ", Str(g, "ev"));        // once
            g.OutResult(2, "hit"); g.OutResult(1, "blocked");
            Step(g, null, 2);
            Assert.Equal("2:blocked 1:hit ", Str(g, "ev"));
            // anything that is not blocked or hit is a miss; null too
            g.OutResult(3, null);
            Step(g);
            Assert.Equal("2:blocked 1:hit 3:missed ", Str(g, "ev"));
            Assert.True(g.ReadOutEvents);

            // nobody answers (no headset, an old renderer): after OutTimeout it counts as missed, so the script never waits for ever
            var h = Playing(OutScript);
            Send(h);
            Step(h, null, (int)(GlassScript.OutTimeout * 30) - 5);
            Assert.Equal("", Str(h, "ev"));
            Step(h, null, 10);
            Assert.Equal("1:missed ", Str(h, "ev"));
            h.OutResult(1, "hit"); Step(h, null, 3);
            Assert.Equal("1:missed ", Str(h, "ev"));

            // a script that does not look in every frame still gets what came meanwhile - once
            var k = Playing(OutScript);
            k.Box.Set("read", false);
            Send(k); Send(k);
            k.OutResult(1, "hit"); Step(k, null, 20); k.OutResult(2, "blocked"); Step(k, null, 20);
            Assert.Equal("", Str(k, "ev"));
            k.Box.Set("read", true);
            Step(k, null, 3);
            Assert.Equal("1:hit 2:blocked ", Str(k, "ev"));

            // a verdict may come from another thread while the game steps
            var t = Playing(OutScript);
            for (int i = 0; i < 6; i++) Send(t);
            var th = new System.Threading.Thread(() => { for (int i = 1; i <= 6; i++) { t.OutResult(i, "blocked"); System.Threading.Thread.Sleep(1); } });
            th.Start();
            for (int i = 0; i < 200 && (th.IsAlive || i < 3); i++) Step(t);
            th.Join(); Step(t, null, 2);
            Assert.Equal(6, Str(t, "ev").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        }

        // ------------------------------------------------------------------ aiming and firing

        const string AimScript = @"n = 0  last = ''  ax = -1  ay = -1  taps_n = 0
function update(dt)
  local x, y = aim()
  ax, ay = x or -1, y or -1
  for _, s in ipairs(shots()) do n = n + 1  last = string.format('%.2f,%.2f', s.x, s.y) end
  if count_taps then taps_n = taps_n + #taps() end
end";

        [Fact]
        public void Aim_is_where_the_player_points_and_a_shot_is_fired_once()
        {
            var g = Game(AimScript);
            // during the intro there are no hands, so nobody points either
            Step(g, Aim(0.7f, 0.6f, true));
            Assert.Equal(-1, Num(g, "ax")); Assert.Equal(0, Num(g, "n"));
            GlassScriptTests.SkipIntro(g);
            Step(g);
            Assert.Equal(-1, Num(g, "ax"));                          // nobody points: nil
            Step(g, Aim(0.7f, 0.6f));
            Assert.Equal(0.7, Num(g, "ax"), 4); Assert.Equal(0.6, Num(g, "ay"), 4); Assert.Equal(0, Num(g, "n"));
            Assert.True(g.ReadAim && g.ReadShots);
            // the trigger: one shot, where it points - also when the Step is long and is cut into pieces
            Step(g, Aim(0.7f, 0.6f, true));
            Assert.Equal(1, Num(g, "n")); Assert.Equal("0.70,0.60", Str(g, "last"));
            g.Step(0.2f, Aim(1.2f, 0.3f, true));
            Assert.Equal(2, Num(g, "n")); Assert.Equal("1.20,0.30", Str(g, "last"));
            // the right hand comes first when both point; each hand's trigger is its own shot
            var both = Aim(0.2f, 0.2f, true, 0);
            both.aimValid[1] = true; both.aimX[1] = 1.5f; both.aimY[1] = 0.9f; both.fire[1] = true;
            Step(g, both);
            Assert.Equal(0.2, Num(g, "ax"), 4); Assert.Equal(4, Num(g, "n"));
            // a trigger without an aim is nothing; nonsense is not an aim
            var none = new GlassInput(); none.fire[0] = true; none.fire[1] = true;
            Step(g, none);
            Assert.Equal(4, Num(g, "n")); Assert.Equal(-1, Num(g, "ax"));
            Step(g, Aim(float.NaN, 0.5f, true)); Step(g, Aim(0.5f, float.PositiveInfinity, true)); Step(g, Aim(50f, 0.5f, true));
            Assert.Equal(4, Num(g, "n")); Assert.Equal(-1, Num(g, "ax"));
        }

        [Fact]
        public void Where_nobody_can_point_a_tap_is_a_shot()
        {
            // on the desk: the fingertip comes down
            var g = Playing(AimScript, touch: true);
            Step(g, GlassScriptTests.Hand(0.4f, 0.5f, 0.06f)); Step(g, GlassScriptTests.Hand(0.4f, 0.5f, 0.004f));
            Assert.Equal(1, Num(g, "n")); Assert.Equal("0.40,0.50", Str(g, "last"));
            // through the glass: a hand that rests - and the ring that closes is shown although the script never asks for taps
            var h = Playing(AimScript);
            bool ring = false;
            for (int i = 0; i < 17; i++) { Step(h, GlassScriptTests.Hand(1.0f, 0.4f, 0.1f)); ring |= h.sprites.Any(s => s.id == GlassScript.DwellId); }
            Assert.Equal(1, Num(h, "n")); Assert.Equal("1.00,0.40", Str(h, "last")); Assert.True(ring);
            // a hand that POINTS fires with its trigger only: resting it is a tap for taps(), but not a second shot
            var k = Playing(AimScript);
            k.Box.Set("count_taps", true);
            var input = GlassScriptTests.Hand(1.0f, 0.4f, 0.1f);
            input.aimValid[0] = true; input.aimX[0] = 0.3f; input.aimY[0] = 0.8f;
            for (int i = 0; i < 20; i++) Step(k, input);
            Assert.Equal(0, Num(k, "n")); Assert.Equal(1, Num(k, "taps_n"));
            input.fire[0] = true; Step(k, input);
            Assert.Equal(1, Num(k, "n")); Assert.Equal("0.30,0.80", Str(k, "last"));
        }

        [Fact]
        public void Hit_at_is_a_distance_test_that_forgives()
        {
            var g = Game(@"function update(dt)
  a = hit_at(0.5, 0.5, 0.55, 0.5, 0.08)
  b = hit_at(0.5, 0.5, 0.9, 0.5, 0.08)
  c = hit_at({x = 0.5, y = 0.5}, 0.52, 0.5, 0.05)
  d = hit_at(0.5, 0.5, 0.5, 0.56)
  e = hit_at()
  f = hit_at('x', nil, {}, 0/0)
  h = hit_at({0.5, 0.5}, 0.5, 0.5)
end");
            Step(g);
            Assert.True(g.Box.Get("a").Boolean); Assert.False(g.Box.Get("b").Boolean); Assert.True(g.Box.Get("c").Boolean);
            Assert.True(g.Box.Get("d").Boolean);                      // r is 0.08 when it is left out
            Assert.False(g.Box.Get("e").Boolean); Assert.False(g.Box.Get("f").Boolean); Assert.True(g.Box.Get("h").Boolean);
        }

        // ------------------------------------------------------------------ the UI kit

        const string Buttons = @"presses = ''
function update(dt)
  if button('A', 0.4, 0.5, 0.2, 0.12) then presses = presses .. 'A' end
  if button('B', 0.7, 0.5, 0.2, 0.12, 'pink') then presses = presses .. 'B' end
end";

        static GlassSprite LabelOf(GlassScript g, string text) { return g.sprites.First(s => s.art == GlassArt.Label && s.text == text); }
        /// <summary>The two boxes under a label: the ink one (the outline and the edge the key stands on) and its face.</summary>
        static (GlassSprite ink, GlassSprite face) Key(GlassScript g, string text)
        {
            int i = g.sprites.FindIndex(s => s.art == GlassArt.Label && s.text == text);
            Assert.True(i >= 2, "no key " + text);
            return (g.sprites[i - 2], g.sprites[i - 1]);
        }

        [Fact]
        public void A_button_is_drawn_in_the_apps_look_from_plain_boxes()
        {
            var g = Playing(Buttons);
            Step(g);
            Assert.Equal(6, g.sprites.Count);
            var (ink, face) = Key(g, "A");
            Assert.Equal((GlassArt.Rect, LuaSandbox.Ink, 1f, 0f), (ink.art, ink.color, ink.alpha, ink.rot));
            Assert.Equal(0.4f, ink.x, 4); Assert.Equal(0.5f, ink.y, 4); Assert.Equal(0.2f, ink.w, 4); Assert.Equal(0.12f, ink.size, 4);
            Assert.Equal((GlassArt.Rect, GlassScript.Key0), (face.art, face.color));
            Assert.Equal(0.2f - 2 * GlassScript.KitLine, face.w, 4); Assert.Equal(0.12f - 2 * GlassScript.KitLine - 0.016f, face.size, 4);
            Assert.Equal(0.508f, face.y, 4);                          // it stands a little high: the ink below it is the edge it will be pushed onto
            var label = LabelOf(g, "A");
            Assert.Equal(LuaSandbox.Ink, label.color); Assert.Equal(face.x, label.x, 4); Assert.Equal(face.y, label.y, 4);
            Assert.InRange(label.size, 0.045f, 0.06f);
            Assert.Equal(0xF6A5B8, Key(g, "B").face.color);
            Assert.True(g.UsedKit && g.ReadTaps);
            // a renderer that can draw rounded paper gets paper, with the corner as a part of the shorter side
            var r = Playing(Buttons, rounded: true);
            Step(r);
            var (ink2, face2) = Key(r, "A");
            Assert.Equal(GlassArt.Paper, ink2.art); Assert.Equal(GlassArt.Paper, face2.art);
            Assert.Equal(0.03f / 0.12f, ink2.fill, 3); Assert.InRange(face2.fill, 0.2f, 0.3f);
            Assert.Equal((ink.x, ink.y, ink.w, ink.size, ink.color), (ink2.x, ink2.y, ink2.w, ink2.size, ink2.color));
            foreach (var s in r.sprites) if (s.art == GlassArt.Paper) { Assert.InRange(s.fill, 0f, 0.5f); Assert.InRange(s.color, 0, 0xFFFFFF); Assert.Equal(0f, s.rot); }
            Assert.DoesNotContain(GlassArt.Paper, GlassArt.All);      // (there is no picture for it: the scene builder asks for one for every name in All)
            // a dark key gets light letters; a label may come last; a number is a label
            var d = Playing("function update(dt) button('Go', 0.4, 0.5, 0.3, 0.12, 'brown') button(0.9, 0.5, 0.3, 0.12, 'Stop') button(7, 1.4, 0.5, 0.2, 0.12) end");
            Step(d);
            Assert.Equal(LuaSandbox.Cream, LabelOf(d, "Go").color);
            Assert.Equal(0.9f, LabelOf(d, "Stop").x, 4);
            Assert.Equal(1.4f, LabelOf(d, "7").x, 4);
            Assert.Empty(d.Box.Notes);
        }

        [Fact]
        public void A_button_is_pressed_by_a_tap_on_the_desk_and_dips()
        {
            var g = Playing(Buttons, touch: true);
            // a hand over it: it lights up and grows a little
            Step(g, GlassScriptTests.Hand(0.7f, 0.5f, 0.06f));
            Assert.Equal("", Str(g, "presses"));
            var (ink, face) = Key(g, "B");
            Assert.Equal(0.208f, ink.w, 4); Assert.NotEqual(0xF6A5B8, face.color);
            Assert.True(((face.color >> 8) & 255) > 0xA5);            // lighter
            Assert.Equal(0.2f, Key(g, "A").ink.w, 4);                 // the other one stays as it is
            // the fingertip comes down: pressed in that frame, once; it dips onto its edge and clicks
            Step(g, GlassScriptTests.Hand(0.7f, 0.5f, 0.004f));
            Assert.Equal("B", Str(g, "presses"));
            Assert.Contains("tap", g.sounds);
            (ink, face) = Key(g, "B");
            Assert.Equal(0.492f, face.y, 4); Assert.Equal(0.2f, ink.w, 4);
            Assert.Equal(face.y, LabelOf(g, "B").y, 4);
            Step(g, GlassScriptTests.Hand(0.7f, 0.5f, 0.004f), 20);
            Assert.Equal("B", Str(g, "presses"));
            Assert.Equal(0.492f, Key(g, "B").face.y, 4);              // down for as long as the finger is
            Step(g, GlassScriptTests.Hand(0.7f, 0.5f, 0.06f), 8);
            Assert.Equal(0.508f, Key(g, "B").face.y, 4);
            // a tap in the gap between the keys presses neither; one on the edge of a key does
            Step(g, GlassScriptTests.Hand(0.55f, 0.5f, 0.004f)); Step(g, GlassScriptTests.Hand(0.55f, 0.5f, 0.06f));
            Assert.Equal("B", Str(g, "presses"));
            Step(g, GlassScriptTests.Hand(0.302f, 0.5f, 0.004f)); Step(g, null, 12);
            Assert.Equal("BA", Str(g, "presses"));
            // during the intro and after the end nothing is pressed (the keys are still drawn)
            var h = Game(Buttons, touch: true);
            Step(h, GlassScriptTests.Hand(0.4f, 0.5f, 0.004f));
            Assert.Equal("", Str(h, "presses")); Assert.Equal(6, h.sprites.Count);
        }

        [Fact]
        public void A_button_is_pressed_through_the_glass_by_resting_on_it_or_by_pointing_and_pinching()
        {
            var g = Playing(Buttons);
            int frames = 0; bool ring = false;
            while (Str(g, "presses") == "" && frames < 40) { Step(g, GlassScriptTests.Hand(0.71f, 0.52f, 0.1f)); frames++; ring |= g.sprites.Any(s => s.id == GlassScript.DwellId); }
            Assert.InRange(frames, 15, 17); Assert.True(ring);
            Assert.Equal("B", Str(g, "presses"));
            Step(g, GlassScriptTests.Hand(0.71f, 0.52f, 0.1f), 60);
            Assert.Equal("B", Str(g, "presses"));                    // once, however long the hand stays
            // pointing at a key lights it up; the pinch presses it
            Step(g, Aim(0.4f, 0.5f));
            Assert.Equal(0.208f, Key(g, "A").ink.w, 4);
            Step(g, Aim(0.4f, 0.5f, true));
            Assert.Equal("BA", Str(g, "presses"));
            Step(g, Aim(0.4f, 0.5f));
            Assert.Equal("BA", Str(g, "presses"));
            // a second press of the same key in the same breath does not count (a tap and a pinch of one gesture)
            Step(g, Aim(0.4f, 0.5f, true));
            Assert.Equal("BA", Str(g, "presses"));
            Step(g, Aim(0.4f, 0.5f), 12); Step(g, Aim(0.4f, 0.5f, true));
            Assert.Equal("BAA", Str(g, "presses"));
        }

        [Fact]
        public void One_tap_presses_one_thing_and_forty_buttons_are_the_most()
        {
            var g = Playing("n = 0\nfunction update(dt)\n  if button('', 0.5, 0.5, 0.3, 0.2) then n = n + 1 end\n  if button('', 0.55, 0.5, 0.3, 0.2) then n = n + 10 end\nend", touch: true);
            Step(g, GlassScriptTests.Hand(0.52f, 0.5f, 0.004f));
            Assert.Equal(1, Num(g, "n"));
            var many = Playing("function update(dt) for i = 1, 45 do button('', 0.1 + (i % 9) * 0.18, 0.1 + math.floor(i / 9) * 0.17, 0.16, 0.14) end end");
            Step(many);
            Assert.Equal(GlassScript.MaxButtons * 2, many.sprites.Count);
            Assert.Contains(many.Box.Notes, n => n.Contains("more than 40 buttons"));
            Assert.Equal(0, many.Dropped);
            // in the check wrong numbers are an error with its line; in a game the button is left out
            var bad = Game("function update(dt)\n  button('A', nil, 0.5, 0.2, 0.1)\nend", strict: true);
            bad.Step(Dt, null);
            Assert.Equal(2, bad.ErrorLine); Assert.Contains("button: x (argument 2) must be a number", bad.Error);
            var soft = Game("function update(dt)\n  r = button('A', nil, 0.5, 0.2, 0.1)\n  display('1', 0.5)\n  panel('x')\n  t = toggle('M')\nend");
            Step(soft);
            Assert.Empty(soft.sprites); Assert.False(soft.Box.Get("r").Boolean); Assert.False(soft.Box.Get("t").Boolean);
        }

        [Fact]
        public void A_display_fits_its_words_and_puts_numbers_on_the_right()
        {
            const float G = GlassScript.GlyphWidth;
            var g = Playing(@"function update(dt)
  display('42', 1.0, 0.8, 0.6, 0.12)
  display('Hello', 1.0, 0.6, 0.6, 0.12)
  display('Hello', 1.0, 0.4, 0.6, 0.12, 'left')
  display('123456789012', 0.3, 0.2, 0.4, 0.12)
  display('123456789012345678', 1.0, 0.2, 0.3, 0.12)
  display(3.5, 1.5, 0.2, 0.3, 0.12, 'center')
end");
            Step(g);
            Assert.Equal(6 * 4, g.sprites.Count);
            // the field: ink, white inside it, a shade under its upper edge
            Assert.Equal((GlassArt.Rect, LuaSandbox.Ink), (g.sprites[0].art, g.sprites[0].color));
            Assert.Equal(0.6f, g.sprites[0].w, 4); Assert.Equal(0.12f, g.sprites[0].size, 4);
            Assert.Equal(0xFFFFFF, g.sprites[1].color); Assert.InRange(g.sprites[2].alpha, 0.05f, 0.2f);
            // a number: as tall as the field allows, its right end a little inside the right edge
            var n = LabelOf(g, "42");
            Assert.Equal(0.072f, n.size, 4);
            Assert.Equal(1.0f + 0.3f - 0.024f, n.x + 2 * G * n.size / 2, 4);
            Assert.Equal(0.8f, n.y, 4);
            // words: in the middle, or where they are told to be
            var w = g.sprites.Where(s => s.text == "Hello").ToArray();
            Assert.Equal(1.0f, w[0].x, 4);
            Assert.Equal(1.0f - 0.3f + 0.024f, w[1].x - 5 * G * w[1].size / 2, 4);
            // many digits: smaller, so that they fit
            var m = LabelOf(g, "123456789012");
            Assert.InRange(m.size, 0.04f, 0.06f);
            Assert.True(12 * G * m.size <= 0.4f - 0.048f + 1e-4f);
            // too many to stay readable: the letters stay readable and the number keeps its END
            var cut = g.sprites.First(s => s.art == GlassArt.Label && s.x < 1.3f && s.x > 0.8f && Math.Abs(s.y - 0.2f) < 0.01f);
            Assert.Equal(0.028f, cut.size, 4);
            Assert.True(cut.text.Length < 18 && "123456789012345678".EndsWith(cut.text), cut.text);
            Assert.True(cut.text.Length * G * cut.size <= 0.3f - 0.048f + 1e-4f);
            Assert.Equal(1.5f, LabelOf(g, "3.5").x, 4);
            Assert.True(g.SmallestText >= 0.028f);
            Assert.Empty(g.Box.Notes);
        }

        [Fact]
        public void A_panel_is_a_card_of_paper_and_a_toggle_turns_over()
        {
            var g = Playing(@"on = false
function update(dt)
  panel(0.9, 0.5, 1.0, 0.9, 'Tools')
  on = toggle('Music', 0.6, 0.3, on)
end", touch: true, rounded: true);
            Step(g);
            // the card: a soft shadow, ink, cream paper inside it, the title on its strip
            Assert.Equal((GlassArt.Paper, LuaSandbox.Ink), (g.sprites[0].art, g.sprites[0].color)); Assert.InRange(g.sprites[0].alpha, 0.1f, 0.3f);
            Assert.Equal((LuaSandbox.Ink, 1f, 1.0f, 0.9f), (g.sprites[1].color, g.sprites[1].alpha, g.sprites[1].w, g.sprites[1].size));
            Assert.Equal(LuaSandbox.Cream, g.sprites[2].color); Assert.Equal(1.0f - 2 * GlassScript.KitLine, g.sprites[2].w, 4);
            Assert.Equal(GlassScript.Strip0, g.sprites[3].color); Assert.Equal(0.5f, g.sprites[3].fill, 3);
            var title = LabelOf(g, "Tools");
            Assert.Equal(g.sprites[3].y, title.y, 4); Assert.True(title.y > 0.8f && title.y + g.sprites[3].size / 2 < 0.95f);
            // the switch: off - grey, the knob on the left; its label to the right of it
            var pill = g.sprites[6]; var knob = g.sprites[8];
            Assert.Equal(GlassScript.ToggleOff, pill.color); Assert.True(knob.x < 0.6f);
            var label = LabelOf(g, "Music");
            Assert.True(label.x - 5 * GlassScript.GlyphWidth * label.size / 2 > 0.6f + 0.065f);
            Assert.False(g.Box.Get("on").Boolean);
            // tapped: on - green, the knob on the right; tapping its label turns it over as well
            Step(g, GlassScriptTests.Hand(0.6f, 0.3f, 0.004f));
            Assert.True(g.Box.Get("on").Boolean);
            Assert.Equal(GlassScript.ToggleOn, g.sprites[6].color); Assert.True(g.sprites[8].x > 0.6f);
            Step(g, null, 12);
            Step(g, GlassScriptTests.Hand(label.x, 0.3f, 0.004f));
            Assert.False(g.Box.Get("on").Boolean);
            // the title may come first, and a panel without one is just the card
            var h = Playing("function update(dt) panel('Notes', 0.9, 0.5, 1.0, 0.9) panel(0.9, 0.5, 0.5, 0.4) panel(0.9, 0.5, 0.5, 0.4, nil, 'mint') end");
            Step(h);
            Assert.Equal(5 + 3 + 3, h.sprites.Count);
            Assert.Equal(1.0f, h.sprites[1].w, 4); Assert.Equal("Notes", h.sprites[4].text);
            Assert.Equal(0x9FD8B4, h.sprites[10].color);
        }

        // ------------------------------------------------------------------ the check

        [Fact]
        public void The_check_plays_through_the_glass_and_reports_it()
        {
            var keeper = GlassScriptExamples.Find("keeper");
            var r = GlassScriptCheck.Run(keeper.lua, GlassScriptExamples.Pack(keeper), 40f, 1);
            o.WriteLine(r.Summary());
            Assert.True(r.Clean, r.Summary());
            Assert.True(r.usedOuts && r.readOutEvents); Assert.False(r.readAim || r.readShots || r.readHands || r.readTaps);
            Assert.InRange(r.outs, 5, 16); Assert.Equal(0, r.outsRefused);
            Assert.True(r.score > 0, "the bot blocks half of them");
            Assert.Contains("shoot_out used (" + r.outs + " sent), out_events() used", r.Summary());
            // with nobody playing everything hits - and still nobody loses within the first 15 seconds
            Assert.Equal(0, r.scoreAlone);
            Assert.True(r.finishedAlone && r.finishedAloneAt > 15f, "alone it ended after " + r.finishedAloneAt);

            var shooter = GlassScriptExamples.Find("shooter");
            var s = GlassScriptCheck.Run(shooter.lua, GlassScriptExamples.Pack(shooter), 40f, 1);
            o.WriteLine(s.Summary());
            Assert.True(s.Clean, s.Summary());
            Assert.True(s.usedOuts && s.readOutEvents && s.readAim && s.readShots);
            Assert.True(s.fired > 20 && s.score > 3, "the bot points at what is drawn and fires: " + s.fired + " shots, score " + s.score);
            Assert.Contains("aim() used, shots() used", s.Summary());
            Assert.True(s.finishedAlone && s.finishedAloneAt > 15f, "alone it ended after " + s.finishedAloneAt);

            // a game that sends things out and never asks what became of them is told so
            var deaf = GlassScriptCheck.Run("local t = 0\nfunction update(dt)\n  t = t + dt\n  if t > 2 then t = 0  shoot_out(W / 2, 0.5) end\n  for _, s in ipairs(shots()) do add(1) end\n  disc(W / 2, 0.5, 0.1 + 0.01 * score(), 'pink')\nend");
            Assert.True(deaf.ok); Assert.False(deaf.Clean);
            Assert.Contains(deaf.warnings, w => w.Contains("shoot_out is called but out_events() is never read"));
            Assert.True(deaf.usedOuts && !deaf.readOutEvents && deaf.readShots && deaf.scoreChanged);
            // one that sends far too many is told that as well
            var flood = GlassScriptCheck.Run("function update(dt)\n  shoot_out(W / 2, 0.5)\n  for _, e in ipairs(out_events()) do add(1) end\n  disc(W / 2, 0.5, 0.1 + 0.001 * score(), 'pink')\nend");
            Assert.Contains(flood.warnings, w => w.Contains("shoot_out: at most 6"));
            Assert.True(flood.outsRefused > 0);
            // and a game whose only input is what comes back from the room is not accused of ignoring the hands
            var back = GlassScriptCheck.Run("local t = 0\nfunction update(dt)\n  t = t + dt\n  if t > 2 then t = 0  shoot_out(W / 2, 0.5) end\n  for _, e in ipairs(out_events()) do if e.what == 'blocked' then add(1) end end\n  disc(W / 2, 0.5, 0.1 + 0.01 * score(), 'pink')\nend");
            Assert.True(back.Clean, back.Summary());
            // a tool made of the kit is clean: the bot presses its keys
            var calc = GlassScriptExamples.Find("calculator");
            var c = GlassScriptCheck.Run(calc.lua, GlassScriptExamples.Pack(calc), 40f, 1);
            o.WriteLine(c.Summary());
            Assert.True(c.Clean, c.Summary());
            Assert.True(c.readTaps && c.reacts && c.pictureChanged && !c.flat);
        }

        // ------------------------------------------------------------------ the examples, played

        static GlassScript StartExample(string id, int seed, bool inside = false, bool touch = false, bool rounded = false)
        {
            var e = GlassScriptExamples.Find(id);
            var g = new GlassScript { inside = inside, touch = touch, Rounded = rounded };
            g.Start(GlassScriptExamples.Pack(e), seed);
            Assert.True(g.Error == "", g.Error);
            return g;
        }

        /// <summary>The room: every GlassOut is taken from the list and answered 0.8 s later with `verdict`.</summary>
        sealed class Room
        {
            public readonly List<GlassOut> all = new List<GlassOut>();
            readonly List<(int id, int due)> waiting = new List<(int, int)>();
            int frame;
            public void After(GlassScript g, string verdict)
            {
                frame++;
                foreach (var x in g.outs) { all.Add(x); waiting.Add((x.id, frame + 24)); }
                g.outs.Clear();
                for (int i = 0; i < waiting.Count; i++)
                    if (waiting[i].due <= frame) { g.OutResult(waiting[i].id, verdict); waiting.RemoveAt(i--); }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Keeper_every_ball_blocked_scores_and_no_ball_blocked_ends_it(bool inside)
        {
            // the keeper who stops everything
            var g = StartExample("keeper", 3, inside);
            Assert.Equal(inside ? "I kick it out of the screen!" : "I kick it up at you!", g.petLine);
            GlassScriptTests.SkipIntro(g);
            var room = new Room();
            bool kicked = false, grew = false; int windUps = 0; float ballBefore = 0f;
            for (int f = 0; f < 60 * 30; f++)
            {
                int had = room.all.Count;
                Step(g);
                // the kick is SEEN coming: Bambi's own motion plays, and the ball grows on its way to the glass before it leaves
                if (g.PetMotion == "kick" && g.petCustom) { if (!kicked) windUps++; kicked = true; } else kicked = false;
                var ball = g.sprites.FirstOrDefault(s => GlassArt.IsFootball(s.art));
                room.After(g, "blocked");
                if (room.all.Count > had)
                {
                    var x = room.all[room.all.Count - 1];
                    Assert.True(GlassArt.IsFootball(x.art)); Assert.InRange(x.x, 0.2f, g.W - 0.2f); Assert.InRange(x.y, 0.3f, 0.9f);
                    Assert.InRange(x.speed, 0.6f, 1.3f); Assert.Equal(0.09f, x.size, 4);
                    Assert.True(ballBefore > 0.15f, "the ball was " + ballBefore + " high just before it left");
                    grew = true;
                }
                ballBefore = GlassArt.IsFootball(ball.art) ? ball.size : 0f;
            }
            o.WriteLine($"inside={inside}: {room.all.Count} balls, score {g.score}, level {g.level}, {windUps} wind-ups");
            Assert.True(grew && windUps >= room.all.Count);
            Assert.InRange(room.all.Count, 10, 30);
            Assert.InRange(g.score, room.all.Count - 1, room.all.Count);        // every ball that has arrived is a save
            Assert.Equal(5, g.lives); Assert.Equal("play", g.phase);
            Assert.True(g.level >= 3 && room.all[room.all.Count - 1].speed > room.all[0].speed, "it gets harder");
            Assert.Empty(g.Box.Notes);

            // the keeper who stops nothing: five goals and it is over - but not within the first 15 seconds
            var h = StartExample("keeper", 3, inside);
            GlassScriptTests.SkipIntro(h);
            var room2 = new Room();
            int lives = h.lives;
            for (int f = 0; f < 90 * 30 && h.phase != "over"; f++)
            {
                Step(h); room2.After(h, "hit");
                if (h.lives < lives) { lives = h.lives; Assert.Contains("error", h.sounds); }
            }
            Assert.Equal("over", h.phase); Assert.Equal("You saved 0 shots!", h.result);
            Assert.Equal(0, h.lives); Assert.Equal(5, room2.all.Count); Assert.Equal(0, h.score);
            Assert.True(h.seconds > 15f, "it was over after " + h.seconds + " s");
            Step(h, null, 30);
            Assert.True(h.sprites.Count > 3);                         // the last picture stays

            // every ball wide: nothing is won and nothing lost
            var w = StartExample("keeper", 3, inside);
            GlassScriptTests.SkipIntro(w);
            var room3 = new Room();
            for (int f = 0; f < 30 * 30; f++) { Step(w); room3.After(w, "missed"); }
            Assert.Equal((0, 5, "play"), (w.score, w.lives, w.phase)); Assert.True(room3.all.Count >= 5);
        }

        static readonly int[] Tints = { 0xFD72A0, 0x64E8F2, 0xBDA1FF, 0xFFD175 };

        /// <summary>The heads of the clones that are fully out: the big tinted discs.</summary>
        static List<GlassSprite> Clones(GlassScript g)
        {
            return g.sprites.Where(s => s.art == GlassArt.Disc && s.size > 0.12f && Array.IndexOf(Tints, s.color) >= 0).ToList();
        }

        [Fact]
        public void Shooter_pointing_and_firing_pops_clones_and_Bambi_shoots_back()
        {
            var g = StartExample("shooter", 2, inside: true);
            GlassScriptTests.SkipIntro(g);
            var room = new Room();
            int seen = 0, shots = 0, hits = 0; bool crosshair = false, charged = false;
            float ax = g.W / 2, ay = 0.5f;
            for (int f = 0; f < 45 * 30; f++)
            {
                var input = Aim(ax, ay);
                var clones = Clones(g);
                seen = Math.Max(seen, clones.Count);
                if (clones.Count > 0 && f % 8 == 0)
                {
                    // point at one and pull the trigger: it is gone in that very frame, and the score went up
                    // (unless it slipped away in that same frame: a clone that is about to go)
                    ax = clones[0].x; ay = clones[0].y;
                    input = Aim(ax, ay, true);
                    int score = g.score;
                    Step(g, input); shots++;
                    Assert.DoesNotContain(Clones(g), s => Math.Abs(s.x - ax) < 0.001f && Math.Abs(s.y - ay) < 0.001f);
                    if (g.sounds.Contains("coin")) { hits++; Assert.InRange(g.score - score, 1, 7); }
                    else Assert.InRange(g.score - score, 0, 2);
                }
                else Step(g, input);
                crosshair |= g.sprites.Any(s => s.art == GlassArt.RingShape && Math.Abs(s.x - ax) < 0.001f && Math.Abs(s.y - ay) < 0.001f && s.color == 0x81FFFF);
                charged |= g.PetMotion == "fling" && g.sprites.Any(s => s.art == GlassArt.Shot);
                room.After(g, "blocked");
            }
            o.WriteLine($"shooter: {shots} shots, {hits} hits, score {g.score}, level {g.level}, {room.all.Count} shots from Bambi");
            Assert.True(shots > 20 && hits >= shots - 3 && g.score > hits && g.level >= 3);
            Assert.True(seen >= 1, "clones were seen"); Assert.True(crosshair, "the crosshair is drawn where the player points"); Assert.True(charged, "Bambi's shot is seen coming");
            Assert.True(room.all.Count >= 8);
            foreach (var x in room.all) { Assert.Equal(GlassArt.Shot, x.art); Assert.Equal(0.7f, x.speed, 4); Assert.InRange(x.x, 0.05f, g.W - 0.05f); }
            Assert.Equal(3, g.lives); Assert.Equal("play", g.phase);        // every shot of Bambi was blocked by the open hand
            Assert.Empty(g.Box.Notes);

            // a shot that lands on nothing pops nothing
            var m = StartExample("shooter", 2);
            GlassScriptTests.SkipIntro(m);
            for (int f = 0; f < 5 * 30; f++) Step(m, Aim(0.02f, 0.02f, f % 5 == 0));
            Assert.Equal(0, m.score);

            // nobody blocks and nobody shoots: three hits end it, not within the first 15 seconds
            var h = StartExample("shooter", 2);
            GlassScriptTests.SkipIntro(h);
            var room2 = new Room();
            for (int f = 0; f < 60 * 30 && h.phase != "over"; f++) { Step(h); room2.After(h, "hit"); }
            Assert.Equal("over", h.phase); Assert.Equal("You popped 0 clones!", h.result);
            Assert.Equal(3, room2.all.Count); Assert.True(h.seconds > 15f, "it was over after " + h.seconds + " s");

            // on the desk, where nobody points: a hand that rests on a clone pops it
            var d = StartExample("shooter", 2);
            GlassScriptTests.SkipIntro(d);
            int popped = 0;
            for (int f = 0; f < 30 * 30 && popped == 0; f++)
            {
                var clones = Clones(d);
                Step(d, clones.Count > 0 ? GlassScriptTests.Hand(clones[0].x, clones[0].y, 0.1f) : null);
                popped = d.score;
            }
            Assert.True(popped > 0);
        }

        // ---- the calculator, driven through its keys

        static (float x, float y) KeyPlace(GlassScript g, string key)
        {
            var s = g.sprites.FirstOrDefault(v => v.art == GlassArt.Label && v.text == key && v.y < 0.7f);
            Assert.True(s.art == GlassArt.Label, "there is no key '" + key + "'");
            return (s.x, s.y);
        }

        /// <summary>What the display shows: the words in the field above the keys.</summary>
        static string Shown(GlassScript g) { return g.sprites.First(s => s.art == GlassArt.Label && s.y > 0.72f && s.y < 0.83f).text; }

        /// <summary>Presses the keys one after the other. how: rest (a hand rests on the key, through the glass) | tap (a fingertip, on a surface) | point (pointing and pinching).</summary>
        static void Press(GlassScript g, string how, params string[] keys)
        {
            foreach (string key in keys)
            {
                var (x, y) = KeyPlace(g, key);
                if (how == "rest") { Step(g, GlassScriptTests.Hand(x, y, 0.1f), 17); Step(g, null, 2); }
                else if (how == "tap") { Step(g, GlassScriptTests.Hand(x, y, 0.06f)); Step(g, GlassScriptTests.Hand(x, y, 0.004f)); Step(g, GlassScriptTests.Hand(x, y, 0.06f)); Step(g, null, 9); }
                else { Step(g, Aim(x, y), 2); Step(g, Aim(x, y, true)); Step(g, Aim(x, y), 10); }
            }
        }

        [Theory]
        [InlineData("rest", false)]
        [InlineData("tap", true)]
        [InlineData("point", false)]
        public void Calculator_does_real_arithmetic_through_its_keys(string how, bool touch)
        {
            var g = StartExample("calculator", 1, inside: how != "tap", touch: touch, rounded: how == "point");
            Assert.False(g.flat);                                      // a tool stands upright
            GlassScriptTests.SkipIntro(g);
            Step(g);
            Assert.Equal("0", Shown(g));
            // it is built from the kit and looks like the app: a card with its title, a display, twenty keys of at least 0.14 x 0.12
            string box = how == "point" ? GlassArt.Paper : GlassArt.Rect;
            Assert.Contains(g.sprites, s => s.art == GlassArt.Label && s.text == "Calculator" && s.y > 0.88f);
            var keys = g.sprites.Where(s => s.art == box && s.color == LuaSandbox.Ink && Math.Abs(s.size - 0.12f) < 0.001f).ToList();
            Assert.Equal(20, keys.Count);
            foreach (var k in keys) { Assert.True(k.w >= 0.14f && k.size >= 0.12f - 1e-4f); Assert.InRange(k.x - k.w / 2, 0.4f, g.W); Assert.InRange(k.x + k.w / 2, 0f, g.W); Assert.InRange(k.y, 0.06f, 0.7f); }
            Assert.True(g.sprites.Where(s => s.art == box).Max(s => s.w * s.size) > 0.9f, "the card fills most of the field");
            Assert.True(g.petX < 0.3f, "Bambi stands beside the keys, not in front of them");
            foreach (var s in g.sprites) if (s.art == GlassArt.Label) Assert.True(s.size >= 0.04f, s.text + " is " + s.size);
            Assert.InRange(g.sprites.Count, 60, 80);

            Press(g, how, "1", "2");
            Assert.Equal("12", Shown(g));
            Press(g, how, "+", "3", "0");
            Assert.Equal("30", Shown(g));
            Press(g, how, "=");
            Assert.Equal("42", Shown(g));
            Assert.Equal("It is 42!", g.petLine);                      // Bambi says the result
            Assert.Equal(1, g.score);
            Press(g, how, "7", "/", "2", "=");
            Assert.Equal("3.5", Shown(g));
            Press(g, how, "C");
            Assert.Equal("0", Shown(g));
            // chained, from left to right; the display shows the sum so far when the next operation is pressed
            Press(g, how, "2", "+", "3", "x");
            Assert.Equal("5", Shown(g));
            Press(g, how, "4", "=");
            Assert.Equal("20", Shown(g));
            // the result is the first number of the next sum
            Press(g, how, "-", "2", "5", "=");
            Assert.Equal("-5", Shown(g));
            // the decimal point, and numbers a computer gets wrong by a hair
            Press(g, how, "C", "0", ".", "1", "+", ".", "2", "=");
            Assert.Equal("0.3", Shown(g));
            Press(g, how, "C", "1", ".", "5", ".", "5", "x", "4", "=");     // (a second point is not taken)
            Assert.Equal("6.2", Shown(g));
            // rub out, change the sign, per cent
            Press(g, how, "C", "1", "2", "3", "<");
            Assert.Equal("12", Shown(g));
            Press(g, how, "+/-");
            Assert.Equal("-12", Shown(g));
            Press(g, how, "<", "<", "<");
            Assert.Equal("0", Shown(g));
            Press(g, how, "5", "0", "%");
            Assert.Equal("0.5", Shown(g));
            // the last operation counts when two are pressed
            Press(g, how, "C", "6", "+", "x", "7", "=");
            Assert.Equal("42", Shown(g));
            // nothing can be divided by zero; the next key starts afresh
            Press(g, how, "C", "5", "/", "0", "=");
            Assert.Equal("Error", Shown(g));
            Assert.Equal("That does not work!", g.petLine);
            Press(g, how, "9");
            Assert.Equal("9", Shown(g));
            // large numbers stay inside the display
            Press(g, how, "C", "9", "9", "9", "9", "9", "9", "x", "9", "9", "9", "9", "9", "9", "=");
            Assert.Equal("999998000001", Shown(g));
            var d = g.sprites.First(s => s.art == GlassArt.Label && s.text == "999998000001");
            Assert.True(d.x + 12 * GlassScript.GlyphWidth * d.size / 2 <= g.W / 2 + 0.22f + 0.51f + 1e-3f && d.size >= 0.04f);
            Assert.Empty(g.Box.Notes); Assert.Equal(0, g.Dropped);
        }
    }
}
