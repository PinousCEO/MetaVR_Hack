using System.Diagnostics;
using System.Text.RegularExpressions;
using LivingPartner.Shared;
using Xunit;
using Xunit.Abstractions;

namespace LivingPartner.Tests
{
    /// <summary>
    /// The script runtime (LuaSandbox + GlassScript): this is what runs programs that a language model wrote - or that
    /// came from somebody else through the marketplace - on the user's PC and headset. So: the sandbox holds, nothing
    /// hangs, nothing throws out, wrong arguments are forgiven or explained, and the same seed gives the same game.
    /// </summary>
    public class GlassScriptTests
    {
        readonly ITestOutputHelper o;
        public GlassScriptTests(ITestOutputHelper o) { this.o = o; }

        internal static GlassScript Game(string lua, int seed = 1, bool strict = false, bool touch = false, string howTo = "")
        {
            var g = new GlassScript { Strict = strict, touch = touch };
            g.Start(new GamePackDef { template = "script", script = lua, howTo = howTo }, seed);
            return g;
        }

        /// <summary>Steps past the intro (no hands).</summary>
        internal static void SkipIntro(GlassScript g)
        {
            for (int i = 0; i < 40 && g.phase == "intro"; i++) g.Step(1f / 30f, null);
        }

        internal static GlassInput Hand(float x, float y, float dist = 0.1f)
        {
            var i = new GlassInput();
            i.valid[0] = true; i.x[0] = x; i.y[0] = y; i.dist[0] = dist;
            return i;
        }

        internal static void AssertSane(GlassScript g)
        {
            Assert.True(g.sprites.Count <= GlassScript.MaxSprites, "sprites " + g.sprites.Count);
            int labels = 0;
            foreach (var s in g.sprites)
            {
                Assert.False(float.IsNaN(s.x) || float.IsNaN(s.y) || float.IsNaN(s.size) || float.IsNaN(s.w) || float.IsNaN(s.rot) || float.IsNaN(s.alpha) || float.IsNaN(s.fill), "NaN in a sprite");
                Assert.False(float.IsInfinity(s.x) || float.IsInfinity(s.y) || float.IsInfinity(s.size) || float.IsInfinity(s.w) || float.IsInfinity(s.rot));
                Assert.InRange(s.x, -100f, 100f); Assert.InRange(s.y, -100f, 100f);
                Assert.True(s.size > 0f && s.size <= 4f && s.w >= 0f && s.w <= 4f);
                Assert.InRange(s.alpha, 0.015f, 1f);
                Assert.NotNull(s.art); Assert.NotNull(s.text); Assert.NotNull(s.text2);
                Assert.True(Array.IndexOf(GlassArt.All, s.art) >= 0 || s.art == GlassArt.Paper || GlassArt.IsFootball(s.art) || s.art.StartsWith(GlassArt.PackPrefix), "art " + s.art);
                Assert.True(s.text.Length <= LuaSandbox.MaxText && s.text2.Length <= LuaSandbox.MaxText);
                if (s.art == GlassArt.Label || s.art == GlassArt.Card) labels++;
                if (s.art == GlassArt.Rect || s.art == GlassArt.Disc || s.art == GlassArt.RingShape || s.art == GlassArt.Label || s.art == GlassArt.Paper) Assert.InRange(s.color, 0, 0xFFFFFF);
                if (s.art == GlassArt.Paper) { Assert.InRange(s.fill, 0f, 0.5f); Assert.True(s.w > 0f && s.rot == 0f); }
            }
            Assert.True(labels <= GlassScript.MaxLabels);
            Assert.False(float.IsNaN(g.petX) || float.IsNaN(g.petY));
            Assert.InRange(g.petX, 0f, g.W); Assert.InRange(g.petY, 0f, 1f);
            Assert.NotNull(g.petPose); Assert.NotNull(g.banner); Assert.NotNull(g.result); Assert.NotNull(g.petLine);
            foreach (string s in g.sounds) Assert.Contains(s, SfxSynth.Names);
            Assert.True(g.sounds.Count <= 6);
        }

        // ------------------------------------------------------------------ the sandbox

