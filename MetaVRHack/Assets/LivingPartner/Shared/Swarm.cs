// Shared pure C# (no UnityEngine). Compiled into the Unity project, the .NET host and the tests.
using System;
using System.Numerics;

namespace LivingPartner.Shared
{
    /// <summary>
    /// SCREEN SCAMPS - the game where little monsters gather behind the glass of a screen (or of a storybook window on
    /// the desk), burst OUT of it and come for the user, who is seated: bolts of light fire by themselves along the
    /// pointing finger, gems fly in, and every level brings a choice of three augments. After about two and a half
    /// minutes Big Grump squeezes out of the screen; popping it wins the run.
    /// This file is the whole game as a simulation: no engine types, no allocation while it runs, the same result for
    /// the same seed and the same inputs. The headset runs it (MR/MrSwarm.cs) and draws what is in the arrays below.
    /// All positions are metres in ONE frame chosen by the caller (the headset: its tracking space, y up).
    /// </summary>
    public static class SwarmKind
    {
        public const int Hopper = 0, Flyer = 1, Tank = 2, Splitter = 3, Mini = 4, Boss = 5, Count = 6;
        public static readonly string[] Names = { "Briar Imp", "Mothwing", "Ram Warden", "Berry Twins", "Cinderling", "The Thorn Regent" };
        public static readonly float[] Radius = { 0.032f, 0.030f, 0.058f, 0.044f, 0.021f, 0.135f };
        public static readonly float[] Speed = { 0.24f, 0.30f, 0.105f, 0.17f, 0.33f, 0.055f };
        public static readonly float[] Hp = { 2f, 2f, 10f, 5f, 1f, 300f };
        public static readonly int[] Xp = { 1, 1, 4, 2, 1, 0 };
        public static readonly int[] Points = { 10, 12, 40, 25, 5, 500 };
    }

    public static class SwarmAug
    {
        public const int Rapid = 0, Twin = 1, Pierce = 2, Explode = 3, Chain = 4, Orbit = 5, Slow = 6, Big = 7, Heal = 8, Magnet = 9, Buddy = 10, Shield = 11, Count = 12;
        public static readonly string[] Names = { "Quick Sparks", "Twin Bolts", "Needle Light", "Pop Bloom", "Chain Spark", "Star Ring", "Sleepy Mist", "Big Bolts", "Honey Tea", "Gem Magnet", "Bambi Helps", "Palm Shield" };
        public static readonly string[] Lines =
        {
            "Bolts fire faster", "One more bolt per shot", "Bolts pass through", "Bolts burst and splash", "Hits jump to a neighbour", "Stars circle your hand",
            "Monsters near you slow", "Bigger, harder bolts", "Heal two hearts", "Gems rush in, worth more", "Bambi throws stars too", "Wider shield, stronger parry",
        };
        public static readonly int[] Max = { 5, 2, 3, 3, 3, 3, 3, 3, 99, 3, 3, 3 };
    }

    public static class SwarmPhase { public const int Idle = 0, Intro = 1, Play = 2, Choosing = 3, Over = 4; }
    public static class SwarmState { public const int Inside = 0, Emerging = 1, Chase = 2; }

    public static class SwarmEv
    {
        public const int Emerge = 1, Hit = 2, Pop = 3, Fire = 4, Gem = 5, HeartLost = 6, LevelUp = 7, Choices = 8, Chosen = 9, BuddyThrow = 10, BossWarn = 11, BossOut = 12,
            Win = 13, Lose = 14, ShieldBlock = 15, Explosion = 16, Chain = 17, Surge = 18, OrbitHit = 19, Started = 20;
    }

    /// <summary>Where the game is played: the rectangle the monsters come out of, and the desk.</summary>
    public struct SwarmArena
    {
        public Vector3 center, right, up, outward;      // the rectangle's middle, its unit axes, and the unit normal that points at the user
        public float halfW, halfH, deskY;
        public float pace;                              // how much faster everything comes when the glass is further away than 0.7 m (0 = 1)
    }

    /// <summary>What the player does, each step.</summary>
    public struct SwarmInput
    {
        public Vector3 target;                          // where the monsters go: the user's chest
        public bool aimValid; public Vector3 aimOrigin, aimDir;      // the pointing finger: its tip and its (unit) direction
        public bool handValid; public Vector3 hand;     // the hand the stars circle
        public bool shieldValid; public Vector3 shieldPos;           // the open left palm
        public bool buddyValid; public Vector3 buddyPos;             // where the partner stands (its stars start there)
    }

    public struct SwarmMonster
    {
        public bool alive; public int id, kind, state, group;
        public Vector3 pos, from; public float u, v, du, dv;        // inside the rectangle: -1..1 across it
        public float hp, maxHp, radius, t, wait, phase, flash, touchCd, grow;      // grow 0..1 while it squeezes out
        public bool slowed;
    }

