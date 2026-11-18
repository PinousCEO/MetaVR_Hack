using System;
using System.Collections.Generic;

namespace LivingPartner.Shared
{
    // The glass games (see Glass.cs for the contract): what they share.
    //
    //   FIELD     x 0..W (W = the aspect, 16:9 unless the renderer sets it before Start), y 0..1 up, the floor at y = 0.
    //   PARTNER   petX, petY are its FEET; it is GlassGames.PetHeight (0.20) tall and about 0.12 wide; its middle is at
    //             y = 0.10. It is NOT in the sprite list: the renderer draws the character itself from petX / petY /
    //             petFacesRight / petPose / petHurt.
    //   HANDS     a hand acts when it is valid and nearer than 0.30 m to the plane (GlassGames.Acts); it reaches
    //             game.handRadius * (0.6 + 0.4 * spread) around its middle (GlassGames.HandRadius).
    //   SPRITES   rebuilt by every Step, listed from the back to the front. x, y is the MIDDLE of the picture and size
    //             its HEIGHT in field units. An id stays with one thing for as long as it lives. alpha is never 0
    //             (things that are invisible are left out).
    //   SOUNDS    SfxSynth names, raised by this Step only; each name at most a few times a second.
    //   HUD       score / lives / level / seconds / banner / result are fields: the renderer writes them itself.
    //   PHASES    intro (1.5 s, the banner is the pack's howTo or the game's own line, nothing dangerous) -> play ->
    //             (choose -> play)* -> over (result is set; stepping goes on harmlessly and what is left fades away).

    /// <summary>Which glass games there are, and how a hand is read by all of them.</summary>
    public static class GlassGames
    {
        public static readonly string[] Templates = { "survival", "umbrella", "script" };     // script = a Lua program (GlassScript)

        public const float PetHeight = 0.20f;           // the partner on the field
        public const float PetMidY = 0.10f;
        public const float HandRadiusBase = 0.085f;     // an open hand, field units
        public const float ActDistance = 0.30f;         // metres from the plane within which a hand acts

        public static bool Is(string template)
        {
            return Index(template) >= 0;
        }

        /// <summary>A new game of that template (not started yet), or null when there is no such template.</summary>
        public static GlassGame Create(string template)
        {
            switch (Index(template))
            {
                case 0: return new GlassSurvival();
                case 1: return new GlassUmbrella();
                case 2: return new GlassScript();
                default: return null;
            }
        }

