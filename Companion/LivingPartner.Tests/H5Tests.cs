using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// What the user's session of 2026-11-17 (23:30 - 23:45) showed to be missing, as rules: the name as the recogniser
/// writes it, PC actions from one fixed list, putting away what is open, make or play, the game that follows the
/// partner through the screen, what flies out of the glass, and the writer's design step and critic.
/// </summary>
public class NameTests
{
    [Theory]
    // the sentences of that evening, and the ways "밤비야" has come back
    [InlineData("밤미야, 유튜브로 들어가서 새로고침 해 줘")]
    [InlineData("밤미야, 이 디스플레이를 가져와 봐")]
    [InlineData("반비야, 메모장 열어 줘")]
    [InlineData("밤비아 이리 와")]
    [InlineData("뱀비야, 춤춰")]
    [InlineData("밤삐야 나와 봐")]
    [InlineData("밤피야, 나와")]
    [InlineData("밤비, 노래 틀어 줘")]
    [InlineData("밤비야, 여기 유튜브 화면에 들어가가지고 새로 고침을 한 번 해줘")]
    [InlineData("밤이야 이쪽으로 들어가 봐")]
    [InlineData("Nilo, open notepad")]
    [InlineData("bamby come here")]
    [InlineData("Bambie, play tennis")]
    [InlineData("Banbi, refresh the page")]
    [InlineData("Bambee, come out")]
    [InlineData("밤별아, 춤춰 봐")]
    [InlineData("이거 봐 봐 밤비야")]
    public void The_name_is_heard_however_the_recogniser_writes_it(string said)
    {
        Assert.True(NameRules.Named(said, out bool only), said);
        Assert.False(only, said);
    }

    [Theory]
    [InlineData("밤에야 겨우 끝났네")]
    [InlineData("밤에야 올 수 있대")]
    [InlineData("밤이 깊었네, 이제 자자")]
    [InlineData("방이야 뭐야 이게")]
    [InlineData("밥이야 빵이야")]
    [InlineData("밤새야 할 것 같아")]
    [InlineData("밤마다 이렇게 늦게 자면 안 되는데")]
    [InlineData("바로 가야 해")]
    [InlineData("Barbie is on tonight")]
    [InlineData("Zombie movies are fun")]
    [InlineData("Bamboo grows fast")]
    [InlineData("오늘 저녁 뭐 먹을까")]
    [InlineData("유튜브로 들어가서 새로고침 해 줘")]
    public void What_only_sounds_a_little_like_it_is_not_the_name(string said)
    {
        Assert.False(NameRules.Named(said, out _), said);
    }

    [Fact]
    public void A_bare_call_is_only_the_name_and_the_name_is_taken_out_for_the_plain_parsers()
    {
        Assert.True(NameRules.Named("밤미야!", out bool only)); Assert.True(only);
        Assert.True(NameRules.Named("밤비야", out only)); Assert.True(only);
        Assert.True(NameRules.Named("밤이야", out only)); Assert.True(only);
        Assert.True(NameRules.Named("Banbi?", out only)); Assert.True(only);
        Assert.True(NameRules.Named("밤비야 저기", out only)); Assert.True(only);
        Assert.True(NameRules.Named("밤미야, 나와", out only)); Assert.False(only);       // two syllables are an order
        Assert.Equal("새로고침 해 줘", NameRules.Strip("밤미야, 새로고침 해 줘").Trim(' ', ','));
        Assert.Equal("메모장 열어 줘", NameRules.Strip("밤비야, 메모장 열어 줘").Trim(' ', ','));
        Assert.Equal("open notepad", NameRules.Strip("Banbi, open notepad").Trim(' ', ','));
        Assert.Equal("밤에야 끝났어", NameRules.Strip("밤에야 끝났어").Trim());
        // one sound: 밤미야 is one jamo from 밤비야, 밤에야 two
        Assert.Equal(1, NameRules.Distance(NameRules.Jamo("밤미야"), NameRules.Jamo("밤비야")));
        Assert.Equal(2, NameRules.Distance(NameRules.Jamo("밤에야"), NameRules.Jamo("밤비야")));
        Assert.True(NameRules.NearCall("밤미야")); Assert.False(NameRules.NearCall("밤에야")); Assert.False(NameRules.NearCall("밤이")); Assert.False(NameRules.NearCall("방비"));
    }
}

public class PcActionTests
{
    /// <summary>Windows, faked: it writes down what it was asked and has a Desktop that lives in memory.</summary>
    sealed class FakeShell : IPcShell
    {
        public readonly List<string> did = new();
        public readonly HashSet<string> there = new(StringComparer.OrdinalIgnoreCase);
        public bool front = true, starts = true;
        public bool Start(string target, string argument) { did.Add($"start {target}{(string.IsNullOrEmpty(argument) ? "" : " [" + Path.GetFileName(argument) + "]")}"); return starts; }
        public bool Foreground(IntPtr hwnd) { did.Add("front " + hwnd); return front; }
        public bool Press(ushort[] hold, ushort key, int times) { did.Add($"press {string.Join("+", hold.Select(h => h.ToString("X2")).Append(key.ToString("X2")))} x{times}"); return true; }
        public bool Type(string text) { did.Add("type " + text); return true; }
        public string Desktop => @"C:\Users\someone\Desktop";
        public bool Exists(string path) => there.Contains(path);
        public void CreateFolder(string path) { if (there.Contains(path)) throw new IOException("exists"); there.Add(path); did.Add("folder " + Path.GetFileName(path)); }
        public void WriteNew(string path, string text) { if (there.Contains(path)) throw new IOException("exists"); there.Add(path); did.Add($"file {Path.GetFileName(path)} = {text}"); }
        public string Title(IntPtr hwnd) => "a window";
    }

    static readonly IntPtr Window = new(4242);

    [Fact]
    public void Only_the_fixed_list_can_be_done()
    {
        Assert.Equal(21, PcRules.Dos.Length);
        foreach (string d in PcRules.Dos) Assert.NotEqual("unknown", PcRules.Plan(d, d == "openApp" ? "notepad" : d == "openUrl" ? "https://example.com" : "x").problem);
        foreach (string no in new[] { "", "run", "cmd", "exec", "delete", "deleteFile", "shell", "powershell", "closeWindow", "click", "rm -rf", "format" })
        {
            var p = PcRules.Plan(no, "notepad");
            Assert.False(p.Ok, no); Assert.Equal("", p.kind); Assert.Equal("", p.target);
            var shell = new FakeShell();
            var o = PcRun.Execute(p, shell, Window, true);
            Assert.False(o.ok); Assert.Empty(shell.did); Assert.NotEqual("", o.line);
        }
    }

