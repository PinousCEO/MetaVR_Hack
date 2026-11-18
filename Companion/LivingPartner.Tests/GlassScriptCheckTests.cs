using System.Diagnostics;
using LivingPartner.Shared;
using Xunit;
using Xunit.Abstractions;

namespace LivingPartner.Tests
{
    /// <summary>
    /// GlassScriptCheck: the proof the host asks for before it accepts a script the model wrote. Scripts that are
    /// bad in one particular way each must be told so, in words the model can act on; good ones must come out clean.
    /// </summary>
    public class GlassScriptCheckTests
    {
        readonly ITestOutputHelper o;
        public GlassScriptCheckTests(ITestOutputHelper o) { this.o = o; }

        const string Good = @"
local dots = {}
function start() for i = 1, 6 do dots[i] = {x = rnd(0.2, W - 0.2), y = rnd(0.2, 0.8)} end end
function update(dt)
  rect(W / 2, 0.5, W, 1, 'cream')
  for _, t in ipairs(taps()) do
    for i = #dots, 1, -1 do
      if dist(t.x, t.y, dots[i].x, dots[i].y) < 0.08 then table.remove(dots, i) add(1) sound('pop') end
    end
  end
  for _, d in ipairs(dots) do disc(d.x, d.y, 0.05, 'pink') end
  text('Tap the dots', W / 2, 0.92, 0.06)
  if #dots == 0 then finish('All gone!') end
end";

        [Fact]
        public void A_good_script_is_clean_and_the_facts_are_right()
        {
            var r = GlassScriptCheck.Run(Good, null, 40f, 3);
            o.WriteLine(r.Summary());
            Assert.True(r.ok); Assert.True(r.Clean, string.Join(" | ", r.warnings));
            Assert.Equal("", r.error); Assert.Equal(0, r.errorLine);
            Assert.True(r.drew && r.pictureChanged && r.reacts && r.scoreChanged && r.readTaps);
            Assert.False(r.readHands); Assert.False(r.readHeld); Assert.False(r.flat);
            Assert.True(r.score > 0); Assert.Equal(0, r.scoreAlone);
            Assert.False(r.finishedAlone);
            Assert.InRange(r.maxSprites, 3, 8);
            Assert.True(r.frames > 100 && r.taps > 3);
            Assert.InRange(r.peakInstructions, 100, 5000); Assert.InRange(r.averageInstructions, 50, 5000);
            Assert.Equal(LuaSandbox.DefaultBudget, r.budget);
            Assert.Equal(0.06f, r.smallestText, 4);
            Assert.Contains("It ran without an error.", r.Summary());
            // the check is itself deterministic
            var again = GlassScriptCheck.Run(Good, null, 40f, 3);
            Assert.Equal((r.frames, r.score, r.taps, r.peakInstructions, r.finished, r.finishedAt), (again.frames, again.score, again.taps, again.peakInstructions, again.finished, again.finishedAt));
        }

