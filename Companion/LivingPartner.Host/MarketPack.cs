using System.IO.Compression;
using System.Text;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>
/// A module as something that travels: to the hub when it is put up for sale, from the hub when it is bought, and as
/// a .bambi file from one person to another. Plain rules again (compiled into the host, the hub and the tests): which
/// files a module names, how it is made safe when it comes from somewhere else, what the shop may show of it before
/// it is paid for, and how it is packed into one file.
/// </summary>
public static class MarketPack
{
    /// <summary>One place in a module that names a file.</summary>
    public sealed class FileRef
    {
        public string role = "";            // icon | model | enemy | pickup | art (a picture of a scripted game: part says which)
        public string part = "";            // art: the name the script draws it by
        public string name = "";            // the file it names ("" = none)
        public Action<string> set;          // writes another name there
        public string Ext => role == "model" ? ".glb" : ".png";
        /// <summary>What goes into a file name between the module's id and the extension, so that each file of one module has a name of its own ("" for its sticker and its model).</summary>
        public string Suffix => role == "enemy" ? "-enemy" : role == "pickup" ? "-pickup" : role == "art" ? "-art-" + part : "";
    }

    /// <summary>Every place in a module that names a file: its picture (in the listing and in its thing or skill), a thing's model, a game's creature and pick-up, the pictures of a scripted game.</summary>
    public static List<FileRef> FileRefs(MarketModule m)
    {
        var refs = new List<FileRef>();
        if (m == null) return refs;
        refs.Add(new FileRef { role = "icon", name = m.icon ?? "", set = v => m.icon = v });
        if (m.item != null)
        {
            refs.Add(new FileRef { role = "icon", name = m.item.icon ?? "", set = v => m.item.icon = v });
            refs.Add(new FileRef { role = "model", name = m.item.model ?? "", set = v => m.item.model = v });
        }
        if (m.skill != null) refs.Add(new FileRef { role = "icon", name = m.skill.icon ?? "", set = v => m.skill.icon = v });
        if (m.game != null)
        {
            // (the pack's own sticker: usually the very file the listing shows)
            refs.Add(new FileRef { role = "icon", name = m.game.icon ?? "", set = v => m.game.icon = v });
            refs.Add(new FileRef { role = "enemy", name = m.game.enemyIcon ?? "", set = v => m.game.enemyIcon = v });
            refs.Add(new FileRef { role = "pickup", name = m.game.pickupIcon ?? "", set = v => m.game.pickupIcon = v });
            // (the pictures painted for a scripted game: the script draws each as "pack:" + its name)
            foreach (var a in m.game.art ?? new List<PackArt>())
                if (a != null) { var art = a; refs.Add(new FileRef { role = "art", part = art.name ?? "", name = art.file ?? "", set = v => art.file = v }); }
        }
        return refs;
    }

    /// <summary>The first word of a new module's id, by its kind (as the Maker and the Market name theirs).</summary>
    public static string Prefix(string kind) => kind switch
    {
        ModuleKinds.Item => "item", ModuleKinds.Skill => "skill", ModuleKinds.Game => "game", ModuleKinds.Ambience => "amb", _ => "ability",
    };

    /// <summary>Gives a module another id, in the listing and in its payload (the two are always the same).</summary>
    public static void ReId(MarketModule m, string id)
    {
        m.id = id;
        if (m.item != null) m.item.id = id;
        if (m.skill != null)
        {
            m.skill.id = id;
            // (a skill's own tune and surroundings are named after it: two copies of one imported skill must not share those names either)
            if (m.skill.song != null) m.skill.song.id = "song-" + id;
            if (m.skill.ambience != null) m.skill.ambience.id = "amb-" + id;
        }
        if (m.ambience != null) m.ambience.id = id;
        if (m.game != null) m.game.id = id;
        if (m.ability != null) m.ability.id = id;
    }

