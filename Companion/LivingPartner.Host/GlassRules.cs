using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// The host's part in a glass game (Shared/Glass.cs), as plain rules - no host, network or clock in this file, so
/// that it is tested from this source. The host does not play: it decides WHICH pack, on WHICH side of the glass and
/// under which run number, passes on what the side that runs the simulation reports, and remembers the best score.
/// </summary>
public static class GlassRules
{
    public const string Pc = "pc", Mr = "mr";
    /// <summary>The ways of playing that are glass games (GlassState.game is one of these). "script": the game is a Lua program (GamePackDef.script, GlassScript) - what every game the partner makes is.</summary>
    public static readonly string[] Templates = { "survival", "umbrella", "script" };
    public static readonly string[] Phases = { "intro", "play", "choose", "over" };
    /// <summary>
    /// The phase the HOST gives a run while the partner goes through the screen: the game FOLLOWS the partner. The side
    /// that was running it stops stepping and drawing it (its reports are not taken any more); when the crossing has
    /// settled the same pack is started again on the other side under a new run number, with the score, level, lives
    /// and seconds of the run that was paused in the start state (Resume) - the side that starts applies them.
    /// </summary>
    public const string Moving = "moving";

    public static bool IsGlass(string template) => Array.IndexOf(Templates, template ?? "") >= 0;

    /// <summary>The pack of the makers' own that a way of playing is known by: what "뱀파이어 서바이벌 하자" means when asked by name, and what is offered from the marketplace when it is not in the bag.</summary>
    public static string PackOf(string template) => "";

    static string Flat(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// The way of playing that words name: "survival" for 뱀파이어 서바이벌 / Vampire Night, "umbrella" for 그림자 우산 /
    /// Shadow Umbrella - or the template's own name. "" when they name none.
    /// </summary>
    public static string TemplateNamed(string words)
    {
        string f = Flat(words);
        if (f.Length == 0) return "";
        if (f.Contains("뱀파이어") || f.Contains("서바이벌") || f.Contains("vampire") || f.Contains("survival") || f.Contains("survivor")) return "survival";
        if (f.Contains("우산") || f.Contains("umbrella")) return "umbrella";
        return "";
    }

    // ------------------------------------------------------------------ which game is meant
    /// <summary>What a wish for a game comes to (PickGame).</summary>
    public sealed class GamePick
    {
        /// <summary>The pack in the bag that is meant ("" = none).</summary>
        public string packId = "";
        /// <summary>How the headset starts it when it is a game of its own: tennis | keeper | stars for the makers' ball game, otherwise the pack's id. (A glass game - every script, so also rock paper scissors, Star Pop and Red Light since 2026-11-18 - is started by the host. A pack of those three that is still the old built-in way of playing is named rps | pop | redlight.)</summary>
        public string name = "";
        /// <summary>Nothing in the bag is meant, but the makers' catalogue has what was named: that module's id. The partner says it does not have it yet and asks whether to get it.</summary>
        public string offer = "";
        /// <summary>What decided, for the log.</summary>
        public string how = "nothing in the bag is named";
        public bool Found => packId.Length > 0;
        public override string ToString() => Found ? $"{packId} ({how})" : offer.Length > 0 ? $"not in the bag; the catalogue has {offer} ({how})" : how;
    }

    // the games that come from the makers, by the name the conversation and the headset use: the module each belongs to, and the words that name it
    static readonly (string name, string module, string[] words)[] Named =
    {
        ("tennis", StarterGames.TennisId, new[] { "테니스", "tennis" }),
        ("keeper", StarterGames.KeeperId, new[] { "골키퍼", "승부차기", "축구", "penalty", "shootout", "goalkeeper", "soccer" }),
        ("swarm", SwarmRules.PackId, new[] { "악마의문", "악마문", "디펜스", "demongate", "defense", "defence" }),
        ("gomoku", StarterGames.GomokuId, new[] { "오목", "gomoku", "fiveinarow" }),
    };

    // words of a sentence that ask for a game without naming one: they never pick a pack by being part of its title
    static readonly HashSet<string> Asking = new(StringComparer.Ordinal)
    {
        "좋아", "그래", "우리", "같이", "나랑", "너랑", "한판", "한번", "게임", "놀이", "하자", "해보자", "해", "해줘", "해봐", "보자", "할래", "할까", "하고", "싶어", "시작", "시작해", "시작하자", "켜줘", "틀어줘", "이제", "지금", "그거", "이거", "다시", "한", "판", "좀",
        "play", "lets", "let", "start", "game", "games", "the", "of", "some", "please", "round", "with", "me", "want", "wanna", "to", "can", "we", "you", "and", "now", "again", "that", "this", "one", "how", "about", "okay", "ok",
    };
    static readonly string[] Endings = { "해보자", "하자", "할래", "할까", "해줘", "해봐", "이랑", "으로", "을", "를", "이", "가", "은", "는", "도", "로", "랑", "나" };

    /// <summary>The words of a sentence that may name a game: no asking words, no particles at their ends, no call of the partner's name, two letters at least.</summary>
    static IEnumerable<string> NamingWords(string text)
    {
        foreach (string raw in System.Text.RegularExpressions.Regex.Split(text ?? "", @"[^\p{L}\p{Nd}]+"))
        {
            string w = raw.ToLowerInvariant();
            if (w.Length < 2 || Asking.Contains(w)) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(w, @"^(밤비|밤별|bambi|bambyeol)(야|아|이)?$")) continue;
            yield return w;
            foreach (string e in Endings)
                if (w.Length - e.Length >= 2 && w.EndsWith(e, StringComparison.Ordinal)) { string cut = w[..^e.Length]; if (!Asking.Contains(cut)) yield return cut; break; }
        }
    }

