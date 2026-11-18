using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>What the language model worked out about a thing the user gave the partner, after checking (WorldRules.Item).</summary>
public sealed class ItemIdea
{
    public string name = "", kind = "box", color = "", note = "", modelPrompt = "", iconPrompt = "";
    public string[] uses = new string[0];
    public float sizeM = 0.06f;
    public float realCm;        // the longest side of the real thing as the model put it (0 = it did not say): the sculptor is told, for the proportions
}

/// <summary>A thing's own shape as the language model sculpted it, after checking (WorldRules.Sculpture): parts to the common measure, and how high its seat / top is.</summary>
public sealed class Sculpture
{
    public List<ShapePart> parts = new List<ShapePart>();
    public float height = 1f, seat, top;
    /// <summary>The parts (by their place in the list) that touch nothing else of the thing: they would hang in the air.</summary>
    public List<int> loose = new List<int>();
}

/// <summary>
/// The rules a model-built record has to obey before it becomes part of the world. A language model fills in songs,
/// surroundings and skills (Maker.cs); what it hands back is a suggestion and is never trusted: every number is
/// clamped, every list is cut or filled to its length, every name is checked against the ones the headset knows, and
/// what cannot be repaired is dropped. The headset can therefore carry out anything it finds in a snapshot.
/// Nothing in here touches the host, the network or the disk (the tests compile this file on its own).
/// </summary>
public static class WorldRules
{
    public const int NoteMin = 36, NoteMax = 96, BpmMin = 60, BpmMax = 140, MaxSteps = 8, MaxPhrases = 8;
    /// <summary>How many things the bag holds and how many skills the user can teach (every snapshot carries all of them). The Maker keeps to these when it makes one, the marketplace when it installs or imports one.</summary>
    public const int MaxItems = 24, MaxSkills = 16;
    /// <summary>
    /// How large a thing may be, at the partner's scale (the partner is 0.10 m tall). Nothing is smaller than 0.05 m:
    /// a hammer of 3 cm could not be seen on the desk, let alone picked up (2026-11-17). What is MADE with words is at
    /// least MadeSizeMin (see MadeSize).
    /// </summary>
    public const float SizeMin = 0.05f, SizeMax = 0.22f, MadeSizeMin = 0.06f;
    public static readonly string[] Leads = { "bell", "pluck", "pad", "square", "soft" };
    // ("peak" and "coast" are whole views that take the place of the room outside the work area: a mountain top, the seaside)
    public static readonly string[] Patterns = { "none", "spots", "stars", "rain", "aurora", "snow", "petals", "bubbles", "peak", "coast" };
    /// <summary>The patterns that are a whole view (the room outside the work area is replaced, not dimmed): their dim is always 1.</summary>
    public static readonly string[] Views = { "peak", "coast" };
    public static readonly string[] Acts = { "music", "dance", "ambience", "activity", "emote", "say", "rain", "goto", "wait" };
    public static readonly string[] ActivityNames = { "study", "sit", "nap", "read", "play" };
    public static readonly string[] Emotes = { "wave", "happy", "think", "startle", "thankful" };

    /// <summary>
    /// A file of the world folder that may be written, deleted or handed out over HTTP: a plain name (letters, digits,
    /// '_', '-', '.'), not hidden, ending in .glb or .png. Nothing with a path in it ever passes.
    /// </summary>
    public static bool SafeFile(string name) => name != null && name.Length <= 80 && Regex.IsMatch(name, @"^[A-Za-z0-9_-][A-Za-z0-9_.-]*\.(glb|png)\z") && !name.Contains("..");

    // ------------------------------------------------------------------ what is typed to the partner at the PC
    /// <summary>A typed sentence as the conversation takes it: one line, at most 500 characters ("" = nothing to say).</summary>
    public static string TypedText(string text) => Short(text, 500);

    /// <summary>
    /// The connected client a sentence typed at the PC is carried out through, by its role: the headset first ("quest",
    /// else the simulator "sim") - what is asked for then happens in the room -, else the character on the PC ("pc"),
    /// else "" (nobody: the host answers on its own). A tool such as lpctl is never the one: it is not where the
    /// partner lives.
    /// </summary>
    public static string TypedThrough(IEnumerable<string> roles)
    {
        var have = new HashSet<string>(roles ?? Enumerable.Empty<string>());
        foreach (string r in new[] { Roles.Quest, Roles.Sim, Roles.Pc }) if (have.Contains(r)) return r;
        return "";
    }

    // ------------------------------------------------------------------ small readers
    /// <summary>A value as one line of text, at most max characters; fallback when it is missing or empty.</summary>
    public static string Text(JToken t, int max, string fallback = "")
    {
        string s = t is JValue v && v.Value != null ? Convert.ToString(v.Value, CultureInfo.InvariantCulture) : "";
        return Short(s, max, fallback);
    }

    /// <summary>One line, trimmed, at most max characters (never cut in the middle of a surrogate pair).</summary>
    public static string Short(string s, int max, string fallback = "")
    {
        s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        if (s.Length > max) { int n = max > 0 && char.IsHighSurrogate(s[max - 1]) ? max - 1 : max; s = s[..n].TrimEnd(); }
        return s.Length > 0 ? s : fallback;
    }

