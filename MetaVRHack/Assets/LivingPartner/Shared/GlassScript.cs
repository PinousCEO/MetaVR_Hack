using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;

namespace LivingPartner.Shared
{
    /// <summary>
    /// A glass game that IS a small Lua program (template "script"): the pack's `script`, written by the partner's
    /// language model when the user asked for something ("make me omok", "a pomodoro timer"), checked by running it
    /// (GlassScriptCheck) and then bought and sold like any module. It runs in a LuaSandbox: it can compute, draw,
    /// read the hands, move the partner and keep the score, and nothing else. GlassScriptApi.Doc is the description
    /// of everything a script can call - the text the model writes against.
    ///
    /// The script's top level and start() run in Start, update(dt) in every Step (a long frame in pieces of
    /// MaxTick - but never more than two updates in one frame: after a hitch those two are longer). Drawing is immediate: what update drew is the sprite list of that frame, in the order it was drawn
    /// (back to front), at most MaxSprites things of which MaxLabels with words. ANY error - it does not compile, it
    /// breaks, a call takes too long - ends the game cleanly: phase "over", result BrokeLine, Error says what and
    /// where. Nothing ever throws out of Start or Step. Deterministic for a seed and the same inputs.
    ///
    /// PHASES  intro (IntroSeconds: the pack's howTo is the banner, update runs with dt = 0 and no hands, so the
    ///         field is drawn but nothing moves) -> play -> over (finish(text): update still runs, without hands, so
    ///         that the last picture stays; a script that draws nothing any more keeps its last full frame).
    /// HANDS   hands() lists every valid hand nearer than GlassGames.ActDistance. `down` and taps() depend on where
    ///         the game is played - the renderer says so with GlassGame.touch:
    ///           touch = true  (the flat field on the real desk; dist = the fingertip's height): down = lower than
    ///                         TouchDown (up again above TouchUp); a tap = the moment it goes down.
    ///           touch = false (through a monitor's glass, on the upright stage): every listed hand is down; a tap =
    ///                         the hand has RESTED within RestRadius for RestSeconds (once; it has to move away
    ///                         before it can tap again). While it rests a small ring closes on that spot (art
    ///                         RingShape, ids DwellId..DwellId + 1, fill = how far) so that the user sees it coming.
    ///         all_hands() lists every hand that is SEEN (GlassInput.seen, or valid), also one too far from the field
    ///         to act: where it is, whether it is `near` (= it is in hands() too), and - as in hands() - its `shape`
    ///         (GlassGames.Shape of GlassInput.fingers) and its `speed` (GlassInput.speed, metres a second).
    /// PARTNER petX / petY / petFacesRight / petPose as in every glass game. pet_goto walks it (pose walk); a motion
    ///         the script defined (LuaMotion) is evaluated here every Step: petCustom is true and petCustomPose is
    ///         the pose to show instead of petPose. petLine is what it says (petLineLeft seconds are left).
    /// THROUGH THE GLASS (Glass.cs: GlassOut)
    ///         shoot_out adds a GlassOut to `outs` (runId is left 0: the running side fills it in, TAKES the list and
    ///         clears it; the id counts 1, 2, 3 ... within the run). At most MaxOutsFlying are on their way at once and
    ///         MaxOutsMinute leave in a minute. The verdict comes back through OutResult(id, blocked | hit | missed)
    ///         - from any thread - and the script reads it with out_events(), each once. One that nobody answers
    ///         within OutTimeout seconds (no headset, an old renderer) counts as "missed", so a script never waits
    ///         for ever. inside() is GlassGame.inside (set before Start).
    /// AIMING  GlassInput.aimValid / aimX / aimY: aim() gives the first valid one. GlassInput.fire[i] (true in ONE
    ///         Step) with a valid aim is a shot at that spot: shots(). A tap of a hand that is not aiming is a shot
    ///         too, so a shooting game can be played where nobody can point. A fire inside a button presses it.
    /// UI KIT  panel / button / display / toggle draw in the app's storybook look with plain sprites: boxes of
    ///         GlassArt.Paper (when Rounded; otherwise GlassArt.Rect with the same geometry: the same boxes with
    ///         square corners) and Labels in ink. An outline is an ink box under a smaller coloured one - a renderer
    ///         needs nothing but the paper art. Words are fitted with GlyphWidth (a letter is about that wide, in
    ///         letter heights): right- and left-aligned words are CENTRED labels placed by that estimate.
    /// </summary>
    public sealed class GlassScript : GlassGame
    {
        public const string TemplateName = "script";
        public const string BrokeLine = "Oops - my script broke.";
        public const int MaxSprites = 160, MaxLabels = 32;
        public const float IntroSeconds = 1.2f, MaxTick = 1f / 30f;
        public const float TouchDown = 0.02f, TouchUp = 0.035f;        // metres above the desk
        public const float RestRadius = 0.035f, RestSeconds = 0.5f;
        public const float PetSpeed = 0.6f;
        public const int DwellId = 900;
        public const int FirstId = 1000;
        public const float MinReadable = 0.03f;                         // letters smaller than this cannot be read
        public const int MaxOutsFlying = 6, MaxOutsMinute = 40, MaxOutEvents = 32;
        public const float OutTimeout = 6f;                             // seconds after which something sent out counts as missed
        public const float OutSpeed = 1.2f, OutSize = 0.06f;            // metres a second / metres across (GlassOut's own defaults)
        public const int MaxButtons = 40;
        public const float KitLine = 0.006f;                            // the ink outline of the UI kit, field units
        public const float GlyphWidth = 0.58f;                          // how wide a letter is taken to be, in letter heights (fitting and aligning words)
        public const float PressShown = 0.16f, PressAgain = 0.3f;       // seconds a pressed button stays down / cannot be pressed again
        public const int Paper0 = 0xFFF8EA, Key0 = 0xFFFFFF, Strip0 = 0xF8DDA0, ToggleOn = 0x9FD8B4, ToggleOff = 0xB9AFA6;

        /// <summary>SET BY THE RENDERER before Start: it can draw GlassArt.Paper (rounded boxes). False: the UI kit's boxes are GlassArt.Rect.</summary>
        public bool Rounded;

        /// <summary>The stickers pic() knows (besides the pack's own pictures).</summary>
        public static readonly string[] Stickers =
        {
            GlassArt.Star, GlassArt.Gem, GlassArt.Heart, GlassArt.Gold, GlassArt.Drop, GlassArt.Orb, GlassArt.Sparkle, GlassArt.Puff, GlassArt.Splash,
            GlassArt.Bolt, GlassArt.Shot, GlassArt.Ring, GlassArt.Blob, GlassArt.Frost, GlassArt.Enemy, GlassArt.Flyer, GlassArt.Brute, GlassArt.StoneA, GlassArt.StoneB,
            GlassArt.Rock, GlassArt.PaperSign, GlassArt.Scissors,
        };
        /// <summary>The poses pet_pose knows (GlassPoses.For), besides the motions a script defines.</summary>
        public static readonly string[] Poses = { "idle", "walk", "run", "cheer", "hurt", "pick", "wipe", "cower", "down" };

        public override string Template { get { return TemplateName; } }

        /// <summary>"" while the script is fine; otherwise what broke, with the line ("line 12: attempt to call a nil value  |  foo(1)").</summary>
        public string Error = "";
        public int ErrorLine;
        public string ErrorSource = "";
        /// <summary>Wrong arguments stop the script with an error (the checker) instead of being forgiven with a note (a game that is being played).</summary>
        public bool Strict;
        /// <summary>Count the instructions exactly (slices of 100; the checker).</summary>
        public bool Precise;
        /// <summary>The machine the script runs in (null before Start).</summary>
        public LuaSandbox Box { get { return box; } }
        public LuaMotion Motions { get { return motions; } }
        /// <summary>The motion that is playing ("" = none: petPose says what the partner does).</summary>
        public string PetMotion { get { return playing ?? ""; } }