    public struct SwarmBolt { public bool alive, star; public Vector3 pos, prev, dir; public float speed, radius, damage, life; public int pierce, lastHit; }
    public struct SwarmGem { public bool alive; public Vector3 pos; public float t; public int value; }
    public struct SwarmEvent { public int type, a, b; public Vector3 pos, pos2; public float f; }

    public sealed class SwarmSim
    {
        public const int MaxMonsters = 64, MaxBolts = 48, MaxGems = 64, MaxEvents = 96, MaxOrbit = 6;
        public const float IntroSeconds = 3.2f, BossAt = 150f, BossSqueeze = 5f, ArriveRadius = 0.13f, AimCone = 0.19f, BoltSpeed = 3.2f, BaseFire = 0.46f;
        public const int StartHearts = 5;
        public const float OuchRadius = 0.45f;

        public readonly SwarmMonster[] monsters = new SwarmMonster[MaxMonsters];
        public readonly SwarmBolt[] bolts = new SwarmBolt[MaxBolts];
        public readonly SwarmGem[] gems = new SwarmGem[MaxGems];
        public readonly SwarmEvent[] events = new SwarmEvent[MaxEvents];
        public int eventCount;
        public readonly Vector3[] orbit = new Vector3[MaxOrbit];
        public int orbitCount;
        public readonly int[] aug = new int[SwarmAug.Count];
        public readonly int[] choices = new int[3];

        public SwarmArena arena;
        public int phase = SwarmPhase.Idle;
        public float time, slow = 1f, iframes;       // time: seconds played since the burst (the intro is before 0)
        public int hearts, maxHearts, level, xp, kills, score, pendingChoices, surges;
        public bool won, bossSpawned, bossOut, bossDead;
        public int bossIndex = -1;
        public int alive;

        bool _offerBuddy;
        uint _rng; int _nextId; float _spawnT, _fireT, _buddyT, _orbitA, _nextSurge, _introT;
        float _gemExtra;

        public int XpNeed => 4 + 3 * (level - 1);
        public float FireInterval => BaseFire * (float)Math.Pow(0.80, aug[SwarmAug.Rapid]);
        public float BoltDamage => 2f + aug[SwarmAug.Big];
        public float BoltRadius => 0.016f * (1f + 0.5f * aug[SwarmAug.Big]);
        public float ShieldRadius => 0.085f + 0.025f * aug[SwarmAug.Shield];
        public float SlowRadius => aug[SwarmAug.Slow] > 0 ? 0.36f + 0.06f * aug[SwarmAug.Slow] : 0f;
        public float IntroLeft => phase == SwarmPhase.Intro ? Math.Max(0f, IntroSeconds - _introT) : 0f;

        // ------------------------------------------------------------------ random numbers (the same on every machine)
        float Rnd() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / 16777216f; }
        float Rnd(float a, float b) => a + (b - a) * Rnd();

        public void Start(uint seed, SwarmArena a, bool offerBuddy = true)
        {
            _offerBuddy = offerBuddy;
            arena = a;
            _rng = seed == 0 ? 2463534242u : seed; _nextId = 1;
            for (int i = 0; i < MaxMonsters; i++) monsters[i] = default;
            for (int i = 0; i < MaxBolts; i++) bolts[i] = default;
            for (int i = 0; i < MaxGems; i++) gems[i] = default;
            for (int i = 0; i < aug.Length; i++) aug[i] = 0;
            eventCount = 0; orbitCount = 0;
            phase = SwarmPhase.Intro; time = 0f; slow = 1f; iframes = 0f; _introT = 0f;
            hearts = maxHearts = StartHearts; level = 1; xp = 0; kills = 0; score = 0; pendingChoices = 0; surges = 0;
            won = bossSpawned = bossOut = bossDead = false; bossIndex = -1; alive = 0;
            _spawnT = 8.5f; _fireT = 0.2f; _buddyT = 1f; _orbitA = 0f; _nextSurge = 24f; _gemExtra = 0f;
            // the first crowd gathers behind the glass and comes out together
            for (int i = 0; i < 8; i++) SpawnInside(i == 2 || i == 6 ? SwarmKind.Flyer : i == 5 ? SwarmKind.Splitter : SwarmKind.Hopper, IntroSeconds + 0.07f * i, 1);
            Push(SwarmEv.Started, a.center);
        }

        public void Stop() { phase = SwarmPhase.Idle; }

        void Push(int type, Vector3 pos, int a = 0, int b = 0, float f = 0f, Vector3 pos2 = default)
        {
            if (eventCount >= MaxEvents) return;
            events[eventCount++] = new SwarmEvent { type = type, pos = pos, a = a, b = b, f = f, pos2 = pos2 };
        }

