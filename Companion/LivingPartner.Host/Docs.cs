using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>
/// DOCUMENTS the partner makes for the user (the contract: Shared/Docs.cs; the rules: DocsRules.cs; the making:
/// DocsMaker.cs; the pages: DocsPages.cs; the PDF: DocsPdf.cs):
///   report    "메타 대회 알아보고 중요한 내용 PDF로 만들어 줘" - researched on the web when that can be done (else from
///             memory, and the first page says so), written, laid out as A4 pages, saved as a PDF
///   designs   "이 디자인 별로지 않아? 아이콘 새로 만들어 줘" - what the user points at on a display is cut out, and four
///             new versions are drawn: PNG each, and a PDF sheet of all of them
/// The snapshot's docs (View) lists the documents of this sitting; their pictures are served under /docs/&lt;id&gt;/ from
/// %LOCALAPPDATA%\LivingAIPartner\docs (emptied when the host starts); the user's files are in Documents\Bambi\
/// &lt;date time&gt; &lt;title&gt;\. "컴퓨터에 넣어 줘": the partner carries the file into the PC (DeliverShow; the snapshot's
/// show, kind "file"), leaves a copy on the Desktop, and a click on it there (doc.command reveal) opens the real file.
/// All state is touched on the core thread; the model calls run on the thread pool and post their results back.
/// </summary>
public sealed class DocsHost
{
    readonly Core _core;
    readonly DocsBook _book = new();
    readonly DeliverShow _carry = new();
    readonly Dictionary<string, string> _onDesktop = new();     // document id -> its PDF's copy on the Desktop
    LookSpot _testSpot; double _testSpotAt = -100;
    double _nextTick;
    int _reports, _designs;

    public DocsHost(Core core)
    {
        _core = core;
        // the pictures of earlier sittings are not kept (the documents themselves are the user's, in Documents\Bambi)
        try
        {
            Directory.CreateDirectory(HostPaths.Docs);
            foreach (string dir in Directory.GetDirectories(HostPaths.Docs))
                if (DocsRules.SafeId(Path.GetFileName(dir))) try { Directory.Delete(dir, true); } catch { }
        }
        catch (Exception e) { Log.Warn("docs: the store could not be put in order: " + e.Message); }
    }

    /// <summary>What the snapshot carries.</summary>
    public DocsView View => _book.View;
    public object Diagnostics => new { documents = View.items.Count, open = View.openId, page = View.page, reports = _reports, designs = _designs, carrying = _carry.stage, folder = UserRoot };

    // (a TRIAL host - LP_HOME - keeps to its own folder: its documents and its "Desktop" are in there, never the user's)
    public static string UserRoot => HostPaths.Trial ? Path.Combine(HostPaths.Root, "Documents", "Bambi") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bambi");
    static string Desktop
    {
        get
        {
            if (!HostPaths.Trial) return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string d = Path.Combine(HostPaths.Root, "Desktop");
            Directory.CreateDirectory(d);
            return d;
        }
    }
    public static string StoreOf(string id) => Path.Combine(HostPaths.Docs, id);

    bool Ko => _core.Talk.LastLanguage == "Korean";
    bool Enabled => _core.Talk.Enabled;
    static bool Hangul(string s) => (s ?? "").Any(ch => ch >= 0xAC00 && ch <= 0xD7A3);
    void Changed() => _core.MarkDirty(false, urgent: true);

    string Tell(string what, string mood = "happy", int n = 0, float seconds = 4)
    {
        string line = DocsRules.Line(what, Ko, "", n);
        if (line.Length > 0) _core.Say(line, mood, seconds, "conversation: docs " + what);
        return line;
    }

    // ------------------------------------------------------------------ asked for
    /// <summary>The plain wordings (DocsRules.Plain), with what there is right now. null = not about documents.</summary>
    public DocAsk Plain(string bare)
    {
        var meant = _book.Meant();
        return DocsRules.Plain(bare, View.items.Count > 0, View.openId.Length > 0, meant is { kind: DocsRules.Designs, status: DocsRules.Ready });
    }

    public string PromptSection() => DocsRules.PromptSection(View);

