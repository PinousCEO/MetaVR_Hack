using System.Text;
using System.Text.RegularExpressions;
using LivingPartner.Shared;

namespace LivingPartner.Host;

// Plain rules of the conversation - nothing of the host in this file, so that the tests hold them to the sentences
// the user really said (the host log of 2026-11-17, 23:30 - 23:45):
//   NameRules   is the partner being spoken to?            "밤미야, 유튜브로 들어가서 ..." was "not said to me"
//   CloseRules  "이 MR 환경 다시 지워 줘", "화면 닫아 줘"       which of the things that are open is put away
//   PlayRules   make or play?                               "계산기를 틀어볼래?" made a SECOND calculator

/// <summary>
/// Is the partner's name said? The speech recogniser writes "밤비야" in many ways - 밤미야, 반비야, 밤비아, 뱀비야,
/// 밤삐야, Bamby, Banbi ... The known spellings are a pattern (anywhere in the sentence); on top of that the FIRST
/// word of a sentence counts when it is one sound away from the call "밤비야" (one jamo: 밤미야 yes, 밤에야 no) or one
/// letter away from Bambi / Bamby / Bambie.
/// </summary>
public static class NameRules
{
    // "밤비야" as the transcription writes it: 밤비야, 밤 비야, 방비야, 반비야, 뱀비, 밤삐 ... and Bambi / Bamby.
    // The partner used to be called 밤별 (Bambyeol) and its owner still says both, so that name is the partner as well:
    // 밤별, 밤별아, 밤별이, 밤벌아, 밤볕아, 밤벼리 - and, only with the calling ending on it (they are words of their own
    // otherwise: 방별 = per room, 밤 별 = the stars at night), 방별아, 반별이, 박별아, 발별아, 밤 별아.
    public static readonly Regex NameRx = new(
        @"((밤|방|반|뱀|범|빰|밥)\s?(비|삐|피)|밤(별|뼐|볕|벌)(아|이|야)?|밤벼리|밤\s(별|벌)(아|야)|(방|반|빰|박|발)\s?(별|뼐|볕|벌)(아|이|야)|bam\s?-?\s?b(i|y|ie|ee)\b|bambi|bam\s?-?\s?[bp]?y(eo|u|o)l\b)\s*(야|아|얌|양)?",
        RegexOptions.IgnoreCase);

    /// <summary>A sound that says nothing after the name ("밤비야 저기", "밤비야 있어?"): still only a call.</summary>
    static readonly string[] JustCalling = { "저기", "어이", "있어", "있니", "있냐", "여기", "헤이", "이봐", "얘야", "응응", "hi", "yo", "oi", "hm", "um", "uh", "oh", "ah" };

    // the calls a first word is measured against (in jamo), and the English spellings (in letters)
    static readonly string[] Calls = { "밤비야", "밤비아" };
    static readonly string[] English = { "bambi", "bamby", "bambie" };
    // real words that are one letter from those: never the name
    static readonly HashSet<string> NotTheName = new(StringComparer.Ordinal) { "barbie", "zombie", "zombi", "mamba", "samba", "rambo", "combi", "bimbo", "jumbo", "bambu", "bamboo", "gambit", "bandit", "bambino", "iambi" };

