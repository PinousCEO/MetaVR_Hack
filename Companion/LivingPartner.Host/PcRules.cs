using System.Text;
using System.Text.RegularExpressions;

namespace LivingPartner.Host;

// PC ACTIONS, the rules. "Bambi must be able to WORK on my computer: refresh, put on another YouTube video, open
// Notepad, make a folder." Everything the partner can do on the PC is in ONE FIXED LIST (Dos). There is no free
// command line, nothing is deleted and nothing is overwritten:
//   openApp     arg = a program from the allow-list (Apps) - never a path, never a command ("browser" = the user's
//               default browser, started on its own start page: no site is chosen for them)
//   openUrl     arg = an http / https address, in the default browser
//   webSearch   arg = what to look for
//   youtube     arg = what to look for: the RESULTS page is opened (no video is started by this - the partner says so)
// WHAT THE PARTNER SAYS about it names no brand and no product ("the video site", "the web", "the notepad"): the
// bubble is on the screen of the headset. (What the user's own words name is theirs.)
//   refresh back forward scrollDown scrollUp fullscreen newTab closeTab     one key to the window the user means
//   playPause nextTrack prevTrack volumeUp volumeDown mute                   the media keys (they need no window)
//   newFolder   arg = its name: a new folder on the Desktop (never over one that is there: " (2)")
//   newNote     arg = the text: a new .txt on the Desktop, opened in Notepad
//   typeText    arg = the text, typed into the window in front - only when the user dictates
// Nothing of Windows is in this file: PcRules.Plan turns (do, arg) into a PcPlan, PcActions carries it out.

/// <summary>What is to be done, decided: one of start / keys / folder / note / type - or a problem.</summary>
public sealed class PcPlan
{
    public const string Start = "start", Keys = "keys", Folder = "folder", Note = "note", Type = "type";
    public string @do = "", kind = "";
    /// <summary>start: what is started (a program of the allow-list, a settings address, an http(s) address) and its one argument.</summary>
    public string target = "", args = "";
    /// <summary>start: the name of the program for the line ("메모장" / "Notepad"); folder / note: the name; type: the text.</summary>
    public string ko = "", en = "", text = "";
    /// <summary>keys: the keys held (virtual key codes), the key pressed, how often, and whether a window must be in front for it.</summary>
    public ushort[] hold = Array.Empty<ushort>(); public ushort key; public int times = 1; public bool window;
    /// <summary>Why not ("" = it can be done): unknown | app | url | empty.</summary>
    public string problem = "";
    public bool Ok => problem.Length == 0;
}

public static class PcRules
{
    public static readonly string[] Dos =
    {
        "openApp", "openUrl", "webSearch", "youtube",
        "refresh", "back", "forward", "playPause", "nextTrack", "prevTrack", "volumeUp", "volumeDown", "mute", "scrollDown", "scrollUp", "fullscreen", "newTab", "closeTab",
        "newFolder", "newNote", "typeText",
    };

    /// <summary>A program the partner may start: what is started is fixed here, the user's words only choose among them.</summary>
    public sealed class App
    {
        public string id = "", target = "", ko = "", en = "";
        public string[] words = Array.Empty<string>();
    }

    /// <summary>
    /// What openApp "browser" starts: the user's DEFAULT browser, by itself, on whatever page it starts with - no
    /// site is chosen for them (it used to be a named search site). This is not an address and not a program name:
    /// the shell (IPcShell.Start) looks the default browser up. Nothing the user says can become this target.
    /// </summary>
    public const string DefaultBrowser = "lp:default-browser";

