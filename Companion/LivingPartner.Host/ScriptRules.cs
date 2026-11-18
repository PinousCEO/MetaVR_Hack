using System.Globalization;
using System.Text;
using LivingPartner.Shared;
using MoonSharp.Interpreter;

namespace LivingPartner.Host;

// MODULES ARE SCRIPTS. A game or little tool is a Lua program on the glass stage (GamePackDef.script, run by
// GlassScript); what a thing does - and a skill - is a Lua program in the room (ItemDef.script / SkillDef.script, run
// by RoomScript). This file is the host's plain rules about them (no host, network or disk in it: the hub and the
// tests compile it on its own): how a script is CHECKED BY RUNNING IT and what the verdict means, what a scripted
// thing offers (so that the conversation can be told), and whether a module that arrives from somewhere else carries
// a script that runs at all. The WRITER - the language model that writes these programs on request - is
// ScriptWriter.cs; the API texts it is given are GlassScriptApi.Doc and RoomScriptApi.Doc (Shared).

/// <summary>Something a scripted thing offers: on_act(name), shown as a button with that label.</summary>
public sealed class ScriptOffer { public string name = "", label = ""; }

/// <summary>What a scripted thing can be asked to do, read from its script (no run: only its top level).</summary>
public sealed class ThingTricks
{
    public bool ok;                     // the script loads
    public bool wearable; public string slot = "";
    public List<ScriptOffer> offers = new();
    public bool Any => ok && (wearable || offers.Count > 0);
}

/// <summary>What running a script in the checker came to.</summary>
public sealed class ScriptVerdict
{
    /// <summary>It ran through the whole check without an error.</summary>
    public bool ran;
    /// <summary>ran, and it is not DEAD: a game that draws something, a thing with at least one hook. Only this may be installed when the partner made it.</summary>
    public bool ok;
    public string error = "";           // what broke ("" = nothing)
    public int errorLine;
    public string errorSource = "";
    /// <summary>"It does not really work": nothing moves, the hands are never read, it ends at once, a motion is never played, pressing does nothing. Sent back to the model up to three times.</summary>
    public List<string> serious = new();
    /// <summary>Worth one more try, and good enough to ship: small letters, a heavy frame, an unknown colour.</summary>
    public List<string> minor = new();
    /// <summary>The checker's own report: for the repair prompt and for the log.</summary>
    public string report = "";
    // a thing's script
    public bool wearable; public string slot = "";
    public List<ScriptOffer> offers = new();
    public List<string> motions = new(), hooks = new();
    /// <summary>How long on_use keeps the partner busy (seconds), and whether it goes on until it is stopped.</summary>
    public float useSeconds; public bool useEndless;

    /// <summary>3 = ok without a warning, 2 = ok with small ones, 1 = ok but something does not really work, 0 = not ok.</summary>
    public int Rank => !ok ? 0 : serious.Count > 0 ? 1 : minor.Count > 0 ? 2 : 3;
    public bool Clean => Rank == 3;
    public IEnumerable<string> Warnings => serious.Concat(minor);

    /// <summary>"line 12: attempt to call a nil value  |  foo(1)" - the error as one line.</summary>
    public string ErrorText => error.Length == 0 ? "" : (errorLine > 0 ? "line " + errorLine + ": " : "") + error + (errorSource.Length > 0 ? "  |  " + errorSource : "");

    /// <summary>The verdict as one line for the log.</summary>
    public string Line => !ran ? "ERROR " + ErrorText
        : (ok ? "ok" : "DEAD (" + error + ")") + (serious.Count + minor.Count == 0 ? ", no warnings" : $", {serious.Count} serious / {minor.Count} small warning(s): " + string.Join(" | ", Warnings.Select(w => WorldRules.Short(w, 90))));
}

public static class ScriptRules
{
    /// <summary>The template of a game that is a script.</summary>
    public const string Template = "script";
    /// <summary>A script travels inside its module (to the hub, in a .bambi file): 64 KB of text at the very most. (The sandbox itself reads far less: LuaSandbox.MaxSource.)</summary>
    public const int MaxScriptBytes = 64 * 1024;
    /// <summary>The pictures drawn for one scripted pack.</summary>
    public const int MaxArt = 3;
    public const int ArtNameMax = 24;
    /// <summary>How long (game / room seconds) a script the partner has just written is played in the check, and one that arrives from somewhere else (enough to see that it runs).</summary>
    public const float MadeGameSeconds = 40f, MadeRoomSeconds = 30f, ForeignSeconds = 10f;
    public static readonly string[] Slots = { "handR", "handL", "head", "back" };

