using System.Drawing;
using System.Drawing.Imaging;
using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// (H6) Documents the partner makes - "look into the Meta competition and make a PDF", "isn't that design bad? make me
/// a new icon" - as rules: the plain wordings, the names of what is written on the PC, the pages and the PDF, the book
/// of this sitting, and the making itself run with models that answer from a list (no network, no screen).
/// </summary>
public class DocsPlainTests
{
    [Theory]
    [InlineData("Meta VR Start Developer Competition 2026 알아보고 중요한 내용 PDF로 만들어 줘", "Meta VR Start Developer Competition 2026")]
    [InlineData("메타 VR 대회에 대해 조사해서 보고서 만들어 줘", "메타 VR 대회")]
    [InlineData("퀘스트 3 손 추적을 알아보고 PDF로 정리해 줘", "퀘스트 3 손 추적")]
    [InlineData("네이버 주가 좀 찾아보고 PDF 만들어 줘", "네이버 주가")]
    [InlineData("유니티 6 URP에 대한 PDF 만들어 줘", "유니티 6 URP")]
    [InlineData("make me a PDF about the Meta VR Start competition", "the Meta VR Start competition")]
    [InlineData("Look into passthrough APIs and make a report", "passthrough APIs")]
    [InlineData("research hand tracking on Quest 3, then write me a PDF", "hand tracking on Quest 3")]
    public void A_report_is_asked_for_in_plain_words(string said, string topic)
    {
        var a = DocsRules.Plain(said);
        Assert.NotNull(a);
        Assert.Equal(DocsRules.MakeReport, a.action);
        Assert.Equal(topic, a.arg);
    }

    [Theory]
    [InlineData("이 화면 피드백 PDF로 만들어 줘")]          // (a review of what is on the screen: not research)
    [InlineData("이거 알아보고 PDF로 만들어 줘")]             // (what "this" is has to be looked at first: the model's)
    [InlineData("이 PDF 읽어 줘")]
    [InlineData("메모장 열어 줘")]
    [InlineData("오늘 날씨 알아봐 줘")]                      // (no document asked for)
    [InlineData("의자 새로 만들어 줘")]
    [InlineData("이 아이콘 뭐야?")]
    [InlineData("책상 디자인 새로 만들어 줘")]                 // (a desk: a thing of the partner's, the model decides)
    [InlineData("노래 다운로드해 줘")]                        // (the tune, not the document)
    [InlineData("read this document for me")]
    public void What_is_no_document_is_left_alone(string said) => Assert.Null(DocsRules.Plain(said, true, true, true));

    [Theory]
    [InlineData("이 아이콘 별로지 않아? 새로 만들어 줘", "icon")]
    [InlineData("이 디자인 별로지 않아? 아이콘 새로 만들어 줘", "icon")]
    [InlineData("이 로고 다시 디자인해 줘", "logo")]
    [InlineData("이 디자인 시안 몇 개 뽑아 줘", "design")]
    [InlineData("make me a new icon", "icon")]
    [InlineData("can you redesign this logo, give me some options", "logo")]
    public void New_designs_are_asked_for_in_plain_words(string said, string what)
    {
        var a = DocsRules.Plain(said);
        Assert.NotNull(a);
        Assert.Equal(DocsRules.MakeDesigns, a.action);
        Assert.Equal(what, a.arg);
    }

    [Theory]
    [InlineData("PDF 보여 줘", DocsRules.ShowDoc, "", 0)]
    [InlineData("그거 보여 줘", DocsRules.ShowDoc, "", 0)]
    [InlineData("보고서 다시 띄워 줘", DocsRules.ShowDoc, "", 0)]
    [InlineData("show me the report", DocsRules.ShowDoc, "", 0)]
    [InlineData("컴퓨터에 넣어 줘", DocsRules.DeliverDoc, "", 0)]
    [InlineData("다운로드해 줘", DocsRules.DeliverDoc, "", 0)]
    [InlineData("내 컴퓨터로 보내 줘", DocsRules.DeliverDoc, "", 0)]
    [InlineData("바탕화면에 놔 줘", DocsRules.DeliverDoc, "", 0)]
    [InlineData("put it on my desktop", DocsRules.DeliverDoc, "", 0)]
    [InlineData("download it", DocsRules.DeliverDoc, "", 0)]
    [InlineData("다음 페이지", DocsRules.DocPage, "next", 0)]
    [InlineData("이전 페이지로", DocsRules.DocPage, "previous", 0)]
    [InlineData("3페이지 보여 줘", DocsRules.DocPage, "", 3)]
    [InlineData("마지막 페이지", DocsRules.DocPage, "last", 0)]
    [InlineData("next page", DocsRules.DocPage, "next", 0)]
    [InlineData("page 2", DocsRules.DocPage, "", 2)]
    [InlineData("2번으로 할게", DocsRules.PickOption, "", 2)]
    [InlineData("세 번째가 좋아", DocsRules.PickOption, "", 3)]
    [InlineData("1번", DocsRules.PickOption, "", 1)]
    [InlineData("option 4", DocsRules.PickOption, "", 4)]
    [InlineData("I like the second one", DocsRules.PickOption, "", 2)]
    [InlineData("PDF 닫아 줘", DocsRules.CloseDoc, "", 0)]
    [InlineData("close the report", DocsRules.CloseDoc, "", 0)]
    public void With_a_document_there_it_is_shown_paged_picked_carried_and_closed(string said, string action, string arg, int n)
    {
        var a = DocsRules.Plain(said, haveDocs: true, open: true, designs: true);
        Assert.NotNull(a);
        Assert.Equal(action, a.action);
        Assert.Equal(arg, a.arg);
        Assert.Equal(n, a.n);
    }

    [Theory]
    [InlineData("PDF 보여 줘")]
    [InlineData("그거 보여 줘")]
    [InlineData("다운로드해 줘")]
    [InlineData("다음 페이지")]
    [InlineData("2번으로 할게")]
    public void Without_a_document_those_words_are_not_about_one(string said) => Assert.Null(DocsRules.Plain(said));

    [Theory]
    [InlineData("2번 화면 가져와")]          // (a display brought into the room)
    [InlineData("상점 보여 줘")]
    [InlineData("이번엔 다른 노래 틀어 줘")]
    [InlineData("화면 닫아 줘")]
    public void Other_things_with_numbers_and_showing_stay_what_they_were(string said) => Assert.Null(DocsRules.Plain(said, true, true, true));

    [Fact]
    public void Pages_are_only_turned_and_options_only_picked_when_there_is_something_to_turn_or_pick()
    {
        Assert.Null(DocsRules.Plain("다음 페이지", haveDocs: true, open: false));
        Assert.Null(DocsRules.Plain("2번으로 할게", haveDocs: true, open: true, designs: false));
    }

