using System.Globalization;
using System.Numerics;
using System.Text;
using LivingPartner.Shared;

namespace LivingPartner.Host;

// THE WORKSHOP'S PLAYTESTS. What the workshop (Workshop.cs) writes is PLAYED here before anybody believes it: the
// real interpreter (GlassScript / RoomScript), headless, with simulated players -
//   nobody   no hands at all; everything that flies out of the field reaches the player (a game must be fair to that)
//   random   hands that wander, rest and fire anywhere, as a child would
//   seeker   a player who goes for what the script draws: rests a hand on keys, cells and things, follows what moves,
//            points and fires at targets (and learns which kind of target scores), shields most of what flies out
// - and what happened is written down: errors, a timeline of score / lives / level / banners / sounds / what Nilo did /
// what left the field and what became of it, how it ended, and the sprite list at a few moments (WorkshopLook.cs turns
// one into a picture). Nothing of the host is in this file: the tests run it as it is.

/// <summary>One moment of a play: everything a renderer would be given.</summary>
public sealed class PlayFrame
{
    public float t; public string player = "";
    public float W = 16f / 9f; public bool flat;
    public List<GlassSprite> sprites = new();
    public float petX, petY; public bool petRight = true; public string petPose = "", petMotion = "", petLine = "";
    public string banner = "", phase = "", result = "";
    public int score, lives, level;
    public bool hand; public float handX, handY;
    public bool aim; public float aimX, aimY;
    /// <summary>Where things left the field a moment ago (they are in the room now, flying at the player).</summary>
    public List<(float x, float y, string art)> flying = new();
}

public sealed class PlayReport
{
    public string player = "";
    public float seconds;
    public string error = ""; public int errorLine; public string errorSource = "";
    public bool finished; public float finishedAt = -1f; public string result = "";
    public int scoreStart, score, scoreChanges, livesStart, lives, level;
    public int taps, shots, outsSent, blocked, hit, missed;
    public bool drew, footballDrawn, footballSent; public int maxSprites;
    public bool readHands, readTaps, readHeld, readShots, readAim, usedOuts, readOutEvents, usedKit, flat;
    public int petLines, petPlays; public float petWalked; public bool petPosed;
    public List<string> petSaid = new(), petMotions = new();
    public List<(float t, string what)> timeline = new();
    public List<PlayFrame> frames = new();
    public List<ulong> hashes = new();
    public bool Ok => error.Length == 0;
    public bool PetActed => petLines > 0 || petPlays > 0 || petWalked > 0.05f || petPosed;

    static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    public static string Who(string player) => player switch
    {
        WorkshopPlay.Nobody => "nobody at the glass (no hands; everything sent out reaches the player)",
        WorkshopPlay.Random => "random hands (they wander, rest and fire anywhere; half of what flies out is blocked)",
        _ => "a player who goes for it at a person's pace (rests a hand on keys and things, follows what moves, points and fires at targets - about one shot a second -, shields 85% of what flies out)",
    };

    /// <summary>One line: how it went.</summary>
    public string Digest()
    {
        var sb = new StringBuilder();
        sb.Append(player).Append(": ");
        if (!Ok) { sb.Append("ERROR ").Append(errorLine > 0 ? "line " + errorLine + ": " : "").Append(error); if (errorSource.Length > 0) sb.Append("  |  ").Append(errorSource); return sb.ToString(); }
        sb.Append(finished ? $"finished after {F(finishedAt)} s (\"{result}\")" : $"still playing after {F(seconds)} s");
        sb.Append($"; score {scoreStart} -> {score} ({scoreChanges} change(s)); lives {livesStart} -> {lives}; level {level}");
        if (player != WorkshopPlay.Nobody) sb.Append($"; {taps} tap(s), {shots} shot(s) fired");
        if (outsSent > 0 || usedOuts) sb.Append($"; through the glass: {outsSent} sent out, {blocked} blocked, {hit} hit, {missed} missed");
        sb.Append(PetActed ? $"; Nilo: {petLines} line(s), {petPlays} motion(s) played, walked {F(petWalked)}" : "; Nilo did NOTHING (no pet_say, pet_play, pet_goto, pet_pose)");
        if (!drew) sb.Append("; NOTHING WAS DRAWN");
        return sb.ToString();
    }