    /// <summary>
    /// The program of a shell "open" command as the registry has it ("C:\...\browser.exe" --single-argument %1): the
    /// full path of an .exe, "" for anything else (a relative name, another kind of file, nothing).
    /// </summary>
    public static string ExeOfCommand(string command)
    {
        string c = (command ?? "").Trim();
        if (c.Length == 0 || c.Length > 1000) return "";
        var m = Regex.Match(c, "^\"([^\"]+)\"");
        string exe = m.Success ? m.Groups[1].Value.Trim() : Regex.Match(c, @"^(.+?\.exe)(\s|$)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        if (exe.Length == 0 || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || exe.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return "";
        return Path.IsPathFullyQualified(exe) ? exe : "";
    }

    public static readonly App[] Apps =
    {
        new App { id = "notepad", target = "notepad.exe", ko = "메모장", en = "the notepad", words = new[] { "notepad", "메모장", "노트패드", "memo" } },
        new App { id = "calculator", target = "calc.exe", ko = "계산기", en = "the calculator", words = new[] { "calculator", "calc", "계산기" } },
        new App { id = "explorer", target = "explorer.exe", ko = "파일 탐색기", en = "the file browser", words = new[] { "explorer", "fileexplorer", "files", "탐색기", "파일탐색기", "내pc", "내컴퓨터", "폴더창" } },
        new App { id = "paint", target = "mspaint.exe", ko = "그림판", en = "the paint program", words = new[] { "paint", "mspaint", "그림판" } },
        new App { id = "browser", target = DefaultBrowser, ko = "브라우저", en = "the browser", words = new[] { "browser", "webbrowser", "internet", "chrome", "edge", "브라우저", "인터넷", "크롬", "엣지", "웹" } },
        new App { id = "settings", target = "ms-settings:", ko = "설정", en = "the settings", words = new[] { "settings", "setting", "설정", "환경설정" } },
        new App { id = "clock", target = "ms-clock:", ko = "시계", en = "the clock", words = new[] { "clock", "alarm", "alarms", "시계", "알람" } },
        new App { id = "photos", target = "ms-photos:", ko = "사진", en = "the photo viewer", words = new[] { "photos", "photo", "사진", "사진앱" } },
    };

    static string Flat(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>The program of the allow-list that words name ("메모장", "notepad", "the calculator app"); null = none: nothing is started.</summary>
    public static App FindApp(string arg)
    {
        string f = Flat(arg);
        if (f.Length == 0 || f.Length > 40) return null;
        foreach (var a in Apps) if (f == a.id || a.words.Contains(f)) return a;
        // ("메모장 앱", "the notepad app", "윈도우 계산기": the word with a little round it - the longest word that is in there)
        string bare = Regex.Replace(f, @"^(the|윈도우|windows|pc|컴퓨터)|(앱|어플|프로그램|app|program|창)$", "");
        (App app, int len) best = (null, 0);
        foreach (var a in Apps)
            foreach (string w in a.words)
                if (w.Length >= 3 && bare.Contains(w) && w.Length > best.len && bare.Length - w.Length <= 3) best = (a, w.Length);       // ("notepad.exe" yes; a name with a command behind it no)
        return best.app;
    }

    // ---- keys (virtual key codes)
    public const ushort VkControl = 0x11, VkMenu = 0x12, VkLeft = 0x25, VkRight = 0x27, VkPrior = 0x21, VkNext = 0x22, VkF5 = 0x74, VkF11 = 0x7A, VkT = 0x54, VkW = 0x57,
        VkVolumeMute = 0xAD, VkVolumeDown = 0xAE, VkVolumeUp = 0xAF, VkMediaNext = 0xB0, VkMediaPrev = 0xB1, VkMediaPlayPause = 0xB3;

    /// <summary>The key of a key action: (held, key, times, needs a window in front). key 0 = not a key action.</summary>
    public static (ushort[] hold, ushort key, int times, bool window) KeysOf(string @do) => @do switch
    {
        "refresh" => (Array.Empty<ushort>(), VkF5, 1, true),
        "back" => (new[] { VkMenu }, VkLeft, 1, true),
        "forward" => (new[] { VkMenu }, VkRight, 1, true),
        "scrollDown" => (Array.Empty<ushort>(), VkNext, 1, true),
        "scrollUp" => (Array.Empty<ushort>(), VkPrior, 1, true),
        "fullscreen" => (Array.Empty<ushort>(), VkF11, 1, true),
        "newTab" => (new[] { VkControl }, VkT, 1, true),
        "closeTab" => (new[] { VkControl }, VkW, 1, true),
        "playPause" => (Array.Empty<ushort>(), VkMediaPlayPause, 1, false),
        "nextTrack" => (Array.Empty<ushort>(), VkMediaNext, 1, false),
        "prevTrack" => (Array.Empty<ushort>(), VkMediaPrev, 1, false),
        "volumeUp" => (Array.Empty<ushort>(), VkVolumeUp, 5, false),         // (one press is two of a hundred)
        "volumeDown" => (Array.Empty<ushort>(), VkVolumeDown, 5, false),
        "mute" => (Array.Empty<ushort>(), VkVolumeMute, 1, false),
        _ => (Array.Empty<ushort>(), (ushort)0, 0, false),
    };

    // ---- addresses
    /// <summary>An http / https address as it is opened; "" when it is anything else (a file, a script, a settings address, a command).</summary>
    public static string Url(string arg)
    {
        string s = (arg ?? "").Trim();
        if (s.Length == 0 || s.Length > 2000 || s.Any(ch => char.IsWhiteSpace(ch) || char.IsControl(ch) || ch is '"' or '\'' or '<' or '>' or '`' or '|' or '^')) return "";
        // ("youtube.com", "www.naver.com/abc": a bare host name is an https address)
        if (!s.Contains("://") && Regex.IsMatch(s, @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+(:\d{1,5})?([/?#].*)?$", RegexOptions.IgnoreCase)) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return "";
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return "";
        if (string.IsNullOrEmpty(u.Host) || u.UserInfo.Length > 0) return "";
        return u.AbsoluteUri;
    }

    static string Query(string arg)
    {
        string q = Regex.Replace((arg ?? "").Replace('\r', ' ').Replace('\n', ' '), @"\s+", " ").Trim();
        return q.Length > 200 ? q[..200] : q;
    }

    public static string SearchUrl(string query) => "https://www.google.com/search?q=" + Uri.EscapeDataString(Query(query));
    /// <summary>The results page for a search; with nothing to look for, the front page.</summary>
    public static string YoutubeUrl(string query) => Query(query).Length == 0 ? "https://www.youtube.com/" : "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(Query(query));

    // ---- names and texts
    static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    /// <summary>
    /// A name for a new folder or file on the Desktop out of what was said: ONE name, never a path - no \ / : * ? " &lt; &gt; |,
    /// no control characters, no dots or spaces at its ends, none of the device names, 60 characters at most. With
    /// nothing left: the fallback.
    /// </summary>
    public static string SafeName(string arg, string fallback)
    {
        var sb = new StringBuilder();
        foreach (char ch in arg ?? "")
        {
            if (char.IsControl(ch) || ch is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|') { sb.Append(' '); continue; }
            sb.Append(ch);
        }
        string s = Regex.Replace(sb.ToString(), @"\s+", " ").Trim(' ', '.');
        if (s.Length > 60) s = s[..60].Trim(' ', '.');
        if (s.Length == 0 || s.All(ch => ch == '.' || ch == ' ')) return fallback;
        string stem = s.Contains('.') ? s[..s.IndexOf('.')] : s;
        if (Reserved.Contains(stem.Trim().ToUpperInvariant())) return fallback;
        return s;
    }

    /// <summary>The name under which something NEW is put: the name itself when nothing is there, else "name (2)", "name (3)" ... - what is there is never touched. ext: ".txt" or "".</summary>
    public static string FreeName(string name, string ext, Func<string, bool> exists)
    {
        if (!exists(name + ext)) return name + ext;
        for (int n = 2; n < 1000; n++) if (!exists($"{name} ({n}){ext}")) return $"{name} ({n}){ext}";
        return $"{name} ({Guid.NewGuid().ToString("N")[..6]}){ext}";
    }

    /// <summary>What is typed or written: no control characters but the line end (typing: none at all - an Enter could send a form), cut at max.</summary>
    public static string SafeText(string arg, int max, bool lines)
    {
        var sb = new StringBuilder();
        foreach (char ch in (arg ?? "").Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (ch == '\n') { sb.Append(lines ? '\n' : ' '); continue; }
            if (ch == '\t') { sb.Append(' '); continue; }
            if (char.IsControl(ch)) continue;
            sb.Append(ch);
        }
        string s = sb.ToString().Trim();
        return s.Length > max ? s[..max] : s;
    }

    /// <summary>The first words of a note as its file name.</summary>
    public static string NoteName(string text, bool korean)
    {
        string first = (text ?? "").Split('\n')[0].Trim();
        if (first.Length > 24) first = first[..24];
        return SafeName(first, korean ? "밤비 메모" : "Bambi note");
    }

    public const int MaxNote = 4000, MaxType = 500;

    // ---- the plan
    /// <summary>What (do, arg) comes to. Never throws; an unknown `do`, a program that is not on the list or an address that is not http(s) is a plan with a problem and nothing to carry out.</summary>
    public static PcPlan Plan(string @do, string arg, bool korean = true)
    {
        string d = Dos.FirstOrDefault(x => string.Equals(x, (@do ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
        var p = new PcPlan { @do = d };
        arg = (arg ?? "").Trim();
        switch (d)
        {
            case "": p.problem = "unknown"; break;
            case "openApp":
            {
                var app = FindApp(arg);
                if (app == null) { p.problem = "app"; p.text = SafeText(arg, 40, false); break; }
                p.kind = PcPlan.Start; p.target = app.target; p.ko = app.ko; p.en = app.en; p.text = app.id;
                break;
            }
            case "openUrl":
            {
                string url = Url(arg);
                if (url.Length == 0) { p.problem = "url"; break; }
                p.kind = PcPlan.Start; p.target = url;
                break;
            }
            case "webSearch":
                if (Query(arg).Length == 0) { p.problem = "empty"; break; }
                p.kind = PcPlan.Start; p.target = SearchUrl(arg); p.text = Query(arg);
                break;
            case "youtube":
                p.kind = PcPlan.Start; p.target = YoutubeUrl(arg); p.text = Query(arg);
                break;
            case "newFolder":
                p.kind = PcPlan.Folder; p.text = SafeName(arg, korean ? "새 폴더" : "New folder");
                break;
            case "newNote":
                p.text = SafeText(arg, MaxNote, true);
                if (p.text.Length == 0) { p.problem = "empty"; break; }
                p.kind = PcPlan.Note; p.ko = p.en = NoteName(p.text, korean);
                break;
            case "typeText":
                p.text = SafeText(arg, MaxType, false);
                if (p.text.Length == 0) { p.problem = "empty"; break; }
                p.kind = PcPlan.Type; p.window = true;
                break;
            default:
            {
                var k = KeysOf(d);
                if (k.key == 0) { p.problem = "unknown"; break; }
                p.kind = PcPlan.Keys; p.hold = k.hold; p.key = k.key; p.times = k.times; p.window = k.window;
                break;
            }
        }
        return p;
    }

    // ---- what the partner says
    static string Josa(string word, string withFinal, string without)
    {
        if (string.IsNullOrEmpty(word)) return without;
        char c = word[^1];
        if (c < 0xAC00 || c > 0xD7A3) return without;
        return (c - 0xAC00) % 28 != 0 ? withFinal : without;
    }

    /// <summary>Why a plan with a problem is not carried out.</summary>
    public static string ProblemLine(PcPlan p, bool korean) => p.problem switch
    {
        "app" => korean ? "그 프로그램은 내가 열 수 있는 목록에 없어. 메모장, 계산기, 탐색기, 그림판, 브라우저, 설정은 열 수 있어." : "That program isn't on my list. I can open the notepad, the calculator, the file browser, the paint program, the browser and the settings.",
        "url" => korean ? "그 주소는 못 열어. http나 https로 시작하는 웹 주소만 열 수 있어." : "I can't open that. I only open web addresses that start with http or https.",
        "empty" => korean ? "뭘 쓸지 말해 줘." : "Tell me what to put in it.",
        _ => korean ? "그건 내가 컴퓨터에서 할 수 있는 일이 아니야." : "That isn't something I can do on the computer.",
    };

    /// <summary>What was really done (name: the folder or file as it was really called; title: the window the key went to).</summary>
    public static string DoneLine(PcPlan p, bool korean, string name = "", string title = "")
    {
        string q = p.text;
        switch (p.@do)
        {
            case "openApp": return korean ? $"{p.ko} 열었어." : $"I opened {p.en}.";
            case "openUrl": return korean ? "웹에서 열었어." : "I opened it on the web.";
            case "webSearch": return korean ? $"웹에서 '{q}' 검색해서 띄웠어." : $"I searched the web for '{q}'.";
            // (honest: the results are open - no video is playing because of this)
            case "youtube": return q.Length == 0 ? (korean ? "영상 사이트 열었어." : "I opened the video site.") : korean ? $"영상 사이트에서 '{q}' 검색 결과를 열었어. 영상은 네가 골라 줘." : $"I opened the video site's results for '{q}'. Pick the video yourself.";
            case "newFolder": return korean ? $"바탕화면에 '{name}' 폴더 만들었어." : $"I made the folder '{name}' on the Desktop.";
            case "newNote": return korean ? $"바탕화면에 '{name}' 메모를 만들어서 열었어." : $"I wrote the note '{name}' on the Desktop and opened it.";
            case "typeText": return korean ? "받아 적었어." : "I typed it.";
            case "refresh": return korean ? "새로고침 했어." : "I refreshed it.";
            case "back": return korean ? "뒤로 갔어." : "I went back.";
            case "forward": return korean ? "앞으로 갔어." : "I went forward.";
            case "scrollDown": return korean ? "아래로 내렸어." : "I scrolled down.";
            case "scrollUp": return korean ? "위로 올렸어." : "I scrolled up.";
            case "fullscreen": return korean ? "전체 화면 눌렀어." : "I pressed full screen.";
            case "newTab": return korean ? "새 탭 열었어." : "I opened a new tab.";
            case "closeTab": return korean ? "탭 닫았어." : "I closed the tab.";
            case "playPause": return korean ? "재생 버튼 눌렀어." : "I pressed play / pause.";
            case "nextTrack": return korean ? "다음 걸로 넘겼어." : "I skipped to the next one.";
            case "prevTrack": return korean ? "이전 걸로 돌렸어." : "I went to the one before.";
            case "volumeUp": return korean ? "소리 키웠어." : "I turned it up.";
            case "volumeDown": return korean ? "소리 줄였어." : "I turned it down.";
            case "mute": return korean ? "음소거 눌렀어." : "I pressed mute.";
            default: return korean ? "했어." : "Done.";
        }
    }

    public static string NoWindowLine(bool korean) => korean ? "어느 창인지 모르겠어. 창을 가리키면서 다시 말해 줘." : "I can't tell which window. Point at it and tell me again.";
    public static string NotInFrontLine(bool korean) => korean ? "그 창을 앞으로 못 가져와서 안 눌렀어." : "I couldn't bring that window to the front, so I didn't press anything.";
    public static string FailedLine(bool korean) => korean ? "해 봤는데 안 됐어." : "I tried, but it didn't work.";

    // ---- the plainest wordings, with no language model
    static readonly Regex OpenWord = new(@"열어|열래|켜|띄워|실행|시작해|틀어|\b(open|start|launch|run)\b", RegexOptions.IgnoreCase);
    static readonly Regex AskTail = new(@"\s*(좀|한\s*번|한번|하나|다시)?\s*(해|해줘|해\s*줘|해줄래|해\s*줄래|해봐|해\s*봐|줘|줄래|볼래|봐|주세요|please)?\s*[.!?~]*\s*$", RegexOptions.IgnoreCase);

    /// <summary>
    /// The obvious ones: "새로고침 해 줘", "메모장 열어 줘", "유튜브에서 고양이 영상 틀어 줘", "소리 좀 키워 줘", "전체 화면",
    /// "폴더 하나 만들어 줘", "refresh", "open notepad". null when the sentence is none of them (the language model
    /// knows the whole list). The name of the partner is taken out by the caller.
    /// </summary>
    public static (string @do, string arg)? Plain(string text)
    {
        string t = Regex.Replace((text ?? "").Trim(), @"\s+", " ");
        if (t.Length == 0 || t.Length > 80) return null;
        string low = t.ToLowerInvariant();

        if (Regex.IsMatch(low, @"새로\s*고침|리프레시|\brefresh\b|\breload\b")) return ("refresh", "");
        if (Regex.IsMatch(low, @"전체\s*화면|풀\s*스크린|\bfull\s*screen\b")) return ("fullscreen", "");
        if (Regex.IsMatch(low, @"음\s*소거|\bmute\b|\bunmute\b")) return ("mute", "");
        if (Regex.IsMatch(low, @"(소리|볼륨|음량)\s*(을|를|좀)?\s*(좀\s*)?(키워|올려|크게|높여)|\b(volume up|turn it up|louder)\b")) return ("volumeUp", "");
        if (Regex.IsMatch(low, @"(소리|볼륨|음량)\s*(을|를|좀)?\s*(좀\s*)?(줄여|내려|작게|낮춰)|\b(volume down|turn it down|quieter)\b")) return ("volumeDown", "");
        if (Regex.IsMatch(low, @"새\s*탭|\bnew tab\b")) return ("newTab", "");
        if (Regex.IsMatch(low, @"탭\s*(을|를|좀)?\s*닫|\bclose (the |this )?tab\b")) return ("closeTab", "");
        if (Regex.IsMatch(low, @"(스크롤|페이지)\s*(을|를|좀)?\s*(좀\s*)?(아래로\s*)?내려|아래로\s*(좀\s*)?(내려|스크롤)|\bscroll down\b|\bpage down\b")) return ("scrollDown", "");
        if (Regex.IsMatch(low, @"(스크롤|페이지)\s*(을|를|좀)?\s*(좀\s*)?(위로\s*)?올려|위로\s*(좀\s*)?(올려|스크롤)|\bscroll up\b|\bpage up\b")) return ("scrollUp", "");
        if (Regex.IsMatch(low, @"다음\s*(영상|동영상|곡|노래|트랙)|\bnext (video|track|song)\b")) return ("nextTrack", "");
        if (Regex.IsMatch(low, @"이전\s*(영상|동영상|곡|노래|트랙)|\b(previous|last) (video|track|song)\b")) return ("prevTrack", "");
        if (Regex.IsMatch(low, @"(영상|동영상|비디오|유튜브)\s*(을|를|좀)?\s*(좀\s*)?(일시\s*정지|멈춰|정지|다시\s*재생|재생)|\b(pause|resume) (the )?video\b")) return ("playPause", "");
        if (Regex.IsMatch(low, @"^(뒤로|이전\s*페이지로?)\s*(가|가기|가줘|가\s*줘|가봐|돌아가)|\bgo back\b")) return ("back", "");
        if (Regex.IsMatch(low, @"^앞으로\s*(가|가기|가줘|가\s*줘)\s*[.!?]*$|\bgo forward\b")) return ("forward", "");

        // YouTube: "유튜브에서 X 틀어 줘 / 찾아 줘 / 검색해 줘", "유튜브 열어 줘", "play X on youtube"
        var yt = Regex.Match(t, @"(?:유튜브|유투브|youtube)\s*(?:에서|에|로|으로)?\s*(.*?)\s*(?:을|를)?\s*(?:좀\s*)?(?:틀어|찾아|검색|보여|재생|열어|켜)", RegexOptions.IgnoreCase);
        if (yt.Success)
        {
            string q = Regex.Replace(yt.Groups[1].Value, @"^(들어가서|들어가|가서)\s*", "").Trim();
            q = Regex.Replace(q, @"\s*(좀|하나|한\s*번)$", "").Trim();
            return ("youtube", q);
        }
        var ytEn = Regex.Match(t, @"^(?:play|find|search(?: for)?|put on|show me)\s+(.+?)\s+on\s+youtube\W*$|^(?:open|start)\s+youtube\W*$", RegexOptions.IgnoreCase);
        if (ytEn.Success) return ("youtube", ytEn.Groups[1].Value.Trim());

        // a search: "X 검색해 줘", "구글에서 X 찾아 줘", "search for X"
        var search = Regex.Match(t, @"^(?:구글|네이버|인터넷|웹)\s*(?:에서|에|로)\s*(.+?)\s*(?:을|를)?\s*(?:좀\s*)?(?:검색|찾아)", RegexOptions.IgnoreCase);
        if (!search.Success) search = Regex.Match(t, @"^(.+?)\s*(?:을|를)?\s*(?:좀\s*)?(?:인터넷에서\s*|구글에서\s*|웹에서\s*)?검색\s*(?:해|좀)", RegexOptions.IgnoreCase);
        if (!search.Success) search = Regex.Match(t, @"^(?:search(?: the web)? for|google)\s+(.+?)\W*$", RegexOptions.IgnoreCase);
        if (search.Success && search.Groups[1].Value.Trim().Length > 0) return ("webSearch", search.Groups[1].Value.Trim());

        // a folder: "폴더 하나 만들어 줘", "여행 사진 폴더 만들어 줘", "make a folder called X"
        var folder = Regex.Match(t, @"^(?:바탕\s*화면에\s*)?(?:(.+?)\s*(?:라는|이라는|란|이란)?\s*)?(?:새\s*)?폴더\s*(?:를|을)?\s*(?:하나\s*|좀\s*)?(?:만들|생성)", RegexOptions.IgnoreCase);
        if (folder.Success) return ("newFolder", Regex.Replace(folder.Groups[1].Value, @"^(새|바탕\s*화면에)\s*|\s*(이름의|이름으로)$", "").Trim());
        var folderEn = Regex.Match(t, @"^(?:make|create|new)\s+(?:me\s+)?(?:a\s+)?(?:new\s+)?folder(?:\s+(?:called|named)\s+(.+?))?\W*$", RegexOptions.IgnoreCase);
        if (folderEn.Success) return ("newFolder", folderEn.Groups[1].Value.Trim());

        // a program of the allow-list with a word of opening: "메모장 열어 줄래?", "open notepad"
        if (OpenWord.IsMatch(low))
        {
            string what = AskTail.Replace(OpenWord.Replace(t, " "), "");
            what = Regex.Replace(what, @"\s*(을|를|이|가|좀|한\s*번|한번|줘|줄래|볼래|봐|해)\s*$|^(please|can you|could you)\s+|\s+(for me|please)$", "", RegexOptions.IgnoreCase).Trim(' ', '.', '?', '!', ',');
            what = Regex.Replace(what, @"\s*(을|를|이|가|좀)$", "").Trim();
            if (what.Length > 0 && what.Length <= 20 && FindApp(what) is { } app) return ("openApp", app.id);
        }
        return null;
    }

    /// <summary>The list for the language model's prompt.</summary>
    public static string PromptSection()
    {
        var sb = new StringBuilder();
        sb.AppendLine("pcDo = you DO something on the user's computer yourself, from a fixed list of quick actions - you CAN: never say you cannot refresh a page, open Notepad, make a folder or put the video site on. The field do names what, the field arg carries its words (\"\" when it needs none). For every other action do and arg are \"\". The host does it and says what really happened, so keep reply to a few words (\"응, 해 볼게.\"). ONLY when the user asks for it to be DONE: a site or a program named inside a question (\"여기 유튜브 로고 보여?\") is not a wish to open it.");
        sb.AppendLine("- do \"openApp\": arg = one of " + string.Join(" | ", Apps.Select(a => a.id)) + " (\"메모장 열어 줄래?\" -> notepad; \"윈도우 계산기 켜 줘\" -> calculator). No other program can be started.");
        sb.AppendLine("- do \"openUrl\": arg = a web address (http / https). do \"webSearch\": arg = what to look for (\"밤비 사슴 검색해 줘\"). do \"youtube\": arg = what to look for on YouTube (\"유튜브에서 로파이 음악 틀어 줘\" -> \"로파이 음악\"; \"다른 영상 틀어 줘\" with nothing named -> \"\"): the results page opens - do not say a video is playing.");
        sb.AppendLine("- keys to the window the user points at, or the display they brought into the room, or the window they last used: do \"refresh\" (\"새로고침 해 줘\", \"이 페이지 다시 불러와\"), \"back\", \"forward\", \"scrollDown\", \"scrollUp\", \"fullscreen\", \"newTab\", \"closeTab\".");
        sb.AppendLine("- media keys: do \"playPause\" (\"영상 멈춰 줘\", \"다시 재생해\"), \"nextTrack\", \"prevTrack\", \"volumeUp\", \"volumeDown\", \"mute\".");
        sb.AppendLine("- do \"newFolder\": arg = the folder's name (\"\" when none was said): a new folder on the Desktop. do \"newNote\": arg = the text to write down (\"이거 메모해 줘: 내일 3시 회의\" -> \"내일 3시 회의\"): a new text file on the Desktop, opened in Notepad. do \"typeText\": arg = the text - ONLY when the user plainly dictates text to be typed into the window in front (\"받아 적어: ...\", \"type this: ...\").");
        sb.AppendLine("  These are the QUICK actions: no other programs, no commands. Anything else on the computer that can be done by looking at the screen and clicking or typing in it is pcOperate (below). A calculator, a timer or a game that is in YOUR bag is playGame (the user's own module) - pcDo openApp only when they mean the computer's own program (\"윈도우 계산기\", \"PC 메모장\") or nothing like it is in your bag.");
        return sb.ToString();
    }
}

/// <summary>The little of Windows the PC actions need.</summary>
public interface IPcShell
{
    /// <summary>Starts a program of the allow-list, a settings address, an http(s) address or - PcRules.DefaultBrowser - the default browser by itself (argument: one file, or "").</summary>
    bool Start(string target, string argument);
    /// <summary>Brings a window to the front. true only when it IS the foreground window afterwards.</summary>
    bool Foreground(IntPtr hwnd);
    /// <summary>Presses a key (with others held) the given number of times, to whatever is in front.</summary>
    bool Press(ushort[] hold, ushort key, int times);
    /// <summary>Types a text into whatever is in front.</summary>
    bool Type(string text);
    string Desktop { get; }
    bool Exists(string path);
    void CreateFolder(string path);
    /// <summary>Writes a NEW file; fails when one is there.</summary>
    void WriteNew(string path, string text);
    string Title(IntPtr hwnd);
}

/// <summary>What became of a PC action: done or not, the line the partner says, and for the log what it was done to.</summary>
public sealed class PcOutcome
{
    public bool ok; public string line = "", detail = "";
}

public static class PcRun
{
    /// <summary>
    /// Carries a plan out. window: the window a key or a typed text is meant for (zero = none could be worked out).
    /// Returns what really happened - never more than that.
    /// </summary>
    public static PcOutcome Execute(PcPlan p, IPcShell shell, IntPtr window, bool korean)
    {
        var o = new PcOutcome();
        if (p == null || !p.Ok) { o.line = PcRules.ProblemLine(p ?? new PcPlan { problem = "unknown" }, korean); o.detail = "refused: " + (p?.problem ?? "unknown"); return o; }
        try
        {
            switch (p.kind)
            {
                case PcPlan.Start:
                    o.ok = shell.Start(p.target, p.args);
                    o.detail = "start " + (p.@do == "openApp" ? p.text : p.@do);
                    o.line = o.ok ? PcRules.DoneLine(p, korean) : PcRules.FailedLine(korean);
                    break;
                case PcPlan.Keys:
                case PcPlan.Type:
                {
                    string title = "";
                    if (p.window)
                    {
                        if (window == IntPtr.Zero) { o.line = PcRules.NoWindowLine(korean); o.detail = "no window is meant"; break; }
                        title = shell.Title(window);
                        if (!shell.Foreground(window)) { o.line = PcRules.NotInFrontLine(korean); o.detail = "the window could not be brought to the front"; break; }
                    }
                    o.ok = p.kind == PcPlan.Type ? shell.Type(p.text) : shell.Press(p.hold, p.key, p.times);
                    o.detail = (p.kind == PcPlan.Type ? $"typed {p.text.Length} characters" : p.@do) + (p.window ? " -> a window" + (title.Length > 0 ? $" ({title.Length} characters in its title)" : "") : " (media key)");
                    o.line = o.ok ? PcRules.DoneLine(p, korean, "", title) : PcRules.FailedLine(korean);
                    break;
                }
                case PcPlan.Folder:
                {
                    string desk = shell.Desktop;
                    string name = PcRules.FreeName(p.text, "", n => shell.Exists(Path.Combine(desk, n)));
                    shell.CreateFolder(Path.Combine(desk, name));
                    o.ok = true; o.detail = "a new folder on the Desktop"; o.line = PcRules.DoneLine(p, korean, name);
                    break;
                }
                case PcPlan.Note:
                {
                    string desk = shell.Desktop;
                    string file = PcRules.FreeName(p.en, ".txt", n => shell.Exists(Path.Combine(desk, n)));
                    string path = Path.Combine(desk, file);
                    shell.WriteNew(path, p.text);
                    bool opened = shell.Start("notepad.exe", path);
                    o.ok = true; o.detail = $"a new note on the Desktop ({p.text.Length} characters){(opened ? ", opened in Notepad" : ", Notepad did not start")}";
                    o.line = PcRules.DoneLine(p, korean, Path.GetFileNameWithoutExtension(file));
                    break;
                }
                default: o.line = PcRules.ProblemLine(new PcPlan { problem = "unknown" }, korean); o.detail = "nothing to do"; break;
            }
        }
        catch (Exception e) { o.ok = false; o.line = PcRules.FailedLine(korean); o.detail = "failed: " + e.GetType().Name + " " + WorldRules.Short(e.Message, 80); }
        return o;
    }
}