    [Theory]
    [InlineData("makeReport", "Meta VR Start 2026", DocsRules.MakeReport, "Meta VR Start 2026", 0)]
    [InlineData("makeDesigns", "logo", DocsRules.MakeDesigns, "logo", 0)]
    [InlineData("makeDesigns", "", DocsRules.MakeDesigns, "icon", 0)]
    [InlineData("showDoc", "", DocsRules.ShowDoc, "", 0)]
    [InlineData("docPage", "next", DocsRules.DocPage, "next", 0)]
    [InlineData("docPage", "previous", DocsRules.DocPage, "previous", 0)]
    [InlineData("docPage", "3", DocsRules.DocPage, "", 3)]
    [InlineData("docPage", "last", DocsRules.DocPage, "last", 0)]
    [InlineData("pickOption", "2", DocsRules.PickOption, "", 2)]
    [InlineData("pickOption", "세 번째", DocsRules.PickOption, "", 3)]
    [InlineData("deliverDoc", "", DocsRules.DeliverDoc, "", 0)]
    [InlineData("closeDoc", "", DocsRules.CloseDoc, "", 0)]
    public void The_models_actions_become_the_same_asks(string action, string thing, string expect, string arg, int n)
    {
        var a = DocsRules.Map(action, thing);
        Assert.NotNull(a);
        Assert.Equal(expect, a.action); Assert.Equal(arg, a.arg); Assert.Equal(n, a.n);
    }

    [Fact]
    public void Other_actions_are_not_documents()
    {
        Assert.Null(DocsRules.Map("makeItem", "chair"));
        Assert.Null(DocsRules.Map("none", ""));
        Assert.Contains("makeReport", DocsRules.PromptSection(new DocsView()));
    }

    [Fact]
    public void A_page_turn_stays_inside_the_document()
    {
        Assert.Equal(2, DocsRules.PageAfter(1, 3, "next", 0));
        Assert.Equal(3, DocsRules.PageAfter(3, 3, "next", 0));
        Assert.Equal(1, DocsRules.PageAfter(1, 3, "previous", 0));
        Assert.Equal(3, DocsRules.PageAfter(1, 3, "last", 0));
        Assert.Equal(3, DocsRules.PageAfter(1, 3, "", 9));
        Assert.Equal(1, DocsRules.PageAfter(2, 0, "", 0));
    }
}

public class DocsNameTests
{
    [Fact]
    public void A_folder_is_the_time_and_the_title_and_never_a_path()
    {
        var at = new DateTime(2026, 11, 18, 1, 42, 7);
        Assert.Equal("2026-11-18 0142 Meta VR Start 2026", DocsRules.FolderName(at, "Meta VR Start 2026"));
        string bad = DocsRules.FolderName(at, @"..\..\Windows\System32: what? <now> | ""then""");
        Assert.DoesNotContain('\\', bad); Assert.DoesNotContain('/', bad); Assert.DoesNotContain(':', bad); Assert.DoesNotContain('?', bad);
        Assert.DoesNotContain('<', bad); Assert.DoesNotContain('|', bad); Assert.DoesNotContain('"', bad);
        Assert.Equal(bad, Path.GetFileName(bad));
        Assert.Equal("2026-11-18 0142 Bambi document", DocsRules.FolderName(at, "  ...  "));
        Assert.Equal("2026-11-18 0142 Bambi document", DocsRules.FolderName(at, "CON"));
        Assert.True(DocsRules.FolderName(at, new string('가', 200)).Length <= 16 + 60);
        Assert.Equal("2026-11-18 0142 메타 VR 대회 정리", DocsRules.FolderName(at, "메타 VR 대회 정리"));
    }

    [Fact]
    public void What_is_there_is_never_written_over()
    {
        var at = new DateTime(2026, 11, 18, 1, 42, 0);
        var there = new HashSet<string> { "2026-11-18 0142 Report", "2026-11-18 0142 Report (2)" };
        Assert.Equal("2026-11-18 0142 Report (3)", DocsRules.FreeFolder(at, "Report", there.Contains));
        Assert.Equal("2026-11-18 0142 Other", DocsRules.FreeFolder(at, "Other", there.Contains));
        var files = new HashSet<string> { "Report.pdf" };
        Assert.Equal("Report (2).pdf", DocsRules.FreeFile("Report", ".pdf", files.Contains));
        Assert.Equal("a b.pdf", DocsRules.FreeFile("a/b", ".pdf", files.Contains));
    }

    [Theory]
    [InlineData("rep-0123abcd", "page-1.png", true)]
    [InlineData("des-0123abcd", "option-3-s.png", true)]
    [InlineData("des-0123abcd", "original.png", true)]
    [InlineData("rep-0123abcd", "..\\state.json", false)]
    [InlineData("rep-0123abcd", "page-1.pdf", false)]
    [InlineData("rep-0123abcd", "Page-1.png", false)]
    [InlineData("..", "page-1.png", false)]
    [InlineData("rep-0123abcd/..", "page-1.png", false)]
    [InlineData("world", "page-1.png", false)]
    [InlineData("rep-0123abcd", "", false)]
    public void Only_a_documents_own_pictures_are_served(string id, string file, bool ok) => Assert.Equal(ok, DocsRules.SafeServed(id, file));

    [Fact]
    public void New_ids_are_ids_that_are_served()
    {
        Assert.True(DocsRules.SafeId(DocsRules.NewId(DocsRules.Report)));
        Assert.True(DocsRules.SafeId(DocsRules.NewId(DocsRules.Designs)));
        Assert.Equal("Option 2 - bolder", DocsRules.OptionName(2, " bolder. "));
        Assert.Equal("Option 3", DocsRules.OptionName(3, ""));
    }

    [Fact]
    public void The_design_is_cut_out_square_with_a_margin_and_inside_the_picture()
    {
        // a box of 100 x 60 in the middle of 1000 x 800: the larger side times 1.15
        var (x, y, side) = DocsRules.CutOut(1000, 800, 0.45f, 0.4625f, 0.10f, 0.075f, 0, 0);
        Assert.Equal(115, side); Assert.InRange(x, 442, 443); Assert.InRange(y, 342, 343);
        // at the corner it is moved inwards
        (x, y, side) = DocsRules.CutOut(1000, 800, 0.95f, 0.95f, 0.05f, 0.05f, 0, 0);
        Assert.True(x + side <= 1000 && y + side <= 800 && side >= 48);
        // no box: a square round the spot
        (x, y, side) = DocsRules.CutOut(1000, 800, 0, 0, 0, 0, 500, 400);
        Assert.Equal(200, side); Assert.Equal(400, x); Assert.Equal(300, y);
        // nonsense is held to the picture
        (x, y, side) = DocsRules.CutOut(1000, 800, -3, float.NaN, 9, 9, 0, 0);
        Assert.True(x >= 0 && y >= 0 && x + side <= 1000 && y + side <= 800);
    }
}

