using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>Captures a held object, stores its Tripo GLB with the inventory and exports the same file on request.</summary>
public sealed class SightHost
{
    readonly Core _core;

    sealed class Look
    {
        public string id = "", asked = "";
        public WorkJob job;
        public TripoClient tripo;
        public bool identify;
        public double deadline;
        public bool working;                    // the picture is here: the models are at it
        public CancellationTokenSource cts;
        public float sent;                      // the progress last told to the queue
    }
    Look _look;                                 // one at a time
    readonly Dictionary<string, string> _docOf = new();      // thing -> the document (kind "model") its file is, this sitting
    int _asked, _made, _exported; string _last = "";

    public SightHost(Core core)
    {
        _core = core;
        core.Work.Cancelled += j => { if (_look != null && _look.job == j) Stop(_look, "", cancelled: true); };
    }

    public object Diagnostics => new { asked = _asked, made = _made, exported = _exported, looking = _look != null, last = _last, provider = "Tripo image to 3D", standIn = TestImage != null };

    WorldState W => _core.State.world;
    bool Ko => _core.Talk.LastLanguage == "Korean";
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
        return action == SightRules.Export ? Export(thing ?? "") : Make(u?.text ?? thing ?? "", action == SightRules.Identify);
    }

    // ------------------------------------------------------------------ 1. asked: the headset is asked for a picture
    string Make(string asked, bool identify)
    {
        _asked++;
        var tripo = identify ? null : _core.World.Tripo();
        if (_look != null || _core.World.Making) return Tell("busy", "neutral");
        if (!identify && W.items.Count >= WorldRules.MaxItems) return Tell("full", "worried", 5);
        string standIn = TestImage;
        if (!_core.HasMr && standIn == null) { _last = "no headset"; return Tell("noHeadset", "curious", 6); }

        var look = _look = new Look { identify = identify, tripo = tripo, id = "eye-" + Maker.NewId(), asked = asked.Trim(), deadline = HostClock.Now + SightRules.FrameSeconds, cts = new CancellationTokenSource(TimeSpan.FromMinutes(7)) };
        look.job = _core.Work.Begin(identify || tripo == null ? "look" : "model", identify ? "Identify room object" : "Copy room object", look.asked, "mr", "Room object detection");
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
            Begin(look, jpeg, f);
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
    void Stop(Look look, string why, bool cancelled = false, string detail = null)
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
        _core.Work.End(look.job, false, "", "", detail ?? note, cancelled);
        Log.Info($"sight: {look.id}: over without a thing ({(cancelled ? "cancelled" : why)})");
        if (cancelled) return;
        string line = why == "notFound" && detail != null ? detail : Tell(why, "worried", 7);
        if (why == "notFound" && detail != null) _core.Say(line, "curious", 5, "room object unclear");
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
    }

    // ------------------------------------------------------------------ 3. seen and sculpted
    void Begin(Look look, byte[] picture, EyeFrame frame = null)
    {
        look.working = true;
        _core.Work.Step(look.job, SightRules.StepSee, 0.10f);
        var ai = _core.Talk.Enabled ? _core.Talk.Ai : null;
        var ct = look.cts.Token;
        _ = Task.Run(() => Build(look, picture, ai, frame, ct));
    }

    void Progress(Look look, string step, float p)
    {
        if (_look != look) return;
        if (step.Length == 0 && p - look.sent < 0.02f) return;
        look.sent = Math.Max(look.sent, p);
        _core.Work.Step(look.job, step, p);
    }

    async Task Build(Look look, byte[] picture, OpenAiClient ai, EyeFrame frame, CancellationToken ct)
    {
        string id = "item-" + Maker.NewId();
        string pending = Path.Combine(HostPaths.World, id + ".pending");
        try
        {
            var target = await PhotoObject.LocateAsync(ai, picture, look.asked, frame, ct);
            if (look.identify || look.tripo == null)
            {
                _core.Post(() =>
                {
                    if (_look != look) return;
                    _look = null; look.cts.Dispose();
                    _last = target.Label;
                    string line = $"I see a {target.Label}." + (look.identify ? "" : " Connect a Tripo key to make its 3D copy.");
                    _core.Work.End(look.job, look.identify, "", "", line);
                    _core.Say(line, "happy", 5, "room object recognized");
                });
                return;
            }
            var (item, glb) = await PhotoObject.CopyAsync(look.tripo, id, target, look.asked,
                (p, step) => _core.Post(() => Progress(look, step, p)), ct);
            Directory.CreateDirectory(HostPaths.World);
            await File.WriteAllBytesAsync(pending, glb, ct);
            ct.ThrowIfCancellationRequested();
            _core.Post(() => Finish(look, item, pending, ct));
        }
        catch (Exception e)
        {
            Discard(pending);
            string reason = e is RoomObjects.Unclear ? "notFound" : "tripoFailed";
            if (e is not OperationCanceledException) Log.Warn($"sight: {look.id}: {e.Message}");
            _core.Post(() => Stop(look, reason, detail: e.Message));
        }
    }

    static void Discard(string path)
    {
        try { File.Delete(path); }
        catch (IOException e) { Log.Warn("sight: pending model cleanup: " + e.Message); }
        catch (UnauthorizedAccessException e) { Log.Warn("sight: pending model cleanup: " + e.Message); }
    }

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
        return (folder, glb, made.png == null ? "" : png);
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
            Log.Info($"sight: {item.id}: exported {made.glb.Length} bytes to {glb} ({doc})");
            return doc;
        }
        catch (Exception e) { Log.Warn($"sight: {item.id}: the 3D file could not be written: {e.Message}"); return ""; }
    }

    void Finish(Look look, ItemDef item, string pending, CancellationToken ct)
    {
        try
        {
            if (_look != look) return;
            if (ct.IsCancellationRequested) { Stop(look, "tripoFailed"); return; }
            if (W.items.Count >= WorldRules.MaxItems) { Stop(look, "full"); return; }
            File.Move(pending, Path.Combine(HostPaths.World, item.model));
        }
        catch (Exception e) { Stop(look, "tripoFailed", detail: e.Message); return; }
        finally { Discard(pending); }

        W.items.Add(item);
        _core.World.Changed();
        _made++; _last = item.name;
        _look = null;
        look.cts.Dispose();
        _core.Work.Rename(look.job, item.name);
        _core.Work.End(look.job, true, "item", item.id, "Saved to your bag");
        Tell("bagged", "happy", 5, item.name);
        _core.Talk.Remember($"You made a 3D copy of the object the user showed. Its name is {item.name}. It is in the bag and can be placed or exported.");
    }

    string Export(string thing)
    {
        var item = SightRules.ExportTarget(W.items, thing);
        if (item == null) return Tell("noThing", "curious", 5);
        if (!_docOf.TryGetValue(item.id, out string doc) || !_core.Docs.Has(doc))
        {
            string title = SightRules.Title(item.name);
            Made made;
            if (!string.IsNullOrEmpty(item.model))
            {
                if (!item.model.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || !Maker.Exists(item.model))
                    return Tell("notWritten", "worried", 5);
                try { made = new Made { glb = File.ReadAllBytes(Path.Combine(HostPaths.World, item.model)) }; }
                catch (IOException) { return Tell("notWritten", "worried", 5); }
                catch (UnauthorizedAccessException) { return Tell("notWritten", "worried", 5); }
            }
            else made = Files(item.parts, title, SightRules.Metres(0));
            doc = Register(item, made, title, item.asked);
            if (doc.Length == 0) return Tell("notWritten", "worried", 5);
        }
        return _core.Docs.DeliverModel(doc);
    }

    /// <summary>`--check-glb <file>`: the file read back by the rules of the format and by the headset's reader; one line about what it holds. 0 = it opens.</summary>
    public static int CheckFile(string path)
    {
        try
        {
            byte[] glb = File.ReadAllBytes(path);
            var model = GlbReader.Read(glb, out string unreadable);
            if (model == null) { Console.WriteLine($"NOT READABLE: {Path.GetFileName(path)}: {unreadable}"); return 1; }
            Console.WriteLine($"OK: {Path.GetFileName(path)}, {glb.Length} bytes, {model.indices.Length / 3} triangles in {model.primitives} primitives");
            return 0;
        }
        catch (Exception e) { Console.WriteLine("check-glb: " + e.Message); return 2; }
    }

    /// <summary>Runs the same photo pipeline as a headset request and writes the bag entry and original GLB.</summary>
    public static async Task<int> RunAsync(string picturePath, string words, string effort, string outFolder)
    {
        try
        {
            string key = TripoKey.Load(out _) ?? throw new InvalidOperationException("No Tripo API key is configured.");
            using var tripo = new TripoClient(key, Log.Info);
            var visionKey = AiKey.Load(out _);
            var ai = string.IsNullOrEmpty(visionKey) ? null : new OpenAiClient(visionKey);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            var target = await PhotoObject.LocateAsync(ai, await File.ReadAllBytesAsync(picturePath), words, null, timeout.Token);
            var result = await PhotoObject.CopyAsync(tripo, "item-" + Maker.NewId(), target, words,
                (p, step) => Console.WriteLine($"{p:P0} {step}"), timeout.Token);
            Directory.CreateDirectory(outFolder);
            File.WriteAllBytes(Path.Combine(outFolder, result.item.model), result.glb);
            File.WriteAllText(Path.Combine(outFolder, "item.json"), Proto.ToJson(result.item, true));
            Console.WriteLine(Path.Combine(outFolder, result.item.model));
            return 0;
        }
        catch (RoomObjects.Unclear) { Console.WriteLine("No held object could be identified."); return 1; }
        catch (Exception e) { Console.WriteLine("sculpt-photo: " + e.Message); return 2; }
    }
}
