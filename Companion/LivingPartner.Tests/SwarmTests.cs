using System.Numerics;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>Screen Scamps (Shared/Swarm.cs): the monsters that come out of the screen, the bolts along the finger, the augments, winning and losing.</summary>
public class SwarmTests
{
    [Fact]
    public void Bolts_cross_the_display_and_damage_a_demon_before_it_emerges()
    {
        var s = New();
        for (int i = 1; i < s.monsters.Length; i++) s.monsters[i].alive = false;
        ref var demon = ref s.monsters[0];
        demon.du = demon.dv = demon.u = demon.v = 0;
        float health = demon.hp;
        bool crossed = false;
        for (int i = 0; i < 120 && demon.hp == health; i++)
        {
            s.Step(Dt, Aim(demon.pos));
            foreach (var bolt in s.bolts)
                if (bolt.alive && Vector3.Dot(bolt.pos - s.arena.center, s.arena.outward) < 0) crossed = true;
        }
        Assert.True(crossed);
        Assert.Equal(SwarmState.Inside, demon.state);
        Assert.True(demon.hp < health);
    }

    [Fact]
    public void A_calibrated_living_room_TV_under_the_gaze_wins_over_a_nearby_side_monitor()
    {
        float Rank(Vector3 center, float width, float height, Vector3 normal, bool occupied) =>
            SwarmRules.ScreenScore(Vector3.Zero,Vector3.UnitZ,center,Vector3.UnitX,Vector3.UnitY,normal,width,height,occupied);
        float tv = Rank(new Vector3(0,0,3.5f),1.4f,.8f,-Vector3.UnitZ,false);
        float monitor = Rank(new Vector3(.7f,0,1f),.45f,.28f,-Vector3.UnitZ,true);
        Assert.True(float.IsFinite(tv) && tv < monitor);
        Assert.True(float.IsPositiveInfinity(Rank(new Vector3(0,0,2f),1f,.6f,Vector3.UnitZ,false)));
    }

    const float Dt = 1f / 72f;
    static readonly Vector3 Chest = new(0f, 1.05f, 0f), Finger = new(0.14f, 0.98f, 0.30f);

    static SwarmArena Arena() => new SwarmArena
    {
        center = new Vector3(0f, 1.0f, 0.75f), right = Vector3.UnitX, up = Vector3.UnitY, outward = -Vector3.UnitZ, halfW = 0.26f, halfH = 0.15f, deskY = 0.75f,
    };

    static SwarmSim New(uint seed = 7)
    {
        var s = new SwarmSim();
        s.Start(seed, Arena());
        return s;
    }

    static SwarmInput Idle() => new SwarmInput { target = Chest };
    static SwarmInput Aim(Vector3 at) => new SwarmInput { target = Chest, aimValid = true, aimOrigin = Finger, aimDir = Vector3.Normalize(at - Finger), handValid = true, hand = Finger - new Vector3(0, 0.03f, 0.08f) };