    static GamePick Picked(GamePackDef g, string how) => new GamePick
    {
        packId = g.id, how = how,
        name = IsGlass(g.template) ? g.id : g.template switch { "tennis" => "tennis", "keeper" => "keeper", _ => g.id },
    };

    /// <summary>
    /// The game a wish means - "오목 해보자", "뱀파이어 서바이벌 하자", "let's play Star Pop" - among the packs in the bag.
    /// game: the name of one of the makers' own games when the language model chose one ("" otherwise: rps | pop |
    /// redlight | keeper | stars | tennis | vampire | umbrella), thing: the words it
    /// gave for the game (a pack's title, usually; "" when it gave none), said: the sentence itself. In this order:
    ///   1. a pack by its id or its very title (in thing, then in the sentence);
    ///   2. a title inside the sentence (the longest), or a word of the sentence that a title begins with or mostly
    ///      consists of ("오목" for the user's own '오목놀이');
    ///   3. the kind of game the words name: survival / umbrella (the makers' own pack first), one of the makers'
    ///      other games by its name;
    ///   4. the model's own choice (game) - only when it gave no words of its own (thing is "").
    /// What is named but not in the bag comes back as `offer` (the makers' module of that kind). NEVER a pack of
    /// another kind because nothing matched: on 2026-11-17 "오목 해보자" started Vampire Night, because the model had
    /// put "vampire" into game next to the right title.
    /// </summary>
    public static GamePick PickGame(IEnumerable<GamePackDef> games, string game, string thing, string said)
    {
        var bag = (games ?? Enumerable.Empty<GamePackDef>()).Where(g => g != null && !string.IsNullOrEmpty(g.id) && !StarterGames.Retired(g.id)).ToList();
        string[] sources = { (thing ?? "").Trim(), (said ?? "").Trim() };
        GamePackDef ById(string id) => bag.FirstOrDefault(g => g.id == id);

        // 1. the id, the very title
        foreach (string src in sources)
        {
            if (src.Length == 0) continue;
            string f = Flat(src);
            var hit = ById(src) ?? (f.Length > 0 ? bag.FirstOrDefault(g => Flat(g.title) == f) : null);
            if (hit != null) return Picked(hit, hit.id == src ? "its id" : "its title");
        }
        // 2. a title in the sentence; a word of the sentence that is (most of) a title
        foreach (string src in sources)
        {
            string f = Flat(src);
            var hit = bag.Where(g => Flat(g.title).Length >= 3 && f.Contains(Flat(g.title))).OrderByDescending(g => Flat(g.title).Length).FirstOrDefault();
            if (hit != null) return Picked(hit, "its title in the sentence");
        }
        foreach (string src in sources)
        {
            GamePackDef best = null; double bestShare = 0;
            foreach (string w in NamingWords(src).Select(Flat).Where(w => w.Length >= 2))
                foreach (var g in bag)
                {
                    string t = Flat(g.title);
                    if (t.Length == 0 || !t.Contains(w)) continue;
                    double share = (double)w.Length / t.Length;
                    if (!(t.StartsWith(w, StringComparison.Ordinal) || share >= 0.4)) continue;
                    if (share > bestShare) { bestShare = share; best = g; }
                }
            if (best != null) return Picked(best, "a word of its title");
        }
        // 3. the kind of game that is named
        foreach (string src in sources)
        {
            if (src.Length == 0) continue;
            string f = Flat(src);
            foreach (var (name, module, words) in Named)
            {
                if (!(f == name || words.Any(w => f.Contains(w)))) continue;
                var pack = ById(module);
                if (pack == null) return new GamePick { offer = module, how = "the game " + name };
                var p = Picked(pack, "the game " + name);
                if (module == "game-tennis") p.name = name;
                return p;
            }
        }
        // 4. the model's own choice, when it gave no words of its own
        string chosen = (game ?? "").Trim().ToLowerInvariant();
        if (chosen.Length > 0 && sources[0].Length == 0)
            foreach (var (name, module, _) in Named)
            {
                if (name != chosen) continue;
                var pack = ById(module);
                if (pack == null) return new GamePick { offer = module, how = "the model chose " + name };
                var p = Picked(pack, "the model chose " + name);
                if (module == "game-tennis") p.name = name;
                return p;
            }
        return new GamePick { how = chosen.Length > 0 && sources[0].Length > 0 ? $"the words name no game in the bag (the model's '{chosen}' is not taken for them)" : "nothing in the bag is named" };
    }

