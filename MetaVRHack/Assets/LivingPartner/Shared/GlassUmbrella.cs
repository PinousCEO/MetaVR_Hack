using System;

namespace LivingPartner.Shared
{
    /// <summary>
    /// "Shadow Umbrella": the partner gathers stars from the floor by itself while ink rains down; the user's hand (its
    /// shadow, inside the PC) is the umbrella. Golden drops are good and must be let through.
    ///
    /// Sprites (back to front): Star (on the floor), Drop / Blob / Gold (rot = the slant of its fall: 0 = straight
    /// down, positive = it drifts to the right), the effects (Splash where ink was caught or met the floor, Puff where
    /// a golden drop was wasted, Sparkle, Heart = one that was just lost, Label "+3").
    /// Poses: idle, walk, pick (bending down for a star), cower (ink is about to hit and no hand is over it), hurt,
    /// down. Sounds: drop (caught), error (hit), sparkle (a star), coin (gold arrived), squeak (gold wasted), ta-da
    /// (a level), whoosh (the wind turns), lose.
    /// score counts stars: one for each picked up, three for a golden drop that reached the partner.
    ///
    /// How it goes: three stars lie on the floor; the partner walks to the nearest, bends down for half a second and has
    /// it; five stars make a level. Ink: 60% of the drops are aimed at where the partner will be, the rest fall
    /// anywhere. A drop is caught when it comes within the hand's reach (plus its own radius); a Blob (from level 2)
    /// only within BlobNeedsMiddle of the hand's reach - the hand has to go and meet it. From level 3 the wind (Wind)
    /// carries the drops sideways and turns every 6 s. From level 5 a second stream is driven hard by that wind: it
    /// comes in at about 39 degrees from the side the wind blows from, UNDER an umbrella held high - so one hand goes
    /// over the partner and the other beside it. One drop in eight is golden (never in the side stream): it falls
    /// slowly, straight, leaning towards the partner, and for the moment in which it passes the place of the umbrella
    /// no ink comes down on the partner - lift the hand, let it through, put the hand back.
    /// With ink about to hit and no hand over it the partner stops and cowers (it gathers nothing then).
    /// </summary>
    public sealed class GlassUmbrella : GlassGameBase
    {
        public override string Template { get { return "umbrella"; } }
        protected override string DefaultHowTo { get { return "Your hand is the umbrella. Let the golden drops through!"; } }

        // ---- tuning
        public const float PetSpeed = 0.30f;            // x pack.speed
        public const float PickSeconds = 0.5f;
        public const int StarsPerLevel = 5;
        public const float GoldShare = 1f / 8f, BlobShare = 0.25f, AimedShare = 0.6f;
        public const float BlobNeedsMiddle = 0.8f;      // a blob is caught only within this share of the hand's reach
        public const float PetHitHalfWidth = 0.045f;    // ink hits the partner when its middle is nearer than this (plus half its own radius) to the partner's middle: a little less than an open hand held over it covers
        public const float SideRainSlant = 0.8f;        // level 5+: the second stream moves sideways this much for every unit it falls
        public const float GoldLean = 0.22f;            // a golden drop drifts towards the partner this fast, so that letting it through is worth it
        const int DropKind = 0, BlobKind = 1, GoldKind = 2;
        static readonly float[] KindSize = { 0.055f, 0.10f, 0.07f };
        static readonly float[] KindFall = { 1f, 0.7f, 0.55f };
        static readonly string[] KindArt = { GlassArt.Drop, GlassArt.Blob, GlassArt.Gold };
        const float TopY = 1.06f;

        struct Ink { public int id, kind; public float x, y, vx, vy, age, glint; }
        const int MaxInk = 64;
        readonly Ink[] ink = new Ink[MaxInk];
        int inkCount;

        const int StarCount = 3;
        readonly float[] starX = new float[StarCount], starAge = new float[StarCount];
        readonly int[] starId = new int[StarCount];

