using LivingPartner.Host;
using LivingPartner.Shared;
using Xunit;
using Xunit.Abstractions;

namespace LivingPartner.Tests
{
    /// <summary>
    /// What a hand SHOWS and how fast it MOVES (GlassInput.seen / fingers / speed -> hands(), all_hands()), the three hand
    /// signs as pictures, and the three games that used to be C# in the headset and are scripts now - Rock Paper Scissors,
    /// Star Pop, Red Light - each played here by a bot that means it. Driven the way a renderer drives a game: hands in,
    /// sprites out.
    /// </summary>
    public class HandSignGameTests
    {
        readonly ITestOutputHelper o;
        public HandSignGameTests(ITestOutputHelper o) { this.o = o; }

        const float Dt = 1f / 30f;
        const int Rock = 0, Paper = 30, Scissors = 6, Point = 2, Unclear = 10, Unknown = -1;

        static GlassScript Start(string id, int seed = 1)
        {
            var g = new GlassScript();
            g.Start(GlassScriptExamples.Pack(GlassScriptExamples.Find(id)), seed);
            Assert.True(g.Error == "", id + ": " + g.Error);
            GlassScriptTests.SkipIntro(g);
            return g;
        }

        static void Step(GlassScript g, GlassInput input)
        {
            g.Step(Dt, input);
            Assert.True(g.Error == "", g.Error);
            Assert.Empty(g.Box.Notes);
        }

        /// <summary>A hand that is seen: near the field (it acts) or held up away from it; what it shows, how fast it moves.</summary>
        static GlassInput Seen(float x, float y, int fingers, float speed = 0f, bool near = false)
        {
            var i = new GlassInput();
            i.seen[0] = true; i.valid[0] = near; i.x[0] = x; i.y[0] = y; i.dist[0] = near ? 0.1f : 0.6f; i.fingers[0] = fingers; i.speed[0] = speed;
            return i;
        }

        static string Repo()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "RUNBOOK.md"))) d = d.Parent;
            Assert.NotNull(d);
            return d.FullName;
        }

        static bool Says(GlassGame g, string text) => g.sprites.Any(s => s.art == GlassArt.Label && s.text == text);
        static bool SaysStart(GlassGame g, string text) => g.sprites.Any(s => s.art == GlassArt.Label && s.text.StartsWith(text, StringComparison.Ordinal));

        // ------------------------------------------------------------------ the API

        [Fact]
        public void A_shape_is_read_from_the_stretched_fingers()
        {
            Assert.Equal("rock", GlassGames.Shape(0)); Assert.Equal("rock", GlassGames.Shape(1));                 // (the thumb is not asked)
            Assert.Equal("paper", GlassGames.Shape(30)); Assert.Equal("paper", GlassGames.Shape(31));
            Assert.Equal("paper", GlassGames.Shape(2 | 4 | 8)); Assert.Equal("paper", GlassGames.Shape(4 | 8 | 16));   // (an open hand whose little finger - or index - reads as bent)
            Assert.Equal("scissors", GlassGames.Shape(6)); Assert.Equal("scissors", GlassGames.Shape(7));
            Assert.Equal("point", GlassGames.Shape(2)); Assert.Equal("point", GlassGames.Shape(3));
            foreach (int unclear in new[] { 4, 8, 16, 2 | 8, 2 | 16, 4 | 8, 8 | 16, 4 | 16 }) Assert.Equal("", GlassGames.Shape(unclear));
            Assert.Equal("", GlassGames.Shape(-1));
            foreach (string s in new[] { "rock", "paper", "scissors", "point" }) Assert.Equal(s, GlassGames.Shape(GlassGames.FingersOf(s)));
            Assert.Equal(-1, GlassGames.FingersOf("")); Assert.Equal(-1, GlassGames.FingersOf("DISPLAY3"));
            // nobody said: not known - never "rock"
            Assert.Equal(-1, new GlassInput().fingers[0]); Assert.Equal(-1, new GlassHandPoint().fingers);
        }

        [Fact]
        public void The_speed_of_a_hand_is_what_it_moved_in_the_last_moment_and_jitter_is_not_speed()
        {
            var m = new HandSpeed();
            // a steady 0.3 m/s at 72 pictures a second
            float t = 0f, v = 0f;
            for (int i = 0; i < 72; i++) { t += 1f / 72f; v = m.Add(t, 0.3f * t, 1f, 0.5f); }
            Assert.InRange(v, 0.29f, 0.31f); Assert.Equal(v, m.Speed);
            // it stops: within a quarter of a second it reads as still
            float x = 0.3f * t;
            for (int i = 0; i < 18; i++) { t += 1f / 72f; v = m.Add(t, x, 1f, 0.5f); }
            Assert.InRange(v, 0f, 0.01f);
            // held still with 2 mm of tracking jitter: far under what counts as moving (0.15)
            var rng = new GlassRng(); rng.Seed(5);
            float worst = 0f;
            for (int i = 0; i < 400; i++) { t += 1f / 72f; worst = Math.Max(worst, m.Add(t, x + rng.Range(-0.002f, 0.002f), 1f + rng.Range(-0.002f, 0.002f), 0.5f)); }
            Assert.InRange(worst, 0f, 0.06f);
            // a slow creep of 5 cm a second is told from standing still
            for (int i = 0; i < 40; i++) { t += 1f / 72f; x += 0.05f / 72f; v = m.Add(t, x, 1f, 0.5f); }
            Assert.InRange(v, 0.04f, 0.06f);
            // lost and found somewhere else: no speed out of the jump
            m.Lost(); Assert.Equal(0f, m.Speed);
            t += 0.5f;
            Assert.Equal(0f, m.Add(t, x + 0.4f, 1f, 0.5f));
            for (int i = 0; i < 20; i++) { t += 1f / 72f; v = m.Add(t, x + 0.4f, 1f, 0.5f); }
            Assert.Equal(0f, v);
            // a long gap without Lost (nobody asked for a while): what is too old is not compared with
            t += 3f;
            Assert.Equal(0f, m.Add(t, x + 1f, 1f, 0.5f));
            // frames of any length, also a hitch
            for (int i = 0; i < 30; i++) { float dt = i == 10 ? 0.2f : 0.011f + 0.004f * (i % 3); t += dt; x += 0.5f * dt; v = m.Add(t, x, 1f, 0.5f); }
            Assert.InRange(v, 0.45f, 0.55f);
        }

        const string Probe = @"-- what the hands are, written out: the frame shows near hands as discs and every seen hand as a line of words