    /// <summary>
    /// The installed glass game a request names - by the pack's id, by its title, by a word of its title, or by its
    /// way of playing (then the makers' own pack of that kind before any other). null: no installed glass game is meant.
    /// </summary>
    public static GamePackDef FindPack(IEnumerable<GamePackDef> games, string what)
    {
        var list = (games ?? Enumerable.Empty<GamePackDef>()).Where(g => g != null && IsGlass(g.template)).ToList();
        if (list.Count == 0) return null;
        var pick = PickGame(list, "", "", what);
        return pick.Found ? list.FirstOrDefault(g => g.id == pick.packId) : null;
    }

    /// <summary>
    /// Which side of the glass runs the game: inside ("pc") when the partner is in the PC, a PC client is connected
    /// and it is not on its way through a screen; otherwise out on the desk ("mr"), which needs a headset. "" when
    /// neither can be: nothing is started.
    /// </summary>
    public static string Side(string owner, bool hasPc, bool hasMr, bool crossing) =>
        owner == Env.Pc && hasPc && !crossing ? Pc : hasMr ? Mr : "";

    /// <summary>
    /// Why a pack that is in the bag is not started just now: "" (it is), "playing" (tennis is on - one game at a
    /// time - or this very game is running already) or "headset" (no side can run it: the partner is on the desk
    /// and no headset is connected). Another glass game that is running gives way to the new one.
    /// </summary>
    public static string StartProblem(GlassState cur, GamePackDef pack, bool tennisOn, string side)
    {
        if (tennisOn) return "playing";
        if (cur != null && cur.game.Length > 0 && cur.packId == pack.id && cur.phase != "over") return "playing";
        return string.IsNullOrEmpty(side) ? "headset" : "";
    }

    /// <summary>Does a client of this role run the simulation of a game on that side?</summary>
    public static bool Runs(string side, string role) => side == Pc ? role == Roles.Pc : side == Mr && (role == Roles.Quest || role == Roles.Sim);