    public static int Bytes(string script) => string.IsNullOrEmpty(script) ? 0 : Encoding.UTF8.GetByteCount(script);

    /// <summary>The lines of a script that are not empty.</summary>
    public static int Lines(string script)
    {
        if (string.IsNullOrWhiteSpace(script)) return 0;
        int n = 0;
        foreach (string line in script.Split('\n')) if (line.Trim().Length > 0) n++;
        return n;
    }

    /// <summary>"made of: a script (N lines)" - what the shop adds to a listing whose module is a script ("" when it is none).</summary>
    public static string MadeOf(string script) => Lines(script) is var n and > 0 ? $"made of: a script ({n} lines)" : "";

    /// <summary>
    /// The line under a listing's name as the shop shows it (140 characters at most), with what the module is made of
    /// when it is a script: "Five in a row wins. - made of: a script (87 lines)". lines = 0: the line as it is.
    /// </summary>
    public static string Summary(string summary, int lines, int max = 140)
    {
        if (lines <= 0) return WorldRules.Short(summary, max);
        string made = $"made of: a script ({lines} lines)";
        string head = WorldRules.Short(summary, max - made.Length - 3);
        return head.Length > 0 ? head + " - " + made : made;
    }

    /// <summary>The script a module is made of ("" = it is not a scripted one).</summary>
    public static string ScriptOf(MarketModule m) => m == null ? "" : m.game != null ? m.game.script ?? "" : m.item != null ? m.item.script ?? "" : m.skill != null ? m.skill.script ?? "" : "";

    /// <summary>A name a script can draw a picture by ("pack:" + name): small letters, digits, '-' and '_'.</summary>
    public static string ArtName(string name)
    {
        var sb = new StringBuilder();
        foreach (char raw in (name ?? "").Trim().ToLowerInvariant())
        {
            char c = raw == ' ' ? '-' : raw;
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_') sb.Append(c);
            if (sb.Length >= ArtNameMax) break;
        }
        return sb.ToString().Trim('-', '_');
    }

    // ------------------------------------------------------------------ a game: played by the checker's bot
    /// <summary>
    /// Plays a scripted game in the checker (GlassScriptCheck: once with a bot hand, once with nobody) and says what
    /// that means. art: the names of the pictures the pack has (or will have): pic() of any other name is noted.
    /// </summary>
    /// <summary>A game with lives must not be lost sooner than this when nobody plays.</summary>
    public const float MinSecondsAlone = 15f;

