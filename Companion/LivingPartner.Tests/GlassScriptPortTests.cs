using LivingPartner.Shared;
using Xunit;
using Xunit.Abstractions;

namespace LivingPartner.Tests
{
    /// <summary>
    /// The two games that used to be C# classes (GlassSurvival, GlassUmbrella) as scripts: "Vampire Night" and
    /// "Shadow Umbrella". Played here the way a person plays them, through hands and sprites only.
    /// </summary>
    public class GlassScriptPortTests
    {
        readonly ITestOutputHelper o;
        public GlassScriptPortTests(ITestOutputHelper o) { this.o = o; }

        const float Dt = 1f / 30f;

        static GlassScript Start(string id, int seed)
        {
            var g = new GlassScript();
            g.Start(GlassScriptExamples.Pack(GlassScriptExamples.Find(id)), seed);
            Assert.True(g.Error == "", g.Error);
            GlassScriptTests.SkipIntro(g);
            return g;
        }

        static void Step(GlassScript g, GlassInput input, int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                g.Step(Dt, input);
                Assert.True(g.Error == "", g.Error);
                GlassScriptTests.AssertSane(g);
            }
        }

        // ------------------------------------------------------------------ Shadow Umbrella

        /// <summary>Where a good player holds the umbrella: under the lowest ink that is coming down on Nilo (a blob is stopped only by the middle of the hand), otherwise over Nilo.</summary>
        static float Shelter(GlassScript g)
        {
            float x = g.petX, low = 9f;
            foreach (var s in g.sprites)
                if ((s.art == GlassArt.Drop || s.art == GlassArt.Blob) && Math.Abs(s.x - g.petX) < 0.16f && s.y > 0.3f && s.y < low) { low = s.y; x = s.x; }
            return x;
        }

        [Fact]
        public void Umbrella_a_hand_over_Bambi_keeps_it_dry_and_it_gathers_stars()
        {
            var g = Start("umbrella", 4);
            Assert.False(g.flat); Assert.Equal(3, g.lives);
            Assert.Equal(3, g.sprites.Count(s => s.art == GlassArt.Star));
            var hand = GlassScriptTests.Hand(g.petX, 0.42f);
            int drops = 0, squeaks = 0, picks = 0; bool walked = false, bent = false, cheered = false;
            for (int f = 0; f < 30 * 40 && g.phase == "play"; f++)
            {
                hand.x[0] += Math.Clamp(Shelter(g) - hand.x[0], -2f * Dt, 2f * Dt);     // the umbrella stays over Nilo, and meets what comes
                Step(g, hand);
                drops += g.sounds.Count(s => s == "drop"); squeaks += g.sounds.Count(s => s == "squeak"); picks += g.sounds.Count(s => s == "sparkle");
                if (g.petPose == "walk") walked = true;
                if (g.petPose == "pick") bent = true;
                if (g.petPose == "cheer") cheered = true;
                Assert.Equal(3, g.sprites.Count(s => s.art == GlassArt.Star));      // there are always three stars to go for
            }
            o.WriteLine($"umbrella, sheltered for 40 s: {g.score} stars, level {g.level}, lives {g.lives}, {drops} drops caught, {squeaks} golden ones wasted");
            Assert.Equal("play", g.phase);
            Assert.True(g.lives >= 2 || g.seconds > 39f, "lives " + g.lives);
            Assert.True(g.score >= 15, "stars " + g.score);
            Assert.True(g.level >= 3);
            Assert.True(drops >= 10);
            Assert.True(squeaks >= 1, "golden drops that fall on the umbrella are wasted");
            Assert.True(walked && bent && cheered);
            Assert.True(picks >= g.score - squeaks * 3 - 10);
        }