    /// <summary>
    /// Makes a module that came from somewhere else (the hub, a file, another person's PC) safe to keep, and says what
    /// is wrong with it ("" = nothing). It is never one of the things that came with the partner, whatever it claims;
    /// a thing in it lies in the bag, finished; a skill in it is not tied to a thing only its maker has.
    /// </summary>
    public static string Sanitise(MarketModule m)
    {
        if (m == null) return "no module";
        MarketRules.Normalise(m);
        m.builtin = false; m.installs = 0;
        m.name = WorldRules.Short(m.name, 40); m.summary = WorldRules.Short(m.summary, 200); m.gives = WorldRules.Short(m.gives, 160); m.version = WorldRules.Short(m.version, 12, "1.0");
        m.author = WorldRules.Short(m.author, HubRules.NickMax * 3);
        m.tags = m.tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => WorldRules.Short(t, 24)).Distinct().Take(8).ToArray();
        // What somebody else wrote is trusted exactly as much as what a language model wrote: every payload goes
        // through the cleaner that is used for model output (WorldRules), so that every text has an end, every list
        // a length, every number a range - and nothing that is not a number passes for one.
        if (m.item != null)
        {
            var i = m.item;
            i.status = "ready"; i.progress = 1; i.inRoom = false; i.x = 0; i.z = 0; i.yaw = 0; i.wornOn = ""; i.script ??= "";
            i.name = WorldRules.Short(i.name, 40); i.asked = WorldRules.Short(i.asked, 300); i.note = WorldRules.Short(i.note, 200);
            if (i.color.Length > 0) i.color = WorldRules.Colour(i.color, "");
            i.uses = i.uses.Distinct().ToArray();
            // a shape sculpted out of parts is checked exactly as one the language model has just written
            if (i.parts != null && i.parts.Count > 0)
            {
                float seat = i.seat, top = i.top;
                var parts = WorldRules.ValidateParts(i.parts, ref seat, ref top, out _);
                i.parts = parts ?? new List<ShapePart>(); i.seat = parts == null ? 0 : seat; i.top = parts == null ? 0 : top;
            }
            else
            {
                i.parts = new List<ShapePart>();
                // (heights in the measure of the whole thing, which is 1: anything else is "not given")
                if (!(i.seat >= 0f && i.seat <= 1f)) i.seat = 0;
                if (!(i.top >= 0f && i.top <= 1f)) i.top = 0;
            }
        }
        if (m.skill != null) m.skill = Clean(m.skill);
        if (m.ambience != null) m.ambience = WorldRules.Ambience(JObject.FromObject(m.ambience), m.ambience.id ?? "");
        if (m.song != null) m.song = WorldRules.Song(JObject.FromObject(m.song), Id(m.song.id, "song-" + m.id));
        if (m.game != null)
        {
            var g = m.game;
            g.title = WorldRules.Short(g.title, MarketRules.GameTitleMax); g.howTo = WorldRules.Short(g.howTo, MarketRules.GameHowToMax);
            g.enemyName = WorldRules.Short(g.enemyName, MarketRules.GameNameMax); g.pickupName = WorldRules.Short(g.pickupName, MarketRules.GameNameMax);
            if (g.song != null) g.song = WorldRules.Song(JObject.FromObject(g.song), Id(g.song.id, "song-" + m.id));
            if (g.ambience != null) g.ambience = WorldRules.Ambience(JObject.FromObject(g.ambience), Id(g.ambience.id, "amb-" + m.id));
            // a game that is no script carries none (nothing would run it); the pictures of one that is: three at most, each under a name a script can use
            if (g.template != ScriptRules.Template) { g.script = ""; g.art.Clear(); }
            var art = new List<PackArt>();
            foreach (var a in g.art)
            {
                string name = ScriptRules.ArtName(a.name);
                if (name.Length > 0 && art.Count < ScriptRules.MaxArt && !art.Any(x => x.name == name)) art.Add(new PackArt { name = name, file = a.file ?? "" });
            }
            g.art = art;
        }
        if (m.ability != null)
        {
            var a = m.ability;
            a.name = WorldRules.Short(a.name, MarketRules.AbilityNameMax);
            a.examples = a.examples.Select(e => WorldRules.Short(e, MarketRules.AbilityExampleMax)).Where(e => e.Length > 0).Distinct().Take(MarketRules.AbilityExamplesMax).ToArray();
        }
        foreach (var r in FileRefs(m)) if (r.name.Length > 0 && !(HubRules.SafeFile(r.name) && r.name.EndsWith(r.Ext, StringComparison.OrdinalIgnoreCase))) r.set("");
        string problem = MarketRules.Problem(m);
        // A SCRIPT THAT COMES FROM SOMEWHERE ELSE IS RUN BEFORE ANYTHING IS DONE WITH IT (ScriptRules.Problem): one that
        // breaks is refused, with the line it breaks on; one that only earns warnings is taken. The hub (putting up
        // for sale), a purchase and a .bambi file all come through here.
        return problem.Length > 0 ? problem : ScriptRules.Problem(m);
    }

    static string Id(string given, string fallback) => MarketRules.SafeId(given) ? given : fallback;

    /// <summary>
    /// A skill from somewhere else, made safe exactly as one the language model has just written (WorldRules.Skill):
    /// a short name, at most eight phrases and eight steps, only acts and arguments the headset knows, a tune and
    /// surroundings that pass the same cleaners. It is nobody's built-in skill and is tied to no thing (a thing only
    /// its maker has); a step that walks to such a thing is left out. null when no step is left.
    /// </summary>
    static SkillDef Clean(SkillDef k)
    {
        var clean = WorldRules.Skill(JObject.FromObject(k), k.id ?? "", null, WorldDefaults.Songs().Select(s => s.id), WorldDefaults.Ambiences().Select(a => a.id));
        // (a skill that is a script needs no steps: what it does is its program, which is checked by running it)
        if (clean == null && !string.IsNullOrWhiteSpace(k.script))
            clean = new SkillDef { id = k.id ?? "", name = WorldRules.Short(k.name, 20), note = WorldRules.Short(k.note, 160), phrases = (k.phrases ?? new string[0]).Select(p => WorldRules.Short(p, 60)).Where(p => p.Length > 0).Distinct().Take(WorldRules.MaxPhrases).ToArray() };
        if (clean == null) return null;
        clean.script = k.script ?? "";
        clean.asked = WorldRules.Short(k.asked, 300); clean.icon = k.icon ?? "";
        clean.builtin = false; clean.status = "ready"; clean.itemId = "";
        return clean;
    }

    /// <summary>What the shop may show of a module before it is bought (see HubDetail).</summary>
    public static HubDetail Detail(MarketModule m)
    {
        var song = MarketRules.SongOf(m);
        var amb = m.ambience ?? m.skill?.ambience ?? m.game?.ambience;
        // (what a scripted thing can be asked to do: where it is worn, the acts it offers - read from its script)
        var wears = m.item != null && !string.IsNullOrWhiteSpace(m.item.script) ? ScriptRules.Tricks(m.item) : new ThingTricks();
        return new HubDetail
        {
            steps = m.skill?.steps.Select(st => new HubStep { act = st.act, arg = st.act == "say" ? WorldRules.Short(st.arg, 80) : st.arg, seconds = st.seconds }).ToArray(),
            phrases = m.skill?.phrases.Take(6).ToArray(),
            song = song?.name ?? "", bpm = song?.bpm ?? 0,
            pattern = amb?.pattern ?? "", moodName = amb?.name ?? "", colors = amb == null ? null : new[] { amb.c0, amb.c1, amb.c2 },
            howTo = m.game?.howTo ?? "", enemyName = m.game?.enemyName ?? "", pickupName = m.game?.pickupName ?? "", enemyIcon = m.game?.enemyIcon ?? "", pickupIcon = m.game?.pickupIcon ?? "",
            seconds = m.game?.seconds ?? 0, lives = m.game?.lives ?? 0, speed = m.game?.speed ?? 0, spawn = m.game?.spawn ?? 0,
            examples = m.ability?.examples.Take(5).ToArray(),
            shape = m.item?.kind ?? "", uses = m.item?.uses, color = m.item == null ? "" : m.item.color.Length > 0 ? m.item.color : ItemKinds.Find(m.item.kind).color,
            tags = m.tags, version = m.version,
            scriptLines = ScriptRules.Lines(ScriptRules.ScriptOf(m)), art = m.game?.art?.Count(a => a != null && !string.IsNullOrEmpty(a.file)) ?? 0,
            wearable = wears.wearable ? wears.slot : "", acts = wears.offers.Select(o => o.label.Length > 0 ? o.label : o.name).Take(4).ToArray(),
        };
    }

    // ------------------------------------------------------------------ a module as one file (.bambi)
    // A zip with module.json (the MarketModule) and files/<name> for every file the module names. Small enough to mail.

    public const string Extension = ".bambi";
    /// <summary>The most a .bambi file may be: room for a model of 16 MB, its pictures and the module, with as much again to spare. (Eight files of 16 MB - 128 MB read into memory at once - was what it used to allow.)</summary>
    public const int MaxBambiBytes = 40 * 1024 * 1024;

    public static byte[] Pack(MarketModule m, IReadOnlyDictionary<string, byte[]> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var s = zip.CreateEntry("module.json", CompressionLevel.Optimal).Open())
            {
                byte[] json = Encoding.UTF8.GetBytes(Proto.ToJson(m, true));
                s.Write(json, 0, json.Length);
            }
            foreach (var f in files ?? new Dictionary<string, byte[]>())
            {
                if (!HubRules.SafeFile(f.Key) || f.Value == null) continue;
                // (pictures and models are compressed already: storing them as they are is quicker and no larger)
                using var s = zip.CreateEntry("files/" + f.Key, CompressionLevel.NoCompression).Open();
                s.Write(f.Value, 0, f.Value.Length);
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Opens a .bambi file. Returns what is wrong with it ("" = nothing): not a zip, no module in it, a module that is
    /// not sound, too large. The module comes back made safe (Sanitise), with only the files it names - and nothing is
    /// ever read past the limits, whatever the zip claims about its sizes.
    /// </summary>
    public static string Unpack(byte[] bambi, out MarketModule m, out Dictionary<string, byte[]> files)
    {
        m = null; files = new Dictionary<string, byte[]>();
        if (bambi == null || bambi.Length < 22) return "not a module file";
        if (bambi.Length > MaxBambiBytes) return "the file is too large";
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bambi), ZipArchiveMode.Read);
            if (zip.Entries.Count > HubRules.MaxFiles + 4) return "too many files in it";
            var entry = zip.GetEntry("module.json");
            if (entry == null) return "no module in it";
            byte[] json = ReadCapped(entry, HubRules.MaxModuleJson);
            if (json == null) return "the module is too large";
            m = Proto.FromJson<MarketModule>(Encoding.UTF8.GetString(json));
            if (m == null) return "the module cannot be read";
            string problem = Sanitise(m);
            if (problem.Length > 0) { m = null; return problem; }
            foreach (var r in FileRefs(m))
            {
                if (r.name.Length == 0 || files.ContainsKey(r.name)) continue;
                var f = zip.GetEntry("files/" + r.name);
                byte[] data = f == null ? null : ReadCapped(f, HubRules.MaxBytesFor(r.name));
                if (data != null && LooksRight(r.name, data) && files.Count < HubRules.MaxFiles) files[r.name] = data;
            }
            // a file the module names and the zip does not carry: the module does without it (a picture by kind, the built-in shape)
            foreach (var r in FileRefs(m)) if (r.name.Length > 0 && !files.ContainsKey(r.name)) r.set("");
            return "";
        }
        catch (InvalidDataException) { m = null; return "not a module file"; }
        catch (Exception e) { m = null; return "the file cannot be read (" + e.GetType().Name + ")"; }
    }

    static byte[] ReadCapped(ZipArchiveEntry entry, int max)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            if (ms.Length + n > max) return null;
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    /// <summary>A file is what its name says: a PNG picture begins as one, a GLB model begins as one. (A picture is handed to browsers and to the headset as a picture: nothing else may hide under that name.)</summary>
    public static bool LooksRight(string name, byte[] data)
    {
        if (data == null || data.Length < 12) return false;
        // (and no larger than a file of its kind may be: a picture 600 kB, a model 16 MB)
        if (data.Length > HubRules.MaxBytesFor(name)) return false;
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
        if (name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) return data[0] == (byte)'g' && data[1] == (byte)'l' && data[2] == (byte)'T' && data[3] == (byte)'F';
        return false;
    }
}