    /// <summary>Carries out one thing asked by voice or typed (the plain parser's, or the model's action). Says its own line and returns it.</summary>
    public string Act(Client c, DocAsk ask, Utterance u)
    {
        Log.Info($"docs: asked: {ask} ('{WorldRules.Short(u?.text, 70)}')");
        switch (ask.action)
        {
            case DocsRules.MakeReport: return StartReport(ask.arg, u?.text ?? ask.arg);
            case DocsRules.MakeDesigns: return ask.arg == DocsRules.SinglePng ? StartPicture(u?.text ?? "") : StartDesigns(c, u, ask.arg, null);
            case DocsRules.ShowDoc: return Show("", true);
            case DocsRules.DocPage:
                if (View.openId.Length == 0 && !_book.OpenDoc()) return Tell("none", "neutral");
                _book.Page(ask.arg, ask.n); Changed();
                return "";
            case DocsRules.PickOption: return Pick("", ask.n);
            case DocsRules.DeliverDoc: return Deliver("");
            case DocsRules.CloseDoc: return Close(true);
        }
        return "";
    }

    /// <summary>doc.command from a client (the board in the headset, the file in the partner's hands on the PC, lpctl).</summary>
    public void OnCommand(Client c, DocCommand d)
    {
        Log.Info($"docs: command from {c?.Role}: {d.cmd} id='{d.id}' n={d.n}");
        switch (d.cmd)
        {
            case "open": Show(d.id, false); break;
            case "close": Close(false); break;
            case "page": if (_book.Page("", d.n) > 0) Changed(); break;
            case "pick": Pick(d.id, d.n); break;
            case "deliver": Deliver(d.id); break;
            case "reveal": Reveal(c, d.id); break;
            case "delete":
                if (_book.Delete(d.id)) { Forget(d.id); if (_carry.docId == d.id && _carry.Running) Apply(_carry.Cancel()); Changed(); }
                break;
        }
    }

    string Show(string id, bool say)
    {
        if (View.items.Count == 0) return say ? Tell("none", "neutral") : "";
        // (C1) a 3D model has no pages for the board: the newest document that has is the one meant
        if ((_book.Find(id) ?? _book.Newest(true))?.kind == DocsRules.Model) id = View.items.LastOrDefault(x => x.kind != DocsRules.Model && x.status == DocsRules.Ready)?.id ?? "-";
        if (!_book.OpenDoc(id ?? "")) return "";
        Changed();
        return say ? Tell(_core.HasMr ? "show" : "showNoMr", "happy") : "";
    }

    /// <summary>Puts the board away. Returns the line said ("" when nothing was open and nothing is said).</summary>
    public string Close(bool say)
    {
        if (!_book.Close()) return "";
        Changed();
        return say ? Tell("close", "neutral") : "";
    }

    public bool IsOpen => View.openId.Length > 0;

    // ------------------------------------------------------------------ (C1) a 3D model the partner sculpted (Sight.cs)
    /// <summary>Is this document (still) in the list, and ready?</summary>
    public bool Has(string id) => _book.Find(id) is { status: DocsRules.Ready };

    /// <summary>
    /// A 3D model file the partner has just written (Sight.cs: a thing sculpted from what the user showed, or one of its
    /// things exported) joins the documents of this sitting as kind "model": THE file is the .glb (DocsRules.Carried),
    /// its thumbnail the rendered preview - so that it is carried into the PC, placed on the Desktop and revealed by
    /// the same path as a PDF. It is never put on the board in the room. Returns its id.
    /// </summary>
    public string AddModel(string title, string asked, string folder, string glbPath, string pngPath, byte[] previewPng)
    {
        string id = "mod-" + Guid.NewGuid().ToString("N")[..8];
        _book.Begin(id, DocsRules.Model, title, asked, HostClock.UnixMs, "", out var removed);
        foreach (string old in removed) Forget(old);
        try
        {
            if (previewPng != null)
            {
                Directory.CreateDirectory(StoreOf(id));
                File.WriteAllBytes(Path.Combine(StoreOf(id), "page-1.png"), previewPng);
                File.WriteAllBytes(Path.Combine(StoreOf(id), "page-1-s.png"), previewPng);
            }
        }
        catch (Exception e) { Log.Warn($"docs: {id}: the model's preview could not be stored: {e.Message}"); }
        var files = new List<DocFile> { new DocFile { name = Path.GetFileName(glbPath), kind = "glb", path = glbPath } };
        if (!string.IsNullOrEmpty(pngPath) && File.Exists(pngPath)) files.Add(new DocFile { name = Path.GetFileName(pngPath), kind = "png", path = pngPath });
        _book.Ready(id, title, 1, null, files, folder);
        Changed();
        Log.Info($"docs: {id}: a 3D model '{title}' joins the documents: {glbPath}");
        return id;
    }