        // ------------------------------------------------------------------ monsters
        int FreeMonster() { for (int i = 0; i < MaxMonsters; i++) if (!monsters[i].alive) return i; return -1; }

        float HpScale => 1f + Math.Max(0f, time) / 110f;
        float SpeedScale => (0.55f + 0.45f * M.Clamp01(time / BossAt)) * (arena.pace > 0f ? arena.pace : 1f);

        int SpawnInside(int kind, float wait, int group)
        {
            int i = FreeMonster();
            if (i < 0) return -1;
            float r = SwarmKind.Radius[kind];
            ref SwarmMonster m = ref monsters[i];
            m = default;
            m.alive = true; m.id = _nextId++; m.kind = kind; m.state = SwarmState.Inside; m.group = group;
            m.radius = r; m.maxHp = m.hp = kind == SwarmKind.Boss ? SwarmKind.Hp[kind] : Math.Max(1f, (float)Math.Round(SwarmKind.Hp[kind] * HpScale));
            m.wait = wait; m.phase = Rnd(0f, 6.28f);
            m.u = kind == SwarmKind.Boss ? 0f : Rnd(-0.85f, 0.85f); m.v = kind == SwarmKind.Boss ? -0.1f : Rnd(-0.8f, 0.8f);
            float a = Rnd(0f, 6.28f); m.du = M.Cos(a) * 0.9f; m.dv = M.Sin(a) * 0.9f;
            m.pos = InsidePos(m);
            alive++;
            return i;
        }

        Vector3 InsidePos(in SwarmMonster m)
        {
            float w = Math.Max(0.01f, arena.halfW - m.radius), h = Math.Max(0.01f, arena.halfH - m.radius);
            return arena.center + arena.right * (m.u * w) + arena.up * (m.v * h);
        }

        int SpawnChase(int kind, Vector3 at)
        {
            int i = FreeMonster();
            if (i < 0) return -1;
            ref SwarmMonster m = ref monsters[i];
            m = default;
            m.alive = true; m.id = _nextId++; m.kind = kind; m.state = SwarmState.Chase; m.grow = 1f;
            m.radius = SwarmKind.Radius[kind]; m.maxHp = m.hp = SwarmKind.Hp[kind]; m.phase = Rnd(0f, 6.28f);
            m.pos = at;
            alive++;
            return i;
        }

        int PickKind()
        {
            float t = time;
            float wF = t > 18f ? 0.55f : 0f, wT = t > 42f ? 0.20f : 0f, wS = t > 70f ? 0.30f : 0f;
            float x = Rnd() * (1f + wF + wT + wS);
            if (x < 1f) return SwarmKind.Hopper;
            if (x < 1f + wF) return SwarmKind.Flyer;
            if (x < 1f + wF + wT) return SwarmKind.Tank;
            return SwarmKind.Splitter;
        }

        void Spawner(float dt)
        {
            if (bossDead) return;
            if (!bossSpawned && time >= BossAt)
            {
                bossSpawned = true;
                bossIndex = SpawnInside(SwarmKind.Boss, BossSqueeze, 2);
                Push(SwarmEv.BossWarn, arena.center);
            }
            if (!bossSpawned && time >= _nextSurge)
            {
                _nextSurge += 21f; surges++;
                int n = Math.Min(10, 5 + surges);
                for (int k = 0; k < n; k++) SpawnInside(PickKind(), 1.7f + 0.03f * k, 1);
                Push(SwarmEv.Surge, arena.center, n);
            }
            _spawnT -= dt;
            if (_spawnT <= 0f)
            {
                float every = M.Lerp(3.4f, 0.70f, M.Clamp01(time / (BossAt - 10f)));
                if (bossSpawned) every = 1.5f;
                _spawnT += every;
                if (alive < MaxMonsters - 10) SpawnInside(PickKind(), 0.75f, 0);
            }
        }