public class DocsBookTests
{
    static DocsBook WithReport(out string id, bool ready = true)
    {
        var b = new DocsBook();
        id = DocsRules.NewId(DocsRules.Report);
        b.Begin(id, DocsRules.Report, "topic", "asked", 1, "Reading about it ...", out _);
        if (ready) b.Ready(id, "Title", 3, null, new[] { new DocFile { name = "Title.pdf", kind = "pdf", path = @"C:\x\Title.pdf" } }, @"C:\x");
        return b;
    }

    [Fact]
    public void A_report_goes_from_making_to_ready()
    {
        var b = WithReport(out string id, ready: false);
        var d = b.Find(id);
        Assert.Equal(DocsRules.Making, d.status); Assert.Equal("Reading about it ...", d.stage); Assert.Equal("asked", d.asked);
        int rev = b.View.revision;
        Assert.True(b.Stage(id, "Writing ...", 0.5f));
        Assert.Equal("Writing ...", d.stage); Assert.Equal(0.5f, d.progress); Assert.True(b.View.revision > rev);
        Assert.True(b.Stage(id, "Laying out the pages ...", 0.3f, "The real title"));       // (progress never goes back)
        Assert.Equal(0.5f, d.progress); Assert.Equal("The real title", d.title);
        Assert.True(b.Ready(id, "The real title", 3, null, new[] { new DocFile { name = "a.pdf", path = "p" } }, "folder"));
        Assert.Equal(DocsRules.Ready, d.status); Assert.Equal(1f, d.progress); Assert.Equal("", d.stage); Assert.Equal(3, d.pages); Assert.Equal("folder", d.folder);
        Assert.Equal("pdf", d.files[0].kind);
        Assert.False(b.Stage(id, "again", 0.1f));       // (what is ready is not made any more)
        Assert.False(b.Ready(id, "x", 1, null, null, ""));
        Assert.False(b.Fail(id, "x"));
    }

    [Fact]
    public void A_document_that_could_not_be_made_says_so_and_leaves_the_board()
    {
        var b = WithReport(out string id, ready: false);
        b.OpenDoc(id);
        Assert.True(b.Fail(id, "It could not be made."));
        Assert.Equal(DocsRules.Failed, b.Find(id).status); Assert.Equal("It could not be made.", b.Find(id).stage);
        Assert.Equal("", b.View.openId);
    }

    [Fact]
    public void Opening_puts_it_on_the_board_at_page_one_and_pages_turn_inside_it()
    {
        var b = WithReport(out string id);
        Assert.Equal(0, b.Page("next", 0));                 // nothing is open
        Assert.True(b.OpenDoc());                           // the newest
        Assert.Equal(id, b.View.openId); Assert.Equal(1, b.View.page);
        Assert.Equal(2, b.Page("next", 0)); Assert.Equal(3, b.Page("next", 0)); Assert.Equal(3, b.Page("next", 0));
        Assert.Equal(2, b.Page("previous", 0)); Assert.Equal(1, b.Page("", 1)); Assert.Equal(3, b.Page("last", 0));
        Assert.True(b.OpenDoc(id)); Assert.Equal(1, b.View.page);       // (opened again: from its first page)
        Assert.False(b.OpenDoc("rep-ffffffff"));
        Assert.True(b.Close()); Assert.Equal("", b.View.openId); Assert.False(b.Close());
    }

    [Fact]
    public void Only_the_last_six_are_kept_and_one_being_made_or_on_the_board_is_not_the_one_to_go()
    {
        var b = new DocsBook();
        var ids = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            string id = $"rep-0000000{i}"; ids.Add(id);
            b.Begin(id, DocsRules.Report, "t" + i, "", i, "", out var none);
            Assert.Empty(none);
            if (i != 0) b.Ready(id, "T" + i, 1, null, null, "");       // (the oldest is still being made)
        }
        b.OpenDoc(ids[1]);
        b.Begin("rep-00000006", DocsRules.Report, "t6", "", 6, "", out var removed);
        Assert.Equal(new[] { ids[2] }, removed);            // not the one being made, not the one on the board
        Assert.Equal(DocsBook.Keep, b.View.items.Count);
        Assert.Equal("rep-00000006", b.View.items[^1].id);
        Assert.Equal(ids[1], b.View.openId);
    }

    [Fact]
    public void An_option_is_picked_and_no_other()
    {
        var b = new DocsBook();
        b.Begin("des-00000001", DocsRules.Designs, "icon", "", 1, "", out _);
        Assert.Null(b.Pick("des-00000001", 1));             // not ready yet
        b.Ready("des-00000001", "Mail icon - new options", 5, Enumerable.Range(1, 4).Select(n => new DocOption { name = "Option " + n, picture = $"option-{n}.png" }), null, "");
        Assert.Equal("Option 2", b.Pick("des-00000001", 2).name);
        Assert.Equal("Option 3", b.Pick("des-00000001", 3).name);
        Assert.Equal(new[] { false, false, true, false }, b.Find("des-00000001").options.Select(o => o.picked));
        Assert.Null(b.Pick("des-00000001", 5)); Assert.Null(b.Pick("des-00000001", 0));
        var r = WithReport(out string id);
        Assert.Null(r.Pick(id, 1));                         // a report has no options
    }

    [Fact]
    public void Deleting_takes_it_off_the_list_and_off_the_board_but_never_one_that_is_being_made()
    {
        var b = WithReport(out string id);
        b.OpenDoc(id);
        Assert.True(b.Delivered(id)); Assert.False(b.Delivered(id)); Assert.True(b.Find(id).delivered);
        Assert.True(b.Delete(id));
        Assert.Empty(b.View.items); Assert.Equal("", b.View.openId);
        var m = WithReport(out string making, ready: false);
        Assert.False(m.Delete(making));
        Assert.Same(m.Find(making), m.Meant());
    }
}

public class DocsDeliverTests
{
    static DeliverShow.World W(string owner, bool pc = true, bool mr = true, bool crossing = false, bool calibrated = true) =>
        new() { owner = owner, hasPc = pc, hasMr = mr, crossing = crossing, calibrated = calibrated };

    [Fact]
    public void From_the_room_it_crosses_with_the_file_then_shows_it_inside_for_twelve_seconds()
    {
        var s = new DeliverShow();
        var act = s.Begin(100, W(Env.Mr), "rep-00000001", "Report.pdf");
        Assert.Equal(CarryAct.Changed | CarryAct.Cross | CarryAct.SayCarry, act);
        Assert.Equal(DeliverShow.Going, s.stage); Assert.Equal("Report.pdf", s.name);
        Assert.Equal(CarryAct.None, s.Tick(101, W(Env.Mr)));                        // the headset has not begun yet
        Assert.Equal(CarryAct.None, s.Tick(103, W(Env.Mr, crossing: true)));        // on its way
        Assert.Equal(CarryAct.None, s.Tick(109, W(Env.Mr, crossing: true)));
        act = s.Tick(110, W(Env.Pc));                                               // inside
        Assert.Equal(CarryAct.Changed | CarryAct.Place | CarryAct.SayPlaced, act);
        Assert.Equal(DeliverShow.Shown, s.stage); Assert.Equal(110, s.since);
        Assert.Equal(CarryAct.None, s.Tick(121.9, W(Env.Pc)));
        Assert.Equal(CarryAct.Changed | CarryAct.Ended, s.Tick(122, W(Env.Pc)));
        Assert.False(s.Running);
        Assert.Equal(CarryAct.None, s.Tick(123, W(Env.Pc)));
    }