    /// <summary>The partner carries the model's file into the PC (as "컴퓨터에 넣어 줘" does for a document). Returns the line said.</summary>
    public string DeliverModel(string id) => Has(id) ? Deliver(id) : "";

    string Pick(string id, int n)
    {
        var d = _book.Find(id) ?? (_book.Open is { kind: DocsRules.Designs } open ? open : View.items.LastOrDefault(x => x.kind == DocsRules.Designs && x.status == DocsRules.Ready));
        if (d == null || d.kind != DocsRules.Designs) return Tell("noDesigns", "neutral");
        var option = _book.Pick(d.id, n);
        if (option == null) return Tell("noOption", "curious");
        try { if (DocsMaker.WritePicked(d, n, option)) Log.Info($"docs: {d.id}: picked option {n} '{option.name}' -> picked.png in {d.folder}"); }
        catch (Exception e) { Log.Warn("docs: picked.png could not be written: " + e.Message); }
        Changed();
        return Tell("pick", "happy", n);
    }

    /// <summary>Under which a document's copy on the Desktop is remembered: the document and THE file it is right now (another option picked = another file).</summary>
    static string DesktopKey(DocItem d) => d == null ? "" : d.id + "|" + (DocsRules.Carried(d).file?.name ?? "");

    void Forget(string id)
    {
        foreach (string k in _onDesktop.Keys.Where(k => k.StartsWith(id + "|", StringComparison.Ordinal)).ToList()) _onDesktop.Remove(k);
        if (!DocsRules.SafeId(id)) return;
        try { if (Directory.Exists(StoreOf(id))) Directory.Delete(StoreOf(id), true); } catch { }
    }

    // ------------------------------------------------------------------ (W1) every document that is being made is a job of the work queue
    readonly Dictionary<string, WorkJob> _work = new();
    readonly Dictionary<string, CancellationTokenSource> _stops = new();
    readonly HashSet<string> _stopped = new();
    bool _workHooked;

    /// <summary>Track a background document job. The document worker owns its cancellation token.</summary>
    CancellationTokenSource WorkBegin(string id, string kind, string title, string asked)
    {
        if (!_workHooked)
        {
            _workHooked = true;
            // Cancel in the queue really stops the making (the model calls hang on this token)
            _core.Work.Cancelled += job =>
            {
                string doc = _work.FirstOrDefault(kv => kv.Value == job).Key;
                if (doc == null) return;
                Log.Info($"docs: {doc}: cancelled in the work queue");
                _stopped.Add(doc);
                if (_stops.TryGetValue(doc, out var stop)) { try { stop.Cancel(); } catch (ObjectDisposedException) { } }
            };
        }
        _work[id] = _core.Work.Begin(kind, WorkshopHost.EnglishOr(title, kind == DocsRules.Report ? "Research report" : "New design options"), asked, "pc", "documents");
        return _stops[id] = new CancellationTokenSource(TimeSpan.FromMinutes(9));
    }

    void WorkEnd(string id, bool ok, string note)
    {
        if (_stops.Remove(id,out var stop)) stop.Dispose();
        if (_work.Remove(id, out var job)) _core.Work.End(job, ok, ok ? "doc" : "", ok ? id : "", note, cancelled: _stopped.Remove(id));
    }

