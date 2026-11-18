using System.Drawing;
using System.Drawing.Imaging;
using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// (Round 4, F) What the user said after the run of 2026-11-18 11:02 ('YouTube logo - new options'):
///   "I asked for a new logo: it made four copies of the real logo, nothing different."
///   "It carries a PDF into the PC but inside it is a PNG."
/// The rules that answer it: four DIFFERENT ways of drawing, each from a description alone and with no brand in it;
/// and ONE file that is carried - the same name and the same thumbnail in the room, in the PC, on the Desktop and
/// behind the click.
/// </summary>
public class W2DocsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "lp-w2docs-" + Guid.NewGuid().ToString("N")[..8]);
    string Store => Path.Combine(_root, "store");
    string User => Path.Combine(_root, "Documents", "Bambi");
    string Desktop => Path.Combine(_root, "Desktop");
    public W2DocsTests() { Directory.CreateDirectory(Store); Directory.CreateDirectory(Desktop); }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    // ------------------------------------------------------------------ four different directions
    [Fact]
    public void The_ways_of_drawing_are_all_different_from_one_another()
    {
        Assert.True(DocsRules.Styles.Length >= 6);
        Assert.Equal(DocsRules.Styles.Length, DocsRules.Styles.Select(s => s.id).Distinct().Count());
        Assert.Equal(DocsRules.Styles.Length, DocsRules.Styles.Select(s => s.label).Distinct().Count());
        Assert.Equal(DocsRules.Styles.Length, DocsRules.Styles.Select(s => s.anchor).Distinct().Count());
        Assert.All(DocsRules.Styles, s => Assert.True(s.anchor.Length > 60, s.id));
        // the four the user's brief names are there, by those words
        foreach (string label in new[] { "geometric", "hand-drawn storybook", "monoline", "playful mascot" }) Assert.Contains(DocsRules.Styles, s => s.label == label);
        Assert.Equal("storybook", DocsRules.StyleOf("Hand-drawn storybook").id);
        Assert.Equal("clay", DocsRules.StyleOf(" clay ").id);
        Assert.Null(DocsRules.StyleOf("bolder")); Assert.Null(DocsRules.StyleOf(""));
    }

    [Theory]
    // the model chose four different ones: they stand
    [InlineData("monoline|mascot|badge|clay", "monoline|mascot|badge|clay")]
    // the same one twice (what happened in effect on 2026-11-18: four of a kind): the later ones get the next that is free
    [InlineData("geometric|geometric|geometric|geometric", "geometric|storybook|monoline|mascot")]
    [InlineData("mascot|mascot|storybook|mascot", "mascot|geometric|storybook|monoline")]
    // words that name none of them (an older answer: "symbol-first | bolder | soft rounded | playful cutout")
    [InlineData("symbol-first|bolder|soft rounded|playful cutout", "geometric|storybook|monoline|mascot")]
    [InlineData("|papercut||", "geometric|papercut|storybook|monoline")]
    // never more than four
    [InlineData("pixel|clay|badge|papercut|mascot|monoline", "pixel|clay|badge|papercut")]
    [InlineData("clay|pixel", "clay|pixel")]
    public void Four_options_get_four_different_ways_of_drawing(string asked, string expected)
    {
        var got = DocsRules.PickStyles(asked.Split('|'));
        Assert.Equal(expected.Split('|'), got.Select(s => s.id));
        Assert.Equal(got.Count, got.Select(s => s.id).Distinct().Count());
    }

    [Fact]
    public void The_model_is_asked_for_original_designs_by_function_and_for_a_style_out_of_the_list()
    {
        string user = DocsRules.DesignUser("logo", "이 로고 별로지? 새로 만들어 줘", true, 0.3f, 0.2f);
        Assert.Contains("NEVER NAME A BRAND", user);
        Assert.Contains("BY ITS FUNCTION", user);
        Assert.Contains("FOUR ORIGINAL designs", user);
        Assert.Contains("will NOT see the current design", user);
        foreach (var s in DocsRules.Styles) Assert.Contains(s.id + " (" + s.label + ")", user);
        // the schema: a style is one of the list (strict output), and the purpose is asked for
        var schema = DocsRules.DesignSchema();
        Assert.Contains("purpose", ((JArray)schema["required"]).Select(x => (string)x));
        var style = schema["properties"]["options"]["items"]["properties"]["style"];
        Assert.Equal(DocsRules.Styles.Select(s => s.id), ((JArray)style["enum"]).Select(x => (string)x));
        Assert.Equal(new[] { "style", "rationale", "prompt" }, ((JArray)schema["properties"]["options"]["items"]["required"]).Select(x => (string)x));
    }

    // ------------------------------------------------------------------ no brand
    [Theory]
    [InlineData("YouTube logo", "Video app logo")]
    [InlineData("youtube logo", "video app logo")]
    [InlineData("Youtube Logo - new options", "Video app Logo - new options")]
    [InlineData("the Gmail icon", "the mail app icon")]
    [InlineData("유튜브 로고네, 작은 크기에선 글자 의존이 커서 약해 보여; 같은 용도로 네 가지 새로 그려볼게.", "영상 앱 로고네, 작은 크기에선 글자 의존이 커서 약해 보여; 같은 용도로 네 가지 새로 그려볼게.")]
    [InlineData("이건 YouTube 로고야", "이건 영상 앱 로고야")]
    [InlineData("Google Chrome icon", "Web browser icon")]
    [InlineData("KakaoTalk app icon", "Messenger app app icon")]
    [InlineData("Mail app icon", "Mail app icon")]
    [InlineData("an apple on a window sill", "an apple on a window sill")]          // (words of their own are left alone)
    [InlineData("", "")]
    public void A_brands_name_becomes_what_the_thing_is_for(string text, string expected)
    {
        Assert.Equal(expected, DocsRules.Debrand(text));
        Assert.False(DocsRules.NamesBrand(DocsRules.Debrand(text)));
    }

    [Fact]
    public void What_the_image_model_is_told_has_the_no_brand_clause_and_no_brand_even_for_a_brand_named_subject()
    {
        var prompts = DocsRules.Styles.Select(s => DocsRules.DrawPrompt("YouTube logo", "the mark of YouTube, the app for watching videos.", s,
            "A red rounded rectangle with a white play triangle like the YouTube logo, next to the Netflix red.", true)).ToList();
        Assert.All(prompts, p =>
        {
            Assert.Contains(DocsRules.NoBrandClause, p);
            Assert.Contains("ORIGINAL design", p);
            Assert.Contains("existing brand's logo", p);
            Assert.False(DocsRules.NamesBrand(p), p);
            Assert.DoesNotContain("youtube", p, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("netflix", p, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("video app logo", p, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("attached image", p, StringComparison.OrdinalIgnoreCase);      // nothing is attached any more
            Assert.DoesNotContain("CURRENT version", p);
            Assert.EndsWith("Transparent background.", p);
        });
        // each way of drawing is spelled out in its own prompt, and no two prompts are the same
        Assert.Equal(prompts.Count, prompts.Distinct().Count());
        for (int i = 0; i < prompts.Count; i++) { Assert.Contains(DocsRules.Styles[i].label, prompts[i]); Assert.Contains(DocsRules.Styles[i].anchor, prompts[i]); }
        Assert.EndsWith("Plain pure white background.", DocsRules.DrawPrompt("Mail app icon", "", null, "An envelope.", false));
    }

    sealed class Model : IDocsModel
    {
        public JObject json = new();
        public List<(bool reference, string prompt)> drawn = new();
        public Task<DocsResearch> ResearchAsync(string topic, string asked, CancellationToken ct) => Task.FromResult(new DocsResearch());
        public Task<JObject> AskJsonAsync(string system, string user, IReadOnlyList<(string mime, byte[] data, string name)> pictures, JObject schema, int maxTokens, CancellationToken ct) => Task.FromResult(json);
        public Task<byte[]> DrawAsync(byte[] referencePng, string transparent, string plain, CancellationToken ct)
        {
            lock (drawn) drawn.Add((referencePng != null, transparent));
            using var bmp = new Bitmap(128, 128);
            using (var g = Graphics.FromImage(bmp)) g.FillEllipse(Brushes.Coral, 10, 10, 100, 100);
            using var ms = new MemoryStream(); bmp.Save(ms, ImageFormat.Png);
            return Task.FromResult(ms.ToArray());
        }
    }

    static byte[] Shot(int w, int h, ImageFormat f)
    {
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.White); g.FillRectangle(Brushes.Red, w / 2 - 40, h / 2 - 30, 80, 60); }
        using var ms = new MemoryStream(); bmp.Save(ms, f);
        return ms.ToArray();
    }

    /// <summary>The answer of 2026-11-18 11:02 as it was: a brand in every line, and four directions of one kind.</summary>
    static JObject BrandedAnswer() => new()
    {
        ["found"] = true, ["x"] = 0.4, ["y"] = 0.4, ["w"] = 0.2, ["h"] = 0.2,
        ["title"] = "YouTube logo", ["purpose"] = "the logo of YouTube, a site for watching videos", ["what"] = "유튜브 로고야", ["weak"] = "유튜브 글자에 기대고 있어",
        ["spoken"] = "유튜브 로고네, 작은 크기에선 약해 보여; 네 가지 새로 그려볼게.",
        ["options"] = new JArray(new[] { "geometric", "geometric", "mascot", "geometric" }.Select((s, i) => new JObject
        {
            ["style"] = s, ["rationale"] = "유튜브답게 " + i, ["prompt"] = "Redraw the YouTube logo: a red rounded rectangle with a white triangle, version " + i + ".",
        })),
    };

    Task<DocsMade> Designs(Model model, List<string> said = null) =>
        DocsMaker.DesignsAsync(model, Shot(800, 450, ImageFormat.Jpeg), Shot(1024, 768, ImageFormat.Png), Shot(1024, 768, ImageFormat.Png), 0.5f, 0.5f, 512, 384, "logo",
            "저 이미지 말고 유튜브 로고 이미지가 따로 있다면은 한 번 만들어 봐 줄래?", true, Store, User, new DateTime(2026, 11, 18, 11, 2, 0), null, l => said?.Add(l), null, default);

    [Fact]
    public async Task The_run_of_11_02_again_four_different_options_drawn_from_words_alone_and_no_brand_anywhere()
    {
        var model = new Model { json = BrandedAnswer() };
        var said = new List<string>();
        var made = await Designs(model, said);
        // nothing the app shows, says or names a file with carries the brand
        Assert.Equal("Video app logo - new options", made.title);
        Assert.Equal("Video app logo - new options.pdf", made.files[0].name);
        Assert.EndsWith("Video app logo - new options", made.folder);
        Assert.False(DocsRules.NamesBrand(made.summary), made.summary);
        Assert.All(said, s => Assert.False(DocsRules.NamesBrand(s), s));
        Assert.StartsWith("영상 앱 로고네", said[0]);
        // four options, four different directions - although the model named one of them three times
        Assert.Equal(new[] { "Option 1 - geometric", "Option 2 - hand-drawn storybook", "Option 3 - playful mascot", "Option 4 - monoline" }, made.options.Select(o => o.name));
        // each drawn from its description alone: the cut-out of the real logo is NOT handed to the image model
        Assert.Equal(4, model.drawn.Count);
        Assert.All(model.drawn, d =>
        {
            Assert.False(d.reference);
            Assert.Contains(DocsRules.NoBrandClause, d.prompt);
            Assert.False(DocsRules.NamesBrand(d.prompt), d.prompt);
            Assert.Contains("video app logo", d.prompt, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(4, model.drawn.Select(d => d.prompt).Distinct().Count());
        foreach (string label in new[] { "geometric", "hand-drawn storybook", "playful mascot", "monoline" }) Assert.Single(model.drawn, d => d.prompt.Contains("Direction: " + label + " - "));
        // (the original is still kept, for the sheet's "Original" and the board)
        Assert.True(File.Exists(Path.Combine(Store, "original.png")));
    }

    // ------------------------------------------------------------------ ONE file is carried
    static DocItem DesignsDoc(string folder = @"C:\d\g", int picked = 0)
    {
        var d = new DocItem { id = "des-0a1b2c3d", kind = DocsRules.Designs, status = DocsRules.Ready, title = "Video app logo - new options", folder = folder, pages = 5 };
        d.files.Add(new DocFile { name = "Video app logo - new options.pdf", kind = "pdf", path = Path.Combine(folder, "Video app logo - new options.pdf") });
        for (int n = 1; n <= 4; n++)
        {
            d.files.Add(new DocFile { name = $"option-{n}.png", kind = "png", path = Path.Combine(folder, $"option-{n}.png") });
            d.options.Add(new DocOption { name = "Option " + n, picture = $"option-{n}.png", picked = n == picked });
        }
        d.files.Add(new DocFile { name = "original.png", kind = "png", path = Path.Combine(folder, "original.png") });
        return d;
    }

    [Fact]
    public void Nothing_picked_the_sheet_is_carried_as_a_PDF_with_its_first_page_as_the_thumbnail()
    {
        var (file, icon) = DocsRules.Carried(DesignsDoc());
        Assert.Equal("Video app logo - new options.pdf", file.name); Assert.Equal("pdf", file.kind);
        Assert.Equal("/docs/des-0a1b2c3d/page-1-s.png", icon);
        // a report: its PDF
        var report = new DocItem { id = "rep-00000001", kind = DocsRules.Report, status = DocsRules.Ready, files = { new DocFile { name = "R.pdf", kind = "pdf", path = @"C:\d\f\R.pdf" } } };
        Assert.Equal(("R.pdf", "/docs/rep-00000001/page-1-s.png"), (DocsRules.Carried(report).file.name, DocsRules.Carried(report).icon));
        // nothing to carry
        Assert.Null(DocsRules.Carried(null).file);
        Assert.Null(DocsRules.Carried(new DocItem { kind = DocsRules.Designs }).file);
    }

    [Fact]
    public void An_option_picked_that_picture_is_carried_under_its_own_name_with_the_option_as_the_thumbnail()
    {
        var d = DesignsDoc(picked: 3);
        var (file, icon) = DocsRules.Carried(d);
        Assert.Equal("Video app logo - new options - option 3.png", file.name); Assert.Equal("png", file.kind);
        Assert.Equal(@"C:\d\g\option-3.png", file.path);
        Assert.Equal("/docs/des-0a1b2c3d/option-3-s.png", icon);
        // what is fetched under /docs/ for it is a name the host hands out
        Assert.True(DocsRules.SafeServed(d.id, "option-3-s.png")); Assert.True(DocsRules.SafeServed(d.id, "page-1-s.png"));
        // a very long title: the option's number is never what is cut off, and the name stays within a file name's 60 characters
        string longName = DocsRules.PickedName(new string('x', 60), 2);
        Assert.EndsWith(" - option 2.png", longName);
        Assert.Equal(Path.GetFileNameWithoutExtension(longName), DocsRules.SafeTitle(Path.GetFileNameWithoutExtension(longName)));
    }

    [Fact]
    public void The_book_keeps_the_carried_file_first_through_every_pick()
    {
        var b = new DocsBook();
        var src = DesignsDoc();
        b.Begin(src.id, DocsRules.Designs, "logo", "", 1, "", out _);
        b.Ready(src.id, src.title, 5, src.options, src.files, src.folder);
        var d = b.Find(src.id);
        Assert.Equal("Video app logo - new options.pdf", d.files[0].name);                       // nothing picked: the sheet
        b.Pick(d.id, 3);
        Assert.Equal("Video app logo - new options - option 3.png", d.files[0].name); Assert.Equal("png", d.files[0].kind);
        Assert.Equal(DocsRules.Carried(d).file.name, d.files[0].name);
        Assert.Equal(7, d.files.Count);                                                           // the picked one, the sheet, four options, the original
        b.Pick(d.id, 1);                                                                          // another choice takes its place - no entry of the earlier one is left
        Assert.Equal("Video app logo - new options - option 1.png", d.files[0].name);
        Assert.Equal(7, d.files.Count);
        Assert.DoesNotContain(d.files, f => f.name.EndsWith("option 3.png"));
        Assert.Equal(1, d.files.Count(f => f.kind == "pdf"));
        Assert.Equal(new[] { "option-1.png", "option-2.png", "option-3.png", "option-4.png" }, d.files.Where(f => f.name.StartsWith("option-")).Select(f => f.name));
        b.Pick(d.id, 1);                                                                          // the same again: nothing doubles
        Assert.Equal(7, d.files.Count);
        Assert.False(DocsRules.PutCarriedFirst(d));
    }

    [Fact]
    public void The_show_carries_that_files_name_and_thumbnail_in_both_stages()
    {
        // what DocsHost.Deliver / Apply do with the rules: Carried -> DeliverShow.Begin(name) -> ShowBoard.File(name, icon)
        var d = DesignsDoc(picked: 2);
        var (file, icon) = DocsRules.Carried(d);
        var carry = new DeliverShow();
        var board = new ShowBoard();
        var act = carry.Begin(10, new DeliverShow.World { owner = Env.Mr, hasPc = true, hasMr = true, calibrated = true }, d.id, file.name);
        Assert.True(act.HasFlag(CarryAct.Cross));
        board.File(10, carry.stage, carry.name, icon, "DISPLAY1", carry.since, (float)carry.StageSeconds);
        Assert.True(board.Refresh(10));
        Assert.Equal(("file", "going", "Video app logo - new options - option 2.png", "/docs/des-0a1b2c3d/option-2-s.png"), (board.Info.kind, board.Info.stage, board.Info.name, board.Info.icon));
        int seq = board.Info.seq;
        act = carry.Tick(14, new DeliverShow.World { owner = Env.Pc, hasPc = true, hasMr = true, calibrated = true });
        Assert.True(act.HasFlag(CarryAct.Place) && act.HasFlag(CarryAct.SayPlaced));
        board.File(14, carry.stage, carry.name, icon, "DISPLAY1", carry.since, (float)carry.StageSeconds);
        Assert.True(board.Refresh(14));
        // inside the PC: the same show, the same file, the same picture - and the name is one of the document's files (the PC client finds the document by it)
        Assert.Equal((seq, "shown", "Video app logo - new options - option 2.png", "/docs/des-0a1b2c3d/option-2-s.png"), (board.Info.seq, board.Info.stage, board.Info.name, board.Info.icon));
        DocsRules.PutCarriedFirst(d);
        Assert.Equal(board.Info.name, d.files[0].name);
        Assert.EndsWith(".png", board.Info.name);
        // nothing picked: a PDF by its name, its first page as the picture
        var sheet = DocsRules.Carried(DesignsDoc());
        Assert.EndsWith(".pdf", sheet.file.name); Assert.EndsWith("/page-1-s.png", sheet.icon);
    }

    [Fact]
    public async Task On_the_Desktop_lies_the_one_file_that_was_carried_and_a_click_opens_it()
    {
        var made = await Designs(new Model { json = BrandedAnswer() });
        var d = new DocItem { id = "des-0a1b2c3d", kind = DocsRules.Designs, status = DocsRules.Ready, title = made.title, folder = made.folder, files = made.files, options = made.options, pages = made.pages };
        // nothing picked: the sheet, and only the sheet
        var placed = DocsMaker.Place(d, Desktop);
        Assert.Equal(new[] { Path.Combine(Desktop, "Video app logo - new options.pdf") }, placed);
        Assert.Equal(new[] { "Video app logo - new options.pdf" }, Directory.GetFiles(Desktop).Select(Path.GetFileName));
        Assert.Equal((DocsRules.RevealOpen, placed[0]), DocsRules.Reveal(d, File.Exists, Directory.Exists, placed[0]));
        // option 3 picked: that picture, under the name the partner shows
        d.options[2].picked = true; DocsRules.PutCarriedFirst(d);
        var png = DocsMaker.Place(d, Desktop);
        Assert.Equal(new[] { Path.Combine(Desktop, d.files[0].name) }, png);
        Assert.Equal("Video app logo - new options - option 3.png", Path.GetFileName(png[0]));
        Assert.Equal(File.ReadAllBytes(Path.Combine(made.folder, "option-3.png")), File.ReadAllBytes(png[0]));
        using (var img = Image.FromFile(png[0])) Assert.Equal(ImageFormat.Png.Guid, img.RawFormat.Guid);      // a .png that IS a PNG
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(placed[0]), 0, 5));       // a .pdf that IS a PDF
        // the click: the copy on the Desktop; moved away: the picture in the document's folder - never the sheet when a picture is held
        Assert.Equal((DocsRules.RevealOpen, png[0]), DocsRules.Reveal(d, File.Exists, Directory.Exists, png[0]));
        Assert.Equal((DocsRules.RevealOpen, Path.Combine(made.folder, "option-3.png")), DocsRules.Reveal(d, File.Exists, Directory.Exists, Path.Combine(Desktop, "gone.png")));
        // asked for again: never over what is there
        Assert.Equal("Video app logo - new options - option 3 (2).png", Path.GetFileName(DocsMaker.Place(d, Desktop)[0]));
    }
}