        float pickT, invuln, spawnT, spawnT2, wind, windGoal, windT, overT;
        int stars, caught, goldTaken, goldWasted, hits;
        bool cowering, sheltered;
        // The rain eases while a golden drop passes the last HushAbove field units above the partner's middle (where a
        // hand held over it would catch it), and for HushAfter seconds after it is down.
        public const float HushAbove = 0.36f, HushAfter = 0.35f;
        const float CowerLead = 0.45f;                 // it ducks this long before ink that nothing will stop

        // ---- for the renderers and the tests
        /// <summary>How fast the wind carries a drop sideways right now, field units a second (0 below level 3; + = to the right).</summary>
        public float Wind { get { return wind; } }
        /// <summary>Stars picked up from the floor (five of them make a level).</summary>
        public int Stars { get { return stars; } }
        public int Caught { get { return caught; } }
        public int GoldTaken { get { return goldTaken; } }
        public int GoldWasted { get { return goldWasted; } }
        public int Hits { get { return hits; } }
        public int InkCount { get { return inkCount; } }
        /// <summary>Seconds between two drops of the main stream at this level.</summary>
        public float DropEvery { get { return Math.Max(0.30f, 1.1f - 0.13f * (level - 1)) / packSpawn; } }
        /// <summary>How fast a plain drop falls at this level, field units a second.</summary>
        public float FallSpeed { get { return Math.Min(1.1f, 0.45f + 0.07f * (level - 1)) * packSpeed; } }

        protected override void OnStart()
        {
            inkCount = 0;
            pickT = 0f; invuln = 0f; spawnT = 0.8f; spawnT2 = 1.4f; wind = 0f; windGoal = 0f; windT = 0f; overT = 0f;
            stars = caught = goldTaken = goldWasted = hits = 0;
            cowering = false; sheltered = false;
            for (int i = 0; i < StarCount; i++) starX[i] = -1f;
            for (int i = 0; i < StarCount; i++) PlaceStar(i);
        }

        void PlaceStar(int i)
        {
            float x = W * 0.5f;
            for (int t = 0; t < 60; t++)
            {
                x = rng.Range(0.10f, W - 0.10f);
                if (Math.Abs(x - petX) < 0.22f) continue;                 // never under the partner
                bool free = true;
                for (int j = 0; j < StarCount; j++)
                    if (j != i && starX[j] >= 0f && Math.Abs(starX[j] - x) < 0.14f) free = false;
                if (free) break;
            }
            if (Math.Abs(x - petX) < 0.22f) x = petX < W * 0.5f ? petX + 0.3f : petX - 0.3f;
            starX[i] = x; starAge[i] = 0f; starId[i] = NewId();
        }

        int NearestStar()
        {
            int best = 0;
            for (int i = 1; i < StarCount; i++)
                if (Math.Abs(starX[i] - petX) < Math.Abs(starX[best] - petX)) best = i;
            return best;
        }

        protected override void TickIntro(float dt, GlassInput input)
        {
            for (int i = 0; i < StarCount; i++) starAge[i] += dt;
        }

        // ------------------------------------------------------------------ play

        protected override void TickPlay(float dt, GlassInput input)
        {
            seconds += dt;
            if (invuln > 0f) invuln -= dt;
            for (int i = 0; i < StarCount; i++) starAge[i] += dt;

            TurnWind(dt);
            Rain(dt);
            MoveInk(dt, input);
            if (phase != Play) return;
            MovePet(dt, input);
        }

        void TurnWind(float dt)
        {
            if (level < 3) { wind = 0f; windGoal = 0f; windT = 0f; return; }
            windT -= dt;
            if (windT <= 0f)
            {
                windT = 6f;
                float strength = (0.10f + 0.025f * Math.Min(level - 3, 5)) * rng.Range(0.75f, 1f) * packSpeed;
                float side = windGoal > 0f ? -1f : windGoal < 0f ? 1f : (rng.Chance(0.5f) ? 1f : -1f);
                windGoal = side * strength;
                Sfx("whoosh", 1f);
            }
            wind = Toward(wind, windGoal, 0.3f * dt);
        }