    /// <summary>A Hangul syllable as its sounds: initial, vowel, final - each a letter of its own (an initial ㅁ is not a final ㅁ).</summary>
    public static string Jamo(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s ?? "")
        {
            if (ch < 0xAC00 || ch > 0xD7A3) { sb.Append(char.ToLowerInvariant(ch)); continue; }
            int i = ch - 0xAC00, final = i % 28;
            sb.Append((char)(0x1100 + i / 588)).Append((char)(0x1161 + i % 588 / 28));
            if (final > 0) sb.Append((char)(0x11A7 + final));
        }
        return sb.ToString();
    }

    /// <summary>The edit distance of two strings, counted up to `max` (anything further is max + 1).</summary>
    public static int Distance(string a, string b, int max = 2)
    {
        a ??= ""; b ??= "";
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return Math.Min(prev[b.Length], max + 1);
    }

    /// <summary>
    /// Is this FIRST word of a sentence the call of the name, misheard by one sound? Korean: three syllables, one jamo
    /// from 밤비야 / 밤비아 (밤미야, 반비야, 뱀비야, 밤삐야, 밤이야 - not 밤에야, 방이야, 밥이야). English: one letter from
    /// bambi / bamby / bambie (banbi, bambee - not barbie, zombie). The bare "밤비" is the pattern's (a first word
    /// "밤이" or "방비" is a word of its own).
    /// </summary>
    public static bool NearCall(string word)
    {
        string w = (word ?? "").Trim().ToLowerInvariant();
        if (w.Length == 0) return false;
        if (w.All(ch => ch >= 0xAC00 && ch <= 0xD7A3))
        {
            if (w.Length != 3) return false;
            string j = Jamo(w);
            return Calls.Any(c => Distance(j, Jamo(c), 1) <= 1);
        }
        if (w.All(ch => ch >= 'a' && ch <= 'z'))
        {
            if (w.Length < 4 || w.Length > 7 || NotTheName.Contains(w)) return false;
            return English.Any(e => Distance(w, e, 1) <= 1);
        }
        return false;
    }

    static string Letters(string text) => new string((text ?? "").Where(ch => !char.IsPunctuation(ch) && !char.IsSymbol(ch)).ToArray()).Trim();

    /// <summary>Where the name stands in the sentence (punctuation taken out first): start and length, or (-1, 0).</summary>
    static (int at, int length) Find(string t)
    {
        var m = NameRx.Match(t);
        // (the pattern's "밥비" / "방비" inside a longer word - 밥비용, 방비를 - is not looked at more closely here: it never was)
        int space = t.IndexOf(' ');
        string first = space < 0 ? t : t[..space];
        if (NearCall(first) && !(m.Success && m.Index == 0 && m.Length >= first.Length)) return (0, first.Length);
        return m.Success ? (m.Index, m.Length) : (-1, 0);
    }

    /// <summary>Does this name the partner? only: nothing but the name (a call, "밤비야!").</summary>
    public static bool Named(string text, out bool only)
    {
        string t = Letters(text);
        only = false;
        var (at, length) = Find(t);
        if (at < 0) return false;
        string rest = (t[..at] + t[(at + length)..]).Replace(" ", "");
        // The name by itself - or with a sound that says nothing ("밤비야 저기", "밤비야 있어?"). Two syllables are NOT
        // too little to be an order: "밤비야, 나와", "밤비야, 그만", "밤비야, 춤 춰", "밤비야, 앉아" were all answered with
        // "응, 왜?" until 2026-11-17 because anything of two characters or fewer counted as just the name.
        only = rest.Length <= 1 || (rest.Length == 2 && Array.IndexOf(JustCalling, rest.ToLowerInvariant()) >= 0);
        return true;
    }

    /// <summary>The sentence without the partner's name (for the plain parsers, which must not trip over it).</summary>
    public static string Strip(string text)
    {
        string t = (text ?? "").TrimStart();
        var first = Regex.Match(t, @"^[\p{L}\p{Nd}]+");
        if (first.Success && NearCall(first.Value)) t = " " + t[first.Length..];
        return NameRx.Replace(t, " ");
    }
}

/// <summary>
/// "이 MR 환경 다시 지워 줘", "화면 닫아 줘", "디스플레이 치워", "보드 꺼 줘", "상점 닫아", "다 치워": something that is
/// open is put away. what: screen (the display that was brought into the room) | shop | menu | place (the
/// surroundings) | all. When the thing named is not open the next one is taken (Order).
/// </summary>
public static class CloseRules
{
    public const string Screen = "screen", Shop = "shop", Menu = "menu", Place = "place", All = "all";
    public static readonly string[] Whats = { Screen, Shop, Menu, Place, All };