        [Fact]
        public void The_sandbox_has_exactly_the_allowed_library()
        {
            var box = new LuaSandbox(1);
            var names = box.FunctionNames();
            string[] expect =
            {
                "assert", "clamp", "collectgarbage", "dist", "error", "ipairs", "lerp", "next", "pairs", "print", "rnd", "rndi", "select",
                "string.byte", "string.char", "string.find", "string.format", "string.len", "string.lower", "string.rep", "string.reverse", "string.sub", "string.upper",
                "table.concat", "table.insert", "table.pack", "table.remove", "table.sort", "table.unpack", "tonumber", "tostring", "type", "unpack",
            };
            var notMath = names.Where(n => !n.StartsWith("math.")).ToArray();
            Assert.Equal(expect.OrderBy(s => s, StringComparer.Ordinal), notMath);
            foreach (string n in names.Where(n => n.StartsWith("math.")))
                Assert.Contains(n.Substring(5), new[] { "abs", "acos", "asin", "atan", "atan2", "ceil", "cos", "cosh", "deg", "exp", "floor", "fmod", "frexp", "ldexp", "log", "max", "min", "modf", "pow", "rad", "random", "randomseed", "sin", "sinh", "sqrt", "tan", "tanh" });
            // everything that reaches outside, or that could run code the budget cannot stop, is simply not there
            Assert.True(box.Load(@"
gone = {}
for _, n in ipairs({'io', 'os', 'require', 'load', 'loadstring', 'loadfile', 'dofile', 'debug', 'package', 'coroutine', 'setmetatable', 'getmetatable',
                    'pcall', 'xpcall', 'rawset', 'rawget', 'bit32', 'module', '_MOONSHARP', 'dynamic', 'json', 'pack'}) do
  gone[#gone + 1] = n .. '=' .. type(_G[n])
end
s = table.concat(gone, ' ')
str = type(string.gsub) .. type(string.gmatch) .. type(string.match) .. type(string.dump) .. type(('x').gsub) .. type(('x').match)
"), box.ErrorText());
            foreach (string entry in box.Get("s").String.Split(' ')) Assert.EndsWith("=nil", entry);
            Assert.Equal("nilnilnilnilnilnil", box.Get("str").String);
        }

        [Theory]
        [InlineData("io.write('x')", "io")]
        [InlineData("local f = io.open('C:/x.txt', 'w')", "io")]
        [InlineData("os.execute('calc')", "os")]
        [InlineData("x = os.time()", "os")]
        [InlineData("os.exit()", "os")]
        [InlineData("require('socket')", "require")]
        [InlineData("load('x = 1')()", "load")]
        [InlineData("loadstring('x = 1')()", "loadstring")]
        [InlineData("dofile('C:/x.lua')", "dofile")]
        [InlineData("debug.traceback()", "debug")]
        [InlineData("setmetatable({}, {})", "setmetatable")]
        [InlineData("coroutine.wrap(function() end)()", "coroutine")]
        [InlineData("pcall(function() end)", "pcall")]
        [InlineData("x = ('abc'):gsub('a', 'b')", "gsub")]
        [InlineData("x = string.match('abc', 'a')", "match")]
        public void What_reaches_outside_is_an_error_with_a_hint(string src, string name)
        {
            var g = Game("-- first line\n" + src + "\nfunction update(dt) end");
            Assert.Equal("over", g.phase);
            Assert.Equal(GlassScript.BrokeLine, g.result);
            Assert.Equal(2, g.ErrorLine);
            Assert.Contains("nil", g.Error);
            Assert.Contains("`" + name + "` does not exist here", g.Error);
            Assert.Equal(src, g.ErrorSource);
            g.Step(0.033f, null);       // and it stays quietly over
            Assert.Equal("over", g.phase);
        }

        [Fact]
        public void Collectgarbage_and_print_do_nothing_harmful()
        {
            var lines = new List<string>();
            var box = new LuaSandbox(1) { Log = lines.Add };
            long before = GC.CollectionCount(2);
            var sw = Stopwatch.StartNew();
            Assert.True(box.Load("for i = 1, 20000 do collectgarbage() collectgarbage('collect') end n = collectgarbage('count')\nfor i = 1, 1000 do print('line', i, nil, true, {}) end print(string.rep('x', 5000))"), box.ErrorText());
            Assert.True(sw.ElapsedMilliseconds < 2000);
            Assert.True(GC.CollectionCount(2) - before < 50, "collectgarbage must not force collections");
            Assert.Equal(0, box.Get("n").Number);
            Assert.Equal(LuaSandbox.MaxPrints, lines.Count);
            Assert.Equal("line 1 nil true table", lines[0]);
            Assert.All(lines, l => Assert.True(l.Length <= 400));
        }

        [Theory]
        [InlineData("while true do end\nfunction update(dt) end", 1)]
        [InlineData("function start()\n  while true do end\nend\nfunction update(dt) end", 0)]
        [InlineData("function update(dt)\n  local i = 0\n  while true do i = i + 1 end\nend", 0)]
        [InlineData("function update(dt)\n  repeat until false\nend", 0)]
        [InlineData("local function f() return f() end\nfunction update(dt) f() end", 0)]
        [InlineData("function update(dt)\n  table.sort({3, 2, 1}, function(a, b) while true do end end)\nend", 0)]
        [InlineData("function update(dt)\n  for i = 1, 1e12 do end\nend", 0)]
        public void An_endless_loop_is_stopped_and_the_game_is_over(string src, int unused)
        {
            var sw = Stopwatch.StartNew();
            var g = Game(src);
            for (int i = 0; i < 5 && g.Error.Length == 0; i++) g.Step(1f / 30f, Hand(0.5f, 0.5f));
            long first = sw.ElapsedMilliseconds;
            Assert.True(g.Error.Contains("took too long") || g.Error.Contains("too much memory"), g.Error);      // (a call that never returns fills the memory first)
            Assert.Equal("over", g.phase);
            Assert.Equal(GlassScript.BrokeLine, g.result);
            Assert.True(g.Box.LastInstructions <= g.Box.Budget + LuaSandbox.Slice);
            sw.Restart();
            for (int i = 0; i < 300; i++) g.Step(1f / 30f, Hand(0.5f, 0.5f));      // the NEXT calls do not hang either: nothing runs any more
            Assert.True(sw.ElapsedMilliseconds < 200, "after: " + sw.ElapsedMilliseconds);
            Assert.True(first < 3000, "first: " + first);
            Assert.Equal("over", g.phase);
            _ = unused;
        }

        [Fact]
        public void Deep_recursion_is_an_error_not_a_crash()
        {
            var g = Game("local function f(n) return 1 + f(n + 1) end\nfunction update(dt)\n  f(1)\nend");
            g.Step(0.033f, null);
            Assert.Equal("over", g.phase);
            Assert.True(g.Error.Contains("stack overflow") || g.Error.Contains("took too long"), g.Error);
            // a recursion that ends is fine
            var h = Game("local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nfunction update(dt) score(fib(15)) end");
            h.Step(0.033f, null);
            Assert.Equal("", h.Error);
            Assert.Equal(610, h.score);
        }

        [Fact]
        public void A_script_that_throws_or_does_not_compile_says_where()
        {
            var g = Game("local x = 1\nfunction update(dt)\n  if time() >= 0 then\n    error('the cat sat on it')\n  end\nend");
            Assert.Equal("", g.Error);
            g.Step(0.033f, null);
            Assert.Equal(4, g.ErrorLine);
            Assert.Contains("the cat sat on it", g.Error);
            Assert.StartsWith("line 4: ", g.Error);
            Assert.Equal("error('the cat sat on it')", g.ErrorSource);
            Assert.Equal("over", g.phase); Assert.Equal(GlassScript.BrokeLine, g.result);

            var s = Game("function update(dt)\n  local a = 1\n  if a == 1 the\n    rect(0, 0, 1, 1)\n  end\nend");
            Assert.Equal(3, s.ErrorLine);
            Assert.Equal("if a == 1 the", s.ErrorSource);
            Assert.Equal("over", s.phase);

            var n = Game("function update(dt)\n  local t = nil\n  t.x = 5\nend");
            n.Step(0.033f, null);
            Assert.Equal(3, n.ErrorLine);
            Assert.Contains("nil", n.Error);

            var c = Game("function update(dt)\n  undefined_function(3)\nend");
            c.Step(0.033f, null);
            Assert.Equal(2, c.ErrorLine);

            // no update at all, an empty script, no pack, a script that is far too long
            Assert.Contains("no function update", Game("x = 1").Error);
            Assert.Contains("empty", Game("   \n ").Error);
            var none = new GlassScript();
            none.Start(null, 1);
            Assert.Equal("over", none.phase); Assert.Contains("empty", none.Error);
            var never = new GlassScript();
            never.Step(0.033f, null);                   // stepped before it was started
            Assert.Equal("over", never.phase);
            Assert.Contains("too long", Game("function update(dt) end\n" + new string('-', LuaSandbox.MaxSource)).Error);
        }

        [Fact]
        public void Source_that_would_overflow_the_parser_is_refused_before_it_is_parsed()
        {
            // (a real stack overflow cannot be caught: these must never reach the parser)
            string deepParens = "x = " + new string('(', 20000) + "1" + new string(')', 20000) + "\nfunction update(dt) end";
            string deepTables = "x = " + string.Concat(Enumerable.Repeat("{", 5000)) + string.Concat(Enumerable.Repeat("}", 5000)) + "\nfunction update(dt) end";
            string deepBlocks = string.Concat(Enumerable.Repeat("do ", 5000)) + string.Concat(Enumerable.Repeat("end ", 5000)) + "\nfunction update(dt) end";
            string deepFns = "x = " + string.Concat(Enumerable.Repeat("function() return ", 3000)) + "1" + string.Concat(Enumerable.Repeat(" end", 3000)) + "\nfunction update(dt) end";
            string longChain = "x = 1" + string.Concat(Enumerable.Repeat(" + 1", 5000)) + "\nfunction update(dt) end";
            string longConcat = "x = 'a'" + string.Concat(Enumerable.Repeat(" .. 'a'", 3000)) + "\nfunction update(dt) end";
            string manyNots = "x = " + string.Concat(Enumerable.Repeat("not ", 5000)) + "true\nfunction update(dt) end";
            string manyMinus = "x = " + string.Concat(Enumerable.Repeat("- ", 5000)) + "1\nfunction update(dt) end";
            foreach (string src in new[] { deepParens, deepTables, deepBlocks, deepFns, longChain, longConcat, manyNots, manyMinus })
            {
                Assert.NotNull(LuaGuard.TooDeep(src));
                var g = Game(src);
                Assert.Equal("over", g.phase);
                Assert.NotEqual("", g.Error);
            }
            // ordinary code is not refused: every example, a big table of numbers, long comments and strings full of brackets
            foreach (var e in GlassScriptExamples.All) Assert.Null(LuaGuard.TooDeep(e.lua));
            string table = "local t = {" + string.Join(", ", Enumerable.Range(0, 800).Select(i => "-" + i + " + 1")) + "}\n" +
                           "-- ((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((\n" +
                           "local s = '((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((('\n" +
                           "--[[ " + new string('{', 500) + " ]]\nlocal u = [[ " + new string('(', 500) + " ]]\n" +
                           "function update(dt) score(#t) end";
            Assert.Null(LuaGuard.TooDeep(table));
            var ok = Game(table);
            ok.Step(0.033f, null);
            Assert.Equal("", ok.Error);
            Assert.Equal(800, ok.score);
        }

        [Fact]
        public void Memory_and_slow_instructions_are_guarded()
        {
            // a string that doubles for ever
            var sw = Stopwatch.StartNew();
            var g = Game("function update(dt)\n  local s = 'x'\n  while true do s = s .. s end\nend");
            g.Step(0.033f, null);
            Assert.True(g.Error.Contains("too much memory") || g.Error.Contains("took too long"), g.Error);
            Assert.Equal("over", g.phase);
            // a table that grows for ever (its length is slow to take: the clock stops it, not only the count)
            var t = Game("function update(dt)\n  local t = {}\n  while true do t[#t + 1] = {} end\nend");
            t.Step(0.033f, null);
            Assert.True(t.Error.Contains("took too long") || t.Error.Contains("too much memory"), t.Error);
            Assert.True(sw.ElapsedMilliseconds < 6000, sw.ElapsedMilliseconds + " ms");
            // the library's own doors to huge strings are shut
            Assert.Contains("string.rep", Game("s = string.rep('abc', 1e8)\nfunction update(dt) end").Error);
            Assert.Contains("string.format", Game("s = string.format('%99999d', 1)\nfunction update(dt) end").Error);
            Assert.Equal("", Game("s = string.format('%5.2f %02d:%02d %s %d%%', 3.14159, 4, 7, 'ok', 50)\nfunction update(dt) end").Error);
        }

        // ------------------------------------------------------------------ wrong arguments

        static readonly string[] BadArgs =
        {
            "", "nil", "'a'", "'a', 'b', 'c', 'd', 'e', 'f', 'g'", "{}", "{}, {}, {}, {}, {}, {}", "true, false", "function() end", "0/0, 0/0, 0/0, 0/0, 0/0, 0/0, 0/0",
            "1/0, -1/0, 1/0, -1/0, 1/0, -1/0", "1e300, -1e300, 1e300, 1e300, 1e300, 1e300", "-1, -1, -1, -1, -1, -1, -1", "0, 0, 0, 0, 0, 0, 0",
            "nil, nil, nil, nil, 'x'", "'12', '0.5', '0.5', '0.1', 'red'", "1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12",
        };

        [Fact]
        public void Wrong_arguments_to_every_function_never_throw_out()
        {
            var probe = Game("function update(dt) end");
            var all = probe.Box.FunctionNames().Where(n => !n.StartsWith("math.") && !n.StartsWith("string.") && !n.StartsWith("table.")).ToList();
            foreach (string f in GlassScriptApi.Functions.Concat(LuaSandbox.CommonNames).Concat(LuaMotion.Functions)) Assert.Contains(f, all);
            all.AddRange(new[] { "string.format", "string.rep", "string.sub", "string.find", "string.char", "string.byte", "table.concat", "table.insert", "table.remove", "table.sort", "table.unpack", "math.random", "math.floor", "math.max" });
            int calls = 0, errors = 0;
            foreach (bool strict in new[] { false, true })
                foreach (string f in all)
                    foreach (string args in BadArgs)
                    {
                        if (f == "error" || f == "assert" || f == "update") continue;        // (these are meant to stop the script; update is the script's own)
                        string src = "-- probe\nfunction update(dt)\n  local r = " + f + "(" + args + ")\nend";
                        var g = Game(src, 1, strict);
                        Assert.True(g.Error == "", f + "(" + args + ") broke at load: " + g.Error);
                        for (int i = 0; i < 3; i++) g.Step(1f / 30f, Hand(0.4f, 0.4f));
                        calls++;
                        AssertSane(g);
                        if (g.Error.Length > 0)
                        {
                            errors++;
                            // an error is a CLEAR one: it has the line, and the game is cleanly over
                            Assert.True(g.ErrorLine == 3, f + "(" + args + "): " + g.Error);
                            Assert.Equal("over", g.phase);
                            Assert.DoesNotContain("Exception", g.Error);
                            Assert.DoesNotContain("it broke inside", g.Error);
                        }
                    }
            o.WriteLine(calls + " calls with wrong arguments, " + errors + " ended in a clear error");
            Assert.True(calls > 1000);
        }

        [Fact]
        public void The_grid_methods_and_the_pose_table_survive_wrong_arguments_too()
        {
            foreach (string args in BadArgs)
            {
                var g = Game("local g = grid(3, 3, 0.5, 0.5, 0.6, 0.6)\nfunction update(dt)\n  local a, b = g:cell(" + args + ")\n  local c = g.center(" + args + ")\n  g:draw(" + args + ")\n  g.draw(" + args + ")\n  local h = grid(" + args + ")\n  h:draw()\nend");
                for (int i = 0; i < 3; i++) g.Step(1f / 30f, Hand(0.4f, 0.4f));
                Assert.True(g.Error == "", args + ": " + g.Error);
                AssertSane(g);
            }
        }

        [Fact]
        public void In_a_game_broken_numbers_are_left_out_and_in_the_check_they_are_errors()
        {
            string src = "function update(dt)\n  rect(W / 2, 0.5, 0.2, 0.2, 'pink')\n  rect(nil, 0.5, 0.2, 0.2, 'pink')\n  disc(0/0, 0.5, 0.1, 'red')\n  pic('star', 1/0, 0.2, 0.1)\n  text('hi', nil, nil)\n  line(0, 0, 1)\nend";
            var g = Game(src);
            g.Step(0.033f, null);
            Assert.Equal("", g.Error);
            Assert.Single(g.sprites);                       // only the good one
            Assert.Contains(g.Box.Notes, n => n.Contains("rect") && n.Contains("x was not a number"));
            AssertSane(g);
            var s = Game(src, 1, strict: true);
            s.Step(0.033f, null);
            Assert.Equal(3, s.ErrorLine);
            Assert.Contains("rect: x (argument 1) must be a number, it is nil", s.Error);
            var r = GlassScriptCheck.Run(src);
            Assert.False(r.ok);
            Assert.Equal(3, r.errorLine);
            Assert.Equal("rect(nil, 0.5, 0.2, 0.2, 'pink')", r.errorSource);
        }

        [Fact]
        public void Too_many_things_too_long_words_and_huge_numbers_are_capped()
        {
            // 100 000 rects in one frame: either the budget stops it or 160 are drawn - never more
            var sw = Stopwatch.StartNew();
            var g = Game("function update(dt)\n  for i = 1, 100000 do rect(rnd(0, W), rnd(), 0.01, 0.01, 'red') end\nend");
            g.Step(0.033f, null);
            Assert.True(g.sprites.Count <= GlassScript.MaxSprites);
            Assert.True(g.Error.Contains("took too long") || g.Error.Contains("too much memory") || g.Dropped > 90000, g.Error + " dropped " + g.Dropped);
            Assert.True(sw.ElapsedMilliseconds < 3000);
            // 5000 in a frame is within the budget: 160 are drawn, the rest is counted
            var h = Game("function update(dt)\n  for i = 1, 5000 do disc(rnd(0, W), rnd(), 0.01, 'red') end\n  for i = 1, 100 do text('t' .. i, 0.5, 0.5, 0.05) end\nend");
            h.Step(0.033f, null);
            Assert.Equal("", h.Error);
            Assert.Equal(GlassScript.MaxSprites, h.sprites.Count);
            Assert.Equal(5100 - 160, h.Dropped);
            Assert.Equal(5100, h.MaxDrawn);
            // texts are capped on their own, too
            var t = Game("function update(dt)\n  for i = 1, 100 do text('t' .. i, 0.5, 0.5, 0.05) end\n  for i = 1, 50 do disc(0.5, 0.5, 0.01, 'red') end\nend");
            t.Step(0.033f, null);
            Assert.Equal(GlassScript.MaxLabels, t.sprites.Count(s => s.art == GlassArt.Label));
            Assert.Equal(50, t.sprites.Count(s => s.art == GlassArt.Disc));
            // a megabyte of text
            var m = Game("local s = 'abcdefgh'\nfor i = 1, 17 do s = s .. s end\nn = #s\nfunction update(dt)\n  text(s, 0.5, 0.5, 0.05)\n  banner(s)\n  pet_say(s)\n  card(s, s, 0.5, 0.5)\n  if time() > 0.1 then finish(s) end\nend");
            Assert.Equal("", m.Error);
            Assert.Equal(8 * 131072, (int)m.Box.Get("n").Number);
            SkipIntro(m);
            for (int i = 0; i < 10; i++) m.Step(0.033f, null);
            Assert.Equal("", m.Error);
            Assert.Equal(LuaSandbox.MaxText, m.sprites[0].text.Length);
            Assert.True(m.banner.Length <= LuaSandbox.MaxText && m.petLine.Length <= LuaSandbox.MaxText && m.result.Length <= 80);
            Assert.Equal("over", m.phase);
            AssertSane(m);
            // huge and tiny numbers
            var n = Game("function update(dt)\n  rect(1e8, -1e8, 1e8, 1e8, 0xFFFFFF, 55, 1e30)\n  disc(0.5, 0.5, 1e9, 'red')\n  disc(0.5, 0.5, -1, 'red')\n  disc(0.5, 0.5, 0, 'red')\n  rect(0.5, 0.5, 0.1, 0.1, 'red', 0)\n" +
                         "  text('x', 0.5, 0.5, 99)\n  score(5000000) lives(1e9) level(-5) add(1e12)\n  pet_goto(1e9, -1e9, 1e9)\n  reach(1e9)\n  banner('x', 1e9)\n  pet_say('x', 1e9)\n  pet_hurt(1e9)\nend");
            n.Step(0.033f, null);
            Assert.Equal("", n.Error);
            AssertSane(n);
            Assert.Equal(999999, n.score); Assert.Equal(9, n.lives); Assert.Equal(1, n.level);
            Assert.InRange(n.handRadius, 0.03f, 0.3f);
            Assert.InRange(n.petLineLeft, 0f, 10f); Assert.InRange(n.petHurt, 0f, 5f);
        }

        // ------------------------------------------------------------------ determinism

        const string Dice = @"
local things = {}
function update(dt)
  if rnd() < 0.3 then things[#things + 1] = {x = rnd(0, W), y = 1, v = rnd(0.2, 0.6), k = rndi(1, 3), m = math.random(), n = math.random(6), q = math.random(3, 9)} end
  for i = #things, 1, -1 do
    local t = things[i]
    t.y = t.y - t.v * dt
    for _, h in ipairs(hands()) do if dist(h.x, h.y, t.x, t.y) < 0.1 then t.y = -1 add(t.k) end end
    if t.y < 0 then table.remove(things, i) else pic(t.k == 1 and 'star' or 'gem', t.x, t.y, 0.05 + t.m * 0.01 * t.n, t.q) end
  end
end";

        static List<string> Trace(int seed, int handSeed)
        {
            var g = Game(Dice, seed);
            var rng = new GlassRng(); rng.Seed(handSeed);
            var input = new GlassInput();
            var lines = new List<string>();
            for (int f = 0; f < 400; f++)
            {
                input.valid[0] = rng.Chance(0.9f); input.x[0] = rng.Range(0f, g.W); input.y[0] = rng.Range(0f, 1f); input.dist[0] = 0.1f;
                g.Step(rng.Range(0.005f, 0.05f), input);
                var sb = new System.Text.StringBuilder();
                sb.Append(g.score).Append('|').Append(g.phase);
                foreach (var s in g.sprites) sb.Append('|').Append(s.art).Append(s.x.ToString("R")).Append(',').Append(s.y.ToString("R")).Append(',').Append(s.size.ToString("R")).Append(',').Append(s.rot);
                lines.Add(sb.ToString());
            }
            Assert.Equal("", g.Error);
            return lines;
        }

        [Fact]
        public void The_same_seed_and_hands_give_the_same_game()
        {
            var a = Trace(5, 9); var b = Trace(5, 9); var c = Trace(6, 9); var d = Trace(5, 10);
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);          // another seed: another game
            Assert.NotEqual(a, d);          // other hands: another game
            Assert.Contains(a, l => l.Contains("star") || l.Contains("gem"));
            // math.randomseed cannot break it, and os.time does not exist to seed from
            var g1 = Game("math.randomseed(1234)\nv = math.random()\nfunction update(dt) end", 3);
            var g2 = Game("math.randomseed(99)\nv = math.random()\nfunction update(dt) end", 3);
            Assert.Equal(g1.Box.Get("v").Number, g2.Box.Get("v").Number);
            // rnd / rndi ranges
            var r = Game("lo, hi, ilo, ihi = 9, -9, 99, -99\nfor i = 1, 2000 do local v = rnd(2, 3) lo = math.min(lo, v) hi = math.max(hi, v) local k = rndi(3, 6) ilo = math.min(ilo, k) ihi = math.max(ihi, k) end\n" +
                         "one = rndi(7, 7) back = rndi(6, 3) plain = rnd() whole = rndi(4)\nfunction update(dt) end");
            Assert.Equal("", r.Error);
            Assert.InRange(r.Box.Get("lo").Number, 2.0, 2.05); Assert.InRange(r.Box.Get("hi").Number, 2.95, 3.0);
            Assert.Equal(3, r.Box.Get("ilo").Number); Assert.Equal(6, r.Box.Get("ihi").Number);
            Assert.Equal(7, r.Box.Get("one").Number); Assert.InRange(r.Box.Get("back").Number, 3, 6);
            Assert.InRange(r.Box.Get("plain").Number, 0.0, 0.99999); Assert.InRange(r.Box.Get("whole").Number, 1, 4);
        }

        // ------------------------------------------------------------------ phases, drawing, the partner

        [Fact]
        public void The_phases_intro_play_over()
        {
            var g = Game("local t, frames, sum = 0, 0, 0\nfunction start() banner_seen = true end\nfunction update(dt)\n  frames = frames + 1  sum = sum + dt  seen = #hands()\n  rect(W / 2, 0.5, 0.3, 0.3, 'pink')\n  text('t ' .. frames, W / 2, 0.8, 0.05)\n  if time() > 1 then finish('Done.') end\n  f, s = frames, sum\nend", 1, false, false, "Catch the stars");
            Assert.Equal("intro", g.phase);
            Assert.Equal("Catch the stars", g.banner);
            Assert.Empty(g.sprites);
            int introFrames = 0;
            while (g.phase == "intro")
            {
                g.Step(1f / 30f, Hand(0.5f, 0.5f));
                introFrames++;
                Assert.Equal(2, g.sprites.Count);                        // it IS drawn during the intro
                if (g.phase == "intro") { Assert.Equal(0, g.Box.Get("s").Number); Assert.Equal(0, g.Box.Get("seen").Number); Assert.Equal(0f, g.seconds); }      // but time stands still and there are no hands
            }
            Assert.InRange(introFrames, 36, 38);                         // 1.2 s
            Assert.Equal("", g.banner);
            int playFrames = 0;
            while (g.phase == "play" && playFrames < 100)
            {
                g.Step(1f / 30f, Hand(0.5f, 0.5f));
                playFrames++;
                if (g.phase == "play") Assert.Equal(1, g.Box.Get("seen").Number);      // now the hand is there
            }
            Assert.Equal("over", g.phase);
            Assert.Equal("Done.", g.result);
            Assert.InRange(g.FinishedAt, 1.0f, 1.1f);
            float at = g.seconds;
            for (int i = 0; i < 30; i++) g.Step(1f / 30f, Hand(0.5f, 0.5f));
            Assert.Equal(at, g.seconds);                                 // the clock stands still when it is over
            Assert.Equal(2, g.sprites.Count);                            // update still runs: the last picture stays
            Assert.Equal(0, g.Box.Get("seen").Number);                   // without hands
            // a long frame is stepped in pieces of 1/30 s - but never in more than two (the headset: after a hitch the
            // script is not run eight times over in the next frame); the two pieces are longer then, no time is lost
            var h = Game("n = 0\nfunction update(dt) n = n + 1  last = dt end");
            SkipIntro(h);
            double before = h.Box.Get("n").Number;
            h.Step(0.05f, null);
            Assert.Equal(2, h.Box.Get("n").Number - before);
            Assert.InRange(h.Box.Get("last").Number, 0.0249, 0.0251);
            before = h.Box.Get("n").Number;
            h.Step(0.25f, null);
            Assert.Equal(2, h.Box.Get("n").Number - before);
            Assert.InRange(h.Box.Get("last").Number, 0.1249, 0.1251);
            h.Step(float.NaN, null); h.Step(-1f, null); h.Step(99f, null);
            Assert.Equal("", h.Error);
        }

        [Fact]
        public void A_finished_game_that_stops_drawing_keeps_its_last_full_picture()
        {
            // finish() in the middle of update, before the frame is drawn - and afterwards nothing is drawn at all
            var g = Game("local over = false\nfunction update(dt)\n  if over then return end\n  if time() > 0.2 then over = true  finish('End')  return end\n  rect(W / 2, 0.5, 0.3, 0.3, 'pink')\n  disc(0.3, 0.3, 0.1, 'sky')\n  text('playing', W / 2, 0.8, 0.05)\nend");
            SkipIntro(g);
            for (int i = 0; i < 60; i++) { g.Step(1f / 30f, null); Assert.Equal(3, g.sprites.Count); }
            Assert.Equal("over", g.phase);
            Assert.Equal("playing", g.sprites[2].text);
        }

        [Fact]
        public void Drawing_becomes_sprites_in_the_order_it_was_drawn()
        {
            var pack = new GamePackDef { template = "script", script = @"
function update(dt)
  rect(0.5, 0.4, 0.3, 0.2, 'pink', 0.5, 30)
  disc(0.6, 0.5, 0.1, 0x112233)
  hoop(0.7, 0.5, 0.05, '#ff8800', 0.25)
  line(0.2, 0.2, 0.6, 0.5, 0.01, 'ink')
  pic('star', 0.3, 0.3, 0.12, 45, 0.9)
  pic('enemy', 0.3, 0.3, 0.12, 0, 1, true)
  pic('Fish', 0.1, 0.1, 0.2)
  pic('pack:fish', 0.1, 0.1, 0.2)
  pic('unicorn', 0.1, 0.1, 0.2)
  text('Hello', 0.9, 0.9, 0.06, 'blue')
  text(42, 0.9, 0.8)
  card('Big Hand', 'your hand is bigger', 0.9, 0.5, 0.34, 0.5)
  line(0.5, 0.2, 0.5, 0.6, 0.02, 'red')
end" };
            pack.art.Add(new PackArt { name = "fish", file = "fish.png" });
            var g = new GlassScript();
            g.Start(pack, 1);
            g.Step(0.033f, null);
            Assert.Equal("", g.Error);
            var s = g.sprites;
            Assert.Equal(13, s.Count);
            for (int i = 0; i < s.Count; i++) Assert.Equal(GlassScript.FirstId + i, s[i].id);
            Assert.Equal((GlassArt.Rect, 0.5f, 0.4f, 0.2f, 0.3f, 0xF6A5B8, 0.5f, 30f), (s[0].art, s[0].x, s[0].y, s[0].size, s[0].w, s[0].color, s[0].alpha, s[0].rot));
            Assert.Equal((GlassArt.Disc, 0.6f, 0.5f, 0.2f, 0.2f, 0x112233, 1f), (s[1].art, s[1].x, s[1].y, s[1].size, s[1].w, s[1].color, s[1].alpha));
            Assert.Equal((GlassArt.RingShape, 0.1f, 0xFF8800, 0.25f), (s[2].art, s[2].size, s[2].color, s[2].alpha));
            Assert.Equal(GlassArt.Rect, s[3].art);                      // a line is a turned rectangle: its middle, its length, its thickness, its angle
            Assert.Equal(0.4f, s[3].x, 5); Assert.Equal(0.35f, s[3].y, 5); Assert.Equal(0.5f, s[3].w, 5); Assert.Equal(0.01f, s[3].size, 5);
            Assert.Equal(Math.Atan2(0.3, 0.4) * 180 / Math.PI, s[3].rot, 3);
            Assert.Equal((GlassArt.Star, 0.12f, 0f, -1, 45f, 0.9f, 0), (s[4].art, s[4].size, s[4].w, s[4].color, s[4].rot, s[4].alpha, s[4].frame));
            Assert.Equal((GlassArt.Enemy, 1), (s[5].art, s[5].frame));       // flip = facing left
            Assert.Equal("pack:fish", s[6].art); Assert.Equal("pack:fish", s[7].art);      // the pack's own picture, by its name
            Assert.Equal(GlassArt.Star, s[8].art);                      // no such picture: a star, and a note for the writer
            Assert.Contains(g.Box.Notes, n => n.Contains("unicorn"));
            Assert.Equal((GlassArt.Label, "Hello", 0.06f, 0x5B9BD5), (s[9].art, s[9].text, s[9].size, s[9].color));
            Assert.Equal((GlassArt.Label, "42", 0.05f, LuaSandbox.Ink), (s[10].art, s[10].text, s[10].size, s[10].color));
            Assert.Equal((GlassArt.Card, "Big Hand", "your hand is bigger", 0.34f, 0.5f), (s[11].art, s[11].text, s[11].text2, s[11].size, s[11].fill));
            Assert.Equal(90f, s[12].rot, 3); Assert.Equal(0.4f, s[12].w, 5);
            AssertSane(g);
            foreach (string name in GlassScript.Stickers) Assert.Contains(name, GlassArt.All);
        }

        [Fact]
        public void Colours()
        {
            int c = 0;
            foreach (string name in LuaSandbox.ColorNames)
            {
                c = -1;
                Assert.True(LuaSandbox.TryColor(MoonSharp.Interpreter.DynValue.NewString(name), ref c));
                Assert.InRange(c, 0, 0xFFFFFF);
            }
            Assert.Equal(16, LuaSandbox.ColorNames.Length);
            var g = Game("function update(dt)\n  local list = {'cream', 'INK', ' Grey ', 'gray', 0xFF0000, 255, '#00ff00', '0x0000ff', '#f80', {1, 0, 0}, {255, 128, 0}, 'nonsense', -5, 1e9, {}, true}\n  for i, c in ipairs(list) do rect(0.1 * i, 0.5, 0.05, 0.05, c) end\n  rect(1.7, 0.5, 0.05, 0.05)\nend");
            g.Step(0.033f, null);
            Assert.Equal("", g.Error);
            int[] expect = { 0xFFF8EA, 0x4A2F27, 0xB9AFA6, 0xB9AFA6, 0xFF0000, 255, 0x00FF00, 0x0000FF, 0xFF8800, 0xFF0000, 0xFF8000, LuaSandbox.Ink, LuaSandbox.Ink, LuaSandbox.Ink, LuaSandbox.Ink, LuaSandbox.Ink, LuaSandbox.Ink };
            Assert.Equal(expect, g.sprites.Select(s => s.color).ToArray());
            Assert.Contains(g.Box.Notes, n => n.Contains("unknown colour 'nonsense'"));
        }

        [Fact]
        public void The_grid_helper()
        {
            var g = Game(@"
local g = grid(9, 9, W / 2, 0.5, 0.9, 0.9)
cw, ch, cols = g.cw, g.ch, g.cols
x1, y1 = g:center(1, 1)
x9, y9 = g:center(9, 9)
c1, r1 = g:cell(x1, y1)
c9, r9 = g:cell(x9 + 0.04, y9 + 0.04)
out = g:cell(0.1, 0.5) == nil and g:cell(W / 2, 0.99) == nil
dc, dr = g.cell(W / 2, 0.5)                 -- with a dot instead of a colon
tc, tr = g:cell({x = x9, y = y1})           -- a tap as it comes from taps()
local wide = grid(4, 2, 1, 0.5, 0.8, 0.2)
wx, wy = wide:center(4, 2)
function update(dt) g:draw('brown', 0.004)  wide:draw('ink') end");
            Assert.Equal("", g.Error);
            double N(string n) => g.Box.Get(n).Number;
            Assert.Equal(0.1, N("cw"), 5); Assert.Equal(0.1, N("ch"), 5); Assert.Equal(9, N("cols"));
            Assert.Equal(g.W / 2 - 0.4, N("x1"), 5); Assert.Equal(0.1, N("y1"), 5);
            Assert.Equal(g.W / 2 + 0.4, N("x9"), 5); Assert.Equal(0.9, N("y9"), 5);
            Assert.Equal((1, 1, 9, 9), ((int)N("c1"), (int)N("r1"), (int)N("c9"), (int)N("r9")));
            Assert.True(g.Box.Get("out").Boolean);
            Assert.Equal((5, 5), ((int)N("dc"), (int)N("dr")));
            Assert.Equal((9, 1), ((int)N("tc"), (int)N("tr")));
            Assert.Equal(1.3, N("wx"), 5); Assert.Equal(0.55, N("wy"), 5);
            g.Step(0.033f, null);
            Assert.Equal(20 + 8, g.sprites.Count);                      // 10 + 10 lines, and 5 + 3
            Assert.All(g.sprites, s => Assert.Equal(GlassArt.Rect, s.art));
            Assert.Equal(g.W / 2 - 0.45f, g.sprites[0].x, 5); Assert.Equal(0.004f, g.sprites[0].w, 5); Assert.Equal(0.904f, g.sprites[0].size, 5);
            Assert.Equal(0.05f, g.sprites[10].y, 5); Assert.Equal(0.904f, g.sprites[10].w, 5);
        }

        [Fact]
        public void The_partner_walks_poses_plays_and_speaks()
        {
            var g = Game(LuaMotion.Worked + @"
cmd = ''
function update(dt)
  if cmd == 'go' then pet_goto(1.5) end
  if cmd == 'back' then pet_goto(0.2, 0.9, 1.2) end
  if cmd == 'cheer' then pet_pose('cheer') end
  if cmd == 'swing' then pet_play('swing') end
  if cmd == 'holdjump' then pet_pose('jump') end
  if cmd == 'idle' then pet_pose('idle') end
  if cmd == 'say' then pet_say('Hello there', 1) end
  if cmd == 'hurt' then pet_hurt() pet_play('hurt', 0.5) end
  if cmd == 'face' then pet_face(0) end
  if cmd == 'bad' then pet_pose('moonwalk') end
  cmd = ''
  px, py = pet_at()
end");
            Assert.Equal("", g.Error);
            SkipIntro(g);
            void Do(string cmd, int frames = 1) { g.Box.Set("cmd", cmd); for (int i = 0; i < frames; i++) g.Step(1f / 30f, null); Assert.Equal("", g.Error); AssertSane(g); }
            Assert.Equal(g.W / 2, g.petX, 4); Assert.Equal("idle", g.petPose); Assert.False(g.petCustom);
            Do("go", 5);
            Assert.Equal("walk", g.petPose); Assert.True(g.petFacesRight);
            Assert.Equal(g.W / 2 + 5 * GlassScript.PetSpeed / 30f, g.petX, 3);
            Assert.Equal(g.petX, g.Box.Get("px").Number, 1);
            Do("", 60);
            Assert.Equal(1.5f, g.petX, 4); Assert.Equal("idle", g.petPose); Assert.Equal(0f, g.petY);
            Do("back", 3);
            Assert.Equal("run", g.petPose); Assert.False(g.petFacesRight); Assert.Equal(0f, g.petY);      // upright: it stays on the floor
            Do("", 60);
            Assert.Equal(0.2f, g.petX, 4);
            Do("cheer", 2); Assert.Equal("cheer", g.petPose);
            Do("face"); Assert.False(g.petFacesRight);
            // a motion of the script, played once: petCustom is on for its second, the pose is the motion's, then back to cheer
            Do("swing", 12);
            Assert.True(g.petCustom); Assert.Equal("swing", g.PetMotion);
            var expect = new Pose();
            Assert.True(g.petCustomPose[Bone.ArmUR].X > 5f, "wound up: the arm is back " + g.petCustomPose[Bone.ArmUR]);
            Do("", 25);
            Assert.False(g.petCustom); Assert.Equal("", g.PetMotion); Assert.Equal("cheer", g.petPose);
            // a motion as the pose: it holds its last frame (it is not a loop), and walking comes before it
            Do("holdjump", 60);
            Assert.True(g.petCustom); Assert.Equal("jump", g.PetMotion);
            g.Motions.Evaluate("jump", 1f, 0f, expect);
            Assert.Equal(expect.HipsOffset.Y, g.petCustomPose.HipsOffset.Y, 3);
            Do("go", 3);
            Assert.False(g.petCustom); Assert.Equal("walk", g.petPose);
            Do("", 80);
            Assert.True(g.petCustom);                                    // arrived: the held motion again
            Do("idle", 2);
            Assert.False(g.petCustom); Assert.Equal("idle", g.petPose);
            Do("say");
            Assert.Equal("Hello there", g.petLine); Assert.InRange(g.petLineLeft, 0.9f, 1f);
            Do("", 31);
            Assert.Equal("", g.petLine); Assert.Equal(0f, g.petLineLeft);
            Do("hurt");
            Assert.InRange(g.petHurt, 0.7f, 0.8f); Assert.Equal("hurt", g.petPose);
            Do("", 20);
            Assert.Equal("idle", g.petPose);                             // a pose that was played for a moment goes back
            Do("bad");
            Assert.Equal("idle", g.petPose);
            Assert.Contains(g.Box.Notes, n => n.Contains("moonwalk"));

            // flat: it walks anywhere on the field
            var f = Game("flat(true)\nfunction update(dt) pet_goto(0.3, 0.8, 2) end");
            Assert.True(f.flat);
            SkipIntro(f);
            for (int i = 0; i < 60; i++) f.Step(1f / 30f, null);
            Assert.Equal(0.3f, f.petX, 3); Assert.Equal(0.8f, f.petY, 3);
            // flat() later than start is refused with a note
            var late = Game("function update(dt) flat(true) end");
            late.Step(0.033f, null);
            Assert.False(late.flat);
            Assert.Contains(late.Box.Notes, n => n.Contains("flat()"));
        }

        [Fact]
        public void Sounds_score_and_the_rest_of_the_game_state()
        {
            var g = Game("function start() lives(5) level(3) score(10) end\nfunction update(dt)\n  sound('pop') sound('pop') sound('coin') sound('trumpet') sound('POP')\n  add(2) add() add(-1)\n  s, l, v, t, w, h, r, fl = score(), lives(), level(), time(), W, H, reach(), flat()\n  banner('Level up!', 0.5)\nend");
            Assert.Equal((10, 5, 3), (g.score, g.lives, g.level));
            SkipIntro(g);
            for (int i = 0; i < 10 && g.sounds.Count == 0; i++) g.Step(1f / 30f, null);
            Assert.Equal(new[] { "pop", "coin" }, g.sounds);               // each once a frame; unknown names are ignored
            Assert.Contains(g.Box.Notes, n => n.Contains("trumpet"));
            Assert.Equal(g.score, (int)g.Box.Get("s").Number);
            Assert.Equal(g.W, g.Box.Get("w").Number, 5); Assert.Equal(1, g.Box.Get("h").Number);
            Assert.Equal(GlassGames.HandRadiusBase, g.Box.Get("r").Number, 5);
            Assert.False(g.Box.Get("fl").Boolean);
            Assert.Equal("Level up!", g.banner);
            int pops = 0;
            for (int i = 0; i < 30; i++) { g.Step(1f / 30f, null); pops += g.sounds.Count(s => s == "pop"); }
            Assert.InRange(pops, 6, 10);                                   // the same sound at most ten times a second
            Assert.Equal(GlassScript.TemplateName, new GlassScript().Template);
            Assert.IsType<GlassScript>(GlassGames.Create("script"));
            Assert.True(GlassGames.Is("Script"));
            var st = new GlassState();
            g.FillState(st);
            Assert.Equal((g.score, 5, 3, "play"), (st.score, st.lives, st.level, st.phase));
        }

        // ------------------------------------------------------------------ the hands

        const string TapScript = "n, hn, last = 0, 0, ''\nfunction update(dt)\n  for _, t in ipairs(taps()) do n = n + 1  last = string.format('%.2f,%.2f', t.x, t.y) end\n  local hs = hands()\n  hn = #hs\n  down = hn > 0 and hs[1].down\n  id = hn > 0 and hs[1].id or 0\n  r = hn > 0 and hs[1].r or 0\n  held_here = held(0.5, 0.5, 0.1)\nend";

        [Fact]
        public void On_the_desk_a_tap_is_the_fingertip_coming_down()
        {
            var g = Game(TapScript, 1, false, touch: true);
            int Taps() => (int)g.Box.Get("n").Number;
            // during the intro there are no hands at all
            g.Step(1f / 30f, Hand(0.5f, 0.5f, 0.001f));
            Assert.Equal(0, Taps()); Assert.Equal(0, g.Box.Get("hn").Number);
            SkipIntro(g);
            // hovering: listed, not down, no tap however long it rests
            for (int i = 0; i < 60; i++) g.Step(1f / 30f, Hand(0.5f, 0.5f, 0.08f));
            Assert.Equal(0, Taps()); Assert.Equal(1, g.Box.Get("hn").Number); Assert.False(g.Box.Get("down").Boolean);
            Assert.Equal(1, g.Box.Get("id").Number); Assert.Equal(GlassGames.HandRadiusBase, g.Box.Get("r").Number, 4);
            Assert.Equal(0, g.Box.Get("held_here").Number);
            Assert.DoesNotContain(g.sprites, s => s.id == GlassScript.DwellId);
            // down: exactly one tap, where the finger is
            g.Step(1f / 30f, Hand(0.52f, 0.48f, 0.01f));
            Assert.Equal(1, Taps()); Assert.Equal("0.52,0.48", g.Box.Get("last").String); Assert.True(g.Box.Get("down").Boolean);
            for (int i = 0; i < 30; i++) g.Step(1f / 30f, Hand(0.52f, 0.48f, 0.01f));
            Assert.Equal(1, Taps());
            Assert.InRange(g.Box.Get("held_here").Number, 0.95, 1.1);         // it has been resting there for a second
            // trembling around the height of the desk does not tap again (it lets go only above 3.5 cm)
            for (int i = 0; i < 30; i++) g.Step(1f / 30f, Hand(0.52f, 0.48f, i % 2 == 0 ? 0.03f : 0.012f));
            Assert.Equal(1, Taps());
            // up, and down again somewhere else: the second tap
            g.Step(1f / 30f, Hand(0.52f, 0.48f, 0.06f));
            Assert.False(g.Box.Get("down").Boolean); Assert.Equal(0, g.Box.Get("held_here").Number);
            g.Step(1f / 30f, Hand(1.2f, 0.3f, 0.005f));
            Assert.Equal(2, Taps()); Assert.Equal("1.20,0.30", g.Box.Get("last").String);
            // the other hand taps too; a hand that is far away is not there at all
            var two = new GlassInput();
            two.valid[0] = true; two.x[0] = 1.2f; two.y[0] = 0.3f; two.dist[0] = 0.005f;
            two.valid[1] = true; two.x[1] = 0.3f; two.y[1] = 0.7f; two.dist[1] = 0.005f;
            g.Step(1f / 30f, two);
            Assert.Equal(3, Taps()); Assert.Equal(2, g.Box.Get("hn").Number);
            g.Step(1f / 30f, Hand(0.5f, 0.5f, 0.5f));
            Assert.Equal(0, g.Box.Get("hn").Number);
            g.Step(1f / 30f, Hand(float.NaN, 0.5f, 0.01f));
            Assert.Equal(0, g.Box.Get("hn").Number); Assert.Equal(3, Taps());
            // a finger that arrives already down is a tap
            g.Step(1f / 30f, Hand(0.9f, 0.9f, 0.01f));
            Assert.Equal(4, Taps());
        }

        [Fact]
        public void Through_the_glass_a_hand_that_rests_half_a_second_taps_once()
        {
            var g = Game(TapScript);                      // touch = false: a monitor, or the upright stage
            int Taps() => (int)g.Box.Get("n").Number;
            SkipIntro(g);
            // coming near does NOT tap (a hand cannot touch anything here), moving about does not either
            for (int i = 0; i < 60; i++) g.Step(1f / 30f, Hand(0.3f + i * 0.01f, 0.5f, 0.1f));
            Assert.Equal(0, Taps()); Assert.True(g.Box.Get("down").Boolean);       // every hand that is near counts as down
            // resting: after 0.5 s exactly one tap, at the place it rests on - and the ring closes meanwhile
            int frames = 0; bool ring = false; float fill = 0f;
            while (Taps() == 0 && frames < 40)
            {
                g.Step(1f / 30f, Hand(1.0f + (frames % 2) * 0.01f, 0.4f, 0.1f));    // (a hand in the air trembles a little)
                frames++;
                foreach (var s in g.sprites) if (s.id == GlassScript.DwellId) { ring = true; Assert.True(s.fill >= fill); fill = s.fill; Assert.Equal(GlassArt.RingShape, s.art); }
            }
            Assert.InRange(frames, 15, 17);
            Assert.True(ring && fill > 0.8f);
            Assert.Equal("1.00,0.40", g.Box.Get("last").String);
            for (int i = 0; i < 90; i++) g.Step(1f / 30f, Hand(1.0f, 0.4f, 0.1f));
            Assert.Equal(1, Taps());                                               // once, however long it stays
            Assert.DoesNotContain(g.sprites, s => s.id == GlassScript.DwellId);
            // away and back: it can tap again
            for (int i = 0; i < 5; i++) g.Step(1f / 30f, Hand(0.5f, 0.5f, 0.1f));
            for (int i = 0; i < 20; i++) g.Step(1f / 30f, Hand(1.0f, 0.4f, 0.1f));
            Assert.Equal(2, Taps());
            // a slow drift never rests
            for (int i = 0; i < 90; i++) g.Step(1f / 30f, Hand(0.2f + i * 0.004f, 0.5f, 0.1f));
            Assert.Equal(2, Taps());
            // held() counts the time near a point, also through the glass
            g.Step(1f / 30f, Hand(1.5f, 0.9f, 0.1f));
            for (int i = 0; i < 30; i++) g.Step(1f / 30f, Hand(0.5f + (i % 3) * 0.02f, 0.5f, 0.1f));
            Assert.InRange(g.Box.Get("held_here").Number, 0.95, 1.1);
            g.Step(1f / 30f, Hand(0.9f, 0.5f, 0.1f));
            Assert.Equal(0, g.Box.Get("held_here").Number);
            // a script that never asks for taps gets no ring
            var h = Game("function update(dt) for _, x in ipairs(hands()) do disc(x.x, x.y, 0.02, 'red') end end");
            SkipIntro(h);
            for (int i = 0; i < 12; i++) h.Step(1f / 30f, Hand(1f, 0.4f, 0.1f));
            Assert.Single(h.sprites);
        }

        // ------------------------------------------------------------------ the Doc

        [Fact]
        public void The_doc_names_every_function_and_every_named_function_exists()
        {
            string doc = GlassScriptApi.Doc;
            var g = Game("function update(dt) end");
            var runtime = g.Box.FunctionNames();
            var own = runtime.Where(n => !n.Contains('.') && n != "update" && Array.IndexOf(LuaSandbox.AllowedGlobals, n) < 0).ToList();
            // what the three parts say they define is exactly what is there
            Assert.Equal(own.OrderBy(s => s, StringComparer.Ordinal), GlassScriptApi.Functions.Concat(LuaSandbox.CommonNames).Concat(LuaMotion.Functions).OrderBy(s => s, StringComparer.Ordinal));
            // every function of the runtime is in the Doc as  name(
            foreach (string f in own) Assert.True(Regex.IsMatch(doc, @"(?<![\w.:])" + Regex.Escape(f) + @"\("), "the Doc does not show " + f + "(");
            foreach (string m in GlassScriptApi.GridMethods) Assert.Contains("g:" + m + "(", doc);
            // every  name(  in the Doc is a function of the runtime (or the script's own start / update, or Lua's function)
            var scripts = new HashSet<string> { "update", "start", "function" };
            foreach (Match m in Regex.Matches(doc, @"(?<![\w.:])([a-z_][a-z_0-9]*)\("))
            {
                string name = m.Groups[1].Value;
                Assert.True(scripts.Contains(name) || runtime.Contains(name), "the Doc names " + name + "( but there is no such function");
            }
            foreach (Match m in Regex.Matches(doc, @"\b(math|string|table)\.([a-z0-9]+)\("))
                Assert.Contains(m.Groups[1].Value + "." + m.Groups[2].Value, runtime);
            foreach (Match m in Regex.Matches(doc, @"\bg:([a-z]+)\(")) Assert.Contains(m.Groups[1].Value, GlassScriptApi.GridMethods);
            // the lists in the Doc are the runtime's lists
            foreach (string c in LuaSandbox.ColorNames) Assert.Contains(" " + c, doc);
            foreach (string s in GlassScript.Stickers) Assert.Contains(s, doc);
            foreach (string s in SfxSynth.Names) Assert.Contains(" " + s, doc);
            foreach (string p in new[] { "idle", "cheer", "hurt", "pick", "wipe", "cower", "down" }) { Assert.Contains(p, GlassScript.Poses); Assert.Contains(p, doc); }
            foreach (string b in LuaMotion.BoneNames) Assert.Contains(b, doc);
            foreach (string b in LuaMotion.BaseNames) Assert.Contains(b, doc);
            foreach (string gone in new[] { "io", "os", "require", "load", "pcall", "coroutine", "setmetatable", "gsub" }) Assert.Contains(gone, LuaSandbox.CommonDoc);
            // the numbers the Doc promises
            Assert.Contains("160 things", doc); Assert.Equal(160, GlassScript.MaxSprites);
            Assert.Contains("32 of them words", doc); Assert.Equal(32, GlassScript.MaxLabels);
            Assert.Contains("60 letters", doc); Assert.Equal(60, LuaSandbox.MaxText);
            Assert.Contains("half a\n             second", doc); Assert.Equal(0.5f, GlassScript.RestSeconds);
            Assert.Contains("At most 40 a frame", doc); Assert.Equal(40, GlassScript.MaxButtons);
            Assert.Contains("At most 6 on their way at once and 40 in a minute", doc); Assert.Equal((6, 40), (GlassScript.MaxOutsFlying, GlassScript.MaxOutsMinute));
            Assert.Contains("1.2 fast", doc); Assert.Equal(1.2f, GlassScript.OutSpeed); Assert.Contains("metres across (0.06)", doc); Assert.Equal(0.06f, GlassScript.OutSize);
            foreach (string what in new[] { "'blocked'", "'hit'", "'missed'" }) Assert.Contains(what, doc);
            // the rules the user's feedback asked for: a tool stands upright and is made of the kit; flat is for boards only
            Assert.Contains("A TOOL (calculator, timer, notes, counter, converter ...) is UPRIGHT - never flat", doc);
            Assert.Contains("Keys are at least 0.14 x 0.12", doc);
            Assert.Contains("Use flat ONLY for board and card games and for Nilo running about on the desk", doc);
            Assert.Contains("things must really LEAVE the field", doc);
            // the two worked motions are in it word for word, and the whole text fits a prompt
            Assert.Contains(LuaMotion.Worked, doc);
            Assert.Contains(LuaMotion.Doc, doc);
            Assert.True(doc.Length < 18000, "the Doc is " + doc.Length + " characters");
            o.WriteLine("Doc: " + doc.Length + " characters, " + doc.Split('\n').Length + " lines");
            // every pose the Doc offers is one the renderers know (GlassPoses.For)
            var p0 = new Pose(); var p1 = new Pose();
            foreach (string pose in GlassScript.Poses.Where(p => p != "idle"))
            {
                GlassPoses.For(pose, p0, 1.3f, 0.3f, 0.7f); GlassPoses.For("idle", p1, 1.3f, 0.3f, 0.7f);
                bool differs = false;
                for (int i = 0; i < Skeleton.N; i++) if (p0.Euler[i] != p1.Euler[i]) differs = true;
                Assert.True(differs, "the renderers do not know the pose " + pose);
            }
        }
    }
}