    /// <summary>A number, whether it came as a number or as text; fallback for anything else (and for NaN / infinity).</summary>
    public static float Number(JToken t, float fallback)
    {
        if (t is not JValue v || v.Value == null) return fallback;
        if (!double.TryParse(Convert.ToString(v.Value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return fallback;
        return double.IsNaN(d) || double.IsInfinity(d) ? fallback : (float)Math.Clamp(d, -1e9, 1e9);
    }

    public static bool Flag(JToken t)
    {
        if (t is not JValue v || v.Value == null) return false;
        if (v.Type == JTokenType.Boolean) return (bool)v;
        string s = Convert.ToString(v.Value, CultureInfo.InvariantCulture).Trim().ToLowerInvariant();
        return s is "true" or "yes" or "on" or "1";
    }

    /// <summary>"#RRGGBB" in capitals, from #RGB / RRGGBB / #RRGGBBAA as well; fallback for anything that is not a colour.</summary>
    public static string Colour(string s, string fallback)
    {
        s = (s ?? "").Trim().TrimStart('#');
        if (s.Length == 8) s = s[..6];                                      // alpha is not ours to use
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 || !s.All(Uri.IsHexDigit)) return fallback;
        return "#" + s.ToUpperInvariant();
    }

    // ------------------------------------------------------------------ a tune
    /// <summary>
    /// One note: 0 stays a rest, anything negative becomes -1 (hold), and a pitch outside 36..96 is moved by whole
    /// octaves into that range - the note keeps its name, so a tune written too high or too low still sounds right.
    /// </summary>
    public static int Note(int n)
    {
        if (n < 0) return -1;
        if (n == 0) return 0;
        if (n < NoteMin) n += 12 * ((NoteMin - n + 11) / 12);
        if (n > NoteMax) n -= 12 * ((n - NoteMax + 11) / 12);
        return n;
    }

    static List<int> Ints(JToken t)
    {
        var list = new List<int>();
        if (t is not JArray a) return list;
        foreach (var x in a)
        {
            if (list.Count >= 4096) break;      // nobody needs more; a runaway answer must not become a runaway snapshot
            list.Add((int)Math.Round(Number(x, 0)));
        }
        return list;
    }

    /// <summary>
    /// A melody or bass line of exactly `steps` sixteenth notes. Too long: cut. Too short: filled with rests to whole
    /// bars and repeated (that is what the headset would do with a short pattern; done here, it has nothing to guess).
    /// A hold with nothing before it to hold is a rest. Empty when there is not a single note in it.
    /// </summary>
    public static int[] Line(JToken t, int steps)
    {
        var src = Ints(t);
        if (src.Count > steps) src.RemoveRange(steps, src.Count - steps);
        if (!src.Any(n => n > 0)) return new int[0];
        while (src.Count % 16 != 0) src.Add(0);
        var line = new int[steps];
        int prev = 0;
        for (int i = 0; i < steps; i++)
        {
            int n = Note(src[i % src.Count]);
            if (n < 0 && prev == 0) n = 0;
            if (n >= 0) prev = n;
            line[i] = n;
        }
        return line;
    }

    /// <summary>
    /// One chord root per bar (0 = none, -1 = the chord before goes on, as the synth plays it); a shorter list repeats.
    /// Empty when no bar has a chord.
    /// </summary>
    public static int[] Chords(JToken t, int bars)
    {
        var src = Ints(t);
        if (src.Count > bars) src.RemoveRange(bars, src.Count - bars);
        if (!src.Any(n => n > 0)) return new int[0];
        var roots = new int[bars];
        int prev = 0;
        for (int i = 0; i < bars; i++)
        {
            int n = Note(src[i % src.Count]);
            if (n < 0 && prev == 0) n = 0;      // nothing before it to go on
            if (n >= 0) prev = n;
            roots[i] = n;
        }
        return roots;
    }

    /// <summary>
    /// A drum pattern: only 'x' (hit) and '-' (rest), 16 characters (one bar, repeated) or bars * 16. Spaces and bar
    /// lines are ignored, every other character is a rest. "" when it never hits.
    /// </summary>
    public static string Drums(string s, int bars)
    {
        var sb = new StringBuilder();
        foreach (char ch in s ?? "")
        {
            if (char.IsWhiteSpace(ch) || ch == '|') continue;
            sb.Append(ch is 'x' or 'X' ? 'x' : '-');
            if (sb.Length >= bars * 16) break;
        }
        if (sb.ToString().IndexOf('x') < 0) return "";
        int want = sb.Length <= 16 ? 16 : bars * 16;
        while (sb.Length < want) sb.Append('-');
        return sb.ToString();
    }

    /// <summary>A model-built tune, made safe to play. null when there is nothing in it to hear.</summary>
    public static SongDef Song(JToken j, string id)
    {
        if (j is not JObject o) return null;
        int bars = Number(o["bars"], 4) < 3 ? 2 : 4;
        string lead = Text(o["lead"], 12).ToLowerInvariant();
        var s = new SongDef
        {
            id = id ?? "", name = Text(o["name"], 24, "Tune"), bars = bars,
            bpm = (int)Math.Clamp(Math.Round(Number(o["bpm"], 100)), BpmMin, BpmMax),
            swing = Math.Clamp(Number(o["swing"], 0), 0f, 0.5f),
            lead = Array.IndexOf(Leads, lead) >= 0 ? lead : "bell",
            minor = Flag(o["minor"]),
            melody = Line(o["melody"], bars * 16), bass = Line(o["bass"], bars * 16), chords = Chords(o["chords"], bars),
            kick = Drums(Text(o["kick"], 400), bars), snare = Drums(Text(o["snare"], 400), bars), hat = Drums(Text(o["hat"], 400), bars),
            volume = Math.Clamp(Number(o["volume"], 0.8f), 0.3f, 1f),
        };
        bool heard = s.melody.Length > 0 || s.bass.Length > 0 || s.chords.Length > 0 || s.kick.Length > 0 || s.snare.Length > 0 || s.hat.Length > 0;
        return heard ? s : null;
    }

    // ------------------------------------------------------------------ surroundings
    /// <summary>Model-built surroundings, made safe to draw. null when there would be nothing to see.</summary>
    public static AmbienceDef Ambience(JToken j, string id)
    {
        if (j is not JObject o) return null;
        var d = new AmbienceDef();      // its own values are what a missing or broken field falls back to
        string pattern = Text(o["pattern"], 16).ToLowerInvariant();
        var a = new AmbienceDef
        {
            id = id ?? "", name = Text(o["name"], 24, "Mood"),
            // a pattern the headset has never heard of cannot be drawn: the colours and the dimming are kept without it
            pattern = Array.IndexOf(Patterns, pattern) >= 0 ? pattern : "none",
            c0 = Colour(Text(o["c0"], 12), d.c0), c1 = Colour(Text(o["c1"], 12), d.c1), c2 = Colour(Text(o["c2"], 12), d.c2),
            // (a whole view covers the room entirely: there is nothing to see through it)
            dim = Array.IndexOf(Views, pattern) >= 0 ? 1f : Math.Clamp(Number(o["dim"], d.dim), 0f, 1f),
            speed = Math.Clamp(Number(o["speed"], 1f), 0.1f, 3f),
            density = Math.Clamp(Number(o["density"], 1f), 0.1f, 3f),
            beat = Flag(o["beat"]),
        };
        return a.pattern == "none" && a.dim < 0.05f ? null : a;
    }

    // ------------------------------------------------------------------ a skill
    /// <summary>The id among `ids` that `arg` means: itself, or its short name ("lofi" for "song-lofi"). "" when none.</summary>
    static string Known(string arg, IEnumerable<string> ids, string prefix)
    {
        string a = (arg ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
        if (a.Length == 0 || ids == null) return "";
        foreach (string id in ids)
        {
            string i = (id ?? "").ToLowerInvariant().Replace("-", "");
            if (i.Length > 0 && (i == a || i == prefix + a)) return id;
        }
        return "";
    }

    /// <summary>
    /// A model-built skill, made safe to carry out. Steps whose act the headset does not know, or whose argument makes
    /// no sense, are dropped; a tune or surroundings that no step uses are dropped too. null when no step is left -
    /// then there is nothing to learn and the caller says so.
    /// songIds / ambienceIds: the tunes and surroundings that already exist (a step may name one instead of its own).
    /// </summary>
    public static SkillDef Skill(JToken j, string id, IList<ItemDef> items, IEnumerable<string> songIds, IEnumerable<string> ambienceIds)
    {
        if (j is not JObject o) return null;
        items ??= new List<ItemDef>();
        var s = new SkillDef { id = id ?? "", name = Text(o["name"], 20), note = Text(o["note"], 160), status = "ready" };
        var phrases = new List<string>();
        foreach (var p in o["phrases"] as JArray ?? new JArray())
        {
            string phrase = Text(p, 60);
            if (phrase.Length > 0 && !phrases.Contains(phrase, StringComparer.OrdinalIgnoreCase) && phrases.Count < MaxPhrases) phrases.Add(phrase);
        }
        s.phrases = phrases.ToArray();
        s.itemId = FindItem(items, Text(o["itemId"], 60))?.id ?? "";
        // "hasSong": false says the song object is only there because the answer's shape demands one
        var song = o["hasSong"] != null && !Flag(o["hasSong"]) ? null : Song(o["song"], "song-" + s.id);
        var ambience = o["hasAmbience"] != null && !Flag(o["hasAmbience"]) ? null : Ambience(o["ambience"], "amb-" + s.id);
        bool usesSong = false, usesAmbience = false;

        foreach (var st in o["steps"] as JArray ?? new JArray())
        {
            if (s.steps.Count >= MaxSteps) break;
            string act = Text(st["act"], 16).ToLowerInvariant(), arg = Text(st["arg"], 160), low = arg.ToLowerInvariant();
            float seconds = Math.Clamp(Number(st["seconds"], 0), 0f, 600f);
            switch (act)
            {
                case "music":
                    if (low is "stop" or "off") arg = "stop";
                    else
                    {
                        string named = Known(low, songIds, "song");
                        if (named.Length > 0) arg = named;
                        else if (song != null) { arg = ""; usesSong = true; }                 // "" or a name nobody knows: its own tune
                        else if ((arg = Known("song-lofi", songIds, "song")).Length == 0) continue;   // no tune of its own: the quiet built-in one, if there is one
                    }
                    break;
                case "ambience":
                    if (low is "clear" or "off" or "none") arg = "clear";
                    else
                    {
                        string named = Known(low, ambienceIds, "amb");
                        if (named.Length > 0) arg = named;
                        else if (ambience != null) { arg = ""; usesAmbience = true; }
                        else continue;
                    }
                    break;
                case "dance": arg = ""; break;
                case "activity": if (Array.IndexOf(ActivityNames, low) < 0) continue; arg = low; break;
                case "emote": arg = Array.IndexOf(Emotes, low) >= 0 ? low : "happy"; break;
                case "say": if (arg.Length == 0) continue; arg = Short(arg, 120); break;
                case "rain": if (low is "on" or "true" or "start") arg = "on"; else if (low is "off" or "false" or "stop") arg = "off"; else continue; break;
                case "goto":
                    if (low == "home") arg = "home";
                    else { var target = FindItem(items, arg); if (target == null) continue; arg = target.id; }
                    break;
                case "wait": arg = ""; if (seconds <= 0) seconds = 1; break;
                default: continue;        // an act that does not exist
            }
            s.steps.Add(new SkillStep { act = act, arg = arg, seconds = seconds });
        }
        if (s.steps.Count == 0) return null;
        s.song = usesSong ? song : null;
        s.ambience = usesAmbience ? ambience : null;
        if (s.song != null && s.song.name == "Tune" && s.name.Length > 0) s.song.name = s.name;
        if (s.ambience != null && s.ambience.name == "Mood" && s.name.Length > 0) s.ambience.name = s.name;
        return s;
    }

    // ------------------------------------------------------------------ a thing
    /// <summary>What the model understood of a thing, checked. guess: the built-in shape its description sounded like (what is used where the answer is unusable).</summary>
    public static ItemIdea Item(JToken j, ItemKinds.Kind guess)
    {
        guess ??= ItemKinds.Find("box");
        var o = j as JObject ?? new JObject();
        string kindId = Text(o["kind"], 16).ToLowerInvariant();
        var kind = ItemKinds.All.FirstOrDefault(k => k.id == kindId) ?? guess;
        var uses = new List<string>();
        foreach (var u in o["uses"] as JArray ?? new JArray())
        {
            string use = Text(u, 16).ToLowerInvariant();
            if (Array.IndexOf(ItemUses.All, use) >= 0 && !uses.Contains(use)) uses.Add(use);
        }
        if (uses.Count == 0) uses.AddRange(kind.uses);
        float cm = Number(o["realSizeCm"], 0);
        return new ItemIdea
        {
            name = Text(o["name"], 20), kind = kind.id, uses = uses.ToArray(),
            // the partner is a 10 cm figure, so its things are at 1:10 of the real ones - but never too small to see or too big for a desk
            sizeM = cm > 0 ? MadeSize(cm) : Math.Max(MadeSizeMin, kind.sizeM),
            realCm = cm > 0 ? Math.Min(cm, 100000f) : 0f,
            color = Colour(Text(o["colour"] ?? o["color"], 12), ""),
            note = Text(o["note"], 200), modelPrompt = Text(o["modelPrompt"], 500), iconPrompt = Text(o["iconPrompt"], 400),
        };
    }

    /// <summary>
    /// How large a thing MADE with words stands on the desk, from the longest side of the real thing in centimetres.
    /// Large things are at one tenth (a desk of 120 cm: 0.12 m, never more than SizeMax). Small ones are not: below
    /// 80 cm the size runs from MadeSizeMin (0.06 m) up to 0.08 m, so that a hammer (30 cm) comes out at about 0.07 m
    /// and a mug (10 cm) at 0.06 m - where one tenth would have made them 3 cm and 1 cm, too small to see.
    /// </summary>
    public static float MadeSize(float realCm)
    {
        if (!(realCm > 0f)) return MadeSizeMin;
        return realCm >= 80f ? Math.Min(realCm / 1000f, SizeMax) : MadeSizeMin + realCm / 4000f;
    }

    static readonly string[] HandTools = { "망치", "hammer", "드라이버", "screwdriver", "렌치", "wrench", "스패너", "spanner", "도끼", "axe", "삽", "shovel", "펜치", "pliers", "톱", "mallet" };
    /// <summary>Is this a tool that is held in the hand (a hammer, a wrench ...), by what it is called?</summary>
    public static bool HandTool(ItemDef i) => i != null && HandTools.Any(w => Flat(i.name).Contains(w) || Flat(i.asked).Contains(w));

    /// <summary>
    /// Things from before that are too small to see are made larger, at every start: under SizeMin (0.05 m) becomes
    /// SizeMin, and a hand tool the user had made (a hammer that came out at 3 cm) becomes 0.07 m. Returns one line
    /// for each thing that changed.
    /// </summary>
    public static List<string> GrowSmall(IList<ItemDef> items)
    {
        var notes = new List<string>();
        foreach (var i in items ?? new List<ItemDef>())
        {
            if (i == null) continue;
            float was = i.sizeM;
            if (!float.IsFinite(i.sizeM)) i.sizeM = SizeMin;
            else if (i.sizeM < SizeMin) i.sizeM = !string.IsNullOrEmpty(i.asked) && HandTool(i) ? 0.07f : SizeMin;
            else if (i.sizeM > SizeMax) i.sizeM = SizeMax;
            if (i.sizeM != was) notes.Add($"{i.id} '{i.name}' was {was:0.000} m: now {i.sizeM:0.000} m");
        }
        return notes;
    }

    // ------------------------------------------------------------------ things that stand on other things
    /// <summary>How high above the desk a thing may stand (ItemDef.y).</summary>
    public const float StackMaxY = 0.4f;

    /// <summary>The things that stand on that thing - directly, or on something that stands on it - and are out on the desk. Never the thing itself.</summary>
    public static List<ItemDef> Riders(IList<ItemDef> items, ItemDef under)
    {
        var list = new List<ItemDef>();
        if (items == null || under == null) return list;
        var ids = new HashSet<string> { under.id };
        for (bool more = true; more;)
        {
            more = false;
            foreach (var i in items)
                if (i != null && i != under && i.inRoom && !string.IsNullOrEmpty(i.onId) && ids.Contains(i.onId) && ids.Add(i.id)) { list.Add(i); more = true; }
        }
        return list;
    }

    /// <summary>
    /// The thing `item` may stand on when the headset says it was put on `onId`: another thing, out on the desk, that
    /// does not itself stand on `item` (directly or over others). null: it stands on the desk.
    /// </summary>
    public static ItemDef BaseFor(IList<ItemDef> items, ItemDef item, string onId)
    {
        if (items == null || item == null || string.IsNullOrEmpty(onId) || onId == item.id) return null;
        var under = items.FirstOrDefault(i => i != null && i.id == onId);
        if (under == null || !under.inRoom || under.status == "failed") return null;
        return Riders(items, item).Contains(under) ? null : under;
    }

    /// <summary>
    /// "place": the thing stands at x, z (clamped to the desk), turned by yaw, y metres above the desk on the thing
    /// onId - as the headset sent it, after checking: onId has to be a thing it can stand on (BaseFor), otherwise it
    /// stands on the desk (y = 0); y is between 0 and StackMaxY. What stands on the thing moves along with it.
    /// </summary>
    public static void PlaceAt(IList<ItemDef> items, ItemDef item, float x, float z, float yaw, float y, string onId)
    {
        if (item == null) return;
        var riders = item.inRoom ? Riders(items, item) : new List<ItemDef>();
        float ox = item.x, oz = item.z, oyaw = item.yaw, oy = item.y;
        var under = BaseFor(items, item, onId);
        item.inRoom = true;
        item.x = Math.Clamp(float.IsFinite(x) ? x : 0f, -1.5f, 1.5f); item.z = Math.Clamp(float.IsFinite(z) ? z : 0f, -1.5f, 1.5f); item.yaw = float.IsFinite(yaw) ? yaw % 360f : 0f;
        item.onId = under?.id ?? "";
        item.y = under != null && float.IsFinite(y) ? Math.Clamp(y, 0f, StackMaxY) : 0f;
        Carry(riders, item, ox, oz, oyaw, oy);
    }

    /// <summary>
    /// The thing `moved` stood at ox, oz, turned by oyaw, oy above the desk, and stands somewhere else now: what stood
    /// on it (riders, as they were before) goes along - the same way sideways and up, and turned about the middle of
    /// the thing by as much as the thing was turned.
    /// </summary>
    public static void Carry(List<ItemDef> riders, ItemDef moved, float ox, float oz, float oyaw, float oy)
    {
        if (riders == null || riders.Count == 0 || moved == null) return;
        float turn = moved.yaw - oyaw;
        double rad = turn * Math.PI / 180.0, cos = Math.Cos(rad), sin = Math.Sin(rad);
        foreach (var r in riders)
        {
            double dx = r.x - ox, dz = r.z - oz;
            r.x = Math.Clamp((float)(moved.x + dx * cos + dz * sin), -1.6f, 1.6f);
            r.z = Math.Clamp((float)(moved.z - dx * sin + dz * cos), -1.6f, 1.6f);
            r.yaw = (r.yaw + turn) % 360f;
            r.y = Math.Clamp(r.y + (moved.y - oy), 0f, 2f * StackMaxY);
        }
    }

    /// <summary>
    /// The thing is put away or thrown away: what stood on it comes down to the desk where it is (y = 0, onId = ""),
    /// and what stood on THAT comes down by as much and stays on it. Returns how many things came down.
    /// </summary>
    public static int Unstack(IList<ItemDef> items, ItemDef gone)
    {
        if (items == null || gone == null) return 0;
        int n = 0;
        foreach (var r in items.Where(i => i != null && i != gone && i.onId == gone.id).ToList())
        {
            float drop = r.y;
            foreach (var up in Riders(items, r)) up.y = Math.Max(0f, up.y - drop);
            r.y = 0f; r.onId = "";
            n++;
        }
        return n;
    }

    /// <summary>
    /// Puts the heights in order (at every start, and after things were taken out of the world by another way): a
    /// thing in the bag stands on nothing; a thing whose onId names nothing that is out on the desk - or itself, or
    /// a ring of things standing on each other - stands on the desk. Returns how many things were changed.
    /// </summary>
    public static int Settle(IList<ItemDef> items)
    {
        if (items == null) return 0;
        int n = 0;
        foreach (var i in items)
        {
            if (i == null) continue;
            i.onId ??= "";
            bool down = !i.inRoom || !float.IsFinite(i.y) || i.y < 0f;
            if (!down && i.onId.Length > 0)
            {
                // follow what it stands on down to the desk: every step has to be a thing that is out, and none may come twice
                var seen = new HashSet<string> { i.id };
                for (var at = i; !string.IsNullOrEmpty(at.onId);)
                {
                    var under = items.FirstOrDefault(x => x != null && x.id == at.onId);
                    if (under == null || !under.inRoom || !seen.Add(under.id)) { down = true; break; }
                    at = under;
                }
            }
            if (down) { if (i.y != 0f || i.onId.Length > 0) n++; i.y = 0f; i.onId = ""; }
            else if (i.y > 2f * StackMaxY) { i.y = 2f * StackMaxY; n++; }
        }
        return n;
    }

    // ------------------------------------------------------------------ "멈춰": what a plain stop ends
    /// <summary>
    /// What a plain "멈춰" / "그만" / "stop" ends - the first of these that is on, and only that:
    ///   glass    a glass game
    ///   tennis   the ball game the PC referees
    ///   cancel   a crossing through a screen or a task that is running (not when the user said "game")
    ///   game     the user said "game" and neither of the two above is on: a game of the headset's own, if there is
    ///            one - the headset is told; nothing else is touched
    ///   world    the activity, the skill, the tune, the surroundings (world "stop"); the headset is told as well,
    ///            because the host cannot see a game of the headset's own
    ///   local    nothing the host knows of is on, but a headset is there: it is told
    ///   nothing  nothing is on, and nobody to tell
    /// </summary>
    public static string StopWhat(bool glass, bool tennis, bool busy, bool saidGame, bool headset, bool world) =>
        glass ? "glass" : tennis ? "tennis" : busy && !saidGame ? "cancel" : saidGame ? (headset ? "game" : "nothing") : world ? "world" : headset ? "local" : "nothing";

    /// <summary>The one line the partner says about what a plain stop ended ("" for world: the Maker says its own).</summary>
    public static string StopLine(string what, bool saidGame, bool korean) => what switch
    {
        "glass" or "game" => korean ? "응, 게임 그만할게." : "Okay, I stopped the game.",
        "tennis" => korean ? "응, 테니스 그만할게." : "Okay, no more tennis.",
        "cancel" => korean ? "응, 그만둘게." : "Okay, I stopped.",
        "local" => korean ? "응, 그만할게." : "Okay, stopping.",
        "nothing" => saidGame ? (korean ? "지금은 게임 안 하고 있어." : "We are not playing right now.") : (korean ? "지금은 아무것도 안 하고 있어." : "I'm not doing anything right now."),
        _ => "",
    };

    // ------------------------------------------------------------------ a shape sculpted out of simple parts
    public const int PartsMin = 3;              // fewer is not a sculpture: the built-in shape of its kind looks better
    public const float PartThin = 0.02f;        // no side of a part is thinner than this much of the whole thing (the ink line would swallow it)
    const double TouchSlack = 0.015;            // parts this close to each other (the whole thing being 1) count as touching

    /// <summary>
    /// The storybook colours a sculpted thing is painted in. A part may name one instead of a hex value; a part whose
    /// colour cannot be read gets one of the first eight (the soft ones), by its place in the list.
    /// </summary>
    public static readonly (string name, string hex)[] Palette =
    {
        ("cream", "#FFF3D6"), ("honey", "#F2C14E"), ("sage", "#9BD3A0"), ("sky", "#BFD9F2"), ("rose", "#F4A9A0"), ("lavender", "#C9B8F0"), ("coral", "#FF9F6B"), ("wood", "#E9C99A"),
        ("walnut", "#B98B63"), ("cocoa", "#8C6A5D"), ("ink", "#4A2F27"), ("white", "#FFFFFF"), ("leaf", "#6DBE86"), ("berry", "#E8697D"), ("butter", "#FFE08A"), ("stone", "#C9CED6"), ("slate", "#7D8CA3"),
    };
    const int PaletteSoft = 8;

    /// <summary>
    /// A part's colour as "#RRGGBB": a hex value or the name of a palette colour, taken into the storybook range (see
    /// Storybook); anything else is a soft palette colour chosen by the part's place in the list.
    /// </summary>
    public static string PartColour(string given, int index)
    {
        string hex = Colour(given, "");
        if (hex.Length == 0)
        {
            string flat = Flat(given);
            foreach (var (name, h) in Palette) if (flat == name) return h;
            return Palette[(index % PaletteSoft + PaletteSoft) % PaletteSoft].hex;
        }
        return Storybook(hex);
    }

    /// <summary>
    /// A colour kept inside the picture-book range: black and near-black become the warm dark brown of the ink line
    /// (a toy piano is dark brown, never a hole in the picture), and a neon-pure colour is taken back to a strong
    /// pastel. Everything in between is left exactly as it was asked for.
    /// </summary>
    public static string Storybook(string hex)
    {
        hex = Colour(hex, "");
        if (hex.Length == 0) return Palette[0].hex;
        float r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255f, g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255f, b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255f;
        float max = Math.Max(r, Math.Max(g, b));
        float lift = Math.Clamp((0.32f - max) / 0.32f, 0f, 1f);
        r += (0.29f - r) * lift; g += (0.184f - g) * lift; b += (0.153f - b) * lift;
        max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b)), sat = max > 1e-4f ? (max - min) / max : 0f;
        if (sat > 0.7f)
        {
            float k = (0.7f + (sat - 0.7f) * 0.1f) / sat;        // (honey, the strongest colour of the palette, is at 0.68) the purest colour keeps 73% of its saturation
            r = max - (max - r) * k; g = max - (max - g) * k; b = max - (max - b) * k;
        }
        static int Byte(float v) => (int)Math.Clamp(Math.Round(v * 255f), 0, 255);
        return $"#{Byte(r):X2}{Byte(g):X2}{Byte(b):X2}";
    }

    /// <summary>The shape a word means ("cube" is a box, "ring" a torus); "" for one the headset cannot build.</summary>
    static string ShapeName(string s)
    {
        s = Flat(s);
        switch (s)
        {
            case "cube": case "cuboid": case "block": case "roundedbox": case "roundbox": return "box";
            case "ball": case "ellipsoid": case "egg": return "sphere";
            case "cyl": case "disc": case "disk": case "tube": return "cylinder";
            case "pill": case "rod": return "capsule";
            case "ring": case "donut": case "doughnut": return "torus";
            case "frustum": case "pyramid": return "cone";
        }
        return Array.IndexOf(ShapePart.Shapes, s) >= 0 ? s : "";
    }

    static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) < 1e6f;