    // ------------------------------------------------------------------ a report
    string StartReport(string topic, string asked)
    {
        if (!Enabled) return Tell("noKey", "worried");
        topic = DocsRules.Topic(topic);
        if (topic.Length == 0) return Tell("noTopic", "curious");
        if (_book.AnyMaking(DocsRules.Report)) return Tell("busy", "neutral");
        bool korean = Hangul(asked) || (!(asked ?? "").Any(char.IsLetter) && Ko);
        string id = DocsRules.NewId(DocsRules.Report);
        _book.Begin(id, DocsRules.Report, topic, asked, HostClock.UnixMs, "Reading about it ...", out var removed);
        foreach (string old in removed) Forget(old);
        _reports++;
        Changed();
        Log.Info($"docs: {id}: a report on '{topic}' ({(korean ? "Korean" : "English")})");
        var model = new OpenAiDocsModel(_core.Talk.Ai);
        var cts = WorkBegin(id, DocsRules.Report, topic, asked);        // (W1: the job in the work queue; its Cancel stops this)
        Task.Run(async () =>
        {
            try
            {
                var made = await DocsMaker.ReportAsync(model, topic, asked, korean, StoreOf(id), UserRoot, DateTime.Now, Progress(id), s => Log.Info($"docs: {id}: {s}"), cts.Token);
                _core.Post(() => Finished(id, made, DocsRules.Report));
            }
            catch (Exception e) { _core.Post(() => Fail(id, e)); }
        });
        return Tell("reportStart", "happy");
    }

    Action<string, float, string> Progress(string id) => (stage, p, title) => _core.Post(() =>
    {
        if (_book.Stage(id, stage, p, title)) _core.MarkDirty(false);
        if (_work.TryGetValue(id, out var job)) { _core.Work.Step(job, stage, p); if (!string.IsNullOrEmpty(title)) _core.Work.Rename(job, WorkshopHost.EnglishOr(title, job.title)); }
    });

    void Finished(string id, DocsMade made, string kind)
    {
        if (_stopped.Contains(id)) { Fail(id,new OperationCanceledException()); return; }
        if (!_book.Ready(id, made.title, made.pages, made.options, made.files, made.folder)) { WorkEnd(id, false, "It was thrown away."); return; }
        bool singlePng = made.options.Count == 1 && made.files.All(f => f.kind == "png");
        WorkEnd(id, true, singlePng ? "PNG image" : kind == DocsRules.Report ? $"A PDF of {made.pages} page(s)." : $"{made.options.Count} option(s), as PNG and PDF.");
        // Opening the completed queue entry displays its document.
        Changed();
        Log.Info($"docs: {id}: READY '{made.title}' ({kind}, {made.pages} pages, {made.options.Count} options, {made.files.Count} files) in {made.folder}");
        if (singlePng) _core.Say(Ko ? "PNG를 저장했어. 작업 목록에서 열어 봐." : "Your PNG is ready in the work queue.", "happy", 4, "docs: PNG ready");
        else if (kind == DocsRules.Report)
        {
            _core.Talk.Remember($"You researched \"{made.title}\" for the user ({(made.web ? "on the web" : "from memory, NOT checked on the web")}) and made a {made.pages}-page PDF of it, saved in {made.folder}. Its summary: {made.summary} (deliverDoc carries it into their computer.)");
            Tell(_core.HasMr ? "reportReady" : "reportReadyNoMr", "happy", made.pages, 5);
        }
        else
        {
            _core.Talk.Remember($"You drew {made.options.Count} new design options for what the user pointed at ({made.title}): {string.Join("; ", made.options.Select(o => o.name))}. {made.summary} The PNG files and a PDF sheet are in {made.folder}. (pickOption marks the one they choose; deliverDoc carries it into their computer.)");
            Tell(_core.HasMr ? "designReady" : "designReadyNoMr", "happy", made.options.Count, 5);
        }
    }

