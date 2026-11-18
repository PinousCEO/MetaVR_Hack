using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LivingPartner.Shared
{
    /// <summary>What GlassScriptCheck found out about a script by playing it.</summary>
    public sealed class GlassScriptReport
    {
        /// <summary>It compiled and ran to the end of the check without an error (warnings do not change this).</summary>
        public bool ok;
        /// <summary>ok, and nothing to warn about: good enough to be put on the shelf.</summary>
        public bool Clean { get { return ok && warnings.Count == 0; } }
        /// <summary>What broke ("" = nothing), the line of the source it is on (0 = unknown) and that line's text.</summary>
        public string error = "";
        public int errorLine;
        public string errorSource = "";
        /// <summary>In which of the plays it broke: "hands" (a bot played) | "alone" (nobody played) | "" (not at all, or before the first frame).</summary>
        public string errorIn = "";

        public int frames;                      // frames the play with the bot ran
        public float secondsPlayed;             // game seconds of that play
        public int maxSprites;                  // the most things one frame drew
        public int dropped;                     // things over the limit that were left out
        public bool drew;                       // anything was ever drawn
        public bool pictureChanged;             // the picture was not the same in every frame
        public bool readHands, readTaps, readHeld;      // hands() / taps() / held() were called
        public bool usedOuts, readOutEvents, readAim, readShots;     // shoot_out / out_events() / aim() / shots() were called
        public int outs, outsRefused;           // things the script sent out toward the player / that were left out (too many at once)
        public int fired;                       // shots the bot fired by pointing
        public bool reacts;                     // with the bot's hands the picture or the score went differently than with none
        public bool scoreChanged;               // the score moved while the bot played
        public int score, scoreAlone;           // at the end, with the bot / with nobody
        public bool finished, finishedAlone;    // finish() was reached, with the bot / with nobody
        public float finishedAt = -1f, finishedAloneAt = -1f;   // after how many seconds of play
        public string result = "";              // the line finish() gave (with the bot)
        public bool flat;
        public int taps;                        // taps the bot made
        public int motions;                     // motions the script defined
        public int startInstructions, peakInstructions, averageInstructions;    // top level + start / the busiest update / an average update
        public int budget = LuaSandbox.DefaultBudget;
        public float peakShare;                 // peakInstructions / budget
        public float smallestText;              // the smallest letters drawn (0 = no words)
        public double realSeconds;              // how long the check itself took
        public readonly List<string> warnings = new List<string>();

        /// <summary>The report as text for the model that has to repair the script (or for a log).</summary>
        public string Summary()
        {
            var sb = new StringBuilder();
            if (!ok)
            {
                sb.Append("ERROR");
                if (errorIn.Length > 0) sb.Append(errorIn == "alone" ? " (while nobody was playing)" : " (while it was played)");
                sb.Append(": ").Append(errorLine > 0 ? "line " + errorLine + ": " : "").Append(error);
                if (errorSource.Length > 0) sb.Append("\n  the line is:  ").Append(errorSource);
                sb.Append('\n');
            }
            else sb.Append("It ran without an error.\n");
            for (int i = 0; i < warnings.Count; i++) sb.Append("WARNING: ").Append(warnings[i]).Append('\n');
            sb.Append("facts: ").Append(frames).Append(" frames, at most ").Append(maxSprites).Append(" things drawn in a frame");
            sb.Append(flat ? ", flat" : ", upright");
            sb.Append(", hands() ").Append(readHands ? "used" : "not used").Append(", taps() ").Append(readTaps ? "used" : "not used").Append(", held() ").Append(readHeld ? "used" : "not used");
            if (usedOuts || readOutEvents) sb.Append(", shoot_out ").Append(usedOuts ? "used (" + outs + " sent)" : "not used").Append(", out_events() ").Append(readOutEvents ? "used" : "not used");
            if (readAim || readShots) sb.Append(", aim() ").Append(readAim ? "used" : "not used").Append(", shots() ").Append(readShots ? "used" : "not used");
            sb.Append(", score ").Append(score).Append(" with a player and ").Append(scoreAlone).Append(" without");
            sb.Append(finished ? ", finished after " + finishedAt.ToString("0.#", CultureInfo.InvariantCulture) + " s (\"" + result + "\")" : ", did not finish in " + secondsPlayed.ToString("0", CultureInfo.InvariantCulture) + " s");
            sb.Append(", ").Append(averageInstructions).Append(" steps a frame (most ").Append(peakInstructions).Append(" of ").Append(budget).Append(")");
            if (motions > 0) sb.Append(", ").Append(motions).Append(" motion(s)");
            sb.Append('.');
            return sb.ToString();
        }
    }

    /// <summary>
    /// THE PROOF THAT A SCRIPT WORKS: it is played. Twice with the same seed - once by a bot hand that wanders, hovers,
    /// rests, and taps both at random places and where things are drawn (so buttons, cells and falling things get
    /// hit), once by nobody - and what happened is compared. The bot's hand SHOWS something, another thing every 0.7 s
    /// (rock, paper, scissors, one finger, something unclear), and says how fast it moves (BotMetres metres for a unit
    /// of the field; 0 while it rests); now and then it is held up away from the field, where it is seen but does not
    /// act (all_hands). The bot's other hand POINTS at what is drawn and fires
    /// (aim / fire), and whatever the script sends out through the glass is answered after 0.6 - 1.2 s: blocked
    /// (50 of 100), hit (30) or missed (20) with the bot, always hit with nobody - so a game must be fair to a
    /// player who does nothing. The host runs this before it accepts a script the model
    /// wrote (and sends Summary() back to the model until the script is Clean); the tests run it on every example.
    /// Wrong arguments are errors here (GlassScript.Strict), every motion the script defines is evaluated, and the
    /// instructions are counted exactly. 40 game seconds take a few tens of milliseconds.
    /// </summary>
    public static class GlassScriptCheck
    {
        public const float Tick = 1f / 30f;
        public const int HeavyAverage = 8000;           // steps an average frame should stay under (a headset runs a few million steps a second: this is about two milliseconds)
        public const float HeavyPeak = 0.25f;           // share of the budget the busiest frame should stay under
        public const float BotMetres = 0.3f;            // the bot's field is this many metres high (for the speed of its hand)
        /// <summary>What the bot's hand shows, one after the other (finger bits; -1 = not known, 10 = index and ring: nothing).</summary>
        public static readonly int[] BotShows = { 0, 30, 6, 2, -1, 30, 0, 6, 10 };
        public const float BotShowSeconds = 0.7f;

        public static GlassScriptReport Run(string lua, GamePackDef pack = null, float seconds = 40f, int seed = 1)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var r = new GlassScriptReport();
            if (!(seconds > 0.5f)) seconds = 0.5f; else if (seconds > 3600f) seconds = 3600f;
            var p = new GamePackDef();
            if (pack != null)
            {
                p.id = pack.id; p.title = pack.title; p.howTo = pack.howTo; p.lives = pack.lives; p.art = pack.art;
                p.seconds = pack.seconds; p.speed = pack.speed; p.spawn = pack.spawn;
            }
            p.template = GlassScript.TemplateName;
            p.script = lua ?? "";

            Play a = new Play(), b = new Play();
            a.Run(p, seconds, seed, true);
            GlassScript g = a.game;
            r.frames = a.frames; r.secondsPlayed = g.seconds; r.flat = g.flat;
            r.maxSprites = g.MaxDrawn; r.dropped = g.Dropped; r.drew = g.DrewEver; r.pictureChanged = a.changed;
            r.readHands = g.ReadHands; r.readTaps = g.ReadTaps; r.readHeld = g.ReadHeld;
            r.usedOuts = g.UsedOuts; r.readOutEvents = g.ReadOutEvents; r.readAim = g.ReadAim; r.readShots = g.ReadShots;
            r.outs = g.OutsSent; r.outsRefused = g.OutsRefused; r.fired = a.fired;
            r.score = g.score; r.scoreChanged = a.scoreMoved;
            r.finished = g.Error.Length == 0 && g.phase == "over"; r.finishedAt = r.finished ? g.FinishedAt : -1f; r.result = r.finished ? g.result : "";
            r.taps = a.taps; r.motions = g.Motions != null ? g.Motions.Count : 0;
            r.startInstructions = g.StartInstructions; r.peakInstructions = g.PeakUpdate;
            r.averageInstructions = g.Updates > 0 ? (int)(g.UpdateInstructions / g.Updates) : 0;
            r.budget = g.Box != null ? g.Box.Budget : LuaSandbox.DefaultBudget;
            r.peakShare = r.budget > 0 ? (float)r.peakInstructions / r.budget : 0f;
            r.smallestText = g.SmallestText == float.MaxValue ? 0f : g.SmallestText;

            if (g.Error.Length > 0)
            {
                r.error = g.Box != null && g.Box.Error.Length > 0 ? g.Box.Error : g.Error;
                r.errorLine = g.ErrorLine; r.errorSource = g.ErrorSource;
                r.errorIn = a.frames > 0 ? "hands" : "";
            }
            else
            {
                b.Run(p, seconds, seed, false);
                GlassScript n = b.game;
                r.scoreAlone = n.score;
                r.finishedAlone = n.Error.Length == 0 && n.phase == "over"; r.finishedAloneAt = r.finishedAlone ? n.FinishedAt : -1f;
                if (n.Error.Length > 0)
                {
                    r.error = n.Box != null && n.Box.Error.Length > 0 ? n.Box.Error : n.Error;
                    r.errorLine = n.ErrorLine; r.errorSource = n.ErrorSource; r.errorIn = "alone";
                }
                r.reacts = Differ(a, b);
                if (n.PeakUpdate > r.peakInstructions) { r.peakInstructions = n.PeakUpdate; r.peakShare = (float)r.peakInstructions / r.budget; }
            }
            r.ok = r.error.Length == 0;

            // ---- the warnings
            List<string> w = r.warnings;
            if (r.ok || a.frames > 0)
            {
                bool early = r.ok && r.finishedAlone && r.finishedAloneAt < 2f;       // (it is over before anything could happen: say that, not what follows from it)
                if (!r.drew) w.Add("nothing is drawn: update must draw the whole picture every frame (rect, disc, pic, text ...)");
                else if (r.ok && !r.pictureChanged && !early) w.Add("the picture never changes: nothing moves and nothing answers the player");
                // (what comes back from the room - blocked, hit - is the player's doing as well)
                if (!r.readHands && !r.readTaps && !r.readHeld && !r.readShots && !r.readAim && !r.readOutEvents) w.Add("the hands are never read: use taps() or hands() so that the player can do something");
                else if (r.ok && r.drew && !r.reacts && !early) w.Add("nothing changes when the hands play: what taps() / hands() give has no effect that can be seen");
                if (r.usedOuts && !r.readOutEvents) w.Add("shoot_out is called but out_events() is never read: the game never learns whether the player blocked it or was hit");
                if (early) w.Add("it ends within 2 seconds without anyone playing (finish() is called too early)");
                if (r.dropped > 0) w.Add("more than " + GlassScript.MaxSprites + " things (or " + GlassScript.MaxLabels + " texts) in one frame: " + r.maxSprites + " were asked for, the rest was left out");
                if (g.Drawn > 0 && g.DrawnOutside > Math.Max(5, g.Drawn / 50)) w.Add("it draws outside the field: x must be 0.." + g.W.ToString("0.##", CultureInfo.InvariantCulture) + " and y 0..1 (these are not pixels)");
                if (r.smallestText > 0f && r.smallestText < GlassScript.MinReadable) w.Add("text too small to read (size " + r.smallestText.ToString("0.###", CultureInfo.InvariantCulture) + "; use 0.04 or more)");
                if (r.averageInstructions > HeavyAverage) w.Add("update is heavy: " + r.averageInstructions + " steps a frame on average (keep it under " + HeavyAverage + ": do less in every frame)");
                else if (r.peakShare > HeavyPeak) w.Add("one frame needed " + r.peakInstructions + " steps (the limit is " + r.budget + "): spread that work over several frames");
                AddNotes(w, a.game); AddNotes(w, b.game);
            }
            r.realSeconds = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) / (double)System.Diagnostics.Stopwatch.Frequency;
            return r;
        }

        static void AddNotes(List<string> w, GlassScript g)
        {
            if (g == null || g.Box == null) return;
            foreach (string n in g.Box.Notes) if (!w.Contains(n)) w.Add(n);
        }

        static bool Differ(Play a, Play b)
        {
            int n = Math.Min(a.hashes.Count, b.hashes.Count);
            for (int i = 0; i < n; i++) if (a.hashes[i] != b.hashes[i] || a.scores[i] != b.scores[i]) return true;
            return a.game.phase != b.game.phase || a.game.score != b.game.score;
        }

        // One play of the script, with the bot hand or with nobody.
        sealed class Play
        {
            public GlassScript game;
            public int frames, taps, fired;
            public bool changed, scoreMoved;
            public readonly List<ulong> hashes = new List<ulong>(1400);
            public readonly List<int> scores = new List<int>(1400);

            // the bot
            readonly GlassRng rng = new GlassRng();
            readonly GlassInput input = new GlassInput();
            float hx, hy, tx, ty, wait, act, away;
            int follow = -1;
            bool pressing, arrived;
            readonly Pose pose = new Pose();
            // the hand that points (its own random numbers: the first hand plays as it always did), and the verdicts on what was sent out
            readonly GlassRng aimRng = new GlassRng(), outRng = new GlassRng();
            float ax, ay, atx, aty, aimWait;
            int aimFollow = -1;
            readonly List<int> outId = new List<int>();
            readonly List<float> outDue = new List<float>();
            readonly List<string> outWhat = new List<string>();

            public void Run(GamePackDef pack, float seconds, int seed, bool bot)
            {
                game = new GlassScript();
                game.Strict = true; game.Precise = true;
                game.Start(pack, seed);
                if (game.Error.Length > 0) return;
                game.touch = game.flat;                         // a flat game is played on the desk, with fingertips
                rng.Seed(seed * 7919 + 13); aimRng.Seed(seed * 104729 + 7); outRng.Seed(seed * 15485863 + 3);
                hx = game.W * 0.5f; hy = 0.5f; NewTarget();
                ax = game.W * 0.5f; ay = 0.5f; atx = ax; aty = ay; aimWait = 0.4f;
                int total = (int)Math.Ceiling(seconds / Tick), overFrames = 0;
                int lastScore = game.score;
                // every motion the script defined is tried (a motion that breaks must be found here, not on the headset)
                LuaMotion m = game.Motions;
                for (int i = 0; m != null && i < m.Count && !game.Box.Broken; i++)
                    for (int k = 0; k <= 8 && !game.Box.Broken; k++) m.Evaluate(m.NameAt(i), k / 8f, k * 0.37f, pose);
                for (int f = 0; f < total; f++)
                {
                    if (bot) { Bot(Tick); Aim(Tick); Shows(f); } else { input.valid[0] = false; input.seen[0] = false; }
                    game.Step(Tick, input);
                    frames++;
                    Outs(bot, frames * Tick);
                    if (game.Error.Length > 0) break;
                    ulong h = Hash(game);
                    if (hashes.Count > 0 && hashes[hashes.Count - 1] != h && game.phase != "intro") changed = true;
                    hashes.Add(h); scores.Add(game.score);
                    if (game.score != lastScore) { scoreMoved = true; lastScore = game.score; }
                    if (game.phase == "over" && ++overFrames > 45) break;       // a second and a half of the end, then it is enough
                }
            }

            static ulong Hash(GlassGame g)
            {
                ulong h = 1469598103934665603UL;
                List<GlassSprite> s = g.sprites;
                for (int i = 0; i < s.Count; i++)
                {
                    if (s[i].id >= GlassScript.DwellId && s[i].id < GlassScript.DwellId + 2) continue;      // (the ring of a resting hand is the runtime's, not the script's)
                    h = Mix(h, (ulong)(uint)(s[i].art != null ? StableHash(s[i].art) : 0));
                    h = Mix(h, (ulong)(uint)(int)Math.Round(s[i].x * 500f)); h = Mix(h, (ulong)(uint)(int)Math.Round(s[i].y * 500f));
                    h = Mix(h, (ulong)(uint)(int)Math.Round(s[i].size * 500f)); h = Mix(h, (ulong)(uint)(int)Math.Round(s[i].w * 500f));
                    h = Mix(h, (ulong)(uint)s[i].color); h = Mix(h, (ulong)(uint)(int)Math.Round(s[i].alpha * 50f));
                    h = Mix(h, (ulong)(uint)(int)Math.Round(s[i].rot)); h = Mix(h, (ulong)(uint)(int)Math.Round(s[i].fill * 50f));
                    if (!string.IsNullOrEmpty(s[i].text)) h = Mix(h, (ulong)(uint)StableHash(s[i].text));
                }
                h = Mix(h, (ulong)(uint)(int)Math.Round(g.petX * 200f)); h = Mix(h, (ulong)(uint)(int)Math.Round(g.petY * 200f));
                h = Mix(h, (ulong)(uint)StableHash(g.petPose ?? "")); h = Mix(h, (ulong)(uint)StableHash(g.banner ?? ""));
                h = Mix(h, (ulong)(uint)StableHash(g.petLine ?? "")); h = Mix(h, (ulong)(uint)g.lives); h = Mix(h, (ulong)(uint)g.level);
                return h;
            }

            static ulong Mix(ulong h, ulong v) { unchecked { return (h ^ v) * 1099511628211UL; } }
            static int StableHash(string s) { unchecked { int h = 17; for (int i = 0; i < s.Length; i++) h = h * 31 + s[i]; return h; } }

            // ---- through the glass: the room answers. With nobody playing, everything hits.
            void Outs(bool bot, float now)
            {
                List<GlassOut> o = game.outs;
                for (int i = 0; i < o.Count; i++)
                {
                    float roll = outRng.Next01();
                    outId.Add(o[i].id); outDue.Add(now + outRng.Range(0.6f, 1.2f));
                    outWhat.Add(!bot ? "hit" : roll < 0.5f ? "blocked" : roll < 0.8f ? "hit" : "missed");
                }
                o.Clear();
                for (int i = 0; i < outId.Count; i++)
                {
                    if (outDue[i] > now) continue;
                    game.OutResult(outId[i], outWhat[i]);
                    outId.RemoveAt(i); outDue.RemoveAt(i); outWhat.RemoveAt(i); i--;
                }
            }

            // ---- the pointing hand: it aims at something that is drawn (or just somewhere), fires when it is on it, and looks for the next
            void Aim(float dt)
            {
                List<GlassSprite> s = game.sprites;
                input.fire[1] = false;
                if (aimFollow >= 0 && aimFollow < s.Count && s[aimFollow].x > 0f && s[aimFollow].x < game.W && s[aimFollow].y > 0f && s[aimFollow].y < 1f) { atx = s[aimFollow].x; aty = s[aimFollow].y; }
                float dx = atx - ax, dy = aty - ay, d = (float)Math.Sqrt(dx * dx + dy * dy), step = 2.4f * dt;
                if (d > step) { ax += dx / d * step; ay += dy / d * step; }
                else
                {
                    ax = atx; ay = aty;
                    aimWait -= dt;
                    if (aimWait <= 0f)
                    {
                        input.fire[1] = true; fired++;
                        aimWait = aimRng.Range(0.25f, 0.7f);
                        aimFollow = -1;
                        atx = aimRng.Range(0.06f, game.W - 0.06f); aty = aimRng.Range(0.06f, 0.94f);
                        for (int tries = 0; tries < 8 && s.Count > 0 && aimRng.Chance(0.8f); tries++)
                        {
                            int k = aimRng.Int(s.Count);
                            float w = s[k].w > 0f ? s[k].w : s[k].size, hgt = s[k].size;
                            if (s[k].id < GlassScript.FirstId || w > 0.7f || hgt > 0.7f || Math.Min(w, hgt) < 0.012f) continue;
                            if (s[k].x < 0f || s[k].x > game.W || s[k].y < 0f || s[k].y > 1f) continue;
                            atx = s[k].x; aty = s[k].y; aimFollow = k;
                            break;
                        }
                    }
                }
                input.aimValid[1] = true; input.aimX[1] = ax; input.aimY[1] = ay;
            }

            // ---- what the bot's hand shows and how fast it moves; a hand that is "gone" is held up away from the field for the second half of that time
            float shownX, shownY; bool shownAny;
            void Shows(int f)
            {
                bool near = input.valid[0];
                input.seen[0] = near || away < 0.25f;
                input.fingers[0] = BotShows[(int)(f * Tick / BotShowSeconds) % BotShows.Length];
                if (!near) { input.x[0] = hx; input.y[0] = hy; input.dist[0] = 0.5f; }
                float dx = hx - shownX, dy = hy - shownY;
                input.speed[0] = shownAny && input.seen[0] ? (float)Math.Sqrt(dx * dx + dy * dy) / Tick * BotMetres : 0f;
                shownX = hx; shownY = hy; shownAny = true;
            }

            // ---- the bot hand: goes somewhere, and there it taps (or rests, or holds); then somewhere else
            void NewTarget()
            {
                follow = -1; arrived = false; pressing = false;
                List<GlassSprite> s = game.sprites;
                float pick = rng.Next01();
                if (pick < 0.62f && s.Count > 0)
                {
                    // something that was drawn: not the big backgrounds, not hair lines
                    for (int tries = 0; tries < 8; tries++)
                    {
                        int k = rng.Int(s.Count);
                        float w = s[k].w > 0f ? s[k].w : s[k].size, hgt = s[k].size;
                        if (s[k].id < GlassScript.FirstId || w > 0.7f || hgt > 0.7f || Math.Min(w, hgt) < 0.012f) continue;
                        if (s[k].x < 0f || s[k].x > game.W || s[k].y < 0f || s[k].y > 1f) continue;
                        tx = s[k].x; ty = s[k].y;
                        if (rng.Chance(0.45f)) follow = k;          // stay with it while it moves (a falling star, a mole)
                        return;
                    }
                }
                tx = rng.Range(0.06f, game.W - 0.06f); ty = rng.Range(0.06f, 0.94f);
            }

            void Bot(float dt)
            {
                bool touch = game.touch;
                float hover = touch ? 0.07f : 0.10f;
                if (away > 0f)                                      // the hand is gone for a moment
                {
                    away -= dt;
                    input.valid[0] = false;
                    return;
                }
                if (follow >= 0 && follow < game.sprites.Count && !arrived)
                {
                    GlassSprite s = game.sprites[follow];
                    if (s.x > -0.2f && s.x < game.W + 0.2f && s.y > -0.2f && s.y < 1.2f) { tx = s.x; ty = s.y; }
                }
                float dx = tx - hx, dy = ty - hy, d = (float)Math.Sqrt(dx * dx + dy * dy), step = 1.6f * dt;
                if (!arrived)
                {
                    if (d <= step) { hx = tx; hy = ty; arrived = true; wait = 0f; act = rng.Chance(0.25f) ? 1.4f : (touch ? 0.14f : 0.66f); pressing = true; taps++; }
                    else { hx += dx / d * step; hy += dy / d * step; }
                }
                else
                {
                    wait += dt;
                    if (wait >= act)
                    {
                        pressing = false;
                        if (wait >= act + 0.18f)
                        {
                            if (rng.Chance(0.06f)) away = 0.5f;
                            NewTarget();
                        }
                    }
                }
                input.valid[0] = true; input.x[0] = hx; input.y[0] = hy; input.spread[0] = 1f;
                input.dist[0] = touch && pressing ? 0.004f : hover;
            }
        }
    }
}
