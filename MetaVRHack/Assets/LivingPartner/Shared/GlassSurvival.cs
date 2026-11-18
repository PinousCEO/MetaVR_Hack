using System;
using System.Collections.Generic;

namespace LivingPartner.Shared
{
    /// <summary>
    /// "Vampire Night": they keep coming, the partner fights by itself, the user's hand leads it and bops what it
    /// touches, and every level a new power is picked.
    ///
    /// Sprites (back to front): Gem (frame 0 = small, 1 = big), Enemy / Flyer / Brute (frame = facing: 0 right, 1 left),
    /// Frost (over each slowed enemy, id = 1000000 + the enemy's id), Orb (frame = which moon), Shot (rot = the way it
    /// flies, 0 = to the right), Ring (size = its diameter), the effects (Puff where one was beaten, Bolt = a strike
    /// from the top edge down to an enemy: x, y its middle, size its height, Sparkle where a gem was taken, Heart = one
    /// that was just lost), and while choosing the Cards (ids 10..12, text = the power's name, text2 = its note,
    /// fill = how far "hold to pick" is, frame = the power's index in AbilityIds).
    /// Poses: idle, walk, hurt, cheer, down. Sounds: pop (beaten), hit (hurt but not beaten, a lightning strike), coin
    /// (a gem), error (a heart lost), whoosh (a new wave, a shock wave), sparkle (level up), tick (a card filling),
    /// ta-da (a power taken), lose.
    ///
    /// How it goes: the partner walks towards the x of the acting hand that is nearest to it (so the hand both leads it
    /// and is a weapon); with no hand it backs away from what is nearest. Its own Star Shot holds the first waves by
    /// itself - what the user does then is gather the gems (they lie where an enemy fell, a gem can be taken 0.25 s
    /// after it fell out) by sweeping a hand over them or by leading the partner there. Level n -> n + 1 takes
    /// 4 + 3n gems. While choosing, the world and the clock stand still; a card is taken by resting an acting hand on
    /// it for HoldToPick seconds (the cards take no hand for the first HoldArms seconds, so that a hand that happened
    /// to be there does not pick by accident), by Choose(i), or by itself after AutoPick seconds (the first one).
    /// After a card is taken the phase is "play" at once, but the world waits Grace seconds more (the card that was
    /// taken is still drawn, fading, id 13) and the partner cannot be hurt for a moment after that.
    /// A card is CardSize tall and CardSize * CardAspect wide, its middle at CardX(i, n), CardY.
    /// handRadius grows with "Big Hand": draw the hand's shadow to GlassGames.HandRadius(game, input, i).
    /// </summary>
    public sealed class GlassSurvival : GlassGameBase
    {
        public override string Template { get { return "survival"; } }
        protected override string DefaultHowTo { get { return "Lead with your hand, bop what comes close!"; } }

        // ---- the powers
        const int Star = 0, Moons = 1, Shock = 2, Frost = 3, Magnet = 4, Swift = 5, Giant = 6, Bolt = 7, Heart = 8;      // indices into AbilityIds
        public static readonly string[] AbilityIds = { "star", "moons", "wave", "frost", "magnet", "swift", "giant", "bolt", "heart" };
        public static readonly string[] AbilityNames = { "Star Shot", "Orbiting Moons", "Shock Wave", "Frost", "Magnet", "Swift Feet", "Big Hand", "Lightning", "Extra Heart" };
        public const int TopLevel = 3;                  // of every power but "heart", which is one more heart each time (up to 6 hearts)

        static readonly string[][] Notes =
        {
            new[] { "a star at the nearest one", "two stars at once, faster", "three stars at once, faster still" },
            new[] { "a moon circles around", "two moons", "three moons" },
            new[] { "a ring pushes them back", "a wider ring, more often", "the widest ring, more often still" },
            new[] { "they come 15% slower", "they come 28% slower", "they come 40% slower" },
            new[] { "gems come from twice as far", "gems come from three times as far", "gems come from almost anywhere" },
            new[] { "25% faster on its feet", "50% faster on its feet", "75% faster on its feet" },
            new[] { "your hand is bigger", "your hand is bigger still", "a huge hand that hits twice as hard" },
            new[] { "strikes the nearest one", "strikes two, more often", "strikes three, more often still" },
        };

        static readonly float[] StarEvery = { 0.9f, 0.9f, 0.65f, 0.45f };
        static readonly float[] WaveEvery = { 0f, 4f, 3.2f, 2.4f };
        static readonly float[] WaveRadius = { 0f, 0.26f, 0.32f, 0.38f };
        static readonly float[] FrostSlow = { 0f, 0.15f, 0.28f, 0.40f };
        static readonly float[] MagnetTimes = { 1f, 2f, 3f, 4.5f };
        static readonly float[] SwiftTimes = { 1f, 1.25f, 1.5f, 1.75f };
        static readonly float[] GiantTimes = { 1f, 1.4f, 1.8f, 2.2f };
        static readonly float[] BoltEvery = { 0f, 3f, 2.4f, 1.8f };