    /// <summary>A new run of a pack: what the host decides, the rest as the game begins.</summary>
    public static void Begin(GlassState s, GamePackDef pack, string side, string monitorId, int best)
    {
        s.game = pack.template; s.packId = pack.id; s.title = pack.title ?? ""; s.side = side; s.monitorId = side == Pc ? monitorId ?? "" : "";
        s.runId++;
        s.phase = "intro"; s.score = 0; s.lives = Math.Max(1, pack.lives); s.level = 1; s.best = Math.Max(0, best); s.seconds = 0;
        s.choices = new string[0]; s.choiceNotes = new string[0]; s.banner = ""; s.result = "";
    }

    /// <summary>No game: everything but the run number is taken away (a late report of the old run then fits nothing).</summary>
    public static void Clear(GlassState s)
    {
        s.game = ""; s.packId = ""; s.title = ""; s.side = ""; s.monitorId = ""; s.phase = "";
        s.score = 0; s.lives = 0; s.level = 0; s.best = 0; s.seconds = 0;
        s.choices = new string[0]; s.choiceNotes = new string[0]; s.banner = ""; s.result = "";
    }

    /// <summary>Is this report taken? Only from the side that runs the game, only for the run that is on, and only while it has not ended.</summary>
    public static bool Accept(GlassState cur, GlassState report, string senderRole) =>
        cur != null && report != null && cur.game.Length > 0 && report.runId == cur.runId && Runs(cur.side, senderRole) && cur.phase != "over" && cur.phase != Moving && Array.IndexOf(Phases, report.phase ?? "") >= 0;

    // ------------------------------------------------------------------ the game follows the partner through the screen
    /// <summary>What a paused run takes along to the other side.</summary>
    public sealed class Carry
    {
        public string packId = "", side = "";
        public int score, lives, level;
        public float seconds;
    }

    public static string OtherSide(string side) => side == Pc ? Mr : side == Mr ? Pc : "";

    /// <summary>The partner starts through the screen while a game is on: the run is paused (phase "moving"). Returns what it takes along; null when there is nothing to pause (no game, or it is over).</summary>
    public static Carry Pause(GlassState s)
    {
        if (s == null || s.game.Length == 0 || s.phase == "over" || s.phase == Moving) return null;
        var c = new Carry { packId = s.packId, side = s.side, score = s.score, lives = s.lives, level = s.level, seconds = s.seconds };
        s.phase = Moving; s.choices = new string[0]; s.choiceNotes = new string[0]; s.banner = "";
        return c;
    }

    /// <summary>
    /// The crossing has settled: the same pack begins again on the side the partner is on now - a NEW run (runId + 1),
    /// phase "intro", and score / level / lives / seconds as they were when it was paused. (A run that is started
    /// fresh has score 0, level 1, the pack's lives and 0 seconds there: the side that starts a run applies these four
    /// after the game's own Start, always.)
    /// </summary>
    public static void Resume(GlassState s, GamePackDef pack, string side, string monitorId, int best, Carry c)
    {
        Begin(s, pack, side, monitorId, best);
        if (c == null) return;
        s.score = Math.Max(0, c.score); s.level = Math.Max(1, c.level); s.lives = Math.Max(1, c.lives);
        s.seconds = float.IsFinite(c.seconds) ? Math.Max(0, c.seconds) : 0;
    }

    /// <summary>
    /// After the crossing: "" = go on, on that side (side = where the partner is now and a client can run it); else why
    /// the game ends: the partner is where it was (the crossing failed or was cancelled) or nobody can run it there.
    /// </summary>
    public static string ResumeProblem(Carry c, string side) =>
        c == null ? "nothing was paused" : string.IsNullOrEmpty(side) ? "no side can run it where the partner is now" : side == c.side ? "the partner did not get through the screen" : "";

    // ------------------------------------------------------------------ through the glass (Shared/Glass.cs: GlassOut, GlassOutResult)
    /// <summary>glass.out is passed on to the headsets: only from the side that runs the game, only for the run that is on, only while it is played.</summary>
    public static bool OutTaken(GlassState cur, int runId, string senderRole) =>
        cur != null && cur.game.Length > 0 && runId == cur.runId && cur.phase != "over" && cur.phase != Moving && Runs(cur.side, senderRole);