function update(dt)
  for _, h in ipairs(hands()) do
    disc(h.x, h.y, 0.05, 'mint')
    text('near ' .. h.id .. ' [' .. h.shape .. '] ' .. string.format('%.2f', h.speed) .. ' ' .. tostring(h.down), W / 2, 0.2 * h.id, 0.05)
  end
  for _, h in ipairs(all_hands()) do
    text('seen ' .. h.id .. ' [' .. h.shape .. '] ' .. string.format('%.2f', h.speed) .. ' ' .. tostring(h.near) .. string.format(' %.2f %.2f', h.x, h.y), W / 2, 0.5 + 0.2 * h.id, 0.05)
  end
end
";

        static List<string> Words(GlassGame g) => g.sprites.Where(s => s.art == GlassArt.Label).Select(s => s.text).ToList();

        [Fact]
        public void Hands_have_a_shape_and_a_speed_and_all_hands_lists_a_hand_that_is_away_from_the_field()
        {
            var g = GlassScriptTests.Game(Probe);
            GlassScriptTests.SkipIntro(g);
            // nobody
            g.Step(Dt, null);
            Assert.Empty(Words(g));
            // a hand as every renderer and test gave it until now (valid, nothing else said): in both lists, shape not known, speed 0
            g.Step(Dt, GlassScriptTests.Hand(0.5f, 0.4f));
            Assert.Equal(new[] { "near 1 [] 0.00 true", "seen 1 [] 0.00 true 0.50 0.40" }, Words(g));
            // near, showing scissors, moving
            g.Step(Dt, Seen(0.7f, 0.6f, Scissors, 0.31f, near: true));
            Assert.Equal(new[] { "near 1 [scissors] 0.31 true", "seen 1 [scissors] 0.31 true 0.70 0.60" }, Words(g));
            // held up away from the field: not in hands() - as before - but in all_hands(), near = false
            g.Step(Dt, Seen(0.2f, 0.9f, Rock, 0.02f));
            Assert.Equal(new[] { "seen 1 [rock] 0.02 false 0.20 0.90" }, Words(g));
            Assert.DoesNotContain(g.sprites, s => s.art == GlassArt.Disc);
            // both hands, the left one (id 2) near
            var both = Seen(1.2f, 0.5f, Paper, 1.5f);
            both.seen[1] = true; both.valid[1] = true; both.x[1] = 0.3f; both.y[1] = 0.3f; both.dist[1] = 0.05f; both.fingers[1] = Point; both.speed[1] = 0f;
            g.Step(Dt, both);
            Assert.Equal(new[] { "near 2 [point] 0.00 true", "seen 1 [paper] 1.50 false 1.20 0.50", "seen 2 [point] 0.00 true 0.30 0.30" }, Words(g));
            // nonsense from a renderer: not a hand, not a speed
            var bad = Seen(float.NaN, 0.5f, Paper, float.NaN);
            g.Step(Dt, bad);
            Assert.Empty(Words(g));
            bad = Seen(0.5f, 0.5f, Unclear, float.NaN);
            g.Step(Dt, bad);
            Assert.Equal(new[] { "seen 1 [] 0.00 false 0.50 0.50" }, Words(g));
            Assert.Equal("", g.Error); Assert.Empty(g.Box.Notes);
            Assert.True(g.ReadHands);
            // when the game is over nobody is listed any more
            var over = GlassScriptTests.Game("function update(dt) finish('done') text(#all_hands() .. ' hands', 0.5, 0.5, 0.05) end");
            GlassScriptTests.SkipIntro(over);
            for (int i = 0; i < 3; i++) over.Step(Dt, Seen(0.5f, 0.5f, Paper));
            Assert.Contains("0 hands", Words(over));
        }

        [Fact]
        public void The_three_hand_signs_are_pictures_and_a_shape_is_the_name_of_its_picture()
        {
            foreach (string art in new[] { GlassArt.Rock, GlassArt.PaperSign, GlassArt.Scissors }) { Assert.Contains(art, GlassArt.All); Assert.Contains(art, GlassScript.Stickers); }
            Assert.DoesNotContain(GlassArt.Paper, GlassArt.All);               // ("paper" stays the UI kit's box: the sign has a name of its own)
            Assert.Equal(GlassArt.All.Length, GlassArt.All.Distinct().Count());
            var g = GlassScriptTests.Game(@"function update(dt)
  pic('rock', 0.3, 0.5, 0.3)  pic('paper', 0.8, 0.5, 0.3)  pic('scissors', 1.3, 0.5, 0.3, 0, 1, true)  pic('paper-sign', 0.8, 0.2, 0.1)
  for _, h in ipairs(all_hands()) do if h.shape ~= '' and h.shape ~= 'point' then pic(h.shape, h.x, h.y, 0.2) end end
end", strict: true);
            GlassScriptTests.SkipIntro(g);
            g.Step(Dt, Seen(0.5f, 0.8f, Paper));
            Assert.Equal(new[] { GlassArt.Rock, GlassArt.PaperSign, GlassArt.Scissors, GlassArt.PaperSign, GlassArt.PaperSign }, g.sprites.Select(s => s.art));
            Assert.Equal(1, g.sprites[2].frame);                                // (mirrored: the other player's hand)
            Assert.Equal("", g.Error); Assert.Empty(g.Box.Notes);
            // the pictures are in the project for both renderers (the scene builder asks for one for every name in All)
            string ui = Path.Combine(Repo(), "MetaVRHack", "Assets", "LivingPartner", "Art", "Ui");
            foreach (string art in new[] { GlassArt.Rock, GlassArt.PaperSign, GlassArt.Scissors }) Assert.True(File.Exists(Path.Combine(ui, "glass-" + art + ".png")), "glass-" + art + ".png");
        }

        [Fact]
        public void The_checkers_bot_shows_every_sign_moves_and_stands_still_and_is_sometimes_away_from_the_field()
        {
            // the script keeps what it has seen as bits of the score
            const string lua = @"-- records what the hands were
local seen = {}
local function mark(n) if not seen[n] then seen[n] = true  add(n) end end
function update(dt)
  for _, h in ipairs(all_hands()) do
    if h.shape == 'rock' then mark(1) elseif h.shape == 'paper' then mark(2) elseif h.shape == 'scissors' then mark(4) elseif h.shape == 'point' then mark(8) else mark(16) end
    if h.speed == 0 then mark(32) end
    if h.speed > 0.15 then mark(64) end
    if not h.near then mark(128) end
    if h.near and h.speed == 0 and h.shape ~= '' then mark(256) end
    disc(h.x, h.y, 0.05, h.near and 'mint' or 'pink')
  end
  for _, h in ipairs(hands()) do
    if h.shape ~= '' and h.speed >= 0 then mark(512) end
  end
end
";
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var r = GlassScriptCheck.Run(lua, null, 40f, seed);
                Assert.True(r.ok, r.Summary());
                Assert.True(r.score == 1023, "seed " + seed + ": the bot's hand was not all of it: " + r.score);
                Assert.Equal(0, r.scoreAlone);                                  // (with nobody playing there is no hand at all)
                Assert.True(r.readHands && r.reacts);
            }
            Assert.Contains(-1, GlassScriptCheck.BotShows);
            foreach (string s in new[] { "rock", "paper", "scissors", "point", "" }) Assert.Contains(s, GlassScriptCheck.BotShows.Select(GlassGames.Shape));
        }

        [Fact]
        public void A_hand_report_carries_what_the_hand_shows_through_the_host_and_an_old_report_says_not_known()
        {
            var h = new GlassHand { monitorId = "DISPLAY3", t = 12.5 };
            h.hands[0] = new GlassHandPoint { valid = false, seen = true, u = 0.4f, v = 0.6f, dist = 0.7f, fingers = 6, speed = 0.21f };
            h.hands[1] = new GlassHandPoint { valid = true, seen = true, u = 0.2f, v = 0.3f, dist = 0.1f, fingers = 0, speed = 0f };
            var back = Proto.FromJson<GlassHand>(Proto.ToJson(h));              // (the host reads the body and passes it on)
            Assert.Equal((false, true, 6, 0.21f, 0.7f), (back.hands[0].valid, back.hands[0].seen, back.hands[0].fingers, back.hands[0].speed, back.hands[0].dist));
            Assert.Equal((true, true, 0, 0f), (back.hands[1].valid, back.hands[1].seen, back.hands[1].fingers, back.hands[1].speed));
            Assert.Equal("scissors", GlassGames.Shape(back.hands[0].fingers)); Assert.Equal("rock", GlassGames.Shape(back.hands[1].fingers));
            // a headset from before: no such fields - the fingers are not known (never a fist), the hand is not "seen" unless valid
            var old = Proto.FromJson<GlassHand>("{\"monitorId\":\"DISPLAY3\",\"t\":1,\"hands\":[{\"valid\":true,\"u\":0.5,\"v\":0.5,\"dist\":0.1,\"spread\":1,\"roll\":0,\"pinch\":false},{\"valid\":false}]}");
            Assert.Equal((-1, 0f, false), (old.hands[0].fingers, old.hands[0].speed, old.hands[0].seen));
            Assert.Equal("", GlassGames.Shape(old.hands[0].fingers));
        }

        // ------------------------------------------------------------------ rock paper scissors

        static readonly string[] SignArts = { GlassArt.Rock, GlassArt.PaperSign, GlassArt.Scissors };
        static readonly int[] SignFingers = { Rock, Paper, Scissors };
        static readonly string[] RoundLines = { "A draw!", "You win this one!", "Bambi wins this one!", "Show me your hand!" };

        sealed class RpsRound { public int bambi, you; public string line = ""; public float at; }

        /// <summary>
        /// Plays a match. show(k): the finger bits of the hand in the round with the number k (from 0; int.MinValue = no hand
        /// at all). The hand is held up AWAY from the field. Returns every round: Bambi's sign, the one read (-1 = none), the line.
        /// </summary>
        static List<RpsRound> PlayRps(GlassScript g, Func<int, int> show, float seconds, bool near = false)
        {
            var rounds = new List<RpsRound>();
            bool inResult = false;
            for (int f = 0; f < seconds / Dt && g.phase != "over"; f++)
            {
                int fingers = show(rounds.Count);
                Step(g, fingers == int.MinValue ? null : Seen(g.W * 0.4f, 0.7f, fingers, 0.02f, near));
                string line = RoundLines.FirstOrDefault(l => Says(g, l)) ?? (g.phase == "over" ? "over" : null);
                if (line == null) { inResult = false; continue; }
                if (inResult) continue;
                inResult = true;
                if (line == "over") continue;
                var r = new RpsRound { line = line, at = g.seconds, bambi = -1, you = -1 };
                foreach (var s in g.sprites)
                {
                    int k = Array.IndexOf(SignArts, s.art);
                    if (k < 0) continue;
                    if (s.x > g.W * 0.5f) { Assert.Equal(-1, r.bambi); r.bambi = k; Assert.Equal(1, s.frame); } else { Assert.Equal(-1, r.you); r.you = k; }
                }
                Assert.InRange(r.bambi, 0, 2);                                  // Bambi's sign is shown, big
                rounds.Add(r);
            }
            return rounds;
        }

        /// <summary>0 = a draw, 1 = a beats b, 2 = b beats a (paper beats rock, scissors beat paper, rock beats scissors).</summary>
        static int Beats(int a, int b) => a == b ? 0 : (a - b + 3) % 3 == 1 ? 1 : 2;

        [Fact]
        public void Rps_the_rules_are_the_rules_and_Bambis_choice_does_not_depend_on_the_hand()
        {
            foreach (int seed in new[] { 1, 2, 3, 4, 5 })
            {
                var all = new List<List<RpsRound>>();
                for (int mine = 0; mine < 3; mine++)
                {
                    var g = Start("rps", seed);
                    Assert.Equal(3, g.lives);
                    int sign = mine;
                    var rounds = PlayRps(g, k => SignFingers[sign], 200f);
                    all.Add(rounds);
                    Assert.Equal("over", g.phase);                              // (always the same sign: somebody gets to three)
                    int you = 0, bambi = 0;
                    foreach (var r in rounds)
                    {
                        Assert.Equal(mine, r.you);                              // what the hand showed is what was read, and shown
                        int w = Beats(mine, r.bambi);
                        Assert.Equal(RoundLines[w], r.line);
                        if (w == 1) you++; else if (w == 2) bambi++;
                    }
                    Assert.True(you == 3 || bambi == 3); Assert.False(you == 3 && bambi == 3);
                    Assert.Equal(you, g.score); Assert.Equal(3 - bambi, g.lives);
                    Assert.Equal((you == 3 ? "You win the match! " : "Bambi wins the match! ") + you + " : " + bambi, g.result);
                    Assert.True(rounds[0].at >= 5f, "the first round was decided after " + rounds[0].at + " s");      // (nothing is lost in the first five seconds)
                }
                // NO CHEATING: round for round Bambi showed the same sign, whatever the player showed
                int n = all.Min(r => r.Count);
                Assert.True(n >= 3);
                for (int k = 0; k < n; k++) { Assert.Equal(all[0][k].bambi, all[1][k].bambi); Assert.Equal(all[0][k].bambi, all[2][k].bambi); }
                // ... and over many rounds it shows all three
                o.WriteLine("seed " + seed + ": Bambi showed " + string.Join(" ", all[0].Select(r => SignArts[r.bambi])));
            }
            var shown = new HashSet<int>();
            for (int seed = 1; seed <= 12; seed++) foreach (var r in PlayRps(Start("rps", seed), k => Rock, 200f)) shown.Add(r.bambi);
            Assert.Equal(3, shown.Count);
        }

        [Fact]
        public void Rps_the_right_shape_wins_the_wrong_one_loses_and_the_same_one_draws()
        {
            foreach (int seed in new[] { 1, 7 })
            {
                // what Bambi will show, round by round (it does not depend on the hand: the test above)
                var bambi = new List<int>();
                for (int s = seed; bambi.Count < 12; ) { foreach (var r in PlayRps(Start("rps", seed), k => SignFingers[(k + bambi.Count) % 3], 300f)) bambi.Add(r.bambi); if (bambi.Count >= 3) break; }
                var again = PlayRps(Start("rps", seed), k => Paper, 300f).Select(r => r.bambi).ToList();
                var known = again.Count > bambi.Count ? again : bambi;

                // the shape that beats it, every round - held up AWAY from the field: 3 : 0
                var g = Start("rps", seed);
                var rounds = PlayRps(g, k => SignFingers[(known[Math.Min(k, known.Count - 1)] + 1) % 3], 100f);
                Assert.Equal(3, rounds.Count); Assert.All(rounds, r => Assert.Equal("You win this one!", r.line));
                Assert.Equal("You win the match! 3 : 0", g.result); Assert.Equal((3, 3), (g.score, g.lives));
                Assert.Equal("cheer", g.petPose);

                // the shape that loses to it: 0 : 3, and the hearts are gone
                g = Start("rps", seed);
                rounds = PlayRps(g, k => SignFingers[(known[Math.Min(k, known.Count - 1)] + 2) % 3], 100f, near: true);
                Assert.Equal(3, rounds.Count); Assert.All(rounds, r => Assert.Equal("Bambi wins this one!", r.line));
                Assert.Equal("Bambi wins the match! 0 : 3", g.result); Assert.Equal((0, 0), (g.score, g.lives));

                // the same shape: draws, for as long as it is kept up - nobody scores
                g = Start("rps", seed);
                rounds = PlayRps(g, k => SignFingers[known[Math.Min(k, known.Count - 1)]], 5.2f * Math.Min(known.Count, 4) + 3f);
                Assert.True(rounds.Count >= 3); Assert.All(rounds, r => Assert.Equal("A draw!", r.line));
                Assert.Equal(("play", 0, 3), (g.phase, g.score, g.lives));
            }
        }

        [Fact]
        public void Rps_no_hand_or_an_unclear_one_replays_the_round_and_one_finger_is_scissors()
        {
            // nobody holds a hand up: "Show me your hand!", again and again - no score, no heart lost, never over
            var g = Start("rps", 3);
            var rounds = PlayRps(g, k => int.MinValue, 40f);
            Assert.True(rounds.Count >= 5); Assert.All(rounds, r => { Assert.Equal("Show me your hand!", r.line); Assert.Equal(-1, r.you); });
            Assert.Equal(("play", 0, 3), (g.phase, g.score, g.lives));
            // a hand that shows nothing that can be told (index and ring; or the fingers are not known)
            foreach (int unclear in new[] { Unclear, Unknown })
            {
                g = Start("rps", 3);
                rounds = PlayRps(g, k => unclear, 30f);
                Assert.True(rounds.Count >= 4); Assert.All(rounds, r => Assert.Equal("Show me your hand!", r.line));
                Assert.Equal(("play", 0, 3), (g.phase, g.score, g.lives));
            }
            // one finger out is read as scissors (the second finger is the one the cameras miss)
            g = Start("rps", 3);
            rounds = PlayRps(g, k => Point, 200f);
            Assert.All(rounds, r => { Assert.Equal(2, r.you); Assert.Equal(RoundLines[Beats(2, r.bambi)], r.line); });
            Assert.Equal("over", g.phase);
            // the hand changes its mind during the count: what it shows at "Shoot!" counts - here mostly paper, with a flicker of rock
            g = Start("rps", 3);
            int frame = 0;
            rounds = PlayRps(g, k => frame++ % 7 == 0 ? Rock : Paper, 200f);
            Assert.All(rounds, r => Assert.Equal(1, r.you));
            // the count: its words, one after the other, and Bambi's arm shakes with it (a motion of the script's own)
            g = Start("rps", 3);
            var said = new List<string>(); bool shook = false, held = false;
            for (int f = 0; f < 330; f++)
            {
                Step(g, null);
                string w = new[] { "First to three!", "Rock ...", "Paper ...", "Scissors ...", "Shoot!", "Show me your hand!", "Ready ..." }.FirstOrDefault(x => Says(g, x));
                if (w != null && (said.Count == 0 || said[said.Count - 1] != w)) said.Add(w);
                if (g.PetMotion == "count" && g.petCustom) shook = true;
                if (g.PetMotion == "show" && g.petCustom) held = true;
            }
            Assert.Equal(new[] { "First to three!", "Rock ...", "Paper ...", "Scissors ...", "Shoot!", "Show me your hand!", "Ready ...", "Rock ..." }, said.Take(8));
            Assert.True(shook && held);
        }

        // ------------------------------------------------------------------ star pop

        static List<GlassSprite> Stars(GlassGame g) => g.sprites.Where(s => s.art == GlassArt.Star).ToList();

        [Fact]
        public void Pop_touching_a_star_scores_one_a_golden_one_three_and_a_star_that_gets_away_costs_nothing()
        {
            var g = Start("pop", 1);
            int lives = g.lives, popped = 0, gold = 0;
            var heard = new HashSet<string>();
            // nothing is popped by a hand that is beside the stars, or held up away from the field
            for (int f = 0; f < 150; f++) Step(g, f % 2 == 0 ? Seen(0.02f, 0.98f, Paper, 0f, near: true) : Seen(g.W / 2f, 0.5f, Paper));
            Assert.Equal(0, g.score); Assert.NotEmpty(Stars(g));
            // the hand goes to a star: it pops, and the score says which kind it was
            GlassInput hand = null;
            for (int f = 0; f < 30 * 30 && popped < 14; f++)
            {
                var stars = Stars(g);
                int before = g.score;
                GlassSprite? target = stars.Count > 0 ? stars.OrderByDescending(s => s.y).First() : (GlassSprite?)null;
                hand = target != null ? Seen(target.Value.x, target.Value.y, Paper, 0.3f, near: true) : null;
                Step(g, hand);
                if (g.score == before) continue;
                Assert.NotNull(target);
                bool golden = target.Value.size > 0.13f;
                Assert.Equal(golden ? 3 : 1, g.score - before);
                Assert.Contains(g.sprites, s => s.art == GlassArt.Label && s.text == (golden ? "+3" : "+1"));
                foreach (string sound in g.sounds) heard.Add(sound);
                popped++; if (golden) gold++;
            }
            Assert.Equal(14, popped); Assert.True(gold >= 2, "golden stars popped: " + gold);      // (every sixth is golden)
            Assert.Contains("pop", heard); Assert.Contains("sparkle", heard);
            Assert.Equal(lives, g.lives); Assert.Equal("play", g.phase);
            // a star is at least a tenth of the field across and can be reached for three seconds or more
            var seen = new Dictionary<int, (float y0, float t0)>();
            g = Start("pop", 2);
            float slowest = float.MaxValue, fastest = 0f;
            float lastY = -1f; float lastT = 0f;
            for (int f = 0; f < 38 * 30; f++)
            {
                Step(g, null);
                foreach (var s in Stars(g)) Assert.True(s.size >= 0.10f);
                var top = Stars(g).OrderByDescending(s => s.y).FirstOrDefault();
                if (Stars(g).Count > 0 && lastY >= 0f && top.y > lastY && top.y - lastY < 0.05f)
                {
                    float v = (top.y - lastY) / (g.seconds - lastT);
                    slowest = Math.Min(slowest, v); fastest = Math.Max(fastest, v);
                }
                lastY = Stars(g).Count > 0 ? top.y : -1f; lastT = g.seconds;
            }
            o.WriteLine($"stars rise at {slowest:0.00} .. {fastest:0.00} of the field a second");
            Assert.True(fastest < 0.36f && slowest > 0.1f);                     // (the whole height in three seconds at the fastest)
            Assert.Equal(0, g.score); Assert.Equal(lives, g.lives);              // every star got away: that cost nothing
        }

        [Fact]
        public void Pop_starts_gently_speeds_up_and_ends_at_forty_seconds()
        {
            var g = Start("pop", 4);
            int early = 0, late = 0, was = 0;
            float firstStar = -1f;
            while (g.phase == "play" && g.seconds < 60f)
            {
                Step(g, null);
                int n = Stars(g).Count;
                if (n > was) { if (firstStar < 0f) firstStar = g.seconds; if (g.seconds < 10f) early += n - was; else if (g.seconds >= 30f) late += n - was; }
                was = n;
            }
            Assert.Equal("over", g.phase);
            Assert.InRange(g.seconds, 40f, 40.1f);
            Assert.Equal("Time! You popped 0 points.", g.result);
            Assert.InRange(firstStar, 0.8f, 1.5f);
            o.WriteLine($"stars in the first ten seconds: {early}, in the last ten: {late}");
            Assert.True(early >= 7 && early <= 11 && late >= 16, early + " / " + late);
            Assert.Empty(Stars(g));                                             // (the stars that were left burst when the time was up)
            // with a player who pops what comes: the score is in the last line, and the picture stays
            g = Start("pop", 4);
            while (g.phase == "play" && g.seconds < 60f)
            {
                var stars = Stars(g);
                Step(g, stars.Count > 0 ? Seen(stars[0].x, stars[0].y, Rock, 0.2f, near: true) : null);
            }
            Assert.True(g.score > 40, "score " + g.score);
            Assert.Equal("Time! You popped " + g.score + " points.", g.result);
            for (int f = 0; f < 60; f++) Step(g, null);
            Assert.NotEmpty(g.sprites);
        }

        // ------------------------------------------------------------------ red light

        const float RedStart = 0.3f, RedTrack = 0.14f;
        static float StarX(GlassGame g) => g.sprites.Single(s => s.art == GlassArt.Star).x;
        static string Light(GlassGame g) => SaysStart(g, "Green light") ? "green" : Says(g, "Do not move!") ? "red" : Says(g, "Back to the start!") ? "caught" : g.phase == "over" ? "over" : "ready";

        [Fact]
        public void RedLight_creeping_on_green_and_freezing_on_red_brings_the_star_to_Bambi()
        {
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var g = Start("redlight", seed);
                Assert.Equal(3, g.lives);
                int greens = 0, reds = 0; string was = "ready"; bool backTurned = false, faced = false;
                float lastStar = RedStart;
                while (g.phase == "play" && g.seconds < 90f)
                {
                    string light = Light(g);
                    float star = StarX(g);
                    if (light == "red" && was == "red") Assert.Equal(lastStar, star, 4);     // on red the star does not move
                    if (light != was) { if (light == "green") greens++; if (light == "red") reds++; was = light; }
                    if (light == "green" && g.petCustom && Math.Abs(g.petCustomPose.Euler[(int)Bone.Hips].Y) > 170f) backTurned = true;      // Bambi's back is to the player while it counts
                    if (light == "red" && g.petCustom && Math.Abs(g.petCustomPose.Euler[(int)Bone.Hips].Y) < 5f) faced = true;              // and it has turned round on red
                    lastStar = star;
                    // the hand leads the star, a little ahead of it: creeping on green, still on red
                    Step(g, Seen(star + 0.1f, 0.3f, Paper, light == "green" ? 0.12f : 0f));
                }
                Assert.Equal("over", g.phase); Assert.Equal("You got me!", g.result);
                Assert.Equal(3, g.lives);
                Assert.True(greens >= 2 && reds >= 1, greens + " greens, " + reds + " reds");   // it takes more than one green light
                Assert.True(backTurned && faced);
                Assert.Equal(g.W - 0.48f, StarX(g), 3);
                o.WriteLine($"seed {seed}: won after {g.seconds:0.0} s, {greens} green lights");
            }
        }

        [Fact]
        public void RedLight_a_hand_that_moves_on_red_is_caught_and_goes_back_to_the_start_three_times_and_it_is_over()
        {
            var g = Start("redlight", 2);
            var caughtAt = new List<float>();
            int lives = g.lives; string was = "ready"; float redFor = 0f, best = RedStart;
            while (g.phase == "play" && g.seconds < 120f)
            {
                string light = Light(g);
                float star = StarX(g);
                best = Math.Max(best, star);
                redFor = light == "red" ? redFor + Dt : 0f;
                if (light == "ready" && was == "caught") Assert.Equal(RedStart, star, 3);    // back to the start
                was = light;
                // the hand leads the star and never stops: 0.3 m/s whatever the light
                Step(g, Seen(star + 0.1f, 0.3f, Paper, 0.3f));
                if (g.lives < lives)
                {
                    lives = g.lives; caughtAt.Add(g.seconds);
                    Assert.Equal("red", light);                                             // only ever on red
                    Assert.InRange(redFor, 0.55f, 0.75f);                                   // after the moment to stop (0.4 s) and 0.2 s of moving
                    Assert.Equal("You moved!", g.banner);
                }
            }
            Assert.Equal(3, caughtAt.Count);
            Assert.True(caughtAt[0] >= 5f, "the first catch came after " + caughtAt[0] + " s");
            Assert.Equal(("over", 0, "Caught three times - Bambi wins!"), (g.phase, g.lives, g.result));
            Assert.True(best > RedStart + 0.2f && best < g.W - 0.48f);                       // (it had got somewhere, and never arrived)
        }

        [Fact]
        public void RedLight_a_twitch_is_forgiven_a_hand_that_runs_ahead_leaves_the_star_behind_and_nobody_playing_loses_nothing()
        {
            // a twitch on red (0.15 s of moving, well after the moment to stop) is not a catch; neither is a slow drift under 0.10 m/s (the threshold since 2026-11-18: 0.15 was the pace of the star itself)
            var g = Start("redlight", 1);
            float redFor = 0f; int reds = 0; string was = "";
            while (g.phase == "play" && g.seconds < 90f)
            {
                string light = Light(g);
                float star = StarX(g);
                redFor = light == "red" ? redFor + Dt : 0f;
                if (light == "red" && was != "red") reds++;
                was = light;
                float speed = light == "green" ? 0.4f : redFor > 0.6f && redFor < 0.75f ? 0.9f : light == "red" ? 0.08f : 0f;
                Step(g, Seen(star + 0.1f, 0.3f, Rock, speed));
            }
            Assert.Equal(("over", 3, "You got me!"), (g.phase, g.lives, g.result));
            Assert.True(reds >= 1);

            // a hand that runs ahead to Bambi does not take the star along: the star only creeps, and only under a hand
            g = Start("redlight", 1);
            float most = RedStart;
            for (int f = 0; f < 30 * 30 && g.phase == "play"; f++)
            {
                bool green = Light(g) == "green";
                Step(g, green ? Seen(g.W - 0.4f, 0.2f, Paper, 0f, near: true) : Seen(RedStart, 0.3f, Paper, 0f));
                most = Math.Max(most, StarX(g));
            }
            Assert.Equal("play", g.phase); Assert.Equal(RedStart, most, 3);
            // ... and under a hand it creeps: never faster than 0.42 of the field a second
            g = Start("redlight", 1);
            float lastX = RedStart;
            for (int f = 0; f < 30 * 20 && g.phase == "play"; f++)
            {
                Step(g, Seen(Math.Min(StarX(g) + 0.2f, g.W), 0.3f, Paper, 0f));
                Assert.True(StarX(g) - lastX <= 0.42f * Dt + 1e-4f);
                lastX = StarX(g);
            }
            Assert.True(lastX > RedStart + 0.3f);

            // nobody plays: Bambi waits for a hand - no light turns, nothing is lost, it does not end
            g = Start("redlight", 1);
            for (int f = 0; f < 30 * 40; f++) { Step(g, null); Assert.Equal("ready", Light(g)); }
            Assert.Equal(("play", 3), (g.phase, g.lives));
            Assert.True(Says(g, "Hold a hand up where I can see it"));
            // a hand that is seen but not on the star is told where to put it
            Step(g, Seen(g.W - 0.2f, 0.5f, Paper));
            Assert.True(Says(g, "Put your hand on the star"));
        }

        // ------------------------------------------------------------------ what ships

        [Fact]
        public void Old_example_scripts_still_execute_without_being_listed()
        {
            foreach (var key in new[] { "rps", "pop", "redlight" })
            {
                var example = GlassScriptExamples.Find(key);
                Assert.NotNull(example);
                var pack = GlassScriptExamples.Pack(example);
                Assert.True(ScriptRules.CheckGame(pack.script).Clean);
                var game = GlassGames.Create(pack.template);
                game.Start(pack, 1); Assert.Equal("intro", game.phase); Assert.Equal(pack.howTo, game.banner);
                Assert.DoesNotContain(MarketSeed.All(), s => s.module.id == "game-" + key);
            }
        }

        [Fact]
        public void Previous_hand_games_are_retained_in_the_archive_and_are_no_longer_offered()
        {
            var s = new MarketState();
            var w = new WorldState();
            foreach (var key in new[] { "rps", "pop", "redlight" })
            {
                var pack = GlassScriptExamples.Pack(GlassScriptExamples.Find(key));
                pack.id = "game-" + key;
                s.catalogue.Add(new MarketModule { id = pack.id, name = pack.title, kind = ModuleKinds.Game, game = pack, builtin = true });
                s.installed.Add(pack.id); w.games.Add(pack);
            }
            MarketRules.Merge(s, MarketSeed.All()); MarketRules.Sync(w,s);
            Assert.Equal(3,s.archivedGames.Count);
            foreach (var old in s.archivedGames)
            {
                Assert.True(old.installed); Assert.NotEmpty(old.module.game.script);
                var pick = GlassRules.PickGame(w.games, "", "", old.module.name);
                Assert.False(pick.Found); Assert.Empty(pick.offer);
            }
            string first = Proto.ToJson(s);
            MarketRules.Merge(s, MarketSeed.All()); Assert.Equal(first, Proto.ToJson(s));
        }
    }
}