        // ---- what the script did, for the checker
        public bool ReadHands, ReadTaps, ReadHeld;
        public bool UsedOuts, ReadOutEvents, ReadAim, ReadShots, UsedKit;   // shoot_out / out_events / aim / shots / the UI kit were called
        public int OutsSent, OutsRefused;           // things sent out / left out because too many were on their way
        public bool DrewEver;
        public int Updates;                         // calls of update
        public int MaxDrawn;                        // the most things one frame asked for (also those over the limit)
        public int Dropped;                         // things over MaxSprites / MaxLabels that were left out
        public long Drawn, DrawnOutside;            // things drawn / of those, far outside the field
        public float SmallestText = float.MaxValue;
        public int StartInstructions, PeakUpdate;   // instructions of top level + start / of the busiest update
        public long UpdateInstructions;             // of all updates together
        public float FinishedAt = -1f;              // seconds played when finish() was called (-1 = not yet)

        LuaSandbox box;
        LuaMotion motions;
        GamePackDef pack;
        bool started, canSetFlat;
        float clock, introT, bannerT, lastTick;
        readonly List<GlassSprite> frame = new List<GlassSprite>(MaxSprites + 2);
        readonly List<GlassSprite> kept = new List<GlassSprite>(MaxSprites + 2);
        int labels, asked;
        bool finishedThisTick;
        readonly Dictionary<string, float> soundAt = new Dictionary<string, float>();
        static readonly GlassInput NoHands = new GlassInput();
        readonly DynValue[] one = new DynValue[1];

        // the partner
        string poseWanted = "idle", poseMotion, playing, poseAfter;
        bool playLoop, playHold;
        float playT, poseBackT;
        bool going; float goX, goY, goSpeed;

        // the hands
        const int Hist = 512;
        sealed class Hand
        {
            public bool present, down;
            public bool seen; public float sx, sy, speed; public int fingers = -1;       // seen at all (all_hands): where, how fast, what it shows
            public float x, y, dist, reach, ax, ay, restT;
            public bool fired;
            public readonly float[] hx = new float[Hist], hy = new float[Hist], ht = new float[Hist];
            public int head, count;
        }
        readonly Hand[] hand = { new Hand(), new Hand() };
        readonly float[] tapX = new float[4], tapY = new float[4];
        readonly bool[] tapUsed = new bool[4];
        int tapCount;

        // aiming: where each hand points, the shots of this frame (fires, and the taps of hands that do not aim), the fires alone (they press buttons)
        readonly bool[] aimOk = new bool[2];
        readonly float[] aimPx = new float[2], aimPy = new float[2];
        readonly float[] shotX = new float[6], shotY = new float[6];
        int shotCount;
        readonly float[] fireX = new float[2], fireY = new float[2];
        readonly bool[] fireUsed = new bool[2];
        int fireCount;

        // through the glass: what is on its way, when each left, the verdicts that came (any thread) and those of this frame
        struct OutEvent { public int id; public string what; public float at; }
        readonly List<OutEvent> flying = new List<OutEvent>(MaxOutsFlying + 2);
        readonly List<float> outTimes = new List<float>(MaxOutsMinute + 2);
        readonly List<OutEvent> arrived = new List<OutEvent>(8), events = new List<OutEvent>(8);
        bool eventsRead;
        int nextOut = 1;

        // the UI kit: buttons of this frame, and where one was pressed a moment ago
        int buttons;
        readonly float[] pressX = new float[8], pressY = new float[8], pressT = new float[8];
        int pressHead;

        // ------------------------------------------------------------------ Start

        public override void Start(GamePackDef pack, int seed)
        {
            this.pack = pack;
            started = true;
            Error = ""; ErrorLine = 0; ErrorSource = "";
            if (!(W >= 0.5f && W <= 4f)) W = 16f / 9f;
            score = 0; level = 1; seconds = 0f; result = "";
            lives = pack != null ? (pack.lives < 0 ? 0 : pack.lives > 9 ? 9 : pack.lives) : 3;
            choices = new string[0]; choiceNotes = new string[0];
            sprites.Clear(); sounds.Clear(); soundAt.Clear(); frame.Clear(); kept.Clear();
            if (sprites.Capacity < MaxSprites + 2) sprites.Capacity = MaxSprites + 2;
            clock = 0f; introT = 0f; lastTick = MaxTick;
            petX = W * 0.5f; petY = 0f; petFacesRight = true; petPose = "idle"; petHurt = 0f;
            petLine = ""; petLineLeft = 0f; petCustom = false;
            if (petCustomPose == null) petCustomPose = new Pose();
            poseWanted = "idle"; poseMotion = null; playing = null; poseAfter = null; playT = 0f; poseBackT = 0f; going = false;
            fog = null; fogCols = 0; fogRows = 0; boardCols = 0; boardRows = 0;
            handRadius = GlassGames.HandRadiusBase;
            flat = false;
            for (int i = 0; i < 2; i++) { hand[i].present = false; hand[i].down = false; hand[i].count = 0; hand[i].restT = 0f; hand[i].fired = false; hand[i].seen = false; }
            tapCount = 0; shotCount = 0; fireCount = 0; aimOk[0] = aimOk[1] = false;
            outs.Clear(); flying.Clear(); outTimes.Clear(); events.Clear(); eventsRead = false; nextOut = 1;
            lock (arrived) arrived.Clear();
            buttons = 0; pressHead = 0;
            for (int i = 0; i < pressT.Length; i++) pressT[i] = -100f;
            UsedOuts = ReadOutEvents = ReadAim = ReadShots = UsedKit = false; OutsSent = 0; OutsRefused = 0;
            ReadHands = ReadTaps = ReadHeld = DrewEver = false;
            Updates = 0; MaxDrawn = 0; Dropped = 0; Drawn = 0; DrawnOutside = 0; SmallestText = float.MaxValue;
            StartInstructions = 0; PeakUpdate = 0; UpdateInstructions = 0; FinishedAt = -1f;
            phase = "intro";
            banner = pack != null && !string.IsNullOrEmpty(pack.howTo) ? Cut(pack.howTo, 80) : "";
            bannerT = 0f;

            try
            {
                box = new LuaSandbox(seed);
                if (Precise) box.SliceSize = 100;
                motions = new LuaMotion(box);
                DefineApi();
                box.Set("W", W);
                box.Set("H", 1.0);
                canSetFlat = true;
                asked = 0; labels = 0;
                bool ok = box.Load(pack != null ? pack.script : "");
                StartInstructions = box.LastInstructions;
                if (ok && !box.Has("update")) box.Fail("the script has no function update(dt)");
                if (ok && !box.Broken && box.Has("start"))
                {
                    box.Call("start");
                    StartInstructions += box.LastInstructions;
                }
                canSetFlat = false;
                frame.Clear(); labels = 0; asked = 0;       // (what the top level or start drew is not a frame)
                // where the partner stands when it begins: where the script sent it before the first frame (there is no walk to show yet), otherwise the middle
                if (flat) petY = 0.5f;
                if (going) { petX = goX; petY = flat ? goY : 0f; going = false; }
                if (box.Broken) Break();
            }
            catch (Exception e) { BreakWith("it could not be started: " + e.GetType().Name + " " + e.Message); }
        }

        void Break()
        {
            Error = box != null ? box.ErrorText() : "it broke";
            ErrorLine = box != null ? box.ErrorLine : 0;
            ErrorSource = box != null ? box.ErrorSource : "";
            if (Error.Length == 0) Error = "it broke";
            Over(BrokeLine);
        }