        static int Index(string template)
        {
            if (string.IsNullOrEmpty(template)) return -1;
            string t = template.Trim();
            for (int i = 0; i < Templates.Length; i++)
                if (string.Equals(t, Templates[i], StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The hand i (0 or 1) is tracked, close enough to the plane to act, and its place is a real number.</summary>
        public static bool Acts(GlassInput input, int i)
        {
            if (input == null || i < 0 || i > 1 || !input.valid[i]) return false;
            float d = input.dist[i], x = input.x[i], y = input.y[i];
            if (!(d < ActDistance)) return false;                       // also false for NaN
            return x > -100f && x < 100f && y > -100f && y < 100f;      // false for NaN and for nonsense
        }

        /// <summary>
        /// What a hand SHOWS, from its stretched fingers (bits: 2 index, 4 middle, 8 ring, 16 little; the thumb, bit 1, is
        /// not asked): "rock" = none of the four, "paper" = an open hand (at least three of the four: a relaxed little
        /// finger often reads as bent), "scissors" = index and middle only, "point" = the index alone, "" = anything
        /// else, and when the fingers are not known (fingers &lt; 0).
        /// </summary>
        public static string Shape(int fingers)
        {
            if (fingers < 0) return "";
            bool i = (fingers & 2) != 0, m = (fingers & 4) != 0, r = (fingers & 8) != 0, l = (fingers & 16) != 0;
            int n = (i ? 1 : 0) + (m ? 1 : 0) + (r ? 1 : 0) + (l ? 1 : 0);
            if (n == 0) return "rock";
            if (n >= 3) return "paper";
            if (i && m) return "scissors";
            if (i && n == 1) return "point";
            return "";
        }

        /// <summary>The finger bits a shape is shown with (for bots and scripted hands): rock 0, paper 30, scissors 6, point 2; anything else -1 (not known).</summary>
        public static int FingersOf(string shape)
        {
            switch ((shape ?? "").Trim().ToLowerInvariant())
            {
                case "rock": case "fist": return 0;
                case "paper": case "open": return 30;
                case "scissors": return 6;
                case "point": return 2;
            }
            return -1;
        }

        /// <summary>How far the hand i reaches around its middle, in field units (an open hand reaches game.handRadius, a fist 0.6 of it).</summary>
        public static float HandRadius(GlassGame game, GlassInput input, int i)
        {
            float s = input.spread[i];
            if (!(s > 0f)) s = 0f; else if (s > 1f) s = 1f;
            return game.handRadius * (0.6f + 0.4f * s);
        }
    }

    /// <summary>
    /// How fast a hand moves, in metres a second: how far it is from where it was about Window seconds ago, over that
    /// time. Not the speed between two frames - 2 mm of tracking jitter at 72 pictures a second is already 0.14 m/s.
    /// 0 while that is not known (the hand has only just been seen). Add once a frame; Lost when it is not tracked,
    /// so that what is seen next is not compared with what was before.
    /// </summary>
    public sealed class HandSpeed
    {
        public const float Window = 0.15f, Oldest = 0.25f, Shortest = 0.08f;
        const int Keep = 48;
        readonly float[] px = new float[Keep], py = new float[Keep], pz = new float[Keep], pt = new float[Keep];
        int next, count;
        public float Speed { get; private set; }

        public void Lost() { count = 0; Speed = 0f; }

        /// <summary>The hand is at (x, y, z) metres at the time t (seconds). Returns its speed.</summary>
        public float Add(float t, float x, float y, float z)
        {
            if (count > 0 && t < pt[(next - 1 + Keep) % Keep]) count = 0;          // (the clock went back: start again)
            float speed = 0f;
            // the place nearest to Window seconds ago, among those not older than Oldest and not younger than Shortest
            float best = float.MaxValue;
            for (int k = count; k >= 1; k--)
            {
                int i = (next - k + Keep) % Keep;
                float age = t - pt[i];
                if (age > Oldest) continue;
                if (age < Shortest) break;
                float off = Math.Abs(age - Window);
                if (off >= best) continue;
                best = off;
                float dx = x - px[i], dy = y - py[i], dz = z - pz[i];
                speed = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) / age;
            }
            // one place every hundredth of a second at most, so that the places kept always reach back far enough
            if (count == 0 || t - pt[(next - 1 + Keep) % Keep] >= 0.01f)
            {
                px[next] = x; py[next] = y; pz[next] = z; pt[next] = t;
                next = (next + 1) % Keep;
                if (count < Keep) count++;
            }
            if (!(speed >= 0f) || speed > 50f) speed = 0f;                          // (NaN, nonsense)
            Speed = speed;
            return speed;
        }
    }

    /// <summary>The games' own random numbers (xorshift32): the same seed gives the same game on every machine.</summary>
    public sealed class GlassRng
    {
        uint s = 0x6D2B79F5u;

        public void Seed(int seed)
        {
            unchecked
            {
                s = (uint)seed * 2654435761u + 0x9E3779B9u;
                if (s == 0u) s = 0x6D2B79F5u;
            }
            for (int i = 0; i < 6; i++) NextU();
        }

        public uint NextU()
        {
            unchecked
            {
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
            }
            return s;
        }

        /// <summary>0 (included) .. 1 (not included).</summary>
        public float Next01() { return (NextU() >> 8) * (1f / 16777216f); }
        public float Range(float a, float b) { return a + (b - a) * Next01(); }
        /// <summary>0 .. n - 1.</summary>
        public int Int(int n) { return n <= 1 ? 0 : (int)(NextU() % (uint)n); }
        public bool Chance(float p) { return Next01() < p; }
    }

    /// <summary>
    /// What the three games have in common: the phases, the clock, the banner, sounds that do not repeat too fast, and
    /// a pool of short-lived effects (puffs, splashes, sparkles, a heart that was lost, a "+3" label).
    /// </summary>
    public abstract class GlassGameBase : GlassGame
    {
        public const string Intro = "intro", Play = "play", ChoosePhase = "choose", Over = "over";
        public const float IntroSeconds = 1.5f;
        public const float MaxTick = 1f / 30f;          // a longer frame is stepped in pieces of at most this
        public const int MaxLives = 6;

        protected const float PetHalfWidth = 0.06f;
        protected const float PetMidY = GlassGames.PetMidY;

        protected readonly GlassRng rng = new GlassRng();
        protected GamePackDef pack;
        protected float packSpeed = 1f, packSpawn = 1f;
        protected float clock;                          // everything stepped since Start, in every phase (for motion)
        protected float phaseT;                         // seconds in this phase
        float bannerT;
        int nextId;
        static readonly GlassInput NoHands = new GlassInput();
        static readonly string[] NoStrings = new string[0];
        readonly Dictionary<string, float> soundAt = new Dictionary<string, float>();
        readonly List<string> soundsBetween = new List<string>(4);
        bool stepping, started;

        /// <summary>The line of the intro when the pack has none.</summary>
        protected abstract string DefaultHowTo { get; }
        protected abstract void OnStart();
        protected virtual void OnPlay() { }
        protected virtual void TickIntro(float dt, GlassInput input) { }
        protected abstract void TickPlay(float dt, GlassInput input);
        protected virtual void TickChoose(float dt, GlassInput input) { }
        protected virtual void TickOver(float dt, GlassInput input) { }
        /// <summary>Fill the sprite list (it was cleared), from the back to the front.</summary>
        protected abstract void Draw(GlassInput input);

        public sealed override void Start(GamePackDef pack, int seed)
        {
            this.pack = pack;
            started = true;
            rng.Seed(seed);
            if (!(W >= 0.5f && W <= 4f)) W = 16f / 9f;
            packSpeed = pack != null ? Sane(pack.speed, 0.3f, 3f, 1f) : 1f;
            packSpawn = pack != null ? Sane(pack.spawn, 0.3f, 3f, 1f) : 1f;
            lives = pack != null ? (pack.lives < 1 ? 1 : pack.lives > MaxLives ? MaxLives : pack.lives) : 3;
            score = 0; level = 1; seconds = 0f; result = "";
            choices = NoStrings; choiceNotes = NoStrings;
            sprites.Clear(); sounds.Clear(); soundAt.Clear(); soundsBetween.Clear(); stepping = false;
            if (sprites.Capacity < 256) sprites.Capacity = 256;         // room for everything at once: no growing while it is played
            if (sounds.Capacity < 16) sounds.Capacity = 16;
            fxCount = 0; nextId = 100; clock = 0f; phaseT = 0f;
            petX = W * 0.5f; petY = 0f; petFacesRight = true; petPose = "idle"; petHurt = 0f;
            fog = null; fogCols = 0; fogRows = 0; fogRevision = 0;
            handRadius = GlassGames.HandRadiusBase;
            phase = Intro;
            banner = pack != null && !string.IsNullOrEmpty(pack.howTo) ? pack.howTo : DefaultHowTo;
            bannerT = 0f;                               // it stays for the whole intro
            OnStart();
        }

        public sealed override void Step(float dt, GlassInput input)
        {
            if (!started) Start(null, 0);                                  // (stepped before it was started: a game with no pack)
            sounds.Clear();
            for (int i = 0; i < soundsBetween.Count; i++) sounds.Add(soundsBetween[i]);     // raised by Choose since the last Step
            soundsBetween.Clear();
            if (input == null) input = NoHands;
            if (!(dt > 0f)) dt = 0f; else if (dt > 0.25f) dt = 0.25f;      // NaN, a negative step, a long hitch
            int n = (int)Math.Ceiling(dt / MaxTick - 0.001f);
            if (n < 1) n = 1;
            float h = dt / n;
            stepping = true;
            for (int i = 0; i < n; i++) Tick(h, input);
            stepping = false;
            sprites.Clear();
            Draw(input);
        }

        void Tick(float dt, GlassInput input)
        {
            clock += dt; phaseT += dt;
            if (petHurt > 0f) petHurt = Math.Max(0f, petHurt - dt);
            if (bannerT > 0f) { bannerT -= dt; if (bannerT <= 0f) banner = ""; }
            TickFx(dt);
            if (phase == Intro)
            {
                TickIntro(dt, input);
                if (phaseT >= IntroSeconds) { phase = Play; phaseT = 0f; banner = ""; bannerT = 0f; OnPlay(); }
            }
            else if (phase == Play) TickPlay(dt, input);
            else if (phase == ChoosePhase) TickChoose(dt, input);
            else TickOver(dt, input);
        }

        protected void SetPhase(string p) { phase = p; phaseT = 0f; }

        /// <summary>Show a line; seconds &lt;= 0: until something else is said.</summary>
        protected void Say(string text, float secs) { banner = text ?? ""; bannerT = secs > 0f ? secs : 0f; }

        /// <summary>The game is over: the line for the partner to say, the pose, the sound.</summary>
        protected void End(string line)
        {
            SetPhase(Over);
            result = line ?? "";
            banner = ""; bannerT = 0f;
            choices = NoStrings; choiceNotes = NoStrings;
            petPose = "down";
            Sfx("lose", 0f);
        }

        /// <summary>Raise a sound unless the same one was raised less than minGap seconds ago.</summary>
        protected bool Sfx(string name, float minGap = 0.2f)
        {
            float last;
            if (soundAt.TryGetValue(name, out last) && clock - last < minGap) return false;
            soundAt[name] = clock;
            // a sound raised between two Steps (Choose) would be cleared by the next Step before anyone heard it: it waits for that Step
            if (stepping) sounds.Add(name); else soundsBetween.Add(name);
            return true;
        }

        protected int NewId() { return nextId++; }

        protected void Put(int id, string art, float x, float y, float size, float rot = 0f, float alpha = 1f, int frame = 0)
        {
            if (!(alpha > 0.015f) || !(size > 0f)) return;
            GlassSprite s = new GlassSprite();
            s.id = id; s.art = art; s.x = x; s.y = y; s.size = size; s.rot = rot;
            s.alpha = alpha > 1f ? 1f : alpha; s.frame = frame; s.text = ""; s.text2 = ""; s.fill = 0f;
            sprites.Add(s);
        }

        // ------------------------------------------------------------------ short-lived effects

        protected struct Fx
        {
            public int id, frame;
            public string art, text;
            public float x, y, vx, vy, size0, size1, rot, age, total;
        }

        const int MaxFx = 64;
        readonly Fx[] fx = new Fx[MaxFx];
        int fxCount;

        /// <summary>An effect that lives for `total` seconds, grows from size0 to size1, drifts by vx, vy and fades out over its second half.</summary>
        protected void AddFx(string art, float x, float y, float size0, float size1, float total, float vx = 0f, float vy = 0f, float rot = 0f, string text = "", int frame = 0)
        {
            if (fxCount == MaxFx)                       // full: the oldest makes room
            {
                Array.Copy(fx, 1, fx, 0, MaxFx - 1);
                fxCount--;
            }
            Fx f = new Fx();
            f.id = NewId(); f.art = art; f.text = text ?? ""; f.frame = frame;
            f.x = x; f.y = y; f.vx = vx; f.vy = vy; f.size0 = size0; f.size1 = size1; f.rot = rot; f.age = 0f; f.total = total > 0.01f ? total : 0.01f;
            fx[fxCount++] = f;
        }

        void TickFx(float dt)
        {
            int k = 0;
            for (int i = 0; i < fxCount; i++)
            {
                Fx f = fx[i];
                f.age += dt;
                if (f.age >= f.total) continue;
                f.x += f.vx * dt; f.y += f.vy * dt;
                fx[k++] = f;
            }
            fxCount = k;
        }

        protected void DrawFx(float fade = 1f)
        {
            for (int i = 0; i < fxCount; i++)
            {
                float t = fx[i].age / fx[i].total;
                float a = (t < 0.5f ? 1f : 2f * (1f - t)) * fade;
                if (!(a > 0.015f)) continue;
                GlassSprite s = new GlassSprite();
                s.id = fx[i].id; s.art = fx[i].art; s.x = fx[i].x; s.y = fx[i].y;
                s.size = fx[i].size0 + (fx[i].size1 - fx[i].size0) * t;
                s.rot = fx[i].rot; s.alpha = a > 1f ? 1f : a; s.frame = fx[i].frame; s.text = fx[i].text; s.text2 = ""; s.fill = 0f;
                if (s.size > 0f) sprites.Add(s);
            }
        }

        /// <summary>A heart that was just lost floats up from the partner's head and fades.</summary>
        protected void LoseHeartFx()
        {
            AddFx(GlassArt.Heart, petX, GlassGames.PetHeight + 0.05f, 0.07f, 0.10f, 0.9f, 0f, 0.16f);
        }

        // ------------------------------------------------------------------ small arithmetic

        protected static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
        protected static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
        protected static float Sane(float v, float a, float b, float otherwise) { return v >= a && v <= b ? v : (v < a ? a : v > b ? b : otherwise); }
        protected static float Toward(float v, float goal, float step) { return v < goal ? Math.Min(goal, v + step) : Math.Max(goal, v - step); }
        protected static float Len(float x, float y) { return MathF.Sqrt(x * x + y * y); }
        protected const float Rad2Deg = 57.29578f;
    }
}