    /// <summary>glass.out.result is passed on to the side that runs the game: only from a headset, only for the run that is on.</summary>
    public static bool OutResultTaken(GlassState cur, GlassOutResult r, string senderRole) =>
        cur != null && r != null && cur.game.Length > 0 && r.runId == cur.runId && cur.phase != Moving && (senderRole == Roles.Quest || senderRole == Roles.Sim) && r.what is "blocked" or "hit" or "missed";

    static string Cut(string s, int n) { s = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim(); return s.Length <= n ? s : s[..n]; }
    static string[] Cut(string[] a, int n) => (a ?? new string[0]).Take(3).Select(x => Cut(x, n)).ToArray();

    /// <summary>
    /// Takes over what the running side reports: phase, score, lives, level, seconds, choices, banner, result - never
    /// which game, which side or which run (those are the host's). Returns whether the phase changed.
    /// </summary>
    public static bool Merge(GlassState cur, GlassState report)
    {
        bool changed = cur.phase != report.phase;
        cur.phase = report.phase;
        cur.score = Math.Max(0, report.score); cur.lives = Math.Max(0, report.lives); cur.level = Math.Max(0, report.level);
        cur.seconds = float.IsFinite(report.seconds) ? Math.Max(0, report.seconds) : cur.seconds;
        cur.choices = Cut(report.choices, 40); cur.choiceNotes = Cut(report.choiceNotes, 80);
        cur.banner = Cut(report.banner, 80); cur.result = Cut(report.result, 160);
        return changed;
    }

    /// <summary>The others hear of a report at most this often ...</summary>
    public const double RelayGap = 0.125;
    /// <summary>... and at once when the phase changed.</summary>
    public static bool RelayNow(double lastRelay, double now, bool phaseChanged) => phaseChanged || now - lastRelay >= RelayGap;

    /// <summary>A hand report is passed on only while it is fresh (t = host time as the headset reckons it; 0 = not stamped).</summary>
    public static bool HandFresh(double t, double now) => t <= 0 || now - t <= 0.5;

    /// <summary>Remembers a score when it is the best for that pack. Returns whether it was.</summary>
    public static bool NewBest(IDictionary<string, int> best, string packId, int score)
    {
        if (best == null || string.IsNullOrEmpty(packId) || score <= 0) return false;
        if (best.TryGetValue(packId, out int had) && had >= score) return false;
        best[packId] = score;
        return true;
    }

    public static int Best(IDictionary<string, int> best, string packId) => best != null && !string.IsNullOrEmpty(packId) && best.TryGetValue(packId, out int b) ? b : 0;

    public static string StartLine(string title, bool korean) => korean ? $"좋아, {title} 하자!" : $"Okay, let's play {title}!";

    /// <summary>What the partner says when a game is over: the line the game itself gave, else one made from the score.</summary>
    public static string OverLine(GlassState s, bool korean, bool newBest)
    {
        if (!string.IsNullOrWhiteSpace(s.result)) return s.result.Trim();
        string best = newBest ? (korean ? " 최고 기록이야!" : " A new best!") : "";
        return korean ? $"끝! {s.score}점이야{(s.level > 1 ? $", 레벨 {s.level}까지 갔어" : "")}.{best}" : $"That's it! {s.score} points{(s.level > 1 ? $", up to level {s.level}" : "")}.{best}";
    }

    /// <summary>The state in one line, for lpctl status and the log.</summary>
    public static string Line(GlassState s) => s == null || s.game.Length == 0 ? "glass: -" :
        $"glass: {s.game} '{s.title}' ({s.packId}) side={s.side}{(s.monitorId.Length > 0 ? " on " + s.monitorId : "")} run={s.runId} phase={s.phase} score={s.score} lives={s.lives} level={s.level} best={s.best} t={s.seconds:0.0}s" +
        $"{(s.choices.Length > 0 ? " choices=[" + string.Join(" | ", s.choices) + "]" : "")}{(s.banner.Length > 0 ? $" banner='{s.banner}'" : "")}{(s.result.Length > 0 ? $" result='{s.result}'" : "")}";
}