        void BreakWith(string message)
        {
            if (box != null && !box.Broken) box.Fail(message);
            if (box != null) Break();
            else { Error = message; Over(BrokeLine); }
        }

        void Over(string line)
        {
            phase = "over";
            result = line ?? "";
            banner = ""; bannerT = 0f;
            if (Error.Length > 0) { playing = null; poseMotion = null; petCustom = false; going = false; petPose = "down"; }
            if (FinishedAt < 0f) FinishedAt = seconds;
        }

        // ------------------------------------------------------------------ Step

        public override void Step(float dt, GlassInput input)
        {
            if (!started) Start(null, 0);
            sounds.Clear();
            if (input == null) input = NoHands;
            if (!(dt > 0f)) dt = 0f; else if (dt > 0.25f) dt = 0.25f;
            if (box == null || box.Broken) { if (Error.Length == 0) Break(); return; }       // the last picture stays
            int n = (int)Math.Ceiling(dt / MaxTick - 0.001f);
            if (n < 1) n = 1;
            if (n > 2) n = 2;      // (at most two updates in one frame: after a hitch the script is not run many times over in the frame that follows it)
            float h = dt / n;
            try
            {
                for (int i = 0; i < n && Error.Length == 0; i++) Tick(h, input, i == 0);
            }
            catch (Exception e) { BreakWith("it broke inside: " + e.GetType().Name + " " + e.Message); }
        }

        /// <summary>What became of something that was sent out (blocked | hit | missed; anything else counts as missed). May be called from any thread; the script gets it in its next frame.</summary>
        public override void OutResult(int id, string what)
        {
            OutEvent e = new OutEvent();
            e.id = id;
            string w = (what ?? "").Trim().ToLowerInvariant();
            e.what = w == "blocked" || w == "hit" ? w : "missed";
            lock (arrived) { if (arrived.Count < MaxOutEvents) arrived.Add(e); }
        }

        // The verdicts for this frame: those that came since the last one (only for what is really on its way, each once),
        // and "missed" for what nobody answered. They stay until a frame has read them with out_events().
        void ReadOuts()
        {
            if (eventsRead) { events.Clear(); eventsRead = false; }
            lock (arrived)
            {
                for (int i = 0; i < arrived.Count; i++)
                {
                    int k = -1;
                    for (int j = 0; j < flying.Count; j++) if (flying[j].id == arrived[i].id) { k = j; break; }
                    if (k < 0) continue;
                    flying.RemoveAt(k);
                    if (events.Count < MaxOutEvents) events.Add(arrived[i]);
                }
                arrived.Clear();
            }
            for (int j = 0; j < flying.Count; j++)
            {
                if (clock - flying[j].at <= OutTimeout) continue;
                OutEvent e = flying[j]; e.what = "missed";
                flying.RemoveAt(j--);
                if (events.Count < MaxOutEvents) events.Add(e);
            }
        }

        void Tick(float h, GlassInput input, bool first)
        {
            clock += h; lastTick = h > 0f ? h : lastTick;
            if (petHurt > 0f) petHurt = Math.Max(0f, petHurt - h);
            if (bannerT > 0f) { bannerT -= h; if (bannerT <= 0f) banner = ""; }
            if (petLineLeft > 0f) { petLineLeft -= h; if (petLineLeft <= 0f) { petLineLeft = 0f; petLine = ""; } }
            bool intro = phase == "intro", over = phase == "over";
            if (intro)
            {
                introT += h;
                if (introT >= IntroSeconds) { phase = "play"; intro = false; if (bannerT <= 0f) banner = ""; }
            }
            ReadInput(h, intro || over ? NoHands : input, first);
            ReadOuts();
            if (phase == "play") seconds += h;

            frame.Clear(); labels = 0; asked = 0; buttons = 0; finishedThisTick = false;
            one[0] = DynValue.NewNumber(intro ? 0.0 : h);
            bool ok = box.CallWithin(box.Budget, box.Get("update"), one);
            Updates++;
            UpdateInstructions += box.LastInstructions;
            if (box.LastInstructions > PeakUpdate) PeakUpdate = box.LastInstructions;
            if (!ok) { if (!box.Broken) box.Fail("update is not a function any more"); Break(); return; }
            if (asked > MaxDrawn) MaxDrawn = asked;

            MovePet(h);
            if (Error.Length > 0) return;

            // what is shown: this frame - but a finished game that stops drawing keeps its last full picture
            bool isOver = phase == "over";
            if (finishedThisTick)
            {
                // (finish() is often called in the middle of update, before everything was drawn)
                if (frame.Count < sprites.Count) { kept.Clear(); kept.AddRange(sprites); }
                else { kept.Clear(); kept.AddRange(frame); }
            }
            sprites.Clear();
            if (isOver && frame.Count == 0 || finishedThisTick && frame.Count < kept.Count) sprites.AddRange(kept);
            else sprites.AddRange(frame);
            if (frame.Count > 0) DrewEver = true;
            if (!isOver && !intro) DwellRings();
        }

        // ------------------------------------------------------------------ the hands

        void ReadInput(float h, GlassInput input, bool first)
        {
            tapCount = 0; shotCount = 0; fireCount = 0;
            for (int i = 0; i < tapUsed.Length; i++) tapUsed[i] = false;
            for (int i = 0; i < 2; i++)
            {
                // where it points (it need not be near), and its trigger - once in a Step, however many pieces the Step is cut into
                float ax = input.aimX[i], ay = input.aimY[i];
                aimOk[i] = input.aimValid[i] && ax > -1f && ax < W + 1f && ay > -1f && ay < 2f;      // (false for NaN)
                aimPx[i] = ax; aimPy[i] = ay;
                if (aimOk[i] && input.fire[i] && first)
                {
                    fireX[fireCount] = ax; fireY[fireCount] = ay; fireUsed[fireCount] = false; fireCount++;
                    Shot(ax, ay);
                }

                Hand s = hand[i];
                // seen at all - near the field or not: where it is, what it shows, how fast it moves
                float vx = input.x[i], vy = input.y[i], sp = input.speed[i];
                s.seen = (input.seen[i] || input.valid[i]) && vx > -100f && vx < 100f && vy > -100f && vy < 100f;      // (false for NaN)
                s.sx = vx; s.sy = vy; s.fingers = s.seen ? input.fingers[i] : -1;
                s.speed = s.seen && sp > 0f && sp < 50f ? sp : 0f;
                bool listed = GlassGames.Acts(input, i);
                if (!listed)
                {
                    s.present = false; s.down = false; s.count = 0; s.restT = 0f; s.fired = false;
                    continue;
                }
                float x = input.x[i], y = input.y[i], d = input.dist[i];
                bool was = s.present, wasDown = s.down;
                s.present = true; s.x = x; s.y = y; s.dist = d < 0f ? 0f : d;
                s.reach = GlassGames.HandRadius(this, input, i);
                if (touch)
                {
                    s.down = wasDown && was ? d < TouchUp : d < TouchDown;
                    if (s.down && !(wasDown && was)) Tap(x, y, i);
                    s.restT = 0f; s.fired = false;
                }
                else
                {
                    s.down = true;
                    float dx = x - s.ax, dy = y - s.ay;
                    if (!was || dx * dx + dy * dy > RestRadius * RestRadius) { s.ax = x; s.ay = y; s.restT = 0f; s.fired = false; }
                    else
                    {
                        s.restT += h;
                        if (s.restT >= RestSeconds && !s.fired) { s.fired = true; Tap(s.ax, s.ay, i); }
                    }
                }
                if (s.down)
                {
                    s.hx[s.head] = x; s.hy[s.head] = y; s.ht[s.head] = clock;
                    s.head = (s.head + 1) % Hist;
                    if (s.count < Hist) s.count++;
                }
                else s.count = 0;
            }
        }

