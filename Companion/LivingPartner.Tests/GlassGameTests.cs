using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// The two native glass games (Shared/GlassGames.cs, GlassSurvival.cs, GlassUmbrella.cs): the simulations that
/// the PC client and the headset both draw. Nothing here needs an engine: a game is started, stepped with hands that
/// the test moves, and what it says it would draw and play is checked.
/// </summary>
public class GlassGameTests
{
    internal const float Dt = 1f / 60f;

    // ------------------------------------------------------------------ helpers

    internal static GlassGame New(string template, int seed = 1, GamePackDef pack = null, float w = 0f)
    {
        var g = GlassGames.Create(template);
        Assert.NotNull(g);
        if (w > 0f) g.W = w;
        g.Start(pack, seed);
        return g;
    }

    internal static T New<T>(int seed = 1, GamePackDef pack = null) where T : GlassGame, new()
    {
        var g = new T();
        g.Start(pack, seed);
        return g;
    }

    internal static void Hand(GlassInput input, int i, float x, float y, float dist = 0.1f, float spread = 1f)
    {
        input.valid[i] = true; input.x[i] = x; input.y[i] = y; input.dist[i] = dist; input.spread[i] = spread;
    }

    /// <summary>Step until `done` (checked before every step); false when it did not happen in time.</summary>
    internal static bool StepUntil(GlassGame g, GlassInput input, Func<bool> done, float maxSeconds, Action beforeEach = null)
    {
        int n = (int)(maxSeconds / Dt);
        for (int i = 0; i < n; i++)
        {
            if (done()) return true;
            beforeEach?.Invoke();
            g.Step(Dt, input);
        }
        return done();
    }

    internal static void Run(GlassGame g, GlassInput input, float seconds, Action beforeEach = null, Action afterEach = null)
    {
        int n = (int)MathF.Round(seconds / Dt);
        for (int i = 0; i < n; i++)
        {
            beforeEach?.Invoke();
            g.Step(Dt, input);
            afterEach?.Invoke();
        }
    }

    /// <summary>Through the intro, to the very start of play.</summary>
    internal static void ToPlay(GlassGame g)
    {
        Assert.True(StepUntil(g, new GlassInput(), () => g.phase == "play", 3f));
    }

    static int Count(GlassGame g, string art)
    {
        int n = 0;
        foreach (var s in g.sprites) if (s.art == art) n++;
        return n;
    }

    static bool Find(GlassGame g, Func<GlassSprite, bool> which, out GlassSprite found)
    {
        foreach (var s in g.sprites) if (which(s)) { found = s; return true; }
        found = default;
        return false;
    }

    static bool HasId(GlassGame g, int id)
    {
        foreach (var s in g.sprites) if (s.id == id) return true;
        return false;
    }

    static bool IsEnemy(GlassSprite s) => s.art == GlassArt.Enemy || s.art == GlassArt.Flyer || s.art == GlassArt.Brute;

    static readonly string[] Poses = { "idle", "walk", "run", "cheer", "hurt", "pick", "cower", "down" };
    static readonly string[] Phases = { "intro", "play", "choose", "over" };