    /// <summary>The part's turn as a matrix (rows): about z first, then x, then y - the order of Unity's Quaternion.Euler(rx, ry, rz).</summary>
    static double[] Turn(ShapePart p)
    {
        double x = p.rx * Math.PI / 180.0, y = p.ry * Math.PI / 180.0, z = p.rz * Math.PI / 180.0;
        double cx = Math.Cos(x), sx = Math.Sin(x), cy = Math.Cos(y), sy = Math.Sin(y), cz = Math.Cos(z), sz = Math.Sin(z);
        return new[]
        {
            cy * cz + sy * sx * sz, -cy * sz + sy * sx * cz, sy * cx,
            cx * sz, cx * cz, -sx,
            -sy * cz + cy * sx * sz, sy * sz + cy * sx * cz, cy * cx,
        };
    }

    /// <summary>
    /// How far the part reaches from its middle in the direction u (given in the part's own axes, length 1). These are
    /// the solids the headset builds (PropMeshes.BuildParts): the two have to agree, or a thing would hover or sink.
    /// </summary>
    static double Reach(ShapePart p, double ux, double uy, double uz)
    {
        double a = p.sx * 0.5, b = p.sy * 0.5, c = p.sz * 0.5;
        switch (p.shape)
        {
            case "sphere": return Math.Sqrt(a * a * ux * ux + b * b * uy * uy + c * c * uz * uz);
            case "cylinder":
            case "capsule":
            {
                // a disc, drawn out along y, with a rim of radius r rolled round it (the whole of it squeezed along z when it is oval)
                double r = (p.shape == "capsule" ? 1.0 : p.round) * Math.Min(a, b), k = a > 1e-9 ? c / a : 1.0;
                return Math.Sqrt((a - r) * (a - r) * ux * ux + (c - r * k) * (c - r * k) * uz * uz) + (b - r) * Math.Abs(uy) + r * Math.Sqrt(ux * ux + uy * uy + k * k * uz * uz);
            }
            case "cone":
            {
                double d = Math.Sqrt(a * a * ux * ux + c * c * uz * uz);
                return Math.Max(d - b * uy, p.round * d + b * uy);      // the base ring or the top ring, whichever is further out that way
            }
            case "torus": return Math.Sqrt((a - b) * (a - b) * ux * ux + (c - b) * (c - b) * uz * uz) + b;
            default:
            {
                double r = p.round * Math.Min(a, Math.Min(b, c));
                return Math.Abs(ux) * (a - r) + Math.Abs(uy) * (b - r) + Math.Abs(uz) * (c - r) + r;
            }
        }
    }