    [Theory]
    [InlineData("notepad", "notepad.exe")] [InlineData("메모장", "notepad.exe")] [InlineData("Notepad", "notepad.exe")] [InlineData("메모장 앱", "notepad.exe")] [InlineData("the notepad app", "notepad.exe")]
    [InlineData("계산기", "calc.exe")] [InlineData("calculator", "calc.exe")] [InlineData("윈도우 계산기", "calc.exe")]
    [InlineData("탐색기", "explorer.exe")] [InlineData("파일 탐색기", "explorer.exe")] [InlineData("explorer", "explorer.exe")]
    [InlineData("그림판", "mspaint.exe")] [InlineData("paint", "mspaint.exe")]
    [InlineData("브라우저", PcRules.DefaultBrowser)] [InlineData("크롬", PcRules.DefaultBrowser)]
    [InlineData("설정", "ms-settings:")] [InlineData("settings", "ms-settings:")]
    public void A_program_of_the_allow_list_is_started_by_its_fixed_target(string arg, string target)
    {
        var p = PcRules.Plan("openApp", arg);
        Assert.True(p.Ok, arg); Assert.Equal(PcPlan.Start, p.kind); Assert.Equal(target, p.target); Assert.Equal("", p.args);
        var shell = new FakeShell();
        var o = PcRun.Execute(p, shell, IntPtr.Zero, true);
        Assert.True(o.ok); Assert.Equal(new[] { "start " + target }, shell.did);
        Assert.EndsWith("열었어.", o.line);
    }

    [Theory]
    [InlineData("cmd")] [InlineData("cmd.exe")] [InlineData("powershell")] [InlineData("regedit")] [InlineData("C:\\Windows\\System32\\cmd.exe")] [InlineData("notepad.exe & del x")]
    [InlineData("터미널")] [InlineData("명령 프롬프트")] [InlineData("shutdown")] [InlineData("format c:")] [InlineData("")] [InlineData("..\\..\\evil.bat")] [InlineData("notepad C:\\secret.txt /p")]
    public void Nothing_that_is_not_on_the_allow_list_is_started(string arg)
    {
        var p = PcRules.Plan("openApp", arg);
        Assert.False(p.Ok, arg); Assert.Equal("app", p.problem); Assert.Equal("", p.target);
        var shell = new FakeShell();
        var o = PcRun.Execute(p, shell, IntPtr.Zero, true);
        Assert.False(o.ok); Assert.Empty(shell.did); Assert.Contains("목록에 없어", o.line);
    }