    static bool Real(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    /// <summary>Everything a renderer reads is a real number in a sane range and a name it knows.</summary>
    internal static void CheckSane(GlassGame g, bool fogToo = false)
    {
        Assert.Contains(g.phase, Phases);
        Assert.Contains(g.petPose, Poses);
        Assert.True(Real(g.petX) && g.petX >= 0f && g.petX <= g.W, $"petX {g.petX}");
        Assert.True(Real(g.petY) && g.petY >= 0f && g.petY <= 1f, $"petY {g.petY}");
        Assert.True(Real(g.seconds) && g.seconds >= 0f);
        Assert.True(Real(g.petHurt) && g.petHurt >= 0f && g.petHurt <= 1f);
        Assert.True(Real(g.handRadius) && g.handRadius >= 0.08f && g.handRadius <= 0.2f, $"handRadius {g.handRadius}");
        Assert.InRange(g.lives, 0, 6);
        Assert.True(g.score >= 0 && g.level >= 1);
        Assert.NotNull(g.banner); Assert.NotNull(g.result);
        Assert.Equal(g.choices.Length, g.choiceNotes.Length);
        Assert.Equal(g.phase == "over", g.result.Length > 0);
        if (g.phase == "choose")
        {
            Assert.InRange(g.choices.Length, 1, 3);
            Assert.Equal(g.choices.Length, Count(g, GlassArt.Card));
        }
        else Assert.Empty(g.choices);

        var ids = new HashSet<int>();
        foreach (var s in g.sprites)
        {
            Assert.Contains(s.art, GlassArt.All);
            Assert.True(Real(s.x) && s.x >= -0.5f && s.x <= g.W + 0.5f, $"{s.art} x {s.x}");
            Assert.True(Real(s.y) && s.y >= -0.5f && s.y <= 1.5f, $"{s.art} y {s.y}");
            Assert.True(Real(s.size) && s.size > 0f && s.size <= 1.2f, $"{s.art} size {s.size}");
            Assert.True(Real(s.alpha) && s.alpha > 0f && s.alpha <= 1f, $"{s.art} alpha {s.alpha}");
            Assert.True(Real(s.rot), $"{s.art} rot {s.rot}");
            Assert.True(Real(s.fill) && s.fill >= 0f && s.fill <= 1f);
            Assert.NotNull(s.text); Assert.NotNull(s.text2);
            Assert.True(ids.Add(s.id), $"two sprites with the id {s.id} ({s.art})");
            if (s.art == GlassArt.Card) { Assert.NotEqual("", s.text); Assert.NotEqual("", s.text2); }
            if (s.art == GlassArt.Label) Assert.NotEqual("", s.text);
        }
        var heard = new HashSet<string>();
        foreach (var name in g.sounds)
        {
            Assert.Contains(name, SfxSynth.Names);
            Assert.True(heard.Add(name), $"'{name}' twice in one step");
        }
        Assert.Null(g.fog);         // (no game has fog any more: the wipe game is gone)
    }

    /// <summary>A hand that moves like a hand: towards where it wants to be, not faster than `speed` field units a second.</summary>
    internal sealed class BotHand
    {
        public readonly GlassInput input = new GlassInput();
        public float x, y, speed = 2f;
        bool placed;

        public void Reach(float tx, float ty)
        {
            if (!placed) { x = tx; y = ty; placed = true; }
            else
            {
                float dx = tx - x, dy = ty - y, d = MathF.Sqrt(dx * dx + dy * dy), step = speed * Dt;
                if (d <= step) { x = tx; y = ty; } else { x += dx / d * step; y += dy / d * step; }
            }
            Hand(input, 0, x, y);
        }

        /// <summary>Pulled back from the glass (it stays where it is).</summary>
        public void Away() { input.valid[0] = placed; input.dist[0] = 0.4f; input.x[0] = x; input.y[0] = y; }
    }

    // ---- the bots: each plays the way a sensible person would, from what is drawn

    /// <summary>The hand goes for whatever is nearest the partner; the first card is taken.</summary>
    internal static void PlaySurvival(GlassGame g, BotHand b)
    {
        if (g.phase == "choose") { g.Choose(0); return; }
        float best = float.MaxValue, tx = 0f, ty = 0f;
        foreach (var s in g.sprites)
        {
            if (!IsEnemy(s)) continue;
            float dx = s.x - g.petX, dy = s.y - 0.10f, d = dx * dx + dy * dy;
            if (d < best) { best = d; tx = s.x; ty = s.y; }
        }
        if (best == float.MaxValue) b.Away(); else b.Reach(tx, ty);
    }

    /// <summary>
    /// The hand stays over the partner, goes to meet the drop that will reach it next (read from where the drop is and
    /// how it is slanted, as a person would), and lifts for a golden drop that is about to meet the hand.
    /// </summary>
    internal static void PlayUmbrella(GlassGame g, BotHand b, bool liftForGold = true, bool meetDrops = true)
    {
        float tx = g.petX, ty = 0.27f;
        if (meetDrops)
        {
            float soonest = 0.45f;                                       // what is higher than this above the hand can wait
            foreach (var s in g.sprites)
            {
                if (s.art != GlassArt.Drop && s.art != GlassArt.Blob) continue;
                float above = s.y - ty;
                if (above < -0.05f || above >= soonest) continue;        // it is past the hand, or another comes first
                float slope = MathF.Tan(s.rot * MathF.PI / 180f);        // sideways for every unit it falls
                if (MathF.Abs(s.x + slope * (s.y - 0.12f) - g.petX) > 0.15f) continue;     // it will not come near the partner
                soonest = above; tx = s.x + slope * MathF.Max(0f, above);
            }
        }
        bool gold = false;
        if (liftForGold)
            foreach (var s in g.sprites)
                if (s.art == GlassArt.Gold && MathF.Abs(s.x - b.x) < 0.17f && s.y - b.y < 0.17f && s.y - b.y > -0.15f) gold = true;
        b.Reach(tx, ty);
        if (gold) b.Away();
    }

    internal static void PlayAny(GlassGame g, BotHand b)
    {
        if (g.Template == "survival") PlaySurvival(g, b);
        else if (g.Template == "umbrella") PlayUmbrella(g, b);
        else throw new InvalidOperationException("no bot for " + g.Template);
    }

    /// <summary>A whole game: with the bot's hand or with no hands at all. Stops when it is over or after maxSeconds of stepping.</summary>
    internal static GlassGame PlayOut(string template, int seed, bool bot, float maxSeconds, GamePackDef pack = null)
    {
        var g = New(template, seed, pack);
        var b = new BotHand();
        int n = (int)(maxSeconds / Dt);
        for (int i = 0; i < n && g.phase != "over"; i++)
        {
            if (bot) PlayAny(g, b);
            g.Step(Dt, b.input);
        }
        return g;
    }

    // ------------------------------------------------------------------ all three

    [Fact]
    public void There_are_two_native_templates_and_each_makes_its_game()
    {
        // (others - a script - may be added behind them; the wipe game, "Foggy Window", is gone with its way of playing)
        Assert.Equal(new[] { "survival", "umbrella" }, GlassGames.Templates.Take(2));
        Assert.DoesNotContain("wipe", GlassGames.Templates); Assert.DoesNotContain("board", GlassGames.Templates);
        foreach (string t in new[] { "survival", "umbrella" })
        {
            Assert.True(GlassGames.Is(t));
            Assert.True(GlassGames.Is(" " + t.ToUpperInvariant() + " "));
            var g = GlassGames.Create(t);
            Assert.NotNull(g);
            Assert.Equal(t, g.Template);
        }
        Assert.IsType<GlassSurvival>(GlassGames.Create("survival"));
        Assert.IsType<GlassUmbrella>(GlassGames.Create("umbrella"));
        foreach (string no in new[] { null, "", "pop", "tennis", "survival2", "wipe", "board" })
        {
            Assert.False(GlassGames.Is(no));
            Assert.Null(GlassGames.Create(no));
        }
    }

    [Fact]
    public void A_hand_acts_only_when_it_is_tracked_and_near_the_plane()
    {
        var g = New("umbrella");
        var input = new GlassInput();
        Assert.False(GlassGames.Acts(input, 0));
        Assert.False(GlassGames.Acts(null, 0));
        Hand(input, 0, 0.5f, 0.5f, 0.29f);
        Assert.True(GlassGames.Acts(input, 0));
        Assert.False(GlassGames.Acts(input, 1));
        Assert.False(GlassGames.Acts(input, 2));
        input.dist[0] = 0.31f; Assert.False(GlassGames.Acts(input, 0));
        input.dist[0] = float.NaN; Assert.False(GlassGames.Acts(input, 0));
        input.dist[0] = 0.1f; input.x[0] = float.NaN; Assert.False(GlassGames.Acts(input, 0));
        input.x[0] = float.PositiveInfinity; Assert.False(GlassGames.Acts(input, 0));
        input.x[0] = 0.5f;
        // an open hand reaches the game's radius, a fist six tenths of it
        input.spread[0] = 1f; Assert.Equal(0.085f, GlassGames.HandRadius(g, input, 0), 4);
        input.spread[0] = 0f; Assert.Equal(0.051f, GlassGames.HandRadius(g, input, 0), 4);
        input.spread[0] = 7f; Assert.Equal(0.085f, GlassGames.HandRadius(g, input, 0), 4);
        input.spread[0] = float.NaN; Assert.Equal(0.051f, GlassGames.HandRadius(g, input, 0), 4);
    }

    [Theory]
    [InlineData("survival")]
    [InlineData("umbrella")]
    public void It_starts_with_an_intro_that_says_how_and_harms_nobody_then_plays(string template)
    {
        var g = New(template, 3);
        Assert.Equal("intro", g.phase);
        Assert.Equal(3, g.lives);
        Assert.Equal(1, g.level);
        Assert.Equal(0, g.score);
        Assert.True(g.banner.Length > 10, "the game's own line");
        Assert.Equal(g.W * 0.5f, g.petX, 3);
        var input = new GlassInput();
        Hand(input, 0, g.petX, 0.3f);
        float t = 0f;
        while (g.phase == "intro" && t < 5f)
        {
            Assert.True(g.banner.Length > 10);
            g.Step(Dt, input); t += Dt;
            CheckSane(g, true);
            if (g.phase != "intro") break;
            foreach (var s in g.sprites) Assert.False(IsEnemy(s) || s.art == GlassArt.Drop || s.art == GlassArt.Blob || s.art == GlassArt.Card, s.art + " in the intro");
            Assert.Equal(0f, g.seconds);
        }
        Assert.Equal("play", g.phase);
        Assert.InRange(t, 1.45f, 1.55f);
        Assert.Equal(3, g.lives);

        // a pack's own line, hearts and pace are taken
        var pack = new GamePackDef { howTo = "Keep the moths off the lantern!", lives = 5 };
        g = New(template, 3, pack);
        Assert.Equal("Keep the moths off the lantern!", g.banner);
        Assert.Equal(5, g.lives);
        // nonsense in a pack does not get in
        g = New(template, 3, new GamePackDef { lives = 99, speed = float.NaN, spawn = -4f, seconds = 0f });
        Assert.Equal(6, g.lives);
        Run(g, new GlassInput(), 20f, null, () => CheckSane(g));
    }

    [Theory]
    [InlineData("survival")]
    [InlineData("umbrella")]
    public void The_same_seed_and_the_same_hands_give_the_same_game(string template)
    {
        string Play(int seed)
        {
            var g = New(template, seed);
            var rnd = new Random(99);
            var input = new GlassInput();
            var sb = new System.Text.StringBuilder();
            float hx = 0.8f, hy = 0.5f, tx = 0.8f, ty = 0.5f;
            for (int i = 0; i < 60 * 60; i++)
            {
                if (i % 45 == 0) { tx = (float)rnd.NextDouble() * g.W; ty = (float)rnd.NextDouble(); }
                hx += (tx - hx) * 0.08f; hy += (ty - hy) * 0.08f;
                Hand(input, 0, hx, hy, i % 400 < 330 ? 0.1f : 0.5f);
                Hand(input, 1, g.W - hx, hy * 0.6f, i % 700 < 200 ? 0.12f : 0.6f, 0.4f);
                g.Step(Dt, input);
                if (i % 60 != 59) continue;
                sb.Append(g.phase).Append(' ').Append(g.score).Append(' ').Append(g.lives).Append(' ').Append(g.level).Append(' ')
                  .Append(g.seconds.ToString("R")).Append(' ').Append(g.petX.ToString("R")).Append(' ').Append(g.petPose).Append(' ').Append(g.banner).Append(' ').Append(g.fogRevision);
                foreach (var s in g.sprites) sb.Append('|').Append(s.id).Append(s.art).Append(s.x.ToString("R")).Append(',').Append(s.y.ToString("R")).Append(',').Append(s.alpha.ToString("R")).Append(s.text);
                foreach (string c in g.choices) sb.Append('/').Append(c);
                if (g.fog != null) { double sum = 0; foreach (float v in g.fog) sum += v; sb.Append(" fog").Append(sum.ToString("R")); }
                sb.Append('\n');
            }
            return sb.ToString();
        }
        string a = Play(11), b = Play(11), c = Play(12);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);

        // a game object that has been played can be started again: nothing of the old game is left in it
        var used = PlayOut(template, 3, true, 45f);
        var fresh = New(template, 11);
        used.Start(null, 11);
        var hand = new GlassInput();
        for (int i = 0; i < 40 * 60; i++)
        {
            Hand(hand, 0, fresh.petX + 0.3f * MathF.Sin(i * 0.02f), 0.3f + 0.25f * MathF.Sin(i * 0.013f), i % 500 < 400 ? 0.1f : 0.5f);
            if (fresh.phase == "choose" && i % 90 == 0) { fresh.Choose(1); used.Choose(1); }
            fresh.Step(Dt, hand); used.Step(Dt, hand);
            Assert.Equal(fresh.sprites.Count, used.sprites.Count);
            if (i % 20 != 0) continue;
            Assert.Equal(fresh.phase, used.phase); Assert.Equal(fresh.score, used.score); Assert.Equal(fresh.lives, used.lives);
            Assert.Equal(fresh.petX, used.petX); Assert.Equal(fresh.seconds, used.seconds); Assert.Equal(fresh.banner, used.banner);
            Assert.Equal(fresh.fogRevision, used.fogRevision); Assert.Equal(fresh.handRadius, used.handRadius);
            Assert.Equal(fresh.sounds, used.sounds);
            for (int k = 0; k < fresh.sprites.Count; k++)
            {
                Assert.Equal(fresh.sprites[k].id, used.sprites[k].id); Assert.Equal(fresh.sprites[k].art, used.sprites[k].art);
                Assert.Equal(fresh.sprites[k].x, used.sprites[k].x); Assert.Equal(fresh.sprites[k].y, used.sprites[k].y);
                Assert.Equal(fresh.sprites[k].alpha, used.sprites[k].alpha);
            }
        }
    }

    [Theory]
    [InlineData("survival")]
    [InlineData("umbrella")]
    public void Left_alone_it_ends_and_over_stays_over(string template)
    {
        var g = New(template, 5);
        var input = new GlassInput();
        var said = new List<string>();
        Assert.True(StepUntil(g, input, () => g.phase == "over", 15 * 60f), "with no hands it has to end");
        Assert.Equal(0, g.lives);
        Assert.Equal("down", g.petPose);
        Assert.Contains("lose", g.sounds);
        Assert.True(g.result.Length > 8 && g.result.Contains(g.score.ToString()), g.result);
        Assert.Empty(g.choices);
        Assert.Equal("", g.banner);
        string result = g.result; int score = g.score, level = g.level; float seconds = g.seconds, petX = g.petX;

        // stepping goes on harmlessly, whatever the hands do; what was on the field fades away
        var rnd = new Random(4);
        for (int i = 0; i < 6 * 60; i++)
        {
            Hand(input, 0, (float)rnd.NextDouble() * g.W, (float)rnd.NextDouble());
            Hand(input, 1, g.petX, 0.2f);
            if (i % 50 == 0) g.Choose(rnd.Next(3));
            g.Step(Dt, input);
            CheckSane(g);
            Assert.Equal("over", g.phase);
            if (i > 0) Assert.Empty(g.sounds);
        }
        Assert.Equal(result, g.result);
        Assert.Equal(score, g.score); Assert.Equal(level, g.level); Assert.Equal(0, g.lives);
        Assert.Equal(seconds, g.seconds); Assert.Equal(petX, g.petX);
        Assert.Equal("down", g.petPose);
        Assert.Empty(g.sprites);

        // and it can be started again
        g.Start(null, 6);
        Assert.Equal("intro", g.phase); Assert.Equal(3, g.lives); Assert.Equal(0, g.score); Assert.Equal("", g.result); Assert.Equal(1, g.level);
    }

