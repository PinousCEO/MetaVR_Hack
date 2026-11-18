using LivingPartner.Shared;
using Xunit;
using Xunit.Abstractions;

namespace LivingPartner.Tests
{
    /// <summary>
    /// The example scripts: each passes the check without a warning, survives five minutes of random hands, and is
    /// PLAYED here by a bot that means it (the omok player makes legal moves until somebody wins, and a referee
    /// watches whether Nilo blocks; the memory player remembers; the tag player chases).
    /// Everything is driven the way a renderer drives a game - hands in, sprites out. Nothing is exposed for tests.
    /// </summary>
    public class GlassScriptExampleTests
    {
        readonly ITestOutputHelper o;
        public GlassScriptExampleTests(ITestOutputHelper o) { this.o = o; }

        const float Dt = 1f / 30f;

        static GlassScript Start(string id, int seed, bool touch, string lua = null)
        {
            var e = GlassScriptExamples.Find(id);
            var pack = GlassScriptExamples.Pack(e);
            if (lua != null) pack.script = lua;
            var g = new GlassScript { touch = touch };
            g.Start(pack, seed);
            Assert.True(g.Error == "", id + ": " + g.Error);
            GlassScriptTests.SkipIntro(g);
            return g;
        }

        static void Step(GlassScript g, GlassInput input, int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                g.Step(Dt, input);
                Assert.True(g.Error == "", g.Error);
            }
        }

        /// <summary>A fingertip comes down on (x, y) and goes up again (the desk).</summary>
        static void Tap(GlassScript g, float x, float y)
        {
            Step(g, GlassScriptTests.Hand(x, y, 0.06f));
            Step(g, GlassScriptTests.Hand(x, y, 0.004f));
            Step(g, GlassScriptTests.Hand(x, y, 0.06f));
        }

        public static IEnumerable<object[]> Ids() { foreach (var e in GlassScriptExamples.All) yield return new object[] { e.id }; }