    /// <summary>The whole report for the model: the digest, what was called, the timeline, and what was drawn at the captured moments.</summary>
    public string Text(int maxEvents = 44, bool dumps = true)
    {
        var sb = new StringBuilder();
        sb.Append("PLAYTEST - ").Append(Who(player)).Append('\n');
        sb.Append(Digest()).Append('\n');
        sb.Append("calls: hands() ").Append(readHands ? "yes" : "no").Append(", taps() ").Append(readTaps ? "yes" : "no").Append(", held() ").Append(readHeld ? "yes" : "no")
          .Append(", shots() ").Append(readShots ? "yes" : "no").Append(", aim() ").Append(readAim ? "yes" : "no").Append(", shoot_out ").Append(usedOuts ? "yes" : "no")
          .Append(", out_events() ").Append(readOutEvents ? "yes" : "no").Append(", UI kit ").Append(usedKit ? "yes" : "no").Append(flat ? ", flat" : ", upright").Append($", at most {maxSprites} things in a frame\n");
        if (petSaid.Count > 0) sb.Append("Nilo said: ").Append(string.Join(" | ", petSaid.Take(6).Select(s => "\"" + s + "\""))).Append(petSaid.Count > 6 ? " ..." : "").Append('\n');
        if (petMotions.Count > 0) sb.Append("Nilo's motions played: ").Append(string.Join(", ", petMotions.GroupBy(m => m).Select(g => g.Key + " x" + g.Count()))).Append('\n');
        sb.Append("timeline (seconds: what happened):\n");
        int n = timeline.Count, head = n <= maxEvents ? n : maxEvents * 2 / 3, tail = n <= maxEvents ? 0 : maxEvents - head;
        for (int i = 0; i < head; i++) sb.Append("  ").Append(F(timeline[i].t).PadLeft(5)).Append("  ").Append(timeline[i].what).Append('\n');
        if (n > maxEvents) sb.Append($"  ... ({n - head - tail} more)\n");
        for (int i = n - tail; i < n; i++) sb.Append("  ").Append(F(timeline[i].t).PadLeft(5)).Append("  ").Append(timeline[i].what).Append('\n');
        if (n == 0) sb.Append("  (nothing happened at all)\n");
        if (dumps) foreach (var f in frames) sb.Append(WorkshopPlay.Dump(f)).Append('\n');
        return sb.ToString();
    }
}

/// <summary>A motion a script defined, sampled over its time: which bones move and how far.</summary>
public sealed class MotionSample
{
    public string name = ""; public float seconds; public bool loop;
    public float moved;                 // the widest swing of any bone, degrees
    public float lift;                  // how far the body leaves its place, character units
    public bool played;                 // pet_play of it was seen in the check
    public string text = "";
    public bool Frozen => moved < 6f && lift < 0.03f;
}

public static class WorkshopPlay
{
    public const string Nobody = "nobody", Random = "random", Seeker = "seeker";
    public static readonly string[] Players = { Nobody, Random, Seeker };
    public const float Tick = 1f / 30f;
    /// <summary>How far the player sits from the glass (metres): what is sent out needs 0.6 / speed seconds to arrive.</summary>
    public const float PlayerMetres = 0.6f;

    static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Plays a scripted game. art: the pictures the pack will have. captureAt: the moments (seconds) whose frame is
    /// kept (the last frame always is). inside: as if it ran inside a monitor. Never throws.
    /// </summary>
    public static PlayReport Run(string lua, IEnumerable<string> art, string player, float seconds = 40f, int seed = 1, bool inside = true, IEnumerable<float> captureAt = null)
    {
        var r = new PlayReport { player = Array.IndexOf(Players, player) >= 0 ? player : Seeker };
        try { new Sim(r, seed).Run(lua, art, Math.Clamp(seconds, 1f, 600f), inside, (captureAt ?? new float[0]).OrderBy(t => t).ToList()); }
        catch (Exception e) { if (r.error.Length == 0) r.error = "the playtest broke: " + e.GetType().Name + " " + e.Message; }
        return r;
    }