        // ---- tuning
        public const int MaxEnemies = 40;
        public const float WaveSeconds = 18f;
        public const int WaveCap = 40;                  // the waves go on counting, but they do not get faster or thicker after this one
        public const float PetSpeed = 0.42f;
        public const float StarRange = 0.75f, StarSpeed = 1.5f;
        public const float OrbitRadius = 0.17f, OrbRadius = 0.035f, OrbTurn = 2.6f;     // rad/s
        public const float MagnetRadius = 0.16f;
        public const float GemRipe = 0.25f;             // a gem can be taken this long after it fell out
        public const float BoltRange = 0.9f;
        public const float SpawnPerSecond = 0.55f;      // in wave 1; x 1.22 each wave
        public const float CardY = 0.56f, CardSize = 0.34f, CardAspect = 0.72f;         // a card is CardSize tall and CardSize * CardAspect wide
        public const float HoldToPick = 0.8f, HoldArms = 0.3f, AutoPick = 15f, Grace = 0.6f;
        const int Walker = 0, FlyerKind = 1, BruteKind = 2;
        static readonly float[] KindSize = { 0.11f, 0.09f, 0.20f };
        static readonly float[] KindSpeed = { 0.16f, 0.19f, 0.085f };
        static readonly int[] KindHp = { 1, 1, 4 };
        static readonly int[] KindXp = { 1, 1, 3 };
        static readonly string[] KindArt = { GlassArt.Enemy, GlassArt.Flyer, GlassArt.Brute };

        struct Foe
        {
            public int id, kind, hp, ring;
            public float x, y, kx, ky, handCd, orbCd, age, weave;
            public bool left;
        }
        struct GemT { public int id, value; public float x, y, vy, age; public bool pulled; }
        struct ShotT { public int id, target; public float x, y, vx, vy, life; }

        const int MaxGems = 64, MaxShots = 24;
        readonly Foe[] foes = new Foe[MaxEnemies];
        readonly GemT[] gems = new GemT[MaxGems];
        readonly ShotT[] shots = new ShotT[MaxShots];
        int foeCount, gemCount, shotCount;

        readonly int[] lv = new int[AbilityIds.Length];
        readonly Dictionary<string, int> abilities = new Dictionary<string, int>();
        int xp, wave;
        float spawnAcc, shotT, waveT, boltT, invuln, cheerT, grace, overT;
        float ringR, ringMax, ringAge; int ringSerial; bool ringOn;
        readonly int[] picked3 = new int[3];

        // choosing
        readonly int[] offer = new int[3];
        int offerCount;
        readonly float[] hold = new float[3];
        static readonly string[] None = new string[0];
        string[] offerNames = None, offerNotes = None, offerIds = None;
        int takenIdx = -1; float takenX; string takenName = "", takenNote = "";

        // ---- for the renderers and the tests
        public int Wave { get { return wave; } }
        /// <summary>Every power's level by its id (0 = not owned; "heart" counts how many were taken).</summary>
        public IReadOnlyDictionary<string, int> Abilities { get { return abilities; } }
        public int Xp { get { return xp; } }
        public int XpNeeded { get { return 4 + 3 * level; } }
        public int EnemyCount { get { return foeCount; } }
        public int GemCount { get { return gemCount; } }
        /// <summary>Phase choose: the ids of what is offered, in the order of choices.</summary>
        public string[] ChoiceIds { get { return offerIds; } }
        public bool Invulnerable { get { return invuln > 0f; } }
        /// <summary>The partner's pace right now, field units a second.</summary>
        public float PetPace { get { return PetSpeed * SwiftTimes[lv[Swift]]; } }
        /// <summary>From how far a gem comes to the partner, field units.</summary>
        public float MagnetReach { get { return MagnetRadius * MagnetTimes[lv[Magnet]]; } }
        /// <summary>Experience from nowhere (a test, a cheat): it counts with the next Step.</summary>
        public void GrantXp(int n) { if (n > 0) xp += n; }

        /// <summary>Where the i-th of n cards stands (its middle, x).</summary>
        public float CardX(int i, int n)
        {
            if (n >= 3) return W * (i == 0 ? 0.22f : i == 1 ? 0.5f : 0.78f);
            if (n == 2) return W * (i == 0 ? 0.36f : 0.64f);
            return W * 0.5f;
        }