    [Theory]
    [InlineData("survival")]
    [InlineData("umbrella")]
    public void Five_minutes_of_random_hands_never_throw_and_never_draw_nonsense(string template)
    {
        var rnd = new Random(2026);
        var hard = new GamePackDef { lives = 1, spawn = 3f, speed = 1.6f, seconds = 12f, howTo = "Hold on!" };
        var g = New(template, 7, hard);
        var input = new GlassInput();
        float[] hx = { 0.5f, 1.2f }, hy = { 0.5f, 0.4f }, tx = { 0.5f, 1.2f }, ty = { 0.5f, 0.4f };
        float overFor = 0f; int games = 1;
        var phasesSeen = new HashSet<string>();
        var perSound = new Dictionary<string, int>();
        int steps = 5 * 60 * 60;
        for (int i = 0; i < steps; i++)
        {
            for (int h = 0; h < 2; h++)
            {
                if (rnd.Next(40) == 0) { tx[h] = (float)(rnd.NextDouble() * 1.2 - 0.1) * g.W; ty[h] = (float)(rnd.NextDouble() * 1.2 - 0.1); }
                if (rnd.Next(300) == 0) { tx[h] = g.petX; ty[h] = 0.3f; }                       // now and then it helps
                hx[h] += (tx[h] - hx[h]) * 0.15f; hy[h] += (ty[h] - hy[h]) * 0.15f;
                input.valid[h] = rnd.Next(50) != 0 ? input.valid[h] : rnd.Next(3) != 0;
                input.x[h] = hx[h]; input.y[h] = hy[h];
                if (rnd.Next(120) == 0) input.dist[h] = (float)rnd.NextDouble() * 0.5f - 0.02f;
                input.spread[h] = (float)rnd.NextDouble() * 1.4f - 0.2f;
                // what a tracker can hand over on a bad frame
                int bad = rnd.Next(900);
                if (bad == 0) input.x[h] = float.NaN;
                else if (bad == 1) input.y[h] = float.PositiveInfinity;
                else if (bad == 2) input.dist[h] = float.NaN;
                else if (bad == 3) input.spread[h] = float.NaN;
                else if (bad == 4) { input.x[h] = -40f; input.y[h] = 77f; }
            }
            if (rnd.Next(400) == 0) g.Choose(rnd.Next(-1, 4));
            float dt = Dt;
            int odd = rnd.Next(2000);
            if (odd == 0) dt = 0f; else if (odd == 1) dt = 0.4f; else if (odd == 2) dt = float.NaN; else if (odd == 3) dt = -1f; else if (odd == 4) dt = 1f / 20f;
            g.Step(dt, input);
            CheckSane(g, i % 30 == 0);
            phasesSeen.Add(g.phase);
            foreach (string s in g.sounds) perSound[s] = perSound.TryGetValue(s, out int c) ? c + 1 : 1;
            if (g.phase == "over")
            {
                overFor += Dt;
                if (overFor > 3f) { g.Start(games % 3 == 2 ? hard : null, 100 + games); games++; overFor = 0f; }
            }
        }
        Assert.Contains("play", phasesSeen);
        Assert.Contains("over", phasesSeen);
        if (template == "survival") Assert.Contains("choose", phasesSeen);
        // no sound more than a few times a second, on the whole
        foreach (var kv in perSound) Assert.True(kv.Value < steps * Dt * 4f, $"'{kv.Key}' {kv.Value} times in five minutes");
    }

    [Theory]
    [InlineData("survival", 1.25f)]
    [InlineData("umbrella", 1.25f)]
    [InlineData("survival", 2.4f)]
    [InlineData("umbrella", 2.4f)]
    public void A_field_of_another_shape_is_played_to_its_own_edges(string template, float w)
    {
        var g = New(template, 9, null, w);
        Assert.Equal(w, g.W);
        Assert.Equal(w * 0.5f, g.petX, 3);
        var b = new BotHand();
        float left = w, right = 0f;
        for (int i = 0; i < 90 * 60 && g.phase != "over"; i++)
        {
            PlayAny(g, b);
            g.Step(Dt, b.input);
            CheckSane(g);
            foreach (var s in g.sprites) { left = MathF.Min(left, s.x); right = MathF.Max(right, s.x); }
        }
        Assert.True(left < w * 0.2f && right > w * 0.8f, $"things were drawn from {left} to {right} on a field {w} wide");
    }

    [Theory]
    [InlineData("survival")]
    [InlineData("umbrella")]
    public void A_long_frame_is_stepped_in_pieces_and_a_bad_one_is_ignored(string template)
    {
        // thirty frames a second and sixty: the same clock (to within a frame, at the end of the intro)
        var pack = new GamePackDef { lives = 6 };
        var a = New(template, 21, pack); var b = New(template, 21, pack);
        var input = new GlassInput();
        for (int i = 0; i < 5 * 30; i++) { a.Step(1f / 30f, input); b.Step(Dt, input); b.Step(Dt, input); }
        Assert.Equal("play", a.phase); Assert.Equal("play", b.phase);
        Assert.InRange(a.seconds, 3.45f, 3.52f);
        Assert.InRange(MathF.Abs(a.seconds - b.seconds), 0f, 0.02f);
        // ten frames a second: three pieces each, the clock does not fall behind
        var c = New(template, 21, pack);
        for (int i = 0; i < 50; i++) c.Step(0.1f, input);
        Assert.Equal("play", c.phase);
        Assert.InRange(c.seconds, 3.45f, 3.55f);
        // no time, a negative time and not-a-number change nothing
        float before = c.seconds, petX = c.petX; int score = c.score;
        c.Step(0f, input); c.Step(-3f, input); c.Step(float.NaN, input); c.Step(0f, null);
        Assert.Equal(before, c.seconds); Assert.Equal(petX, c.petX); Assert.Equal(score, c.score);
        CheckSane(c);
        // a hitch of ten seconds counts as a quarter of a second
        c.Step(10f, input);
        Assert.Equal(before + 0.25f, c.seconds, 3);
        // a game that is stepped before it was started is a game with no pack
        var raw = GlassGames.Create(template);
        raw.Step(Dt, null);
        Assert.Equal("intro", raw.phase);
        CheckSane(raw, true);
        Run(raw, input, 3f, null, () => CheckSane(raw));
        Assert.Equal("play", raw.phase);
    }

    [Theory]
    [InlineData("survival")]
    [InlineData("umbrella")]
    public void The_state_for_the_host_is_the_games_own(string template)
    {
        var g = PlayOut(template, 2, true, 20f);
        var s = new GlassState();
        g.FillState(s);
        Assert.Equal(g.phase, s.phase); Assert.Equal(g.score, s.score); Assert.Equal(g.lives, s.lives); Assert.Equal(g.level, s.level);
        Assert.Equal(g.seconds, s.seconds); Assert.Equal(g.banner, s.banner); Assert.Equal(g.result, s.result);
        Assert.Same(g.choices, s.choices); Assert.Same(g.choiceNotes, s.choiceNotes);
    }

    [Theory]
    [InlineData("survival")]
    [InlineData("umbrella")]
    public void Playing_does_not_make_garbage_every_frame(string template)
    {
        var g = New(template, 4);
        var b = new BotHand();
        for (int i = 0; i < 20 * 60; i++) { PlayAny(g, b); g.Step(Dt, b.input); }      // everything has its room by now
        Assert.NotEqual("over", g.phase);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int frames = 0;
        for (int i = 0; i < 20 * 60 && g.phase != "over"; i++)
        {
            // the hand by hand here (the bot's own search is the test's garbage, not the game's)
            Hand(b.input, 0, g.petX + MathF.Sin(i * 0.05f) * 0.3f, 0.3f + 0.2f * MathF.Sin(i * 0.031f));
            if (g.phase == "choose") g.Choose(0);
            g.Step(Dt, b.input);
            frames++;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        // a banner now and then, the names of three cards at a level-up: a few bytes a frame on the average, not a list every frame
        Assert.True(bytes < frames * 48L, $"{bytes} bytes in {frames} frames");
    }

    // ------------------------------------------------------------------ survival

    [Fact]
    public void Survival_they_keep_coming_and_more_of_them_wave_after_wave()
    {
        var g = New<GlassSurvival>(1, new GamePackDef { lives = 6 });
        ToPlay(g);
        var b = new BotHand();
        var born = new HashSet<int>();
        var perWave = new Dictionary<int, int>();
        var kinds = new Dictionary<int, HashSet<string>>();
        int maxAlive = 0;
        string lastBanner = "";
        var banners = new List<string>();
        for (int i = 0; i < 100 * 60 && g.phase != "over" && g.Wave < 6; i++)
        {
            PlaySurvival(g, b);
            g.Step(Dt, b.input);
            if (g.Wave == 0) continue;
            foreach (var s in g.sprites)
            {
                if (!IsEnemy(s) || !born.Add(s.id)) continue;
                perWave[g.Wave] = perWave.TryGetValue(g.Wave, out int c) ? c + 1 : 1;
                if (!kinds.TryGetValue(g.Wave, out var set)) kinds[g.Wave] = set = new HashSet<string>();
                set.Add(s.art);
            }
            maxAlive = Math.Max(maxAlive, g.EnemyCount);
            if (g.banner != lastBanner) { lastBanner = g.banner; if (g.banner.StartsWith("Wave")) banners.Add(g.banner + "@" + g.Wave); }
            Assert.Equal(1 + (int)(g.seconds / GlassSurvival.WaveSeconds), g.Wave);
        }
        Assert.True(g.Wave >= 6, $"the bot fell in wave {g.Wave}");
        // wave 1 is walkers only, flyers from wave 2, brutes from wave 4
        Assert.Equal(new[] { GlassArt.Enemy }, kinds[1].ToArray());
        Assert.Contains(GlassArt.Flyer, kinds[2]);
        Assert.DoesNotContain(GlassArt.Brute, kinds[1]); Assert.DoesNotContain(GlassArt.Brute, kinds[2]); Assert.DoesNotContain(GlassArt.Brute, kinds[3]);
        Assert.True(kinds[4].Contains(GlassArt.Brute) || kinds[5].Contains(GlassArt.Brute));
        // 0.55 a second in wave 1, 1.22 times more each wave
        Assert.InRange(perWave[1], 8, 12);
        Assert.True(perWave[3] > perWave[1] && perWave[5] > perWave[3], $"{perWave[1]} {perWave[2]} {perWave[3]} {perWave[4]} {perWave[5]}");
        Assert.InRange(perWave[5], 19, 26);
        Assert.Equal(new[] { "Wave 1@1", "Wave 2@2", "Wave 3@3", "Wave 4@4", "Wave 5@5", "Wave 6@6" }, banners.ToArray());
    }

    [Fact]
    public void Survival_with_no_hands_the_partner_holds_out_for_a_while_and_then_falls()
    {
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var g = PlayOut("survival", seed, false, 20 * 60f);
            Assert.Equal("over", g.phase);
            Assert.Equal(0, g.lives);
            Assert.InRange(g.seconds, 20f, 8 * 60f);            // its own star shot is worth something, but not everything
            Assert.True(g.score >= 10, $"seed {seed}: {g.score} beaten");
            Assert.StartsWith("Level " + g.level + ", " + g.score + " beaten", g.result);
        }
    }