        [Fact]
        public void Umbrella_golden_drops_must_be_let_through_and_without_a_hand_it_ends()
        {
            // the careful player: the umbrella is over Nilo - except while a golden drop is coming down on it
            var g = Start("umbrella", 9);
            var hand = GlassScriptTests.Hand(g.petX, 0.42f);
            int coins = 0, squeaks = 0;
            for (int f = 0; f < 30 * 75 && g.phase == "play"; f++)
            {
                bool gold = g.sprites.Any(s => s.art == GlassArt.Gold && Math.Abs(s.x - g.petX) < 0.2f && s.y < 0.7f);
                hand.x[0] = gold ? g.petX + (g.petX > g.W / 2 ? -0.5f : 0.5f) : Shelter(g);
                Step(g, hand);
                coins += g.sounds.Count(s => s == "coin"); squeaks += g.sounds.Count(s => s == "squeak");
            }
            o.WriteLine($"umbrella, letting gold through for 75 s: score {g.score}, {coins} golden drops arrived, {squeaks} wasted, lives {g.lives}, level {g.level}");
            Assert.True(coins >= 3, "golden drops that arrived: " + coins);
            Assert.True(coins > squeaks);
            Assert.True(g.score >= coins * 3 + 5);
            Assert.Contains("'+3'", g.Box.Source);

            // nobody shelters it: ink hits, it cowers under what is coming, three hearts go and it is over
            var alone = Start("umbrella", 4);
            bool cowered = false; int hits = 0;
            for (int f = 0; f < 30 * 120 && alone.phase == "play"; f++)
            {
                Step(alone, null);
                if (alone.petPose == "cower") cowered = true;
                hits += alone.sounds.Count(s => s == "error");
            }
            Assert.Equal("over", alone.phase);
            Assert.Equal(0, alone.lives); Assert.Equal(3, hits);
            Assert.True(cowered);
            Assert.Equal("down", alone.petPose);
            Assert.Equal("Nilo gathered " + alone.score + " stars.", alone.result);
            Assert.True(alone.seconds > 4f, "it does not end at once: " + alone.seconds);
            // the wind (from level 3) leans the drops
            var w = Start("umbrella", 4);
            var h2 = GlassScriptTests.Hand(w.petX, 0.42f);
            bool leaning = false, whoosh = false;
            for (int f = 0; f < 30 * 90 && w.phase == "play"; f++)
            {
                h2.x[0] = Shelter(w);
                Step(w, h2);
                if (w.sounds.Contains("whoosh")) whoosh = true;
                if (w.sprites.Any(s => s.art == GlassArt.Drop && Math.Abs(s.rot) > 5f)) leaning = true;
            }
            Assert.True(w.level >= 3 && whoosh && leaning);
        }

        // ------------------------------------------------------------------ Vampire Night

        static readonly string[] Foes = { GlassArt.Enemy, GlassArt.Flyer, GlassArt.Brute };