        protected override void OnStart()
        {
            foeCount = gemCount = shotCount = 0;
            for (int i = 0; i < lv.Length; i++) lv[i] = 0;
            lv[Star] = 1;
            SyncAbilities();
            xp = 0; wave = 0;
            spawnAcc = 0.7f; shotT = 0.5f; waveT = 1f; boltT = 1f; invuln = 0f; cheerT = 0f; grace = 0f; overT = 0f;
            ringOn = false; ringR = 0f; ringSerial = 0;
            offerCount = 0; takenIdx = -1;
            offerNames = None; offerNotes = None; offerIds = None;
        }

        void SyncAbilities()
        {
            for (int i = 0; i < lv.Length; i++) abilities[AbilityIds[i]] = lv[i];
        }

        protected override void TickIntro(float dt, GlassInput input)
        {
            MovePet(dt, input);
        }

        // ------------------------------------------------------------------ play

        protected override void TickPlay(float dt, GlassInput input)
        {
            if (grace > 0f)                              // a power was just taken: the world waits a moment
            {
                grace -= dt;
                petPose = "cheer";
                return;
            }
            if (xp >= XpNeeded) { LevelUp(); if (phase != Play) return; }

            seconds += dt;
            if (invuln > 0f) invuln -= dt;
            if (cheerT > 0f) cheerT -= dt;

            int w = 1 + (int)(seconds / WaveSeconds);
            if (w != wave)
            {
                wave = w;
                Say("Wave " + wave, 2f);
                Sfx("whoosh", 1f);
            }

            Spawn(dt);
            MovePet(dt, input);
            MoveFoes(dt);
            Hands(input);
            StarShot(dt);
            MoveShots(dt);
            Orbs();
            ShockWave(dt);
            Lightning(dt);
            TouchPet();
            if (phase != Play) return;
            SweepFoes();
            MoveGems(dt, input);
        }

        float PetMid { get { return PetMidY; } }

        void Spawn(float dt)
        {
            float rate = SpawnPerSecond * packSpawn * MathF.Pow(1.22f, Math.Min(wave, WaveCap) - 1);
            spawnAcc += rate * dt;
            if (foeCount >= MaxEnemies) { if (spawnAcc > 1f) spawnAcc = 1f; return; }
            while (spawnAcc >= 1f && foeCount < MaxEnemies)
            {
                spawnAcc -= 1f;
                float brutes = wave >= 4 ? Math.Min(0.30f, 0.08f + 0.03f * (wave - 4)) : 0f;
                float flyers = wave >= 2 ? Math.Min(0.40f, 0.26f + 0.02f * (wave - 2)) : 0f;
                float r = rng.Next01();
                int kind = r < brutes ? BruteKind : r < brutes + flyers ? FlyerKind : Walker;
                Foe f = new Foe();
                f.id = NewId(); f.kind = kind; f.hp = KindHp[kind]; f.ring = 0;
                f.weave = rng.Range(0f, 6.28f);
                bool fromLeft = rng.Chance(0.5f);
                if (kind == FlyerKind && rng.Chance(0.5f))
                {
                    f.x = rng.Range(0.1f, W - 0.1f); f.y = 1.08f;                 // from the top
                }
                else
                {
                    f.x = fromLeft ? -0.08f : W + 0.08f;
                    f.y = kind == FlyerKind ? rng.Range(0.5f, 0.95f) : KindSize[kind] * 0.5f;
                }
                f.left = f.x > petX;
                foes[foeCount++] = f;
            }
        }

        void MovePet(float dt, GlassInput input)
        {
            // the acting hand that is nearest to it leads it
            int lead = -1; float best = float.MaxValue;
            for (int h = 0; h < 2; h++)
            {
                if (!GlassGames.Acts(input, h)) continue;
                float d = Len(input.x[h] - petX, input.y[h] - PetMid);
                if (d < best) { best = d; lead = h; }
            }
            float move = 0f;                              // -1 .. 1
            if (lead >= 0)
            {
                float dx = Clamp(input.x[lead], 0.08f, W - 0.08f) - petX;
                if (Math.Abs(dx) > 0.05f) move = dx > 0f ? 1f : -1f;
            }
            else
            {
                // by itself: away from what is nearest, and back towards the middle
                float nearest = 0.5f, away = 0f;
                for (int i = 0; i < foeCount; i++)
                {
                    if (foes[i].hp <= 0) continue;
                    float dx = petX - foes[i].x;
                    float d = Len(dx, (foes[i].y - PetMid) * 0.6f);
                    if (d < nearest) { nearest = d; away = (dx >= 0f ? 1f : -1f) * (1f - d / 0.5f); }
                }
                move = away * 1.4f + (W * 0.5f - petX) / (W * 0.5f) * 0.6f;
                move = Clamp(move, -1f, 1f);
                if (Math.Abs(move) < 0.2f) move = 0f;
            }
            float before = petX;
            petX = Clamp(petX + move * PetPace * dt, 0.08f, W - 0.08f);
            petY = 0f;
            bool walking = Math.Abs(petX - before) > 1e-6f;
            if (walking) petFacesRight = petX > before;
            petPose = petHurt > 0f ? "hurt" : cheerT > 0f ? "cheer" : walking ? "walk" : "idle";
        }

