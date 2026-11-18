using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>What the research came to: notes with the pages they come from - or nothing, when the web could not be searched (then the report is written from memory and says so).</summary>
public sealed class DocsResearch
{
    public string notes = "";
    public List<ReportSource> sources = new();
    public bool web;
    public string how = "";         // one line for the log: how it was done, or why not
}

/// <summary>The models a document needs. The host gives the OpenAI ones (Docs.cs); a test gives ones that answer from a list.</summary>
public interface IDocsModel
{
    /// <summary>Looks the topic up on the web (one call). web = false when that cannot be done: never an invented source.</summary>
    Task<DocsResearch> ResearchAsync(string topic, string asked, CancellationToken ct);
    /// <summary>One question, with pictures or without, answered as JSON of the given shape.</summary>
    Task<JObject> AskJsonAsync(string system, string user, IReadOnlyList<(string mime, byte[] data, string name)> pictures, JObject schema, int maxTokens, CancellationToken ct);
    /// <summary>
    /// A new picture (1024 px, PNG). referencePng null: drawn from the description alone (design options: always - the
    /// current design must not be the image model's starting picture); else with that picture attached.
    /// transparent / plain: the same request for a see-through and for a white background - the model gets the one it can do.
    /// </summary>
    Task<byte[]> DrawAsync(byte[] referencePng, string transparent, string plain, CancellationToken ct);
}

/// <summary>What was made: for the DocItem (the pages and options are in the store folder, the files in the user's folder).</summary>
public sealed class DocsMade
{
    public string title = "", folder = "", summary = "";
    public int pages;
    public bool web;
    public List<DocFile> files = new();
    public List<DocOption> options = new();
}

/// <summary>Something the partner has a line for (DocsRules.Line): nothing was found at the spot ...</summary>
public sealed class DocsProblem : Exception
{
    public readonly string Line;
    public DocsProblem(string line, string message) : base(message) { Line = line; }
}

/// <summary>
/// Making a document, from the request to the files: the research and the writing (or the look and the drawings), the
/// pages as pictures in the store folder (served to the clients under /docs/&lt;id&gt;/), the PDF and the PNGs in a new
/// folder of the user's (Documents\Bambi\&lt;date time&gt; &lt;title&gt;\ - never one that is there). No host in it: the models,
/// the folders and the clock are given, so that a test runs all of it with made-up answers.
/// progress(stage, 0..1, title or null) is called as it goes.
/// </summary>
public static class DocsMaker
{
    public const int Options = DocsRules.DesignOptions;