        void StepMonsters(float dt, in SwarmInput inp)
        {
            float slowR = SlowRadius, slowK = 1f - (0.22f + 0.10f * aug[SwarmAug.Slow]);
            for (int i = 0; i < MaxMonsters; i++)
            {
                ref SwarmMonster m = ref monsters[i];
                if (!m.alive) continue;
                m.t += dt; m.phase += dt; if (m.flash > 0f) m.flash -= dt; if (m.touchCd > 0f) m.touchCd -= dt;
                if (m.state == SwarmState.Inside)
                {
                    // behind the glass: it mills about; just before it comes out it presses against the glass (the view shows that)
                    m.u += m.du * dt; m.v += m.dv * dt;
                    if (m.u > 0.9f) { m.u = 0.9f; m.du = -Math.Abs(m.du); } else if (m.u < -0.9f) { m.u = -0.9f; m.du = Math.Abs(m.du); }
                    if (m.v > 0.85f) { m.v = 0.85f; m.dv = -Math.Abs(m.dv); } else if (m.v < -0.85f) { m.v = -0.85f; m.dv = Math.Abs(m.dv); }
                    if (m.kind == SwarmKind.Boss) { m.u *= 0.9f; m.v = -0.1f; }
                    m.pos = InsidePos(m);
                    if (m.t >= m.wait)
                    {
                        m.state = SwarmState.Emerging; m.t = 0f; m.from = m.pos; m.grow = 0f;
                        Push(m.kind == SwarmKind.Boss ? SwarmEv.BossOut : SwarmEv.Emerge, m.pos, m.kind, m.group);
                        if (m.kind == SwarmKind.Boss) bossOut = true;
                    }
                    continue;
                }
                if (m.state == SwarmState.Emerging)
                {
                    float dur = m.kind == SwarmKind.Boss ? 2.4f : 0.38f, k = M.Clamp01(m.t / dur);
                    m.grow = k;
                    m.pos = m.from + arena.outward * ((m.kind == SwarmKind.Boss ? 0.24f : 0.09f) * M.EaseOut(k) + m.radius * 0.3f);
                    if (k >= 1f) { m.state = SwarmState.Chase; m.t = 0f; m.grow = 1f; }
                    continue;
                }
                // ---- out in the room: each kind comes for the user in its own way
                Vector3 to = inp.target - m.pos; float dist = to.Length();
                Vector3 dir = dist > 1e-5f ? to / dist : arena.outward;
                float sp = SwarmKind.Speed[m.kind] * SpeedScale;
                m.slowed = slowR > 0f && dist < slowR;
                if (m.slowed) sp *= slowK;
                switch (m.kind)
                {
                    case SwarmKind.Hopper:
                    {
                        Vector3 flat = new Vector3(to.X, 0f, to.Z); float fd = flat.Length();
                        if (fd > 0.30f)
                        {
                            // along the desk in hops
                            Vector3 fdir = flat / fd;
                            m.pos += fdir * (sp * dt);
                            float hop = Math.Abs(M.Sin(m.phase * 6.5f)) * 0.055f;
                            float want = arena.deskY + m.radius + hop;
                            m.pos.Y += (want - m.pos.Y) * Math.Min(1f, dt * 7f);
                        }
                        else m.pos += dir * (sp * 1.5f * dt);      // the last leap, up at the user
                        break;
                    }
                    case SwarmKind.Flyer:
                    {
                        Vector3 side = Vector3.Cross(Vector3.UnitY, dir);
                        m.pos += dir * (sp * dt) + side * (M.Cos(m.phase * 4.2f) * 0.42f * dt) + Vector3.UnitY * (M.Sin(m.phase * 2.6f) * 0.10f * dt);
                        break;
                    }
                    default:
                        m.pos += dir * (sp * dt);
                        break;
                }
                // (the boss spits out little ones as it comes)
                if (m.kind == SwarmKind.Boss && m.t > 2.8f)
                {
                    m.t = 0f;
                    for (int k = 0; k < 2; k++) SpawnChase(SwarmKind.Mini, m.pos + arena.right * ((k == 0 ? -1f : 1f) * (m.radius + 0.03f)) + arena.outward * 0.03f);
                }
                // A palm intercepts an arriving demon before a hit to the player's chest on the same tick.
                float reach = m.radius + ShieldRadius;
                if (inp.shieldValid && m.touchCd <= 0f && Vector3.DistanceSquared(m.pos, inp.shieldPos) <= reach * reach)
                {
                    m.touchCd = 0.45f;
                    Vector3 away = M.SafeNormalize(m.pos - inp.target, -arena.outward);
                    m.pos += away * (m.kind == SwarmKind.Boss ? 0.06f : 0.20f);
                    Push(SwarmEv.ShieldBlock, inp.shieldPos, m.kind);
                    Damage(i, 2f + 2f * aug[SwarmAug.Shield], m.pos, away);
                    if (phase == SwarmPhase.Over) return;
                    continue;
                }
                // ---- it got to the user
                if (dist < ArriveRadius + m.radius)
                {
                    bool boss = m.kind == SwarmKind.Boss;
                    if (iframes <= 0f)
                    {
                        hearts--; iframes = 1.2f;
                        Push(SwarmEv.HeartLost, m.pos, m.kind, hearts);
                        // a ring of light goes out from the user and pops everything close: a moment to breathe
                        for (int j = 0; j < MaxMonsters; j++)
                        {
                            ref SwarmMonster o = ref monsters[j];
                            if (!o.alive || o.state != SwarmState.Chase || j == i || o.kind == SwarmKind.Boss) continue;
                            if ((o.pos - inp.target).Length() < OuchRadius) { Push(SwarmEv.Pop, o.pos, o.kind, 1); o.alive = false; alive--; }
                        }
                        if (hearts <= 0) { hearts = 0; End(false); return; }
                    }
                    if (boss) m.pos -= dir * 0.36f;
                    else { Push(SwarmEv.Pop, m.pos, m.kind, 1); m.alive = false; alive--; }
                }
            }
        }