        [Fact]
        public void Survival_the_hand_leads_bops_gathers_and_picks_a_power_from_the_cards()
        {
            var g = Start("survival", 3);
            Assert.Equal(3, g.lives); Assert.Equal(1, g.level);
            // the hand leads: Nilo walks under it
            var hand = GlassScriptTests.Hand(0.4f, 0.6f);
            Step(g, hand, 45);
            Assert.InRange(g.petX, 0.38f, 0.5f);
            Assert.InRange(g.petY, 0.55f, 0.62f);
            Assert.False(g.petFacesRight && g.petPose == "walk" && g.petX < 0.39f);
            // play: go for the nearest gem, otherwise bop the nearest enemy that is not too close to Nilo
            int pops = 0, coins = 0, shotsSeen = 0;
            bool cards = false;
            for (int f = 0; f < 30 * 120 && !cards; f++)
            {
                GlassSprite? goal = null; float best = 9f;
                foreach (var s in g.sprites)
                {
                    bool gem = s.art == GlassArt.Gem, foe = Array.IndexOf(Foes, s.art) >= 0;
                    if (!gem && !foe) continue;
                    float d = MathF.Abs(s.x - hand.x[0]) + MathF.Abs(s.y - hand.y[0]) - (gem ? 0.5f : 0f);
                    if (s.x < 0.05f || s.x > g.W - 0.05f) continue;
                    if (d < best) { best = d; goal = s; }
                }
                if (goal != null)
                {
                    hand.x[0] += Math.Clamp(goal.Value.x - hand.x[0], -1.6f * Dt, 1.6f * Dt);
                    hand.y[0] += Math.Clamp(goal.Value.y - hand.y[0], -1.6f * Dt, 1.6f * Dt);
                }
                Step(g, hand);
                pops += g.sounds.Count(s => s == "pop"); coins += g.sounds.Count(s => s == "coin");
                shotsSeen += g.sprites.Count(s => s.art == GlassArt.Shot);
                cards = g.sprites.Any(s => s.art == GlassArt.Card);
            }
            Assert.True(cards, "level 2 was never reached");
            Assert.Equal(2, g.level);
            Assert.True(pops >= 4 && coins >= 5, pops + " pops, " + coins + " gems");      // (two in the same moment make one sound)
            Assert.True(g.score >= 7);
            Assert.True(shotsSeen > 0, "Nilo shoots by itself");
            Assert.Contains("pick a power", g.banner);

            // choosing: three different cards; the world stands still; a hand that rests on one fills its ring and takes it
            var shown = g.sprites.Where(s => s.art == GlassArt.Card).ToList();
            Assert.Equal(3, shown.Count);
            Assert.Equal(3, shown.Select(s => s.text).Distinct().Count());
            Assert.All(shown, s => { Assert.True(s.text.Length > 0 && s.text2.Length > 0); Assert.Equal(0.34f, s.size); Assert.Equal(0.56f, s.y, 3); });
            var foesBefore = g.sprites.Where(s => Array.IndexOf(Foes, s.art) >= 0).Select(s => (s.x, s.y)).ToList();
            float secondsBefore = g.seconds; int livesBefore = g.lives;
            Step(g, null, 60);
            Assert.Equal(foesBefore, g.sprites.Where(s => Array.IndexOf(Foes, s.art) >= 0).Select(s => (s.x, s.y)).ToList());
            Assert.Equal(livesBefore, g.lives);
            var pick = shown[2];
            var onCard = GlassScriptTests.Hand(pick.x, pick.y);
            Step(g, onCard, 8);
            float fill = g.sprites.First(s => s.art == GlassArt.Card && s.text == pick.text).fill;
            Assert.InRange(fill, 0.2f, 0.5f);
            Assert.Equal(0f, g.sprites.First(s => s.art == GlassArt.Card && s.text != pick.text).fill);
            bool tada = false;
            for (int f = 0; f < 30 && g.sprites.Any(s => s.art == GlassArt.Card); f++) { Step(g, onCard); if (g.sounds.Contains("ta-da")) tada = true; }
            Assert.DoesNotContain(g.sprites, s => s.art == GlassArt.Card);
            Assert.True(tada);
            Assert.Equal(pick.text + "!", g.banner);
            if (pick.text == "Big Hand") Assert.True(g.handRadius > GlassGames.HandRadiusBase * 1.3f);
            if (pick.text == "Extra Heart") Assert.Equal(livesBefore + 1, g.lives);
            // and the world goes on
            Step(g, hand, 30);
            Assert.True(g.seconds > secondsBefore + 0.9f);
            Assert.Equal("play", g.phase);

            // nobody chooses: after fifteen seconds the first card is taken by itself
            bool again = false;
            for (int f = 0; f < 30 * 240 && !again; f++)
            {
                GlassSprite? goal = null; float best = 9f;
                foreach (var s in g.sprites)
                {
                    bool gem = s.art == GlassArt.Gem, foe = Array.IndexOf(Foes, s.art) >= 0;
                    float d = MathF.Abs(s.x - hand.x[0]) + MathF.Abs(s.y - hand.y[0]) - (gem ? 0.5f : 0f);
                    if ((gem || foe) && s.x > 0.05f && s.x < g.W - 0.05f && d < best) { best = d; goal = s; }
                }
                if (goal != null) { hand.x[0] += Math.Clamp(goal.Value.x - hand.x[0], -1.6f * Dt, 1.6f * Dt); hand.y[0] += Math.Clamp(goal.Value.y - hand.y[0], -1.6f * Dt, 1.6f * Dt); }
                Step(g, hand);
                again = g.sprites.Any(s => s.art == GlassArt.Card);
            }
            Assert.True(again && g.level == 3);
            string first = g.sprites.First(s => s.art == GlassArt.Card).text;
            Step(g, null, 30 * 14);
            Assert.Contains(g.sprites, s => s.art == GlassArt.Card);
            Step(g, null, 45);
            Assert.DoesNotContain(g.sprites, s => s.art == GlassArt.Card);
            Assert.Equal(first + "!", g.banner);
        }