    /// <summary>A player: points at the monster nearest to the chest, turning the finger at `turn` per second; takes the cards in the order of `likes`.</summary>
    sealed class Bot
    {
        public float turn = 6f; public int[] likes = System.Array.Empty<int>();
        Vector3 _dir = Vector3.UnitZ;
        public float elapsed;
        public SwarmInput Input(SwarmSim s)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < SwarmSim.MaxMonsters; i++)
            {
                var m = s.monsters[i];
                if (!m.alive || m.state == SwarmState.Inside) continue;
                float d = (m.pos - Chest).Length();
                if (d < bd) { bd = d; best = i; }
            }
            Vector3 want = best >= 0 ? Vector3.Normalize(s.monsters[best].pos - Finger) : Vector3.Normalize(s.arena.center - Finger);
            _dir = Vector3.Normalize(Vector3.Lerp(_dir, want, System.Math.Min(1f, turn * Dt)));
            var inp = Aim(Finger + _dir);
            return inp;
        }
        public void Run(SwarmSim s, float seconds)
        {
            for (float t = 0; t < seconds && s.phase != SwarmPhase.Over; t += Dt)
            {
                elapsed += Dt;
                if (s.phase == SwarmPhase.Choosing)
                {
                    int card = 0, rank = int.MaxValue;
                    for (int c = 0; c < 3; c++) { int r = System.Array.IndexOf(likes, s.choices[c]); if (r >= 0 && r < rank) { rank = r; card = c; } }
                    Assert.True(s.Choose(card));
                    continue;
                }
                s.Step(Dt, Input(s));
            }
        }
    }

    static int Count(SwarmSim s, int state) { int n = 0; foreach (var m in s.monsters) if (m.alive && m.state == state) n++; return n; }
    static void Play(SwarmSim s) { var idle = Idle(); while (s.phase == SwarmPhase.Intro) s.Step(Dt, idle); foreach (ref var m in s.monsters.AsSpan()) m.alive = false; s.alive = 0; }

    [Fact]
    public void The_first_crowd_gathers_behind_the_glass_and_bursts_out_together()
    {
        var s = New();
        Assert.Equal(SwarmPhase.Intro, s.phase);
        Assert.Equal(8, Count(s, SwarmState.Inside));
        var a = Arena();
        int emerged = 0; float firstOut = -1, lastOut = -1, t = 0;
        var inp = Idle();
        Assert.True(Vector3.Dot(s.monsters[0].pos - a.center, a.outward) < -0.8f);
        for (; t < 5f; t += Dt)
        {
            s.Step(Dt, inp);
            for (int e = 0; e < s.eventCount; e++) if (s.events[e].type == SwarmEv.Emerge) { emerged++; if (firstOut < 0) firstOut = t; lastOut = t; }
            foreach (var m in s.monsters)
            {
                if (!m.alive || m.state != SwarmState.Inside) continue;
                // behind the glass they stay inside the rectangle, across the entire approach
                Vector3 d = m.pos - a.center;
                Assert.InRange(Vector3.Dot(d, a.right), -a.halfW, a.halfW);
                Assert.InRange(Vector3.Dot(d, a.up), -a.halfH, a.halfH);
                Assert.InRange(Vector3.Dot(d, a.outward), -s.StageDepth - 0.001f, 0.001f);
                Assert.Equal(m.maxHp, m.hp);
            }
            if (t < SwarmSim.IntroSeconds - 0.1f) Assert.Equal(0, emerged);
        }
        Assert.Equal(8, emerged);
        Assert.InRange(firstOut, SwarmSim.IntroSeconds - 0.05f, SwarmSim.IntroSeconds + 0.2f);
        Assert.True(lastOut - firstOut < 0.6f, "the crowd comes out together");
        Assert.Equal(SwarmPhase.Play, s.phase);
    }

    [Fact]
    public void What_comes_out_moves_toward_the_user_and_costs_a_heart_when_it_arrives()
    {
        var s = New();
        Play(s);
        int i = s.Put(SwarmKind.Tank, new Vector3(0, 1.05f, 0.5f));
        float d0 = (s.monsters[i].pos - Chest).Length();
        var idle = Idle(); bool lost = false;
        for (float t = 0; t < 1f; t += Dt) s.Step(Dt, idle);
        Assert.True((s.monsters[i].pos - Chest).Length() < d0 - 0.05f);
        for (float t = 0; t < 12f && !lost; t += Dt)
        {
            s.Step(Dt, idle);
            for (int e = 0; e < s.eventCount; e++) if (s.events[e].type == SwarmEv.HeartLost) lost = true;
        }
        Assert.True(lost);
        Assert.Equal(SwarmSim.StartHearts - 1, s.hearts);
        Assert.False(s.monsters[i].alive);
    }

    [Fact]
    public void An_idle_player_loses_but_not_in_the_first_25_seconds()
    {
        foreach (uint seed in new uint[] { 1, 7, 99, 2026 })
        {
            var s = New(seed); var idle = Idle(); float t = 0;
            for (; t < 200f && s.phase != SwarmPhase.Over; t += Dt) { s.Step(Dt, idle); Assert.NotEqual(SwarmPhase.Choosing, s.phase); }
            Assert.Equal(SwarmPhase.Over, s.phase);
            Assert.False(s.won);
            Assert.Equal(0, s.hearts);
            Assert.InRange(t, 25f, 90f);
        }
    }

    [Fact]
    public void A_decent_player_reaches_the_boss()
    {
        foreach (uint seed in new uint[] { 3, 11, 500 })
        {
            var s = New(seed);
            var bot = new Bot { turn = 5f, likes = new[] { SwarmAug.Heal, SwarmAug.Twin, SwarmAug.Rapid, SwarmAug.Explode, SwarmAug.Chain, SwarmAug.Big, SwarmAug.Pierce } };      // (a slow finger, sensible picks)
            for (int k = 0; k < 60 && !s.bossOut && s.phase != SwarmPhase.Over; k++) bot.Run(s, 5f);
            Assert.True(s.bossOut, $"seed {seed}: over at {s.time:0} s with {s.hearts} hearts, level {s.level}, {s.kills} popped");
            Assert.True(s.level >= 6, "levels: " + s.level);
            Assert.True(s.kills >= 100, "popped: " + s.kills);
        }
    }

    [Fact]
    public void A_good_player_wins_and_the_run_takes_about_three_minutes()
    {
        var s = New(42);
        var bot = new Bot { turn = 14f, likes = new[] { SwarmAug.Twin, SwarmAug.Rapid, SwarmAug.Big, SwarmAug.Explode, SwarmAug.Chain, SwarmAug.Pierce, SwarmAug.Orbit, SwarmAug.Comet } };
        bot.Run(s, 400f);
        Assert.Equal(SwarmPhase.Over, s.phase);
        Assert.True(s.won, $"lost at {s.time:0} s, level {s.level}, {s.kills} popped, boss out {s.bossOut}");
        Assert.True(s.bossDead);
        Assert.InRange(s.time, SwarmSim.BossAt, SwarmSim.BossAt + 60f);
        Assert.Equal(0, s.alive);
        Assert.True(s.score > 1000);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        (int, int, float, int) Go(uint seed) { var s = New(seed); new Bot { turn = 6f }.Run(s, 70f); return (s.kills, s.score, s.time, s.xp); }
        Assert.Equal(Go(5), Go(5));
        Assert.NotEqual(Go(5), Go(6));
    }

    [Fact]
    public void Bolts_fire_by_themselves_along_the_finger_and_bend_a_little_to_what_is_near_its_line()
    {
        var s = New(); Play(s);
        // straight ahead of the finger: hit
        Vector3 ahead = Finger + new Vector3(0, 0, 0.45f);
        int a = s.Put(SwarmKind.Tank, ahead + new Vector3(0.05f, 0.02f, 0));       // a little off the line: the aim helps
        int far = s.Put(SwarmKind.Tank, Finger + new Vector3(0.45f, 0, 0.30f));    // well off the line: left alone
        var inp = Aim(ahead); int fired = 0;
        for (float t = 0; t < 1.5f; t += Dt) { s.Step(Dt, inp); for (int e = 0; e < s.eventCount; e++) if (s.events[e].type == SwarmEv.Fire) fired++; s.monsters[a].pos = ahead + new Vector3(0.05f, 0.02f, 0); s.monsters[far].pos = Finger + new Vector3(0.45f, 0, 0.30f); }
        Assert.InRange(fired, 3, 4);                    // a steady rhythm: one every 0.46 s
        Assert.True(s.monsters[a].hp < s.monsters[a].maxHp);
        Assert.Equal(s.monsters[far].maxHp, s.monsters[far].hp);
        // no pointing hand: no bolts
        var s2 = New(); Play(s2); var idle = Idle(); fired = 0;
        for (float t = 0; t < 2f; t += Dt) { s2.Step(Dt, idle); for (int e = 0; e < s2.eventCount; e++) if (s2.events[e].type == SwarmEv.Fire) fired++; }
        Assert.Equal(0, fired);
    }

    [Fact]
    public void A_level_up_slows_time_to_a_stop_and_offers_three_different_augments()
    {
        var s = New(); Play(s);
        var inp = Aim(Arena().center);
        s.GiveXp(s.XpNeed);
        int i = s.Put(SwarmKind.Tank, new Vector3(0.3f, 1.0f, 0.6f));
        bool levelled = false, offered = false; float t = 0;
        for (; t < 2f && s.phase != SwarmPhase.Choosing; t += Dt)
        {
            float before = s.slow;
            s.Step(Dt, inp);
            for (int e = 0; e < s.eventCount; e++) { if (s.events[e].type == SwarmEv.LevelUp) levelled = true; if (s.events[e].type == SwarmEv.Choices) offered = true; }
            if (levelled && s.phase == SwarmPhase.Play) Assert.True(s.slow <= before);
        }
        Assert.True(levelled); Assert.True(offered);
        Assert.Equal(SwarmPhase.Choosing, s.phase);
        Assert.Equal(2, s.level);
        Assert.InRange(t, 0.3f, 0.7f);
        Assert.True(s.choices[0] != s.choices[1] && s.choices[1] != s.choices[2] && s.choices[0] != s.choices[2]);
        foreach (int c in s.choices) { Assert.InRange(c, 0, SwarmAug.Count - 1); Assert.NotEqual(SwarmAug.Heal, c); }      // (all hearts are there: no tea is offered)
        // nothing moves while the cards are up
        Vector3 at = s.monsters[i].pos; float time = s.time;
        for (int k = 0; k < 100; k++) s.Step(Dt, inp);
        Assert.Equal(at, s.monsters[i].pos); Assert.Equal(time, s.time);
        Assert.False(s.Choose(3));
        int picked = s.choices[1];
        Assert.True(s.Choose(1));
        Assert.Equal(1, s.aug[picked]);
        Assert.Equal(SwarmPhase.Play, s.phase);
        Assert.False(s.Choose(0));
        for (int k = 0; k < 60; k++) s.Step(Dt, inp);
        Assert.Equal(1f, s.slow);
        Assert.NotEqual(at, s.monsters[i].pos);
    }

    [Fact]
    public void Two_levels_at_once_are_two_choices()
    {
        var s = New(); Play(s);
        s.GiveXp(4 + 7);
        var inp = Idle();
        for (int k = 0; k < 200 && s.phase != SwarmPhase.Choosing; k++) s.Step(Dt, inp);
        Assert.Equal(3, s.level);
        Assert.True(s.Choose(0)); Assert.Equal(SwarmPhase.Choosing, s.phase);
        Assert.True(s.Choose(0)); Assert.Equal(SwarmPhase.Play, s.phase);
    }

    static int Fires(SwarmSim s, SwarmInput inp, float seconds, out int bolts)
    {
        int n = 0; bolts = 0;
        for (float t = 0; t < seconds; t += Dt) { s.Step(Dt, inp); for (int e = 0; e < s.eventCount; e++) if (s.events[e].type == SwarmEv.Fire) { n++; bolts += s.events[e].a; } }
        return n;
    }

    [Fact]
    public void Quick_sparks_and_twin_bolts_put_more_light_in_the_air()
    {
        var inp = Aim(Arena().center);
        var s = New(); Play(s); int plain = Fires(s, inp, 5f, out int plainBolts);
        var q = New(); Play(q); q.aug[SwarmAug.Rapid] = 3; int quick = Fires(q, inp, 5f, out _);
        var w = New(); Play(w); w.aug[SwarmAug.Twin] = 2; int twin = Fires(w, inp, 5f, out int twinBolts);
        Assert.Equal(plain, plainBolts);
        Assert.True(quick >= plain * 1.8f, $"{quick} vs {plain}");
        Assert.Equal(twin * 3, twinBolts);
        Assert.Equal(plain, twin);
    }

    [Fact]
    public void Needle_light_goes_through_and_big_bolts_hit_harder()
    {
        Vector3 p1 = Finger + new Vector3(0, 0, 0.30f), p2 = Finger + new Vector3(0, 0, 0.48f);
        float Second(int pierce, int big, out float first)
        {
            var s = New(); Play(s); s.aug[SwarmAug.Pierce] = pierce; s.aug[SwarmAug.Big] = big;
            int a = s.Put(SwarmKind.Boss, p1), b = s.Put(SwarmKind.Boss, p2 + new Vector3(0, 0, 0.2f));
            var inp = Aim(p1);
            for (float t = 0; t < 0.6f; t += Dt) { s.Step(Dt, inp); s.monsters[a].pos = p1; s.monsters[b].pos = p2 + new Vector3(0, 0, 0.2f); }
            first = s.monsters[a].maxHp - s.monsters[a].hp;
            return s.monsters[b].maxHp - s.monsters[b].hp;
        }
        Assert.Equal(0f, Second(0, 0, out float plain));
        Assert.True(plain > 0);
        Assert.True(Second(1, 0, out _) > 0);
        Second(0, 2, out float big);
        Assert.True(big > plain);
    }

    [Fact]
    public void Pop_bloom_and_chain_spark_reach_the_neighbours()
    {
        Vector3 p1 = Finger + new Vector3(0, 0, 0.40f), beside = p1 + new Vector3(0.12f, 0.10f, 0.02f);
        float Neighbour(int explode, int chain, out bool sawExplosion, out bool sawChain)
        {
            var s = New(); Play(s); s.aug[SwarmAug.Explode] = explode; s.aug[SwarmAug.Chain] = chain;
            int a = s.Put(SwarmKind.Tank, p1), b = s.Put(SwarmKind.Tank, beside);
            s.monsters[a].hp = s.monsters[a].maxHp = 400; s.monsters[b].hp = s.monsters[b].maxHp = 400;
            var inp = Aim(p1 - new Vector3(0.03f, 0.03f, 0)); sawExplosion = sawChain = false;      // (aimed away from the neighbour: the first bolt is for p1)
            for (float t = 0; t < 0.5f; t += Dt)
            {
                s.Step(Dt, inp); s.monsters[a].pos = p1; s.monsters[b].pos = beside;
                for (int e = 0; e < s.eventCount; e++) { if (s.events[e].type == SwarmEv.Explosion) sawExplosion = true; if (s.events[e].type == SwarmEv.Chain) sawChain = true; }
            }
            Assert.True(s.monsters[a].hp < 400);
            return 400 - s.monsters[b].hp;
        }
        Assert.Equal(0f, Neighbour(0, 0, out bool ex, out bool ch)); Assert.False(ex); Assert.False(ch);
        Assert.True(Neighbour(3, 0, out ex, out _) > 0); Assert.True(ex);
        Assert.True(Neighbour(0, 1, out _, out ch) > 0); Assert.True(ch);
    }

    [Fact]
    public void Orbit_mist_and_player_cast_comets_apply_their_effects()
    {
        // stars circle the hand and bonk what they touch
        var s = New(); Play(s); s.aug[SwarmAug.Orbit] = 1;
        var inp = Idle(); inp.handValid = true; inp.hand = new Vector3(0.1f, 1.0f, 0.3f);
        int m = s.Put(SwarmKind.Tank, inp.hand + new Vector3(0.13f, 0, 0));
        for (float t = 0; t < 1.5f; t += Dt) { s.Step(Dt, inp); if (s.monsters[m].alive) s.monsters[m].pos = inp.hand + new Vector3(0.13f, 0, 0); }
        Assert.Equal(2, s.orbitCount);
        Assert.True(!s.monsters[m].alive || s.monsters[m].hp < s.monsters[m].maxHp);

        // the mist: what is near the user is slower
        float Travel(int mist)
        {
            var q = New(); Play(q); q.aug[SwarmAug.Slow] = mist;
            int k = q.Put(SwarmKind.Tank, Chest + new Vector3(0, 0, 0.34f));
            var idle = Idle(); Vector3 from = q.monsters[k].pos;
            for (float t = 0; t < 1f; t += Dt) q.Step(Dt, idle);
            return (q.monsters[k].pos - from).Length();
        }
        Assert.True(Travel(2) < Travel(0) * 0.7f);

        var b = New(); Play(b); b.aug[SwarmAug.Comet] = 1;
        int target = b.Put(SwarmKind.Boss, new Vector3(0.2f, 1.0f, 0.6f)); int throws = 0;
        var bi = Aim(b.monsters[target].pos);
        for (float t = 0; t < 4f; t += Dt) { b.Step(Dt, bi); b.monsters[target].pos = new Vector3(0.2f, 1.0f, 0.6f); for (int e = 0; e < b.eventCount; e++) if (b.events[e].type == SwarmEv.CometFire) throws++; }
        Assert.InRange(throws, 2, 4);
        Assert.True(b.monsters[target].hp < b.monsters[target].maxHp);
    }

    [Fact]
    public void Honey_tea_heals_and_the_magnet_makes_gems_worth_more()
    {
        var s = New(); Play(s);
        s.hearts = 2; s.GiveXp(s.XpNeed);
        var idle = Idle();
        for (int k = 0; k < 100 && s.phase != SwarmPhase.Choosing; k++) s.Step(Dt, idle);
        s.choices[0] = SwarmAug.Heal;
        Assert.True(s.Choose(0));
        Assert.Equal(4, s.hearts);

        int Xp(int magnet)
        {
            var q = New(); Play(q); q.aug[SwarmAug.Magnet] = magnet;
            var inp = Aim(Finger + new Vector3(0, 0, 0.4f));
            for (int n = 0; n < 4; n++)
            {
                int k = q.Put(SwarmKind.Mini, Finger + new Vector3(0, 0, 0.4f));
                for (float t = 0; t < 2.5f; t += Dt) { q.Step(Dt, inp); if (q.phase == SwarmPhase.Choosing) q.Choose(0); }
            }
            return q.xp + (q.level - 1) * 100;
        }
        Assert.Equal(100, Xp(0));      // (four gems of one: exactly the first level)
        Assert.True(Xp(3) > 100);
    }

    [Fact]
    public void Twins_split_into_two_little_ones_and_gems_fly_to_the_user_by_themselves()
    {
        var s = New(); Play(s);
        Vector3 at = Finger + new Vector3(0, 0, 0.4f);
        s.Put(SwarmKind.Splitter, at);
        var inp = Aim(at); bool popped = false; int gems = 0;
        for (float t = 0; t < 2.2f; t += Dt)
        {
            s.Step(Dt, inp);
            for (int e = 0; e < s.eventCount; e++) { if (s.events[e].type == SwarmEv.Pop && s.events[e].a == SwarmKind.Splitter) { popped = true; int minis = 0; foreach (var m in s.monsters) if (m.alive && m.kind == SwarmKind.Mini) minis++; Assert.Equal(2, minis); } if (s.events[e].type == SwarmEv.Gem) gems++; }
        }
        Assert.True(popped);
        Assert.True(gems >= 1);
        Assert.True(s.xp >= 2);
    }

    [Fact]
    public void The_boss_squeezes_out_at_the_end_and_popping_it_wins()
    {
        var s = New(); Play(s);
        var inp = Idle(); s.hearts = s.maxHearts = 99;
        s.SkipTo(SwarmSim.BossAt - 0.5f);
        bool warned = false, came = false;
        for (float t = 0; t < SwarmSim.BossSqueeze + 4f; t += Dt)
        {
            s.Step(Dt, inp);
            for (int e = 0; e < s.eventCount; e++) { if (s.events[e].type == SwarmEv.BossWarn) warned = true; if (s.events[e].type == SwarmEv.BossOut) { came = true; Assert.True(warned); } }
        }
        Assert.True(warned); Assert.True(came); Assert.True(s.bossOut);
        ref var boss = ref s.monsters[s.bossIndex];
        Assert.Equal(SwarmKind.Boss, boss.kind); Assert.Equal(SwarmState.Chase, boss.state);
        boss.hp = 1f;
        var aim = Aim(boss.pos); bool win = false;
        for (float t = 0; t < 2f && s.phase != SwarmPhase.Over; t += Dt) { aim = Aim(s.monsters[s.bossIndex].pos); s.Step(Dt, aim); for (int e = 0; e < s.eventCount; e++) if (s.events[e].type == SwarmEv.Win) win = true; }
        Assert.True(win); Assert.True(s.won); Assert.Equal(SwarmPhase.Over, s.phase); Assert.Equal(0, s.alive);
    }

    [Fact]
    public void A_whole_run_allocates_nothing_and_never_overflows()
    {
        var s = New(9);
        var bot = new Bot { turn = 8f };
        bot.Run(s, 20f);                                  // (warm: everything is compiled)
        long spent = 0;
        int most = 0;
        for (float t = 0; t < 60f && s.phase != SwarmPhase.Over; t += Dt)
        {
            if (s.phase == SwarmPhase.Choosing) { s.Choose(0); continue; }
            var inp = bot.Input(s);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            s.Step(Dt, inp);
            spent += System.GC.GetAllocatedBytesForCurrentThread() - before;
            int n = 0; foreach (var m in s.monsters) if (m.alive) n++;
            Assert.Equal(n, s.alive);
            if (n > most) most = n;
            Assert.InRange(s.eventCount, 0, SwarmSim.MaxEvents);
        }
        Assert.True(spent == 0, $"allocated {spent} bytes");
        Assert.InRange(most, 5, SwarmSim.MaxMonsters);
    }

    [Theory]
    [InlineData("몬스터 게임 하자", false)]
    [InlineData("괴물 잡는 게임 하자", false)]
    [InlineData("let's play the monster game", false)]
    [InlineData("Screen Scamps", false)]
    [InlineData("game-swarm", true)]
    [InlineData("악마의 문 하자", true)]
    [InlineData("Demon Gate", true)]
    [InlineData("3D 디펜스 하자", true)]
    [InlineData("테니스 하자", false)]
    [InlineData("오목 해보자", false)]
    [InlineData("", false)]
    public void The_words_that_ask_for_it(string words, bool asked) => Assert.Equal(asked, SwarmRules.Asked(words));

    [Fact]
    public void The_clock_reads_minutes_and_seconds()
    {
        Assert.Equal("0:07", SwarmRules.Clock(7.9f)); Assert.Equal("2:30", SwarmRules.Clock(150f)); Assert.Equal("0:00", SwarmRules.Clock(-3f));
        Assert.Equal(SwarmAug.Count, SwarmAug.Names.Length); Assert.Equal(SwarmAug.Count, SwarmAug.Lines.Length); Assert.Equal(SwarmAug.Count, SwarmAug.Max.Length);
        Assert.True(SwarmAug.Count >= 10);
    }
}