    [Fact]
    public void Survival_the_partner_follows_the_acting_hand_and_minds_itself_without_one()
    {
        var g = New<GlassSurvival>(2);
        ToPlay(g);
        var input = new GlassInput();
        float x0 = g.petX;
        // a hand to the right, near the glass: it walks there at 0.42 a second and stops within the dead zone
        Hand(input, 0, x0 + 0.5f, 0.6f);
        Run(g, input, 0.5f);
        Assert.Equal("walk", g.petPose); Assert.True(g.petFacesRight);
        Assert.Equal(x0 + 0.5f * GlassSurvival.PetSpeed, g.petX, 2);
        Run(g, input, 1.5f);
        Assert.InRange(g.petX, x0 + 0.44f, x0 + 0.5f);
        Assert.Equal("idle", g.petPose);
        // a hand too far from the glass does not lead; the nearer of two hands does
        Hand(input, 0, 0.2f, 0.6f, 0.35f);
        float x1 = g.petX;
        Run(g, input, 0.2f);
        Assert.True(MathF.Abs(g.petX - x1) < 0.03f);
        Hand(input, 0, 0.1f, 0.2f, 0.1f);
        Hand(input, 1, g.petX + 0.2f, 0.2f, 0.1f);
        x1 = g.petX;
        Run(g, input, 0.3f);
        Assert.True(g.petX > x1 + 0.1f);
        // it never leaves the field
        Hand(input, 0, -5f, 0.2f); input.valid[1] = false;
        Run(g, input, 6f);
        Assert.InRange(g.petX, 0.08f, 0.131f);                  // the edge it may stand at, and the dead zone before it
        Assert.False(g.petFacesRight);

        // by itself it backs away from what comes near (three times as many come, so that some do)
        g = New<GlassSurvival>(2, new GamePackDef { spawn = 3f, lives = 6 });
        ToPlay(g);
        input = new GlassInput();
        int moved = 0, away = 0;
        for (int i = 0; i < 16 * 60 && g.phase == "play"; i++)       // wave 1: walkers only
        {
            float before = g.petX, nearest = 0f, dist = float.MaxValue;
            foreach (var s in g.sprites)
                if (IsEnemy(s) && MathF.Abs(s.x - before) < dist) { dist = MathF.Abs(s.x - before); nearest = s.x; }
            g.Step(Dt, input);
            if (dist > 0.22f || dist < 0.03f || g.petX == before) continue;
            moved++;
            if ((nearest < before) == (g.petX > before)) away++;
        }
        Assert.True(moved >= 30 && away >= moved * 0.9f, $"it moved {moved} times with something near, {away} of them away from it");
    }

    [Fact]
    public void Survival_a_hand_on_an_enemy_beats_it_and_its_gem_is_experience()
    {
        var g = New<GlassSurvival>(3);
        ToPlay(g);
        var input = new GlassInput();
        GlassSprite foe = default;
        Assert.True(StepUntil(g, input, () => Find(g, IsEnemy, out foe), 5f));
        Assert.Equal(GlassArt.Enemy, foe.art);
        Assert.True(MathF.Abs(foe.x - g.petX) > GlassSurvival.StarRange, "out of the star shot's reach: only the hand can have done it");
        Assert.InRange(foe.size, 0.08f, 0.14f);
        Assert.Equal(foe.x > g.petX ? 1 : 0, foe.frame);            // it faces the partner
        Assert.Equal(0, g.score);

        // a hand that is not near the glass does nothing
        Hand(input, 0, foe.x, foe.y, 0.35f);
        g.Step(Dt, input);
        Assert.True(HasId(g, foe.id)); Assert.Equal(0, g.score);
        // near the glass, it bops
        Assert.True(Find(g, s => s.id == foe.id, out foe));
        Hand(input, 0, foe.x, foe.y, 0.1f);
        g.Step(Dt, input);
        Assert.False(HasId(g, foe.id));
        Assert.Equal(1, g.score);
        Assert.Contains("pop", g.sounds);
        Assert.Equal(1, Count(g, GlassArt.Puff));
        Assert.True(Find(g, s => s.art == GlassArt.Gem, out var gem));
        Assert.InRange(gem.x - foe.x, -0.02f, 0.12f);           // where it fell (inside the field: it came in over the edge)
        Assert.Equal(1, g.GemCount);
        Assert.Equal(0, g.Xp);

        // the gem lies there (the hand is taken away at once) until a hand sweeps over it; then it flies to the partner
        input.valid[0] = false;
        Run(g, input, 0.5f);
        Assert.True(Find(g, s => s.id == gem.id, out gem));
        Assert.Equal(0, g.Xp);
        Hand(input, 0, gem.x, gem.y + 0.05f);
        g.Step(Dt, input);
        input.valid[0] = false;
        bool coin = false;
        Assert.True(StepUntil(g, input, () => { coin |= g.sounds.Contains("coin"); return g.Xp >= 1; }, 2f));
        Assert.True(coin || g.sounds.Contains("coin"));
        Assert.False(HasId(g, gem.id));
    }

    [Fact]
    public void Survival_a_gem_is_taken_when_the_partner_comes_within_its_reach()
    {
        var g = New<GlassSurvival>(3);
        ToPlay(g);
        var input = new GlassInput();
        GlassSprite foe = default;
        Assert.True(StepUntil(g, input, () => Find(g, IsEnemy, out foe), 5f));
        Hand(input, 0, foe.x, foe.y);
        g.Step(Dt, input);
        input.valid[0] = false;
        Run(g, input, 0.6f);                                     // it settles on the floor
        Assert.True(Find(g, s => s.art == GlassArt.Gem, out var gem));
        Assert.InRange(gem.y, 0.02f, 0.04f);
        // the hand leads the partner towards it from above (too high to touch the gem itself)
        float far = MathF.Abs(gem.x - g.petX);
        Assert.True(far > 0.5f);
        Hand(input, 0, Math.Clamp(gem.x, 0.1f, g.W - 0.1f), 0.9f);
        float took = -1f;
        for (int i = 0; i < 6 * 60 && took < 0f; i++)
        {
            g.Step(Dt, input);
            if (!HasId(g, gem.id) || (Find(g, s => s.id == gem.id, out var now) && now.y > 0.045f))      // it has left the floor: it is coming
                took = MathF.Abs(gem.x - g.petX);
        }
        Assert.InRange(took, 0.08f, GlassSurvival.MagnetRadius + 0.01f);     // it came from the magnet's reach, not from under its feet
        Assert.True(StepUntil(g, input, () => g.Xp >= 1 || g.level > 1, 1f));
    }

    static GlassSurvival ChoosingWith(string id, out int index, int lives = 3, float spawn = 1f)
    {
        for (int seed = 1; seed < 300; seed++)
        {
            var g = New<GlassSurvival>(seed, new GamePackDef { lives = lives, spawn = spawn });
            ToPlay(g);
            g.GrantXp(g.XpNeeded);
            g.Step(Dt, new GlassInput());
            Assert.Equal("choose", g.phase);
            index = Array.IndexOf(g.ChoiceIds, id);
            if (index >= 0) return g;
        }
        throw new Exception(id + " was never offered");
    }