    void Fail(string id, Exception e)
    {
        string line = e is DocsProblem p ? p.Line : "failed";
        Log.Warn($"docs: {id}: FAILED: {e.Message}");
        if (_stopped.Contains(id)) { WorkEnd(id, false, "Stopped."); if (_book.Fail(id, "Stopped.")) Changed(); _core.Say(Ko ? "알겠어, 그건 그만둘게." : "Okay, I stopped that one.", "neutral", 4, "docs: cancelled"); return; }
        WorkEnd(id, false, e is DocsProblem ? "Nothing to redesign was found there." : "It could not be made.");
        if (_book.Fail(id, e is DocsProblem ? "Nothing to redesign was found there." : "It could not be made.")) Changed();
        Tell(line, "worried");
    }

    // ------------------------------------------------------------------ design options
    string StartPicture(string request)
    {
        if (!Enabled) return Tell("noKey","worried");
        if (_book.AnyMaking(DocsRules.Designs)) return Tell("busy","neutral");
        string id = DocsRules.NewId(DocsRules.Designs), prompt = NameRules.Strip(request);
        _book.Begin(id,DocsRules.Designs,"PNG",prompt,HostClock.UnixMs,"Drawing the image",out var removed);
        foreach (string old in removed) Forget(old);
        _designs++;
        var cts = WorkBegin(id,DocsRules.Designs,"PNG",prompt);
        var model = new OpenAiDocsModel(_core.Talk.Ai);
        _ = Task.Run(async () =>
        {
            try
            {
                var image = await DocsMaker.PictureAsync(model,prompt,StoreOf(id),UserRoot,DateTime.Now,cts.Token);
                _core.Post(() => Finished(id,image,DocsRules.Designs));
            }
            catch (Exception e) { _core.Post(() => Fail(id,e)); }
        });
        Changed();
        string line = Ko ? "PNG 작업을 대기줄에 넣었어." : "I added the PNG to the work queue.";
        _core.Say(line,"happy",4,"docs: PNG queued"); return line;
    }

    /// <summary>A check from lpctl on this PC: the pixel that stands for the pointing hand for the next twelve seconds (`lpctl doc point px py`).</summary>
    public void TestPoint(float px, float py)
    {
        var mons = _core.Desktop.Monitors;
        // (two numbers between 0 and 1: fractions of the main display - a check need not know where the displays are)
        if (px > 0 && px < 1 && py > 0 && py < 1 && (mons.FirstOrDefault(x => x.primary) ?? mons.FirstOrDefault()) is { } main) { px = main.bounds.x + px * main.bounds.w; py = main.bounds.y + py * main.bounds.h; }
        if (ScreenMath.FindMonitor(mons, px, py) is not { } m) { Log.Warn($"docs: TEST pointer: no display at ({px:0}, {py:0})"); return; }
        ScreenMath.PixelToUV(m, px, py, out float u, out float v);
        _testSpot = new LookSpot { source = LookRules.Command, monitorId = m.id, u = Math.Clamp(u, 0f, 1f), v = Math.Clamp(v, 0f, 1f) };
        _testSpotAt = HostClock.Now;
        Log.Info($"docs: TEST pointer at ({px:0}, {py:0}) = {m.id} ({u:0.000}, {v:0.000}) for the next 12 s");
    }