    public static ScriptVerdict CheckGame(string lua, IEnumerable<string> art = null, float seconds = MadeGameSeconds, int seed = 1)
    {
        var v = new ScriptVerdict();
        try
        {
            var pack = new GamePackDef { id = "game-check", template = Template, title = "check" };
            foreach (string name in art ?? Enumerable.Empty<string>()) if (!string.IsNullOrEmpty(name)) pack.art.Add(new PackArt { name = name });
            var r = GlassScriptCheck.Run(lua ?? "", pack, seconds, seed);
            v.ran = r.ok; v.error = r.error ?? ""; v.errorLine = r.errorLine; v.errorSource = r.errorSource ?? "";
            v.report = r.Summary();
            v.ok = r.ok && r.drew;
            if (r.ok && !r.drew) v.error = "nothing is ever drawn";
            // what "does not really work" is read from the facts of the play, not from the wording of a warning
            // (a shooter reads shots() / aim() and what comes back from the room, and nothing else: that IS the hands - until
            // 2026-11-18 this said "the hands are never read" of every game across the glass, and the repair it set off made them worse)
            bool noHands = !r.readHands && !r.readTaps && !r.readHeld && !r.readShots && !r.readAim && !r.readOutEvents;
            // (after an error the rest is only what the error left undone: it is in the report, not in the verdict)
            foreach (string w in r.ok ? r.warnings : new List<string>())
            {
                bool serious = w.Contains("nothing is drawn") || w.Contains("never changes") || w.Contains("never read") || w.Contains("nothing changes when") || w.Contains("ends within") || w.Contains("outside the field");
                (serious ? v.serious : v.minor).Add(w);
            }
            // (should the checker's wording change, the facts still say it)
            if (r.ok && v.serious.Count == 0)
            {
                if (!r.drew) v.serious.Add("nothing is drawn");
                else if (!r.pictureChanged) v.serious.Add("the picture never changes");
                if (noHands) v.serious.Add("the hands are never read: the player cannot do anything");
                else if (r.drew && !r.reacts) v.serious.Add("nothing changes when the hands play");
                if (r.finishedAlone && r.finishedAloneAt >= 0f && r.finishedAloneAt < 2f) v.serious.Add("it ends within 2 seconds without anyone playing");
            }
            // Everything a game SHOWS is English, whatever the user speaks (what Bambi SAYS may be theirs)
            if (r.ok && ShownKorean(lua) is { Length: > 0 } korean)
                v.serious.Add($"the program shows text that is not English ({korean}): everything on the screen - text, banner, finish, the labels of buttons, cards and displays - is ENGLISH, whatever language the user speaks");
            // A tool whose keys cannot be pressed one at a time is not a tool ("I asked for a calculator ... I cannot use it")
            if (r.ok && r.drew) v.serious.AddRange(Layout(lua, art, seed));
            // A game that takes lives must give the player time. The first game the partner made by voice ("풍선 터뜨리기",
            // 2026-11-17) passed every check and was over 8 seconds after it began when nobody touched it: five lives gone.
            if (r.ok && r.finishedAlone && r.finishedAloneAt >= 2f && r.finishedAloneAt < MinSecondsAlone && (lua ?? "").Contains("lives("))
                v.serious.Add($"it is lost after {r.finishedAloneAt:0} seconds when the player does nothing: far too fast. Nothing may cost a life in the first 5 seconds, and someone who does nothing must last at least {MinSecondsAlone:0} seconds (start slow and few, then speed up with the score or the time)");
        }
        catch (Exception e) { v.ran = false; v.ok = false; v.error = "the checker broke: " + e.GetType().Name + " " + e.Message; v.report = "ERROR: " + v.error; }
        return v;
    }

    static readonly System.Text.RegularExpressions.Regex LuaString = new("\"(?:[^\"\\\\\\n]|\\\\.)*\"|'(?:[^'\\\\\\n]|\\\\.)*'");