        // ------------------------------------------------------------------ hitting
        void Damage(int i, float dmg, Vector3 at, Vector3 push)
        {
            ref SwarmMonster m = ref monsters[i];
            if (!m.alive || m.state == SwarmState.Inside) return;
            m.hp -= dmg; m.flash = 0.10f;
            if (m.kind != SwarmKind.Boss && m.state == SwarmState.Chase) m.pos += push * (m.kind == SwarmKind.Tank ? 0.006f : 0.022f);
            if (m.hp > 0f) { Push(SwarmEv.Hit, at, m.kind, i); return; }
            Kill(i);
        }

        void Kill(int i)
        {
            ref SwarmMonster m = ref monsters[i];
            int kind = m.kind; Vector3 pos = m.pos;
            m.alive = false; alive--;
            kills++; score += SwarmKind.Points[kind];
            Push(SwarmEv.Pop, pos, kind, 0);
            if (SwarmKind.Xp[kind] > 0) SpawnGem(pos, SwarmKind.Xp[kind]);
            if (kind == SwarmKind.Splitter)
                for (int k = 0; k < 2; k++) SpawnChase(SwarmKind.Mini, pos + arena.right * ((k == 0 ? -1f : 1f) * 0.035f) + Vector3.UnitY * 0.01f);
            if (kind == SwarmKind.Boss) { bossDead = true; End(true); }
        }

        void SpawnGem(Vector3 at, int value)
        {
            for (int i = 0; i < MaxGems; i++)
            {
                if (gems[i].alive) continue;
                gems[i] = new SwarmGem { alive = true, pos = at, t = 0f, value = value };
                return;
            }
            xp += value;      // (no room for one more gem: it counts at once)
        }

        void End(bool win)
        {
            won = win; phase = SwarmPhase.Over; slow = 1f; pendingChoices = 0;
            score += level * 50 + (int)Math.Max(0f, time) + (win ? 1000 : 0);
            if (win)
                for (int i = 0; i < MaxMonsters; i++)
                    if (monsters[i].alive) { Push(SwarmEv.Pop, monsters[i].pos, monsters[i].kind, 0); monsters[i].alive = false; }
            if (win) alive = 0;
            for (int i = 0; i < MaxBolts; i++) bolts[i].alive = false;
            Push(win ? SwarmEv.Win : SwarmEv.Lose, arena.center);
        }

        static float SegDist2(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a; float l2 = ab.LengthSquared();
            float t = l2 < 1e-10f ? 0f : M.Clamp01(Vector3.Dot(p - a, ab) / l2);
            return (a + ab * t - p).LengthSquared();
        }