    string StartDesigns(Client c, Utterance u, string what, LookSpot named)
    {
        if (!Enabled) return Tell("noKey", "worried");
        if (_book.AnyMaking(DocsRules.Designs)) return Tell("busy", "neutral");
        double now = HostClock.Now;
        // where the user means: exactly as for looking (the hand's dwell, else the gaze's, else the last seconds)
        var spot = named ?? (now - _testSpotAt < 12 ? _testSpot : null) ?? _core.Tasks.ResolveLook(c, u?.t0 ?? now - 1.5, u?.t1 ?? now);
        _testSpotAt = -100;
        var mons = _core.Desktop.Monitors;
        var m = spot.Pointed ? ScreenMath.FindMonitor(mons, spot.monitorId) : null;
        if (m == null) { Log.Info($"docs: designs asked for, but nothing is pointed at (source={spot.source})"); return Tell("point", "curious", 0, 5); }
        string asked = u?.text ?? "";
        bool korean = Hangul(asked) || (asked.Length == 0 && Ko);
        string id = DocsRules.NewId(DocsRules.Designs);
        what = string.IsNullOrWhiteSpace(what) ? "icon" : what;
        _book.Begin(id, DocsRules.Designs, "New " + what + " options", asked, HostClock.UnixMs, "Looking at it ...", out var removed);
        foreach (string old in removed) Forget(old);
        _designs++;
        Changed();
        var b = m.bounds;
        var crop = LookRules.Crop(b.w, b.h, spot.u, spot.v);
        Log.Info($"docs: {id}: new {what} options; source={spot.source} monitor={m.id} (u, v)=({spot.u:0.000}, {spot.v:0.000}) close-up=[{crop.x},{crop.y} {crop.w}x{crop.h}] of {b.w}x{b.h}");
        var model = new OpenAiDocsModel(_core.Talk.Ai);
        var cts = WorkBegin(id, DocsRules.Designs, "New " + what + " options", asked);        // (W1: the job in the work queue; its Cancel stops this)
        Task.Run(async () =>
        {
            try
            {
                byte[] overview, closeRing, closePlain;
                float zoom = LookRules.CropZoom(crop.w);
                using (var full = LookShots.Grab(b))
                {
                    overview = LookShots.Overview(full, spot);
                    closeRing = LookShots.CloseUp(full, spot, crop);
                    closePlain = ClosePlain(full, crop, zoom);
                }
                var (sx, sy) = LookRules.SpotInCrop(crop, b.w, b.h, spot.u, spot.v, zoom);
                var made = await DocsMaker.DesignsAsync(model, overview, closeRing, closePlain, spot.u, spot.v, sx, sy, what, asked, korean, StoreOf(id), UserRoot, DateTime.Now, Progress(id),
                    line => _core.Post(() => { if (line.Length > 0) _core.Say(line, "curious", 6, "conversation: docs designs"); }),
                    s => Log.Info($"docs: {id}: {s}"), cts.Token);
                _core.Post(() => Finished(id, made, DocsRules.Designs));
            }
            catch (Exception e) { _core.Post(() => Fail(id, e)); }
        });
        return Tell("designStart", "curious");
    }

    /// <summary>`lpctl doc designs px py ["words"]`: the pixel names the spot (a check without a headset).</summary>
    public void DesignsAt(Client c, float px, float py, string words)
    {
        TestPoint(px, py);
        double now = HostClock.Now;
        StartDesigns(c, new Utterance { text = string.IsNullOrWhiteSpace(words) ? "이 아이콘 별로지 않아? 새로 만들어 줘" : words, t0 = now - 1.5, t1 = now, source = "lpctl" }, "", null);
    }

    public void ReportOn(string topic) => StartReport(topic, topic);