        void Tap(float x, float y, int who)
        {
            if (tapCount < tapX.Length) { tapX[tapCount] = x; tapY[tapCount] = y; tapCount++; }
            if (!aimOk[who]) Shot(x, y);            // (a hand that points fires with its trigger, not by resting)
        }

        void Shot(float x, float y)
        {
            if (shotCount < shotX.Length) { shotX[shotCount] = x; shotY[shotCount] = y; shotCount++; }
        }

        float Held(float x, float y, float r)
        {
            float best = 0f;
            for (int i = 0; i < 2; i++)
            {
                Hand s = hand[i];
                if (!s.present || !s.down || s.count == 0) continue;
                float oldest = -1f;
                for (int k = 1; k <= s.count; k++)
                {
                    int j = (s.head - k + Hist * 2) % Hist;
                    float dx = s.hx[j] - x, dy = s.hy[j] - y;
                    if (dx * dx + dy * dy > r * r) break;
                    oldest = s.ht[j];
                }
                if (oldest < 0f) continue;
                float t = clock - oldest + lastTick;
                if (t > best) best = t;
            }
            return best;
        }

        // A hand that rests (and the script uses taps): a ring closes on the spot, so that the tap is seen coming.
        void DwellRings()
        {
            if (touch || !(ReadTaps || ReadShots)) return;
            for (int i = 0; i < 2; i++)
            {
                Hand s = hand[i];
                if (!s.present || s.fired || s.restT < 0.12f || sprites.Count >= MaxSprites) continue;
                float k = s.restT / RestSeconds;
                if (k > 1f) k = 1f;
                GlassSprite g = new GlassSprite();
                g.id = DwellId + i; g.art = GlassArt.RingShape; g.x = s.ax; g.y = s.ay;
                g.size = 0.10f - 0.05f * k; g.w = g.size; g.alpha = 0.35f + 0.5f * k; g.color = LuaSandbox.Ink;
                g.text = ""; g.text2 = ""; g.fill = k;
                sprites.Add(g);
            }
        }

        // ------------------------------------------------------------------ the partner

        void MovePet(float h)
        {
            bool walking = false;
            if (going)
            {
                float dx = goX - petX, dy = flat ? goY - petY : 0f;
                float d = (float)Math.Sqrt(dx * dx + dy * dy), step = goSpeed * h;
                if (Math.Abs(dx) > 0.002f) petFacesRight = dx > 0f;
                if (d <= step || d < 0.004f) { petX = goX; if (flat) petY = goY; going = false; }
                else { petX += dx / d * step; petY += dy / d * step; walking = h > 0f; }
            }
            if (!flat) petY = 0f;

            // a built-in pose that was played for a moment (pet_play("cheer")) goes back
            if (poseBackT > 0f)
            {
                poseBackT -= h;
                if (poseBackT <= 0f && poseAfter != null) { poseWanted = poseAfter; poseAfter = null; }
            }

            // which motion of the script is on: one that was played (once) comes before walking, a held one after it
            string motion = null;
            if (playing != null)
            {
                playT += h;
                float secs = motions.Seconds(playing);
                if (secs <= 0f) playing = null;
                else if (!playLoop && !playHold && playT >= secs)                       // played once: over - back to the pose
                {
                    playing = poseMotion; playT = 0f;
                    if (playing != null) { playLoop = motions.Loops(playing); playHold = !playLoop; motion = playing; }
                }
                else motion = playing;
            }
            if (motion != null && walking && (playLoop || playHold)) motion = null;     // a held motion gives way to walking
            petPose = walking ? (goSpeed > 0.95f ? "run" : "walk") : poseWanted;
            if (motion != null)
            {
                float secs = motions.Seconds(motion), u = playT / secs;
                if (!motions.Evaluate(motion, u, clock, petCustomPose))
                {
                    petCustom = false;
                    if (box.Broken) Break();
                    return;
                }
                petCustom = true;
            }
            else petCustom = false;
        }

        // ------------------------------------------------------------------ the API

        static string Cut(string s, int max) { return s == null ? "" : s.Length > max ? s.Substring(0, max) : s; }

        // A coordinate / a size the script gave. In Strict a missing or broken one is an error at that line; otherwise the thing is left out.
        bool Get(LuaArgs a, int i, string what, out float v)
        {
            if (a.TryNum(i, out v))
            {
                if (v > 100f) v = 100f; else if (v < -100f) v = -100f;
                return true;
            }
            if (Strict) a.Need(i, what);
            box.Note(a.Function + ": " + what + " was not a number (that one was not drawn)");
            return false;
        }

        float Alpha(LuaArgs a, int i)
        {
            float al = a.Num(i, 1f);
            return al < 0f ? 0f : al > 1f ? 1f : al;
        }

        void Put(string art, float x, float y, float size, float w, int color, float alpha, float rot, int frameNo, string text, string text2, float fill, bool label)
        {
            asked++;
            if (!(alpha > 0.015f) || !(size > 0f)) return;
            if (size > 4f) size = 4f;
            if (w > 4f) w = 4f;
            Drawn++;
            if (x < -0.25f || x > W + 0.25f || y < -0.25f || y > 1.25f) DrawnOutside++;
            if (frame.Count >= MaxSprites || (label && labels >= MaxLabels)) { Dropped++; return; }
            if (label) labels++;
            GlassSprite s = new GlassSprite();
            s.id = FirstId + frame.Count; s.art = art; s.x = x; s.y = y; s.size = size; s.w = w; s.color = color;
            s.alpha = alpha; s.rot = rot; s.frame = frameNo; s.text = text ?? ""; s.text2 = text2 ?? ""; s.fill = fill;
            frame.Add(s);
        }

        static float Turn(float deg)
        {
            if (!(deg > -1e6f && deg < 1e6f)) return 0f;
            deg %= 360f;
            return deg;
        }

        static string Bare(string name)
        {
            string n = (name ?? "").Trim();
            return n.StartsWith(GlassArt.PackPrefix, StringComparison.Ordinal) ? n.Substring(GlassArt.PackPrefix.Length) : n;
        }

        // One of the pack's own pictures or a sticker, by its exact name.
        string ExactArt(string n)
        {
            string low = n.ToLowerInvariant();
            if (pack != null && pack.art != null)
                for (int i = 0; i < pack.art.Count; i++)
                    if (pack.art[i] != null && string.Equals(pack.art[i].name, n, StringComparison.OrdinalIgnoreCase)) return GlassArt.PackPrefix + pack.art[i].name;
            for (int i = 0; i < Stickers.Length; i++) if (Stickers[i] == low) return Stickers[i];
            return null;
        }

        // The other words a writer may use for a sticker.
        static string AliasArt(string n)
        {
            switch (n.ToLowerInvariant())
            {
                case "coin": return GlassArt.Gold;
                case "stone": case "stone_a": case "stonea": case "black": return GlassArt.StoneA;
                case "stone_b": case "stoneb": case "white": return GlassArt.StoneB;
                case "ghost": case "monster": case "bat": case "clone": return GlassArt.Enemy;
                case "spark": return GlassArt.Sparkle;
                case "smoke": case "cloud": return GlassArt.Puff;
                case "ball": case "moon": return GlassArt.Orb;
                case "football": case "soccer": case "soccerball": return "football";
                case "raindrop": case "ink": return GlassArt.Drop;
                case "diamond": case "jewel": return GlassArt.Gem;
                case "lightning": return GlassArt.Bolt;
                case "bullet": case "laser": case "arrow": return GlassArt.Shot;
                case "paper": case "paper_sign": case "papersign": case "open": return GlassArt.PaperSign;      // (what hands() calls the shape: pic(h.shape, ...) draws it)
                case "fist": return GlassArt.Rock;
            }
            return null;
        }