    [Fact]
    public void Survival_enough_gems_make_a_level_and_a_choice_of_three_different_powers()
    {
        var first = new HashSet<string>();
        for (int seed = 1; seed <= 40; seed++)
        {
            var g = New<GlassSurvival>(seed);
            ToPlay(g);
            Assert.Equal(7, g.XpNeeded);                        // 4 + 3 * level
            var input = new GlassInput();
            g.GrantXp(6);
            Run(g, input, 0.1f);
            Assert.Equal("play", g.phase); Assert.Equal(1, g.level);
            g.GrantXp(1);
            float secondsBefore = g.seconds;
            g.Step(Dt, input);
            Assert.Equal("choose", g.phase);
            Assert.Equal(2, g.level);
            Assert.Equal(0, g.Xp);
            Assert.Equal(10, g.XpNeeded);
            Assert.Equal("Level up! Pick a power", g.banner);
            Assert.Equal("cheer", g.petPose);
            Assert.Contains("sparkle", g.sounds);
            Assert.Equal(3, g.choices.Length); Assert.Equal(3, g.choiceNotes.Length); Assert.Equal(3, g.ChoiceIds.Length);
            Assert.Equal(3, g.ChoiceIds.Distinct().Count());
            for (int k = 0; k < 3; k++)
            {
                int a = Array.IndexOf(GlassSurvival.AbilityIds, g.ChoiceIds[k]);
                Assert.True(a >= 0);
                Assert.Equal(GlassSurvival.AbilityNames[a], g.choices[k]);
                int has = g.Abilities[g.ChoiceIds[k]];
                if (g.ChoiceIds[k] == "heart") Assert.Contains("4 in all", g.choiceNotes[k]);
                else Assert.StartsWith("Lv " + (has + 1) + ": ", g.choiceNotes[k]);
                first.Add(g.ChoiceIds[k]);
            }
            // three cards, where the renderers expect them
            Run(g, input, 0.3f);
            var cards = g.sprites.Where(s => s.art == GlassArt.Card).ToArray();
            Assert.Equal(3, cards.Length);
            float[] xs = { g.W * 0.22f, g.W * 0.5f, g.W * 0.78f };
            for (int k = 0; k < 3; k++)
            {
                Assert.Equal(xs[k], cards[k].x, 4); Assert.Equal(0.56f, cards[k].y, 4); Assert.Equal(0.34f, cards[k].size, 4);
                Assert.Equal(g.choices[k], cards[k].text); Assert.Equal(g.choiceNotes[k], cards[k].text2);
                Assert.Equal(GlassSurvival.AbilityIds[cards[k].frame], g.ChoiceIds[k]);
                Assert.Equal(0f, cards[k].fill); Assert.Equal(1f, cards[k].alpha);
                Assert.Equal(xs[k], g.CardX(k, 3), 4);
            }
            // the world stands still while choosing: the clock, and everything on the field
            string Field() => string.Join(";", g.sprites.Where(s => IsEnemy(s) || s.art == GlassArt.Gem || s.art == GlassArt.Shot).Select(s => s.id + ":" + s.x.ToString("R") + "," + (IsEnemy(s) ? "" : s.y.ToString("R"))));
            string field = Field();
            Run(g, input, 3f);
            Assert.Equal(secondsBefore, g.seconds, 3);
            Assert.Equal(field, Field());
            Assert.Equal(3, g.lives);
            Assert.Equal("choose", g.phase);
        }
        // every power turns up among the first offers of forty games
        Assert.Equal(GlassSurvival.AbilityIds.OrderBy(x => x).ToArray(), first.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Survival_choose_takes_the_power_and_play_goes_on_after_a_moment()
    {
        var g = ChoosingWith("moons", out int k);
        var input = new GlassInput();
        Assert.Equal(0, g.Abilities["moons"]);
        g.Choose(-1); g.Choose(3);
        Assert.Equal("choose", g.phase);
        g.Choose(k);
        Assert.Equal("play", g.phase);
        Assert.Empty(g.choices); Assert.Empty(g.choiceNotes); Assert.Empty(g.ChoiceIds);
        Assert.Equal(1, g.Abilities["moons"]);
        Assert.Equal("Orbiting Moons Lv 1!", g.banner);
        g.Choose(0);                                            // nothing to choose now
        Assert.Equal(2, g.Abilities.Values.Sum());              // the star shot it began with, and the moon: nothing was taken twice
        // 0.6 s of grace: the clock waits, the card that was taken is seen going
        float t0 = g.seconds;
        g.Step(Dt, input);
        Assert.Contains("ta-da", g.sounds);
        Assert.Equal(t0, g.seconds);
        Assert.Equal(1, Count(g, GlassArt.Card));
        Assert.Equal("cheer", g.petPose);
        Run(g, input, 0.5f);
        Assert.Equal(t0, g.seconds);
        Run(g, input, 0.2f);
        Assert.True(g.seconds > t0);
        Assert.Equal(0, Count(g, GlassArt.Card));
        Assert.True(g.Invulnerable, "a moment to get the hands back");
        // one moon, on its circle around the partner
        Assert.Equal(1, Count(g, GlassArt.Orb));
        Assert.True(Find(g, s => s.art == GlassArt.Orb, out var orb));
        float r = MathF.Sqrt((orb.x - g.petX) * (orb.x - g.petX) + (orb.y - 0.11f) * (orb.y - 0.11f));
        Assert.Equal(GlassSurvival.OrbitRadius, r, 2);
        float a0 = MathF.Atan2(orb.y - 0.11f, orb.x - g.petX);
        g.Step(Dt, input);
        Find(g, s => s.art == GlassArt.Orb, out orb);
        Assert.NotEqual(a0, MathF.Atan2(orb.y - 0.11f, orb.x - g.petX));
    }

    [Fact]
    public void Survival_the_moons_beat_what_they_touch()
    {
        var g = ChoosingWith("moons", out int k, 6, 3f);        // three times as many come: more than its stars can stop
        g.Choose(k);
        var input = new GlassInput();
        int moonKills = 0;
        for (int i = 0; i < 60 * 60 && g.phase != "over"; i++)
        {
            if (g.phase == "choose") g.Choose(Math.Max(0, Array.IndexOf(g.ChoiceIds, "moons")));
            var orbs = g.sprites.Where(s => s.art == GlassArt.Orb).ToArray();
            var foes = g.sprites.Where(IsEnemy).ToArray();
            var shots = g.sprites.Where(s => s.art == GlassArt.Shot).ToArray();
            g.Step(Dt, input);
            // an enemy that is gone now, was under a moon and had no star near it
            foreach (var f in foes)
            {
                if (HasId(g, f.id)) continue;
                bool underMoon = orbs.Any(o => MathF.Sqrt((o.x - f.x) * (o.x - f.x) + (o.y - f.y) * (o.y - f.y)) < 0.12f);
                bool starNear = shots.Any(s => MathF.Sqrt((s.x - f.x) * (s.x - f.x) + (s.y - f.y) * (s.y - f.y)) < 0.12f);
                if (underMoon && !starNear) moonKills++;
            }
        }
        Assert.True(moonKills >= 3, $"{moonKills} fell to a moon");
    }

    [Fact]
    public void Survival_star_shot_fires_by_itself_and_grows_faster_and_wider()
    {
        // level 1: one star at a time, every 0.9 s at the most, only at what is within 0.75
        (int most, float shortest, int volleys) Watch(GlassSurvival g, float seconds)
        {
            var input = new GlassInput();
            var seen = new HashSet<int>();
            int most = 0, volleys = 0; float shortest = 99f, last = -99f;
            for (int i = 0; i < (int)(seconds / Dt) && g.phase != "over"; i++)
            {
                if (g.phase == "choose") { g.Choose(Array.FindIndex(g.ChoiceIds, id => id != "star")); last = -99f; continue; }
                g.Step(Dt, input);
                int fresh = 0;
                foreach (var s in g.sprites)
                {
                    if (s.art != GlassArt.Shot || !seen.Add(s.id)) continue;
                    fresh++;
                    Assert.True(g.sprites.Any(e => IsEnemy(e) && MathF.Abs(e.x - g.petX) < GlassSurvival.StarRange + 0.05f), "a star with nothing in reach");
                }
                if (fresh == 0) continue;
                most = Math.Max(most, fresh); volleys++;
                if (last > 0f) shortest = MathF.Min(shortest, g.seconds - last);
                last = g.seconds;
            }
            return (most, shortest, volleys);
        }

        var one = New<GlassSurvival>(8, new GamePackDef { lives = 6, spawn = 2f });
        ToPlay(one);
        Assert.Equal(1, one.Abilities["star"]);
        var w1 = Watch(one, 30f);
        Assert.True(w1.volleys >= 8, $"{w1.volleys} volleys");
        Assert.Equal(1, w1.most);
        Assert.InRange(w1.shortest, 0.88f, 0.95f);
        Assert.True(one.score >= 5, "its stars beat what comes");

        var two = ChoosingWith("star", out int k, 6, 3f);
        Assert.StartsWith("Lv 2: two stars", two.choiceNotes[k]);
        two.Choose(k);
        Assert.Equal(2, two.Abilities["star"]);
        var w2 = Watch(two, 30f);
        Assert.Equal(2, w2.most);
        Assert.InRange(w2.shortest, 0.63f, 0.70f);
    }

    [Fact]
    public void Survival_extra_heart_is_a_heart_at_once_and_never_more_than_six()
    {
        var g = ChoosingWith("heart", out int k);
        Assert.Equal(3, g.lives);
        g.Choose(k);
        Assert.Equal(4, g.lives);
        Assert.Equal(1, g.Abilities["heart"]);
        Assert.Equal("Extra Heart!", g.banner);

        // with six hearts it is not offered
        for (int seed = 1; seed <= 30; seed++)
        {
            var full = New<GlassSurvival>(seed, new GamePackDef { lives = 6 });
            ToPlay(full);
            full.GrantXp(full.XpNeeded);
            full.Step(Dt, new GlassInput());
            Assert.DoesNotContain("heart", full.ChoiceIds);
        }
    }

    [Fact]
    public void Survival_swift_feet_make_the_partner_faster()
    {
        float Walked(GlassSurvival g)
        {
            var input = new GlassInput();
            Run(g, input, 0.7f);                                 // the grace after a choice
            float x0 = g.petX;
            Hand(input, 0, g.W - 0.1f, 0.9f);
            Run(g, input, 1f);
            return g.petX - x0;
        }
        var plain = New<GlassSurvival>(1);
        ToPlay(plain);
        Assert.Equal(0.42f, Walked(plain), 2);
        var swift = ChoosingWith("swift", out int k);
        swift.Choose(k);
        Assert.Equal(0.42f * 1.25f, swift.PetPace, 4);
        Assert.Equal(0.42f * 1.25f, Walked(swift), 2);
    }

    [Fact]
    public void Survival_big_hand_reaches_what_a_plain_hand_does_not()
    {
        // a hand held 0.155 beside a walker that has just come in (far out of the star shot's reach):
        // a plain hand reaches 0.085 + 0.05, a big one 0.119 + 0.05
        bool Bops(GlassSurvival g)
        {
            var input = new GlassInput();
            Run(g, input, 0.7f);
            GlassSprite foe = default;
            Assert.True(StepUntil(g, input, () => Find(g, s => s.art == GlassArt.Enemy && MathF.Abs(s.x - g.petX) > 0.82f, out foe), 8f));
            int score = g.score;
            Hand(input, 0, foe.x + (foe.x < g.petX ? 0.155f : -0.155f), foe.y);
            Run(g, input, 3 * Dt);
            return !HasId(g, foe.id) && g.score == score + 1;
        }
        var plain = New<GlassSurvival>(1);
        ToPlay(plain);
        Assert.Equal(0.085f, plain.handRadius, 4);
        Assert.False(Bops(plain));

        var big = ChoosingWith("giant", out int k);
        big.Choose(k);
        Assert.Equal(0.085f * 1.4f, big.handRadius, 4);
        Assert.True(Bops(big));
    }

    [Fact]
    public void Survival_resting_a_hand_on_a_card_takes_it_after_eight_tenths_of_a_second()
    {
        var g = New<GlassSurvival>(5);
        ToPlay(g);
        var input = new GlassInput();
        g.GrantXp(g.XpNeeded);
        g.Step(Dt, input);
        Assert.Equal("choose", g.phase);
        string id = g.ChoiceIds[1];
        int had = g.Abilities[id], lives = g.lives;

        // a hand beside the cards fills nothing; neither does one that is on a card but far from the glass
        Hand(input, 0, g.W * 0.36f, 0.56f);
        Run(g, input, 1.2f);
        Hand(input, 0, g.W * 0.5f, 0.56f, 0.4f);
        Run(g, input, 1.2f);
        Assert.Equal("choose", g.phase);
        foreach (var s in g.sprites) if (s.art == GlassArt.Card) Assert.Equal(0f, s.fill);

        // on the middle card: its ring fills, ticking, and at 0.8 s it is taken
        Hand(input, 0, g.W * 0.5f + 0.05f, 0.62f);
        float t = 0f, lastFill = 0f; int ticks = 0;
        while (g.phase == "choose" && t < 3f)
        {
            g.Step(Dt, input); t += Dt;
            if (g.sounds.Contains("tick")) ticks++;
            if (g.phase != "choose") break;
            var cards = g.sprites.Where(s => s.art == GlassArt.Card).ToArray();
            Assert.True(cards[1].fill >= lastFill && cards[1].fill < 1f);
            Assert.Equal(0f, cards[0].fill); Assert.Equal(0f, cards[2].fill);
            lastFill = cards[1].fill;
        }
        Assert.Equal("play", g.phase);
        Assert.InRange(t, 0.78f, 0.86f);
        Assert.InRange(lastFill, 0.9f, 1f);
        Assert.InRange(ticks, 3, 5);
        Assert.Contains("ta-da", g.sounds);
        if (id == "heart") Assert.Equal(lives + 1, g.lives); else Assert.Equal(had + 1, g.Abilities[id]);

        // a hand that leaves the card lets the ring run down again, and a hand that was already there when the cards
        // came up does not take one by accident at once
        g.GrantXp(g.XpNeeded);
        Hand(input, 0, g.W * 0.22f, 0.56f);
        Run(g, input, 0.7f);                                     // the grace, then the level
        Assert.True(StepUntil(g, input, () => g.phase == "choose", 1f));
        Run(g, input, 0.5f);
        Assert.Equal("choose", g.phase);
        Assert.True(Find(g, s => s.art == GlassArt.Card, out var card));
        Assert.InRange(card.fill, 0.1f, 0.4f);
        input.valid[0] = false;
        Run(g, input, 0.4f);
        Find(g, s => s.art == GlassArt.Card, out card);
        Assert.Equal(0f, card.fill);
        Assert.Equal("choose", g.phase);
    }

    [Fact]
    public void Survival_after_fifteen_seconds_the_first_card_is_taken_by_itself()
    {
        var g = New<GlassSurvival>(6);
        ToPlay(g);
        var input = new GlassInput();
        g.GrantXp(g.XpNeeded);
        g.Step(Dt, input);
        Assert.Equal("choose", g.phase);
        string id = g.ChoiceIds[0];
        int had = g.Abilities[id], lives = g.lives;
        float t = 0f;
        while (g.phase == "choose" && t < 20f) { g.Step(Dt, input); t += Dt; }
        Assert.Equal("play", g.phase);
        Assert.InRange(t, 14.9f, 15.1f);
        if (id == "heart") Assert.Equal(lives + 1, g.lives); else Assert.Equal(had + 1, g.Abilities[id]);
    }

    [Fact]
    public void Survival_every_power_reaches_its_top_and_is_not_offered_again()
    {
        var g = New<GlassSurvival>(7);
        ToPlay(g);
        var input = new GlassInput();
        Assert.Equal(9, GlassSurvival.AbilityIds.Length);
        Assert.Equal(GlassSurvival.AbilityIds.Length, GlassSurvival.AbilityNames.Length);
        Assert.Equal(GlassSurvival.AbilityIds.OrderBy(x => x).ToArray(), g.Abilities.Keys.OrderBy(x => x).ToArray());
        foreach (var kv in g.Abilities) Assert.Equal(kv.Key == "star" ? 1 : 0, kv.Value);
        var notes = new HashSet<string>();
        int picks = 0;
        bool AllAtTop() => GlassSurvival.AbilityIds.All(id => id == "heart" || g.Abilities[id] == GlassSurvival.TopLevel);

        while (!AllAtTop())
        {
            Assert.True(picks < 40);
            int level = g.level;
            g.GrantXp(g.XpNeeded);
            Assert.True(StepUntil(g, input, () => g.phase == "choose", 2f), $"no choice at level {level}");
            Assert.Equal(level + 1, g.level);
            int left = GlassSurvival.AbilityIds.Count(id => id != "heart" && g.Abilities[id] < GlassSurvival.TopLevel) + (g.lives < 6 ? 1 : 0);
            Assert.Equal(Math.Min(3, left), g.choices.Length);
            Assert.Equal(g.ChoiceIds.Length, g.ChoiceIds.Distinct().Count());
            foreach (string id in g.ChoiceIds) if (id != "heart") Assert.True(g.Abilities[id] < GlassSurvival.TopLevel, id + " is at its top and was offered");
            // fewer cards stand in the middle
            g.Step(Dt, input);
            var cards = g.sprites.Where(s => s.art == GlassArt.Card).ToArray();
            Assert.Equal(g.choices.Length, cards.Length);
            Assert.Equal(g.W * 0.5f, cards.Average(c => c.x), 3);
            int k = Array.FindIndex(g.ChoiceIds, id => id != "heart");
            Assert.True(k >= 0);
            string taking = g.ChoiceIds[k];
            int had = g.Abilities[taking];
            notes.Add(g.choiceNotes[k]);
            g.Choose(k);
            Assert.Equal(had + 1, g.Abilities[taking]);
            picks++;
        }
        Assert.Equal(2 + 7 * 3, picks);                         // star from 1 to 3, the seven others from 0 to 3
        Assert.Equal(picks, notes.Count);                       // every step of every power has its own note
        Assert.Equal(0.085f * 2.2f, g.handRadius, 4);
        Assert.Equal(0.42f * 1.75f, g.PetPace, 4);
        Assert.Equal(0.16f * 4.5f, g.MagnetReach, 4);
        Assert.Equal(3, Count(g, GlassArt.Orb));

        // now only hearts are offered, one card, until there are six
        while (g.lives < 6)
        {
            int lives = g.lives;
            g.GrantXp(g.XpNeeded);
            Assert.True(StepUntil(g, input, () => g.phase == "choose", 2f));
            Assert.Equal(new[] { "heart" }, g.ChoiceIds);
            Assert.Equal(new[] { "Extra Heart" }, g.choices);
            g.Choose(0);
            Assert.Equal(lives + 1, g.lives);
        }
        // and then a level is just a level: no choice, the game goes on
        int lv = g.level;
        g.GrantXp(g.XpNeeded);
        Run(g, input, 1f, null, () => Assert.NotEqual("choose", g.phase));
        Assert.Equal(lv + 1, g.level);
        Assert.Equal(6, g.lives);
        foreach (string id in GlassSurvival.AbilityIds) if (id != "heart") Assert.Equal(3, g.Abilities[id]);
    }

    [Fact]
    public void Survival_frost_slows_what_comes_and_shows_on_it()
    {
        float Pace(GlassSurvival g)
        {
            var input = new GlassInput();
            Run(g, input, 0.7f);
            GlassSprite foe = default;
            Assert.True(StepUntil(g, input, () => Find(g, s => s.art == GlassArt.Enemy && MathF.Abs(s.x - g.petX) > 0.8f && s.x > 0f && s.x < g.W, out foe), 8f));
            float x0 = foe.x;
            Run(g, input, 0.25f);
            Assert.True(Find(g, s => s.id == foe.id, out var now));
            Assert.Equal(g.Abilities["frost"] > 0, HasId(g, 1000000 + foe.id));
            return MathF.Abs(now.x - x0) / 0.25f;
        }
        var plain = New<GlassSurvival>(1);
        ToPlay(plain);
        float p = Pace(plain);
        Assert.Equal(0.16f, p, 2);
        Assert.Equal(0, Count(plain, GlassArt.Frost));
        var frost = ChoosingWith("frost", out int k);
        frost.Choose(k);
        Assert.Equal(0.16f * 0.85f, Pace(frost), 2);
        Assert.Equal(frost.EnemyCount, Count(frost, GlassArt.Frost));
    }

    [Fact]
    public void Survival_shock_wave_and_lightning_strike_what_is_near()
    {
        var wave = ChoosingWith("wave", out int k, 6, 3f);
        wave.Choose(k);
        var input = new GlassInput();
        bool ring = false, pushed = false; float widest = 0f;
        for (int i = 0; i < 60 * 60 && wave.phase != "over"; i++)
        {
            if (wave.phase == "choose") { wave.Choose(Math.Max(0, Array.IndexOf(wave.ChoiceIds, "heart"))); continue; }
            wave.Step(Dt, input);
            if (!Find(wave, s => s.art == GlassArt.Ring, out var r)) continue;
            ring = true;
            Assert.Equal(wave.petX, r.x, 3);
            widest = MathF.Max(widest, r.size);
            if (wave.sprites.Any(s => s.art == GlassArt.Puff && MathF.Abs(s.x - wave.petX) < 0.45f)) pushed = true;
        }
        Assert.True(ring, "a ring was drawn");
        Assert.InRange(widest, 0.5f, 2f * 0.38f + 0.01f);       // its diameter: twice the radius of its level
        Assert.True(pushed, "the ring beat something near the partner");

        var bolt = ChoosingWith("bolt", out k, 6, 3f);
        bolt.Choose(k);
        bool struck = false;
        for (int i = 0; i < 60 * 60 && bolt.phase != "over" && !struck; i++)
        {
            if (bolt.phase == "choose") { bolt.Choose(0); continue; }
            var foes = bolt.sprites.Where(IsEnemy).ToArray();
            int score = bolt.score;
            bolt.Step(Dt, input);
            if (!Find(bolt, s => s.art == GlassArt.Bolt && s.alpha == 1f, out var b)) continue;
            // it comes down from the top edge onto an enemy that was there, and that one (1 hp, 2 damage) is beaten
            Assert.InRange(b.y + b.size * 0.5f, 1f, 1.05f);
            float foot = b.y - b.size * 0.5f;
            Assert.Contains(foes, f => MathF.Abs(f.x - b.x) < 0.02f && MathF.Abs(f.y - foot) < 0.03f);
            struck = bolt.score > score;
        }
        Assert.True(struck, "lightning beat an enemy");
    }

    [Fact]
    public void Survival_an_enemy_that_reaches_the_partner_costs_a_heart_and_then_it_has_a_moment()
    {
        var g = New<GlassSurvival>(4, new GamePackDef { spawn = 3f });
        ToPlay(g);
        var input = new GlassInput();
        // the hand leads the partner straight into whatever comes (too high to bop it)
        int lives = g.lives, hits = 0;
        float lastHit = -9f;
        for (int i = 0; i < 5 * 60 * 60 && g.phase != "over"; i++)
        {
            if (g.phase == "choose") { g.Choose(Math.Max(0, Array.FindIndex(g.ChoiceIds, id => id == "magnet" || id == "giant" || id == "swift"))); lives = g.lives; continue; }
            if (Find(g, IsEnemy, out var foe)) Hand(input, 0, Math.Clamp(foe.x, 0.1f, g.W - 0.1f), 0.95f);
            g.Step(Dt, input);
            if (g.lives == lives) continue;
            Assert.Equal(lives - 1, g.lives);                   // one heart at a time
            lives = g.lives; hits++;
            Assert.Contains("error", g.sounds);
            Assert.InRange(g.petHurt, 0.45f, 0.5f);
            Assert.Equal(1, Count(g, GlassArt.Heart));          // the heart that was lost, floating up
            Assert.True(g.seconds - lastHit >= 1.19f, $"hit again after {g.seconds - lastHit} s");
            lastHit = g.seconds;
            if (g.lives > 0) { Assert.Equal("hurt", g.petPose); Assert.True(g.Invulnerable); }
            // what touched it was thrown back: nothing stands on it now
            Assert.DoesNotContain(g.sprites, s => s.art == GlassArt.Enemy && MathF.Abs(s.x - g.petX) < 0.03f);
        }
        Assert.True(hits >= 3);
        Assert.Equal("over", g.phase);
        Assert.Equal("down", g.petPose);
    }

    [Fact]
    public void Survival_never_more_than_forty_at_once()
    {
        // a pack that sends three times as many, and a partner with all the hearts there are
        int most = 0;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = New<GlassSurvival>(seed, new GamePackDef { spawn = 3f, lives = 6, speed = 0.3f });
            var input = new GlassInput();
            for (int i = 0; i < 6 * 60 * 60 && g.phase != "over"; i++)
            {
                if (g.phase == "choose") g.Choose(Math.Max(0, Array.IndexOf(g.ChoiceIds, "heart")));
                g.Step(Dt, input);
                int alive = g.sprites.Count(IsEnemy);
                Assert.True(alive <= GlassSurvival.MaxEnemies && g.EnemyCount <= GlassSurvival.MaxEnemies, $"{alive} at once");
                most = Math.Max(most, alive);
            }
        }
        Assert.Equal(GlassSurvival.MaxEnemies, most);           // the cap was really met
    }

    [Fact]
    public void Survival_a_hand_that_plays_lasts_far_longer_than_no_hand()
    {
        float alone = 0f, played = 0f; int aloneScore = 0, playedScore = 0, playedLevel = 0;
        int[] seeds = { 1, 2, 3, 4, 5 };
        foreach (int seed in seeds)
        {
            var a = PlayOut("survival", seed, false, 30 * 60f);
            var b = PlayOut("survival", seed, true, 30 * 60f);
            Assert.Equal("over", a.phase);
            alone += a.seconds; aloneScore += a.score;
            played += b.seconds; playedScore += b.score; playedLevel += b.level;
            Assert.True(b.seconds > a.seconds, $"seed {seed}: {b.seconds} s with a hand, {a.seconds} s without");
        }
        Assert.True(played > alone * 1.6f, $"{played / seeds.Length} s with a hand, {alone / seeds.Length} s without");
        Assert.True(playedScore > aloneScore * 2, $"{playedScore / seeds.Length} beaten with a hand, {aloneScore / seeds.Length} without");
        Assert.True(playedLevel / seeds.Length >= 8, "a played game gets well into the powers");
    }

    // ------------------------------------------------------------------ umbrella

    [Fact]
    public void Umbrella_with_no_hand_the_ink_reaches_the_partner()
    {
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var g = New<GlassUmbrella>(seed);
            ToPlay(g);
            var input = new GlassInput();
            int lives = g.lives; bool cowered = false, error = false, heart = false; float lastHit = -9f;
            Assert.True(StepUntil(g, input, () => g.phase == "over", 4 * 60f, null), $"seed {seed}");
            Assert.Equal(3, g.Hits);
            Assert.Equal(0, g.Caught);
            Assert.EndsWith(" under your umbrella!", g.result);
            Assert.StartsWith(g.score.ToString() + " star", g.result);

            g = New<GlassUmbrella>(seed);
            ToPlay(g);
            for (int i = 0; i < 4 * 60 * 60 && g.phase != "over"; i++)
            {
                g.Step(Dt, input);
                cowered |= g.petPose == "cower";
                if (g.lives == lives) continue;
                Assert.Equal(lives - 1, g.lives); lives = g.lives;
                Assert.True(g.seconds - lastHit >= 1.19f, "the moment of grace after a hit");
                lastHit = g.seconds;
                error |= g.sounds.Contains("error");
                heart |= Count(g, GlassArt.Heart) == 1;
                if (g.lives > 0) { Assert.Equal("hurt", g.petPose); Assert.InRange(g.petHurt, 0.45f, 0.5f); }
            }
            Assert.True(cowered && error && heart);
            Assert.InRange(g.seconds, 4f, 120f);
        }
    }