    /// <summary>The close-up as LookShots.CloseUp makes it, without the ring: what the design is cut out of.</summary>
    static byte[] ClosePlain(Bitmap full, RectI crop, float zoom)
    {
        int w = (int)Math.Round(crop.w * zoom), h = (int)Math.Round(crop.h * zoom);
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(full, new Rectangle(0, 0, w, h), crop.x, crop.y, crop.w, crop.h, GraphicsUnit.Pixel);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ into the PC
    DeliverShow.World World() => new()
    {
        owner = _core.State.character.owner, hasPc = _core.HasPc, hasMr = _core.HasMr, crossing = _core.Coord.Machine.IsActive,
        calibrated = _core.Shows.Calibrated().Count > 0,
    };

    string Deliver(string id)
    {
        var d = _book.Meant(id);
        if (d == null) return Tell("none", "neutral");
        if (d.status == DocsRules.Making) return Tell("making", "neutral");
        // THE file of the document (DocsRules.Carried): the PDF - or the picked option as a picture. One file, one
        // name, one thumbnail, in the room and inside the PC, on the Desktop and behind the click.
        var (file, icon) = DocsRules.Carried(d);
        if (d.status != DocsRules.Ready || file == null || !File.Exists(file.path)) return Tell("gone", "worried");
        var w = World();
        Log.Info($"docs: {d.id}: deliver '{file.name}' ({file.kind}; thumbnail {icon}) (the partner is {(w.owner == Env.Pc ? "inside the PC" : "in the room")}; PC client {(w.hasPc ? "yes" : "no")}, headset {(w.hasMr ? "yes" : "no")}, a display it knows {(w.calibrated ? "yes" : "no")}{(w.crossing ? ", crossing" : "")})");
        _carryIcon = icon;
        return Apply(_carry.Begin(HostClock.Now, w, d.id, file.name));
    }

    string _carryIcon = "";     // the thumbnail of the file that is being carried (the show's icon)

    string Apply(CarryAct act)
    {
        if (act == CarryAct.None) return "";
        string line = "";
        var d = _book.Find(_carry.docId);
        if (act.HasFlag(CarryAct.Cross)) Cross();
        bool placed = false;
        if (act.HasFlag(CarryAct.Place) && d != null)
        {
            try
            {
                // (asked for twice: the copy that is there already is the one - the Desktop is not filled with copies;
                //  another option picked since: that is another file, and it is placed as well)
                string key = DesktopKey(d);
                if (_onDesktop.TryGetValue(key, out string have) && File.Exists(have)) placed = true;
                else
                {
                    var copies = DocsMaker.Place(d, Desktop);
                    placed = copies.Count > 0;
                    if (placed) _onDesktop[key] = copies[0];
                    foreach (string p in copies) Log.Info($"docs: {d.id}: placed on the Desktop: {p}");
                }
            }
            catch (Exception e) { Log.Warn($"docs: {d.id}: could not be placed on the Desktop: {e.Message}"); }
            if (placed) _book.Delivered(d.id);
        }
        if (act.HasFlag(CarryAct.SayCarry)) line = Tell("carry", "happy");
        if (act.HasFlag(CarryAct.SayPlaced)) line = Tell(placed ? "placed" : "notPlaced", placed ? "happy" : "worried", 0, 5);
        if (act.HasFlag(CarryAct.SayPlacedNoPc)) line = Tell(placed ? "placedNoPc" : "notPlaced", placed ? "happy" : "worried", 0, 5);
        if (act.HasFlag(CarryAct.Changed) || act.HasFlag(CarryAct.Ended))
        {
            if (_carry.Running)
            {
                Log.Info($"docs: carrying '{_carry.name}' -> {_carry.stage}");
                // (name = the file's name, icon = its thumbnail: both clients draw THIS file - a PDF sheet with its first page, or the picked picture)
                _core.Shows.File(_carry.stage, _carry.name, _carryIcon.Length > 0 ? _carryIcon : $"/docs/{_carry.docId}/page-1-s.png", _carry.since, (float)_carry.StageSeconds);
            }
            else { Log.Info("docs: carrying over"); _core.Shows.FileEnd(); }
        }
        Changed();
        return line;
    }

    /// <summary>
    /// The partner is sent through the screen as "여기로 들어가" does when nothing is pointed at: the headsets get the
    /// intent's result with the spot it last went through (when the headset still knows that display), else the
    /// middle of the first display it knows.
    /// </summary>
    void Cross()
    {
        var known = _core.Shows.Calibrated();
        var last = _core.Coord.Info.target;
        bool same = last != null && known.Any(m => m.id == last.monitorId) && (last.u != 0 || last.v != 0);
        string monitor = same ? last.monitorId : known.FirstOrDefault()?.id ?? "";
        var pointer = new PointerSample { t = HostClock.Now, monitorId = monitor, u = same ? Math.Clamp(last.u, 0.05f, 0.95f) : 0.5f, v = same ? Math.Clamp(last.v, 0.05f, 0.95f) : 0.6f, valid = true };
        Log.Info($"docs: crossing into {monitor} at ({pointer.u:0.00}, {pointer.v:0.00}){(same ? " (where it last went through)" : "")}");
        _core.Broadcast(Msg.IntentResult, new IntentResultMsg { text = "", kind = "enterPortal", pointer = pointer }, _core.IsMr);
    }

    /// <summary>The user clicked the file in the partner's hands (the PC client), or `lpctl doc reveal`: the real file is opened on the PC.</summary>
    void Reveal(Client c, string id)
    {
        // (it opens a program on this PC: only for a client on this PC)
        if (c != null && !c.Loopback) { Log.Warn($"docs: reveal from {c.Role} at {c.Remote} turned away: not from this PC"); return; }
        var d = _book.Find(id) ?? _book.Find(_carry.docId) ?? View.items.LastOrDefault(x => x.delivered) ?? _book.Meant();
        _onDesktop.TryGetValue(DesktopKey(d), out string copy);
        var (how, path) = DocsRules.Reveal(d, File.Exists, Directory.Exists, copy ?? "");
        if (how.Length == 0) { Log.Warn($"docs: reveal '{id}': nothing to open"); Tell("gone", "worried"); return; }
        Log.Info($"docs: {d.id}: reveal -> {how} {path}{(_core.NoAutoOpen ? " (NOT opened: --no-auto-open)" : "")}");
        if (_core.NoAutoOpen) return;
        try
        {
            if (how == DocsRules.RevealSelect) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) { Log.Warn("docs: could not be opened: " + e.Message); }
    }

    public void Tick(double now)
    {
        if (now < _nextTick) return;
        _nextTick = now + 0.2;
        if (_carry.Running) Apply(_carry.Tick(now, World()));
    }
}

/// <summary>The OpenAI models of documents: the web search of the Responses API for the research, the vision model for the writing and the look, the image model (with the cut-out as its reference) for the drawings.</summary>
sealed class OpenAiDocsModel : IDocsModel
{
    readonly OpenAiClient _ai;
    volatile bool _plain;       // the image model refused a see-through background once: it is not asked again