        string ArtOf(string name)
        {
            string n = Bare(name);
            string art = ExactArt(n) ?? AliasArt(n);
            if (art != null) return art;
            box.Note("pic: there is no picture '" + Cut(n, 24) + "' (a star was drawn; the stickers are: " + string.Join(" ", Stickers) + ")");
            return GlassArt.Star;
        }

        // ------------------------------------------------------------------ the UI kit

        static int Mix(int a, int b, float k)
        {
            int r = (int)(((a >> 16) & 255) + (((b >> 16) & 255) - ((a >> 16) & 255)) * k);
            int g = (int)(((a >> 8) & 255) + (((b >> 8) & 255) - ((a >> 8) & 255)) * k);
            int bl = (int)((a & 255) + ((b & 255) - (a & 255)) * k);
            return (r << 16) | (g << 8) | bl;
        }

        static bool Dark(int c) { return ((c >> 16) & 255) * 0.30f + ((c >> 8) & 255) * 0.59f + (c & 255) * 0.11f < 120f; }

        // A box of paper: w x h around x, y with corners of radius r (field units).
        void Paper(float x, float y, float w, float h, float r, int color, float alpha)
        {
            if (!(w > 0.003f) || !(h > 0.003f)) return;
            float m = w < h ? w : h, f = r > 0f ? r / m : 0f;
            Put(Rounded ? GlassArt.Paper : GlassArt.Rect, x, y, h, w, color, alpha, 0f, 0, "", "", f > 0.5f ? 0.5f : f, false);
        }

        // An outlined box: ink under the colour.
        void Box2(float x, float y, float w, float h, float r, int color)
        {
            Paper(x, y, w, h, r, LuaSandbox.Ink, 1f);
            Paper(x, y, w - 2f * KitLine, h - 2f * KitLine, r - KitLine, color, 1f);
        }

        // Words in a box maxW x maxH: as large as fits. align -1 left, 0 centre, 1 right (x is then that end of the box).
        void Words(string s, float x, float y, float maxW, float maxH, int align, int color)
        {
            if (string.IsNullOrEmpty(s) || !(maxW > 0f) || !(maxH > 0f)) return;
            float size = maxH, wide = s.Length * GlyphWidth;
            if (size * wide > maxW) size = maxW / wide;
            if (size < 0.028f && maxH >= 0.028f)
            {
                // it would be too small to read: the letters stay readable and the words are cut (a number keeps its end)
                size = 0.028f;
                int n = Math.Max(1, (int)(maxW / (size * GlyphWidth)));
                if (n < s.Length) s = align > 0 ? s.Substring(s.Length - n) : s.Substring(0, n);
            }
            if (size < SmallestText) SmallestText = size;
            float half = s.Length * GlyphWidth * size * 0.5f;
            Put(GlassArt.Label, align < 0 ? x + half : align > 0 ? x - half : x, y, size, 0f, color, 1f, 0f, 0, s, "", 0f, true);
        }

        bool InBox(float px, float py, float x, float y, float w, float h)
        {
            return Math.Abs(px - x) <= w * 0.5f + 0.006f && Math.Abs(py - y) <= h * 0.5f + 0.006f;
        }

        // A hand is over the box, or points at it.
        bool Hover(float x, float y, float w, float h)
        {
            for (int i = 0; i < 2; i++)
                if (hand[i].present && InBox(hand[i].x, hand[i].y, x, y, w, h) || aimOk[i] && InBox(aimPx[i], aimPy[i], x, y, w, h)) return true;
            return false;
        }

        bool HeldDown(float x, float y, float w, float h)
        {
            if (Shown(x, y)) return true;
            for (int i = 0; touch && i < 2; i++) if (hand[i].present && hand[i].down && InBox(hand[i].x, hand[i].y, x, y, w, h)) return true;
            return false;
        }

        // It was pressed a moment ago (it is drawn pushed in).
        bool Shown(float x, float y)
        {
            for (int i = 0; i < pressT.Length; i++)
                if (clock - pressT[i] < PressShown && Math.Abs(pressX[i] - x) < 0.002f && Math.Abs(pressY[i] - y) < 0.002f) return true;
            return false;
        }

        // A tap (or a shot that was fired while pointing) landed in the box: each presses one thing only, and a thing not twice within PressAgain.
        bool Pressed(float x, float y, float w, float h)
        {
            ReadTaps = true; UsedKit = true;
            bool hit = false;
            for (int i = 0; i < tapCount; i++) if (!tapUsed[i] && InBox(tapX[i], tapY[i], x, y, w, h)) { tapUsed[i] = true; hit = true; }
            for (int i = 0; i < fireCount; i++) if (!fireUsed[i] && InBox(fireX[i], fireY[i], x, y, w, h)) { fireUsed[i] = true; hit = true; }
            if (!hit) return false;
            for (int i = 0; i < pressT.Length; i++)
                if (clock - pressT[i] < PressAgain && Math.Abs(pressX[i] - x) < 0.002f && Math.Abs(pressY[i] - y) < 0.002f) return false;
            pressX[pressHead] = x; pressY[pressHead] = y; pressT[pressHead] = clock;
            pressHead = (pressHead + 1) % pressT.Length;
            Sound("tap");
            return true;
        }

        void Sound(string name)
        {
            float last;
            if (soundAt.TryGetValue(name, out last) && clock - last < 0.1f) return;
            if (sounds.Count >= 6) return;
            soundAt[name] = clock;
            sounds.Add(name);
        }

        // The label and the four numbers of button / display / panel / toggle: the label comes first, but it is also found at the end.
        bool Labelled(LuaArgs a, int numbers, out string label, out float x, out float y, out float w, out float h, out int next)
        {
            int at = 1;
            label = ""; w = 0f; h = 0f; next = numbers + 1;
            bool last = a.Raw(0).Type == DataType.Number && a.Raw(numbers).Type == DataType.String;
            if (last) { at = 0; label = a.Str(numbers, "", LuaSandbox.MaxText); }
            else label = a.Str(0, "", LuaSandbox.MaxText);
            bool ok = Get(a, at, "x", out x) & Get(a, at + 1, "y", out y);
            if (numbers > 2) ok = ok & Get(a, at + 2, "w", out w) & Get(a, at + 3, "h", out h);
            return ok;
        }