    [Fact]
    public void Umbrella_a_hand_over_the_partner_catches_the_drops()
    {
        // three times the rain of level 1 (plain drops, no wind) for as long as level 1 lasts: with a hand that is held
        // over the partner and goes to meet what comes, and with no hand at all - the same game otherwise
        int caughtAll = 0, hitsSheltered = 0, hitsAlone = 0;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var pack = new GamePackDef { spawn = 3f, lives = 6 };
            var g = New<GlassUmbrella>(seed, pack);
            ToPlay(g);
            Assert.Equal(1.1f / 3f, g.DropEvery, 3);
            var b = new BotHand();
            int splashes = 0, drops = 0; float played = 0f;
            for (int i = 0; i < 60 * 60 && g.level == 1 && g.phase == "play"; i++)
            {
                PlayUmbrella(g, b, false);
                int caught = g.Caught;
                g.Step(Dt, b.input); played += Dt;
                if (g.Caught > caught)
                {
                    drops += g.sounds.Contains("drop") ? 1 : 0;
                    Assert.True(Find(g, s => s.art == GlassArt.Splash && MathF.Abs(s.y - b.y) < 0.13f && MathF.Abs(s.x - b.x) < 0.13f, out _), "a splash at the hand");
                    splashes++;
                }
                Assert.DoesNotContain(g.sprites, s => s.art == GlassArt.Blob);
            }
            Assert.Equal(2, g.level);
            Assert.Equal(5, g.Stars);
            Assert.True(g.Caught >= 2 && splashes == g.Caught && drops >= 2, $"seed {seed}: {g.Caught} caught");
            caughtAll += g.Caught; hitsSheltered += g.Hits;

            var alone = New<GlassUmbrella>(seed, pack);
            ToPlay(alone);
            Run(alone, new GlassInput(), played);
            Assert.Equal(0, alone.Caught);
            hitsAlone += alone.Hits;
        }
        Assert.True(caughtAll >= 30, $"{caughtAll} caught");
        Assert.True(hitsAlone >= 9, $"{hitsAlone} hits with no hand");
        Assert.True(hitsSheltered * 5 <= hitsAlone, $"{hitsSheltered} hits under the hand ({caughtAll} caught), {hitsAlone} with no hand");