        void Rain(float dt)
        {
            spawnT -= dt;
            if (spawnT <= 0f) { spawnT += DropEvery; AddInk(false); }
            if (level >= 5)                               // a second stream, driven hard by the wind: it comes in from the side, under an umbrella held high
            {
                spawnT2 -= dt;
                if (spawnT2 <= 0f) { spawnT2 += DropEvery * 1.5f; AddInk(true); }
            }
        }

        /// <summary>Where the partner will be in t seconds, as far as it can be told (it walks to its star and stops there).</summary>
        float PetIn(float t)
        {
            if (cowering) return petX;
            if (!sheltered) t -= CowerLead;               // with no hand over it, it will stop and duck when the drop is that near
            if (t < 0f) t = 0f;
            float goal = starX[NearestStar()];
            return Toward(petX, goal, PetSpeed * packSpeed * t);
        }

        void AddInk(bool side)
        {
            if (inkCount == MaxInk) return;
            int kind = !side && rng.Chance(GoldShare) ? GoldKind : level >= 2 && rng.Chance(BlobShare) ? BlobKind : DropKind;
            float pace = rng.Range(0.93f, 1.07f);
            float vy = FallSpeed * KindFall[kind] * pace;
            float t = (TopY - 0.14f) / vy;                                 // until it is at the partner's height
            // THE RAIN EASES AROUND A GOLDEN DROP: for the moment in which the umbrella has to be lifted for it, no ink
            // comes down on the partner - letting the gold through is a matter of seeing it and lifting in time, not of
            // paying a heart for it.
            if (kind == GoldKind)
            {
                // ink that is already falling would arrive in that moment: then this one is plain ink after all
                float from = t - (HushAbove / vy + 0.15f), to = t + HushAfter;
                for (int n = 0; n < inkCount; n++)
                {
                    if (ink[n].kind == GoldKind) continue;
                    float a = (ink[n].y - 0.14f) / ink[n].vy;
                    if (a <= from || a >= to) continue;
                    if (Math.Abs(ink[n].x + ink[n].vx * a - PetIn(a)) >= 0.3f) continue;     // that one comes down far from it
                    kind = DropKind; vy = FallSpeed * pace; t = (TopY - 0.14f) / vy;
                    break;
                }
            }
            bool hush = false;
            if (kind != GoldKind)
                for (int n = 0; n < inkCount && !hush; n++)
                {
                    if (ink[n].kind != GoldKind) continue;
                    float a = (ink[n].y - 0.14f) / ink[n].vy;              // when the golden one is down
                    hush = t > a - (HushAbove / ink[n].vy + 0.15f) && t < a + HushAfter;
                }
            if (hush && side) return;

            Ink d = new Ink();
            d.id = NewId(); d.kind = kind; d.age = 0f; d.glint = 0f; d.vy = vy;
            bool aimed = kind == GoldKind || (rng.Chance(AimedShare) && !hush);
            float tx = aimed ? PetIn(t) + rng.Range(-0.03f, 0.03f) : rng.Range(0.05f, W - 0.05f);
            if (hush)
            {
                float keep = PetIn(t);
                for (int tries = 0; tries < 8 && Math.Abs(tx - keep) < 0.3f; tries++) tx = rng.Range(0.05f, W - 0.05f);
                if (Math.Abs(tx - keep) < 0.3f) return;
            }
            if (side) d.vx = (windGoal >= 0f ? 1f : -1f) * SideRainSlant * d.vy;
            else d.vx = kind == GoldKind ? 0f : wind;                       // the golden ones fall straight
            d.x = tx - d.vx * t; d.y = TopY;
            // one that would start beyond the side edge starts AT the edge, lower down, on the same line
            float lo = -0.08f, hi = W + 0.08f;
            if (d.x < lo && d.vx > 0.001f) { d.y -= (lo - d.x) / d.vx * d.vy; d.x = lo; }
            else if (d.x > hi && d.vx < -0.001f) { d.y -= (hi - d.x) / d.vx * d.vy; d.x = hi; }
            d.x = Clamp(d.x, lo, hi);
            if (d.y < 0.3f) d.y = 0.3f;
            ink[inkCount++] = d;
        }