    /// <summary>The box (along the room's axes) that holds all the parts.</summary>
    static void Bounds(List<ShapePart> parts, double[] lo, double[] hi)
    {
        for (int i = 0; i < 3; i++) { lo[i] = double.MaxValue; hi[i] = double.MinValue; }
        foreach (var p in parts)
        {
            var m = Turn(p);
            double[] at = { p.x, p.y, p.z };
            for (int i = 0; i < 3; i++)
            {
                // the room's axis i, seen from the part: row i of its turn
                double ux = m[i * 3], uy = m[i * 3 + 1], uz = m[i * 3 + 2];
                hi[i] = Math.Max(hi[i], at[i] + Reach(p, ux, uy, uz));
                lo[i] = Math.Min(lo[i], at[i] - Reach(p, -ux, -uy, -uz));
            }
        }
    }

    static float Milli(double v) => (float)(Math.Round(v * 1000.0) / 1000.0);
    static float Angle(float deg) { deg %= 360f; if (deg > 180f) deg -= 360f; if (deg <= -180f) deg += 360f; return (float)(Math.Round(deg * 10.0) / 10.0); }

    /// <summary>Makes each number of a part one the headset can build from: its own shape's rules, nothing thinner than `thin`.</summary>
    static void Tidy(ShapePart p, float thin)
    {
        p.sx = Math.Max(p.sx, thin); p.sy = Math.Max(p.sy, thin); p.sz = Math.Max(p.sz, thin);
        float round = float.IsNaN(p.round) ? 0f : Math.Clamp(p.round, 0f, 1f);
        switch (p.shape)
        {
            // a toy has no knife edges: a box and a cylinder are always a little soft (which also lets the ink line run round the corner unbroken)
            case "box": case "cylinder": p.round = Math.Max(0.1f, round); break;
            case "cone": p.round = Math.Min(round, 0.95f); break;
            // the ring's own thickness: at most half its radius, or there is no hole left
            case "torus": p.sy = Math.Max(thin * 0.5f, Math.Min(p.sy, Math.Min(p.sx, p.sz) * 0.5f)); p.round = 0f; break;
            default: p.round = 0f; break;
        }
        p.round = (float)(Math.Round(p.round * 100.0) / 100.0);
        p.rx = Angle(p.rx); p.ry = Angle(p.ry); p.rz = Angle(p.rz);
    }