    static string NewFolder(string userRoot, DateTime now, string title)
    {
        Directory.CreateDirectory(userRoot);
        string name = DocsRules.FreeFolder(now, title, n => Directory.Exists(Path.Combine(userRoot, n)) || File.Exists(Path.Combine(userRoot, n)));
        string dir = Path.Combine(userRoot, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Writes a new file into a folder under a name that is free there; returns its path.</summary>
    static string WriteNew(string dir, string stem, string ext, byte[] data)
    {
        string name = DocsRules.FreeFile(stem, ext, n => File.Exists(Path.Combine(dir, n)) || Directory.Exists(Path.Combine(dir, n)));
        string path = Path.Combine(dir, name);
        using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) f.Write(data, 0, data.Length);
        return path;
    }

    // ------------------------------------------------------------------ a report
    public static async Task<DocsMade> ReportAsync(IDocsModel model, string topic, string asked, bool korean, string storeDir, string userRoot, DateTime now,
        Action<string, float, string> progress, Action<string> log, CancellationToken ct)
    {
        progress?.Invoke("Reading about it ...", 0.08f, null);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var research = await model.ResearchAsync(topic, asked, ct) ?? new DocsResearch();
        bool web = research.web && !string.IsNullOrWhiteSpace(research.notes);
        log?.Invoke($"research in {sw.ElapsedMilliseconds} ms: {(web ? $"on the web, {research.notes.Length} characters of notes, {research.sources.Count} pages read" : "NOT on the web - written from memory")}{(research.how.Length > 0 ? " (" + research.how + ")" : "")}");

        progress?.Invoke("Writing ...", 0.5f, null);
        sw.Restart();
        var json = await model.AskJsonAsync(DocsRules.WriterSystem, DocsRules.WriterUser(topic, asked, korean, web ? research.notes : "", research.sources, now),
            Array.Empty<(string, byte[], string)>(), DocsRules.ReportSchema(), 8000, ct);
        var data = ReportData.From(json, topic, korean, web, research.sources, now);
        if (!data.Usable) throw new Exception("the report came back empty");
        log?.Invoke($"written in {sw.ElapsedMilliseconds} ms: '{data.title}', {data.sections.Count} sections, {data.facts.Count} facts, {data.sections.Count(s => s.table.Any)} tables, {data.sources.Count} sources");

        progress?.Invoke("Laying out the pages ...", 0.84f, data.title);
        ct.ThrowIfCancellationRequested();
        using var pages = new DocsPages();
        pages.Report(data);
        int count = pages.SavePngs(storeDir);
        progress?.Invoke($"Laying out page {count} ...", 0.94f, null);
        byte[] pdf = DocsPdf.Write(pages.Jpegs(), data.title);
        string dir = NewFolder(userRoot, now, data.title);
        string path = WriteNew(dir, data.title, ".pdf", pdf);
        log?.Invoke($"{count} pages, PDF {pdf.Length / 1024} kB -> {path}");
        return new DocsMade
        {
            title = data.title, folder = dir, pages = count, web = web, summary = data.summary,
            files = { new DocFile { name = Path.GetFileName(path), kind = "pdf", path = path } },
        };
    }

    // ------------------------------------------------------------------ design options
    static byte[] Png(Image img) { using var ms = new MemoryStream(); img.Save(ms, ImageFormat.Png); return ms.ToArray(); }

    public static async Task<DocsMade> PictureAsync(IDocsModel model,string prompt,string store,string root,DateTime now,CancellationToken ct)
    {
        byte[] bytes = await model.DrawAsync(null,prompt,prompt,ct);
        using var stream = new MemoryStream(bytes);
        using var img = Image.FromStream(stream);
        ct.ThrowIfCancellationRequested();
        byte[] png = img.RawFormat.Guid == ImageFormat.Png.Guid ? bytes : Png(img);
        Directory.CreateDirectory(store);
        File.WriteAllBytes(Path.Combine(store,"option-1.png"),png);
        float factor = Math.Min(1,384f/Math.Max(img.Width,img.Height));
        using (var thumb = new Bitmap(img, Math.Max(1,(int)(img.Width*factor)),Math.Max(1,(int)(img.Height*factor))))
            thumb.Save(Path.Combine(store,"option-1-s.png"),ImageFormat.Png);
        ct.ThrowIfCancellationRequested();
        string title = DocsRules.SafeTitle(prompt,"Bambi image"), folder = NewFolder(root,now,title);
        string path = WriteNew(folder,title,".png",png);
        return new DocsMade { title=title, folder=folder,
            options={new DocOption {name=title,picture="option-1.png",picked=true}},
            files={new DocFile {kind="png",path=path,name=Path.GetFileName(path)}} };
    }

    static Bitmap Scaled(Image src, Rectangle from, int side)
    {
        var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var wrap = new ImageAttributes();
        wrap.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(src, new Rectangle(0, 0, side, side), from.X, from.Y, from.Width, from.Height, GraphicsUnit.Pixel, wrap);
        return bmp;
    }

    /// <summary>
    /// overview: the whole display with the ring (JPEG); closeRing: the close-up with the small ring, for the model;
    /// closePlain: the same close-up without it - what is cut out; spotX / spotY: the spot in the close-up, in its pixels.
    /// said(line): what the partner says once it has seen what it is (before the drawings).
    /// </summary>
    public static async Task<DocsMade> DesignsAsync(IDocsModel model, byte[] overview, byte[] closeRing, byte[] closePlain, float spotU, float spotV, float spotX, float spotY,
        string what, string asked, bool korean, string storeDir, string userRoot, DateTime now,
        Action<string, float, string> progress, Action<string> said, Action<string> log, CancellationToken ct)
    {
        progress?.Invoke("Looking at it ...", 0.06f, null);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var json = await model.AskJsonAsync(DocsRules.DesignSystem, DocsRules.DesignUser(what, asked, korean, spotU, spotV),
            new (string, byte[], string)[] { ("image/jpeg", overview, "display.jpg"), ("image/png", closeRing, "close-up.png") }, DocsRules.DesignSchema(), 3000, ct);
        var wishes = (json["options"] as JArray ?? new JArray()).OfType<JObject>()
            .Select(o => (style: ((string)o["style"] ?? (string)o["name"] ?? "").Trim(), rationale: DocsRules.Debrand(((string)o["rationale"] ?? "").Trim()), prompt: ((string)o["prompt"] ?? "").Trim()))
            .Where(o => o.prompt.Length > 10).Take(Options).ToList();
        // FOUR DIFFERENT DIRECTIONS: each option gets a way of drawing of its own (the model's choice; the same one
        // twice, or none, gets the next that is free) - its name says which, and the image model is told exactly how
        var styles = DocsRules.PickStyles(wishes.Select(o => o.style));
        var asks = wishes.Select((o, i) => (name: styles[i].label, style: styles[i], o.rationale, o.prompt)).ToList();
        bool found = (bool?)json["found"] ?? false;
        // NO BRAND in what is shown, said or drawn: the thing is named by what it is for (the model is told so; Debrand is the net under it)
        string subject = DocsRules.Debrand(((string)json["title"] ?? "").Trim()), purpose = DocsRules.Debrand(((string)json["purpose"] ?? "").Trim());
        string whatIs = DocsRules.Debrand(((string)json["what"] ?? "").Trim()), weak = DocsRules.Debrand(((string)json["weak"] ?? "").Trim());
        log?.Invoke($"looked in {sw.ElapsedMilliseconds} ms: found={found} '{subject}' box=({(float?)json["x"] ?? 0:0.00}, {(float?)json["y"] ?? 0:0.00} {(float?)json["w"] ?? 0:0.00}x{(float?)json["h"] ?? 0:0.00}) {asks.Count} directions: {string.Join(" | ", asks.Select(a => a.name))}{(DocsRules.NamesBrand((string)json["title"] + " " + (string)json["purpose"] + " " + (string)json["spoken"]) ? " (a brand's name was taken out of the model's words)" : "")}");
        if (!found || asks.Count == 0) throw new DocsProblem("designNotFound", "no design was found at the spot");
        string title = DocsRules.SafeTitle((subject.Length > 0 ? subject : what) + " - new options", "New design options");

        // the design as it is now, cut out of the close-up with a margin: only for the sheet ("Original") and the board -
        // the image model never gets it (it used to be its starting picture, and the four "new" ones were four copies of it)
        Directory.CreateDirectory(storeDir);
        byte[] original;
        using (var close = new Bitmap(new MemoryStream(closePlain)))
        {
            var (x, y, side) = DocsRules.CutOut(close.Width, close.Height, (float?)json["x"] ?? 0, (float?)json["y"] ?? 0, (float?)json["w"] ?? 0, (float?)json["h"] ?? 0, spotX, spotY);
            using var cut = Scaled(close, new Rectangle(x, y, side, side), Math.Clamp(side, 512, 1024));
            original = Png(cut);
            log?.Invoke($"cut out [{x},{y} {side}x{side}] of the {close.Width}x{close.Height} close-up -> original.png {cut.Width} px");
        }
        File.WriteAllBytes(Path.Combine(storeDir, "original.png"), original);
        said?.Invoke(DocsRules.Debrand(((string)json["spoken"] ?? "").Trim()));

        // the drawings, each from its description ALONE (no reference picture): the first by itself (it finds out
        // whether the model can do a see-through background), the others together
        var drawn = new byte[asks.Count][];
        int done = 0;
        async Task Draw(int i)
        {
            var t = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                drawn[i] = await model.DrawAsync(null, DocsRules.DrawPrompt(subject.Length > 0 ? subject : what, purpose, asks[i].style, asks[i].prompt, true), DocsRules.DrawPrompt(subject.Length > 0 ? subject : what, purpose, asks[i].style, asks[i].prompt, false), ct);
                log?.Invoke($"option {i + 1} '{asks[i].name}' drawn in {t.ElapsedMilliseconds} ms ({(drawn[i]?.Length ?? 0) / 1024} kB)");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { log?.Invoke($"option {i + 1} '{asks[i].name}' was NOT drawn: {e.Message}"); }
            int n = Interlocked.Increment(ref done);
            if (n < asks.Count) progress?.Invoke($"Drawing option {n + 1} of {asks.Count} ...", 0.16f + 0.7f * n / asks.Count, null);
        }
        progress?.Invoke($"Drawing option 1 of {asks.Count} ...", 0.16f, title);
        await Draw(0);
        await Task.WhenAll(Enumerable.Range(1, asks.Count - 1).Select(Draw));

        // what came of it, numbered in order; each as it is, and small for the headset's cards
        var options = new List<(string name, string rationale, byte[] png)>();
        for (int i = 0; i < asks.Count; i++)
        {
            if (drawn[i] == null) continue;
            try
            {
                using var img = Image.FromStream(new MemoryStream(drawn[i]));
                int n = options.Count + 1;
                byte[] png = img.RawFormat.Guid == ImageFormat.Png.Guid ? drawn[i] : Png(img);
                File.WriteAllBytes(Path.Combine(storeDir, $"option-{n}.png"), png);
                using (var small = Scaled(img, new Rectangle(0, 0, img.Width, img.Height), 384)) small.Save(Path.Combine(storeDir, $"option-{n}-s.png"), ImageFormat.Png);
                options.Add((DocsRules.OptionName(n, asks[i].name), asks[i].rationale, png));
            }
            catch (Exception e) { log?.Invoke($"option {i + 1}: what came back is no picture ({e.Message})"); }
        }
        if (options.Count < 2) throw new Exception($"only {options.Count} of {asks.Count} options could be drawn");

        progress?.Invoke("Putting the sheet together ...", 0.9f, null);
        ct.ThrowIfCancellationRequested();
        int count; byte[] pdf;
        var pictures = new List<Image>();
        try
        {
            using var orig = Image.FromStream(new MemoryStream(original));
            foreach (var o in options) pictures.Add(Image.FromStream(new MemoryStream(o.png)));
            using var pages = new DocsPages();
            pages.DesignSheet(title, whatIs, weak, orig, options.Select((o, i) => (o.name, o.rationale, pictures[i])).ToList(), korean, now);
            count = pages.SavePngs(storeDir);
            pdf = DocsPdf.Write(pages.Jpegs(), title);
        }
        finally { foreach (var p in pictures) p.Dispose(); }

        string dir = NewFolder(userRoot, now, title);
        var made = new DocsMade { title = title, folder = dir, pages = count, summary = $"{whatIs} {weak}".Trim() };
        string sheet = WriteNew(dir, title, ".pdf", pdf);
        made.files.Add(new DocFile { name = Path.GetFileName(sheet), kind = "pdf", path = sheet });
        string kept = WriteNew(dir, "original", ".png", original);
        for (int i = 0; i < options.Count; i++)
        {
            string path = WriteNew(dir, $"option-{i + 1}", ".png", options[i].png);
            made.files.Add(new DocFile { name = Path.GetFileName(path), kind = "png", path = path });
            made.options.Add(new DocOption { name = options[i].name, picture = $"option-{i + 1}.png" });
        }
        made.files.Add(new DocFile { name = Path.GetFileName(kept), kind = "png", path = kept });
        log?.Invoke($"{options.Count} options, sheet of {count} pages ({pdf.Length / 1024} kB) -> {dir}");
        return made;
    }

    /// <summary>The option the user picked, as picked.png with its name beside it (picked.txt) in the document's folder: the choice of the moment, so a later choice takes its place.</summary>
    public static bool WritePicked(DocItem d, int n, DocOption option)
    {
        if (d == null || option == null || string.IsNullOrEmpty(d.folder) || !Directory.Exists(d.folder)) return false;
        string from = Path.Combine(d.folder, $"option-{n}.png");
        if (!File.Exists(from)) return false;
        File.Copy(from, Path.Combine(d.folder, "picked.png"), true);
        File.WriteAllText(Path.Combine(d.folder, "picked.txt"), option.name + Environment.NewLine);
        return true;
    }

    /// <summary>
    /// Copies THE file of a document (DocsRules.Carried: the PDF - or, of design options with one picked, that option
    /// as a picture) to a folder (the Desktop) under its own name, or the next that is free there - never over
    /// anything. ONE file: what the partner carried is what lies on the Desktop (until 2026-11-18 a PDF and PNGs were
    /// left there together). Returns the copy's path (empty when the file is gone).
    /// </summary>
    public static List<string> Place(DocItem d, string desktop)
    {
        var placed = new List<string>();
        var file = DocsRules.Carried(d).file;
        if (file == null || !File.Exists(file.path) || string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop)) return placed;
        string ext = Path.GetExtension(file.name);
        string name = DocsRules.FreeFile(Path.GetFileNameWithoutExtension(file.name), ext, n => File.Exists(Path.Combine(desktop, n)) || Directory.Exists(Path.Combine(desktop, n)));
        string to = Path.Combine(desktop, name);
        File.Copy(file.path, to, false);
        placed.Add(to);
        return placed;
    }
}