    public OpenAiDocsModel(OpenAiClient ai) { _ai = ai; }

    string TextModel => Environment.GetEnvironmentVariable("LP_OPENAI_DOCS_MODEL") is { Length: > 0 } m ? m : _ai.VisionModel;

    public async Task<DocsResearch> ResearchAsync(string topic, string asked, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("LP_DOCS_NO_WEB") == "1") return new DocsResearch { web = false, how = "LP_DOCS_NO_WEB" };
        try
        {
            var (text, sources, searches) = await _ai.WebResearchAsync(DocsRules.ResearchSystem, DocsRules.ResearchUser(topic, asked, DateTime.Now), ct, TextModel, "medium");
            // (an answer for which nothing was searched is the model's memory, whatever it says)
            if (searches == 0 || text.Trim().Length < 200) return new DocsResearch { web = false, how = $"the model searched {searches} times and wrote {text.Trim().Length} characters" };
            return new DocsResearch { web = true, notes = text, sources = sources.Select(s => new ReportSource { title = s.title, url = s.url }).ToList(), how = $"{searches} searches, {sources.Count} pages cited" };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { return new DocsResearch { web = false, how = "the web search failed: " + WorldRules.Short(e.Message, 160) }; }
    }

    public Task<JObject> AskJsonAsync(string system, string user, IReadOnlyList<(string mime, byte[] data, string name)> pictures, JObject schema, int maxTokens, CancellationToken ct) =>
        _ai.AskJsonAsync(system, user, pictures, schema, maxTokens, ct, TextModel, "low");

    public async Task<byte[]> DrawAsync(byte[] referencePng, string transparent, string plain, CancellationToken ct)
    {
        // no reference: a NEW picture from the description alone (design options - the image model must not start from
        // the design that is to be replaced). GenerateImageAsync asks for a see-through background and, when the model
        // cannot do that, once more for a plain white one (it turns the words of the request itself).
        if (referencePng == null) return await _ai.GenerateImageAsync(transparent, "1024x1024", true, ct);
        if (!_plain)
        {
            try { return await _ai.EditImageAsync(referencePng, transparent, "1024x1024", ct, true); }
            catch (OperationCanceledException) { throw; }
            // 400 = the request itself was not accepted (this model draws no see-through background); anything else would fail the same way again
            catch (Exception e) when (e.Message.Contains("HTTP 400")) { _plain = true; Log.Warn("docs: a transparent background was refused (" + WorldRules.Short(e.Message, 120) + "); drawing on a plain white one"); }
        }
        return await _ai.EditImageAsync(referencePng, plain, "1024x1024", ct);
    }
}
