using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>
/// "3D making: when I SHOW some real object in the MR environment, Nilo makes it and brings it into the PC."
///   1. "이거 만들어 줘" (action makeFromSight; the rules and wordings: SightRules.cs) - a job in the work queue begins
///      ("Looking at what you are holding") and the headset is asked for ONE picture (Msg.EyeRequest; MR/MrEyes.cs asks
///      for its camera only then and shows that the partner is looking).
///   2. The picture comes back (Msg.EyeFrame) - or not: the camera is not allowed, there is none, the desktop simulator.
///      Then the partner says so and asks the user to describe the thing instead. (For checks without a headset the
///      environment variable LP_EYE_TEST_IMAGE names a stand-in picture; never a camera of this PC.)
///   3. The vision model says what the object is (name, size, colours, parts); the sculptor - the same that makes every
///      thing out of simple solids - is given the PICTURE and sculpts that object ("Shaping it").
///   4. The thing stands on the desk as an ordinary thing (ItemDef with parts: grab, place, put away), with its own
///      rendered picture as its icon.
///   5. Its shape is written as a real 3D file, Documents\Nilo\Models\<date> <name>\<name>.glb (GlbWriter: the very
///      triangles the headset draws, PartMesh) with a PNG preview next to it, and the partner CARRIES it into the PC the
///      way it carries documents (DocsHost: the show of kind "file", a copy on the Desktop, a click on it reveals the file).
///      "의자 PC로 가져가 줘" (exportThing) does the same for any thing it sculpted earlier.
/// The picture of the user's surroundings is held in memory for the two model calls and nowhere else: it is not written
/// to disk and not kept. State is touched on the core thread only; the model calls run on the thread pool and post back.
/// </summary>
public sealed class SightHost
{
    readonly Core _core;

    sealed class Look
    {
        public string id = "", asked = "";
        public WorkJob job;
        public double deadline;
        public bool working;                    // the picture is here: the models are at it
        public CancellationTokenSource cts;
        public float sent;                      // the progress last told to the queue
    }
    Look _look;                                 // one at a time
    string _carryDoc = ""; double _carryAt;     // the model whose file is carried into the PC in a moment
    readonly Dictionary<string, string> _docOf = new();      // thing -> the document (kind "model") its file is, this sitting
    int _asked, _made, _exported; string _last = "";

    public SightHost(Core core)
    {
        _core = core;
        core.Work.Cancelled += j => { if (_look != null && _look.job == j) Stop(_look, "", cancelled: true); };
    }

    public object Diagnostics => new { asked = _asked, made = _made, exported = _exported, looking = _look != null, last = _last, effort = Effort, standIn = TestImage != null };

    WorldState W => _core.State.world;
    bool Ko => _core.Talk.LastLanguage == "Korean";
    bool Enabled => _core.Talk.Enabled;
    /// <summary>How long the sculptor thinks with the photo in front of it (none | low | medium | high): LP_OPENAI_SIGHT_EFFORT. A likeness takes more looking than a thing made up from a word.</summary>
    internal static string Effort => Environment.GetEnvironmentVariable("LP_OPENAI_SIGHT_EFFORT") is { Length: > 0 } e ? e : "medium";
    /// <summary>A stand-in picture for checks without a headset camera (the simulator, a test): the file LP_EYE_TEST_IMAGE names, when it is there.</summary>
    static string TestImage => (_testImage.Length > 0 ? _testImage : Environment.GetEnvironmentVariable("LP_EYE_TEST_IMAGE")) is { Length: > 0 } p && File.Exists(p) ? p : null;
    static string _testImage = "";
    /// <summary>`lpctl debug eyeTest 0 0 0 "<picture>"`: names the stand-in picture while the host runs ("" = none again). Only ever a file on this PC, named by a check.</summary>
    public void SetTestImage(string path)
    {
        _testImage = (path ?? "").Trim();
        Log.Info(_testImage.Length == 0 ? "sight: TEST: no stand-in picture any more" : $"sight: TEST: the stand-in picture is {_testImage} ({(File.Exists(_testImage) ? "found" : "NOT found")})");
    }
    public static string ModelsRoot => Path.Combine(DocsHost.UserRoot, "Models");

    string Tell(string what, string mood = "happy", float seconds = 4, string a = "")
    {
        string line = SightRules.Line(what, Ko, a);
        if (line.Length > 0) _core.Say(line, mood, seconds, "sight: " + what);
        return line;
    }