        void DefineKit()
        {
            box.Def("panel", a =>
            {
                // panel(x, y, w, h [, title [, color]])  -  the title may also come first
                UsedKit = true;
                float x, y, w, h;
                string title;
                int next = 5;
                if (a.Raw(0).Type == DataType.String) { if (!Labelled(a, 4, out title, out x, out y, out w, out h, out next)) return null; }
                else
                {
                    if (!Get(a, 0, "x", out x) | !Get(a, 1, "y", out y) | !Get(a, 2, "w", out w) | !Get(a, 3, "h", out h)) return null;
                    title = a.Str(4, "", LuaSandbox.MaxText);
                }
                if (!(w > 0.02f) || !(h > 0.02f)) return null;
                float r = Math.Min(0.045f, Math.Min(w, h) * 0.25f);
                Paper(x + 0.007f, y - 0.009f, w, h, r, LuaSandbox.Ink, 0.18f);         // a soft shadow
                Box2(x, y, w, h, r, a.Color(next, Paper0));
                if (title.Length > 0)
                {
                    float th = Math.Max(0.06f, Math.Min(0.09f, h * 0.14f)), tw = w - 2f * KitLine - 0.024f;
                    float ty = y + h * 0.5f - KitLine - 0.012f - th * 0.5f;
                    Paper(x, ty, tw, th, th * 0.5f, Strip0, 1f);
                    Words(title, x, ty, tw - th, th * 0.6f, 0, LuaSandbox.Ink);
                }
                return null;
            });
            box.Def("button", a =>
            {
                UsedKit = true;
                string label; float x, y, w, h; int next;
                if (!Labelled(a, 4, out label, out x, out y, out w, out h, out next)) return DynValue.False;
                if (!(w > 0.02f) || !(h > 0.02f)) return DynValue.False;
                if (++buttons > MaxButtons) { box.Note("button: more than " + MaxButtons + " buttons in one frame (the rest was left out)"); return DynValue.False; }
                bool pressed = phase == "play" && Pressed(x, y, w, h);
                bool down = pressed || HeldDown(x, y, w, h), hover = !down && Hover(x, y, w, h);
                int color = a.Color(next, Key0);
                float grow = hover ? 0.008f : 0f, bw = w + grow, bh = h + grow;
                float lip = Math.Min(0.016f, bh * 0.16f), r = Math.Min(0.03f, Math.Min(bw, bh) * 0.3f);
                float fw = bw - 2f * KitLine, fh = bh - 2f * KitLine - lip, fy = y + (down ? -lip : lip) * 0.5f;
                Paper(x, y, bw, bh, r, LuaSandbox.Ink, 1f);                              // the outline, and the edge the key stands on
                Paper(x, fy, fw, fh, r - KitLine, down ? Mix(color, LuaSandbox.Ink, 0.12f) : hover ? Mix(color, 0xFFFFFF, 0.45f) : color, 1f);
                Words(label, x, fy, fw - 0.02f, Math.Min(fh * 0.56f, 0.09f), 0, Dark(color) ? LuaSandbox.Cream : LuaSandbox.Ink);
                return DynValue.NewBoolean(pressed);
            });
            box.Def("display", a =>
            {
                UsedKit = true;
                string s; float x, y, w, h; int next;
                if (!Labelled(a, 4, out s, out x, out y, out w, out h, out next)) return null;
                if (!(w > 0.02f) || !(h > 0.02f)) return null;
                float r = Math.Min(0.025f, Math.Min(w, h) * 0.25f), pad = Math.Max(0.02f, h * 0.2f);
                Box2(x, y, w, h, r, 0xFFFFFF);
                Paper(x, y + h * 0.5f - KitLine - 0.005f, w - 2f * KitLine - 2f * r, 0.01f, 0.005f, LuaSandbox.Ink, 0.10f);     // sunk in: a shade under its upper edge
                string how = a.Str(next, "", 12).Trim().ToLowerInvariant();
                float number;
                int align = how == "left" ? -1 : how == "center" || how == "centre" || how == "middle" ? 0 : how == "right" ? 1
                    : float.TryParse(s.Replace(",", "").Replace(" ", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) ? 1 : 0;
                Words(s, align < 0 ? x - w * 0.5f + pad : align > 0 ? x + w * 0.5f - pad : x, y, w - 2f * pad, h * 0.6f, align, LuaSandbox.Ink);
                return null;
            });
            box.Def("toggle", a =>
            {
                // toggle(label, x, y, on) -> on, turned over when it was tapped
                string label; float x, y, w, h; int next;
                if (!Labelled(a, 2, out label, out x, out y, out w, out h, out next)) return DynValue.NewBoolean(a.Bool(3, false));
                bool on = a.Bool(next, false);
                const float TW = 0.13f, TH = 0.07f, LS = 0.045f;
                float lw = label.Length * GlyphWidth * LS, all = TW + (label.Length > 0 ? 0.02f + lw : 0f);
                if (++buttons > MaxButtons) { box.Note("button: more than " + MaxButtons + " buttons in one frame (the rest was left out)"); return DynValue.NewBoolean(on); }
                if (phase == "play" && Pressed(x + (all - TW) * 0.5f, y, all, TH)) on = !on;
                Box2(x, y, TW, TH, TH * 0.5f, on ? ToggleOn : ToggleOff);
                float kd = TH - 2f * KitLine - 0.006f;
                Box2(x + (on ? 1f : -1f) * (TW - TH) * 0.5f, y, kd, kd, kd * 0.5f, Key0);
                if (label.Length > 0) Words(label, x + TW * 0.5f + 0.02f, y, lw + 0.001f, LS, -1, LuaSandbox.Ink);
                return DynValue.NewBoolean(on);
            });
        }

        // ------------------------------------------------------------------ through the glass, and aiming

        void DefineOuts()
        {
            box.Def("inside", a => DynValue.NewBoolean(inside));
            box.Def("shoot_out", a =>
            {
                // shoot_out(x, y [, pic_or_color [, speed [, size]]]) -> id (0 = it was not sent)
                UsedOuts = true;
                float x, y;
                if (!Get(a, 0, "x", out x) | !Get(a, 1, "y", out y)) return DynValue.NewNumber(0);
                if (phase != "play") return DynValue.NewNumber(0);          // (nothing moves before the game begins or after it has ended)
                while (outTimes.Count > 0 && clock - outTimes[0] > 60f) outTimes.RemoveAt(0);
                if (flying.Count >= MaxOutsFlying || outTimes.Count >= MaxOutsMinute)
                {
                    OutsRefused++;
                    box.Note("shoot_out: at most " + MaxOutsFlying + " things can be on their way at once and " + MaxOutsMinute + " in a minute (the rest was not sent)");
                    return DynValue.NewNumber(0);
                }
                GlassOut o = new GlassOut();
                o.id = nextOut++;
                o.x = x < 0f ? 0f : x > W ? W : x; o.y = y < 0f ? 0f : y > 1f ? 1f : y;
                o.art = GlassArt.Orb; o.color = -1;
                DynValue v = a.Raw(2);
                if (v.Type != DataType.Nil)
                {
                    string n = v.Type == DataType.String ? Bare(v.String) : "";
                    string art = n.Length > 0 ? ExactArt(n) : null;
                    int rgb = 0;
                    if (art != null) o.art = art;
                    else if (LuaSandbox.TryColor(v, ref rgb)) { o.art = GlassArt.Disc; o.color = rgb; }
                    else if (n.Length > 0 && (art = AliasArt(n)) != null) o.art = art;
                    else box.Note("shoot_out: '" + Cut(LuaSandbox.Text(v), 24) + "' is neither a picture nor a colour (an orb was sent)");
                }
                o.speed = a.Num(3, OutSpeed, 0.3f, 4f);
                o.size = a.Num(4, OutSize, 0.02f, 0.3f);
                if (outs.Count >= 16) outs.RemoveAt(0);                     // (nobody takes them: an old renderer)
                outs.Add(o);
                OutEvent f = new OutEvent(); f.id = o.id; f.what = ""; f.at = clock;
                flying.Add(f); outTimes.Add(clock);
                OutsSent++;
                return DynValue.NewNumber(o.id);
            });
            box.Def("out_events", a =>
            {
                ReadOutEvents = true; eventsRead = true;
                Table list = box.NewTable();
                for (int i = 0; i < events.Count; i++)
                {
                    Table t = box.NewTable();
                    t.Set("id", DynValue.NewNumber(events[i].id)); t.Set("what", DynValue.NewString(events[i].what));
                    list.Append(DynValue.NewTable(t));
                }
                return DynValue.NewTable(list);
            });
            box.Def("aim", a =>
            {
                ReadAim = true;
                for (int i = 0; i < 2; i++) if (aimOk[i]) return DynValue.NewTuple(DynValue.NewNumber(aimPx[i]), DynValue.NewNumber(aimPy[i]));
                return DynValue.Nil;
            });
            box.Def("shots", a =>
            {
                ReadShots = true;
                Table list = box.NewTable();
                for (int i = 0; i < shotCount; i++)
                {
                    Table t = box.NewTable();
                    t.Set("x", DynValue.NewNumber(shotX[i])); t.Set("y", DynValue.NewNumber(shotY[i]));
                    list.Append(DynValue.NewTable(t));
                }
                return DynValue.NewTable(list);
            });
            box.Def("hit_at", a =>
            {
                // hit_at(px, py, x, y [, r]): the point px, py is within r of x, y. A point given as a table ({x=, y=}: a shot, a tap) works too.
                float px, py, x, y, r;
                int at = 2;
                if (a.IsTable(0)) { Table p = a.Tab(0); px = LuaArgs.Field(p, "x", 1, float.NaN); py = LuaArgs.Field(p, "y", 2, float.NaN); at = 1; }
                else { px = a.Num(0, float.NaN); py = a.Num(1, float.NaN); }
                x = a.Num(at, float.NaN); y = a.Num(at + 1, float.NaN); r = a.Num(at + 2, 0.08f, 0f, 10f);
                float dx = px - x, dy = py - y;
                return DynValue.NewBoolean(dx * dx + dy * dy <= r * r);         // (false for anything that is not a number)
            });
        }

        void DefineApi()
        {
            // ---- drawing
            box.Def("rect", a =>
            {
                float x, y, w, h;
                if (!Get(a, 0, "x", out x) | !Get(a, 1, "y", out y) | !Get(a, 2, "w", out w) | !Get(a, 3, "h", out h)) return null;
                Put(GlassArt.Rect, x, y, h, w, a.Color(4), Alpha(a, 5), Turn(a.Num(6, 0f)), 0, "", "", 0f, false);
                return null;
            });
            box.Def("disc", a =>
            {
                float x, y, r;
                if (!Get(a, 0, "x", out x) | !Get(a, 1, "y", out y) | !Get(a, 2, "r", out r)) return null;
                Put(GlassArt.Disc, x, y, r * 2f, r * 2f, a.Color(3), Alpha(a, 4), 0f, 0, "", "", 0f, false);
                return null;
            });
            box.Def("hoop", a =>
            {
                float x, y, r;
                if (!Get(a, 0, "x", out x) | !Get(a, 1, "y", out y) | !Get(a, 2, "r", out r)) return null;
                Put(GlassArt.RingShape, x, y, r * 2f, r * 2f, a.Color(3), Alpha(a, 4), 0f, 0, "", "", 1f, false);
                return null;
            });
            box.Def("line", a =>
            {
                float x1, y1, x2, y2;
                if (!Get(a, 0, "x1", out x1) | !Get(a, 1, "y1", out y1) | !Get(a, 2, "x2", out x2) | !Get(a, 3, "y2", out y2)) return null;
                float thick = a.Num(4, 0.006f, 0.001f, 0.5f);
                float dx = x2 - x1, dy = y2 - y1, len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.0005f) return null;
                Put(GlassArt.Rect, (x1 + x2) * 0.5f, (y1 + y2) * 0.5f, thick, len, a.Color(5), Alpha(a, 6), (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI), 0, "", "", 0f, false);
                return null;
            });
            box.Def("pic", a =>
            {
                float x, y;
                string name = a.Str(0, "", 40);
                if (!Get(a, 1, "x", out x) | !Get(a, 2, "y", out y)) return null;
                float size = a.Num(3, 0.1f);
                int frameNo = a.IsNumber(6) ? Math.Max(0, Math.Min(15, a.Int(6, 0))) : (a.Bool(6, false) ? 1 : 0);
                Put(ArtOf(name), x, y, size, 0f, -1, Alpha(a, 5), Turn(a.Num(4, 0f)), frameNo, "", "", 0f, false);
                return null;
            });
            box.Def("text", a =>
            {
                float x, y;
                string s = a.Str(0, "", LuaSandbox.MaxText);
                if (!Get(a, 1, "x", out x) | !Get(a, 2, "y", out y)) return null;
                float size = a.Num(3, 0.05f, 0.005f, 0.5f);
                if (s.Length == 0) return null;
                if (size < SmallestText) SmallestText = size;
                Put(GlassArt.Label, x, y, size, 0f, a.Color(4), Alpha(a, 5), 0f, 0, s, "", 0f, true);
                return null;
            });
            box.Def("card", a =>
            {
                float x, y;
                string title = a.Str(0, "", 24), note = a.Str(1, "", 40);
                if (!Get(a, 2, "x", out x) | !Get(a, 3, "y", out y)) return null;
                float size = a.Num(4, 0.34f, 0.05f, 1f), fill = a.Num(5, 0f, 0f, 1f);
                Put(GlassArt.Card, x, y, size, 0f, -1, Alpha(a, 6), 0f, 0, title, note, fill, true);
                return null;
            });

            // ---- the hands
            box.Def("hands", a =>
            {
                ReadHands = true;
                Table list = box.NewTable();
                for (int i = 0; i < 2; i++)
                {
                    Hand s = hand[i];
                    if (!s.present) continue;
                    Table t = box.NewTable();
                    t.Set("x", DynValue.NewNumber(s.x)); t.Set("y", DynValue.NewNumber(s.y));
                    t.Set("down", DynValue.NewBoolean(s.down)); t.Set("id", DynValue.NewNumber(i + 1));
                    t.Set("r", DynValue.NewNumber(s.reach));
                    t.Set("shape", DynValue.NewString(GlassGames.Shape(s.fingers))); t.Set("speed", DynValue.NewNumber(s.speed));
                    list.Append(DynValue.NewTable(t));
                }
                return DynValue.NewTable(list);
            });
            box.Def("all_hands", a =>
            {
                // every hand that is seen, also one that is too far from the field to act
                ReadHands = true;
                Table list = box.NewTable();
                for (int i = 0; i < 2; i++)
                {
                    Hand s = hand[i];
                    if (!s.seen) continue;
                    Table t = box.NewTable();
                    t.Set("x", DynValue.NewNumber(s.sx)); t.Set("y", DynValue.NewNumber(s.sy));
                    t.Set("id", DynValue.NewNumber(i + 1)); t.Set("near", DynValue.NewBoolean(s.present));
                    t.Set("shape", DynValue.NewString(GlassGames.Shape(s.fingers))); t.Set("speed", DynValue.NewNumber(s.speed));
                    list.Append(DynValue.NewTable(t));
                }
                return DynValue.NewTable(list);
            });
            box.Def("taps", a =>
            {
                ReadTaps = true;
                Table list = box.NewTable();
                for (int i = 0; i < tapCount; i++)
                {
                    Table t = box.NewTable();
                    t.Set("x", DynValue.NewNumber(tapX[i])); t.Set("y", DynValue.NewNumber(tapY[i]));
                    list.Append(DynValue.NewTable(t));
                }
                return DynValue.NewTable(list);
            });
            box.Def("held", a =>
            {
                ReadHeld = true;
                float x, y;
                if (!Get(a, 0, "x", out x) | !Get(a, 1, "y", out y)) return DynValue.NewNumber(0);
                return DynValue.NewNumber(Held(x, y, a.Num(2, 0.06f, 0.005f, 2f)));
            });
            box.Def("reach", a =>
            {
                if (a.Has(0)) handRadius = a.Num(0, GlassGames.HandRadiusBase, 0.03f, 0.3f);
                return DynValue.NewNumber(handRadius);
            });

            // ---- the partner
            box.Def("pet_goto", a =>
            {
                float x;
                if (!Get(a, 0, "x", out x)) return null;
                goX = x < 0.06f ? 0.06f : x > W - 0.06f ? W - 0.06f : x;
                float y = a.Num(1, canSetFlat ? 0.5f : petY);
                goY = flat || canSetFlat ? (y < 0.02f ? 0.02f : y > 0.98f ? 0.98f : y) : 0f;
                goSpeed = a.Num(2, PetSpeed, 0.02f, 4f);
                going = true;
                return null;
            });
            box.Def("pet_at", a => DynValue.NewTuple(DynValue.NewNumber(petX), DynValue.NewNumber(petY)));
            box.Def("pet_pose", a =>
            {
                string name = a.Str(0, "idle", 24).Trim().ToLowerInvariant();
                if (motions.Has(name))
                {
                    if (playing != name || !(playLoop || playHold)) { playing = name; playT = 0f; }
                    playLoop = motions.Loops(name); playHold = !playLoop;
                    poseMotion = name;
                    return null;
                }
                if (playing != null && (playLoop || playHold)) playing = null;      // a held motion ends; one that is being played once goes on
                poseMotion = null; poseAfter = null; poseBackT = 0f;
                poseWanted = PoseOf(name, "pet_pose");
                return null;
            });
            box.Def("pet_play", a =>
            {
                string name = a.Str(0, "", 24).Trim().ToLowerInvariant();
                if (motions.Has(name))
                {
                    playing = name; playT = 0f;
                    playLoop = a.Has(1) ? a.Bool(1, false) : motions.Loops(name);
                    playHold = false;
                    return null;
                }
                string p = PoseOf(name, "pet_play");
                if (poseAfter == null) poseAfter = poseWanted;
                poseWanted = p; poseBackT = a.Num(1, 1.2f, 0.1f, 10f);
                return null;
            });
            box.Def("pet_say", a =>
            {
                petLine = a.Str(0, "", LuaSandbox.MaxText);
                petLineLeft = petLine.Length > 0 ? a.Num(1, 2.5f, 0.3f, 10f) : 0f;
                return null;
            });
            box.Def("pet_hurt", a => { petHurt = a.Num(0, 0.8f, 0f, 5f); return null; });
            box.Def("pet_face", a =>
            {
                float x;
                if (a.TryNum(0, out x)) petFacesRight = x >= petX;
                return null;
            });

            // ---- the game
            box.Def("score", a => { if (a.Has(0)) score = a.Int(0, score) < -999999 ? -999999 : Math.Min(999999, a.Int(0, score)); return DynValue.NewNumber(score); });
            box.Def("add", a => { long s = (long)score + a.Int(0, 1); score = s > 999999 ? 999999 : s < -999999 ? -999999 : (int)s; return DynValue.NewNumber(score); });
            box.Def("lives", a => { if (a.Has(0)) lives = Math.Max(0, Math.Min(9, a.Int(0, lives))); return DynValue.NewNumber(lives); });
            box.Def("level", a => { if (a.Has(0)) level = Math.Max(1, Math.Min(999, a.Int(0, level))); return DynValue.NewNumber(level); });
            box.Def("banner", a =>
            {
                banner = a.Str(0, "", LuaSandbox.MaxText);
                bannerT = a.Num(1, 2f, 0f, 60f);
                return null;
            });
            box.Def("finish", a =>
            {
                if (phase == "over") return null;
                string line = a.Str(0, "", 80);
                Over(line.Length > 0 ? line : "The end.");
                finishedThisTick = true;
                return null;
            });
            box.Def("time", a => DynValue.NewNumber(seconds));
            box.Def("sound", a =>
            {
                string name = a.Str(0, "", 24).Trim().ToLowerInvariant();
                if (Array.IndexOf(SfxSynth.Names, name) < 0) { box.Note("sound: there is no sound '" + name + "' (there are: " + string.Join(" ", SfxSynth.Names) + ")"); return null; }
                float last;
                if (soundAt.TryGetValue(name, out last) && clock - last < 0.1f) return null;
                if (sounds.Count >= 6) return null;
                soundAt[name] = clock;
                sounds.Add(name);
                return null;
            });
            box.Def("flat", a =>
            {
                if (a.Has(0))
                {
                    if (canSetFlat) flat = a.Bool(0, true);
                    else if (a.Bool(0, true) != flat) box.Note("flat() only works at the top of the script or in start()");
                }
                return DynValue.NewBoolean(flat);
            });

            // ---- a grid for board games
            box.Def("grid", a => Grid(a));
            DefineKit();
            DefineOuts();
        }