        void MoveInk(float dt, GlassInput input)
        {
            int k = 0;
            for (int n = 0; n < inkCount; n++)
            {
                Ink d = ink[n];
                d.age += dt;
                if (d.kind == GoldKind)
                {
                    d.x = Toward(d.x, petX, GoldLean * dt);                  // it leans a little towards the one it is meant for
                    d.glint -= dt;
                    if (d.glint <= 0f) { d.glint = 0.3f; AddFx(GlassArt.Sparkle, d.x + rng.Range(-0.02f, 0.02f), d.y + 0.04f, 0.035f, 0.015f, 0.4f); }
                }
                d.x += d.vx * dt; d.y -= d.vy * dt;
                float r = KindSize[d.kind] * 0.45f;

                // the umbrella
                bool gone = false;
                for (int h = 0; h < 2 && !gone; h++)
                {
                    if (!GlassGames.Acts(input, h)) continue;
                    float hr = GlassGames.HandRadius(this, input, h);
                    float dx = d.x - input.x[h], dy = d.y - input.y[h];
                    float reach = d.kind == BlobKind ? hr * BlobNeedsMiddle : hr + r;
                    if (dx * dx + dy * dy > reach * reach) continue;
                    gone = true;
                    if (d.kind == GoldKind)
                    {
                        goldWasted++;
                        AddFx(GlassArt.Puff, d.x, d.y, 0.05f, 0.10f, 0.45f, 0f, -0.05f);
                        Sfx("squeak", 0.2f);
                    }
                    else
                    {
                        caught++;
                        AddFx(GlassArt.Splash, d.x, d.y, KindSize[d.kind] * 1.1f, KindSize[d.kind] * 2f, 0.3f);
                        Sfx("drop", 0.12f);
                    }
                }
                if (gone) continue;

                // the partner
                if (Math.Abs(d.x - petX) < (d.kind == GoldKind ? PetHalfWidth + r : PetHitHalfWidth + r * 0.5f) && d.y - r < GlassGames.PetHeight - 0.01f && d.y > 0f)
                {
                    if (d.kind == GoldKind)
                    {
                        goldTaken++;
                        score += 3;
                        Sfx("coin", 0.1f);
                        AddFx(GlassArt.Sparkle, petX, GlassGames.PetHeight, 0.08f, 0.18f, 0.45f);
                        AddFx(GlassArt.Label, petX, GlassGames.PetHeight + 0.08f, 0.07f, 0.07f, 0.9f, 0f, 0.12f, 0f, "+3");
                    }
                    else
                    {
                        AddFx(GlassArt.Splash, d.x, d.y, KindSize[d.kind] * 1.1f, KindSize[d.kind] * 2f, 0.3f);
                        if (invuln <= 0f)
                        {
                            hits++;
                            lives--;
                            invuln = 1.2f;
                            petHurt = 0.5f;
                            pickT = 0f;
                            Sfx("error", 0.3f);
                            LoseHeartFx();
                            if (lives <= 0)
                            {
                                lives = 0;
                                inkCount = Compact(k, n + 1);
                                overT = 0f;
                                End(score + (score == 1 ? " star" : " stars") + " under your umbrella!");
                                return;
                            }
                        }
                    }
                    continue;
                }

                // the floor
                if (d.y - r <= 0f)
                {
                    AddFx(GlassArt.Splash, d.x, 0.015f, KindSize[d.kind] * 0.9f, KindSize[d.kind] * 1.7f, 0.3f);
                    continue;
                }
                if (d.x < -0.3f || d.x > W + 0.3f) continue;
                ink[k++] = d;
            }
            inkCount = k;
        }