    public string PromptSection() => SightRules.PromptSection(_core.HasMr);

    /// <summary>Carries out one of the two actions (the plain parser's, or the model's). Says its own line and returns it.</summary>
    public string Act(Client c, string action, string thing, Utterance u)
    {
        Log.Info($"sight: asked: {action} '{WorldRules.Short(thing, 40)}' ('{WorldRules.Short(u?.text, 70)}')");
        return action == SightRules.Export ? Export(thing ?? "") : Make(u?.text ?? thing ?? "");
    }

    // ------------------------------------------------------------------ 1. asked: the headset is asked for a picture
    string Make(string asked)
    {
        _asked++;
        if (!Enabled) return Tell("noKey", "worried", 5);
        if (_look != null || _core.World.Making) return Tell("busy", "neutral");
        if (W.items.Count >= WorldRules.MaxItems) return Tell("full", "worried", 5);
        string standIn = TestImage;
        if (!_core.HasMr && standIn == null) { _last = "no headset"; return Tell("noHeadset", "curious", 6); }

        var look = _look = new Look { id = "eye-" + Maker.NewId(), asked = asked.Trim(), deadline = HostClock.Now + SightRules.FrameSeconds, cts = new CancellationTokenSource(TimeSpan.FromMinutes(7)) };
        look.job = _core.Work.Begin("model", "3D model of what you showed", look.asked, "mr", "sculptor " + Sculptor.Model(_core.Talk.Ai));
        _core.Work.Step(look.job, SightRules.StepLook, 0.04f);
        if (_core.HasMr)
        {
            _core.Broadcast(Msg.EyeRequest, new EyeRequest { id = look.id, maxSide = 1280 }, _core.IsMr);
            Log.Info($"sight: {look.id}: the headset is asked for one picture (waiting up to {SightRules.FrameSeconds:0} s)");
        }
        else
        {
            Log.Info($"sight: {look.id}: no headset - the stand-in picture is used (LP_EYE_TEST_IMAGE)");
            _core.Post(() => { if (_look == look && !look.working) StandIn(look, "no headset"); });
        }
        return Tell("look", "curious");
    }

    // ------------------------------------------------------------------ 2. the picture, or why there is none
    public void OnFrame(Client c, EyeFrame f)
    {
        var look = _look;
        if (f == null || look == null || look.working || f.id != look.id) { Log.Info($"sight: a picture nobody waits for ('{f?.id}') from {c?.Role}: dropped"); return; }
        if (f.ok)
        {
            byte[] jpeg = null;
            try { jpeg = Convert.FromBase64String(f.jpeg ?? ""); } catch { }
            if (jpeg == null || jpeg.Length < 200 || jpeg.Length > SightRules.MaxPictureBytes || Mime(jpeg).Length == 0)
            {
                Log.Warn($"sight: {look.id}: what the headset sent is not a picture ({jpeg?.Length ?? 0} bytes)");
                if (!StandIn(look, "an unusable picture")) Stop(look, "nocamera");
                return;
            }
            Log.Info($"sight: {look.id}: picture from the headset: {f.width}x{f.height}, {jpeg.Length / 1024} KB, {(f.cropped ? "cut round the hands" : "the middle of the view")}");
            Begin(look, jpeg);
            return;
        }
        string why = f.why is "denied" or "nocamera" or "simulator" ? f.why : "nocamera";
        Log.Info($"sight: {look.id}: the headset has no picture: {why}");
        if (!StandIn(look, why)) Stop(look, why == "simulator" ? "nocamera" : why);
    }