    [Fact]
    public void Already_inside_it_is_shown_at_once()
    {
        var s = new DeliverShow();
        Assert.Equal(CarryAct.Changed | CarryAct.Place | CarryAct.SayPlaced, s.Begin(5, W(Env.Pc), "rep-00000001", "a.pdf"));
        Assert.Equal(DeliverShow.Shown, s.stage);
        Assert.Equal(CarryAct.Changed | CarryAct.Ended, s.Tick(5 + DeliverShow.ShownSeconds, W(Env.Pc)));
    }

    [Fact]
    public void Without_a_PC_client_the_file_is_simply_placed_and_the_partner_says_so()
    {
        var s = new DeliverShow();
        Assert.Equal(CarryAct.Place | CarryAct.SayPlacedNoPc, s.Begin(5, W(Env.Mr, pc: false, mr: false), "rep-00000001", "a.pdf"));
        Assert.False(s.Running);
        Assert.Equal(CarryAct.Place | CarryAct.SayPlacedNoPc, s.Begin(6, W(Env.Mr, pc: false), "rep-00000001", "a.pdf"));
        Assert.Equal(CarryAct.Place | CarryAct.SayPlacedNoPc, s.Begin(7, W(Env.Pc, pc: false), "rep-00000001", "a.pdf"));
        // a PC client but no headset to send it through a screen, or no display the headset knows the place of
        Assert.Equal(CarryAct.Place | CarryAct.SayPlacedNoPc, s.Begin(8, W(Env.Mr, mr: false), "rep-00000001", "a.pdf"));
        Assert.Equal(CarryAct.Place | CarryAct.SayPlacedNoPc, s.Begin(9, W(Env.Mr, calibrated: false), "rep-00000001", "a.pdf"));
        Assert.False(s.Running);
    }

    [Fact]
    public void A_crossing_that_never_begins_or_is_cancelled_still_leaves_the_file_on_the_PC()
    {
        var s = new DeliverShow();
        s.Begin(0, W(Env.Mr), "rep-00000001", "a.pdf");
        Assert.Equal(CarryAct.None, s.Tick(DeliverShow.StartSeconds - 0.1, W(Env.Mr)));
        Assert.Equal(CarryAct.Changed | CarryAct.Ended | CarryAct.Place | CarryAct.SayPlacedNoPc, s.Tick(DeliverShow.StartSeconds + 0.1, W(Env.Mr)));
        Assert.False(s.Running);
        // cancelled on the way: it is back in the room
        s.Begin(100, W(Env.Mr), "rep-00000001", "a.pdf");
        Assert.Equal(CarryAct.None, s.Tick(102, W(Env.Mr, crossing: true)));
        Assert.Equal(CarryAct.Changed | CarryAct.Ended | CarryAct.Place | CarryAct.SayPlacedNoPc, s.Tick(103, W(Env.Mr)));
        // a crossing that never ends
        s.Begin(200, W(Env.Mr), "rep-00000001", "a.pdf");
        Assert.Equal(CarryAct.None, s.Tick(200 + DeliverShow.GoingSeconds - 1, W(Env.Mr, crossing: true)));
        Assert.Equal(CarryAct.Changed | CarryAct.Ended | CarryAct.Place | CarryAct.SayPlacedNoPc, s.Tick(200 + DeliverShow.GoingSeconds + 1, W(Env.Mr, crossing: true)));
        // the PC client goes away while it holds the file up: nothing to click on any more
        s.Begin(300, W(Env.Pc), "rep-00000001", "a.pdf");
        Assert.Equal(CarryAct.Changed | CarryAct.Ended, s.Tick(302, W(Env.Pc, pc: false)));
        // asked again while one is carried: that show is over, the new one begins
        s.Begin(400, W(Env.Mr), "rep-00000001", "a.pdf");
        var again = s.Begin(401, W(Env.Mr), "rep-00000002", "b.pdf");
        Assert.True(again.HasFlag(CarryAct.Cross) && again.HasFlag(CarryAct.Changed));
        Assert.Equal("rep-00000002", s.docId); Assert.Equal(401, s.since);
        Assert.Equal(CarryAct.Changed | CarryAct.Ended, s.Cancel());
    }

    [Fact]
    public void The_snapshots_show_is_the_file_in_its_hands_under_one_number_and_a_look_goes_before_it()
    {
        var board = new ShowBoard();
        board.File(10, DeliverShow.Going, "Report.pdf", "/docs/rep-00000001/page-1-s.png", "DISPLAY1", 10, 45);
        Assert.True(board.Refresh(10));
        Assert.Equal("file", board.Info.kind); Assert.Equal("going", board.Info.stage); Assert.Equal("Report.pdf", board.Info.name);
        Assert.Equal("/docs/rep-00000001/page-1-s.png", board.Info.icon); Assert.Equal("DISPLAY1", board.Info.monitorId);
        int seq = board.Info.seq;
        board.File(20, DeliverShow.Shown, "Report.pdf", "/docs/rep-00000001/page-1-s.png", "DISPLAY1", 20, 12);
        Assert.True(board.Refresh(20));
        Assert.Equal("shown", board.Info.stage); Assert.Equal(seq, board.Info.seq); Assert.Equal(12f, board.Info.seconds);
        int look = board.LookBegin(21, "DISPLAY1", 0, 0, 1, 1);
        Assert.True(board.Refresh(21)); Assert.Equal("look", board.Info.kind);
        board.LookEnd(look);
        Assert.True(board.Refresh(22)); Assert.Equal("file", board.Info.kind);
        board.FileEnd();
        Assert.True(board.Refresh(32)); Assert.Equal("", board.Info.kind);
    }