        [Theory]
        [InlineData("nothing is drawn", "function update(dt) local t = taps() if #t > 0 then add(1) end end")]
        [InlineData("the hands are never read", "local x = 0\nfunction update(dt) x = (x + dt * 0.3) % W  disc(x, 0.5, 0.05, 'pink') end")]
        [InlineData("nothing changes when the hands play", "local x = 0\nfunction update(dt) local h = hands() local t = taps() x = (x + dt * 0.3) % W  disc(x, 0.5, 0.05, 'pink') end")]
        [InlineData("the picture never changes", "function update(dt) local h = hands() rect(W / 2, 0.5, 0.5, 0.5, 'pink') text('Hello', W / 2, 0.5, 0.06) end")]
        [InlineData("ends within 2 seconds", "function update(dt) for _, t in ipairs(taps()) do add(1) end disc(W / 2, 0.5, 0.1 + 0.01 * score(), 'pink') if time() > 0.5 then finish('Over') end end")]
        [InlineData("draws outside the field", "function update(dt) for _, t in ipairs(taps()) do add(1) end rect(400, 300, 100 + score(), 50, 'pink') disc(640, 360, 20, 'red') text('Score', 100, 40, 0.06) end")]
        [InlineData("text too small", "function update(dt) for _, t in ipairs(taps()) do add(1) end text('score ' .. score(), W / 2, 0.5, 0.012) end")]
        [InlineData("more than 160 things", "function update(dt) for _, t in ipairs(taps()) do add(1) end for i = 1, 300 do disc(rnd(0, W), rnd(), 0.01 + score() * 0.001, 'pink') end end")]
        [InlineData("there is no picture 'dragon'", "function update(dt) for _, t in ipairs(taps()) do add(1) end pic('dragon', W / 2, 0.5, 0.2 + 0.01 * score()) end")]
        [InlineData("unknown colour 'turquoise'", "function update(dt) for _, t in ipairs(taps()) do add(1) end disc(W / 2, 0.5, 0.1 + 0.01 * score(), 'turquoise') end")]
        [InlineData("there is no sound 'explosion'", "function update(dt) for _, t in ipairs(taps()) do add(1) sound('explosion') end disc(W / 2, 0.5, 0.1 + 0.01 * score(), 'pink') end")]
        [InlineData("there is no pose 'breakdance'", "function update(dt) for _, t in ipairs(taps()) do add(1) pet_pose('breakdance') end disc(W / 2, 0.5, 0.1 + 0.01 * score(), 'pink') end")]
        [InlineData("update is heavy", "function update(dt) for _, t in ipairs(taps()) do add(1) end local s = 0 for i = 1, 4000 do s = s + math.sin(i) end disc(W / 2, 0.5, 0.1 + 0.01 * score(), 'pink') end")]
        [InlineData("flat() only works", "function update(dt) flat(true) for _, t in ipairs(taps()) do add(1) end disc(W / 2, 0.5, 0.1 + 0.01 * score(), 'pink') end")]
        public void A_bad_script_gets_the_warning_it_deserves(string warning, string lua)
        {
            var r = GlassScriptCheck.Run(lua);
            o.WriteLine(r.Summary());
            Assert.True(r.ok, r.error);
            Assert.False(r.Clean);
            Assert.Contains(r.warnings, w => w.Contains(warning));
            Assert.Contains("WARNING: ", r.Summary());
            Assert.Contains(warning, r.Summary());
            // and only that one: a warning must not drag others along that are not true
            Assert.True(r.warnings.Count <= 2, string.Join(" | ", r.warnings));
        }

        [Fact]
        public void One_heavy_frame_is_warned_about_as_well()
        {
            var r = GlassScriptCheck.Run("local done = false\nfunction update(dt)\n  for _, t in ipairs(taps()) do add(1) end\n  if time() > 1 and not done then done = true  local s = 0  for i = 1, 60000 do s = s + i end end\n  disc(W / 2, 0.5, 0.1 + 0.01 * score(), 'pink')\nend");
            Assert.True(r.ok, r.error);
            Assert.Contains(r.warnings, w => w.Contains("one frame needed"));
            Assert.True(r.peakShare > GlassScriptCheck.HeavyPeak);
        }