        [Theory]
        [MemberData(nameof(Ids))]
        public void Every_example_passes_the_check_without_a_warning(string id)
        {
            var e = GlassScriptExamples.Find(id);
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var r = GlassScriptCheck.Run(e.lua, GlassScriptExamples.Pack(e), 40f, seed);
                o.WriteLine("seed " + seed + ": " + r.Summary());
                Assert.True(r.ok, r.Summary());
                Assert.True(r.Clean, string.Join(" | ", r.warnings));
                Assert.True(r.drew && r.pictureChanged && r.reacts);
                Assert.True(r.peakShare < GlassScriptCheck.HeavyPeak && r.averageInstructions < GlassScriptCheck.HeavyAverage);
            }
            // the shape the writer is to imitate: short, commented, with a request in both languages
            int lines = e.lua.Split('\n').Length - 1;
            Assert.InRange(lines, 55, e.id is "survival" or "shooter" or "keeper" ? 250 : 200);
            Assert.StartsWith("-- ", e.lua);
            Assert.True(e.title.Length > 0 && e.requestKo.Length > 0 && e.requestEn.Length > 0 && e.about.Length > 0 && e.howTo.Length > 0);
            Assert.DoesNotContain("\r", e.lua);
            Assert.DoesNotContain("\t", e.lua);
            Assert.Equal(e.flat, GlassScriptCheck.Run(e.lua).flat);
        }

        [Fact]
        public void The_examples_are_what_was_asked_for()
        {
            Assert.Equal(new[] { "omok", "catch", "memory", "pomodoro", "tag" }, GlassScriptExamples.All.Take(5).Select(e => e.id));
            Assert.Equal(GlassScriptExamples.All.Length, GlassScriptExamples.All.Select(e => e.id).Distinct().Count());
            // omok is NOT for the shelf: the partner has to make it when it is asked for. It is a test case and an example for the writer.
            Assert.False(GlassScriptExamples.Find("omok").ship);
            Assert.Contains("오목", GlassScriptExamples.Find("omok").requestKo);
            Assert.True(GlassScriptExamples.Find("omok").flat && GlassScriptExamples.Find("memory").flat && GlassScriptExamples.Find("tag").flat);
            Assert.False(GlassScriptExamples.Find("catch").flat); Assert.False(GlassScriptExamples.Find("pomodoro").flat);
            foreach (var e in GlassScriptExamples.All)
            {
                var p = GlassScriptExamples.Pack(e);
                Assert.Equal("script", p.template); Assert.Equal(e.lua, p.script); Assert.Equal(e.title, p.title); Assert.Equal(e.howTo, p.howTo);
                var game = GlassGames.Create(p.template);
                game.Start(p, 1);
                Assert.Equal("intro", game.phase); Assert.Equal(e.howTo, game.banner); Assert.Equal(e.flat, game.flat);
            }
        }

        [Theory]
        [MemberData(nameof(Ids))]
        public void Five_minutes_of_random_hands_break_nothing(string id)
        {
            foreach (bool touch in new[] { false, true })
            {
                var rng = new GlassRng(); rng.Seed(id.Length * 31 + (touch ? 1 : 0));
                int seed = 1, rounds = 0, maxSprites = 0;
                var g = Start(id, seed, touch);
                var input = new GlassInput();
                float[] vx = new float[2], vy = new float[2];
                int overFrames = 0;
                for (int f = 0; f < 9000; f++)
                {
                    for (int h = 0; h < 2; h++)
                    {
                        if (rng.Chance(0.02f)) input.valid[h] = rng.Chance(h == 0 ? 0.85f : 0.4f);
                        if (rng.Chance(0.08f)) { vx[h] = rng.Range(-1.5f, 1.5f); vy[h] = rng.Range(-1.5f, 1.5f); }
                        if (rng.Chance(0.03f)) { vx[h] = 0f; vy[h] = 0f; }                                  // it rests now and then
                        if (rng.Chance(0.01f)) { input.x[h] = rng.Range(-0.3f, g.W + 0.3f); input.y[h] = rng.Range(-0.3f, 1.3f); }
                        input.x[h] = Math.Clamp(input.x[h] + vx[h] * Dt, -0.4f, g.W + 0.4f); input.y[h] = Math.Clamp(input.y[h] + vy[h] * Dt, -0.4f, 1.4f);
                        if (rng.Chance(0.1f)) input.dist[h] = rng.Chance(0.5f) ? rng.Range(0f, 0.03f) : rng.Range(0f, 0.4f);
                        input.spread[h] = rng.Next01();
                    }
                    g.Step(rng.Chance(0.05f) ? rng.Range(0.001f, 0.2f) : Dt, input);
                    Assert.True(g.Error == "", id + " frame " + f + ": " + g.Error);
                    GlassScriptTests.AssertSane(g);
                    if (g.petCustom) for (int b = 0; b < Skeleton.N; b++) Assert.False(float.IsNaN(g.petCustomPose.Euler[b].X + g.petCustomPose.Euler[b].Y + g.petCustomPose.Euler[b].Z));
                    maxSprites = Math.Max(maxSprites, g.sprites.Count);
                    if (g.phase == "over" && ++overFrames > 60)                                              // a new round
                    {
                        Assert.NotEqual("", g.result);
                        rounds++; overFrames = 0;
                        g = Start(id, ++seed, touch);
                    }
                }
                Assert.Empty(g.Box.Notes);
                Assert.Equal(0, g.Dropped);
                o.WriteLine($"{id} touch={touch}: {rounds} rounds finished, at most {maxSprites} sprites, busiest update {g.PeakUpdate} steps, final score {g.score}");
            }
        }

        // ------------------------------------------------------------------ omok

        const int N = 9;
        const float Cell = 0.1f, BoardY0 = 0.05f;
        static float BoardX0(GlassGame g) { return g.W / 2f - 0.45f; }
        static float CellX(GlassGame g, int c) { return BoardX0(g) + (c + 0.5f) * Cell; }
        static float CellY(int r) { return BoardY0 + (r + 0.5f) * Cell; }

        /// <summary>The board as the picture shows it: 0 empty, 1 the player's stone, 2 Nilo's. Index = row * 9 + col (0-based).</summary>
        static int[] ReadBoard(GlassScript g)
        {
            var b = new int[N * N];
            foreach (var s in g.sprites)
            {
                int who = s.art == GlassArt.StoneA ? 1 : s.art == GlassArt.StoneB ? 2 : 0;
                if (who == 0) continue;
                int c = (int)Math.Floor((s.x - BoardX0(g)) / Cell), r = (int)Math.Floor((s.y - BoardY0) / Cell);
                Assert.InRange(c, 0, N - 1); Assert.InRange(r, 0, N - 1);
                Assert.Equal(CellX(g, c), s.x, 3); Assert.Equal(CellY(r), s.y, 3);       // a stone sits in the middle of its cell
                Assert.Equal(0, b[r * N + c]);
                b[r * N + c] = who;
            }
            return b;
        }

        static readonly (int dc, int dr)[] Dirs = { (1, 0), (0, 1), (1, 1), (1, -1) };

        static int Run(int[] b, int c, int r, int dc, int dr, int who)
        {
            int n = 0;
            for (int k = 1; k < 9; k++) { int x = c + dc * k, y = r + dr * k; if (x < 0 || x >= N || y < 0 || y >= N || b[y * N + x] != who) break; n++; }
            return n;
        }

        /// <summary>The empty cells where `who` would have five in a row at once.</summary>
        static List<int> WinningCells(int[] b, int who)
        {
            var list = new List<int>();
            for (int i = 0; i < N * N; i++)
            {
                if (b[i] != 0) continue;
                int c = i % N, r = i / N;
                foreach (var (dc, dr) in Dirs)
                    if (1 + Run(b, c, r, dc, dr, who) + Run(b, c, r, -dc, -dr, who) >= 5) { list.Add(i); break; }
            }
            return list;
        }

        static bool HasFive(int[] b, int who)
        {
            for (int i = 0; i < N * N; i++)
            {
                if (b[i] != who) continue;
                foreach (var (dc, dr) in Dirs) if (1 + Run(b, i % N, i / N, dc, dr, who) >= 5) return true;
            }
            return false;
        }

        /// <summary>The player's stone goes to cell i (a tap), then the game runs until Nilo has answered or it is over.</summary>
        static int[] Move(GlassScript g, int i, out int bambi)
        {
            int[] before = ReadBoard(g);
            Assert.Equal(0, before[i]);
            Tap(g, CellX(g, i % N), CellY(i / N));
            int[] after = ReadBoard(g);
            Assert.Equal(1, after[i]);                                   // the stone is there at once
            bambi = -1;
            for (int f = 0; f < 90 && g.phase != "over"; f++)
            {
                Step(g, null);
                after = ReadBoard(g);
                if (after.Count(v => v == 2) > before.Count(v => v == 2)) break;
            }
            for (int k = 0; k < N * N; k++) if (after[k] == 2 && before[k] != 2) bambi = k;
            return after;
        }

        [Fact]
        public void Omok_a_whole_game_with_legal_moves_ends_with_a_winner_and_its_line()
        {
            int bambiWins = 0, playerWins = 0, draws = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var g = Start("omok", seed, touch: true);
                Assert.True(g.flat);
                var rng = new GlassRng(); rng.Seed(seed * 17);
                int[] b = ReadBoard(g);
                Assert.All(b, v => Assert.Equal(0, v));
                int moves = 0;
                while (g.phase != "over")
                {
                    var free = Enumerable.Range(0, N * N).Where(i => b[i] == 0).ToList();
                    Assert.NotEmpty(free);
                    b = Move(g, free[rng.Int(free.Count)], out int reply);
                    moves++;
                    int mine = b.Count(v => v == 1), its = b.Count(v => v == 2);
                    Assert.Equal(moves, mine);
                    Assert.True(its == mine || (its == mine - 1 && g.phase == "over"), "Nilo answers every stone with exactly one");
                    Assert.True(moves <= 41);
                }
                Step(g, null, 120);                                  // (Nilo finishes its walk)
                b = ReadBoard(g);
                bool p = HasFive(b, 1), q = HasFive(b, 2);
                Assert.False(p && q);
                if (q) { bambiWins++; Assert.Contains("Nilo wins", g.result); Assert.Equal(0, g.score); Assert.Equal("cheer", g.petPose); }
                else if (p) { playerWins++; Assert.Contains("You win", g.result); Assert.Equal(1, g.score); Assert.Equal("down", g.petPose); }
                else { draws++; Assert.Contains("draw", g.result); }
                if (p || q)
                {
                    // the winning line is shown: five rings on five of the winner's stones, in one line
                    var rings = g.sprites.Where(s => s.art == GlassArt.RingShape && s.color == 0xE2574C).ToList();
                    Assert.Equal(5, rings.Count);
                    var cells = rings.Select(s => ((int)Math.Floor((s.x - BoardX0(g)) / Cell), (int)Math.Floor((s.y - BoardY0) / Cell))).OrderBy(t => t.Item1).ThenBy(t => t.Item2).ToList();
                    foreach (var (c, r) in cells) Assert.Equal(q ? 2 : 1, b[r * N + c]);
                    int dc = cells[1].Item1 - cells[0].Item1, dr = cells[1].Item2 - cells[0].Item2;
                    for (int k = 1; k < 5; k++) Assert.Equal((cells[0].Item1 + dc * k, cells[0].Item2 + dr * k), cells[k]);
                    Assert.Equal(5, g.sprites.Count(s => s.art == GlassArt.Sparkle));
                }
                // it is over: a tap does nothing any more
                int stones = b.Count(v => v != 0);
                int freeCell = Array.IndexOf(b, 0);
                if (freeCell >= 0) Tap(g, CellX(g, freeCell % N), CellY(freeCell / N));
                Assert.Equal(stones, ReadBoard(g).Count(v => v != 0));
            }
            o.WriteLine($"against random legal moves: Nilo won {bambiWins}, the player {playerWins}, draws {draws}");
            Assert.Equal(6, bambiWins);                                  // somebody who plays at random loses every time
        }

        [Fact]
        public void Omok_Bambi_takes_its_win_and_blocks_a_four()
        {
            int blocked = 0, mustBlock = 0, tookWin = 0, couldWin = 0, playerWins = 0, games = 0, blockedThrees = 0, openThrees = 0;
            for (int seed = 1; seed <= 30; seed++)
            {
                var g = Start("omok", seed, touch: true);
                var rng = new GlassRng(); rng.Seed(seed * 101);
                int[] b = ReadBoard(g);
                games++;
                while (g.phase != "over")
                {
                    // the attacker: finish five when it can; otherwise lengthen its longest line that still has room for five
                    int pick = -1, best = -1;
                    var wins = WinningCells(b, 1);
                    var danger = WinningCells(b, 2);
                    if (wins.Count > 0) pick = wins[0];
                    else if (danger.Count > 0) pick = danger[0];       // (it defends too, or the games would be over before it has built anything)
                    else if (FourMaker(b, rng) is int four && four >= 0) pick = four;      // a stone that threatens five at once
                    else
                        for (int i = 0; i < N * N; i++)
                        {
                            if (b[i] != 0) continue;
                            int c = i % N, r = i / N, score = rng.Int(3);
                            foreach (var (dc, dr) in Dirs)
                            {
                                int a = Run(b, c, r, dc, dr, 1), z = Run(b, c, r, -dc, -dr, 1);
                                int room = 1 + a + z;
                                for (int k = a + 1; k < 5; k++) { int x = c + dc * k, y = r + dr * k; if (x < 0 || x >= N || y < 0 || y >= N || b[y * N + x] == 2) break; room++; }
                                for (int k = z + 1; k < 5; k++) { int x = c - dc * k, y = r - dr * k; if (x < 0 || x >= N || y < 0 || y >= N || b[y * N + x] == 2) break; room++; }
                                if (room >= 5) score = Math.Max(score, (a + z) * 10 + rng.Int(3));
                            }
                            if (score > best) { best = score; pick = i; }
                        }
                    var bambiCould = WinningCells(b, 2);              // what Nilo could finish on its coming move (after the player's stone, if that does not take the cell)
                    int[] after = Move(g, pick, out int reply);
                    var threat = WinningCells(Place(b, pick, 1), 1);  // where the player would win next
                    bool playerHasFive = HasFive(Place(b, pick, 1), 1);
                    bambiCould.Remove(pick);
                    if (!playerHasFive)
                    {
                        Assert.True(reply >= 0, "Nilo did not answer");
                        if (bambiCould.Count > 0)
                        {
                            couldWin++;
                            Assert.Contains(reply, bambiCould);         // win > block: it finishes its own five
                            Assert.Equal("over", g.phase);
                            tookWin++;
                        }
                        else if (threat.Count >= 1)
                        {
                            mustBlock++;
                            Assert.Contains(reply, threat);             // a four is blocked (when there are two ends, one of them: that game is lost anyway)
                            blocked++;
                        }
                        // an open three (three in a row, both ends free with room) should be answered next to it
                        if (bambiCould.Count == 0 && threat.Count == 0 && IsOpenThreeMove(Place(b, pick, 1), pick, out var ends))
                        {
                            openThrees++;
                            if (ends.Contains(reply)) blockedThrees++;
                        }
                    }
                    b = after;
                }
                if (HasFive(b, 1)) playerWins++;
            }
            o.WriteLine($"{games} games against an attacker: a four had to be blocked {mustBlock} times and was {blocked} times; Nilo could win {couldWin} times and did {tookWin} times; open threes {openThrees}, answered at an end {blockedThrees}; the attacker won {playerWins} games");
            // (Nilo kills most lines at two or three stones, so fours are rare here: the constructed positions below are the real test of that)
            Assert.Equal(mustBlock, blocked);
            Assert.True(couldWin >= 10); Assert.Equal(couldWin, tookWin);
            Assert.True(blockedThrees >= openThrees * 0.8, "open threes are blocked");
            Assert.True(playerWins <= games / 5, "a line-building attacker should not often beat it: " + playerWins);
        }

        // a free cell that gives the player a place to win on the next move (a four), or -1
        static int FourMaker(int[] b, GlassRng rng)
        {
            var list = new List<int>();
            for (int i = 0; i < N * N; i++) if (b[i] == 0 && WinningCells(Place(b, i, 1), 1).Count > 0) list.Add(i);
            return list.Count == 0 ? -1 : list[rng.Int(list.Count)];
        }

        /// <summary>
        /// The omok script with stones already on the board (the same program: only the line that makes the empty
        /// board is followed by assignments). cells: "c,r" pairs, 1-based, as the script counts them.
        /// </summary>
        static GlassScript OmokWith(int seed, string[] player, string[] bambi)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("local stones, clock = ").Append(player.Length + bambi.Length).Append(", 0\n");
            foreach (string c in player) { var p = c.Split(','); sb.Append("board[(").Append(p[1]).Append(" - 1) * N + ").Append(p[0]).Append("] = 1\n"); }
            foreach (string c in bambi) { var p = c.Split(','); sb.Append("board[(").Append(p[1]).Append(" - 1) * N + ").Append(p[0]).Append("] = 2\n"); }
            string lua = GlassScriptExamples.Omok.Replace("local stones, clock = 0, 0\n", sb.ToString());
            Assert.NotEqual(GlassScriptExamples.Omok, lua);
            var g = Start("omok", seed, touch: true, lua: lua);
            Assert.Equal(player.Length, ReadBoard(g).Count(v => v == 1));
            Assert.Equal(bambi.Length, ReadBoard(g).Count(v => v == 2));
            return g;
        }

        static int At(int c, int r) { return (r - 1) * N + (c - 1); }     // 1-based col, row -> index of ReadBoard

        [Fact]
        public void Omok_in_constructed_positions_Bambi_blocks_the_four_blocks_the_three_and_takes_its_win()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                // an OPEN FOUR: the player has 3,5 4,5 5,5 with both ends free and plays 6,5. Nilo has to take one of the two ends.
                var g = OmokWith(seed, new[] { "3,5", "4,5", "5,5" }, new[] { "1,1", "9,9", "1,9" });
                int[] b = Move(g, At(6, 5), out int reply);
                Assert.Contains(reply, new[] { At(2, 5), At(7, 5) });
                Assert.Equal("play", g.phase);
                // ... and the player then wins at the other end: five in a row, the line is shown
                int other = reply == At(2, 5) ? At(7, 5) : At(2, 5);
                Move(g, other, out _);
                Assert.Equal("over", g.phase); Assert.Contains("You win", g.result); Assert.Equal(1, g.score);

                // A FOUR WITH ONE END SHUT (Nilo's stone at 2,5): there is exactly one cell to block, on the far side
                g = OmokWith(seed, new[] { "3,5", "4,5", "5,5" }, new[] { "2,5", "9,9", "1,9" });
                Move(g, At(6, 5), out reply);
                Assert.Equal(At(7, 5), reply);
                // the same along a diagonal and down a column, and with a hole in the four (X X _ X X: the hole is the cell)
                g = OmokWith(seed, new[] { "2,2", "3,3", "4,4" }, new[] { "1,1", "9,1", "1,9" });
                Move(g, At(5, 5), out reply);
                Assert.Equal(At(6, 6), reply);
                g = OmokWith(seed, new[] { "7,9", "7,8", "7,7" }, new[] { "1,1", "9,1", "1,9" });
                Move(g, At(7, 6), out reply);
                Assert.Equal(At(7, 5), reply);
                g = OmokWith(seed, new[] { "2,3", "3,3", "5,3" }, new[] { "1,9", "9,9", "5,9" });
                Move(g, At(6, 3), out reply);
                Assert.Equal(At(4, 3), reply);

                // AN OPEN THREE is answered at once, at one of its ends (or it would be an open four next)
                g = OmokWith(seed, new[] { "4,5", "5,5" }, new[] { "1,1", "9,9" });
                Move(g, At(6, 5), out reply);
                Assert.Contains(reply, new[] { At(3, 5), At(7, 5), At(2, 5), At(8, 5) });

                // WIN BEFORE BLOCK: the player makes a four, but Nilo has four of its own with a free end - it wins instead of blocking
                g = OmokWith(seed, new[] { "3,5", "4,5", "5,5", "9,1" }, new[] { "2,8", "3,8", "4,8", "5,8" });
                Move(g, At(6, 5), out reply);
                Assert.Contains(reply, new[] { At(1, 8), At(6, 8) });
                Assert.Equal("over", g.phase); Assert.Contains("Nilo wins", g.result); Assert.Equal(0, g.score);
                Step(g, null, 5);
                Assert.Equal(5, g.sprites.Count(s => s.art == GlassArt.RingShape && s.color == 0xE2574C));

                // with nothing to block and nothing to win it builds on its own stones: next to its three
                g = OmokWith(seed, new[] { "1,1", "9,1", "1,9" }, new[] { "4,6", "5,6", "6,6" });
                Move(g, At(9, 9), out reply);
                Assert.Contains(reply, new[] { At(3, 6), At(7, 6) });
            }
        }

        static int[] Place(int[] b, int i, int who) { var c = (int[])b.Clone(); c[i] = who; return c; }

        // The stone at i has made exactly three in a row with both ends free (and a free cell beyond one of them).
        static bool IsOpenThreeMove(int[] b, int i, out List<int> ends)
        {
            ends = new List<int>();
            int c = i % N, r = i / N;
            foreach (var (dc, dr) in Dirs)
            {
                int a = Run(b, c, r, dc, dr, 1), z = Run(b, c, r, -dc, -dr, 1);
                if (a + z != 2) continue;
                int ex = c + dc * (a + 1), ey = r + dr * (a + 1), fx = c - dc * (z + 1), fy = r - dr * (z + 1);
                if (ex < 0 || ex >= N || ey < 0 || ey >= N || fx < 0 || fx >= N || fy < 0 || fy >= N) continue;
                if (b[ey * N + ex] != 0 || b[fy * N + fx] != 0) continue;
                ends.Add(ey * N + ex); ends.Add(fy * N + fx);
                // (the cells one further count as an answer too: they make the three a dead one just as well)
                int gx = ex + dc, gy = ey + dr, hx = fx - dc, hy = fy - dr;
                if (gx >= 0 && gx < N && gy >= 0 && gy < N) ends.Add(gy * N + gx);
                if (hx >= 0 && hx < N && hy >= 0 && hy < N) ends.Add(hy * N + hx);
                return true;
            }
            return false;
        }

        [Fact]
        public void Omok_works_through_the_glass_too_and_Bambi_walks_to_its_move()
        {
            // no touching here: the hand rests over a cell for half a second
            var g = Start("omok", 4, touch: false);
            int cell = 4 * N + 4;
            float x = CellX(g, 4), y = CellY(4);
            Step(g, GlassScriptTests.Hand(x, y), 5);
            Assert.All(ReadBoard(g), v => Assert.Equal(0, v));
            Assert.Contains(g.sprites, s => s.art == GlassArt.RingShape && s.id >= GlassScript.FirstId && Math.Abs(s.x - x) < 0.001f);     // the free cell under the hand is marked
            bool walked = false, tapSound = false; int reply = -1;
            for (int f = 0; f < 14; f++)
            {
                Step(g, GlassScriptTests.Hand(x, y));
                if (g.sounds.Contains("tap")) tapSound = true;
                if (g.petPose == "walk") walked = true;
            }
            Assert.Equal(1, ReadBoard(g)[cell]);
            Assert.True(tapSound);
            // Nilo thinks: it walks to the side of the board nearest its move, then its stone lands
            Assert.Equal("Nilo is thinking...", g.banner);
            for (int f = 0; f < 60 && reply < 0; f++)
            {
                Step(g, GlassScriptTests.Hand(x, y));
                if (g.petPose == "walk") walked = true;
                reply = Array.IndexOf(ReadBoard(g), 2);
            }
            Assert.True(reply >= 0);
            Assert.True(walked);
            Assert.InRange(Math.Abs(reply % N - 4) + Math.Abs(reply / N - 4), 1, 2);     // next to the player's stone
            Assert.Equal(CellY(reply / N), g.petY, 1);                                    // level with its move
            Assert.True(Math.Abs(g.petX - g.W / 2) > 0.5f);                               // beside the board, not on it
            // the hand is still on the same cell: that is not a second tap, and a taken cell takes no stone
            Step(g, GlassScriptTests.Hand(x, y), 40);
            Assert.Equal(1, ReadBoard(g).Count(v => v == 1));
            Step(g, GlassScriptTests.Hand(CellX(g, reply % N), CellY(reply / N)), 25);
            Assert.Equal(1, ReadBoard(g).Count(v => v == 1));
            // outside the board nothing happens either
            Step(g, GlassScriptTests.Hand(0.1f, 0.5f), 25);
            Assert.Equal(2, ReadBoard(g).Count(v => v != 0));
        }

        [Fact]
        public void Omok_stays_cheap()
        {
            // a board that is nearly full: the most stones to draw, and Nilo still has to think
            var g = new GlassScript { touch = true, Precise = true };
            g.Start(GlassScriptExamples.Pack(GlassScriptExamples.Find("omok")), 3);
            GlassScriptTests.SkipIntro(g);
            int budget = g.Box.Budget, thinking = 0, quiet = 0, stones = 0;
            var rng = new GlassRng(); rng.Seed(5);
            while (g.phase != "over")
            {
                // a careful player: it blocks what Nilo could finish, and Nilo's threes; otherwise it plays far from everything
                int[] b = ReadBoard(g);
                var free = Enumerable.Range(0, N * N).Where(i => b[i] == 0).ToList();
                var danger = WinningCells(b, 2);
                int pick = -1;
                if (danger.Count > 0) pick = danger[0];
                else
                {
                    int best = -1;
                    foreach (int i in free)
                    {
                        int near = 0;
                        foreach (var (dc, dr) in Dirs) near = Math.Max(near, Run(b, i % N, i / N, dc, dr, 2) + Run(b, i % N, i / N, -dc, -dr, 2));
                        int own = 0;
                        foreach (var (dc, dr) in Dirs) own = Math.Max(own, Run(b, i % N, i / N, dc, dr, 1) + Run(b, i % N, i / N, -dc, -dr, 1));
                        int score = (near >= 2 ? near * 100 : 0) - own * 10 + rng.Int(5);        // break Nilo's lines, never build one of its own
                        if (score > best) { best = score; pick = i; }
                    }
                }
                Step(g, GlassScriptTests.Hand(CellX(g, pick % N), CellY(pick / N), 0.06f));
                g.Step(Dt, GlassScriptTests.Hand(CellX(g, pick % N), CellY(pick / N), 0.004f));     // the tap: the stone, the test for five, and Nilo's thinking
                thinking = Math.Max(thinking, g.Box.LastInstructions);
                int had = ReadBoard(g).Count(v => v == 2);
                for (int f = 0; f < 60 && g.phase != "over"; f++)
                {
                    g.Step(Dt, null);
                    bool landed = ReadBoard(g).Count(v => v == 2) > had;
                    if (landed) { thinking = Math.Max(thinking, g.Box.LastInstructions); break; }   // (its stone lands: the test for five again)
                    quiet = Math.Max(quiet, g.Box.LastInstructions);
                }
                g.Step(Dt, null);
                quiet = Math.Max(quiet, g.Box.LastInstructions);
                stones = ReadBoard(g).Count(v => v != 0);
            }
            Assert.Equal("", g.Error);
            o.WriteLine($"omok with {stones} stones: an ordinary frame at most {quiet} steps ({100.0 * quiet / budget:0.00} % of {budget}), the frame in which Nilo thinks {thinking} ({100.0 * thinking / budget:0.0} %)");
            Assert.True(stones >= 20, "stones " + stones);
            int full = quiet + (N * N - stones) * 100;                  // (each stone more costs under a hundred steps to draw)
            o.WriteLine($"with all {N * N} cells taken an ordinary frame would be about {full} steps ({100.0 * full / budget:0.00} %)");
            Assert.True(full < budget * 0.05, "an ordinary frame with a full board: " + full);
            Assert.True(thinking < budget * 0.10, "the thinking frame: " + thinking);
            Assert.True(g.StartInstructions < budget * 0.10);
        }

        // ------------------------------------------------------------------ catching stars

        [Fact]
        public void Catch_a_hand_under_the_stars_catches_them_and_no_hand_loses()
        {
            var g = Start("catch", 2, touch: false);
            Assert.Equal(5, g.lives);
            var hand = GlassScriptTests.Hand(g.W / 2, 0.5f);
            bool cheered = false, bannerSeen = false; int coins = 0;
            for (int f = 0; f < 30 * 80 && g.phase == "play"; f++)
            {
                // go under the lowest star
                GlassSprite low = default; bool any = false;
                foreach (var s in g.sprites) if (s.art == GlassArt.Star && (!any || s.y < low.y)) { low = s; any = true; }
                if (any) hand.x[0] += Math.Clamp(low.x - hand.x[0], -2.5f * Dt, 2.5f * Dt);
                Step(g, hand);
                coins += g.sounds.Count(s => s == "coin");
                if (g.petPose == "cheer") cheered = true;
                if (g.banner.Contains("faster")) bannerSeen = true;
                // the basket (the brown rectangle) follows the hand, and Nilo runs along under it
                var basket = g.sprites.First(s => s.art == GlassArt.Rect && s.color == 0x9A6B4F);
                Assert.InRange(basket.x, 0.11f, g.W - 0.11f);
                if (f > 60) Assert.True(Math.Abs(basket.x - Math.Clamp(hand.x[0], 0.12f, g.W - 0.12f)) < 0.2f);
            }
            Step(g, hand, 45);
            Assert.Equal(g.sprites.First(s => s.art == GlassArt.Rect && s.color == 0x9A6B4F).x, g.petX, 1);      // the hand rests: Nilo stands under the basket
            o.WriteLine($"catch: after 40 s with a hand: score {g.score}, lives {g.lives}, level {g.level}, phase {g.phase}");
            Assert.Equal("play", g.phase);
            Assert.True(g.score >= 25, "score " + g.score);
            Assert.InRange(coins, g.score * 3 / 4, g.score);         // (two stars in one moment make one sound)
            Assert.True(g.lives >= 2);
            Assert.Equal(1 + g.score / 8, g.level);
            Assert.True(cheered && bannerSeen);
            // the hand goes away: the stars fall to the ground, three lives go, it is over with the score in the result
            int at = g.score, hurt = 0;
            for (int f = 0; f < 30 * 40 && g.phase == "play"; f++) { Step(g, null); if (g.petHurt > 0f) hurt++; }
            Assert.Equal("over", g.phase);
            Assert.Equal(0, g.lives);
            Assert.True(hurt > 0);
            Assert.Equal("down", g.petPose);
            Assert.Contains("You caught " + g.score + " stars", g.result);
            Assert.InRange(g.score, at, at + 3);
            Step(g, hand, 60);
            Assert.Contains(g.sprites, s => s.art == GlassArt.Rect);                      // the last picture stays
        }

        // ------------------------------------------------------------------ memory

        [Fact]
        public void Memory_a_player_who_remembers_finds_all_pairs()
        {
            var g = Start("memory", 7, touch: true);
            float cw = 1.2f / 4, ch = 0.86f / 3, x0 = g.W / 2 + 0.1f - 0.6f, y0 = 0.5f - 0.43f;
            float X(int i) => x0 + (i % 4 + 0.5f) * cw;
            float Y(int i) => y0 + (i / 4 + 0.5f) * ch;
            string[] faces = { "star", "heart", "gem", "gold", "drop", "orb" };
            string FaceAt(int i)
            {
                foreach (var s in g.sprites) if (Array.IndexOf(faces, s.art) >= 0 && Math.Abs(s.x - X(i)) < 0.01f && Math.Abs(s.y - Y(i)) < 0.01f) return s.art;
                return null;
            }
            Assert.Equal(12, g.sprites.Count(s => s.art == GlassArt.Label && s.text == "?"));
            Assert.Null(FaceAt(0));
            var known = new string[12];
            // look at every card once, two at a time
            for (int i = 0; i < 12; i += 2)
            {
                Tap(g, X(i), Y(i)); Step(g, null, 8); known[i] = FaceAt(i);
                Tap(g, X(i + 1), Y(i + 1)); Step(g, null, 8); known[i + 1] = FaceAt(i + 1);
                Assert.NotNull(known[i]); Assert.NotNull(known[i + 1]);
                Step(g, null, 40);                                    // (two that do not match turn back)
            }
            foreach (string f in faces) Assert.Equal(2, known.Count(k => k == f));
            // a third card cannot be opened while two are being looked at; a tap beside the cards does nothing
            int scoreNow = g.score;
            Tap(g, 0.05f, 0.5f);
            Assert.Equal(scoreNow, g.score);
            // now take the pairs that are still closed
            foreach (string f in faces)
            {
                int a = Array.IndexOf(known, f), b = Array.LastIndexOf(known, f);
                if (a + 1 == b && a % 2 == 0) continue;               // found by luck in the first pass
                Tap(g, X(a), Y(a)); Step(g, null, 6);
                Tap(g, X(b), Y(b)); Step(g, null, 6);
            }
            Step(g, null, 10);
            Assert.Equal("over", g.phase);
            Assert.Equal(60, g.score);
            Assert.Matches(@"All pairs found in \d+ turns!", g.result);
            Assert.Equal("cheer", g.petPose);
            for (int i = 0; i < 12; i++) Assert.Equal(known[i], FaceAt(i));       // all open
        }

        // ------------------------------------------------------------------ the focus timer

        [Fact]
        public void Pomodoro_runs_rests_pauses_and_Bambi_studies_along()
        {
            // (the same script with shorter times: 6 seconds of work, 3 of rest)
            string lua = GlassScriptExamples.Pomodoro.Replace("25 * 60, 5 * 60", "6, 3");
            Assert.NotEqual(GlassScriptExamples.Pomodoro, lua);
            var g = Start("pomodoro", 1, touch: false, lua: lua);
            string Clock() => g.sprites.First(s => s.art == GlassArt.Label && s.text.Length == 5 && s.text[2] == ':').text;
            int Lit() => g.sprites.Count(s => s.art == GlassArt.Disc && s.size > 0.03f && s.size < 0.04f);
            float cx = g.W / 2 + 0.3f, cy = 0.52f;
            Step(g, null, 60);
            Assert.Equal("00:06", Clock()); Assert.Equal(0, Lit());
            Assert.Contains(g.sprites, s => s.text == "tap to start");
            Assert.False(g.petCustom);
            // nothing runs until the ring is tapped (through the glass: a hand rests on it)
            Step(g, GlassScriptTests.Hand(0.2f, 0.9f), 30);
            Assert.Equal("00:06", Clock());
            Step(g, GlassScriptTests.Hand(cx + 0.1f, cy), 17);
            Assert.Contains(g.sprites, s => s.text == "focus");
            Step(g, null, 30);
            Assert.Equal("00:05", Clock());
            Assert.InRange(Lit(), 9, 13);                              // a sixth of the sixty dots
            Assert.True(g.petCustom); Assert.Equal("study", g.PetMotion);     // Nilo sits and writes: the script's own motion
            Assert.True(g.petCustomPose.HipsOffset.Y < -0.05f, "sitting");
            Assert.True(g.petCustomPose[Bone.ArmUR].X < -40f, "the writing hand is up in front");
            // a pause: the clock stands still, Nilo stops; again: it goes on
            Step(g, GlassScriptTests.Hand(cx, cy - 0.1f), 17);
            Assert.Contains(g.sprites, s => s.text == "tap to start");
            string paused = Clock();
            Step(g, null, 90);
            Assert.Equal(paused, Clock()); Assert.False(g.petCustom);
            Step(g, GlassScriptTests.Hand(cx, cy), 17);
            Step(g, null, 1);
            Assert.True(g.petCustom);
            // the round ends: a point, a cheer (the hop is the script's second motion), the rest begins
            bool hop = false; float top = 0f;
            for (int f = 0; f < 30 * 6 && g.score == 0; f++) Step(g, null);
            Assert.Equal(1, g.score);
            Assert.Equal("Well done! Take a break.", g.banner);
            for (int f = 0; f < 40; f++) { Step(g, null); if (g.PetMotion == "hooray") { hop = true; top = Math.Max(top, g.petCustomPose.HipsOffset.Y); } }
            Assert.True(hop && top > 0.2f, "the hop: " + top);
            Assert.Contains(g.sprites, s => s.text == "break");
            Assert.Equal("cheer", g.petPose);
            // and after the rest it is work again
            for (int f = 0; f < 30 * 4 && g.banner != "Back to work!"; f++) Step(g, null);
            Assert.Equal("Back to work!", g.banner);
            Step(g, null, 5);
            Assert.Equal("study", g.PetMotion);
            Assert.Equal("play", g.phase);
            // the real one counts 25 minutes
            var real = Start("pomodoro", 1, touch: false);
            Step(real, null, 5);
            Assert.Contains(real.sprites, s => s.text == "25:00");
        }

        // ------------------------------------------------------------------ tag

        [Fact]
        public void Tag_Bambi_flees_from_the_hand_and_a_hand_that_chases_catches_it()
        {
            var g = Start("tag", 3, touch: true);
            Assert.True(g.flat);
            // a hand comes close: it runs AWAY from it
            float px = g.petX, py = g.petY;
            var hand = GlassScriptTests.Hand(px - 0.3f, py, 0.05f);
            bool ran = false, right = false;
            for (int f = 0; f < 12; f++) { Step(g, hand); if (g.petPose == "walk" || g.petPose == "run") { ran = true; right = g.petFacesRight; } }
            Assert.True(g.petX > px + 0.2f, "it ran from " + px + " to " + g.petX);
            Assert.True(ran && right);
            Assert.Equal(0, g.score);
            // chase it: the hand is a little faster than Nilo
            int squeaks = 0; float minX = 9, maxX = -9, minY = 9, maxY = -9;
            while (g.phase == "play")
            {
                float dx = g.petX - hand.x[0], dy = g.petY - hand.y[0], d = MathF.Sqrt(dx * dx + dy * dy);
                float step = Math.Min(d, 1.5f * Dt);
                if (d > 0.0001f) { hand.x[0] += dx / d * step; hand.y[0] += dy / d * step; }
                Step(g, hand);
                squeaks += g.sounds.Count(s => s == "squeak");
                minX = Math.Min(minX, g.petX); maxX = Math.Max(maxX, g.petX); minY = Math.Min(minY, g.petY); maxY = Math.Max(maxY, g.petY);
                Assert.True(g.seconds <= 30.1f);
            }
            o.WriteLine($"tag: caught {g.score} times in 30 s; Nilo stayed within x {minX:0.00}..{maxX:0.00}, y {minY:0.00}..{maxY:0.00}");
            Assert.InRange(g.score, 5, 25);
            Assert.Equal(g.score, squeaks);
            Assert.InRange(g.seconds, 29.9f, 30.1f);
            Assert.Equal("You caught Nilo " + g.score + " times!", g.result);
            Assert.True(minX >= 0.13f && maxX <= g.W - 0.13f && minY >= 0.13f && maxY <= 0.87f, "it stays on the field");
            Assert.True(maxX - minX > 0.6f && maxY - minY > 0.3f, "it uses the field");
            // nobody there: it strolls about by itself and is never caught
            var alone = Start("tag", 5, touch: true);
            float moved = 0f; px = alone.petX; py = alone.petY;
            for (int f = 0; f < 30 * 10; f++) { Step(alone, null); moved += MathF.Abs(alone.petX - px) + MathF.Abs(alone.petY - py); px = alone.petX; py = alone.petY; }
            Assert.True(moved > 1f, "strolling: " + moved);
            Assert.Equal(0, alone.score);
        }
    }
}