    public static List<ShapePart> ValidateParts(IList<ShapePart> parts, out float height)
    {
        float seat = 0, top = 0;
        return ValidateParts(parts, ref seat, ref top, out height);
    }

    /// <summary>
    /// The parts of a sculpted thing, made safe to build and brought to the common measure of every shape: parts that
    /// are no shape the headset knows, or that carry a number that is not one, are dropped; at most ShapePart.Max are
    /// kept (the first ones - the big forms come first); no side is thinner than PartThin of the whole; every colour is
    /// a storybook one; then the whole is scaled and moved so that its longest side is 1, it stands on y = 0 and its
    /// middle is over the origin. seat / top come in the measure the parts came in (heights above the ground they
    /// stood on) and go out in the common one, inside 0..height (0 = not given).
    /// null when fewer than PartsMin parts are left: that is no sculpture, and the caller keeps the built-in shape.
    /// </summary>
    public static List<ShapePart> ValidateParts(IList<ShapePart> parts, ref float seat, ref float top, out float height)
    {
        height = 0f;
        if (parts == null) return null;
        var list = new List<ShapePart>();
        foreach (var p in parts)
        {
            if (list.Count >= ShapePart.Max) break;
            if (p == null) continue;
            string shape = ShapeName(p.shape);
            if (shape.Length == 0) continue;
            if (!(Finite(p.x) && Finite(p.y) && Finite(p.z) && Finite(p.sx) && Finite(p.sy) && Finite(p.sz) && Finite(p.rx) && Finite(p.ry) && Finite(p.rz))) continue;
            list.Add(new ShapePart
            {
                shape = shape, x = p.x, y = p.y, z = p.z, sx = Math.Abs(p.sx), sy = Math.Abs(p.sy), sz = Math.Abs(p.sz), rx = p.rx, ry = p.ry, rz = p.rz,
                color = PartColour(p.color, list.Count), round = p.round,
            });
        }
        if (list.Count < PartsMin) return null;

        // how big the whole is as it came (in whatever unit the sculptor worked): the thinnest allowed side is measured against that
        double[] lo = new double[3], hi = new double[3];
        foreach (var p in list) Tidy(p, 0f);
        Bounds(list, lo, hi);
        double whole = Math.Max(hi[0] - lo[0], Math.Max(hi[1] - lo[1], hi[2] - lo[2]));
        if (!(whole > 1e-6) || whole > 1e7) return null;
        foreach (var p in list) Tidy(p, (float)(whole * PartThin));
        Bounds(list, lo, hi);
        whole = Math.Max(hi[0] - lo[0], Math.Max(hi[1] - lo[1], hi[2] - lo[2]));

        double k = 1.0 / whole, midX = (lo[0] + hi[0]) * 0.5, midZ = (lo[2] + hi[2]) * 0.5, ground = lo[1];
        foreach (var p in list)
        {
            p.x = Milli((p.x - midX) * k); p.y = Milli((p.y - ground) * k); p.z = Milli((p.z - midZ) * k);
            p.sx = Milli(p.sx * k); p.sy = Milli(p.sy * k); p.sz = Milli(p.sz * k);
        }
        height = Milli((hi[1] - lo[1]) * k);
        seat = Level(seat, ground, k, height); top = Level(top, ground, k, height);
        return list;
    }

    /// <summary>A height above the ground in the sculptor's measure, in the common one; 0 (not given) stays 0.</summary>
    static float Level(float given, double ground, double k, float height)
    {
        if (!Finite(given) || given <= 0f) return 0f;
        double v = (given - ground) * k;
        return v <= 0.0 ? 0f : Milli(Math.Min(v, height));
    }