    /// <summary>
    /// The first piece of Korean text a game would put on the screen: a string of the program with Hangul in it, on
    /// a line that is not something Bambi says (pet_say: its bubble is speech, and speech may be the user's
    /// language). "" = none. Comments are not looked at.
    /// </summary>
    public static string ShownKorean(string lua)
    {
        foreach (string raw in (lua ?? "").Split('\n'))
        {
            string line = raw;
            if (!line.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3) || line.Contains("pet_say")) continue;
            foreach (System.Text.RegularExpressions.Match m in LuaString.Matches(line))
            {
                // (a comment that begins before this string hides it)
                int comment = line.IndexOf("--", StringComparison.Ordinal);
                if (comment >= 0 && comment < m.Index && !LuaString.Matches(line).Any(x => x.Index < comment && comment < x.Index + x.Length)) break;
                if (m.Value.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)) return WorldRules.Short(m.Value, 40);
            }
        }
        return "";
    }

    // ------------------------------------------------------------------ a tool's layout: where its keys really are
    /// <summary>A key or a field of the UI kit as the script asked for it in one frame (x, y is its middle).</summary>
    public readonly struct KitRect
    {
        public readonly string kind, label; public readonly float x, y, w, h;
        public KitRect(string kind, string label, float x, float y, float w, float h) { this.kind = kind; this.label = label; this.x = x; this.y = y; this.w = w; this.h = h; }
        public float Left => x - w / 2; public float Right => x + w / 2; public float Bottom => y - h / 2; public float Top => y + h / 2;
        public string Name => kind == "button" ? $"the button '{label.Trim()}'" : $"the display at ({N(x)}, {N(y)})";
    }

    static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>How far two keys may reach into each other before it is said (the kit's own outline is 0.006), and how far a key may reach over the edge of the field.</summary>
    public const float LayoutSlack = 0.012f, EdgeSlack = 0.006f;
    public const int MaxLayoutProblems = 4;

    /// <summary>
    /// "The calculator ... I cannot use it": the keys of a tool have to be where a finger can press ONE of them. The
    /// script is run for two seconds with nobody playing and every button() and display() it asks for is noted with
    /// its rectangle. A key that lies partly outside the field, two keys that overlap, a key on top of a display: each
    /// is something the player cannot use, and is said with its numbers so that the writer can repair it. (The checker
    /// in Shared plays the game; this looks at what a hand would find. Only the screen a tool opens with is seen.)
    /// And Bambi: at the end of those seconds it must not stand in front of the middle of a key (an upright tool
    /// sends it to a side where no key is).
    /// </summary>
    public static List<string> Layout(string lua, IEnumerable<string> art = null, int seed = 1, float seconds = 2f)
    {
        var problems = new List<string>();
        try
        {
            var pack = new GamePackDef { id = "game-check", template = GlassScript.TemplateName, title = "check", script = lua ?? "" };
            foreach (string name in art ?? Enumerable.Empty<string>()) if (!string.IsNullOrEmpty(name)) pack.art.Add(new PackArt { name = name });
            var game = new GlassScript { Strict = true };
            game.Start(pack, seed);
            if (game.Error.Length > 0 || game.Box == null) return problems;
            var frame = new List<KitRect>();
            foreach (string fn in new[] { "button", "display" })
            {
                DynValue original = game.Box.Get(fn);
                if (original.Type != DataType.ClrFunction) continue;
                string kind = fn;
                game.Box.Set(fn, DynValue.NewCallback((ctx, args) =>
                {
                    if (frame.Count < 200 && Asked(kind, args) is { } rect) frame.Add(rect);
                    return original.Callback.Invoke(ctx, args.GetArray());
                }, fn));
            }
            var nobody = new GlassInput();
            var said = new HashSet<string>();
            int frames = (int)Math.Ceiling(Math.Clamp(seconds, 0.1f, 10f) * 30f);
            for (int f = 0; f < frames && game.Error.Length == 0 && problems.Count < MaxLayoutProblems; f++)
            {
                frame.Clear();
                game.Step(1f / 30f, nobody);
                foreach (string problem in LayoutProblems(frame, game.W))
                    if (said.Add(problem) && problems.Count < MaxLayoutProblems) problems.Add(problem);
            }
            // (by now it has walked to where start() sent it; on a flat board it walks among the pieces, and that is the game)
            if (game.Error.Length == 0 && !game.flat && problems.Count < MaxLayoutProblems && Hidden(frame, game.petX, game.petY) is { } key)
                problems.Add($"Bambi stands in front of {key.Name} (Bambi is at x = {N(game.petX)}, 0.2 tall and about 0.12 wide; the key's middle is at ({N(key.x)}, {N(key.y)})): send Bambi with pet_goto to a side where no key is, or move the key");
        }
        catch (Exception) { }       // (what breaks in a script is the checker's to say, not this look at its keys)
        return problems;
    }

    /// <summary>The rectangle a call of button(label, x, y, w, h) or display(text, x, y, w, h) asks for (the label may also stand last); null when it is not one.</summary>
    static KitRect? Asked(string kind, CallbackArguments args)
    {
        if (args == null || args.Count < 5) return null;
        bool last = args[0].Type == DataType.Number && args[4].Type == DataType.String;
        int at = last ? 0 : 1;
        var n = new float[4];
        for (int i = 0; i < 4; i++)
        {
            if (args[at + i].Type != DataType.Number) return null;
            n[i] = (float)args[at + i].Number;
            if (!float.IsFinite(n[i])) return null;
        }
        if (!(n[2] > 0.02f) || !(n[3] > 0.02f)) return null;        // (the kit itself draws nothing that small)
        var l = args[last ? 4 : 0];
        string label = l.Type == DataType.String ? l.String : l.Type == DataType.Number ? l.Number.ToString(CultureInfo.InvariantCulture) : "";
        return new KitRect(kind, WorldRules.Short(label, 20), n[0], n[1], n[2], n[3]);
    }

    /// <summary>The key whose MIDDLE Bambi covers when it stands at (petX, petY) on an upright field - its label cannot be read; null when there is none.</summary>
    public static KitRect? Hidden(IReadOnlyList<KitRect> frame, float petX, float petY)
    {
        foreach (var r in frame)
            if (r.kind == "button" && Math.Abs(r.x - petX) < 0.06f && r.y > petY + 0.01f && r.y < petY + 0.19f) return r;
        return null;
    }

    /// <summary>What a hand would find wrong with the keys of one frame (pure: the tests give it rectangles).</summary>
    public static List<string> LayoutProblems(IReadOnlyList<KitRect> frame, float fieldW)
    {
        var problems = new List<string>();
        foreach (var r in frame)
        {
            if (r.Left >= -EdgeSlack && r.Right <= fieldW + EdgeSlack && r.Bottom >= -EdgeSlack && r.Top <= 1f + EdgeSlack) continue;
            string where = r.Bottom < -EdgeSlack ? $"its lower edge is at y = {N(r.Bottom)}" : r.Top > 1f + EdgeSlack ? $"its upper edge is at y = {N(r.Top)}" : r.Left < -EdgeSlack ? $"its left edge is at x = {N(r.Left)}" : $"its right edge is at x = {N(r.Right)}";
            problems.Add($"{r.Name} lies partly outside the field ({where}; the field is x 0..{N(fieldW)}, y 0..1): move it or make it smaller so that it is wholly inside");
        }
        for (int i = 0; i < frame.Count; i++)
            for (int k = i + 1; k < frame.Count; k++)
            {
                KitRect a = frame[i], b = frame[k];
                if (a.kind != "button" && b.kind != "button") continue;       // (two displays on each other are the picture's business; a key under something is the player's)
                float ox = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left), oy = Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom);
                if (ox <= LayoutSlack || oy <= LayoutSlack) continue;
                string how = $"{a.Name} is {N(a.w)} x {N(a.h)} at ({N(a.x)}, {N(a.y)}), {b.Name} is {N(b.w)} x {N(b.h)} at ({N(b.x)}, {N(b.y)}): they cover each other by {N(ox)} x {N(oy)}";
                problems.Add(a.kind == "button" && b.kind == "button"
                    ? $"two buttons overlap - {how}. Space rows by the buttons' height plus 0.02 and columns by their width plus 0.02"
                    : $"a button lies on top of a display - {how}. Give each its own place");
            }
        return problems;
    }

    // ------------------------------------------------------------------ a thing or a skill: used, worn, asked for its acts
    static readonly string[] RoomSerious =
    {
        "nothing that is seen or heard", "still going", "never played", "none of the hooks", "pressing the thing does nothing", "there is no on_act",
        "nothing is offered", "is not wearable", "the thing is not on", "still worn",
    };

    /// <summary>Runs the script of a thing (or of a skill: the item then only carries its id and name) through everything a user can do with it (RoomScriptCheck).</summary>
    public static ScriptVerdict CheckRoom(string lua, ItemDef item, float seconds = MadeRoomSeconds, int seed = 1)
    {
        var v = new ScriptVerdict();
        try
        {
            var r = RoomScriptCheck.Run(lua ?? "", ForCheck(item), seconds, seed);
            v.ran = r.ok; v.error = r.error ?? ""; v.errorLine = r.errorLine;
            v.errorSource = LineOf(lua, r.errorLine);
            v.report = r.Report();
            v.wearable = r.wearable; v.slot = r.slot ?? "";
            foreach (var o in r.offers) v.offers.Add(new ScriptOffer { name = o.name ?? "", label = o.label ?? "" });
            v.motions.AddRange(r.motions); v.hooks.AddRange(r.hooks);
            v.ok = r.ok && r.hooks.Count > 0;
            if (r.ok && r.hooks.Count == 0) v.error = "the script defines none of the hooks: the thing does nothing";
            foreach (string w in r.warnings) (RoomSerious.Any(s => w.Contains(s)) ? v.serious : v.minor).Add(w);
            var use = r.Phase("use");
            if (use != null) { v.useSeconds = use.seconds; v.useEndless = use.endless; }
        }
        catch (Exception e) { v.ran = false; v.ok = false; v.error = "the checker broke: " + e.GetType().Name + " " + e.Message; v.report = "ERROR: " + v.error; }
        return v;
    }

    /// <summary>The thing as the checker has it: standing on the desk a little in front of the home spot, not worn, whatever the real one is doing.</summary>
    static ItemDef ForCheck(ItemDef item)
    {
        item ??= new ItemDef { id = "thing", name = "thing", sizeM = 0.08f };
        return new ItemDef
        {
            id = string.IsNullOrEmpty(item.id) ? "thing" : item.id, name = item.name ?? "", kind = item.kind ?? "box", uses = item.uses ?? new string[0],
            sizeM = float.IsFinite(item.sizeM) && item.sizeM > 0 ? item.sizeM : 0.08f, inRoom = true, status = "ready", x = 0.10f, z = -0.07f, seat = item.seat, top = item.top,
        };
    }

    static string LineOf(string source, int line)
    {
        if (line < 1 || string.IsNullOrEmpty(source)) return "";
        string[] lines = source.Split('\n');
        return line <= lines.Length ? WorldRules.Short(lines[line - 1].Trim(), 140) : "";
    }

    /// <summary>
    /// What a scripted thing offers, read by loading its script (the top level only - offer(...) and wearable(...)
    /// stand there): cheap enough for every start of the host. Nothing is run beyond that.
    /// </summary>
    public static ThingTricks Tricks(ItemDef item)
    {
        var t = new ThingTricks();
        if (item == null || string.IsNullOrWhiteSpace(item.script)) return t;
        try
        {
            var rs = new RoomScript(item.script, ForCheck(item), null, 1);
            t.ok = rs.Ok;
            if (!rs.Ok) return t;
            t.wearable = rs.Wearable; t.slot = rs.WearSlot ?? "";
            foreach (var o in rs.Offers) t.offers.Add(new ScriptOffer { name = o.name ?? "", label = o.label ?? "" });
        }
        catch (Exception) { t.ok = false; }
        return t;
    }

    /// <summary>"wear (handR), acts: swing 'Swing!', bunt 'Bunt'" - what the conversation is told about a thing with tricks ("" = it has none).</summary>
    public static string TricksLine(ThingTricks t)
    {
        if (t == null || !t.Any) return "";
        var parts = new List<string>();
        if (t.wearable) parts.Add($"wear ({t.slot})");
        if (t.offers.Count > 0) parts.Add("acts: " + string.Join(", ", t.offers.Select(o => o.name + (o.label.Length > 0 && !string.Equals(o.label, o.name, StringComparison.OrdinalIgnoreCase) ? $" '{o.label.Replace("'", "")}'" : ""))));
        return string.Join(", ", parts);
    }

    /// <summary>The act of a thing that words name: its very name, its label, or either inside the sentence ("스윙 해 봐" -> swing when the label is "스윙!"). null when none.</summary>
    public static ScriptOffer FindAct(ThingTricks t, string words)
    {
        if (t == null || !t.ok || t.offers.Count == 0) return null;
        string f = Flat(words);
        if (f.Length == 0) return null;
        return t.offers.FirstOrDefault(o => Flat(o.name) == f || Flat(o.label) == f)
            ?? t.offers.Where(o => (Flat(o.name).Length >= 2 && f.Contains(Flat(o.name))) || (Flat(o.label).Length >= 2 && f.Contains(Flat(o.label)))).OrderByDescending(o => Math.Max(Flat(o.name).Length, Flat(o.label).Length)).FirstOrDefault();
    }

    static string Flat(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // ------------------------------------------------------------------ a module that arrives from somewhere else
    /// <summary>
    /// The script of a module that comes from outside (the hub, a .bambi file) - or is about to leave for there - is RUN
    /// before anything is done with it: "" when the module has no script or its script runs (warnings do not matter
    /// here: such a module is installed), otherwise one line that says what broke and where. Called by
    /// MarketPack.Sanitise, so the hub, a purchase and an import all refuse the same things.
    /// </summary>
    public static string Problem(MarketModule m)
    {
        if (m == null) return "";
        if (m.game != null && m.game.template == Template)
        {
            if (string.IsNullOrWhiteSpace(m.game.script)) return "the game is a script, and there is none in it";
            var v = CheckGame(m.game.script, (m.game.art ?? new List<PackArt>()).Where(a => a != null).Select(a => a.name), ForeignSeconds);
            return v.ran ? "" : "the game's script does not run: " + WorldRules.Short(v.ErrorText, 200);
        }
        if (m.item != null && !string.IsNullOrWhiteSpace(m.item.script))
        {
            var v = CheckRoom(m.item.script, m.item, ForeignSeconds);
            return v.ran ? "" : "the thing's script does not run: " + WorldRules.Short(v.ErrorText, 200);
        }
        if (m.skill != null && !string.IsNullOrWhiteSpace(m.skill.script))
        {
            var v = CheckRoom(m.skill.script, new ItemDef { id = m.skill.id ?? "skill", name = m.skill.name ?? "" }, ForeignSeconds);
            return v.ran ? "" : "the skill's script does not run: " + WorldRules.Short(v.ErrorText, 200);
        }
        return "";
    }

    /// <summary>Is this the line of a script that does not run (ScriptRules.Problem), as opposed to any other reason a module is refused?</summary>
    public static bool IsScriptProblem(string problem) => !string.IsNullOrEmpty(problem) && problem.Contains("script does not run");

    // ------------------------------------------------------------------ wearing
    /// <summary>
    /// The partner puts a thing on (world command "wear"). slot: handR | handL | head | back; "" = where the thing's
    /// script says. Only ONE thing per slot: what was worn there comes off (and is returned in `off`). A worn thing
    /// stays `inRoom` (it is out, on the partner, not in the bag) with wornOn set; it stands on nothing.
    /// Returns "" when it is worn now, otherwise why not: "no-script" | "not-wearable" | "bag" | "making".
    /// </summary>
    public static string Wear(IList<ItemDef> items, ItemDef item, string slot, ThingTricks tricks, out ItemDef off)
    {
        off = null;
        if (item == null) return "no-thing";
        if (item.status == "making") return "making";
        if (string.IsNullOrWhiteSpace(item.script) || tricks == null || !tricks.ok) return "no-script";
        if (!tricks.wearable) return "not-wearable";
        string where = Array.IndexOf(Slots, slot ?? "") >= 0 ? slot : tricks.slot;
        if (Array.IndexOf(Slots, where) < 0) where = "handR";
        foreach (var other in items ?? new List<ItemDef>())
            if (other != null && other != item && other.wornOn == where) { other.wornOn = ""; off = other; }
        item.inRoom = true; item.wornOn = where; item.y = 0f; item.onId = "";
        return "";
    }

    /// <summary>The partner takes a thing off: it stands on the desk again where it stood. False when it was not worn.</summary>
    public static bool Unwear(ItemDef item)
    {
        if (item == null || string.IsNullOrEmpty(item.wornOn)) return false;
        item.wornOn = "";
        return true;
    }

    // ------------------------------------------------------------------ what ships is a script too
    /// <summary>
    /// ONCE for a saved state (the flag is the caller's): every thing in the world that came from one of the makers'
    /// modules and has no script yet gets the script that module carries now, and so does every built-in skill.
    /// (Games need no migration: an installed game is brought up to date with its module at every start.)
    /// Returns what was done, for the log.
    /// </summary>
    public static List<string> SeedScripts(WorldState w, IEnumerable<MarketModule> shelf)
    {
        var notes = new List<string>();
        if (w == null || shelf == null) return notes;
        foreach (var m in shelf)
        {
            if (m == null || !m.builtin) continue;
            if (m.item != null && !string.IsNullOrWhiteSpace(m.item.script))
                foreach (var i in w.items ?? new List<ItemDef>())
                    if (i != null && i.id == m.id && string.IsNullOrWhiteSpace(i.script)) { i.script = m.item.script; notes.Add($"{i.id} '{i.name}' gets the script of its kind ({Lines(i.script)} lines)"); }
            if (m.skill != null && !string.IsNullOrWhiteSpace(ScriptOfSkill(m.skill)))
                foreach (var k in w.skills ?? new List<SkillDef>())
                    if (k != null && k.id == m.id && string.IsNullOrWhiteSpace(ScriptOfSkill(k))) { SetSkillScript(k, ScriptOfSkill(m.skill)); notes.Add($"{k.id} '{k.name}' gets its script ({Lines(ScriptOfSkill(k))} lines)"); }
        }
        return notes;
    }

    // (SkillDef.script: one place that names the field)
    public static string ScriptOfSkill(SkillDef k) => k?.script ?? "";
    public static void SetSkillScript(SkillDef k, string script) { if (k != null) k.script = script ?? ""; }
}