    const string Shut = @"(닫아|닫어|닫자|닫을|지워|지우|치워|치우|없애|집어\s*넣|꺼(?!내)|끄(?!집)|close|shut|put\s+(\S+\s+){0,3}away|take\s+(\S+\s+){0,3}away|get\s+rid|remove|hide|dismiss|turn\s+(it\s+|that\s+|this\s+)?off)";
    static readonly Regex ShutRx = new(Shut, RegexOptions.IgnoreCase);
    static readonly Regex AllRx = new(@"(다|전부|모두|싹|죄다)\s*(닫아|닫어|지워|치워|없애|꺼)|\b(close|hide|remove|put away)\s+(everything|all)\b|\beverything\s+(away|off)\b", RegexOptions.IgnoreCase);
    static readonly Regex ScreenRx = new(@"(mr|엠\s*알|엠아르|혼합\s*현실)\s*(환경|화면|공간)?|화면|디스플레이|모니터|스크린|보드|\b(screen|display|monitor|board)\b", RegexOptions.IgnoreCase);
    static readonly Regex ShopRx = new(@"상점|마켓|가게|\b(shop|market|store|marketplace)\b", RegexOptions.IgnoreCase);
    static readonly Regex MenuRx = new(@"메뉴|가방|\b(menu|bag)\b", RegexOptions.IgnoreCase);
    static readonly Regex PlaceRx = new(@"배경|풍경|주변|분위기|환경|바다|바닷가|해변|산\s*정상|노을|\b(surroundings|scenery|place|background|ambience|environment)\b", RegexOptions.IgnoreCase);
    // what is NOT a wish to put something away although it has such a word: the rain, the music, a timer, the game, a tab of the browser (PcRules), a file
    static readonly Regex Other = new(@"비\s*(가|를|좀)?\s*(그쳐|꺼|멈)|노래|음악|타이머|게임|탭|창\s*(을|를)?\s*닫|파일|폴더|소리|볼륨|\b(rain|music|song|timer|game|tab|window|file|folder|sound|volume)\b", RegexOptions.IgnoreCase);

    /// <summary>What a short sentence asks to be put away; null when it is no such wish.</summary>
    public static string Plain(string text)
    {
        string t = (text ?? "").Trim();
        if (t.Length == 0 || t.Length > 60 || !ShutRx.IsMatch(t) || Other.IsMatch(t)) return null;
        if (ShopRx.IsMatch(t)) return Shop;
        if (ScreenRx.IsMatch(t)) return Screen;        // ("MR 환경" is the display that was brought in, before "환경" = the surroundings)
        if (MenuRx.IsMatch(t)) return Menu;
        if (PlaceRx.IsMatch(t)) return Place;
        if (AllRx.IsMatch(t)) return All;
        return null;
    }

    /// <summary>The word of the model (or of lpctl) as one of the five; "" = whatever is open.</summary>
    public static string What(string word)
    {
        string w = (word ?? "").Trim().ToLowerInvariant();
        if (Array.IndexOf(Whats, w) >= 0) return w;
        if (w.Length == 0) return "";
        if (Regex.IsMatch(w, @"everything|모두|전부|^다$")) return All;
        if (ShopRx.IsMatch(w)) return Shop;
        if (ScreenRx.IsMatch(w)) return Screen;
        if (MenuRx.IsMatch(w)) return Menu;
        if (PlaceRx.IsMatch(w)) return Place;
        return "";
    }

    /// <summary>
    /// What is really put away. what: the thing named ("" = none in particular); the rest: what is open now
    /// (menu: the bag is the headset's, the host cannot see it - true when a headset is connected). The thing named
    /// when it is open; otherwise the first of screen, shop, menu, place that is. "all": everything that is open.
    /// </summary>
    public static List<string> Order(string what, bool screen, bool shop, bool menu, bool place)
    {
        var open = new List<string>();
        if (screen) open.Add(Screen); if (shop) open.Add(Shop); if (menu) open.Add(Menu); if (place) open.Add(Place);
        if (what == All) return open;
        if (open.Contains(what)) return new List<string> { what };
        return open.Count == 0 ? open : new List<string> { open[0] };
    }