        void MoveFoes(float dt)
        {
            float sp = packSpeed * MathF.Pow(1.05f, Math.Min(wave, WaveCap) - 1) * (1f - FrostSlow[lv[Frost]]);
            float decay = MathF.Exp(-5f * dt);
            for (int i = 0; i < foeCount; i++)
            {
                Foe f = foes[i];
                if (f.hp <= 0) continue;
                f.age += dt;
                if (f.handCd > 0f) f.handCd -= dt;
                if (f.orbCd > 0f) f.orbCd -= dt;
                float v = KindSpeed[f.kind] * sp;
                if (f.kind == FlyerKind)
                {
                    float dx = petX - f.x, dy = PetMid + 0.03f - f.y;
                    float d = Len(dx, dy);
                    if (d > 0.001f)
                    {
                        float ux = dx / d, uy = dy / d;
                        float wv = MathF.Sin(f.age * 3.2f + f.weave) * 0.55f;      // a gentle weave across its way
                        f.x += (ux - uy * wv) * v * dt;
                        f.y += (uy + ux * wv) * v * dt;
                        if (Math.Abs(dx) > 0.02f) f.left = dx < 0f;
                    }
                    f.x += f.kx * dt; f.y += f.ky * dt;
                    f.y = Clamp(f.y, 0.05f, 1.12f);
                }
                else
                {
                    float dx = petX - f.x;
                    if (Math.Abs(dx) > 0.01f) { f.x += (dx > 0f ? v : -v) * dt; f.left = dx < 0f; }
                    f.x += f.kx * dt;
                    f.y = KindSize[f.kind] * 0.5f;
                }
                f.kx *= decay; f.ky *= decay;
                f.x = Clamp(f.x, -0.15f, W + 0.15f);
                foes[i] = f;
            }
            // those on the floor do not stand inside each other
            for (int i = 0; i < foeCount; i++)
            {
                if (foes[i].hp <= 0 || foes[i].kind == FlyerKind) continue;
                for (int j = i + 1; j < foeCount; j++)
                {
                    if (foes[j].hp <= 0 || foes[j].kind == FlyerKind) continue;
                    float gap = (KindSize[foes[i].kind] + KindSize[foes[j].kind]) * 0.3f;
                    float dx = foes[j].x - foes[i].x;
                    float a = Math.Abs(dx);
                    if (a >= gap) continue;
                    float push = Math.Min(gap - a, 0.25f * dt) * 0.5f;
                    float s = dx > 0f ? 1f : dx < 0f ? -1f : ((foes[i].id & 1) == 0 ? 1f : -1f);
                    foes[i].x -= s * push; foes[j].x += s * push;
                }
            }
        }

        static float FoeRadius(int kind) { return KindSize[kind] * 0.45f; }

        /// <summary>Take hp from an enemy and push it (field units a second, dying away); a beaten one leaves a gem.</summary>
        void Hit(int i, int dmg, float kx, float ky)
        {
            if (foes[i].hp <= 0) return;
            float heavy = foes[i].kind == BruteKind ? 0.5f : 1f;
            foes[i].hp -= dmg;
            foes[i].kx += kx * heavy;
            if (foes[i].kind == FlyerKind) foes[i].ky += ky;
            if (foes[i].hp > 0) { Sfx("hit"); return; }
            score++;
            Sfx("pop");
            float size = KindSize[foes[i].kind];
            AddFx(GlassArt.Puff, foes[i].x, foes[i].y, size * 0.8f, size * 1.5f, 0.35f);
            DropGem(foes[i].x, foes[i].y, KindXp[foes[i].kind]);
        }

        void DropGem(float x, float y, int value)
        {
            if (gemCount == MaxGems) { gems[0].value += value; return; }    // too many lie about: the oldest grows instead
            GemT g = new GemT();
            g.id = NewId(); g.value = value; g.x = Clamp(x, 0.03f, W - 0.03f); g.y = Math.Max(0.03f, y); g.vy = 0.25f; g.age = 0f; g.pulled = false;
            gems[gemCount++] = g;
        }