        /// <summary>Keep ink[0..kept) and ink[from..count): what is left when the loop over the ink stops early.</summary>
        int Compact(int kept, int from)
        {
            for (int n = from; n < inkCount; n++) ink[kept++] = ink[n];
            return kept;
        }

        void MovePet(float dt, GlassInput input)
        {
            // ink that is about to hit, and no hand over it?
            bool threat = false;
            for (int n = 0; n < inkCount && !threat; n++)
            {
                if (ink[n].kind == GoldKind) continue;
                float ahead = ink[n].vy > 0.01f ? (ink[n].y - GlassGames.PetHeight) / ink[n].vy : 9f;      // seconds until it is down
                if (ahead < 0f || ahead > CowerLead) continue;
                threat = Math.Abs(ink[n].x + ink[n].vx * ahead - petX) < 0.13f;
            }
            sheltered = false;
            for (int h = 0; h < 2; h++)
                if (GlassGames.Acts(input, h) && Math.Abs(input.x[h] - petX) < 0.17f && input.y[h] > 0.10f && input.y[h] < 0.75f) sheltered = true;
            cowering = threat && !sheltered;

            petY = 0f;
            if (petHurt > 0f) { petPose = "hurt"; return; }
            if (cowering) { petPose = "cower"; pickT = 0f; return; }

            int s = NearestStar();
            float dx = starX[s] - petX;
            if (Math.Abs(dx) > 0.015f)
            {
                pickT = 0f;
                petFacesRight = dx > 0f;
                petX = Toward(petX, starX[s], PetSpeed * packSpeed * dt);
                petPose = "walk";
                return;
            }
            // it bends down for the star
            petPose = "pick";
            pickT += dt;
            if (pickT < PickSeconds) return;
            pickT = 0f;
            stars++; score++;
            Sfx("sparkle", 0.2f);
            AddFx(GlassArt.Sparkle, starX[s], 0.07f, 0.07f, 0.16f, 0.4f);
            PlaceStar(s);
            int l = 1 + stars / StarsPerLevel;
            if (l > level)
            {
                level = l;
                Say("Level " + level, 2f);
                Sfx("ta-da", 1f);
            }
        }

        protected override void TickOver(float dt, GlassInput input)
        {
            overT += dt;
            petPose = "down";
        }

        // ------------------------------------------------------------------ drawing

        protected override void Draw(GlassInput input)
        {
            float fade = phase == Over ? Clamp01(1f - overT / 1.2f) : 1f;
            int near = NearestStar();
            for (int i = 0; i < StarCount; i++)
            {
                float a = Math.Min(1f, 0.15f + starAge[i] / 0.3f) * fade;
                float lift = phase == Play && i == near && petPose == "pick" ? pickT / PickSeconds * 0.05f : 0f;    // it comes up as it is picked
                Put(starId[i], GlassArt.Star, starX[i], 0.05f + lift + MathF.Sin(clock * 2.5f + i * 2.1f) * 0.006f, 0.085f, MathF.Sin(clock * 1.7f + i) * 8f, a, 0);
            }
            for (int n = 0; n < inkCount; n++)
            {
                int kind = ink[n].kind;
                float a = Math.Min(1f, ink[n].age / 0.15f) * fade;
                float rot = MathF.Atan2(ink[n].vx, ink[n].vy) * Rad2Deg;
                if (kind == GoldKind) { rot = MathF.Sin(ink[n].age * 6f) * 12f; a *= 0.85f + 0.15f * MathF.Sin(ink[n].age * 11f); }
                Put(ink[n].id, KindArt[kind], ink[n].x, ink[n].y, KindSize[kind], rot, a, 0);
            }
            DrawFx(fade);
        }
    }
}