    public static string Line(IReadOnlyList<string> closed, bool korean)
    {
        if (closed == null || closed.Count == 0) return korean ? "지금은 닫을 게 없어." : "There's nothing open to close right now.";
        if (closed.Count > 1) return korean ? "다 치웠어." : "I've put it all away.";
        return closed[0] switch
        {
            Screen => korean ? "가져온 화면 치웠어." : "I've put the screen away.",
            Shop => korean ? "상점 닫았어." : "I've closed the shop.",
            Menu => korean ? "가방 닫았어." : "I've closed the bag.",
            _ => korean ? "방을 원래대로 돌려놨어." : "The room is back to how it was.",
        };
    }
}

/// <summary>
/// MAKE or PLAY? "계산기를 틀어볼래?" made a second calculator, and "방금 만든 거 틀어봐" did nothing. The rules:
/// something is MADE only when a make-word is in the sentence (만들어 / 만들자 / 제작 / make / build / create - or the
/// user gives it: "책상 하나 줄게"); 틀어 / 켜 / 열어 / 실행 / 시작 / 하자 / play / open / start with a name that fits something
/// in the bag plays or uses THAT; "방금 만든 거" is what was made last.
/// </summary>
public static class PlayRules
{
    static readonly Regex MakeRx = new(@"만들|만든다|제작|짜\s*줘|짜\s*봐|추가|가르쳐|배워|줄게|줄께|줄\s*게|선물|\b(make|makes|making|build|create|craft|invent|design|write|add|teach|learn|give\s+you|for\s+you|here'?s|here\s+is)\b", RegexOptions.IgnoreCase);
    static readonly Regex PlayRx = new(@"틀어|틀자|틀래|켜|열어|열자|실행|시작|하자|해\s*보자|할래|해\s*볼래|플레이|꺼내|\b(play|open|start|run|launch|use|let'?s)\b", RegexOptions.IgnoreCase);
    static readonly Regex JustMadeRx = new(@"(방금|아까|금방|조금\s*전에?|좀\s*전에?|지금|마지막에?)\s*(네가\s*|니가\s*|우리가\s*)?(만든|만들었던|만들어\s*준|만들어\s*놓은)|\b(just|last)\s+(made|built|created)\b|\bthe\s+(one|thing|game)\s+(you|we|i)\s+(just\s+)?(made|built)\b", RegexOptions.IgnoreCase);
    static readonly Regex AgainRx = new(@"^(응\s*|그래\s*|그럼\s*)?(새로|새\s*걸로|새\s*거로|다시)\s*(하나\s*)?(만들어|만들자|만들래|해)?\s*(줘|봐|줄래)?$|^(a\s+)?new(\s+one)?(\s+please)?$|^(make|build)\s+(it\s+again|a\s+new\s+one|another(\s+one)?)$|^again$|^remake(\s+it)?$", RegexOptions.IgnoreCase);
    static readonly Regex ThatRx = new(@"^(응\s*|그래\s*|그럼\s*)?(그거|그걸|그걸로|그것|있는\s*거|있는\s*걸|그냥)?\s*(틀어|틀자|켜|열어|실행해|시작해|하자|해\s*보자|할래|쓸래|쓰자)\s*(줘|봐|줄래)?$|^(그거|그걸|그걸로|있는\s*거|있는\s*걸로?)$|^(play|open|start|use)\s+(it|that|that\s+one|the\s+old\s+one)$|^that(\s+one)?$", RegexOptions.IgnoreCase);
    static readonly Regex OnPcRx = new(@"윈도우|컴퓨터|컴터|피씨|\b(pc|windows|computer)\b", RegexOptions.IgnoreCase);

    /// <summary>Is there a word of MAKING (or of giving) in the sentence?</summary>
    public static bool MakeWord(string text) => MakeRx.IsMatch(text ?? "");
    /// <summary>Is there a word of playing, opening, switching on?</summary>
    public static bool PlayWord(string text) => PlayRx.IsMatch(text ?? "");
    /// <summary>"방금 만든 거", "아까 만든 게임", "that one you just made".</summary>
    public static bool JustMade(string text) => JustMadeRx.IsMatch(text ?? "");
    /// <summary>The sentence says that the PC's own program is meant ("윈도우 계산기 열어 줘").</summary>
    public static bool OnPc(string text) => OnPcRx.IsMatch(text ?? "");