        void SweepFoes()
        {
            int k = 0;
            for (int i = 0; i < foeCount; i++)
                if (foes[i].hp > 0) foes[k++] = foes[i];
            foeCount = k;
        }

        void Hands(GlassInput input)
        {
            int dmg = lv[Giant] >= 3 ? 2 : 1;
            for (int h = 0; h < 2; h++)
            {
                if (!GlassGames.Acts(input, h)) continue;
                float hx = input.x[h], hy = input.y[h], hr = GlassGames.HandRadius(this, input, h);
                for (int i = 0; i < foeCount; i++)
                {
                    if (foes[i].hp <= 0 || foes[i].handCd > 0f) continue;
                    float dx = foes[i].x - hx, dy = foes[i].y - hy;
                    float reach = hr + FoeRadius(foes[i].kind);
                    if (dx * dx + dy * dy > reach * reach) continue;
                    foes[i].handCd = 0.35f;
                    float d = Len(dx, dy), kx, ky;
                    if (foes[i].kind == FlyerKind && d > 0.005f) { kx = dx / d * 0.9f; ky = dy / d * 0.9f; }
                    else { kx = (Math.Abs(dx) > 0.01f ? (dx > 0f ? 1f : -1f) : (foes[i].x >= petX ? 1f : -1f)) * 0.9f; ky = 0.4f; }
                    Hit(i, dmg, kx, ky);
                }
            }
        }

        /// <summary>The n living enemies nearest the partner within range, into picked3 (indices); how many were found.</summary>
        int Nearest(int n, float range)
        {
            int found = 0;
            for (int k = 0; k < n && k < 3; k++)
            {
                int bestI = -1; float best = range * range;
                for (int i = 0; i < foeCount; i++)
                {
                    if (foes[i].hp <= 0) continue;
                    bool taken = false;
                    for (int q = 0; q < found; q++) if (picked3[q] == i) taken = true;
                    if (taken) continue;
                    float dx = foes[i].x - petX, dy = foes[i].y - PetMid;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < best) { best = d2; bestI = i; }
                }
                if (bestI < 0) break;
                picked3[found++] = bestI;
            }
            return found;
        }

        void StarShot(float dt)
        {
            if (shotT > 0f) { shotT -= dt; if (shotT > 0f) return; }
            int n = Nearest(lv[Star], StarRange);
            if (n == 0) return;                           // it waits, ready
            shotT = StarEvery[lv[Star]];
            for (int k = 0; k < n && shotCount < MaxShots; k++)
            {
                int i = picked3[k];
                float dx = foes[i].x - petX, dy = foes[i].y - (PetMid + 0.04f);
                float d = Math.Max(0.001f, Len(dx, dy));
                ShotT s = new ShotT();
                s.id = NewId(); s.target = foes[i].id; s.x = petX; s.y = PetMid + 0.04f;
                s.vx = dx / d * StarSpeed; s.vy = dy / d * StarSpeed; s.life = 1.2f;
                shots[shotCount++] = s;
            }
        }

        void MoveShots(float dt)
        {
            int k = 0;
            for (int n = 0; n < shotCount; n++)
            {
                ShotT s = shots[n];
                s.life -= dt;
                // it follows the one it was sent at for as long as that one lives
                for (int i = 0; i < foeCount; i++)
                {
                    if (foes[i].id != s.target || foes[i].hp <= 0) continue;
                    float dx = foes[i].x - s.x, dy = foes[i].y - s.y;
                    float d = Len(dx, dy);
                    if (d > 0.001f) { s.vx = dx / d * StarSpeed; s.vy = dy / d * StarSpeed; }
                    break;
                }
                s.x += s.vx * dt; s.y += s.vy * dt;
                bool gone = s.life <= 0f || s.x < -0.3f || s.x > W + 0.3f || s.y < -0.2f || s.y > 1.3f;
                for (int i = 0; i < foeCount && !gone; i++)
                {
                    if (foes[i].hp <= 0) continue;
                    float dx = foes[i].x - s.x, dy = foes[i].y - s.y;
                    float reach = FoeRadius(foes[i].kind) + 0.03f;
                    if (dx * dx + dy * dy > reach * reach) continue;
                    Hit(i, 1, s.vx / StarSpeed * 0.25f, s.vy / StarSpeed * 0.25f);
                    gone = true;
                }
                if (!gone) shots[k++] = s;
            }
            shotCount = k;
        }

        void OrbAt(int i, out float x, out float y)
        {
            float a = clock * OrbTurn + i * 6.2831853f / Math.Max(1, lv[Moons]);
            x = petX + MathF.Cos(a) * OrbitRadius;
            y = PetMid + 0.01f + MathF.Sin(a) * OrbitRadius;
        }