        [Fact]
        public void Survival_all_seven_powers_work_and_left_alone_it_is_lost_in_the_end()
        {
            // a player who sweeps the field and takes whatever card comes first, for five minutes
            var taken = new HashSet<string>();
            int moons = 0, bolts = 0, maxFoes = 0, peak = 0; float maxReach = 0f; int maxLives = 0;
            long steps = 0, frames = 0;
            for (int seed = 1; seed <= 4; seed++)
            {
                var g = new GlassScript { Precise = true };
                g.Start(GlassScriptExamples.Pack(GlassScriptExamples.Find("survival")), seed);
                GlassScriptTests.SkipIntro(g);
                var hand = GlassScriptTests.Hand(g.W / 2, 0.3f);
                var rng = new GlassRng(); rng.Seed(seed);
                float tx = 0.5f, ty = 0.2f;
                for (int f = 0; f < 30 * 300 && g.phase == "play"; f++)
                {
                    var card = g.sprites.Where(s => s.art == GlassArt.Card).OrderBy(s => taken.Contains(s.text) ? 1 : 0).ToList();
                    if (card.Count > 0) { hand.x[0] = card[0].x; hand.y[0] = card[0].y; if (card[0].fill > 0.9f) taken.Add(card[0].text); }
                    else
                    {
                        if (MathF.Abs(hand.x[0] - tx) + MathF.Abs(hand.y[0] - ty) < 0.05f) { tx = rng.Range(0.1f, g.W - 0.1f); ty = rng.Range(0.05f, 0.5f); }
                        hand.x[0] += Math.Clamp(tx - hand.x[0], -1.2f * Dt, 1.2f * Dt); hand.y[0] += Math.Clamp(ty - hand.y[0], -1.2f * Dt, 1.2f * Dt);
                    }
                    Step(g, hand);
                    moons = Math.Max(moons, g.sprites.Count(s => s.art == GlassArt.Orb));
                    bolts += g.sprites.Count(s => s.art == GlassArt.Bolt);
                    maxFoes = Math.Max(maxFoes, g.sprites.Count(s => Array.IndexOf(Foes, s.art) >= 0));
                    maxReach = Math.Max(maxReach, g.handRadius); maxLives = Math.Max(maxLives, g.lives);
                    peak = Math.Max(peak, g.Box.LastInstructions); steps += g.Box.LastInstructions; frames++;
                }
                o.WriteLine($"survival seed {seed}: {g.seconds:0} s, level {g.level}, {g.score} beaten, lives {g.lives}, phase {g.phase}");
                Assert.True(g.level >= 5, "level " + g.level);
            }
            o.WriteLine($"powers taken: {string.Join(", ", taken.OrderBy(s => s))}; at most {maxFoes} enemies; a frame {steps / frames} steps on average, {peak} at most");
            foreach (string p in new[] { "Star Shot", "Moons", "Frost", "Swift Feet", "Big Hand", "Lightning", "Extra Heart" }) Assert.Contains(p, taken);
            Assert.True(moons >= 1 && bolts >= 1);
            Assert.True(maxReach > GlassGames.HandRadiusBase * 1.3f && maxLives >= 4);
            Assert.InRange(maxFoes, 12, 28);
            Assert.True(steps / frames < GlassScriptCheck.HeavyAverage, "average " + steps / frames);
            Assert.True(peak < LuaSandbox.DefaultBudget / 20, "the busiest frame: " + peak);     // twenty times of room under the budget

            // nobody plays: Nilo holds the first waves by itself (its stars), but in the end it is lost
            var alone = Start("survival", 2);
            bool auto = false;
            for (int f = 0; f < 30 * 900 && alone.phase == "play"; f++)
            {
                alone.Step(Dt, null);
                Assert.True(alone.Error == "", alone.Error);
                if (alone.banner.EndsWith("!") && alone.level >= 2) auto = true;        // a card was taken by itself after fifteen seconds
            }
            o.WriteLine($"survival alone: lost after {alone.seconds:0} s at level {alone.level} with {alone.score} beaten");
            Assert.Equal("over", alone.phase);
            Assert.True(alone.seconds > 40f, "it holds the first waves: " + alone.seconds);
            Assert.True(alone.score >= 10);
            _ = auto;
            Assert.Equal("down", alone.petPose);
            Assert.Matches(@"^Nilo beat \d+ of them in \d+ seconds\.$", alone.result);
        }
    }
}