        [Fact]
        public void Errors_come_with_the_line_its_text_and_when_it_happened()
        {
            // does not compile
            var r = GlassScriptCheck.Run("function update(dt)\n  local a = = 3\nend");
            Assert.False(r.ok); Assert.False(r.Clean);
            Assert.Equal(2, r.errorLine); Assert.Equal("local a = = 3", r.errorSource); Assert.Equal("", r.errorIn);
            Assert.Contains("ERROR: line 2: ", r.Summary());
            Assert.Contains("the line is:  local a = = 3", r.Summary());
            Assert.Equal(0, r.frames);

            // breaks at the top level
            r = GlassScriptCheck.Run("local t = nil\nlocal x = t.field\nfunction update(dt) end");
            Assert.False(r.ok); Assert.Equal(2, r.errorLine);

            // breaks only when it is played: a tap runs a line with a mistake (found because the bot taps)
            r = GlassScriptCheck.Run("function update(dt)\n  rect(W / 2, 0.5, 0.5, 0.5, 'pink')\n  for _, t in ipairs(taps()) do\n    score(score() + points)\n  end\nend");
            Assert.False(r.ok);
            Assert.Equal(4, r.errorLine); Assert.Equal("score(score() + points)", r.errorSource); Assert.Equal("hands", r.errorIn);
            Assert.Contains("while it was played", r.Summary());
            Assert.True(r.frames > 30);

            // breaks only when NOBODY plays
            r = GlassScriptCheck.Run("local idle = 0\nfunction update(dt)\n  rect(W / 2, 0.5, 0.5, 0.5, 'pink')\n  if #hands() > 0 then idle = 0 else idle = idle + dt end\n  if idle > 5 then nobody_home() end\nend");
            Assert.False(r.ok);
            Assert.Equal(5, r.errorLine); Assert.Equal("alone", r.errorIn);
            Assert.Contains("while nobody was playing", r.Summary());

            // a loop that never ends, some seconds into the game: stopped, and the line it was on is named
            var sw = Stopwatch.StartNew();
            r = GlassScriptCheck.Run("function update(dt)\n  rect(W / 2, 0.5, 0.5, 0.5, 'pink')\n  local n = 0\n  while time() > 2 do\n    n = n + 1\n  end\nend");
            Assert.False(r.ok);
            Assert.Contains("took too long", r.error);
            Assert.InRange(r.errorLine, 4, 5);
            Assert.True(sw.ElapsedMilliseconds < 3000);

            // wrong arguments are errors in the check (in a game they are forgiven)
            r = GlassScriptCheck.Run("function update(dt)\n  local hs = hands()\n  disc(hs.x, hs.y, 0.05, 'pink')\nend");
            Assert.False(r.ok);
            Assert.Equal(3, r.errorLine);
            Assert.Contains("disc: x (argument 1) must be a number, it is nil", r.error);

            // a motion that breaks is found although the game never plays it
            r = GlassScriptCheck.Run("motion('bad', 1, function(u, p)\n  p.armR = {x = missing.value}\nend)\nfunction update(dt)\n  for _, t in ipairs(taps()) do add(1) end\n  disc(W / 2, 0.5, 0.1 + 0.01 * score(), 'pink')\nend");
            Assert.False(r.ok);
            Assert.Equal(2, r.errorLine);
            Assert.Equal(1, r.motions);

            // nothing at all
            Assert.False(GlassScriptCheck.Run(null).ok);
            Assert.False(GlassScriptCheck.Run("").ok);
            Assert.Contains("no function update", GlassScriptCheck.Run("x = 1").error);
        }

        [Fact]
        public void The_pack_is_used_but_not_changed()
        {
            var pack = new GamePackDef { id = "p1", title = "Fish", howTo = "Tap the fish", lives = 5, script = "this is not even Lua" };
            pack.art.Add(new PackArt { name = "fish", file = "fish.png" });
            var r = GlassScriptCheck.Run("function update(dt)\n  for _, t in ipairs(taps()) do add(1) end\n  pic('fish', W / 2, 0.5, 0.2 + 0.01 * score())\n  if lives() ~= 5 then error('lives ' .. lives()) end\nend", pack);
            Assert.True(r.Clean, r.Summary());
            Assert.Equal("this is not even Lua", pack.script);
            Assert.Equal("p1", pack.id);
        }

        [Fact]
        public void Forty_seconds_take_well_under_a_second()
        {
            foreach (var e in GlassScriptExamples.All) GlassScriptCheck.Run(e.lua, GlassScriptExamples.Pack(e), 5f);      // (warm)
            double worst = 0;
            foreach (var e in GlassScriptExamples.All)
            {
                var r = GlassScriptCheck.Run(e.lua, GlassScriptExamples.Pack(e), 40f, 1);
                o.WriteLine($"{e.id}: {r.realSeconds * 1000:0} ms for {r.frames} frames with a player (and as many without), {r.averageInstructions} steps a frame, most {r.peakInstructions}");
                worst = Math.Max(worst, r.realSeconds);
            }
            Assert.True(worst < 1.0, "the slowest check took " + worst + " s");
        }
    }
}