        void Orbs()
        {
            for (int o = 0; o < lv[Moons]; o++)
            {
                float ox, oy;
                OrbAt(o, out ox, out oy);
                for (int i = 0; i < foeCount; i++)
                {
                    if (foes[i].hp <= 0 || foes[i].orbCd > 0f) continue;
                    float dx = foes[i].x - ox, dy = foes[i].y - oy;
                    float reach = OrbRadius + FoeRadius(foes[i].kind);
                    if (dx * dx + dy * dy > reach * reach) continue;
                    foes[i].orbCd = 0.5f;
                    Hit(i, 1, (foes[i].x >= petX ? 1f : -1f) * 0.45f, 0.2f);
                }
            }
        }

        void ShockWave(float dt)
        {
            int l = lv[Shock];
            if (ringOn)
            {
                ringAge += dt;
                ringR = ringMax * Math.Min(1f, ringAge / 0.35f);
                for (int i = 0; i < foeCount; i++)
                {
                    if (foes[i].hp <= 0 || foes[i].ring == ringSerial) continue;
                    float dx = foes[i].x - petX, dy = foes[i].y - PetMid;
                    float d = Len(dx, dy);
                    if (d - FoeRadius(foes[i].kind) > ringR) continue;
                    foes[i].ring = ringSerial;
                    float ux = d > 0.005f ? dx / d : (foes[i].x >= petX ? 1f : -1f), uy = d > 0.005f ? dy / d : 0f;
                    if (foes[i].kind != FlyerKind) { ux = ux >= 0f ? 1f : -1f; }
                    Hit(i, 1, ux * 1.4f, uy * 1.4f);
                }
                if (ringAge >= 0.5f) ringOn = false;
            }
            if (l <= 0) return;
            if (waveT > 0f) { waveT -= dt; if (waveT > 0f) return; }
            if (ringOn) return;
            float reach = WaveRadius[l] + 0.06f;
            bool any = false;
            for (int i = 0; i < foeCount && !any; i++)
            {
                if (foes[i].hp <= 0) continue;
                float dx = foes[i].x - petX, dy = foes[i].y - PetMid;
                any = dx * dx + dy * dy < reach * reach;
            }
            if (!any) return;                             // it waits, ready
            waveT = WaveEvery[l];
            ringOn = true; ringAge = 0f; ringR = 0f; ringMax = WaveRadius[l]; ringSerial++;
            Sfx("whoosh", 0.5f);
        }

        void Lightning(float dt)
        {
            int l = lv[Bolt];
            if (l <= 0) return;
            if (boltT > 0f) { boltT -= dt; if (boltT > 0f) return; }
            int n = Nearest(l, BoltRange);
            if (n == 0) return;                           // it waits, ready
            boltT = BoltEvery[l];
            for (int k = 0; k < n; k++)
            {
                int i = picked3[k];
                float top = 1.02f, y = foes[i].y;
                AddFx(GlassArt.Bolt, foes[i].x, (top + y) * 0.5f, top - y, top - y, 0.28f);
                Sfx("hit");
                Hit(i, 2, 0f, 0f);
            }
        }

        void TouchPet()
        {
            for (int i = 0; i < foeCount; i++)
            {
                if (foes[i].hp <= 0) continue;
                float r = FoeRadius(foes[i].kind) * 0.8f;
                float dx = foes[i].x - petX, dy = foes[i].y - PetMid;
                if (Math.Abs(dx) > PetHalfWidth + r || Math.Abs(dy) > GlassGames.PetHeight * 0.5f + r) continue;
                // it bounces off, whether it bit or not
                float side = Math.Abs(dx) > 0.005f ? (dx > 0f ? 1f : -1f) : ((foes[i].id & 1) == 0 ? 1f : -1f);
                foes[i].kx = side * (foes[i].kind == BruteKind ? 0.6f : 1.0f);
                if (foes[i].kind == FlyerKind) foes[i].ky = 0.6f;
                if (invuln > 0f) continue;
                lives--;
                invuln = 1.2f;
                petHurt = 0.5f;
                petPose = "hurt";
                Sfx("error", 0.3f);
                LoseHeartFx();
                if (lives <= 0)
                {
                    lives = 0;
                    overT = 0f;
                    int s = (int)seconds;
                    End("Level " + level + ", " + score + " beaten in " + (s / 60) + ":" + (s % 60).ToString("00") + " - what a night!");
                    return;
                }
            }
        }