        string PoseOf(string name, string fn)
        {
            if (Array.IndexOf(Poses, name) >= 0) return name;
            switch (name)
            {
                case "happy": case "joy": case "dance": case "win": case "jump": return "cheer";
                case "sad": case "lose": case "sit": case "sleep": return "down";
                case "scared": case "afraid": case "hide": return "cower";
                case "ouch": case "hit": return "hurt";
                case "stand": case "think": case "": return "idle";
            }
            box.Note(fn + ": there is no pose '" + Cut(name, 24) + "' (there are: " + string.Join(" ", Poses) + ", and the motions the script defines)");
            return "idle";
        }

        // grid(cols, rows, x, y, w, h): cols x rows cells in the rectangle w x h around x, y. Column 1 is the left one, row 1 the bottom one.
        DynValue Grid(LuaArgs a)
        {
            int cols = Math.Max(1, Math.Min(40, a.Int(0, 3))), rows = Math.Max(1, Math.Min(40, a.Int(1, cols)));
            float cx = a.Num(2, W * 0.5f), cy = a.Num(3, 0.5f), w = a.Num(4, 0.8f, 0.01f, 4f), h = a.Num(5, w, 0.01f, 4f);
            float x0 = cx - w * 0.5f, y0 = cy - h * 0.5f, cw = w / cols, ch = h / rows;
            Table g = box.NewTable();
            g.Set("cols", DynValue.NewNumber(cols)); g.Set("rows", DynValue.NewNumber(rows));
            g.Set("x", DynValue.NewNumber(cx)); g.Set("y", DynValue.NewNumber(cy));
            g.Set("w", DynValue.NewNumber(w)); g.Set("h", DynValue.NewNumber(h));
            g.Set("cw", DynValue.NewNumber(cw)); g.Set("ch", DynValue.NewNumber(ch));
            g.Set("cell", box.Callback("grid:cell", b =>
            {
                if (b.IsTable(0)) b = b.Skip(1);                // g:cell(x, y) and g.cell(x, y) both work
                float px, py;
                if (b.IsTable(0)) { Table p = b.Tab(0); px = LuaArgs.Field(p, "x", 1, float.NaN); py = LuaArgs.Field(p, "y", 2, float.NaN); }      // g:cell(tap)
                else { px = b.Num(0, float.NaN); py = b.Num(1, float.NaN); }
                if (float.IsNaN(px) || float.IsNaN(py)) return DynValue.Nil;
                int c = (int)Math.Floor((px - x0) / cw), r = (int)Math.Floor((py - y0) / ch);
                if (c < 0 || c >= cols || r < 0 || r >= rows) return DynValue.Nil;
                return DynValue.NewTuple(DynValue.NewNumber(c + 1), DynValue.NewNumber(r + 1));
            }));
            g.Set("center", box.Callback("grid:center", b =>
            {
                if (b.IsTable(0)) b = b.Skip(1);
                float c = b.Num(0, 1f), r = b.Num(1, 1f);
                return DynValue.NewTuple(DynValue.NewNumber(x0 + (c - 0.5f) * cw), DynValue.NewNumber(y0 + (r - 0.5f) * ch));
            }));
            g.Set("draw", box.Callback("grid:draw", b =>
            {
                if (b.IsTable(0)) b = b.Skip(1);
                int color = b.Color(0);
                float thick = b.Num(1, 0.004f, 0.001f, 0.1f), alpha = Alpha(b, 2);
                for (int i = 0; i <= cols; i++) Put(GlassArt.Rect, x0 + i * cw, cy, h + thick, thick, color, alpha, 0f, 0, "", "", 0f, false);
                for (int i = 0; i <= rows; i++) Put(GlassArt.Rect, cx, y0 + i * ch, thick, w + thick, color, alpha, 0f, 0, "", "", 0f, false);
                return null;
            }));
            return DynValue.NewTable(g);
        }
    }
}