    [Theory]
    [InlineData("https://example.com/a?b=c", "https://example.com/a?b=c")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("www.example.co.kr/page", "https://www.example.co.kr/page")]
    public void An_http_address_is_opened(string arg, string url)
    {
        Assert.Equal(url, PcRules.Url(arg));
        Assert.Equal(url, PcRules.Plan("openUrl", arg).target);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")] [InlineData("javascript:alert(1)")] [InlineData("ms-settings:")] [InlineData("C:\\Windows\\notepad.exe")] [InlineData("\\\\server\\share\\x.exe")]
    [InlineData("ftp://example.com/x")] [InlineData("https://example.com/ a b")] [InlineData("cmd /c del x")] [InlineData("https://user:pw@example.com/")] [InlineData("notepad")] [InlineData("")] [InlineData("data:text/html,x")]
    [InlineData("https://example.com/\"& calc")]
    public void Nothing_but_an_http_address_is_opened(string arg)
    {
        Assert.Equal("", PcRules.Url(arg));
        var p = PcRules.Plan("openUrl", arg);
        Assert.False(p.Ok); Assert.Equal("url", p.problem);
        var shell = new FakeShell();
        Assert.False(PcRun.Execute(p, shell, IntPtr.Zero, false).ok); Assert.Empty(shell.did);
    }

    [Fact]
    public void A_search_and_YouTube_open_a_results_page_and_the_partner_does_not_pretend_a_video_plays()
    {
        Assert.Equal("https://www.google.com/search?q=%EB%B0%A4%EB%B9%84%20%EC%82%AC%EC%8A%B4", PcRules.Plan("webSearch", "밤비 사슴").target);
        Assert.Equal("empty", PcRules.Plan("webSearch", "  ").problem);
        var yt = PcRules.Plan("youtube", "로파이 음악 & more");
        Assert.Equal("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString("로파이 음악 & more"), yt.target);
        Assert.Equal("https://www.youtube.com/", PcRules.Plan("youtube", "").target);
        var shell = new FakeShell();
        var o = PcRun.Execute(yt, shell, IntPtr.Zero, true);
        Assert.True(o.ok); Assert.Single(shell.did);
        Assert.Contains("검색 결과를 열었어", o.line); Assert.DoesNotContain("틀었어", o.line); Assert.DoesNotContain("재생", o.line);
        Assert.Contains("results", PcRun.Execute(yt, new FakeShell(), IntPtr.Zero, false).line); Assert.DoesNotContain("playing", PcRun.Execute(yt, new FakeShell(), IntPtr.Zero, false).line);
        // (a query cannot carry a second address or a line break into the address)
        Assert.DoesNotContain("\n", PcRules.YoutubeUrl("a\nb")); Assert.StartsWith("https://www.youtube.com/results?search_query=", PcRules.YoutubeUrl("https://evil.example/x"));
    }

    [Theory]
    [InlineData("refresh", "74", 1)] [InlineData("back", "12+25", 1)] [InlineData("forward", "12+27", 1)] [InlineData("scrollDown", "22", 1)] [InlineData("scrollUp", "21", 1)]
    [InlineData("fullscreen", "7A", 1)] [InlineData("newTab", "11+54", 1)] [InlineData("closeTab", "11+57", 1)]
    public void A_window_key_goes_to_the_window_that_is_meant_and_only_when_it_is_in_front(string @do, string keys, int times)
    {
        var p = PcRules.Plan(@do, "");
        Assert.True(p.Ok); Assert.Equal(PcPlan.Keys, p.kind); Assert.True(p.window);
        var shell = new FakeShell();
        var o = PcRun.Execute(p, shell, Window, true);
        Assert.True(o.ok); Assert.Equal(new[] { "front 4242", $"press {keys} x{times}" }, shell.did);
        // no window is meant: nothing is pressed
        shell = new FakeShell();
        o = PcRun.Execute(p, shell, IntPtr.Zero, true);
        Assert.False(o.ok); Assert.Empty(shell.did); Assert.Equal(PcRules.NoWindowLine(true), o.line);
        // the window could not be brought to the front: nothing is pressed (a key must never land in another window)
        shell = new FakeShell { front = false };
        o = PcRun.Execute(p, shell, Window, true);
        Assert.False(o.ok); Assert.Equal(new[] { "front 4242" }, shell.did); Assert.Equal(PcRules.NotInFrontLine(true), o.line);
    }

    [Theory]
    [InlineData("playPause", "B3", 1)] [InlineData("nextTrack", "B0", 1)] [InlineData("prevTrack", "B1", 1)] [InlineData("volumeUp", "AF", 5)] [InlineData("volumeDown", "AE", 5)] [InlineData("mute", "AD", 1)]
    public void A_media_key_needs_no_window(string @do, string key, int times)
    {
        var p = PcRules.Plan(@do, "whatever");
        Assert.True(p.Ok); Assert.False(p.window);
        var shell = new FakeShell();
        Assert.True(PcRun.Execute(p, shell, IntPtr.Zero, true).ok);
        Assert.Equal(new[] { $"press {key} x{times}" }, shell.did);
    }

    [Theory]
    [InlineData("여행 사진", "여행 사진")]
    [InlineData("  my / folder  ", "my folder")]
    [InlineData("..\\..\\Windows\\System32", "Windows System32")]
    [InlineData("C:\\Users\\x\\evil", "C Users x evil")]
    [InlineData("a:b*c?d\"e<f>g|h", "a b c d e f g h")]
    [InlineData("...", "새 폴더")] [InlineData("", "새 폴더")] [InlineData("   ", "새 폴더")] [InlineData("..", "새 폴더")]
    [InlineData("CON", "새 폴더")] [InlineData("nul", "새 폴더")] [InlineData("com1.txt", "새 폴더")] [InlineData("LPT9", "새 폴더")]
    [InlineData("name. ", "name")] [InlineData("line\nbreak\ttab", "line break tab")]
    public void A_folder_name_is_one_safe_name_never_a_path(string arg, string name)
    {
        string safe = PcRules.SafeName(arg, "새 폴더");
        Assert.Equal(name, safe);
        Assert.DoesNotContain('\\', safe); Assert.DoesNotContain('/', safe); Assert.DoesNotContain(':', safe); Assert.DoesNotContain("..", safe);
        Assert.Equal(Path.Combine(@"C:\Users\someone\Desktop", safe), Path.GetFullPath(Path.Combine(@"C:\Users\someone\Desktop", safe)));      // it stays on the Desktop
        Assert.True(PcRules.SafeName(new string('가', 300), "x").Length <= 60);
    }

    [Fact]
    public void A_new_folder_and_a_new_note_never_touch_what_is_there()
    {
        var shell = new FakeShell();
        string desk = shell.Desktop;
        var folder = PcRules.Plan("newFolder", "여행 사진");
        Assert.Contains("'여행 사진' 폴더", PcRun.Execute(folder, shell, IntPtr.Zero, true).line);
        Assert.Contains("'여행 사진 (2)' 폴더", PcRun.Execute(folder, shell, IntPtr.Zero, true).line);
        Assert.Contains("'여행 사진 (3)' 폴더", PcRun.Execute(folder, shell, IntPtr.Zero, true).line);
        Assert.Equal(new[] { "folder 여행 사진", "folder 여행 사진 (2)", "folder 여행 사진 (3)" }, shell.did);
        // a FILE of that name is in the way as well
        shell.there.Add(Path.Combine(desk, "새 폴더"));
        Assert.Contains("'새 폴더 (2)'", PcRun.Execute(PcRules.Plan("newFolder", ""), shell, IntPtr.Zero, true).line);
        // a note: a new .txt named after its first words, opened in Notepad; a second one of the same words gets (2)
        shell = new FakeShell();
        var note = PcRules.Plan("newNote", "내일 3시 회의\n자료 챙기기");
        Assert.Equal(PcPlan.Note, note.kind);
        var o = PcRun.Execute(note, shell, IntPtr.Zero, true);
        Assert.True(o.ok); Assert.Contains("'내일 3시 회의' 메모", o.line);
        Assert.Equal(new[] { "file 내일 3시 회의.txt = 내일 3시 회의\n자료 챙기기", "start notepad.exe [내일 3시 회의.txt]" }, shell.did);
        o = PcRun.Execute(note, shell, IntPtr.Zero, true);
        Assert.Contains("'내일 3시 회의 (2)' 메모", o.line);
        Assert.Equal("empty", PcRules.Plan("newNote", " \n ").problem);
        Assert.Equal("x", PcRules.FreeName("x", "", _ => false)); Assert.Equal("x (2).txt", PcRules.FreeName("x", ".txt", n => n == "x.txt"));
        // a note's name can be no path either
        Assert.Equal("C Windows evil", PcRules.NoteName("C:\\Windows\\evil", true));
    }

    [Fact]
    public void Typing_goes_to_the_window_in_front_without_control_characters()
    {
        var p = PcRules.Plan("typeText", "안녕\r\n하세요\t!\u0003\u001b");
        Assert.Equal(PcPlan.Type, p.kind); Assert.True(p.window); Assert.Equal("안녕 하세요 !", p.text);
        var shell = new FakeShell();
        Assert.True(PcRun.Execute(p, shell, Window, true).ok);
        Assert.Equal(new[] { "front 4242", "type 안녕 하세요 !" }, shell.did);
        Assert.Equal(PcRules.MaxType, PcRules.Plan("typeText", new string('a', 5000)).text.Length);
        Assert.False(PcRun.Execute(p, new FakeShell(), IntPtr.Zero, true).ok);
    }

    [Theory]
    // the user's own sentences first
    [InlineData("여기 유튜브 화면에 들어가가지고 새로 고침을 한 번 해줘", "refresh", "")]
    [InlineData("메모장을 열어줄래?", "openApp", "notepad")]
    [InlineData("새로고침 해 줘", "refresh", "")] [InlineData("refresh the page", "refresh", "")]
    [InlineData("메모장 열어 줘", "openApp", "notepad")] [InlineData("open notepad", "openApp", "notepad")] [InlineData("can you open the calculator", "openApp", "calculator")]
    [InlineData("그림판 켜 줘", "openApp", "paint")] [InlineData("설정 좀 열어 봐", "openApp", "settings")] [InlineData("파일 탐색기 열어", "openApp", "explorer")]
    [InlineData("유튜브에서 고양이 영상 틀어 줘", "youtube", "고양이 영상")] [InlineData("유튜브 열어 줘", "youtube", "")] [InlineData("play lofi music on youtube", "youtube", "lofi music")]
    [InlineData("강아지 훈련 검색해 줘", "webSearch", "강아지 훈련")] [InlineData("search for meta quest", "webSearch", "meta quest")]
    [InlineData("폴더 하나 만들어 줘", "newFolder", "")] [InlineData("여행 사진 폴더 만들어 줘", "newFolder", "여행 사진")] [InlineData("make a folder called Trip", "newFolder", "Trip")]
    [InlineData("소리 좀 키워 줘", "volumeUp", "")] [InlineData("볼륨 줄여", "volumeDown", "")] [InlineData("음소거 해 줘", "mute", "")]
    [InlineData("전체 화면으로 해 줘", "fullscreen", "")] [InlineData("새 탭 열어 줘", "newTab", "")] [InlineData("이 탭 닫아 줘", "closeTab", "")]
    [InlineData("스크롤 좀 내려 줘", "scrollDown", "")] [InlineData("위로 올려 줘", "scrollUp", "")] [InlineData("다음 영상", "nextTrack", "")] [InlineData("영상 멈춰 줘", "playPause", "")]
    [InlineData("뒤로 가", "back", "")]
    public void The_plain_wordings_of_the_PC_actions(string said, string @do, string arg)
    {
        var p = PcRules.Plain(said);
        Assert.True(p.HasValue, said);
        Assert.Equal(@do, p.Value.@do); Assert.Equal(arg, p.Value.arg);
        Assert.True(PcRules.Plan(p.Value.@do, p.Value.arg).Ok);
    }

    [Theory]
    [InlineData("노래 틀어 줘")] [InlineData("이리 나와 봐")] [InlineData("이쪽으로 들어가 봐")] [InlineData("상점 열어 줘")] [InlineData("가방 열어 줘")] [InlineData("테니스 하자")]
    [InlineData("오목 게임 만들어 줘")] [InlineData("의자 하나 만들어 줘")] [InlineData("타이머 켜 줘")] [InlineData("화면 다시 찾아")] [InlineData("이 화면이 어떤 화면이야?")]
    [InlineData("앞으로 집중 모드라고 하면 비 내려 줘")] [InlineData("춤춰")] [InlineData("cmd 열어 줘")] [InlineData("터미널 열어")] [InlineData("이 파일 지워 줘")] [InlineData("")]
    public void What_is_no_PC_action_is_left_alone(string said)
    {
        Assert.False(PcRules.Plain(said).HasValue, said);
    }

    [Fact]
    public void The_model_is_told_the_whole_list_and_that_it_can()
    {
        string prompt = PcRules.PromptSection();
        foreach (string d in PcRules.Dos) Assert.Contains("\"" + d + "\"", prompt);
        foreach (var a in PcRules.Apps) Assert.Contains(a.id, prompt);
        Assert.Contains("never say you cannot refresh", prompt); Assert.Contains("do not say a video is playing", prompt);
    }
}

public class CloseAndPlayTests
{
    [Theory]
    [InlineData("이 MR 환경 다시 지워 줘", "screen")]          // (the user's own two sentences)
    [InlineData("이 MR 환경 닫아줘", "screen")]
    [InlineData("화면 닫아 줘", "screen")] [InlineData("디스플레이 치워", "screen")] [InlineData("보드 꺼 줘", "screen")] [InlineData("가져온 화면 좀 없애 줘", "screen")] [InlineData("close the screen", "screen")] [InlineData("put the display away", "screen")]
    [InlineData("상점 닫아", "shop")] [InlineData("마켓 치워 줘", "shop")] [InlineData("close the shop", "shop")]
    [InlineData("메뉴 닫아 줘", "menu")] [InlineData("가방 치워", "menu")]
    [InlineData("배경 치워 줘", "place")] [InlineData("바다 꺼 줘", "place")] [InlineData("주변 풍경 없애 줘", "place")]
    [InlineData("다 닫아 줘", "all")] [InlineData("전부 치워", "all")] [InlineData("close everything", "all")]
    public void What_is_to_be_put_away(string said, string what)
    {
        Assert.Equal(what, CloseRules.Plain(said));
    }

    [Theory]
    [InlineData("비 꺼 줘")] [InlineData("노래 꺼")] [InlineData("타이머 꺼 줘")] [InlineData("게임 꺼")] [InlineData("이 탭 닫아 줘")] [InlineData("이 화면이 어떤 화면이야?")] [InlineData("화면 가져와")]
    [InlineData("1번 화면 가져와 봐")] [InlineData("의자 치워 줘")] [InlineData("번역기 지워 줘")] [InlineData("다시 지워 줘")] [InlineData("화면 다시 찾아")] [InlineData("소리 꺼 줘")] [InlineData("")]
    public void What_is_no_wish_to_put_something_away(string said)
    {
        Assert.Null(CloseRules.Plain(said));
    }

    [Fact]
    public void The_thing_named_is_closed_when_it_is_open_and_the_next_one_otherwise()
    {
        static string[] O(string what, bool screen, bool shop, bool menu, bool place) => CloseRules.Order(what, screen, shop, menu, place).ToArray();
        Assert.Equal(new[] { "screen" }, O("screen", true, true, false, true));
        // "이 MR 환경 지워 줘" with no display brought in: the shop board, then the bag, then the surroundings
        Assert.Equal(new[] { "shop" }, O("screen", false, true, true, true));
        Assert.Equal(new[] { "menu" }, O("screen", false, false, true, true));
        Assert.Equal(new[] { "place" }, O("screen", false, false, false, true));
        Assert.Empty(O("screen", false, false, false, false));
        Assert.Equal(new[] { "place" }, O("place", true, true, false, true));
        Assert.Equal(new[] { "screen" }, O("", true, false, false, true));
        Assert.Equal(new[] { "screen", "shop", "menu", "place" }, O("all", true, true, true, true));
        Assert.Equal(new[] { "shop", "place" }, O("all", false, true, false, true));
        // the model's word, however it comes
        Assert.Equal("screen", CloseRules.What("screen")); Assert.Equal("screen", CloseRules.What("MR 환경")); Assert.Equal("shop", CloseRules.What("상점")); Assert.Equal("place", CloseRules.What("surroundings"));
        Assert.Equal("all", CloseRules.What("ALL")); Assert.Equal("", CloseRules.What("")); Assert.Equal("", CloseRules.What("whatever"));
        // it says what was closed
        Assert.Equal("가져온 화면 치웠어.", CloseRules.Line(new[] { "screen" }, true)); Assert.Equal("상점 닫았어.", CloseRules.Line(new[] { "shop" }, true));
        Assert.Equal("지금은 닫을 게 없어.", CloseRules.Line(new string[0], true)); Assert.Equal("다 치웠어.", CloseRules.Line(new[] { "screen", "place" }, true));
        Assert.Equal("I've put the screen away.", CloseRules.Line(new[] { "screen" }, false));
    }

    static GamePackDef Pack(string id, string title) => new() { id = id, template = "script", title = title };

    [Theory]
    [InlineData("계산기를 틀어볼래?", false, true)]             // (the sentence that made a SECOND calculator)
    [InlineData("방금 만든 거 틀어봐", false, true)]
    [InlineData("계산기 켜 줘", false, true)] [InlineData("골키퍼 하자", false, true)] [InlineData("오목 실행해", false, true)] [InlineData("let's play omok", false, true)] [InlineData("open the calculator", false, true)]
    [InlineData("계산기 만들어 줘", true, false)] [InlineData("축구 게임을 만들어 줘", true, false)] [InlineData("오목 게임 하나 제작해 줘", true, false)] [InlineData("make me a calculator", true, false)]
    [InlineData("build a timer app", true, false)] [InlineData("create a game", true, false)] [InlineData("책상 하나 줄게", true, false)] [InlineData("우리 게임 만들자", true, false)]
    [InlineData("이 화면 뭐야?", false, false)]
    public void Making_needs_a_word_of_making_and_playing_a_word_of_playing(string said, bool make, bool play)
    {
        Assert.Equal(make, PlayRules.MakeWord(said));
        if (!make) Assert.Equal(play, PlayRules.PlayWord(said));
    }

    [Fact]
    public void What_was_made_last_and_what_is_there_already()
    {
        foreach (string s in new[] { "방금 만든 거 틀어봐", "아까 만든 게임 하자", "방금 네가 만든 거 켜 봐", "조금 전에 만든 거 열어 줘", "play that one you just made", "open the one you just made", "start the game we just made" })
        {
            Assert.True(PlayRules.JustMade(s), s); Assert.True(PlayRules.PlayWord(s), s); Assert.False(PlayRules.MakeWord(s), s);
        }
        Assert.False(PlayRules.JustMade("계산기 틀어 줘")); Assert.False(PlayRules.JustMade("게임 만들어 줘"));

        var own = new[] { Pack("game-5f73b6a2", "손 계산기"), Pack("game-cd155593", "골키퍼"), Pack("game-1", "오목놀이") };
        // asking to make what is there already: found by its title, or by the word it mostly consists of
        Assert.Equal("game-5f73b6a2", PlayRules.OwnGameNamed(own, "계산기 만들어 줘")?.id);
        Assert.Equal("game-5f73b6a2", PlayRules.OwnGameNamed(own, "계산기")?.id);
        Assert.Equal("game-5f73b6a2", PlayRules.OwnGameNamed(own, "손 계산기 하나 만들어 줄래?")?.id);
        Assert.Equal("game-cd155593", PlayRules.OwnGameNamed(own, "골키퍼 게임 만들어 줘")?.id);
        Assert.Equal("game-1", PlayRules.OwnGameNamed(own, "오목 게임 만들어줘")?.id);
        // something else is simply made
        Assert.Null(PlayRules.OwnGameNamed(own, "축구 게임을 만들어 줘. 너가 저 화면 안에서 공을 차면은 내가 막을게."));
        Assert.Null(PlayRules.OwnGameNamed(own, "슈팅 게임 만들어 줘")); Assert.Null(PlayRules.OwnGameNamed(own, "게임 하나 만들어 줘")); Assert.Null(PlayRules.OwnGameNamed(own, "스톱워치 만들어 줘"));
        Assert.Null(PlayRules.OwnGameNamed(new GamePackDef[0], "계산기 만들어 줘")); Assert.Null(PlayRules.OwnGameNamed(own, ""));
        // the answer to "이미 있어. 새로 만들까, 그걸 틀까?"
        foreach (string s in new[] { "새로", "새로 만들어 줘", "응 새로 만들어", "다시 만들어 줘", "new one", "make a new one", "remake it" }) Assert.Equal("new", PlayRules.DuplicateAnswer(s));
        foreach (string s in new[] { "그거 틀어", "그걸로 하자", "틀어 줘", "그냥 틀어", "있는 거", "play that one", "that one" }) Assert.Equal("play", PlayRules.DuplicateAnswer(s));
        foreach (string s in new[] { "", "오늘 날씨 어때", "그 계산기 색깔을 바꿔서 새로운 느낌으로 다시 만들어 주면 좋겠는데", "응" }) Assert.Equal("", PlayRules.DuplicateAnswer(s));
        // the user's own calculator before the PC's, unless the PC is named
        Assert.True(PlayRules.OnPc("윈도우 계산기 열어 줘")); Assert.True(PlayRules.OnPc("open the PC calculator")); Assert.False(PlayRules.OnPc("계산기 틀어 줘"));
        // a thing of the bag by its name
        var items = new List<ItemDef> { new() { id = "item-1", name = "야구배트", status = "ready" }, new() { id = "item-2", name = "배트", status = "ready" }, new() { id = "item-3", name = "북", status = "failed" } };
        Assert.Equal("item-1", PlayRules.ThingNamed(items, "", "야구배트 꺼내 봐")?.id); Assert.Equal("item-2", PlayRules.ThingNamed(items, "배트", "그거 써 봐")?.id); Assert.Null(PlayRules.ThingNamed(items, "", "의자 꺼내 줘"));
    }
}

public class GlassFollowTests
{
    static GamePackDef Pack(string id = "game-cd155593", string title = "골키퍼", int lives = 3) => new() { id = id, template = "script", title = title, lives = lives };

    [Fact]
    public void A_game_follows_the_partner_through_the_screen()
    {
        var s = new GlassState { runId = 700 };
        GlassRules.Begin(s, Pack(), GlassRules.Pc, @"\\.\DISPLAY3", 12);
        Assert.Equal(701, s.runId);
        Assert.True(GlassRules.Accept(s, new GlassState { runId = 701, phase = "play", score = 7, lives = 2, level = 3, seconds = 41.5f, banner = "Level 3!" }, Roles.Pc));
        GlassRules.Merge(s, new GlassState { runId = 701, phase = "play", score = 7, lives = 2, level = 3, seconds = 41.5f, banner = "Level 3!" });

        // the crossing begins: the run is paused - phase "moving", the same run number, and the side that ran it is not heard any more
        var carry = GlassRules.Pause(s);
        Assert.NotNull(carry);
        Assert.Equal(GlassRules.Moving, s.phase); Assert.Equal(701, s.runId); Assert.Equal(GlassRules.Pc, s.side); Assert.Equal("", s.banner);
        Assert.Equal((7, 2, 3, 41.5f, "game-cd155593", "pc"), (carry.score, carry.lives, carry.level, carry.seconds, carry.packId, carry.side));
        Assert.False(GlassRules.Accept(s, new GlassState { runId = 701, phase = "play", score = 99 }, Roles.Pc));
        Assert.False(GlassRules.OutTaken(s, 701, Roles.Pc));
        Assert.Null(GlassRules.Pause(s));       // (it is paused already)

        // the crossing settled: the partner is on the desk now - the same pack, a NEW run on the other side, with what it had
        Assert.Equal("", GlassRules.ResumeProblem(carry, GlassRules.Side(Env.Mr, true, true, false)));
        GlassRules.Resume(s, Pack(), GlassRules.Mr, "", 12, carry);
        Assert.Equal(702, s.runId); Assert.Equal(GlassRules.Mr, s.side); Assert.Equal("", s.monitorId); Assert.Equal("intro", s.phase); Assert.Equal("game-cd155593", s.packId);
        Assert.Equal((7, 2, 3, 41.5f, 12), (s.score, s.lives, s.level, s.seconds, s.best));
        // the headset runs it now; the PC's late report of the old run fits nothing
        Assert.True(GlassRules.Accept(s, new GlassState { runId = 702, phase = "play", score = 8 }, Roles.Quest));
        Assert.False(GlassRules.Accept(s, new GlassState { runId = 701, phase = "play", score = 8 }, Roles.Pc));
        Assert.False(GlassRules.Accept(s, new GlassState { runId = 702, phase = "play", score = 8 }, Roles.Pc));
        // (a game cannot report the host's own phase)
        Assert.False(GlassRules.Accept(s, new GlassState { runId = 702, phase = "moving" }, Roles.Quest));

        // and back in again
        carry = GlassRules.Pause(s);
        GlassRules.Resume(s, Pack(), GlassRules.Pc, @"\\.\DISPLAY1", 12, carry);
        Assert.Equal((703, "pc", @"\\.\DISPLAY1"), (s.runId, s.side, s.monitorId));
        Assert.Equal("pc", GlassRules.OtherSide("mr")); Assert.Equal("mr", GlassRules.OtherSide("pc"));
    }

    [Fact]
    public void A_crossing_that_fails_ends_the_game_and_a_fresh_start_carries_the_defaults()
    {
        var s = new GlassState();
        GlassRules.Begin(s, Pack(lives: 5), GlassRules.Mr, "", 0);
        // (a fresh start: score 0, level 1, the pack's lives, 0 seconds - what the side that starts applies, always)
        Assert.Equal((0, 5, 1, 0f), (s.score, s.lives, s.level, s.seconds));
        var carry = GlassRules.Pause(s);
        // the partner is still on the desk (cancelled, failed): the same side - the game ends
        Assert.NotEqual("", GlassRules.ResumeProblem(carry, GlassRules.Side(Env.Mr, true, true, false)));
        // it is inside now but no PC client can run it there; or nobody at all
        Assert.Equal("", GlassRules.ResumeProblem(carry, GlassRules.Side(Env.Pc, true, true, false)));
        Assert.NotEqual("", GlassRules.ResumeProblem(carry, GlassRules.Side(Env.Pc, false, true, false)));       // (no PC client: the desk again - where it was)
        Assert.NotEqual("", GlassRules.ResumeProblem(carry, ""));
        Assert.NotEqual("", GlassRules.ResumeProblem(null, GlassRules.Pc));
        // nothing to pause: no game, or one that is over
        Assert.Null(GlassRules.Pause(new GlassState()));
        var over = new GlassState(); GlassRules.Begin(over, Pack(), GlassRules.Pc, "", 0); over.phase = "over";
        Assert.Null(GlassRules.Pause(over));
        // lives never come back as 0 (a run that goes on has one at least), a level never under 1
        GlassRules.Resume(s, Pack(), GlassRules.Pc, "", 0, new GlassRules.Carry { score = -3, lives = 0, level = 0, seconds = float.NaN });
        Assert.Equal((0, 1, 1, 0f), (s.score, s.lives, s.level, s.seconds));
    }

    [Fact]
    public void What_flies_out_of_the_glass_is_passed_on_only_for_the_run_that_is_on()
    {
        var s = new GlassState { runId = 40 };
        GlassRules.Begin(s, Pack(), GlassRules.Pc, @"\\.\DISPLAY3", 0);
        s.phase = "play";
        // glass.out: from the side that runs the game, for this run
        Assert.True(GlassRules.OutTaken(s, 41, Roles.Pc));
        Assert.False(GlassRules.OutTaken(s, 40, Roles.Pc));           // an old run
        Assert.False(GlassRules.OutTaken(s, 41, Roles.Quest));        // not the side that runs it
        Assert.False(GlassRules.OutTaken(s, 41, Roles.Tool));
        // glass.out.result: from a headset, for this run, one of the three verdicts
        Assert.True(GlassRules.OutResultTaken(s, new GlassOutResult { runId = 41, id = 3, what = "blocked" }, Roles.Quest));
        Assert.True(GlassRules.OutResultTaken(s, new GlassOutResult { runId = 41, id = 3, what = "hit" }, Roles.Sim));
        Assert.True(GlassRules.OutResultTaken(s, new GlassOutResult { runId = 41, id = 3, what = "missed" }, Roles.Quest));
        Assert.False(GlassRules.OutResultTaken(s, new GlassOutResult { runId = 40, id = 3, what = "hit" }, Roles.Quest));
        Assert.False(GlassRules.OutResultTaken(s, new GlassOutResult { runId = 41, id = 3, what = "hit" }, Roles.Pc));
        Assert.False(GlassRules.OutResultTaken(s, new GlassOutResult { runId = 41, id = 3, what = "exploded" }, Roles.Quest));
        Assert.False(GlassRules.OutResultTaken(s, null, Roles.Quest));
        // over, or no game: nothing
        s.phase = "over"; Assert.False(GlassRules.OutTaken(s, 41, Roles.Pc));
        GlassRules.Clear(s); Assert.False(GlassRules.OutTaken(s, 41, Roles.Pc)); Assert.False(GlassRules.OutResultTaken(s, new GlassOutResult { runId = 41, what = "hit" }, Roles.Quest));
        // on the desk the headset runs the game: it is the running side
        GlassRules.Begin(s, Pack(), GlassRules.Mr, "", 0);
        Assert.True(GlassRules.OutTaken(s, s.runId, Roles.Quest)); Assert.False(GlassRules.OutTaken(s, s.runId, Roles.Pc));
    }
}

public class WriterQualityTests
{
    /// <summary>A model that answers from a list, and a critic that answers from another.</summary>
    sealed class Model : IScriptModel
    {
        readonly Queue<JObject> _answers; readonly Queue<object> _verdicts;
        public readonly List<string> asked = new(), judged = new(); public readonly List<bool> repairs = new(); public readonly List<JObject> shapes = new();
        public Model(IEnumerable<JObject> answers, params object[] verdicts) { _answers = new Queue<JObject>(answers); _verdicts = new Queue<object>(verdicts); }
        public string Name => "fake";
        public Task<JObject> AskAsync(string system, string user, JObject schema, int maxTokens, CancellationToken ct, bool repair = false)
        {
            asked.Add(user); repairs.Add(repair); shapes.Add(schema);
            if (_answers.Count == 0) throw new Exception("no more answers");
            return Task.FromResult(_answers.Dequeue());
        }
        public Task<JObject> JudgeAsync(string system, string user, JObject schema, int maxTokens, CancellationToken ct)
        {
            judged.Add(user);
            object v = _verdicts.Count > 0 ? _verdicts.Dequeue() : null;
            if (v is Exception e) throw e;
            return Task.FromResult((JObject)v);
        }
    }

    static JObject Answer(string script, string design = "1. The player moves a hand under the stars.\n2. Nilo runs along.\n3. Nothing crosses the glass.\n4. Three misses end it.\n5. Upright: things fall.") => new JObject
    {
        ["design"] = design, ["title"] = "Test", ["howTo"] = "Tap it.", ["script"] = script, ["phrases"] = new JArray(), ["art"] = new JArray(),
    };
    static JObject Verdict(params string[] defects) => new JObject { ["recognised"] = defects.Length == 0, ["defects"] = new JArray(defects.Cast<object>().ToArray()) };

    const string Broken = "local t = 0\nfunction update(dt)\n  rect(W / 2, 0.5, 0.2, 0.2, 'pink')\n  local nothing = nil\n  nothing.x = 1\nend\n";
    const string NoHands = "function update(dt)\n  disc(W / 2 + 0.3 * math.sin(time()), 0.5, 0.05, 'pink')\nend\n";
    static string Good => GlassScriptExamples.Find("catch").lua;
    static string Other => GlassScriptExamples.Find("umbrella").lua;

    static ScriptRequest Football(int maxRepairs = -1) => new() { kind = ScriptRequest.Game, words = "축구 게임을 만들어 줘. 너가 저 화면 안에서 공을 차면은 내가 막을게. 물론 화면 바깥에서도 할 수 있어.", language = "Korean", id = "game-test", maxRepairs = maxRepairs };

    static (ScriptResult res, List<string> stages, List<string> log) Write(Model model, ScriptRequest r)
    {
        var stages = new List<string>(); var log = new List<string>();
        var res = ScriptWriter.WriteAsync(model, r, ScriptWriter.GameExamples(), (stage, n, p) => { string s = ScriptWriter.StageText(stage, n); if (stages.Count == 0 || stages[^1] != s) stages.Add(s); }, log.Add, CancellationToken.None).Result;
        return (res, stages, log);
    }

    [Fact]
    public void A_game_is_designed_before_it_is_written()
    {
        // the answer's FIRST field is the design
        var shape = ScriptWriter.ShapeFor(ScriptRequest.Game);
        Assert.Equal("design", ((JObject)shape["properties"]).Properties().First().Name);
        Assert.Equal("design", (string)shape["required"][0]);
        Assert.Equal("script", ((JObject)shape["properties"]).Properties().Select(p => p.Name).SkipWhile(n => n != "howTo").Skip(1).First());
        Assert.Null(ScriptWriter.ShapeFor(ScriptRequest.Thing)["properties"]["design"]);
        var model = new Model(new[] { Answer(Good) }, Verdict());
        var (res, _, log) = Write(model, Football());
        Assert.True(res.ok); Assert.StartsWith("1. The player moves a hand", res.design);
        Assert.Contains("- design: FIRST, before the program", model.asked[0]); Assert.Contains("what crosses the glass", model.asked[0]);
        Assert.Equal("design", ((JObject)model.shapes[0]["properties"]).Properties().First().Name);
        Assert.Contains(log, l => l.Contains("design: 1. The player moves a hand"));
        Assert.DoesNotContain(log, l => l.Contains("function update"));
    }

    [Fact]
    public void The_critic_reads_what_passed_the_check_and_its_defects_go_into_one_repair()
    {
        // it runs - but it is a flat picture of a goalkeeper: the critic says so, and ONE repair is asked for at the repair's effort
        var model = new Model(new[] { Answer(Good), Answer(Other, "1. Nilo kicks the ball out of the screen.") },
            Verdict("The ball is only drawn inside the field: Nilo must kick it OUT with shoot_out.", "The save must come from out_events."));
        var (res, stages, log) = Write(model, Football());
        Assert.True(res.ok); Assert.True(res.judged); Assert.True(res.judgedRepairTaken);
        Assert.Equal(Other.Trim(), res.script.Trim()); Assert.Equal("1. Nilo kicks the ball out of the screen.", res.design);
        Assert.Equal(3, res.calls); Assert.Equal(1, res.repairs); Assert.Equal(2, res.defects.Count);
        // (H7: the writing for the critic is asked at the effort of a first writing - the program runs, the user is waiting; at the repair's effort it took 111 s and 187 s)
        Assert.Equal(new[] { false, false }, model.repairs);
        Assert.Single(model.judged);
        // the critic was given the request, the design and the program
        Assert.Contains("축구 게임을 만들어 줘", model.judged[0]); Assert.Contains("=== THE DESIGN the writer gave ===", model.judged[0]); Assert.Contains("1. The player moves a hand", model.judged[0]);
        Assert.Contains("  1| ", model.judged[0]); Assert.Contains("at most 3", model.judged[0]);
        // the repair was given the defects, word for word
        Assert.Contains("NOT WHAT I ASKED FOR: The ball is only drawn inside the field", model.asked[1]); Assert.Contains("NOT WHAT I ASKED FOR: The save must come from out_events.", model.asked[1]);
        Assert.Contains("=== HARD RULES ===", model.asked[1]);
        Assert.Equal(new[] { "Writing the code", "Trying it out", "Checking it is what you asked", "Fixing a bug (1)", "Trying it out" }, stages);
        Assert.Contains(log, l => l.Contains("critic") && l.Contains("2 defect(s)"));
        Assert.Contains(log, l => l.Contains("for the critic") && l.Contains("taken"));
    }

    [Fact]
    public void The_budget_is_the_first_writing_the_critic_and_two_repairs()
    {
        Assert.Equal(2, ScriptWriter.Repairs(new ScriptRequest { kind = ScriptRequest.Game })); Assert.Equal(3, ScriptWriter.Repairs(new ScriptRequest { kind = ScriptRequest.Thing }));
        // broken, mended, and then the critic finds something: first + repair + critic + repair = 4 calls, and no more
        var model = new Model(new[] { Answer(Broken), Answer(Good), Answer(Other), Answer(Good) }, Verdict("Nilo has no part in it."), Verdict("Still not it."));
        var (res, _, _) = Write(model, Football());
        Assert.True(res.ok); Assert.Equal(4, res.calls); Assert.Equal(2, res.repairs); Assert.Equal(Other.Trim(), res.script.Trim()); Assert.Single(model.judged);
        // both repairs went on making it run: the critic is still asked, but nothing is left to repair with - the program is kept
        model = new Model(new[] { Answer(Broken), Answer(Broken), Answer(Good), Answer(Other) }, Verdict("Something is missing."));
        (res, _, var log) = Write(model, Football());
        Assert.True(res.ok); Assert.Equal(4, res.calls); Assert.Equal(2, res.repairs); Assert.Equal(Good.Trim(), res.script.Trim()); Assert.False(res.judgedRepairTaken); Assert.Single(res.defects);
        Assert.Contains(log, l => l.Contains("no repair is left"));
        // a repair for the critic that does not run is NOT taken: what ran is kept
        model = new Model(new[] { Answer(Good), Answer(Broken) }, Verdict("Something is missing."));
        (res, _, log) = Write(model, Football());
        Assert.True(res.ok); Assert.Equal(Good.Trim(), res.script.Trim()); Assert.False(res.judgedRepairTaken); Assert.Equal(3, res.calls);
        Assert.Contains(log, l => l.Contains("NOT taken"));
        // ... and neither is one in which something "does not work" when what there was is clean
        model = new Model(new[] { Answer(Good), Answer(NoHands) }, Verdict("Something is missing."));
        (res, _, _) = Write(model, Football());
        Assert.Equal(Good.Trim(), res.script.Trim()); Assert.False(res.judgedRepairTaken);
        // recognised: no repair; no critic (the model has none), or a critic that fails: the program is kept, nothing breaks
        model = new Model(new[] { Answer(Good) }, Verdict());
        (res, _, _) = Write(model, Football());
        Assert.True(res.ok); Assert.Equal(2, res.calls); Assert.Equal(0, res.repairs); Assert.True(res.judged); Assert.Empty(res.defects);
        model = new Model(new[] { Answer(Good) });
        (res, _, _) = Write(model, Football());
        Assert.True(res.ok); Assert.Equal(1, res.calls); Assert.False(res.judged);
        model = new Model(new[] { Answer(Good) }, new Exception("HTTP 500"));
        (res, _, _) = Write(model, Football());
        Assert.True(res.ok); Assert.Equal(1, res.calls); Assert.False(res.judged);
        // a program that never ran is not shown to the critic
        model = new Model(new[] { Answer(Broken), Answer(Broken), Answer(Broken) }, Verdict("x x x x x x"));
        (res, _, _) = Write(model, Football());
        Assert.False(res.ok); Assert.Empty(model.judged); Assert.Equal(3, res.calls);
        // measuring: no repairs at all - the first writing and the critic
        model = new Model(new[] { Answer(Good), Answer(Other) }, Verdict("Something is missing."));
        (res, _, _) = Write(model, Football(0));
        Assert.Equal(2, res.calls); Assert.Equal(0, res.repairs); Assert.Single(res.defects); Assert.Equal(Good.Trim(), res.script.Trim());
        // at most three defects, and only sentences
        Assert.Equal(3, ScriptWriter.Defects(Verdict("one defect here", "two defects here", "three defects here", "four defects here")).Count);
        Assert.Empty(ScriptWriter.Defects(Verdict("", "ok"))); Assert.Empty(ScriptWriter.Defects(null));
        // a thing or a skill has no critic and no design
        var thing = new Model(new[] { new JObject { ["title"] = "", ["howTo"] = "눌러 봐.", ["script"] = "function on_use()\n  pet_say('Hi!')\n  sound('pop')\nend\n", ["art"] = new JArray(), ["phrases"] = new JArray() } }, Verdict("x x x x x x"));
        var r = new ScriptRequest { kind = ScriptRequest.Thing, words = "작은 종", language = "Korean", id = "item-test", item = new ItemDef { id = "item-test", name = "종", kind = "box", uses = new[] { "decor" }, sizeM = 0.06f } };
        var written = ScriptWriter.WriteAsync(thing, r, ScriptWriter.RoomExamples(false), null, null, CancellationToken.None).Result;
        Assert.Empty(thing.judged); Assert.False(written.judged); Assert.Null(thing.shapes[0]["properties"]["design"]);
    }

    [Fact]
    public void The_hard_rules_use_the_new_API_and_the_examples_fit_the_request()
    {
        string doc = GlassScriptApi.Doc;
        var model = new Model(new[] { Answer(Good) }, Verdict());
        Write(model, Football());
        string prompt = model.asked[0];
        // (each rule is only there when the API text names the function it speaks of)
        Assert.Equal(doc.Contains("button(") && doc.Contains("panel("), prompt.Contains("A TOOL or app (calculator, timer, notes"));
        Assert.Equal(doc.Contains("shoot_out(") && doc.Contains("out_events("), prompt.Contains("the thing must REALLY leave the glass"));
        Assert.Equal(doc.Contains("aim()") && doc.Contains("shots()"), prompt.Contains("aim() for the crosshair and shots() for the shots"));
        Assert.Contains("flat(true) ONLY for games played ON a board", prompt);
        if (doc.Contains("button(")) { Assert.Contains("never flat(true)", prompt); Assert.Contains("at least 0.14 across", prompt); }
        // what a request needs to be shown
        Assert.NotEqual(0, ScriptWriter.Needs("계산기")); Assert.NotEqual(0, ScriptWriter.Needs("슈팅 게임: 밤비가 화면 안에서 나한테 쏘고, 나는 왼손으로 막으면서 밤비 분신들을 쏴"));
        Assert.NotEqual(0, ScriptWriter.Needs(Football().words)); Assert.Equal(0, ScriptWriter.Needs("오목 게임 만들어 줘")); Assert.Equal(0, ScriptWriter.Needs("카드 짝 맞추기"));
        var all = ScriptWriter.GameExamples();
        // the very thing that is asked for is never shown ...
        foreach (var (words, id) in new[] { (Football().words, "keeper"), ("슈팅 게임: 밤비가 화면 안에서 나한테 쏘고, 나는 왼손으로 막으면서 밤비 분신들을 쏴", "shooter"), ("계산기", "calculator"), ("계산기 만들어 줘", "calculator") })
            if (all.Any(e => e.id == id)) Assert.DoesNotContain(ScriptWriter.Pick(all, words), e => e.id == id);
        // ... but a program that shows what it needs is: one that sends things out for the football game, one built from buttons for a tool
        if (all.Any(e => e.lua.Contains("shoot_out(") && !ScriptWriter.SameThing(e, Football().words)))
            Assert.Contains(ScriptWriter.Pick(all, Football().words), e => e.lua.Contains("shoot_out("));
        if (all.Any(e => e.lua.Contains("button(") && !ScriptWriter.SameThing(e, "스톱워치 만들어 줘")))
            Assert.Contains(ScriptWriter.Pick(all, "스톱워치 만들어 줘"), e => e.lua.Contains("button("));
        Assert.Equal(ScriptWriter.ExamplesShown, ScriptWriter.Pick(all, Football().words).Count);
        Assert.Equal("Checking it is what you asked", ScriptWriter.StageText(ScriptWriter.Review, 0));
    }
}