        void MoveGems(float dt, GlassInput input)
        {
            float reach = MagnetReach;
            int k = 0;
            for (int n = 0; n < gemCount; n++)
            {
                GemT g = gems[n];
                g.age += dt;
                float dx = petX - g.x, dy = PetMid - g.y;
                float d = Len(dx, dy);
                if (!g.pulled)
                {
                    if (g.y > 0.03f) { g.vy -= 1.8f * dt; g.y = Math.Max(0.03f, g.y + g.vy * dt); }      // it falls to the floor
                    bool ripe = g.age > GemRipe;              // it is seen to fall out before anything takes it
                    // the floor distance counts for what lies there, so that walking over a gem always takes it
                    if (ripe && (d < reach || (g.y <= 0.031f && Math.Abs(dx) < reach))) g.pulled = true;
                    for (int h = 0; h < 2 && ripe && !g.pulled; h++)
                    {
                        if (!GlassGames.Acts(input, h)) continue;
                        float hr = GlassGames.HandRadius(this, input, h) + 0.03f;
                        float hx = g.x - input.x[h], hy = g.y - input.y[h];
                        if (hx * hx + hy * hy < hr * hr) g.pulled = true;
                    }
                    if (!g.pulled && g.age > 30f) continue;       // nobody came for it
                }
                else
                {
                    float step = 1.4f * dt;
                    if (d <= step + 0.04f)
                    {
                        xp += g.value;
                        Sfx("coin", 0.2f);
                        AddFx(GlassArt.Sparkle, petX, PetMid + 0.06f, 0.05f, 0.11f, 0.3f);
                        continue;
                    }
                    g.x += dx / d * step; g.y += dy / d * step;
                }
                gems[k++] = g;
            }
            gemCount = k;
        }

        // ------------------------------------------------------------------ level up

        void LevelUp()
        {
            xp -= XpNeeded;
            level++;
            cheerT = 0.8f;
            Sfx("sparkle", 0.3f);
            // what can still grow, three different ones of it in a random order
            offerCount = 0;
            int pool = 0;
            for (int a = 0; a < AbilityIds.Length; a++) if (CanGrow(a)) pool++;
            for (int k = 0; k < 3 && pool > 0; k++)
            {
                int pick = rng.Int(pool);
                for (int a = 0; a < AbilityIds.Length; a++)
                {
                    if (!CanGrow(a) || Offered(a)) continue;
                    if (pick-- > 0) continue;
                    offer[offerCount++] = a;
                    break;
                }
                pool--;
            }
            if (offerCount == 0)
            {
                // nothing is left to learn: a heart instead (there is none to give when all six are there)
                if (lives < MaxLives) lives++;
                Say("Level " + level + "!", 1.5f);
                return;
            }
            offerNames = new string[offerCount]; offerNotes = new string[offerCount]; offerIds = new string[offerCount];
            for (int k = 0; k < offerCount; k++)
            {
                int a = offer[k];
                offerIds[k] = AbilityIds[a];
                offerNames[k] = AbilityNames[a];
                offerNotes[k] = a == Heart ? "One more heart: " + (lives + 1) + " in all" : "Lv " + (lv[a] + 1) + ": " + Notes[a][lv[a]];
                hold[k] = 0f;
            }
            choices = offerNames; choiceNotes = offerNotes;
            takenIdx = -1;
            SetPhase(ChoosePhase);
            petPose = "cheer";
            Say("Level up! Pick a power", 0f);
        }

        bool CanGrow(int a) { return a == Heart ? lives < MaxLives : lv[a] < TopLevel; }
        bool Offered(int a)
        {
            for (int k = 0; k < offerCount; k++) if (offer[k] == a) return true;
            return false;
        }

        protected override void TickChoose(float dt, GlassInput input)
        {
            petPose = "cheer";
            if (phaseT >= AutoPick) { Take(0); return; }
            bool filling = false;
            for (int k = 0; k < offerCount; k++)
            {
                float cx = CardX(k, offerCount), halfW = CardSize * CardAspect * 0.5f, halfH = CardSize * 0.5f;
                bool under = false;
                for (int h = 0; h < 2; h++)
                    if (GlassGames.Acts(input, h) && Math.Abs(input.x[h] - cx) <= halfW && Math.Abs(input.y[h] - CardY) <= halfH) under = true;
                if (under && phaseT >= HoldArms) { hold[k] += dt; filling = true; }
                else hold[k] = Math.Max(0f, hold[k] - 2.5f * dt);
                if (hold[k] >= HoldToPick) { Take(k); return; }
            }
            if (filling) Sfx("tick", 0.2f);
        }

        public override void Choose(int i)
        {
            if (phase == ChoosePhase && i >= 0 && i < offerCount) Take(i);
        }