    /// <summary>
    /// Whether two parts touch (or nearly: within `slack`). Each is taken as the box that holds it, turned as it is
    /// turned - generous for a ball or a ring, so two parts are called apart only when they clearly are.
    /// </summary>
    static bool Touch(ShapePart p, double[] mp, ShapePart q, double[] mq, double slack)
    {
        double[] a = { p.sx * 0.5 + slack, p.sy * 0.5 + slack, p.sz * 0.5 + slack }, b = { q.sx * 0.5, q.sy * 0.5, q.sz * 0.5 };
        double[] d = { q.x - p.x, q.y - p.y, q.z - p.z }, t = new double[3];
        var r = new double[3, 3]; var ar = new double[3, 3];
        // q's axes and the way to q, as p sees them (a part's own axis i is column i of its turn)
        for (int i = 0; i < 3; i++)
        {
            t[i] = d[0] * mp[i] + d[1] * mp[3 + i] + d[2] * mp[6 + i];
            for (int j = 0; j < 3; j++)
            {
                r[i, j] = mp[i] * mq[j] + mp[3 + i] * mq[3 + j] + mp[6 + i] * mq[6 + j];
                ar[i, j] = Math.Abs(r[i, j]) + 1e-7;
            }
        }
        // a plane between two boxes, if there is one, is square to a side of either or to an edge of each: fifteen places to look
        for (int i = 0; i < 3; i++) if (Math.Abs(t[i]) > a[i] + b[0] * ar[i, 0] + b[1] * ar[i, 1] + b[2] * ar[i, 2]) return false;
        for (int j = 0; j < 3; j++) if (Math.Abs(t[0] * r[0, j] + t[1] * r[1, j] + t[2] * r[2, j]) > a[0] * ar[0, j] + a[1] * ar[1, j] + a[2] * ar[2, j] + b[j]) return false;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                int i1 = (i + 1) % 3, i2 = (i + 2) % 3, j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                double ra = a[i1] * ar[i2, j] + a[i2] * ar[i1, j], rb = b[j1] * ar[i, j2] + b[j2] * ar[i, j1];
                if (Math.Abs(t[i2] * r[i1, j] - t[i1] * r[i2, j]) > ra + rb) return false;
            }
        return true;
    }

    /// <summary>
    /// The parts that hang in the air: those that are not joined, part touching part, to the main body of the thing
    /// (the joined group with the most volume). Empty when everything holds together. For parts in the common measure.
    /// </summary>
    public static List<int> LooseParts(IList<ShapePart> parts)
    {
        var loose = new List<int>();
        if (parts == null || parts.Count < 2) return loose;
        var group = Groups(parts, out int main);
        for (int i = 0; i < parts.Count; i++) if (group[i] != main) loose.Add(i);
        return loose;
    }

    /// <summary>Which joined group each part belongs to (a group is named by one of its parts), and which group is the main body.</summary>
    static int[] Groups(IList<ShapePart> parts, out int main)
    {
        int n = parts.Count;
        var turns = new double[n][]; var group = new int[n];
        for (int i = 0; i < n; i++) { turns[i] = Turn(parts[i]); group[i] = i; }
        int Root(int i) { while (group[i] != i) i = group[i] = group[group[i]]; return i; }
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Root(i) != Root(j) && Touch(parts[i], turns[i], parts[j], turns[j], TouchSlack)) group[Root(j)] = Root(i);
        var volume = new double[n];
        for (int i = 0; i < n; i++) { group[i] = Root(i); volume[group[i]] += (double)parts[i].sx * parts[i].sy * parts[i].sz; }
        main = 0;
        for (int i = 1; i < n; i++) if (volume[i] > volume[main]) main = i;
        return group;
    }

    /// <summary>
    /// Brings what hangs in the air to the thing: each loose group of parts is moved, as it is, straight towards the
    /// nearest part of the main body until the two touch (the language model sees a thing in numbers only, and now and
    /// then misses by a few hundredths - or by a lot). Returns how many parts were moved; the caller brings the whole
    /// to the common measure again (ValidateParts), because its outline may have changed.
    /// </summary>
    public static int PullTogether(List<ShapePart> parts)
    {
        int moved = 0;
        if (parts == null) return 0;
        for (int pass = 0; pass < parts.Count; pass++)
        {
            var group = Groups(parts, out int main);
            int loose = Array.FindIndex(group, g => g != main);
            if (loose < 0) break;
            int from = -1, to = -1; double best = double.MaxValue;
            for (int i = 0; i < parts.Count; i++)
            {
                if (group[i] != group[loose]) continue;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (group[j] != main) continue;
                    double dx = parts[j].x - parts[i].x, dy = parts[j].y - parts[i].y, dz = parts[j].z - parts[i].z, d = dx * dx + dy * dy + dz * dz;
                    if (d < best) { best = d; from = i; to = j; }
                }
            }
            if (from < 0) break;
            // the shortest way along the line between the two middles that makes them touch (at the end of it they sit in each other)
            var p = parts[from]; var q = parts[to];
            float x0 = p.x, y0 = p.y, z0 = p.z, wx = q.x - p.x, wy = q.y - p.y, wz = q.z - p.z;
            double[] mp = Turn(p), mq = Turn(q);
            double lo = 0, hi = 1;
            for (int step = 0; step < 16; step++)
            {
                double f = (lo + hi) * 0.5;
                p.x = (float)(x0 + wx * f); p.y = (float)(y0 + wy * f); p.z = (float)(z0 + wz * f);
                if (Touch(p, mp, q, mq, 0.0)) hi = f; else lo = f;
            }
            p.x = x0; p.y = y0; p.z = z0;
            foreach (int i in Enumerable.Range(0, parts.Count).Where(i => group[i] == group[loose]))
            {
                parts[i].x += (float)(wx * hi); parts[i].y += (float)(wy * hi); parts[i].z += (float)(wz * hi);
                moved++;
            }
        }
        return moved;
    }

    /// <summary>
    /// What the model sculpted ({ parts: [...], seat, top }), checked: see ValidateParts. null, with the reason, when
    /// it is not a usable sculpture. Parts that hang in the air are listed in `loose` and left in: whether to ask
    /// again or to live with them is the caller's decision.
    /// </summary>
    public static Sculpture Sculpt(JToken j, out string why)
    {
        why = "";
        if (j is not JObject o || o["parts"] is not JArray arr) { why = "the answer has no list of parts"; return null; }
        var raw = new List<ShapePart>();
        foreach (var t in arr)
        {
            if (raw.Count >= 200) break;        // a runaway answer: ValidateParts keeps the first ShapePart.Max that are usable
            if (t is not JObject q) continue;
            raw.Add(new ShapePart
            {
                shape = Text(q["shape"], 16),
                x = Number(q["x"], float.NaN), y = Number(q["y"], float.NaN), z = Number(q["z"], float.NaN),
                sx = Number(q["sx"], float.NaN), sy = Number(q["sy"], float.NaN), sz = Number(q["sz"], float.NaN),
                rx = Number(q["rx"], 0f), ry = Number(q["ry"], 0f), rz = Number(q["rz"], 0f),
                color = Text(q["color"] ?? q["colour"], 16), round = Number(q["round"], 0f),
            });
        }
        float seat = Number(o["seat"], 0f), top = Number(o["top"], 0f);
        var parts = ValidateParts(raw, ref seat, ref top, out float height);
        if (parts == null) { why = $"only {raw.Count(p => ShapeName(p.shape).Length > 0)} of {arr.Count} parts can be built (at least {PartsMin} are needed)"; return null; }
        return new Sculpture { parts = parts, height = height, seat = seat, top = top, loose = LooseParts(parts) };
    }

    /// <summary>Brings the loose parts of a sculpture in (PullTogether) and the whole to the common measure again. Returns how many parts were moved.</summary>
    public static int Join(Sculpture s)
    {
        if (s == null || s.loose.Count == 0) return 0;
        int moved = PullTogether(s.parts);
        var parts = ValidateParts(s.parts, ref s.seat, ref s.top, out float height);
        if (parts != null) { s.parts = parts; s.height = height; }
        s.loose = LooseParts(s.parts);
        return moved;
    }

    /// <summary>
    /// Worked examples the sculptor is shown (Sculptor.cs), each the whole answer as it should come back. They are
    /// kept here, as data, so that the tests can hold them to the rules they teach: every part touching, the longest
    /// side 1, standing on the ground.
    /// </summary>
    public static readonly (string what, string answer)[] SculptExamples =
    {
        ("a small chair, rose-coloured seat and back, wooden legs",
"""
{"plan":"Silhouette: a seat slab on four thick legs with a tall back, 1.0 high and 0.62 wide. Big forms: seat 0.62 x 0.10 x 0.60 at y 0.46; four legs 0.09 thick under its corners; two back posts rising from the back edge to y 1.0 with a soft back rest between them; a flat cream cushion on the seat. The back is on the +z side, so the chair faces the viewer.",
"parts":[
{"name":"seat","shape":"box","x":0,"y":0.46,"z":0,"sx":0.62,"sy":0.10,"sz":0.60,"rx":0,"ry":0,"rz":0,"round":0.4,"color":"#F4A9A0"},
{"name":"back rest","shape":"box","x":0,"y":0.80,"z":0.25,"sx":0.52,"sy":0.30,"sz":0.06,"rx":0,"ry":0,"rz":0,"round":0.5,"color":"#F4A9A0"},
{"name":"back post left","shape":"box","x":-0.24,"y":0.74,"z":0.25,"sx":0.09,"sy":0.52,"sz":0.08,"rx":0,"ry":0,"rz":0,"round":0.3,"color":"#B98B63"},
{"name":"back post right","shape":"box","x":0.24,"y":0.74,"z":0.25,"sx":0.09,"sy":0.52,"sz":0.08,"rx":0,"ry":0,"rz":0,"round":0.3,"color":"#B98B63"},
{"name":"leg front left","shape":"box","x":-0.24,"y":0.215,"z":-0.23,"sx":0.09,"sy":0.43,"sz":0.09,"rx":0,"ry":0,"rz":0,"round":0.3,"color":"#B98B63"},
{"name":"leg front right","shape":"box","x":0.24,"y":0.215,"z":-0.23,"sx":0.09,"sy":0.43,"sz":0.09,"rx":0,"ry":0,"rz":0,"round":0.3,"color":"#B98B63"},
{"name":"leg back left","shape":"box","x":-0.24,"y":0.215,"z":0.23,"sx":0.09,"sy":0.43,"sz":0.09,"rx":0,"ry":0,"rz":0,"round":0.3,"color":"#B98B63"},
{"name":"leg back right","shape":"box","x":0.24,"y":0.215,"z":0.23,"sx":0.09,"sy":0.43,"sz":0.09,"rx":0,"ry":0,"rz":0,"round":0.3,"color":"#B98B63"},
{"name":"cushion","shape":"sphere","x":0,"y":0.52,"z":-0.02,"sx":0.50,"sy":0.09,"sz":0.48,"rx":0,"ry":0,"rz":0,"round":0,"color":"#FFF3D6"}
],"seat":0.56,"top":0}
"""),
        ("a table lamp with a butter-yellow shade",
"""
{"plan":"Silhouette: a wide shade on a thin stem on a round foot, 1.0 high, 0.66 wide. Big forms: foot disc 0.44 across; stem 0.07 thick from the foot up into the shade; the shade is a blunt cone 0.64 wide from y 0.57 to 0.97 with a coral ring round its lower edge; a cream bulb peeks out under it; a small knob on top.",
"parts":[
{"name":"shade","shape":"cone","x":0,"y":0.77,"z":0,"sx":0.64,"sy":0.40,"sz":0.64,"rx":0,"ry":0,"rz":0,"round":0.5,"color":"#FFE08A"},
{"name":"foot","shape":"cylinder","x":0,"y":0.04,"z":0,"sx":0.44,"sy":0.08,"sz":0.44,"rx":0,"ry":0,"rz":0,"round":0.5,"color":"#B98B63"},
{"name":"stem","shape":"cylinder","x":0,"y":0.35,"z":0,"sx":0.07,"sy":0.58,"sz":0.07,"rx":0,"ry":0,"rz":0,"round":0.2,"color":"#E9C99A"},
{"name":"shade ring","shape":"torus","x":0,"y":0.59,"z":0,"sx":0.66,"sy":0.05,"sz":0.66,"rx":0,"ry":0,"rz":0,"round":0,"color":"#FF9F6B"},
{"name":"bulb","shape":"sphere","x":0,"y":0.58,"z":0,"sx":0.18,"sy":0.18,"sz":0.18,"rx":0,"ry":0,"rz":0,"round":0,"color":"#FFF3D6"},
{"name":"collar","shape":"sphere","x":0,"y":0.11,"z":0,"sx":0.15,"sy":0.12,"sz":0.15,"rx":0,"ry":0,"rz":0,"round":0,"color":"#F2C14E"},
{"name":"knob","shape":"sphere","x":0,"y":0.975,"z":0,"sx":0.07,"sy":0.05,"sz":0.07,"rx":0,"ry":0,"rz":0,"round":0,"color":"#FF9F6B"}
],"seat":0,"top":0}
"""),
        ("a potted plant",
"""
{"plan":"Silhouette: a round leafy crown over a flower pot, 1.0 high, 0.57 wide. Big forms: the pot is a blunt cone turned upside down (rx 180: wide end up), 0.46 wide and 0.34 high, with a thicker rim and dark soil showing; a green stem; an egg-shaped crown 0.40 wide on top; two leaves, mirrored, each an oval that starts inside the stem and points up and outwards (rz -30 on the left, +30 on the right); one small pink bud half sunk into the front of the crown.",
"parts":[
{"name":"crown","shape":"sphere","x":0,"y":0.82,"z":0,"sx":0.40,"sy":0.36,"sz":0.36,"rx":0,"ry":0,"rz":0,"round":0,"color":"#9BD3A0"},
{"name":"pot","shape":"cone","x":0,"y":0.17,"z":0,"sx":0.46,"sy":0.34,"sz":0.46,"rx":180,"ry":0,"rz":0,"round":0.7,"color":"#FF9F6B"},
{"name":"pot rim","shape":"cylinder","x":0,"y":0.32,"z":0,"sx":0.52,"sy":0.08,"sz":0.52,"rx":0,"ry":0,"rz":0,"round":0.5,"color":"#F4A9A0"},
{"name":"soil","shape":"cylinder","x":0,"y":0.355,"z":0,"sx":0.44,"sy":0.04,"sz":0.44,"rx":0,"ry":0,"rz":0,"round":0.3,"color":"#8C6A5D"},
{"name":"stem","shape":"capsule","x":0,"y":0.54,"z":0,"sx":0.06,"sy":0.38,"sz":0.06,"rx":0,"ry":0,"rz":0,"round":0,"color":"#6DBE86"},
{"name":"leaf left","shape":"sphere","x":-0.15,"y":0.58,"z":0,"sx":0.30,"sy":0.13,"sz":0.20,"rx":0,"ry":0,"rz":-30,"round":0,"color":"#6DBE86"},
{"name":"leaf right","shape":"sphere","x":0.15,"y":0.58,"z":0,"sx":0.30,"sy":0.13,"sz":0.20,"rx":0,"ry":0,"rz":30,"round":0,"color":"#6DBE86"},
{"name":"bud","shape":"sphere","x":0.12,"y":0.90,"z":-0.14,"sx":0.10,"sy":0.10,"sz":0.10,"rx":0,"ry":0,"rz":0,"round":0,"color":"#F4A9A0"}
],"seat":0,"top":0}
"""),
    };

    // ------------------------------------------------------------------ finding what the user means
    static string Flat(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// The skill meant by an id, a name, or a sentence that contains one of its phrases. What the user taught comes
    /// before what came with the partner, so that "play music" means their own way of playing music once they have
    /// taught one.
    /// </summary>
    public static SkillDef FindSkill(IList<SkillDef> skills, string text)
    {
        string raw = (text ?? "").Trim(), flat = Flat(raw);
        if (raw.Length == 0 || skills == null) return null;
        // (the passing "skill" of a thing being looked at is nobody's to ask for)
        var order = skills.Where(s => s != null && !s.builtin && s.id != UseSkillId).Concat(skills.Where(s => s != null && s.builtin && s.id != UseSkillId)).ToList();
        foreach (var s in order) if (s.id == raw) return s;
        foreach (var s in order) if (string.Equals(s.name.Trim(), raw, StringComparison.OrdinalIgnoreCase)) return s;
        if (flat.Length == 0) return null;
        SkillDef best = null; int bestLen = 0;
        foreach (var s in order)
            foreach (string p in s.phrases ?? new string[0])
            {
                string f = Flat(p);
                if (f.Length >= 2 && f.Length > bestLen && flat.Contains(f)) { best = s; bestLen = f.Length; }
            }
        if (best != null) return best;
        foreach (var s in order) { string n = Flat(s.name); if (n.Length >= 2 && flat.Contains(n)) return s; }
        return null;
    }

    /// <summary>The thing meant by an id, its name, part of a sentence ("put the chair away") or the kind of thing it is.</summary>
    public static ItemDef FindItem(IList<ItemDef> items, string text)
    {
        string raw = (text ?? "").Trim(), flat = Flat(raw);
        if (raw.Length == 0 || items == null) return null;
        foreach (var i in items) if (i.id == raw) return i;
        foreach (var i in items) if (string.Equals(i.name.Trim(), raw, StringComparison.OrdinalIgnoreCase)) return i;
        if (flat.Length == 0) return null;
        foreach (var i in items) { string n = Flat(i.name); if (n.Length > 0 && (flat.Contains(n) || (flat.Length >= 2 && n.Contains(flat)))) return i; }
        foreach (var i in items) if (i.kind == raw.ToLowerInvariant()) return i;
        var kind = ItemKinds.Guess(raw);
        if (kind.words.Length > 0) foreach (var i in items) if (i.kind == kind.id) return i;
        return null;
    }

    // ------------------------------------------------------------------ what the user owns and switches on by hand
    /// <summary>How many records the record player holds in all (every snapshot carries them), and how many of those may be tunes the user had made.</summary>
    public const int MaxRecords = 16, MaxOwnRecords = 10;
    /// <summary>The id of the passing skill the partner carries out when a thing on the desk is pressed that it can only look at (Maker.Use). Never saved, never offered, never sold.</summary>
    public const string UseSkillId = "skill-use";

    static T Owned<T>(IEnumerable<T> list, string text, Func<T, string> id, Func<T, string> name, string prefix) where T : class
    {
        string raw = (text ?? "").Trim(), flat = Flat(raw);
        if (raw.Length == 0 || list == null) return null;
        var all = list.Where(x => x != null).ToList();
        foreach (var x in all) if (id(x) == raw) return x;
        if (flat.Length == 0) return null;
        foreach (var x in all) if (Flat(name(x)) == flat) return x;
        foreach (var x in all) { string i = Flat(id(x)); if (i.Length > 0 && (i == flat || i == prefix + flat)) return x; }
        return null;
    }

    /// <summary>
    /// The record meant by an id ("song-lofi"), its short form ("lofi") or its name ("Lo-fi") among the ones the user
    /// OWNS (world.songs). null when it is none of them - only then is anything else tried (a word for a built-in
    /// tune, a language model that composes one). Nothing here ever needs a model.
    /// </summary>
    public static SongDef FindRecord(IEnumerable<SongDef> owned, string text) => Owned(owned, text, s => s.id, s => s.name, "song");

    /// <summary>The same for the surroundings the user owns (world.ambiences): "amb-peak", "peak", "Mountain top".</summary>
    public static AmbienceDef FindSurroundings(IEnumerable<AmbienceDef> owned, string text) => Owned(owned, text, a => a.id, a => a.name, "amb");

    /// <summary>
    /// A skill that does nothing but put a tune on: at least one act that plays one, and beside that only what goes
    /// with putting a record on - walking to the player, a wave, a line, a pause. Such a skill is a RECORD, not a skill
    /// (music is one thing: the record player and its records). A skill that also dances, changes the surroundings,
    /// lets it rain or starts an activity is a real skill and stays one.
    /// </summary>
    public static bool TuneOnly(SkillDef s)
    {
        if (s == null || s.steps == null || s.steps.Count == 0) return false;
        bool plays = false;
        foreach (var st in s.steps)
        {
            if (st == null) return false;
            switch (st.act)
            {
                case "music": if (st.arg != "stop") plays = true; break;
                case "emote": case "say": case "goto": case "wait": break;
                default: return false;
            }
        }
        return plays;
    }

    /// <summary>
    /// The record a tune-only skill becomes: its own tune under the SKILL'S name (a copy; the id is the tune's own, or
    /// "song-" + the skill's id), or - when it only names a tune that exists - that tune as it is (one of `known`:
    /// it is a record already, and no second one is made of it). null when the skill is not tune-only, or its tune
    /// cannot be found.
    /// </summary>
    public static SongDef RecordOf(SkillDef s, IEnumerable<SongDef> known)
    {
        if (!TuneOnly(s)) return null;
        var st = s.steps.First(x => x.act == "music" && x.arg != "stop");
        if (st.arg.Length > 0) return known?.FirstOrDefault(k => k != null && k.id == st.arg);
        if (s.song == null) return null;
        var own = Proto.FromJson<SongDef>(Proto.ToJson(s.song));
        if (own == null) return null;
        if (!string.IsNullOrWhiteSpace(s.name)) own.name = Short(s.name, 24);
        if (string.IsNullOrEmpty(own.id)) own.id = "song-" + s.id;
        return own;
    }

    static bool For(ItemDef i, string use) => i != null && i.uses != null && Array.IndexOf(i.uses, use) >= 0;

    /// <summary>
    /// What the partner does with a thing on the desk when the user presses it, from what the thing is for:
    ///   music            (a record player, a speaker: the headset shows the records instead)
    ///   study            a table - and a chair, when a table stands on the desk as well
    ///   sit | nap        a seat; a bed (whichever of the two the thing is for FIRST: a bed is slept in, a sofa sat on)
    ///   read | play      a book; a toy or a ball
    ///   eat | light | decor   nothing to do with it but to enjoy it: a short reaction (a cup or food; a lamp; anything else)
    /// </summary>
    public static string UseOf(IList<ItemDef> items, ItemDef item)
    {
        if (item == null) return "";
        if (For(item, ItemUses.Music)) return "music";
        if (For(item, ItemUses.Table)) return "study";
        bool sit = For(item, ItemUses.Sit), sleep = For(item, ItemUses.Sleep);
        if (sit && sleep) return Array.IndexOf(item.uses, ItemUses.Sleep) < Array.IndexOf(item.uses, ItemUses.Sit) ? "nap" : "sit";
        if (sleep) return "nap";
        if (sit) return items != null && items.Any(i => i != item && i != null && i.inRoom && i.status != "failed" && For(i, ItemUses.Table)) ? "study" : "sit";
        if (For(item, ItemUses.Read)) return "read";
        if (For(item, ItemUses.Play)) return "play";
        if (For(item, ItemUses.Eat)) return "eat";
        if (For(item, ItemUses.Light)) return "light";
        return "decor";
    }

    /// <summary>The use of a thing that an activity takes it for ("" when the activity does not use it): the chair of study is its seat, the desk its table.</summary>
    public static string UseIn(string activity, ItemDef item) => activity switch
    {
        "study" => For(item, ItemUses.Table) ? ItemUses.Table : For(item, ItemUses.Sit) ? ItemUses.Sit : "",
        "sit" => For(item, ItemUses.Sit) ? ItemUses.Sit : "",
        "nap" => For(item, ItemUses.Sleep) ? ItemUses.Sleep : "",
        "read" => For(item, ItemUses.Read) ? ItemUses.Read : "",
        "play" => For(item, ItemUses.Play) ? ItemUses.Play : "",
        "music" => For(item, ItemUses.Music) ? ItemUses.Music : "",
        _ => "",
    };

    /// <summary>
    /// Makes the thing that was pressed the one the activity uses. The headset (Activities) takes the FIRST thing on
    /// the desk that is for a use; with two chairs out, the one that was pressed is therefore moved in front of the
    /// other in the list. Returns true when the order changed.
    /// </summary>
    public static bool Prefer(List<ItemDef> items, ItemDef item, string use)
    {
        if (items == null || item == null || string.IsNullOrEmpty(use)) return false;
        int at = items.IndexOf(item);
        int first = items.FindIndex(i => i != null && i.inRoom && i.status != "failed" && For(i, use));
        if (at < 0 || first < 0 || first >= at) return false;
        items.RemoveAt(at); items.Insert(first, item);
        return true;
    }

    /// <summary>Is the activity that is going on being done with this very thing (so that pressing it again means "stop")?</summary>
    public static bool DoingWith(WorldState w, ItemDef item)
    {
        if (w == null || item == null || string.IsNullOrEmpty(w.doing) || !string.IsNullOrEmpty(w.doingSkill)) return false;
        var act = Activities.Find(w.items, w.doing);
        return act != null && Array.IndexOf(act.itemIds, item.id) >= 0 && UseIn(w.doing, item).Length > 0;
    }

    /// <summary>
    /// What comes of pressing a thing (world command "use"), from the world as it stands and where the partner is
    /// (owner: Env.Mr on the desk, Env.Pc inside the PC):
    ///   ""            there is no such thing
    ///   making        it is still being made
    ///   inside        the partner is inside the PC: it cannot reach the things on the desk (it says so)
    ///   bag           the thing is in the bag, not on the desk
    ///   music:play | music:stop    a thing for music: the first record is put on - or, with a tune playing, the music stops
    ///   stop          it is doing exactly that with this very thing already: pressed again, it stops
    ///   missing:study (...)        the activity needs another thing that is not out (a desk without a chair)
    ///   study | sit | nap | read | play    the activity begins
    ///   eat | light | decor        a short reaction: it walks over, shows that it likes the thing and says one line
    /// </summary>
    public static string UseDecision(WorldState w, ItemDef item, string owner)
    {
        if (w == null || item == null) return "";
        if (item.status == "making") return "making";
        if (owner == Env.Pc) return "inside";
        if (!item.inRoom) return "bag";
        string use = UseOf(w.items, item);
        if (use == "music") return w.song != null ? "music:stop" : "music:play";
        if (Array.IndexOf(ActivityNames, use) < 0) return use;
        if (w.doing == use && DoingWith(w, item)) return "stop";
        return Activities.Find(w.items, use) == null ? "missing:" + use : use;
    }

    // ------------------------------------------------------------------ "did it hear me?"
    public const int HeardMax = 80;
    /// <summary>What was transcribed, as CharacterInfo.heard carries it: one line, 80 characters at most.</summary>
    public static string Heard(string text) => Short(text, HeardMax);
    /// <summary>
    /// What the partner made of what it heard, as CharacterInfo.heardNote carries it: "not for me" (its name was not
    /// said), the action it took, "" for a plain answer (no action, or none it knows). null when the sentence was not
    /// SPOKEN (typed at the PC, sent by a tool): then there is nothing to note.
    /// </summary>
    public static string HeardNote(string source, string made)
    {
        if (source == null || !source.StartsWith("voice", StringComparison.Ordinal)) return null;
        return made is null or "none" or "unknown" ? "" : Short(made, 40);
    }

    /// <summary>dance | study | sit | nap | read | play | stop, from the word itself or a few plain ways of saying it; "" when it is none of them.</summary>
    public static string ActivityWord(string text)
    {
        string t = Flat(text);
        if (t.Length == 0) return "";
        if (t is "dance" or "study" or "sit" or "nap" or "read" or "play" or "stop") return t;
        if (t.Contains("원래대로") || t.Contains("그만") || t.Contains("멈춰") || t is "off" or "clear" or "cancel") return "stop";
        if (t.Contains("춤") || t.Contains("댄스") || t.Contains("dance")) return "dance";
        if (t.Contains("공부") || t.Contains("study")) return "study";
        if (t.Contains("낮잠") || t.Contains("자러") || t.Contains("자자") || t.Contains("sleep") || t.Contains("nap")) return "nap";
        if (t.Contains("읽") || t.Contains("read")) return "read";
        if (t.Contains("앉") || t.Contains("sit")) return "sit";
        if (t.Contains("놀") || t.Contains("play")) return "play";
        return "";
    }

    /// <summary>"clear", "off", "원래대로", "꺼 줘" ...: take it away rather than set something.</summary>
    public static bool IsOff(string text)
    {
        string t = Flat(text);
        return t is "clear" or "off" or "none" or "stop" or "reset" or "꺼" or "꺼줘" or "꺼줄래" or "그만" or "없애줘" || t.Contains("원래대로");
    }

    /// <summary>
    /// A label for a thing before (or without) a language model: the word that named its kind ("책상", "Desk"),
    /// otherwise what was said with the asking taken off ("빨간 우산 하나 만들어 줘" -> "빨간 우산").
    /// </summary>
    public static string RoughName(string asked, ItemKinds.Kind kind)
    {
        string t = (asked ?? "").Trim(), low = t.ToLowerInvariant();
        if (kind != null)
            foreach (string w in kind.words)
                if (low.Contains(w)) return w.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3) ? w : char.ToUpperInvariant(w[0]) + w[1..];
        t = Regex.Replace(t, @"[.!?~,]+", " ").Trim();
        t = Regex.Replace(t, @"\s*(하나|한\s*개|좀)?\s*(만들어|줄게|줄께|줄까|줄래|줘|주라|선물).*$", "");
        t = Regex.Replace(t, @"^(please\s+)?((can|could)\s+you\s+)?(make|give|create|build|draw)\s+(me\s+|you\s+)?|^(here'?s|here\s+is|this\s+is|have|take|i('ll|\s+will)\s+give\s+you)\s+", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"^(an?|the|some)\s+", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s+(for\s+you|please)$", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"(을|를|이야)$", "");
        return Short(t, 16, kind?.id ?? "box");
    }

    /// <summary>The Korean particle that fits the word: Josa("의자", "이", "가") = "가", Josa("책상", "이", "가") = "이".</summary>
    public static string Josa(string word, string afterConsonant, string afterVowel)
    {
        word = (word ?? "").TrimEnd();
        if (word.Length == 0) return afterVowel;
        char ch = word[^1];
        if (ch >= 0xAC00 && ch <= 0xD7A3) return (ch - 0xAC00) % 28 != 0 ? afterConsonant : afterVowel;
        // a word in Latin letters or digits: by how its last letter is usually read out in Korean
        return "lmnrLMNR136780".IndexOf(ch) >= 0 ? afterConsonant : afterVowel;
    }
}