        int Nearest(Vector3 from, float within, int notA, int notB)
        {
            int best = -1; float bd = within * within;
            for (int i = 0; i < MaxMonsters; i++)
            {
                ref SwarmMonster m = ref monsters[i];
                if (!m.alive || m.state == SwarmState.Inside || i == notA || i == notB) continue;
                float d = (m.pos - from).LengthSquared();
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void AddBolt(Vector3 pos, Vector3 dir, bool star)
        {
            for (int i = 0; i < MaxBolts; i++)
            {
                if (bolts[i].alive) continue;
                bolts[i] = new SwarmBolt
                {
                    alive = true, star = star, pos = pos, prev = pos, dir = dir, speed = star ? 2.2f : BoltSpeed, radius = star ? 0.022f : BoltRadius,
                    damage = star ? 3f + aug[SwarmAug.Buddy] : BoltDamage, life = 1.3f, pierce = star ? 0 : aug[SwarmAug.Pierce], lastHit = -1,
                };
                return;
            }
        }

        readonly int[] _picked = new int[3];

        void Fire(in SwarmInput inp)
        {
            int n = 1 + aug[SwarmAug.Twin];
            // generous aim: the bolts go to what is near the finger's line (the nearest to the line first)
            int found = 0;
            for (int k = 0; k < n; k++)
            {
                int best = -1; float bs = float.MaxValue;
                for (int i = 0; i < MaxMonsters; i++)
                {
                    ref SwarmMonster m = ref monsters[i];
                    if (!m.alive || m.state == SwarmState.Inside) continue;
                    bool taken = false; for (int q = 0; q < found; q++) if (_picked[q] == i) taken = true;
                    if (taken) continue;
                    Vector3 to = m.pos - inp.aimOrigin; float d = to.Length();
                    if (d < 0.02f) continue;
                    float cos = Vector3.Dot(to / d, inp.aimDir);
                    float ang = (float)Math.Acos(M.Clamp(cos, -1f, 1f));
                    float allowed = AimCone + (float)Math.Atan2(m.radius, d);
                    if (ang > allowed) continue;
                    float s = ang + d * 0.06f;
                    if (s < bs) { bs = s; best = i; }
                }
                if (best < 0) break;
                _picked[found++] = best;
            }
            for (int k = 0; k < n; k++)
            {
                Vector3 dir;
                if (k < found)
                {
                    ref SwarmMonster m = ref monsters[_picked[k]];
                    dir = M.SafeNormalize(m.pos - inp.aimOrigin, inp.aimDir);
                }
                else
                {
                    float a = (k - (n - 1) * 0.5f) * 0.10f + (found > 0 ? (k % 2 == 0 ? 0.07f : -0.07f) : 0f);
                    dir = Vector3.Transform(inp.aimDir, Quaternion.CreateFromAxisAngle(Vector3.UnitY, a));
                }
                AddBolt(inp.aimOrigin + dir * 0.02f, dir, false);
            }
            Push(SwarmEv.Fire, inp.aimOrigin, n);
        }

        void StepBolts(float dt)
        {
            int explode = aug[SwarmAug.Explode], chain = aug[SwarmAug.Chain];
            for (int b = 0; b < MaxBolts; b++)
            {
                ref SwarmBolt bo = ref bolts[b];
                if (!bo.alive) continue;
                bo.prev = bo.pos; bo.pos += bo.dir * (bo.speed * dt); bo.life -= dt;
                if (bo.life <= 0f) { bo.alive = false; continue; }
                for (int i = 0; i < MaxMonsters; i++)
                {
                    ref SwarmMonster m = ref monsters[i];
                    if (!m.alive || m.state == SwarmState.Inside || m.id == bo.lastHit) continue;
                    float r = m.radius * (0.5f + 0.5f * m.grow) + bo.radius;
                    if (SegDist2(bo.prev, bo.pos, m.pos) > r * r) continue;
                    Vector3 at = m.pos - bo.dir * m.radius;
                    bo.lastHit = m.id;
                    Damage(i, bo.damage, at, bo.dir);
                    if (!bo.star)
                    {
                        if (explode > 0)
                        {
                            float R = 0.085f + 0.03f * explode;
                            Push(SwarmEv.Explosion, at, 0, 0, R);
                            for (int j = 0; j < MaxMonsters; j++)
                            {
                                if (j == i || !monsters[j].alive || monsters[j].state == SwarmState.Inside) continue;
                                Vector3 d = monsters[j].pos - at;
                                if (d.Length() < R + monsters[j].radius) Damage(j, 1f + explode, monsters[j].pos, M.SafeNormalize(d, bo.dir));
                            }
                        }
                        if (chain > 0)
                        {
                            int from = i, before = -1; Vector3 fromPos = at;
                            for (int hop = 0; hop < chain; hop++)
                            {
                                int nx = Nearest(fromPos, 0.30f, from, before);
                                if (nx < 0) break;
                                Vector3 np = monsters[nx].pos;
                                Push(SwarmEv.Chain, fromPos, 0, 0, 0f, np);
                                Damage(nx, 1f + chain, np, M.SafeNormalize(np - fromPos, bo.dir));
                                before = from; from = nx; fromPos = np;
                            }
                        }
                    }
                    if (phase == SwarmPhase.Over) return;
                    if (bo.pierce > 0) bo.pierce--; else { bo.alive = false; break; }
                }
            }
        }

        void StepGems(float dt, in SwarmInput inp)
        {
            float magnet = aug[SwarmAug.Magnet];
            for (int i = 0; i < MaxGems; i++)
            {
                ref SwarmGem g = ref gems[i];
                if (!g.alive) continue;
                g.t += dt;
                if (g.t < 0.28f) { g.pos.Y += 0.16f * dt; continue; }
                Vector3 to = inp.target - g.pos; float d = to.Length();
                float sp = (0.7f + 3.2f * (g.t - 0.28f)) * (1f + 0.8f * magnet);
                if (d <= sp * dt + 0.09f)
                {
                    g.alive = false;
                    int value = g.value;
                    if (magnet > 0f) { _gemExtra += g.value * 0.3f * magnet; int extra = (int)_gemExtra; _gemExtra -= extra; value += extra; }
                    xp += value;
                    Push(SwarmEv.Gem, inp.target, value);
                }
                else g.pos += to / d * (sp * dt);
            }
            while (phase != SwarmPhase.Over && xp >= XpNeed) { xp -= XpNeed; level++; pendingChoices++; Push(SwarmEv.LevelUp, inp.target, level); }
        }

        void StepExtras(float dt, in SwarmInput inp)
        {
            // ---- the stars that circle the hand
            int stars = Math.Min(MaxOrbit, aug[SwarmAug.Orbit] * 2);
            orbitCount = stars;
            if (stars > 0)
            {
                _orbitA += dt * 4.4f;
                Vector3 c = inp.handValid ? inp.hand : inp.target + arena.outward * -0.25f;
                float R = 0.13f;
                for (int k = 0; k < stars; k++)
                {
                    float a = _orbitA + k * (6.2832f / stars);
                    orbit[k] = c + arena.right * (M.Cos(a) * R) + arena.up * (M.Sin(a) * R * 0.35f) + arena.outward * (M.Sin(a) * R * -0.94f);
                    for (int i = 0; i < MaxMonsters; i++)
                    {
                        ref SwarmMonster m = ref monsters[i];
                        if (!m.alive || m.state != SwarmState.Chase || m.touchCd > 0f) continue;
                        float r = m.radius + 0.026f;
                        if ((m.pos - orbit[k]).LengthSquared() > r * r) continue;
                        m.touchCd = 0.35f;
                        Push(SwarmEv.OrbitHit, orbit[k]);
                        Damage(i, 2f + aug[SwarmAug.Orbit], orbit[k], M.SafeNormalize(m.pos - c, arena.outward * -1f));
                        if (phase == SwarmPhase.Over) return;
                    }
                }
            }
            // ---- the partner throws stars
            if (aug[SwarmAug.Buddy] > 0 && inp.buddyValid)
            {
                _buddyT -= dt;
                if (_buddyT <= 0f)
                {
                    int nx = Nearest(inp.target, 3f, -1, -1);
                    if (nx < 0) _buddyT = 0.2f;
                    else
                    {
                        _buddyT = 1.9f - 0.4f * aug[SwarmAug.Buddy];
                        Vector3 from = inp.buddyPos;
                        AddBolt(from, M.SafeNormalize(monsters[nx].pos - from, arena.outward * -1f), true);
                        Push(SwarmEv.BuddyThrow, from, 0, 0, 0f, monsters[nx].pos);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the choice at a level-up
        void Offer()
        {
            int n = 0;
            for (int guard = 0; guard < 200 && n < 3; guard++)
            {
                int a = (int)(Rnd() * SwarmAug.Count); if (a >= SwarmAug.Count) a = SwarmAug.Count - 1;
                if ((!_offerBuddy && a == SwarmAug.Buddy) || aug[a] >= SwarmAug.Max[a]) continue;
                if (a == SwarmAug.Heal && hearts >= maxHearts) continue;
                bool dup = false; for (int k = 0; k < n; k++) if (choices[k] == a) dup = true;
                if (dup) continue;
                choices[n++] = a;
            }
            while (n < 3) choices[n++] = SwarmAug.Heal;
            Push(SwarmEv.Choices, arena.center, level);
        }

        /// <summary>The augment of one of the three cards is taken (0..2). False when no choice is open.</summary>
        public bool Choose(int card)
        {
            if (phase != SwarmPhase.Choosing || card < 0 || card > 2) return false;
            int a = choices[card];
            aug[a]++;
            if (a == SwarmAug.Heal) hearts = Math.Min(maxHearts, hearts + 2);
            eventCount = 0;
            Push(SwarmEv.Chosen, arena.center, a, aug[a]);
            pendingChoices--;
            if (pendingChoices > 0) Offer();
            else { phase = SwarmPhase.Play; slow = 0f; }
            return true;
        }

        // ------------------------------------------------------------------ one step
        /// <summary>Advances the game by dt seconds. The events of this step are in events[0 .. eventCount).</summary>
        public void Step(float dt, in SwarmInput inp)
        {
            eventCount = 0;
            if (phase == SwarmPhase.Idle || phase == SwarmPhase.Over || phase == SwarmPhase.Choosing) return;
            if (dt > 0.05f) dt = 0.05f;
            // time slows to a stop for a choice, and picks up again after it
            if (pendingChoices > 0 && phase == SwarmPhase.Play)
            {
                slow = Math.Max(0f, slow - dt / 0.45f);
                if (slow <= 0f) { phase = SwarmPhase.Choosing; Offer(); return; }
            }
            else if (slow < 1f) slow = Math.Min(1f, slow + dt / 0.4f);
            float sdt = dt * slow;
            if (phase == SwarmPhase.Intro)
            {
                _introT += sdt;
                if (_introT >= IntroSeconds) { phase = SwarmPhase.Play; time = 0f; }
            }
            else { time += sdt; Spawner(sdt); }
            if (iframes > 0f) iframes -= sdt;
            // the bolts, in a steady rhythm, wherever the finger points
            _fireT -= sdt;
            if (_fireT <= 0f)
            {
                if (inp.aimValid) { Fire(inp); _fireT += FireInterval; if (_fireT < 0f) _fireT = 0f; }
                else _fireT = 0f;
            }
            StepMonsters(sdt, inp);
            if (phase == SwarmPhase.Over) return;
            StepBolts(sdt);
            if (phase == SwarmPhase.Over) return;
            StepExtras(sdt, inp);
            if (phase == SwarmPhase.Over) return;
            StepGems(sdt, inp);
        }

        /// <summary>For checks and captures: the clock is put forward (the boss is `BossAt` seconds in).</summary>
        public void SkipTo(float seconds) { if (phase == SwarmPhase.Play && seconds > time) { time = seconds; _nextSurge = Math.Max(_nextSurge, seconds + 6f); } }

        /// <summary>For checks and captures: one monster is put out in the room. Its index, or -1 when there is no room.</summary>
        public int Put(int kind, Vector3 at) => kind < 0 || kind >= SwarmKind.Count ? -1 : SpawnChase(kind, at);

        /// <summary>For checks and captures: experience is given (a level-up follows when it is enough).</summary>
        public void GiveXp(int n) { if (phase == SwarmPhase.Play) xp += Math.Max(0, n); }
    }

    /// <summary>
    /// What is behind the glass of a screen just now: headset -> host -> PC client, ten times a second (Msg.SwarmState).
    /// The PC client draws them inside the monitor (Windows/PcSwarm.cs); the headset then leaves those to it and draws
    /// only what has come out.
    /// </summary>
    [Serializable]
    public sealed class SwarmGlass
    {
        public const int Max = 24;
        public bool on; public string monitorId = ""; public int phase, count, bursts;      // bursts: counts up whenever some come out (the glass flashes)
        public int level, hearts, maxHearts, score, xp, need;
        public float elapsed;
        public bool won;
        public int[] choices = new int[3]; // Index into SwarmAug, valid only in the Choosing phase.
        public float halfW, halfH;                                                          // half the lit screen in metres, as the headset measured it (the monsters are as large inside as they are outside)
        // (count = -1: the PC client's answer, back through the host to the headset about twice a second - "I am drawing them")
        public int[] id = new int[Max], kind = new int[Max];
        public float[] u = new float[Max], v = new float[Max], press = new float[Max];      // -1..1 across the screen (v up); press 0..1: how hard it pushes against the glass
    }

    /// <summary>What a run came to: for the result card and the save.</summary>
    public static class SwarmRules
    {
        public const string PackId = "game-swarm", Template = "swarm", Title = "Demon Gate";

        // Rank measured screens by how far the gaze ray misses their rectangle. A TV
        // at sofa distance competes on the same angular basis as a monitor on a desk.
        public static float ScreenScore(Vector3 eye, Vector3 gaze, Vector3 center, Vector3 right, Vector3 up, Vector3 outward, float width, float height, bool occupied)
        {
            Vector3 delta = center - eye;
            float distance = delta.Length(), towards = -Vector3.Dot(gaze, outward);
            if (distance < .25f || towards < .15f || Vector3.Dot(delta, gaze) < .30f * distance)
                return float.PositiveInfinity;
            float t = Vector3.Dot(eye - center, outward) / towards;
            if (t <= 0f) return float.PositiveInfinity;
            Vector3 miss = eye + gaze * t - center;
            float x = Math.Max(0f, Math.Abs(Vector3.Dot(miss, right)) - width * .5f);
            float y = Math.Max(0f, Math.Abs(Vector3.Dot(miss, up)) - height * .5f);
            float angularMiss = (x * x + y * y) / (t * t);
            return angularMiss + .0001f * distance - (occupied ? .0005f : 0f);
        }

        public static string Clock(float seconds) { int s = (int)Math.Max(0f, seconds); return (s / 60) + ":" + (s % 60).ToString("00"); }

        /// <summary>Recognize the title and defense aliases for Demon Gate.</summary>
        public static bool Asked(string words)
        {
            string plain = (words ?? "").Trim().ToLowerInvariant();
            if (plain == PackId || plain == Template) return true;
            string f = plain.Replace(" ", "").Replace("-", "");
            if (f.Length == 0) return false;
            return f.Contains("악마의문") || f.Contains("악마문") || f.Contains("디펜스") || f.Contains("demongate") || f.Contains("defense") || f.Contains("defence");
        }
    }
}