    [Fact]
    public void A_click_opens_the_PDF_and_for_designs_shows_the_folder_with_the_sheet_selected()
    {
        var report = new DocItem { id = "rep-00000001", kind = DocsRules.Report, status = DocsRules.Ready, folder = @"C:\d\f", files = { new DocFile { name = "R.pdf", kind = "pdf", path = @"C:\d\f\R.pdf" } } };
        var there = new HashSet<string> { @"C:\d\f\R.pdf", @"C:\desk\R.pdf", @"C:\d\g\S.pdf" };
        Assert.Equal((DocsRules.RevealOpen, @"C:\desk\R.pdf"), DocsRules.Reveal(report, there.Contains, _ => true, @"C:\desk\R.pdf"));       // the copy on the Desktop
        Assert.Equal((DocsRules.RevealOpen, @"C:\d\f\R.pdf"), DocsRules.Reveal(report, there.Contains, _ => true, @"C:\desk\gone.pdf"));    // ... moved away: the one in its folder
        Assert.Equal((DocsRules.RevealOpen, @"C:\d\f\R.pdf"), DocsRules.Reveal(report, there.Contains, _ => true));
        Assert.Equal((DocsRules.RevealFolder, @"C:\d\f"), DocsRules.Reveal(report, _ => false, _ => true));                                 // the file is gone: its folder
        Assert.Equal(("", ""), DocsRules.Reveal(report, _ => false, _ => false));
        var designs = new DocItem { id = "des-00000001", kind = DocsRules.Designs, status = DocsRules.Ready, folder = @"C:\d\g", files = { new DocFile { name = "S.pdf", kind = "pdf", path = @"C:\d\g\S.pdf" }, new DocFile { name = "option-1.png", kind = "png", path = @"C:\d\g\option-1.png" } } };
        // (designs: THE file in the partner's hands opens - the sheet while nothing is picked; W2DocsTests has the picked one)
        Assert.Equal((DocsRules.RevealOpen, @"C:\d\g\S.pdf"), DocsRules.Reveal(designs, there.Contains, _ => true, @"C:\desk\S.pdf"));       // (no copy on the Desktop: the one in its folder)
        there.Add(@"C:\desk\S.pdf");
        Assert.Equal((DocsRules.RevealOpen, @"C:\desk\S.pdf"), DocsRules.Reveal(designs, there.Contains, _ => true, @"C:\desk\S.pdf"));
        Assert.Equal(("", ""), DocsRules.Reveal(new DocItem { status = DocsRules.Making }, _ => true, _ => true));
        Assert.Equal(("", ""), DocsRules.Reveal(null, _ => true, _ => true));
    }
}

public class DocsPagesTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lp-docs-tests-" + Guid.NewGuid().ToString("N")[..8]);
    public DocsPagesTests() { Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    const string Ko = "메타는 혼합현실 개발자를 위한 대회를 열고, 손 추적과 패스스루를 중심으로 한 새로운 경험을 찾는다. 참가자는 마감 전에 빌드와 설명 영상을 제출해야 하며 심사위원은 완성도와 안정성, 그리고 혼합현실이 꼭 필요한 이유를 본다. ";
    const string En = "The competition asks developers for mixed reality experiences that are hands-first, comfortable while seated and quick to get into. Judges only receive the build, so it has to give a complete moment without any explanation at all. ";

    static ReportData Long(bool korean, int sections, int paragraphs)
    {
        string p = korean ? Ko : En;
        var r = new ReportData { korean = korean, web = !korean, title = korean ? "메타 VR 스타트 개발자 대회 2026: 꼭 알아야 할 내용 정리" : "Meta VR Start Developer Competition 2026: what matters", summary = p + p, made = new DateTime(2026, 11, 18) };
        for (int i = 0; i < 5; i++) r.facts.Add(new ReportFact { label = korean ? "제출 마감" : "Entries close", value = i == 1 ? "9/24/2026-11/18/2026" : korean ? "2026년 11월 19일 05:00 KST" : "19 Nov 2026, 05:00 KST" });
        for (int s = 0; s < sections; s++)
        {
            var sec = new ReportSection { heading = (korean ? "심사 기준과 꼭 지켜야 할 규칙 " : "Judging criteria and the rules that bind ") + (s + 1) };
            for (int k = 0; k < paragraphs; k++) sec.paragraphs.Add(p + p);
            sec.bullets.AddRange(new[] { p, korean ? "손이 먼저다" : "Hands first", "https://developers.example.com/a/very/long/address/that/has/no/spaces/at/all/and/goes/on/and/on/and/on/for/a/long/while/index.html?with=parameters&and=more&and=more&and=more" });
            if (s % 2 == 0) { sec.table.columns = new List<string> { korean ? "항목" : "Item", korean ? "내용" : "What", korean ? "비고" : "Note" }; for (int row = 0; row < 5; row++) sec.table.rows.Add(new List<string> { korean ? "프레임" : "Frame rate", p, "60 fps" }); }
            r.sections.Add(sec);
        }
        for (int i = 0; i < 6; i++) r.sources.Add(new ReportSource { title = korean ? "공식 규칙 페이지" : "Official rules", url = "https://developers.example.com/competition/2026/rules-and-eligibility-and-everything-else-there-is-to-know-about-it/" + i });
        return r;
    }

    static (int w, int h) Size(string png) { using var img = Image.FromFile(png); return (img.Width, img.Height); }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_long_report_is_laid_out_over_pages_without_trouble(bool korean)
    {
        using var pages = new DocsPages();
        pages.Report(Long(korean, 6, 5));
        Assert.True(pages.Count >= 4, $"only {pages.Count} pages");
        int n = pages.SavePngs(_dir);
        Assert.Equal(pages.Count, n);
        // (LP_DOCS_KEEP=<folder>: the pages are kept there as well, to be looked at)
        if (Environment.GetEnvironmentVariable("LP_DOCS_KEEP") is { Length: > 0 } keep) pages.SavePngs(Path.Combine(keep, korean ? "report-ko" : "report-en"));
        for (int i = 1; i <= n; i++)
        {
            Assert.Equal((DocsPages.W, DocsPages.H), Size(Path.Combine(_dir, $"page-{i}.png")));
            Assert.Equal((620, 877), Size(Path.Combine(_dir, $"page-{i}-s.png")));
        }
        Assert.False(File.Exists(Path.Combine(_dir, $"page-{n + 1}.png")));
        var jpegs = pages.Jpegs();
        Assert.Equal(n, jpegs.Count);
        Assert.All(jpegs, j => Assert.Equal((DocsPages.W, DocsPages.H), DocsPdf.JpegSize(j)));
        byte[] pdf = DocsPdf.Write(jpegs, korean ? "메타 대회" : "Meta");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Equal(n, DocsPdf.PageCount(pdf));
    }

    [Fact]
    public void A_short_report_is_one_page_and_a_report_with_nothing_but_a_title_still_is_a_page()
    {
        using var one = new DocsPages();
        var r = Long(true, 1, 1); r.sections[0].bullets.Clear(); r.sections[0].table = new ReportTable(); r.sources.Clear();
        one.Report(r);
        Assert.Equal(1, one.Count);
        using var empty = new DocsPages();
        empty.Report(new ReportData { title = "x" });
        Assert.Equal(1, empty.Count);
    }

    [Fact]
    public void The_fonts_that_are_drawn_with_have_Hangul()
    {
        Assert.True(DocsPages.HasHangulFont, "Malgun Gothic is not on this PC: Hangul may not be drawn");
        Assert.True(DocsPages.HasDisplayFont, "Jua (web\\Jua-Regular.ttf) was not found");
        // a page of Hangul is not a page of empty boxes: what is drawn differs from line to line
        using var pages = new DocsPages();
        pages.Report(new ReportData { korean = true, title = "가나다라 마바사", summary = "가나다라마바사 아자차카타파하", sections = { new ReportSection { heading = "한글", paragraphs = { "다람쥐 헌 쳇바퀴에 타고파" } } } });
        pages.SavePngs(_dir);
        using var bmp = new Bitmap(Path.Combine(_dir, "page-1.png"));
        var inks = new HashSet<int>();
        for (int x = 100; x < 900; x++) { int dark = 0; for (int y = 130; y < 300; y++) if (bmp.GetPixel(x, y).R < 140) dark++; inks.Add(dark); }
        Assert.True(inks.Count > 12, "the title looks like boxes, not letters");
    }

    [Fact]
    public void A_sheet_of_designs_is_one_page_of_all_and_one_page_each()
    {
        using var orig = new Bitmap(300, 300); using var opt = new Bitmap(1024, 1024);
        using (var g = Graphics.FromImage(opt)) g.FillEllipse(Brushes.Tomato, 100, 100, 800, 800);
        using var pages = new DocsPages();
        pages.DesignSheet("Mail app icon - new options", "받은 편지함을 여는 메일 앱 아이콘이다.", "회색 한 가지 색이라 작은 크기에서 눈에 띄지 않고 봉투 모양이 흐릿하다. " + Ko, orig,
            Enumerable.Range(1, 4).Select(n => ($"Option {n} - rounder, warmer and friendlier", Ko, (Image)opt)).ToList(), true, new DateTime(2026, 11, 18));
        Assert.Equal(5, pages.Count);
        if (Environment.GetEnvironmentVariable("LP_DOCS_KEEP") is { Length: > 0 } keep) pages.SavePngs(Path.Combine(keep, "designs"));
        Assert.Equal(5, DocsPdf.PageCount(DocsPdf.Write(pages.Jpegs(), "Mail")));
        using var three = new DocsPages();
        three.DesignSheet("x", "", "", null, Enumerable.Range(1, 3).Select(n => ("Option " + n, "", (Image)opt)).ToList(), false, DateTime.Now);
        Assert.Equal(4, three.Count);
    }

    [Fact]
    public void A_PDF_holds_its_pages_and_what_is_no_PDF_is_told_apart()
    {
        using var pages = new DocsPages();
        pages.Report(Long(false, 2, 2));
        var jpegs = pages.Jpegs();
        byte[] pdf = DocsPdf.Write(jpegs, "Title with 한글");
        string text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains($"/Count {jpegs.Count}", text);
        Assert.Contains("/Filter /DCTDecode", text);
        Assert.Contains("/MediaBox [0 0 595.28 841.89]", text);
        Assert.Contains("/Title <FEFF", text);
        Assert.Equal(jpegs.Count, DocsPdf.PageCount(pdf));
        // every object is where the table says it is
        int xref = int.Parse(System.Text.RegularExpressions.Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
        var lines = text[xref..].Split('\n');
        int count = int.Parse(lines[1].Split(' ')[1]);
        Assert.Equal(1 + 3 + jpegs.Count * 3, count);
        for (int i = 1; i < count; i++) Assert.StartsWith($"{i} 0 obj", text[int.Parse(lines[2 + i][..10])..]);
        Assert.Equal(-1, DocsPdf.PageCount(pdf[..(pdf.Length / 2)]));
        Assert.Equal(-1, DocsPdf.PageCount(System.Text.Encoding.ASCII.GetBytes("not a pdf at all, but long enough to be looked at by the reader of pdf files here")));
        Assert.Equal(-1, DocsPdf.PageCount(null));
        Assert.Throws<ArgumentException>(() => DocsPdf.Write(new List<byte[]>()));
        Assert.Throws<ArgumentException>(() => DocsPdf.Write(new List<byte[]> { new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 } }));
    }
}