        // a hand that simply stays over it: the partner is not afraid, and walks on to its stars
        var calm = New<GlassUmbrella>(1);
        ToPlay(calm);
        var still = new BotHand();
        Run(calm, still.input, 8f, () => PlayUmbrella(calm, still, false, false), () => Assert.NotEqual("cower", calm.petPose));
        Assert.True(calm.Stars >= 3);
    }

    [Fact]
    public void Umbrella_a_hand_far_from_the_glass_or_far_from_the_partner_is_no_umbrella()
    {
        var g = New<GlassUmbrella>(2);
        ToPlay(g);
        var input = new GlassInput();
        for (int i = 0; i < 4 * 60 * 60 && g.phase != "over"; i++)
        {
            Hand(input, 0, g.petX, 0.34f, 0.35f);                // over it, but 35 cm from the glass
            Hand(input, 1, g.petX > g.W * 0.5f ? 0.1f : g.W - 0.1f, 0.9f, 0.05f);     // at the glass, but in a far corner
            g.Step(Dt, input);
        }
        Assert.Equal("over", g.phase);
        Assert.Equal(3, g.Hits);
    }

    [Fact]
    public void Umbrella_the_partner_gathers_stars_by_itself_and_five_make_a_level()
    {
        var g = New<GlassUmbrella>(3, new GamePackDef { lives = 6 });
        var b = new BotHand();
        var input = new GlassInput();
        g.Step(Dt, input);
        Assert.Equal(3, Count(g, GlassArt.Star));
        foreach (var s in g.sprites) if (s.art == GlassArt.Star) { Assert.True(MathF.Abs(s.x - g.petX) >= 0.2f, "a star under the partner"); Assert.InRange(s.y, 0.02f, 0.08f); }
        ToPlay(g);
        int stars = 0, level = 1; bool picked = false, walked = false;
        var banners = new List<string>();
        for (int i = 0; i < 3 * 60 * 60 && g.phase != "over" && g.level < 4; i++)
        {
            PlayUmbrella(g, b);
            var before = g.sprites.Where(s => s.art == GlassArt.Star).ToArray();
            float x0 = g.petX; int score = g.score, gold = g.GoldTaken;
            g.Step(Dt, b.input);
            Assert.Equal(3, Count(g, GlassArt.Star));
            Assert.Equal(1 + g.Stars / GlassUmbrella.StarsPerLevel, g.level);
            Assert.True(MathF.Abs(g.petX - x0) <= GlassUmbrella.PetSpeed * Dt + 1e-4f);
            if (g.petPose == "walk")
            {
                walked = true;
                // towards the nearest star
                float nearest = before.OrderBy(s => MathF.Abs(s.x - x0)).First().x;
                Assert.True(MathF.Abs(nearest - g.petX) < MathF.Abs(nearest - x0));
                Assert.Equal(nearest > x0, g.petFacesRight);
            }
            picked |= g.petPose == "pick";
            if (g.Stars > stars)
            {
                stars = g.Stars;
                Assert.Equal(score + 1 + 3 * (g.GoldTaken - gold), g.score);
                Assert.Contains("sparkle", g.sounds);
                // the star it stood on is gone, a new one is somewhere else
                Assert.Contains(before, s => MathF.Abs(s.x - g.petX) < 0.03f && !HasId(g, s.id));
                foreach (var s in g.sprites) if (s.art == GlassArt.Star) Assert.True(MathF.Abs(s.x - g.petX) > 0.15f);
            }
            if (g.level > level) { level = g.level; banners.Add(g.banner); Assert.Contains("ta-da", g.sounds); }
        }
        Assert.True(walked && picked);
        Assert.Equal(4, g.level);
        Assert.Equal(15, g.Stars);
        Assert.Equal(new[] { "Level 2", "Level 3", "Level 4" }, banners.ToArray());
        Assert.Equal(g.Stars + 3 * g.GoldTaken, g.score);
    }

    [Fact]
    public void Umbrella_a_golden_drop_let_through_is_three_stars_and_a_caught_one_is_wasted()
    {
        int taken = 0, wasted = 0, slipped = 0;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            // the same game twice: a hand that lifts for gold, and a hand that never lifts
            for (int pass = 0; pass < 2; pass++)
            {
                bool lifts = pass == 0;
                var g = New<GlassUmbrella>(seed, new GamePackDef { lives = 6 });
                ToPlay(g);
                var b = new BotHand();
                for (int i = 0; i < 60 * 60 && g.phase != "over"; i++)
                {
                    PlayUmbrella(g, b, lifts, lifts);                   // the one that never lifts does not leave its place over the partner either
                    int score = g.score, stars = g.Stars, t = g.GoldTaken, w = g.GoldWasted;
                    var golds = g.sprites.Where(s => s.art == GlassArt.Gold).ToArray();
                    g.Step(Dt, b.input);
                    if (g.GoldTaken > t)
                    {
                        if (lifts) taken++;
                        Assert.Equal(score + 3 + (g.Stars - stars), g.score);
                        Assert.Contains("coin", g.sounds);
                        Assert.Contains(golds, s => MathF.Abs(s.x - g.petX) < 0.12f && s.y < 0.3f && !HasId(g, s.id));
                        Assert.Contains(g.sprites, s => s.art == GlassArt.Label && s.text == "+3");
                    }
                    if (g.GoldWasted > w)
                    {
                        Assert.Equal(score + (g.Stars - stars), g.score);
                        Assert.Contains("squeak", g.sounds);
                        Assert.DoesNotContain("coin", g.sounds);
                        Assert.Contains(golds, s => MathF.Abs(s.x - b.x) < 0.14f && MathF.Abs(s.y - b.y) < 0.14f && !HasId(g, s.id));
                        Assert.Contains(g.sprites, s => s.art == GlassArt.Puff);
                        wasted++;
                    }
                }
                if (lifts) Assert.Equal(0, g.GoldWasted);
                else { slipped += g.GoldTaken; Assert.True(g.GoldWasted >= 1, $"seed {seed}: no gold in a minute"); }
                Assert.Equal(g.Stars + 3 * g.GoldTaken, g.score);
            }
        }
        Assert.True(wasted >= 12, $"{wasted} wasted by the hand that never lifts");
        Assert.True(slipped * 8 <= wasted, $"{slipped} slipped past the edge of the hand that never lifts, {wasted} wasted");
        Assert.True(taken >= wasted * 2 / 3, $"{taken} taken by the hand that lifts, {wasted} wasted by the other");
    }

    [Fact]
    public void Umbrella_it_rains_harder_level_by_level_with_blobs_from_two_and_wind_from_three()
    {
        var g = New<GlassUmbrella>(4, new GamePackDef { lives = 6 });
        ToPlay(g);
        Assert.Equal(1.1f, g.DropEvery, 3); Assert.Equal(0.45f, g.FallSpeed, 3);
        var b = new BotHand();
        var born = new Dictionary<int, HashSet<int>>();           // level -> ids
        var time = new Dictionary<int, float>();
        var arts = new Dictionary<int, HashSet<string>>();
        var seen = new HashSet<int>();
        var windSigns = new HashSet<int>();
        float windAt3 = -1f, windy = 0f, blowing = 0f; bool slanted = false, steep = false, whoosh = false;
        for (int i = 0; i < 8 * 60 * 60 && g.phase != "over" && g.level < 7; i++)
        {
            PlayUmbrella(g, b);
            g.Step(Dt, b.input);
            int l = g.level;
            if (l == 6) { Assert.Equal(0.45f, g.DropEvery, 3); Assert.Equal(0.8f, g.FallSpeed, 3); }
            if (!born.ContainsKey(l)) { born[l] = new HashSet<int>(); arts[l] = new HashSet<string>(); time[l] = 0f; }
            time[l] += Dt;
            foreach (var s in g.sprites)
            {
                if (s.art != GlassArt.Drop && s.art != GlassArt.Blob && s.art != GlassArt.Gold) continue;
                if (seen.Add(s.id)) { born[l].Add(s.id); arts[l].Add(s.art); }
                if (l < 3 && s.art != GlassArt.Gold) Assert.Equal(0f, s.rot);
                if (l >= 3 && s.art == GlassArt.Drop && MathF.Abs(s.rot) > 3f) slanted = true;
                if (s.art != GlassArt.Gold && MathF.Abs(s.rot) > 30f) { Assert.True(l >= 5, "side rain before level 5"); steep = true; }
            }
            if (l < 3) Assert.Equal(0f, g.Wind);
            else
            {
                if (windAt3 < 0f) windAt3 = g.seconds;
                windy += Dt;
                if (MathF.Abs(g.Wind) > 0.05f) { blowing += Dt; windSigns.Add(MathF.Sign(g.Wind)); }
                Assert.InRange(MathF.Abs(g.Wind), 0f, 0.26f);
                whoosh |= g.sounds.Contains("whoosh");
            }
        }
        Assert.True(g.level >= 6, $"the bot got to level {g.level}");
        Assert.DoesNotContain(GlassArt.Blob, arts[1]);
        Assert.True(arts[2].Contains(GlassArt.Blob) || arts[3].Contains(GlassArt.Blob), "blobs from level 2");
        Assert.True(slanted, "the wind slants the drops");
        Assert.True(blowing > windy * 0.75f, $"from level 3 it blew for {blowing} of {windy} s");      // but for the moments in which it turns
        Assert.True(windSigns.Contains(1) && windSigns.Contains(-1), "the wind turns");
        Assert.True(whoosh);
        Assert.True(steep, "from level 5 a second stream comes in from the side");
        // drops a second: about 1 / 1.1 at level 1, more at every level
        float Rate(int l) => born[l].Count / time[l];
        Assert.InRange(Rate(1), 0.6f, 1.2f);
        Assert.True(Rate(3) > Rate(1) && Rate(5) > Rate(3) * 1.3f, $"{Rate(1)} {Rate(2)} {Rate(3)} {Rate(4)} {Rate(5)}");
    }

    [Fact]
    public void Umbrella_a_blob_needs_the_middle_of_the_hand()
    {
        // a hand held still: drops are caught out to its edge (and a little beyond), blobs only near its middle
        float farthestDrop = 0f, farthestBlob = 0f; int blobs = 0, blobsPast = 0;
        foreach (int seed in new[] { 1, 2, 3, 4 })
        {
            var g = New<GlassUmbrella>(seed, new GamePackDef { lives = 6, spawn = 3f });
            ToPlay(g);
            var input = new GlassInput();
            var b = new BotHand();
            for (int i = 0; i < 3 * 60 * 60 && g.phase != "over"; i++)
            {
                float hx = g.petX, hy = 0.5f;
                Hand(input, 0, hx, hy);
                var ink = g.sprites.Where(s => s.art == GlassArt.Drop || s.art == GlassArt.Blob).ToArray();
                int caught = g.Caught;
                g.Step(Dt, input);
                foreach (var s in ink)
                {
                    float d = MathF.Sqrt((s.x - hx) * (s.x - hx) + (s.y - hy) * (s.y - hy));
                    if (!HasId(g, s.id) && d < 0.2f && g.Caught > caught)
                    {
                        if (s.art == GlassArt.Drop) farthestDrop = MathF.Max(farthestDrop, d);
                        else { farthestBlob = MathF.Max(farthestBlob, d); blobs++; }
                    }
                    else if (s.art == GlassArt.Blob && HasId(g, s.id) && s.y < hy && s.y > hy - 0.03f && MathF.Abs(s.x - hx) < 0.1f) blobsPast++;
                }
            }
        }
        Assert.True(blobs >= 5 && blobsPast >= 3, $"{blobs} blobs caught, {blobsPast} slipped past the edge of the hand");
        Assert.InRange(farthestDrop, 0.085f, 0.085f + 0.05f);
        Assert.InRange(farthestBlob, 0.03f, 0.085f * GlassUmbrella.BlobNeedsMiddle + 0.03f);
    }

    [Fact]
    public void Umbrella_a_hand_that_shelters_gets_far_more_stars_than_no_hand()
    {
        float alone = 0f, played = 0f; int aloneScore = 0, playedScore = 0;
        int[] seeds = { 1, 2, 3, 4, 5 };
        foreach (int seed in seeds)
        {
            var a = PlayOut("umbrella", seed, false, 20 * 60f);
            var b = PlayOut("umbrella", seed, true, 20 * 60f);
            Assert.Equal("over", a.phase);
            alone += a.seconds; aloneScore += a.score; played += b.seconds; playedScore += b.score;
            Assert.True(b.score > a.score + 10, $"seed {seed}: {b.score} with a hand, {a.score} without");
        }
        Assert.True(played > alone * 3f, $"{played / seeds.Length} s with a hand, {alone / seeds.Length} s without");
        Assert.True(playedScore > aloneScore * 4, $"{playedScore / seeds.Length} stars with a hand, {aloneScore / seeds.Length} without");
    }
}