    /// <summary>The answer to "이미 있어. 새로 만들까, 그걸 틀까?": "new" | "play" | "" (not plainly an answer).</summary>
    public static string DuplicateAnswer(string said)
    {
        string t = Regex.Replace((said ?? "").Trim(), @"[\p{P}\p{S}]+", " ").Trim();
        if (t.Length == 0 || t.Length > 24) return "";
        if (AgainRx.IsMatch(t)) return "new";
        if (ThatRx.IsMatch(t)) return "play";
        return "";
    }

    static string Flat(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>The thing in the bag (or on the desk) that a sentence names: its whole name is in the sentence; the longest name wins. null = none.</summary>
    public static ItemDef ThingNamed(IEnumerable<ItemDef> items, params string[] sources)
    {
        foreach (string src in sources)
        {
            string f = Flat(src);
            if (f.Length < 2) continue;
            var hit = (items ?? Enumerable.Empty<ItemDef>()).Where(i => i != null && i.status != "failed" && Flat(i.name).Length >= 2 && f.Contains(Flat(i.name))).OrderByDescending(i => Flat(i.name).Length).FirstOrDefault();
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>
    /// The user's OWN game that a wish to make one would make a second time: "계산기 만들어 줘" when '손 계산기' is
    /// theirs. The title has to be named - whole, or by the word it mostly consists of (GlassRules.PickGame, steps 1
    /// and 2); the kind of game alone ("게임 하나 만들어 줘") names nothing. null = nothing like it is there.
    /// </summary>
    public static GamePackDef OwnGameNamed(IEnumerable<GamePackDef> own, string words)
    {
        var list = (own ?? Enumerable.Empty<GamePackDef>()).Where(g => g != null && !string.IsNullOrEmpty(g.id) && !string.IsNullOrWhiteSpace(g.title)).ToList();
        if (list.Count == 0 || string.IsNullOrWhiteSpace(words)) return null;
        // (the words of making are not names: "만들어" must not fit a title, and neither must "게임")
        string asked = Regex.Replace(words, @"만들어\s*(줘|봐|줄래|볼래|주세요)?|만들자|만들래|만들\s*수\s*있어|제작해\s*(줘|봐)?|하나|새로운?|\b(make|build|create|me|a|an|new|please|can|you)\b", " ", RegexOptions.IgnoreCase);
        // The partner's own name names no game: on 2026-11-18 "밤비랑 캐치볼 하는 게임 만들어 줘" was answered with "밤비 슈팅은
        // 이미 있어" - both have "밤비" in them, and nothing was made. The name is taken out of the wish AND out of the
        // titles before they are compared ('밤비 슈팅' is asked for again by "슈팅", not by "밤비").
        asked = PartnerRx.Replace(asked, " ");
        list = list.Select(g => new GamePackDef { id = g.id, title = PartnerRx.Replace(g.title, " ").Trim(), template = g.template }).Where(g => Flat(g.title).Length >= 2).ToList();
        if (list.Count == 0) return null;
        var pick = GlassRules.PickGame(list, "", "", asked);
        if (!pick.Found || pick.how.StartsWith("the way of playing") || pick.how.StartsWith("the game ") || pick.how.StartsWith("the model")) return null;
        string id = pick.packId;
        return (own ?? Enumerable.Empty<GamePackDef>()).FirstOrDefault(g => g != null && g.id == id);
    }

    static readonly Regex PartnerRx = new(@"밤비|밤별|\bbambi\b", RegexOptions.IgnoreCase);
}

public static class ScreenReading
{
    public static bool Requested(string words)
    {
        string t = Regex.Replace((words ?? "").ToLowerInvariant(), @"\s+", "");
        if (!t.Contains("읽") && !Regex.IsMatch(words ?? "", @"\bread\b", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(t, "파일|첨부|문서|file|document|attachment|\\.pdf")) return false;
        return Regex.IsMatch(t, "화면|카카오톡|카톡|채팅|대화창|메시지|메세지|screen|kakaotalk|chat|messages?");
    }
}