    /// <summary>The frame of a play at the time t (the nearest one that was reached): for the picture.</summary>
    public static PlayFrame FrameAt(string lua, IEnumerable<string> art, string player, float t, int seed = 1, bool inside = true)
    {
        var r = Run(lua, art, player, Math.Max(1f, t + Tick), seed, inside, new[] { t });
        return r.frames.Count > 0 ? r.frames[0] : null;
    }

    public static string Hex(int c) => c < 0 ? "as-is" : "#" + (c & 0xFFFFFF).ToString("X6");

    /// <summary>What was drawn at one moment, compactly: counts by kind, then the things themselves (the big ones first cut, 22 at most).</summary>
    public static string Dump(PlayFrame f)
    {
        if (f == null) return "(no frame)";
        var sb = new StringBuilder();
        sb.Append($"DRAWN at t={N(f.t)} s ({f.player}; phase {f.phase}; score {f.score}, lives {f.lives}, level {f.level}{(f.banner.Length > 0 ? $"; banner \"{f.banner}\"" : "")}{(f.result.Length > 0 ? $"; result \"{f.result}\"" : "")})\n");
        sb.Append($"  Nilo at ({N(f.petX)}, {N(f.petY)}) facing {(f.petRight ? "right" : "left")}, {(f.petMotion.Length > 0 ? "playing motion '" + f.petMotion + "'" : "pose " + f.petPose)}{(f.petLine.Length > 0 ? $", says \"{f.petLine}\"" : "")}\n");
        if (f.hand) sb.Append($"  the player's hand is at ({N(f.handX)}, {N(f.handY)})");
        if (f.aim) sb.Append($"{(f.hand ? "; " : "  ")}the player points at ({N(f.aimX)}, {N(f.aimY)})");
        if (f.hand || f.aim) sb.Append('\n');
        if (f.flying.Count > 0) sb.Append($"  {f.flying.Count} thing(s) are in the ROOM, flying at the player (they left the field at {string.Join(", ", f.flying.Select(o => $"({N(o.x)}, {N(o.y)})"))})\n");
        var s = f.sprites.Where(x => x.id >= GlassScript.FirstId).ToList();
        if (s.Count == 0) { sb.Append("  NOTHING is drawn\n"); return sb.ToString(); }
        sb.Append($"  {s.Count} thing(s): {string.Join(", ", s.GroupBy(x => x.art ?? "?").Select(g => g.Key + " x" + g.Count()))}\n");
        int shown = 0;
        foreach (var x in s)
        {
            if (shown >= 22) { sb.Append($"  ... and {s.Count - shown} more\n"); break; }
            float w = x.w > 0 ? x.w : x.size;
            if (Math.Max(w, x.size) < 0.02f) continue;
            shown++;
            sb.Append("  ").Append(x.art);
            sb.Append($" ({N(x.x)}, {N(x.y)})");
            if (x.art == GlassArt.Label) sb.Append($" \"{x.text}\" size {N(x.size)}");
            else if (x.art == GlassArt.Card) sb.Append($" \"{x.text}\" / \"{x.text2}\" h {N(x.size)}");
            else sb.Append($" {N(w)} x {N(x.size)}");
            if (x.color >= 0 && x.art != GlassArt.Card) sb.Append(' ').Append(Hex(x.color));
            if (x.alpha < 0.95f) sb.Append($" alpha {N(x.alpha)}");
            if (x.x < 0 || x.x > f.W || x.y < 0 || x.y > 1) sb.Append(" OUTSIDE THE FIELD");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ a thing or a skill
    /// <summary>The motions a room script (or a game) defines, each sampled at 13 moments: which bones move and how far.</summary>
    public static List<MotionSample> Motions(string lua, ItemDef item, ICollection<string> played = null)
    {
        var list = new List<MotionSample>();
        try
        {
            var rs = new RoomScript(lua ?? "", item ?? new ItemDef { id = "thing", name = "thing", sizeM = 0.08f }, null, 1);
            if (!rs.Ok) return list;
            foreach (string name in rs.Motions)
            {
                var m = new MotionSample { name = name, seconds = rs.MotionSeconds(name), played = played == null || played.Contains(name) };
                var lo = new Vector3[Skeleton.N]; var hi = new Vector3[Skeleton.N];
                float liftLo = 0, liftHi = 0, squashHi = 0; bool any = false;
                var p = new Pose();
                for (int k = 0; k <= 12; k++)
                {
                    p.Clear();
                    if (!rs.MotionPose(name, k / 12f, p)) break;
                    for (int b = 0; b < Skeleton.N; b++)
                    {
                        if (!any) { lo[b] = p.Euler[b]; hi[b] = p.Euler[b]; }
                        else { lo[b] = Vector3.Min(lo[b], p.Euler[b]); hi[b] = Vector3.Max(hi[b], p.Euler[b]); }
                    }
                    float ly = p.HipsOffset.Y;
                    if (!any) { liftLo = liftHi = ly; } else { liftLo = Math.Min(liftLo, ly); liftHi = Math.Max(liftHi, ly); }
                    squashHi = Math.Max(squashHi, Math.Abs(p.Squash));
                    any = true;
                }
                var parts = new List<string>();
                for (int b = 0; b < Skeleton.N; b++)
                {
                    var d = hi[b] - lo[b];
                    float widest = Math.Max(d.X, Math.Max(d.Y, d.Z));
                    if (widest < 3f) continue;
                    m.moved = Math.Max(m.moved, widest);
                    var axes = new List<string>();
                    if (d.X >= 3f) axes.Add($"x {lo[b].X:0}..{hi[b].X:0}");
                    if (d.Y >= 3f) axes.Add($"y {lo[b].Y:0}..{hi[b].Y:0}");
                    if (d.Z >= 3f) axes.Add($"z {lo[b].Z:0}..{hi[b].Z:0}");
                    parts.Add($"{(Bone)b} {string.Join(" ", axes)}");
                }
                m.lift = liftHi - liftLo;
                if (m.lift >= 0.01f) parts.Add($"body lifts {N(liftLo)}..{N(liftHi)}");
                if (squashHi >= 0.05f) parts.Add($"squash up to {N(squashHi)}");
                m.text = $"motion '{name}' ({N(m.seconds)} s): " + (!any ? "IT BREAKS when it is evaluated" : m.Frozen ? "A FROZEN POSE - no bone swings more than 6 degrees over its time (a motion must MOVE: make the angles functions of u)" : string.Join("; ", parts) + " (degrees over its time)")
                         + (m.played ? "" : " - NEVER PLAYED");
                list.Add(m);
            }
        }
        catch (Exception) { }
        return list;
    }

    /// <summary>What a thing's (or a skill's) script does when everything a user can do with it is done: the checker's report plus the motions sampled over time.</summary>
    public static string Room(string lua, ItemDef item, out List<MotionSample> motions, float seconds = ScriptRules.MadeRoomSeconds, int seed = 1)
    {
        var sb = new StringBuilder();
        var r = RoomScriptCheck.Run(lua ?? "", item ?? new ItemDef { id = "thing", name = "thing", sizeM = 0.08f }, seconds, seed);
        var played = new HashSet<string>();
        foreach (var p in r.phases) foreach (string c in p.calls) { int at = c.IndexOf(" pet_play ", StringComparison.Ordinal); if (at >= 0) played.Add(c[(at + 10)..].Trim().Split(' ')[0]); }
        motions = r.ok ? Motions(lua, item, played) : new List<MotionSample>();
        sb.Append("PLAYTEST in a room that is not there: the thing is made, pressed (on_use), every act it offers is asked for, it is put on, the acts are asked for again, it is taken off. Each line: the step (how long the script was busy): time call ...\n");
        sb.Append(r.Report());
        foreach (var m in motions) sb.Append(m.text).Append('\n');
        if (r.said.Count > 0) sb.Append("Nilo said: ").Append(string.Join(" | ", r.said.Take(8).Select(s => "\"" + s + "\""))).Append('\n');
        if (r.sounds.Count > 0) sb.Append("sounds: ").Append(string.Join(" ", r.sounds.Distinct().Take(12))).Append('\n');
        return sb.ToString();
    }

    // ------------------------------------------------------------------ one play
    sealed class Sim
    {
        readonly PlayReport r;
        readonly GlassRng rng = new(), aimRng = new(), outRng = new();
        readonly GlassInput input = new();
        GlassScript game;
        bool seeker, random;
        // the hand that touches
        float hx, hy, tx, ty, wait, act, away; bool arrived, pressing; string followKey = "";
        // the hand that points
        float ax, ay, atx, aty, aimWait; string aimKey = "";
        // what is in the room
        readonly List<(int id, float due, string what, float x, float y, string art)> flying = new();
        // what the seeker has learned: which kind of thing scores
        readonly Dictionary<string, float> weight = new();
        string lastKey = ""; float lastAt = -10f;

        readonly int seed;
        public Sim(PlayReport report, int seed) { r = report; this.seed = seed; rng.Seed(seed * 7919 + 13); aimRng.Seed(seed * 104729 + 7); outRng.Seed(seed * 15485863 + 3); }

        static string Key(GlassSprite s) => (s.art ?? "") + ":" + s.color + ":" + (int)Math.Round(s.size * 25f);

        public void Run(string lua, IEnumerable<string> art, float seconds, bool inside, List<float> captureAt)
        {
            seeker = r.player == Seeker; random = r.player == Random;
            var pack = new GamePackDef { id = "game-playtest", template = GlassScript.TemplateName, title = "playtest", script = lua ?? "" };
            foreach (string name in art ?? Enumerable.Empty<string>()) if (!string.IsNullOrEmpty(name)) pack.art.Add(new PackArt { name = name });
            game = new GlassScript { Strict = true, Precise = true, Rounded = true, inside = inside };
            game.Start(pack, seed);
            r.flat = game.flat;
            if (Broke()) return;
            game.touch = game.flat;
            hx = game.W * 0.5f; hy = 0.5f; tx = hx; ty = hy; arrived = true; wait = 0; act = 0.2f;
            ax = game.W * 0.5f; ay = 0.5f; atx = ax; aty = ay; aimWait = 0.5f;
            r.scoreStart = game.score; r.livesStart = game.lives;
            int lastScore = game.score, lastLives = game.lives, lastLevel = game.level, overFrames = 0, next = 0;
            string lastBanner = "", lastLine = "", lastMotion = "", lastPose = game.petPose ?? "", lastPhase = game.phase;
            float lastPetX = game.petX, lastPetY = game.petY;
            var soundAt = new Dictionary<string, float>();
            int total = (int)Math.Ceiling(seconds / Tick);
            float t = 0f;
            for (int f = 0; f < total; f++)
            {
                if (seeker || random) { Touch(Tick); Point(Tick); } else { input.valid[0] = input.seen[0] = false; input.aimValid[1] = false; input.fire[1] = false; }
                game.Step(Tick, input);
                if (!r.footballDrawn) r.footballDrawn = game.sprites.Exists(s => GlassArt.IsFootball(s.art));
                t = (f + 1) * Tick;
                r.seconds = t;
                // what left the field, and what became of what was on its way
                foreach (var o in game.outs)
                {
                    float roll = outRng.Next01(), flight = Math.Clamp(PlayerMetres / Math.Max(0.2f, o.speed), 0.3f, 3f);
                    string what = !(seeker || random) ? "hit" : seeker ? (roll < 0.85f ? "blocked" : roll < 0.95f ? "hit" : "missed") : (roll < 0.5f ? "blocked" : roll < 0.8f ? "hit" : "missed");
                    flying.Add((o.id, t + flight, what, o.x, o.y, o.art ?? ""));
                    r.outsSent++;
                    r.footballSent |= GlassArt.IsFootball(o.art);
                    Note(t, $"OUT #{o.id} leaves the field at ({N(o.x)}, {N(o.y)}) and flies at the player ({o.art}, {N(o.speed)} m/s, {N(o.size)} m)");
                }
                game.outs.Clear();
                for (int i = 0; i < flying.Count; i++)
                {
                    if (flying[i].due > t) continue;
                    game.OutResult(flying[i].id, flying[i].what);
                    if (flying[i].what == "blocked") r.blocked++; else if (flying[i].what == "hit") r.hit++; else r.missed++;
                    Note(t, $"OUT #{flying[i].id}: {(flying[i].what == "blocked" ? "BLOCKED by the player's open hand" : flying[i].what == "hit" ? "it HIT the player" : "it missed the player")}");
                    flying.RemoveAt(i--);
                }
                if (Broke()) { Note(t, "ERROR: " + r.error); break; }
                // what changed
                if (game.phase != lastPhase) { Note(t, "phase " + game.phase + (game.phase == "over" ? $": \"{game.result}\"" : "")); lastPhase = game.phase; }
                if (game.score != lastScore)
                {
                    r.scoreChanges++; Note(t, $"score {lastScore} -> {game.score}");
                    if (t - lastAt < 0.7f && lastKey.Length > 0) weight[lastKey] = Math.Min(30f, W(lastKey) * (game.score > lastScore ? 2.2f : 0.5f) + (game.score > lastScore ? 1f : 0f));
                    lastScore = game.score;
                }
                if (game.lives != lastLives)
                {
                    Note(t, $"lives {lastLives} -> {game.lives}");
                    if (game.lives < lastLives && t - lastAt < 0.5f && lastKey.Length > 0) weight[lastKey] = Math.Max(0.05f, W(lastKey) * 0.4f);
                    lastLives = game.lives;
                }
                if (game.level != lastLevel) { Note(t, $"level {lastLevel} -> {game.level}"); lastLevel = game.level; }
                if ((game.banner ?? "") != lastBanner) { lastBanner = game.banner ?? ""; if (lastBanner.Length > 0) Note(t, $"banner \"{lastBanner}\""); }
                if ((game.petLine ?? "") != lastLine) { lastLine = game.petLine ?? ""; if (lastLine.Length > 0) { r.petLines++; if (r.petSaid.Count < 40) r.petSaid.Add(lastLine); Note(t, $"Nilo says \"{lastLine}\""); } }
                string motion = game.petCustom ? game.PetMotion : "";
                if (motion != lastMotion) { lastMotion = motion; if (motion.Length > 0) { r.petPlays++; if (r.petMotions.Count < 200) r.petMotions.Add(motion); Note(t, $"Nilo plays the motion '{motion}'"); } }
                string pose = game.petPose ?? "";
                if (pose != lastPose) { if (pose != "walk" && pose != "run" && pose != "idle") { r.petPosed = true; Note(t, $"Nilo's pose: {pose}"); } lastPose = pose; }
                r.petWalked += Math.Abs(game.petX - lastPetX) + Math.Abs(game.petY - lastPetY); lastPetX = game.petX; lastPetY = game.petY;
                foreach (string s in game.sounds)
                    if (!soundAt.TryGetValue(s, out float at) || t - at > 2.5f) { soundAt[s] = t; Note(t, "sound " + s); }
                r.hashes.Add(Hash(game));
                while (next < captureAt.Count && t >= captureAt[next]) { r.frames.Add(Frame(t)); next++; }
                if (game.phase == "over" && ++overFrames > 30) break;
            }
            r.score = game.score; r.lives = game.lives; r.level = game.level;
            r.finished = game.Error.Length == 0 && game.phase == "over"; r.finishedAt = r.finished ? game.FinishedAt : -1f; r.result = r.finished ? game.result ?? "" : "";
            r.drew = game.DrewEver; r.maxSprites = game.MaxDrawn;
            r.readHands = game.ReadHands; r.readTaps = game.ReadTaps; r.readHeld = game.ReadHeld; r.readShots = game.ReadShots; r.readAim = game.ReadAim;
            r.usedOuts = game.UsedOuts; r.readOutEvents = game.ReadOutEvents; r.usedKit = game.UsedKit;
            if (r.frames.Count == 0 || Math.Abs(r.frames[^1].t - t) > 0.01f) r.frames.Add(Frame(t));
        }

        float W(string key) => weight.TryGetValue(key, out float w) ? w : 1f;

        void Note(float t, string what) { if (r.timeline.Count < 400) r.timeline.Add((t, what)); }

        bool Broke()
        {
            if (game.Error.Length == 0) return false;
            r.error = game.Box != null && game.Box.Error.Length > 0 ? game.Box.Error : game.Error;
            r.errorLine = game.ErrorLine; r.errorSource = game.ErrorSource ?? "";
            return true;
        }

        PlayFrame Frame(float t)
        {
            var f = new PlayFrame
            {
                t = t, player = r.player, W = game.W, flat = game.flat, petX = game.petX, petY = game.petY, petRight = game.petFacesRight, petPose = game.petPose ?? "",
                petMotion = game.petCustom ? game.PetMotion : "", petLine = game.petLine ?? "", banner = game.banner ?? "", phase = game.phase ?? "", result = game.result ?? "",
                score = game.score, lives = game.lives, level = game.level,
                hand = input.valid[0], handX = input.x[0], handY = input.y[0], aim = input.aimValid[1], aimX = input.aimX[1], aimY = input.aimY[1],
            };
            f.sprites.AddRange(game.sprites);
            foreach (var o in flying) f.flying.Add((o.x, o.y, o.art));
            return f;
        }

        static ulong Hash(GlassGame g)
        {
            ulong h = 1469598103934665603UL;
            foreach (var s in g.sprites)
            {
                if (s.id < GlassScript.FirstId) continue;
                h = Mix(h, (ulong)(uint)Stable(s.art));
                h = Mix(h, (ulong)(uint)(int)Math.Round(s.x * 300f)); h = Mix(h, (ulong)(uint)(int)Math.Round(s.y * 300f)); h = Mix(h, (ulong)(uint)(int)Math.Round(s.size * 300f));
                h = Mix(h, (ulong)(uint)s.color);
                if (!string.IsNullOrEmpty(s.text)) h = Mix(h, (ulong)(uint)Stable(s.text));
            }
            h = Mix(h, (ulong)(uint)g.score); h = Mix(h, (ulong)(uint)g.lives); h = Mix(h, (ulong)(uint)Stable(g.banner));
            return h;
        }
        static ulong Mix(ulong h, ulong v) { unchecked { return (h ^ v) * 1099511628211UL; } }
        static int Stable(string s) { unchecked { int h = 17; foreach (char c in s ?? "") h = h * 31 + c; return h; } }

        // ---- what a hand could go for: not the backgrounds, not hair lines, not the rings and crosshairs
        bool Candidate(GlassSprite s, bool labels)
        {
            if (s.id < GlassScript.FirstId || s.alpha < 0.3f) return false;
            float w = s.w > 0f ? s.w : s.size, h = s.size;
            if (s.art == GlassArt.Label) { if (!labels) return false; w = Math.Max(0.03f, (s.text ?? "").Length * s.size * GlassScript.GlyphWidth); }
            else if (Math.Min(w, h) < 0.02f || Math.Max(w, h) > 0.62f) return false;
            if (s.art == GlassArt.RingShape) return false;
            return s.x > 0.02f && s.x < game.W - 0.02f && s.y > 0.02f && s.y < 0.98f;
        }

        bool Choose(GlassRng g, bool labels, out float x, out float y, out string key)
        {
            x = y = 0; key = "";
            var s = game.sprites;
            float sum = 0f;
            for (int i = 0; i < s.Count; i++) if (Candidate(s[i], labels)) sum += W(Key(s[i]));
            if (sum <= 0f) return false;
            float pick = g.Next01() * sum;
            for (int i = 0; i < s.Count; i++)
            {
                if (!Candidate(s[i], labels)) continue;
                pick -= W(Key(s[i]));
                if (pick <= 0f) { x = s[i].x; y = s[i].y; key = Key(s[i]); return true; }
            }
            return false;
        }

        /// <summary>The thing of that kind that is nearest to where it was: so that a hand stays with what moves.</summary>
        bool Track(string key, ref float x, ref float y)
        {
            if (key.Length == 0) return false;
            var s = game.sprites; float best = 0.2f * 0.2f; bool found = false; float bx = x, by = y;
            for (int i = 0; i < s.Count; i++)
            {
                if (s[i].id < GlassScript.FirstId || Key(s[i]) != key) continue;
                float dx = s[i].x - x, dy = s[i].y - y, d = dx * dx + dy * dy;
                if (d < best) { best = d; bx = s[i].x; by = s[i].y; found = true; }
            }
            if (found) { x = bx; y = by; }
            return found;
        }

        void NewTouchTarget()
        {
            arrived = false; pressing = false; followKey = "";
            // a tool's keys are found by their words too; a game's words are not something to press
            bool labels = game.UsedKit || (game.ReadTaps && !game.ReadShots && !game.ReadHands);
            // (a board's empty cells are not drawn: half of a seeker's taps in a game of taps go to free places)
            bool drawn = seeker ? rng.Chance(game.UsedKit ? 0.97f : game.ReadTaps ? 0.5f : 0.92f) : rng.Chance(0.5f);
            if (drawn && Choose(rng, labels, out tx, out ty, out string key)) { if (seeker || rng.Chance(0.45f)) followKey = key; lastTouchKey = key; return; }
            lastTouchKey = "";
            tx = rng.Range(0.06f, game.W - 0.06f); ty = rng.Range(0.06f, 0.94f);
        }
        string lastTouchKey = "";

        void Touch(float dt)
        {
            bool touch = game.touch;
            float hover = touch ? 0.07f : 0.10f;
            if (away > 0f) { away -= dt; input.valid[0] = false; input.seen[0] = false; return; }
            if (!arrived && followKey.Length > 0) Track(followKey, ref tx, ref ty);
            float dx = tx - hx, dy = ty - hy, d = (float)Math.Sqrt(dx * dx + dy * dy), step = (seeker ? 1.3f : 1.6f) * dt;      // (a person reaches at about 0.4 m a second: the seeker is no machine)
            if (!arrived)
            {
                if (d <= step)
                {
                    hx = tx; hy = ty; arrived = true; wait = 0f; pressing = true; r.taps++;
                    act = touch ? 0.14f : 0.66f;
                    if (lastTouchKey.Length > 0) { lastKey = lastTouchKey; lastAt = r.seconds + (touch ? 0f : GlassScript.RestSeconds); }
                }
                else { hx += dx / d * step; hy += dy / d * step; }
            }
            else
            {
                wait += dt;
                if (wait >= act)
                {
                    pressing = false;
                    if (wait >= act + 0.18f) { if (!seeker && rng.Chance(0.06f)) away = 0.5f; NewTouchTarget(); }
                }
            }
            input.valid[0] = true; input.seen[0] = true; input.x[0] = hx; input.y[0] = hy; input.spread[0] = 1f; input.fingers[0] = 30;
            input.dist[0] = touch && pressing ? 0.004f : hover;
        }

        void Point(float dt)
        {
            input.fire[1] = false;
            bool on = aimKey.Length > 0 && Track(aimKey, ref atx, ref aty);
            float dx = atx - ax, dy = aty - ay, d = (float)Math.Sqrt(dx * dx + dy * dy), step = (seeker ? 1.5f : 2.4f) * dt;
            if (d > step) { ax += dx / d * step; ay += dy / d * step; }
            else
            {
                ax = atx; ay = aty;
                aimWait -= dt;
                if (aimWait <= 0f)
                {
                    // (a seeker whose target has gone does not waste the shot: it looks for the next)
                    if (!seeker || aimKey.Length == 0 || on)
                    {
                        input.fire[1] = true; r.shots++;
                        if (aimKey.Length > 0) { lastKey = aimKey; lastAt = r.seconds; }
                    }
                    aimWait = seeker ? aimRng.Range(0.45f, 0.85f) : aimRng.Range(0.25f, 0.7f);      // (steadying the finger, then the pinch: about one shot a second with the way there)
                    aimKey = "";
                    bool drawn = seeker ? aimRng.Chance(0.95f) : aimRng.Chance(0.4f);
                    if (!(drawn && Choose(aimRng, game.UsedKit, out atx, out aty, out aimKey)))
                    {
                        aimKey = ""; atx = aimRng.Range(0.06f, game.W - 0.06f); aty = aimRng.Range(0.06f, 0.94f);
                    }
                }
            }
            input.aimValid[1] = true; input.aimX[1] = ax; input.aimY[1] = ay;
        }
    }
}