    /// <summary>The stand-in picture of a check, when one is named. false: there is none.</summary>
    bool StandIn(Look look, string because)
    {
        string path = TestImage;
        if (path == null) return false;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length > SightRules.MaxPictureBytes || Mime(bytes).Length == 0) { Log.Warn("sight: LP_EYE_TEST_IMAGE is not a JPEG or PNG of at most 3 MB"); return false; }
            Log.Info($"sight: {look.id}: {because} - the STAND-IN picture is used ({Path.GetFileName(path)}, {bytes.Length / 1024} KB; LP_EYE_TEST_IMAGE)");
            Begin(look, bytes);
            return true;
        }
        catch (Exception e) { Log.Warn("sight: the stand-in picture could not be read: " + e.Message); return false; }
    }

    /// <summary>Over without a thing: the job ends, the partner says why (and what the user can do instead).</summary>
    void Stop(Look look, string why, bool cancelled = false)
    {
        if (_look != look) return;
        _look = null;
        try { look.cts.Cancel(); look.cts.Dispose(); } catch { }
        _last = cancelled ? "cancelled" : why;
        string note = why switch
        {
            "denied" => "The headset's camera is not allowed", "nocamera" => "The headset's camera gave no picture", "timeout" => "No picture arrived from the headset",
            "notFound" => "Nothing to copy could be made out in the picture", _ => cancelled ? "" : "It could not be sculpted",
        };
        _core.Work.End(look.job, false, "", "", note, cancelled);
        Log.Info($"sight: {look.id}: over without a thing ({(cancelled ? "cancelled" : why)})");
        if (cancelled) return;
        string line = Tell(why, why == "notFound" ? "curious" : "worried", 7);
        // (so that "it's a mug" a moment later is understood as the thing to make)
        _core.Talk.Remember($"The user held something up and asked you to make it, but you could not see it ({note.ToLowerInvariant()}). You said: \"{line}\" If they now say what it is, make it with makeItem.");
    }

    public void Tick(double now)
    {
        if (_look is { working: false } look && now > look.deadline)
        {
            Log.Info($"sight: {look.id}: no answer from the headset in {SightRules.FrameSeconds:0} s");
            if (!StandIn(look, "no answer from the headset")) Stop(look, "timeout");
        }
        if (_carryDoc.Length > 0 && now >= _carryAt)
        {
            string id = _carryDoc; _carryDoc = "";
            _core.Docs.DeliverModel(id);
        }
    }

    // ------------------------------------------------------------------ 3. seen and sculpted
    void Begin(Look look, byte[] picture)
    {
        look.working = true;
        _core.Work.Step(look.job, SightRules.StepSee, 0.10f);
        var ai = _core.Talk.Ai;
        string lang = _core.Talk.LastLanguage;
        _ = Task.Run(() => Build(look, picture, ai, lang));
    }

    void Progress(Look look, string step, float p)
    {
        if (_look != look) return;
        if (step.Length == 0 && p - look.sent < 0.02f) return;
        look.sent = Math.Max(look.sent, p);
        _core.Work.Step(look.job, step, p);
    }

    async Task Build(Look look, byte[] picture, OpenAiClient ai, string lang)
    {
        var ct = look.cts.Token;
        try
        {
            var seen = await SeeAsync(ai, picture, look.asked, lang, ct);
            if (!seen.found) { _core.Post(() => Stop(look, "notFound")); return; }
            string title = SightRules.Title(seen.what);
            _core.Post(() =>
            {
                if (_look != look) return;
                _core.Work.Rename(look.job, "3D model: " + title);
                Progress(look, SightRules.StepShape, 0.22f);
                Tell("seen", "happy", 4, seen.idea.name);
            });
            string id = "item-" + Maker.NewId();
            var s = await ShapeAsync(ai, id, seen, picture, look.asked, ct, p => _core.Post(() => Progress(look, "", 0.22f + 0.62f * p)));
            if (s == null) { _core.Post(() => Stop(look, "failed")); return; }
            _core.Post(() => Progress(look, SightRules.StepColour, 0.88f));
            var made = Files(s.parts, title, SightRules.Metres(seen.idea.realCm));
            _core.Post(() => Finish(look, id, seen, s, title, made));
        }
        catch (OperationCanceledException) { _core.Post(() => Stop(look, "failed")); }
        catch (Exception e) { Log.Warn($"sight: {look.id}: {e.Message}"); _core.Post(() => Stop(look, "failed")); }
    }

    internal sealed class Seen { public bool found; public string what = ""; public ItemIdea idea = new(); public JObject raw; public long ms; }

    /// <summary>What is the object in the picture? (the vision model, one call)</summary>
    internal static async Task<Seen> SeeAsync(OpenAiClient ai, byte[] picture, string asked, string lang, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var kinds = ItemKinds.All.Select(k => k.id).ToList();
        var json = await ai.AskJsonAsync(SightRules.SeeSystem, SightRules.SeeAsk(asked, lang, kinds, ItemUses.All), new[] { (Mime(picture), picture, "held.jpg") }, SightRules.SeeShape(kinds, ItemUses.All), 1800, ct, ai.VisionModel, "low");
        string what = WorldRules.Text(json["what"], 60).Trim();
        var seen = new Seen { found = (bool?)json["found"] == true && what.Length > 0, what = what, raw = json, ms = sw.ElapsedMilliseconds };
        if (seen.found)
        {
            seen.idea = WorldRules.Item(json, ItemKinds.Guess(what));
            if (seen.idea.name.Length == 0) seen.idea.name = SightRules.Title(what);
        }
        Log.Info($"sight: seen in {seen.ms} ms: {(seen.found ? $"'{what}' ('{seen.idea.name}', kind={seen.idea.kind}, {seen.idea.realCm:0} cm, {seen.idea.color})" : "nothing to copy")}");
        return seen;
    }

    /// <summary>The sculptor with the picture in front of it.</summary>
    internal static Task<Sculpture> ShapeAsync(OpenAiClient ai, string id, Seen seen, byte[] picture, string asked, CancellationToken ct, Action<float> progress = null, Action<JObject> answer = null, string effort = null) =>
        Sculptor.SculptAsync(ai, id, seen.idea, asked.Length > 0 ? asked : seen.what, ct, progress, answer,
            new[] { (Mime(picture), picture, "held.jpg") }, SightRules.SculptNote(seen.what, seen.idea.modelPrompt), string.IsNullOrEmpty(effort) ? Effort : effort);

    internal static string Mime(byte[] b) =>
        b != null && b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg" : b != null && b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png" : "";

    // ------------------------------------------------------------------ 5. the 3D file and its pictures
    internal sealed class Made { public byte[] glb, png, icon; public GlbWriter.Facts facts; public string why = ""; public bool Ok => glb != null; }

    /// <summary>
    /// The thing's shape as a .glb, its preview (on cream paper) and its icon (cut out). The file is read back twice
    /// before anyone gets it: by the rules of the format (GlbWriter.Check) and by the reader the headset uses
    /// (GlbReader) - a file that does not open is not carried anywhere.
    /// </summary>
    internal static Made Files(IList<ShapePart> parts, string title, float metres)
    {
        var made = new Made();
        try
        {
            var mesh = PartMesh.Build(parts);
            if (mesh == null) { made.why = "the parts give no shape"; return made; }
            made.png = Png(ModelRender.Render(mesh, 768), 768);
            made.icon = Png(ModelRender.Render(mesh, 512, 0u), 512);
            byte[] glb = GlbWriter.Write(mesh, title, metres);
            var facts = GlbWriter.Check(glb, out string why);
            if (facts == null) { made.why = "the file breaks a rule of glTF: " + why; return made; }
            if (GlbReader.Read(glb, out string unreadable) == null) { made.why = "the file cannot be read back: " + unreadable; return made; }
            made.glb = glb; made.facts = facts;
        }
        catch (Exception e) { made.why = e.Message; }
        return made;
    }

    static byte[] Png(byte[] bgra, int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < size; y++) Marshal.Copy(bgra, y * size * 4, data.Scan0 + y * data.Stride, size * 4); }
        finally { bmp.UnlockBits(data); }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Writes the model's files into a folder of their own under Documents\Nilo\Models. Returns (folder, glb, png); throws when the disk says no.</summary>
    internal static (string folder, string glb, string png) Save(Made made, string title, DateTime now, string root)
    {
        Directory.CreateDirectory(root);
        string folder = Path.Combine(root, SightRules.FolderName(now, title, n => Directory.Exists(Path.Combine(root, n)) || File.Exists(Path.Combine(root, n))));
        Directory.CreateDirectory(folder);
        string stem = SightRules.FileStem(title), glb = Path.Combine(folder, stem + ".glb"), png = Path.Combine(folder, stem + ".png");
        File.WriteAllBytes(glb, made.glb);
        if (made.png != null) File.WriteAllBytes(png, made.png);
        return (folder, glb, png);
    }

    /// <summary>The thing's file joins the documents of this sitting (kind "model"), so that it is carried, placed and revealed like one. "" when it could not be written.</summary>
    string Register(ItemDef item, Made made, string title, string asked)
    {
        if (!made.Ok) { Log.Warn($"sight: {item.id}: no 3D file: {made.why}"); return ""; }
        try
        {
            var (folder, glb, png) = Save(made, title, DateTime.Now, ModelsRoot);
            string doc = _core.Docs.AddModel(title, asked, folder, glb, png, made.png);
            _docOf[item.id] = doc;
            _exported++;
            Log.Info($"sight: {item.id}: 3D file written: {glb} ({made.glb.Length / 1024} KB, {made.facts.triangles} triangles, {made.facts.primitives} colours, {made.facts.max[0] - made.facts.min[0]:0.000} x {made.facts.max[1] - made.facts.min[1]:0.000} x {made.facts.max[2] - made.facts.min[2]:0.000} m) - document {doc}");
            return doc;
        }
        catch (Exception e) { Log.Warn($"sight: {item.id}: the 3D file could not be written: {e.Message}"); return ""; }
    }

    // ------------------------------------------------------------------ 4. the thing stands on the desk
    void Finish(Look look, string id, Seen seen, Sculpture s, string title, Made made)
    {
        if (_look != look) return;      // cancelled meanwhile
        _look = null;
        try { look.cts.Dispose(); } catch { }
        var idea = seen.idea;
        var item = new ItemDef
        {
            id = id, name = idea.name, asked = look.asked.Length > 0 ? look.asked : seen.what, kind = idea.kind, uses = idea.uses, sizeM = idea.sizeM, color = idea.color, note = idea.note,
            status = "ready", progress = 1, inRoom = true, madeUnixMs = HostClock.UnixMs, parts = s.parts, seat = s.seat, top = s.top,
        };
        W.items.Add(item);
        Activities.Arrange(W.items, item);
        // its icon: the thing itself as it was sculpted, cut out (nothing is drawn by an image model for it)
        if (made.icon != null)
            try { File.WriteAllBytes(Path.Combine(HostPaths.World, id + ".png"), made.icon); item.icon = id + ".png"; }
            catch (Exception e) { Log.Warn($"sight: {id}: its icon could not be written: {e.Message}"); }
        _core.World.Changed();
        _made++; _last = seen.what;

        _core.Work.Step(look.job, SightRules.StepFile, 0.95f);
        string doc = Register(item, made, title, item.asked);
        _core.Work.End(look.job, true, "item", item.id, doc.Length > 0 ? "It is on the desk; its 3D file goes into your computer" : "It is on the desk");
        string line = ((item.note.Length > 0 ? item.note + " " : "") + SightRules.Line(doc.Length > 0 ? "done" : "doneNoFile", Ko)).Trim();
        _core.Say(line, "happy", 6, "sight: made");
        _core.Talk.Remember($"The user held up a real object ({seen.what}) and you sculpted a copy of it: \"{item.name}\". It stands on the desk; it is for: {string.Join(", ", item.uses)}." +
                            (doc.Length > 0 ? " Its 3D file (.glb) is saved on their computer and you are carrying it in (exportThing carries a thing's 3D file into the computer again)." : ""));
        Log.Info($"sight: {id} '{item.name}' ({seen.what}) is on the desk: {s.parts.Count} parts, height {s.height:0.00}{(doc.Length > 0 ? "; its file is carried into the PC in a moment" : "")}");
        if (doc.Length > 0) { _carryDoc = doc; _carryAt = HostClock.Now + SightRules.CarryAfterSeconds; }
    }

    // ------------------------------------------------------------------ "PC로 가져가 줘": a thing sculpted earlier
    string Export(string thing)
    {
        var sculpted = W.items.Where(i => i.status == "ready" && i.parts != null && i.parts.Count > 0).ToList();
        var item = thing.Trim().Length > 0 ? WorldRules.FindItem(W.items, thing) : sculpted.LastOrDefault();
        if (item == null) return Tell("noThing", "curious", 5);
        if (item.parts == null || item.parts.Count == 0) return Tell("noShape", "neutral", 5, item.name);
        if (!_docOf.TryGetValue(item.id, out string doc) || !_core.Docs.Has(doc))
        {
            // (its English name when it has one: the file is for any program on the PC)
            string title = SightRules.Title(item.name);
            doc = Register(item, Files(item.parts, title, SightRules.Metres(0)), title, item.asked);
            if (doc.Length == 0) return Tell("notWritten", "worried", 5);
        }
        _carryDoc = "";
        return _core.Docs.DeliverModel(doc);
    }

    /// <summary>`--check-glb <file>`: the file read back by the rules of the format and by the headset's reader; one line about what it holds. 0 = it opens.</summary>
    public static int CheckFile(string path)
    {
        try
        {
            byte[] glb = File.ReadAllBytes(path);
            var facts = GlbWriter.Check(glb, out string why);
            if (facts == null) { Console.WriteLine($"NOT VALID: {Path.GetFileName(path)}: {why}"); return 1; }
            var model = GlbReader.Read(glb, out string unreadable);
            if (model == null) { Console.WriteLine($"NOT READABLE: {Path.GetFileName(path)}: {unreadable}"); return 1; }
            Console.WriteLine($"OK: {Path.GetFileName(path)}: {glb.Length} bytes, glTF 2.0, '{facts.name}', {facts.primitives} primitives / {facts.materials} materials, {facts.vertices} vertices, {facts.triangles} triangles, " +
                              $"size {facts.max[0] - facts.min[0]:0.000} x {facts.max[1] - facts.min[1]:0.000} x {facts.max[2] - facts.min[2]:0.000} m, stands on y = {facts.min[1]:0.000}; GlbReader: {model.indices.Length / 3} triangles in {model.primitives} primitives");
            return 0;
        }
        catch (Exception e) { Console.WriteLine("check-glb: " + e.Message); return 2; }
    }

    // ------------------------------------------------------------------ the command line: trying it on a photo
    /// <summary>
    /// `--sculpt-photo <picture> [--say "words"] [--effort low|medium|high] --out <folder>`: looks at a picture as the
    /// host does for "이거 만들어 줘", sculpts what it shows and writes into the folder: the thing (item.json), what the
    /// models answered (raw.json), the 3D file and its preview. Starts nothing else. 0 = made, 1 = seen but not
    /// sculpted, 2 = nothing could be done.
    /// </summary>
    public static async Task<int> RunAsync(string picturePath, string words, string effort, string outFolder)
    {
        var report = new JObject { ["picture"] = Path.GetFileName(picturePath), ["asked"] = words };
        int code = 2;
        string folder = Path.GetFullPath(outFolder);
        Directory.CreateDirectory(folder);
        try
        {
            string key = AiKey.Load(out _);
            if (key == null) throw new Exception("there is no OpenAI key");
            var ai = new OpenAiClient(key);
            byte[] picture = File.ReadAllBytes(picturePath);
            if (Mime(picture).Length == 0) throw new Exception("the picture is neither a JPEG nor a PNG");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            bool korean = words.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3);
            var seen = await SeeAsync(ai, picture, words, korean ? "Korean" : "English", cts.Token);
            report["seen"] = seen.raw; report["seenMs"] = seen.ms;
            if (!seen.found) throw new Exception("nothing to copy was made out in the picture");
            string title = SightRules.Title(seen.what), id = "item-" + Maker.NewId();
            var answers = new JArray();
            var sw = Stopwatch.StartNew();
            var s = await ShapeAsync(ai, id, seen, picture, words, cts.Token, null, a => answers.Add(a), effort);
            report["model"] = Sculptor.Model(ai); report["effort"] = string.IsNullOrEmpty(effort) ? Effort : effort; report["sculptMs"] = sw.ElapsedMilliseconds; report["answers"] = answers;
            code = 1;
            if (s == null) throw new Exception("it was seen but could not be sculpted");
            var item = new ItemDef { id = id, name = seen.idea.name, asked = words, kind = seen.idea.kind, uses = seen.idea.uses, sizeM = seen.idea.sizeM, color = seen.idea.color, note = seen.idea.note, status = "ready", progress = 1, inRoom = true, parts = s.parts, seat = s.seat, top = s.top };
            File.WriteAllText(Path.Combine(folder, "item.json"), Proto.ToJson(item, true), new UTF8Encoding(false));
            var made = Files(s.parts, title, SightRules.Metres(seen.idea.realCm));
            if (!made.Ok) throw new Exception("no 3D file: " + made.why);
            string stem = SightRules.FileStem(title);
            File.WriteAllBytes(Path.Combine(folder, stem + ".glb"), made.glb);
            File.WriteAllBytes(Path.Combine(folder, stem + ".png"), made.png);
            File.WriteAllBytes(Path.Combine(folder, stem + ".icon.png"), made.icon);
            report["file"] = stem + ".glb"; report["triangles"] = made.facts.triangles; report["colours"] = made.facts.primitives;
            code = 0;
            Console.WriteLine($"{seen.what}: {s.parts.Count} parts, height {s.height:0.00}, {made.facts.triangles} triangles in {sw.Elapsed.TotalSeconds:0.0} s -> {Path.Combine(folder, stem + ".glb")}");
        }
        catch (Exception e) { report["error"] = e.Message; Console.WriteLine("sculpt-photo: " + e.Message); }
        try { report["log"] = new JArray(Log.Recent.ToArray()); File.WriteAllText(Path.Combine(folder, "raw.json"), report.ToString(), new UTF8Encoding(false)); } catch { }
        return code;
    }
}