/// <summary>The making itself, with models that answer from a list: the files that are written, where, and what the document says of itself.</summary>
public class DocsMakerTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "lp-docs-make-" + Guid.NewGuid().ToString("N")[..8]);
    string Store => Path.Combine(_root, "store");
    string User => Path.Combine(_root, "Documents", "Bambi");
    string Desktop => Path.Combine(_root, "Desktop");
    public DocsMakerTests() { Directory.CreateDirectory(Store); Directory.CreateDirectory(Desktop); }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    sealed class FakeModel : IDocsModel
    {
        public DocsResearch research = new();
        public JObject json = new();
        public int texts, pictures, failDrawing = -1;
        public bool sawNotes, sawFromMemory;
        public List<string> prompts = new();

        public Task<DocsResearch> ResearchAsync(string topic, string asked, CancellationToken ct) => Task.FromResult(research);

        public Task<JObject> AskJsonAsync(string system, string user, IReadOnlyList<(string mime, byte[] data, string name)> pictures, JObject schema, int maxTokens, CancellationToken ct)
        {
            texts++;
            sawNotes = user.Contains("--- NOTES ---"); sawFromMemory = user.Contains("could NOT be searched");
            return Task.FromResult(json);
        }

        public Task<byte[]> DrawAsync(byte[] referencePng, string transparent, string plain, CancellationToken ct)
        {
            int n = Interlocked.Increment(ref pictures);
            lock (prompts) prompts.Add(transparent);
            if (n - 1 == failDrawing) throw new Exception("drawing: HTTP 500 no");
            using var bmp = new Bitmap(256, 256);
            using (var g = Graphics.FromImage(bmp)) g.FillEllipse(Brushes.SteelBlue, 20, 20, 216, 216);
            using var ms = new MemoryStream(); bmp.Save(ms, ImageFormat.Png);
            return Task.FromResult(ms.ToArray());
        }
    }

    static JObject ReportJson() => JObject.Parse(@"{
        ""title"": ""Meta VR Start 2026: 핵심 정리?"", ""summary"": ""**요약** 한 문단이다."",
        ""facts"": [ { ""label"": ""마감"", ""value"": ""11월 19일 05:00 KST"" }, { ""label"": """", ""value"": ""x"" } ],
        ""sections"": [
          { ""heading"": ""대회 개요"", ""paragraphs"": [""첫 문단."", """", ""둘째 문단.""], ""bullets"": [""- 손이 먼저""], ""table"": { ""columns"": [], ""rows"": [] } },
          { ""heading"": ""규칙"", ""paragraphs"": [], ""bullets"": [], ""table"": { ""columns"": [""항목"", ""내용""], ""rows"": [[""프레임"", ""60 fps""], [""자세""], []] } },
          { ""heading"": ""빈 섹션"", ""paragraphs"": [], ""bullets"": [], ""table"": { ""columns"": [], ""rows"": [] } } ],
        ""sources"": [ { ""title"": ""Rules"", ""url"": ""https://example.com/rules/"" }, { ""title"": ""Made up"", ""url"": ""https://invented.example.org/x"" }, { ""title"": ""bad"", ""url"": ""file:///C:/x"" } ] }");

    [Fact]
    public async Task An_explicit_PNG_request_creates_one_downloadable_image_with_no_reference_screen()
    {
        const string prompt = "고양이를 PNG로 그려 줘";
        Assert.Equal(DocsRules.SinglePng, DocsRules.Plain(prompt)?.arg);
        Assert.Null(DocsRules.ExplicitFile("고양이 PNG를 만들지 마"));
        Assert.Null(DocsRules.ExplicitFile("고양이 PNG를 띄우는 게임을 만들어 줘"));
        var model = new FakeModel();
        var made = await DocsMaker.PictureAsync(model,prompt,Store,User,DateTime.Now,default);
        var file = Assert.Single(made.files);
        Assert.Equal("png",file.kind);
        Assert.True(File.Exists(Path.Combine(Store,"option-1-s.png")));
        var doc = new DocItem {kind=DocsRules.Designs, options=made.options, files=made.files};
        Assert.Same(file,DocsRules.Carried(doc).file);
        Assert.Equal(File.ReadAllBytes(file.path),File.ReadAllBytes(Assert.Single(DocsMaker.Place(doc,Desktop))));
        Assert.Equal(1, model.pictures);
        Assert.Equal(0, model.texts);
    }

    [Fact]
    public void The_report_is_cleaned_and_its_sources_are_only_pages_that_were_read()
    {
        var read = new[] { new ReportSource { title = "Official rules", url = "https://www.example.com/rules?utm=1" }, new ReportSource { title = "Blog", url = "https://example.com/blog" } };
        var r = ReportData.From(ReportJson(), "topic", true, true, read, DateTime.Now);
        Assert.True(r.Usable);
        Assert.Equal("Meta VR Start 2026: 핵심 정리?", r.title); Assert.Equal("요약 한 문단이다.", r.summary);
        Assert.Single(r.facts);
        Assert.Equal(2, r.sections.Count);                                      // the empty one is dropped
        Assert.Equal(new[] { "첫 문단.", "둘째 문단." }, r.sections[0].paragraphs);
        Assert.Equal(new[] { "손이 먼저" }, r.sections[0].bullets);
        Assert.False(r.sections[0].table.Any);
        Assert.True(r.sections[1].table.Any);
        Assert.Equal(2, r.sections[1].table.rows.Count); Assert.Equal(new[] { "자세", "" }, r.sections[1].table.rows[1]);
        Assert.Single(r.sources);                                               // the invented one and the file address are gone
        Assert.Equal("Rules", r.sources[0].title); Assert.Equal("https://www.example.com/rules", r.sources[0].url);     // (without what only tracks the reader)
        Assert.Equal("https://a.example.com/x?id=7#top", ReportData.Url("https://a.example.com/x?utm_source=n&id=7&trk=abc#top"));
        Assert.Equal("", ReportData.Url("javascript:alert(1)"));
        // written from memory: no sources at all, whatever the model names
        Assert.Empty(ReportData.From(ReportJson(), "topic", true, false, read, DateTime.Now).sources);
        // the model named none of the pages: the pages themselves
        var none = ReportJson(); none["sources"] = new JArray();
        Assert.Equal(2, ReportData.From(none, "topic", true, true, read, DateTime.Now).sources.Count);
        Assert.False(ReportData.From(new JObject(), "", false, false, null, DateTime.Now).Usable);
    }

    [Fact]
    public async Task A_report_becomes_pages_in_the_store_and_a_PDF_in_a_new_folder_of_the_users()
    {
        var model = new FakeModel { json = ReportJson(), research = new DocsResearch { web = true, notes = "Notes with facts.", sources = { new ReportSource { title = "Rules", url = "https://example.com/rules" } } } };
        var stages = new List<(string stage, float p)>();
        var now = new DateTime(2026, 11, 18, 1, 42, 0);
        var made = await DocsMaker.ReportAsync(model, "Meta VR Start 2026", "Meta VR Start 2026 알아보고 PDF로 만들어 줘", true, Store, User, now, (s, p, _) => stages.Add((s, p)), null, default);
        Assert.Equal(1, model.texts); Assert.True(model.sawNotes); Assert.True(made.web);
        Assert.Equal("Meta VR Start 2026: 핵심 정리?", made.title);
        Assert.Equal(Path.Combine(User, "2026-11-18 0142 Meta VR Start 2026 핵심 정리"), made.folder);
        Assert.Equal("Meta VR Start 2026 핵심 정리.pdf", made.files[0].name); Assert.Equal("pdf", made.files[0].kind);
        byte[] pdf = File.ReadAllBytes(made.files[0].path);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Equal(made.pages, DocsPdf.PageCount(pdf));
        Assert.True(made.pages >= 1 && File.Exists(Path.Combine(Store, "page-1.png")) && File.Exists(Path.Combine(Store, "page-1-s.png")));
        Assert.Equal(new[] { "Reading about it ...", "Writing ...", "Laying out the pages ..." }, stages.Take(3).Select(s => s.stage));
        Assert.True(stages.Zip(stages.Skip(1)).All(x => x.Second.p >= x.First.p));

        // the same again in the same minute: another folder, the first is not touched
        var stamp = File.GetLastWriteTimeUtc(made.files[0].path);
        var again = await DocsMaker.ReportAsync(model, "Meta VR Start 2026", "", true, Path.Combine(_root, "store2"), User, now, null, null, default);
        Assert.Equal(made.folder + " (2)", again.folder);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(made.files[0].path));
        Assert.Equal(2, Directory.GetDirectories(User).Length);
    }

    [Fact]
    public async Task Without_the_web_the_report_is_written_from_memory_and_says_so()
    {
        var model = new FakeModel { json = ReportJson(), research = new DocsResearch { web = false, how = "no tool" } };
        var made = await DocsMaker.ReportAsync(model, "topic", "make me a PDF about topic", false, Store, User, DateTime.Now, null, null, default);
        Assert.False(made.web); Assert.True(model.sawFromMemory); Assert.False(model.sawNotes);
        // notes that are claimed without the web having been searched do not count either
        model.research = new DocsResearch { web = true, notes = "  " };
        Assert.False((await DocsMaker.ReportAsync(model, "topic", "", false, Path.Combine(_root, "s3"), User, DateTime.Now, null, null, default)).web);
        model.json = new JObject();
        await Assert.ThrowsAnyAsync<Exception>(() => DocsMaker.ReportAsync(model, "topic", "", false, Path.Combine(_root, "s4"), User, DateTime.Now, null, null, default));
    }

    static byte[] Shot(int w, int h, ImageFormat f)
    {
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.SteelBlue); g.FillRectangle(Brushes.Gray, w / 2 - 40, h / 2 - 40, 80, 80); }
        using var ms = new MemoryStream(); bmp.Save(ms, f);
        return ms.ToArray();
    }

    static JObject DesignJson(bool found = true) => new()
    {
        ["found"] = found, ["x"] = 0.45, ["y"] = 0.43, ["w"] = 0.1, ["h"] = 0.14, ["title"] = "Mail app icon", ["what"] = "메일 앱 아이콘", ["weak"] = "회색이라 눈에 안 띈다", ["spoken"] = "메일 아이콘이네, 새로 그려 볼게!",
        ["options"] = new JArray(new[] { "simpler", "bolder", "rounder, warmer", "more playful", "a fifth that is not wanted" }.Select(n => new JObject { ["name"] = n, ["rationale"] = "이유 " + n, ["prompt"] = "Draw an envelope, " + n + ", flat colours." })),
    };

    Task<DocsMade> Designs(FakeModel model, List<string> stages = null, List<string> said = null) =>
        DocsMaker.DesignsAsync(model, Shot(800, 450, ImageFormat.Jpeg), Shot(1024, 768, ImageFormat.Png), Shot(1024, 768, ImageFormat.Png), 0.5f, 0.5f, 512, 384, "icon", "이 아이콘 별로지 않아? 새로 만들어 줘", true,
            Store, User, new DateTime(2026, 11, 18, 2, 5, 0), (s, _, _) => stages?.Add(s), l => said?.Add(l), null, default);

    [Fact]
    public async Task Designs_become_four_options_as_PNG_and_a_sheet_as_PDF()
    {
        var model = new FakeModel { json = DesignJson() };
        var stages = new List<string>(); var said = new List<string>();
        var made = await Designs(model, stages, said);
        Assert.Equal(1, model.texts); Assert.Equal(4, model.pictures);
        Assert.Equal(new[] { "메일 아이콘이네, 새로 그려 볼게!" }, said);
        Assert.Equal("Mail app icon - new options", made.title);
        Assert.Equal(Path.Combine(User, "2026-11-18 0205 Mail app icon - new options"), made.folder);
        // (the model named no way of drawing: each option gets one of its own, in order - the name says which)
        Assert.Equal(new[] { "Option 1 - geometric", "Option 2 - hand-drawn storybook", "Option 3 - monoline", "Option 4 - playful mascot" }, made.options.Select(o => o.name));
        Assert.Equal(new[] { "option-1.png", "option-2.png", "option-3.png", "option-4.png" }, made.options.Select(o => o.picture));
        Assert.Equal("pdf", made.files[0].kind);
        Assert.Equal(new[] { "Mail app icon - new options.pdf", "option-1.png", "option-2.png", "option-3.png", "option-4.png", "original.png" }, made.files.Select(f => f.name));
        Assert.All(made.files, f => Assert.True(File.Exists(f.path), f.path));
        Assert.Equal(5, made.pages);
        Assert.Equal(5, DocsPdf.PageCount(File.ReadAllBytes(made.files[0].path)));
        foreach (string f in new[] { "original.png", "option-1.png", "option-4.png", "option-2-s.png", "page-1.png", "page-5-s.png" }) Assert.True(File.Exists(Path.Combine(Store, f)), f);
        using (var orig = Image.FromFile(Path.Combine(Store, "original.png"))) { Assert.Equal(512, orig.Width); Assert.Equal(512, orig.Height); }
        Assert.Equal("Looking at it ...", stages[0]);
        Assert.Contains("Drawing option 1 of 4 ...", stages); Assert.Contains("Drawing option 4 of 4 ...", stages); Assert.Equal("Putting the sheet together ...", stages[^1]);
        // each drawing is asked for in its own direction, with the same subject
        Assert.Equal(4, model.prompts.Distinct().Count());
        Assert.All(model.prompts, p => { Assert.Contains("Mail app icon", p); Assert.Contains("Transparent background", p); });

        // the pick: picked.png and its name, and a later choice takes its place
        var item = new DocItem { id = "des-00000001", kind = DocsRules.Designs, status = DocsRules.Ready, folder = made.folder, files = made.files, options = made.options };
        Assert.True(DocsMaker.WritePicked(item, 2, made.options[1]));
        Assert.Equal("Option 2 - hand-drawn storybook", File.ReadAllText(Path.Combine(made.folder, "picked.txt")).Trim());
        Assert.Equal(File.ReadAllBytes(Path.Combine(made.folder, "option-2.png")), File.ReadAllBytes(Path.Combine(made.folder, "picked.png")));
        Assert.True(DocsMaker.WritePicked(item, 4, made.options[3]));
        Assert.Equal("Option 4 - playful mascot", File.ReadAllText(Path.Combine(made.folder, "picked.txt")).Trim());
        Assert.False(DocsMaker.WritePicked(item, 7, made.options[0]));

        // onto the Desktop: nothing is written over, and ONE file goes - the sheet while none is picked, the picked option as a picture after
        File.WriteAllText(Path.Combine(Desktop, "Mail app icon - new options.pdf"), "the user's own file");
        var all = DocsMaker.Place(item, Desktop);
        Assert.Single(all);
        Assert.Equal("Mail app icon - new options (2).pdf", Path.GetFileName(all[0]));
        Assert.Equal("the user's own file", File.ReadAllText(Path.Combine(Desktop, "Mail app icon - new options.pdf")));
        item.options[3].picked = true;
        var one = DocsMaker.Place(item, Desktop);
        Assert.Single(one);
        Assert.Equal("Mail app icon - new options - option 4.png", Path.GetFileName(one[0]));
        Assert.Equal(File.ReadAllBytes(Path.Combine(made.folder, "option-4.png")), File.ReadAllBytes(one[0]));
        Assert.Empty(DocsMaker.Place(item, Path.Combine(_root, "no such folder")));
        Assert.Empty(DocsMaker.Place(new DocItem { files = { new DocFile { kind = "pdf", path = Path.Combine(_root, "gone.pdf") } } }, Desktop));
    }

    [Fact]
    public async Task Nothing_at_the_spot_is_said_and_a_drawing_that_fails_leaves_the_others()
    {
        var nothing = new FakeModel { json = DesignJson(found: false) };
        var problem = await Assert.ThrowsAsync<DocsProblem>(() => Designs(nothing));
        Assert.Equal("designNotFound", problem.Line);
        Assert.Equal(0, nothing.pictures);
        Assert.False(Directory.Exists(User));                                   // nothing of the user's was touched

        var model = new FakeModel { json = DesignJson(), failDrawing = 2 };
        var made = await Designs(model);
        Assert.Equal(3, made.options.Count);
        Assert.Equal(new[] { "option-1.png", "option-2.png", "option-3.png" }, made.options.Select(o => o.picture));
        Assert.StartsWith("Option 3 - ", made.options[2].name);
        Assert.Equal(4, made.pages);
    }

    [Fact]
    public void A_report_on_the_Desktop_is_one_PDF()
    {
        string folder = Path.Combine(_root, "f"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "R.pdf"), "%PDF-");
        var d = new DocItem { kind = DocsRules.Report, status = DocsRules.Ready, folder = folder, files = { new DocFile { name = "R.pdf", kind = "pdf", path = Path.Combine(folder, "R.pdf") } } };
        Assert.Equal(new[] { Path.Combine(Desktop, "R.pdf") }, DocsMaker.Place(d, Desktop));
        Assert.Equal(new[] { Path.Combine(Desktop, "R (2).pdf") }, DocsMaker.Place(d, Desktop));
    }
}