        void Take(int k)
        {
            int a = offer[k];
            if (a == Heart) { if (lives < MaxLives) lives++; lv[a]++; }
            else if (lv[a] < TopLevel) lv[a]++;
            if (a == Giant) handRadius = GlassGames.HandRadiusBase * GiantTimes[lv[Giant]];
            if (a == Shock) waveT = Math.Min(waveT, 0.5f);
            if (a == Bolt) boltT = Math.Min(boltT, 0.5f);
            SyncAbilities();
            takenIdx = a; takenX = CardX(k, offerCount); takenName = offerNames[k]; takenNote = offerNotes[k];
            offerCount = 0;
            offerIds = None; offerNames = None; offerNotes = None;
            choices = None; choiceNotes = None;
            Sfx("ta-da", 0f);
            SetPhase(Play);
            grace = Grace;
            invuln = Math.Max(invuln, Grace + 0.8f);
            cheerT = 0.8f;
            Say(takenName + (a == Heart ? "!" : " Lv " + lv[a] + "!"), 1.6f);
        }

        protected override void TickOver(float dt, GlassInput input)
        {
            overT += dt;
            petPose = "down";
        }

        // ------------------------------------------------------------------ drawing

        protected override void Draw(GlassInput input)
        {
            float fade = phase == Over ? Clamp01(1f - overT / 1.5f) : 1f;

            for (int n = 0; n < gemCount; n++)
            {
                float a = gems[n].pulled ? 1f : Math.Min(1f, (30f - gems[n].age) / 3f);
                float bob = gems[n].pulled ? 0f : MathF.Sin(clock * 3f + gems[n].id) * 0.006f;
                Put(gems[n].id, GlassArt.Gem, gems[n].x, gems[n].y + bob, gems[n].value >= 3 ? 0.06f : 0.042f, 0f, a * fade, gems[n].value >= 3 ? 1 : 0);
            }

            bool frosty = lv[Frost] > 0;
            for (int i = 0; i < foeCount; i++)
            {
                if (foes[i].hp <= 0) continue;
                int kind = foes[i].kind;
                float size = KindSize[kind];
                float a = Math.Min(1f, foes[i].age / 0.3f) * fade;
                float y = foes[i].y, rot = 0f;
                if (kind != FlyerKind)
                {
                    float beat = foes[i].age * (kind == BruteKind ? 5f : 9f) + foes[i].weave;
                    y += Math.Abs(MathF.Sin(beat)) * size * 0.08f;        // a little hop as it walks
                    rot = MathF.Sin(beat) * 5f;
                }
                else rot = MathF.Sin(foes[i].age * 3.2f + foes[i].weave) * 10f;
                Put(foes[i].id, KindArt[kind], foes[i].x, y, size, rot, a, foes[i].left ? 1 : 0);
                if (frosty) Put(1000000 + foes[i].id, GlassArt.Frost, foes[i].x, y, size * 1.25f, 0f, (0.22f + 0.09f * lv[Frost]) * a, 0);
            }

            if (phase != Over)
                for (int o = 0; o < lv[Moons]; o++)
                {
                    float ox, oy;
                    OrbAt(o, out ox, out oy);
                    Put(20 + o, GlassArt.Orb, ox, oy, OrbRadius * 2f, clock * 90f, 1f, o);
                }

            for (int n = 0; n < shotCount; n++)
                Put(shots[n].id, GlassArt.Shot, shots[n].x, shots[n].y, 0.05f, MathF.Atan2(shots[n].vy, shots[n].vx) * Rad2Deg, fade, 0);

            if (ringOn && ringR > 0.01f)
                Put(30, GlassArt.Ring, petX, PetMid, ringR * 2f, 0f, Clamp01(1.4f - ringAge / 0.5f * 1.3f) * fade, 0);

            DrawFx(fade);

            if (phase == ChoosePhase)
            {
                float appear = Clamp01(0.2f + phaseT / 0.25f);
                for (int k = 0; k < offerCount; k++)
                    Card(10 + k, CardX(k, offerCount), offerNames[k], offerNotes[k], Clamp01(hold[k] / HoldToPick), appear, offer[k]);
            }
            else if (phase == Play && grace > 0f && takenIdx >= 0)
                Card(13, takenX, takenName, takenNote, 1f, Clamp01(grace / Grace), takenIdx);     // the one that was taken, going
        }

        void Card(int id, float x, string name, string note, float fill, float alpha, int frame)
        {
            if (!(alpha > 0.015f)) return;
            GlassSprite s = new GlassSprite();
            s.id = id; s.art = GlassArt.Card; s.x = x; s.y = CardY; s.size = CardSize; s.rot = 0f; s.alpha = alpha;
            s.frame = frame; s.text = name; s.text2 = note; s.fill = fill;
            sprites.Add(s);
        }
    }
}
