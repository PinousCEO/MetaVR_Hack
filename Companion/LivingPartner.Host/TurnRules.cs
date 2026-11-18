using System.Text;
using System.Text.RegularExpressions;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

// UNDERSTANDING, the rules (round 4, 2026-11-18). Nothing of the host in this file: the tests hold it to the sentences
// of the host log of that morning.
//   HearRules   what the speech recogniser is told, and which transcripts are NOT something the user said
//               (it gave its own prompt back, word for word, when the sound was unclear - and the partner went into the screen)
//   TurnRules   who decides: the language model, given where the partner is and what is open. Rules act by themselves
//               only for a handful of exact short orders (stop / come out / quiet / cancel); two guards hold the model
//               to its word (a question is never a move; a promise without an action is asked again)
//   EnterRules  where the partner goes into a screen: the spot pointed at on purpose, else where the user looks

/// <summary>What the speech recogniser is told, and which of its transcripts are dropped.</summary>
public static class HearRules
{
    /// <summary>
    /// The hint for the recogniser: the partner's name and nothing else. NO example sentences: with unclear sound the
    /// recogniser returned them as the transcript ("밤비야, 이쪽으로 들어가 봐. 밤비야, 이 화면이 어떤 화면이야? ..." at
    /// 10:57:08 and 11:04:28 on 2026-11-18) and the partner did what the user never said. Worded so that an echo of it
    /// is told from a real call of the name (Drop).
    /// </summary>
    public const string Prompt = "등장하는 이름: 밤비 (Bambi).";

    // the sentences the prompt carried until 2026-11-18 (a headset build may still be answered by an older host's echo in a log; the tests use the real strings)
    static readonly string[] OldExamples =
    {
        "이쪽으로 들어가 봐", "이리 나와 봐", "이 화면이 어떤 화면이야", "이거 피드백 줘", "이 아이콘 네가 직접 수정해 볼래", "테니스 하자", "책상 하나 만들어 줘", "춤춰 봐", "노래 틀어 줘",
        "앞으로 집중 모드라고 하면 비 내리고 조용한 노래 틀어 줘", "밤비라는 작은 책상 친구에게 말하는 중입니다", "하고 부릅니다", "화자는 주로 한국어로 말하고 가끔 영어를 씁니다",
    };

    // what recognisers write over silence, music and noise: the credits of the videos they learned from
    static readonly Regex Credits = new(
        @"^\W*(" +
        @"(끝까지\s*)?시청해\s*주셔서\s*(대단히\s*)?감사합니다|시청\s*감사합니다|구독과\s*좋아요.*|좋아요와\s*구독.*|구독\s*(,|과|이랑)?\s*좋아요\s*(,|와|랑)?\s*알림\s*설정.*|다음\s*(영상|시간)에(서)?\s*(만나요|뵙겠습니다|봬요|만나겠습니다)|" +
        @"(한글\s*)?자막\s*(제공|제작|번역|:|by).*|.*뉴스\s*\S{2,5}입니다|이\s*영상은.*(제공|협찬|유료\s*광고).*|감사합니다|고맙습니다|" +
        @"thanks?(\s+you)?(\s+so\s+much|\s+very\s+much)?\s+for\s+watching.*|thank\s+you\s*|please\s+subscribe.*|(don'?t\s+forget\s+to\s+)?like\s+and\s+subscribe.*|subtitles?\s+(by|provided|made).*|.*amara\.org.*|" +
        @"transcri(bed|ption)\s+by.*|see\s+you\s+(in\s+the\s+)?next\s+(video|time|one).*|(https?://|www\.)\S+|\[?\(?(music|applause|silence|blank_audio|laughter|음악|박수|웃음)\)?\]?|[♪♫\s]+" +
        @")\W*$", RegexOptions.IgnoreCase);

    static string Flat(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Is this transcript something the recogniser made up? "" = no, it is kept; otherwise why it is dropped (for the
    /// log). Dropped: nothing but punctuation; the prompt given back (in part or whole); two or more of the old
    /// prompt's example sentences in one transcript; a row of three or more different sentences that each call the
    /// name; the credits recognisers write over silence ("시청해 주셔서 감사합니다", "Thanks for watching"); one word or
    /// one phrase over and over.
    /// </summary>
    public static string Drop(string text)
    {
        string t = (text ?? "").Trim();
        string flat = Flat(t);
        if (flat.Length == 0) return "nothing was said";
        string prompt = Flat(Prompt);
        if (flat.Contains(Flat("등장하는 이름")) || (flat.Length >= 6 && prompt.Contains(flat))) return "the recogniser gave its own hint back";
        int old = OldExamples.Count(e => flat.Contains(Flat(e)));
        if (old >= 2) return $"the recogniser gave the old hint's example sentences back ({old} of them)";
        // "밤비야, A. 밤비야, B. 밤비야, C.": nobody talks like that - a list of sentences that each call the name
        var sentences = Regex.Split(t, @"[.!?。]+\s*").Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        int calling = sentences.Count(s => NameRules.Named(s, out bool only) && !only);
        if (calling >= 3 && sentences.Select(Flat).Distinct().Count() >= 3) return $"a list of {calling} sentences that each call the name";
        if (Credits.IsMatch(t)) return "the credits a recogniser writes over silence";
        // one thing over and over ("네 네 네 네 네 네", "아니 아니 아니 ...") - but calling the name again and again is a call
        var words = t.Split(new[] { ' ', ',', '.', '!', '?', '~' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 6)
        {
            var top = words.GroupBy(w => w.ToLowerInvariant()).OrderByDescending(g => g.Count()).First();
            if (top.Count() >= words.Length * 0.75 && !(NameRules.Named(top.Key, out _))) return "one word over and over";
        }
        var parts = Regex.Split(t, @"[.!?,。]+\s*").Select(Flat).Where(s => s.Length >= 2).ToList();
        if (parts.Count >= 4)
        {
            var top = parts.GroupBy(p => p).OrderByDescending(g => g.Count()).First();
            if (top.Count() >= parts.Count * 0.75 && top.Key.Length >= 2 && !NameRules.Named(top.Key, out _)) return "one phrase over and over";
        }
        return "";
    }
}

/// <summary>What the language model is told about the moment: where the partner is, what is open, what the user points at.</summary>
public sealed class TurnContext
{
    public string name = "Bambi", personality = "";
    /// <summary>Where the partner is, in one clause: "on the desk in the room" | "inside the PC, walking freely on the desktop of display 2" | "inside the PC on display 1, bound to the window 'Cat videos - Browser'".</summary>
    public string where = "on the desk in the room";
    public bool insidePc;
    public string activity = "idle";
    /// <summary>What the user points or looks at, in one sentence ("" = nothing known).</summary>
    public string pointing = "";
    /// <summary>What is open or going on right now (a game, the document board, a display brought into the room, the work queue ...), one line each.</summary>
    public List<string> open = new();
    /// <summary>Timers, rain, quiet mode, the time: one line each.</summary>
    public List<string> state = new();
    /// <summary>The sections of the things that have their own actions (the computer, documents, the partner's world, the shop, abilities), as they are.</summary>
    public string sections = "";
    public List<string> notes = new();
    public string language = "Korean";
    /// <summary>"" = said to the partner | "afterOffer" = overheard a moment after its own question | "overheard".</summary>
    public string mode = "";
}

/// <summary>Who decides what a sentence means: the model, with these rules round it.</summary>
public static class TurnRules
{
    public static readonly string[] Actions =
    {
        "none", "enterScreen", "comeBack", "goDesktop", "lookAtScreen", "createTimer", "startTimer", "pauseTimer", "resumeTimer", "resetTimer", "setTimer",
        "rainOn", "rainOff", "quietOn", "quietOff", "cancel", "playTennis", "stopTennis", "readFile", "calibrate", "reviewScreen", "reviseScreen", "playGame", "comeHere",
        // the partner's world (Maker.cs): each of these carries its words in the field "thing"
        "makeItem", "teachSkill", "useSkill", "setAmbience", "playMusic", "doActivity", "storeItem", "placeItem",
        // things with a script: put one on / take it off, one of its acts (thing = the thing, act = on | off / the act's name)
        "wearThing", "actThing",
        // the marketplace, a display brought into the room, a tune saved as a file: these carry their words in "thing" as well
        "openMarket", "installModule", "removeModule", "publishModule", "makeModule", "bringScreen", "exportSong",
        "setPrice", "unpublishModule",
        // the answer to the partner's own "shall I put it up in the marketplace?" (thing: yes | yes free | no; price)
        "answerOffer",
        // the bag (the menu board in the headset), opened and closed by voice
        "openMenu", "closeMenu",
        // the computer: one of the fixed quick actions (fields "do" and "arg"), or - pcOperate - anything that needs SEEING the screen and clicking / typing in it (thing = the goal)
        "pcDo", "pcOperate", "closeThing",
        // documents made as real files: a researched report as a PDF, design options as PNG + PDF
        "makeReport", "makeDesigns", "showDoc", "docPage", "pickOption", "deliverDoc", "closeDoc",
        // (C1) a real object the user holds up is looked at and sculpted; one of the partner's things is carried into the PC as a 3D file (SightRules.cs: Make, Export - each carries its words in "thing")
        "makeFromSight", "exportThing",
    };

    /// <summary>The games the model may name in the field "game" (GlassRules.PickGame knows the module of each).</summary>
    public static readonly string[] Games = { "", "tennis", "keeper", "swarm", "gomoku" };

    /// <summary>The shape of the model's answer. The action and its words come FIRST and the line it says LAST: the line is written knowing what was chosen.</summary>
    public static JObject Schema()
    {
        JObject S() => new() { ["type"] = "string" };
        return new JObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["required"] = new JArray("action", "thing", "do", "arg", "game", "act", "minutes", "start", "price", "mood", "reply"),
            ["properties"] = new JObject
            {
                ["action"] = new JObject { ["type"] = "string", ["enum"] = new JArray(Actions) },
                ["thing"] = S(),
                ["do"] = new JObject { ["type"] = "string", ["enum"] = new JArray(new[] { "" }.Concat(PcRules.Dos).Cast<object>().ToArray()) },
                ["arg"] = S(),
                ["game"] = new JObject { ["type"] = "string", ["enum"] = new JArray(Games) },
                ["act"] = S(),
                ["minutes"] = new JObject { ["type"] = "number" },
                ["start"] = new JObject { ["type"] = "boolean" },
                ["price"] = new JObject { ["type"] = "integer" },
                ["mood"] = new JObject { ["type"] = "string", ["enum"] = new JArray("neutral", "happy", "worried", "curious") },
                ["reply"] = S(),
            },
        };
    }

    /// <summary>The request to the chat model for one sentence (the host sends exactly this; so does the recorded check of the tests).</summary>
    public static JObject Request(string model, string system, IEnumerable<(string role, string text)> history, string user)
    {
        var messages = new JArray { new JObject { ["role"] = "system", ["content"] = system } };
        foreach (var h in history ?? Enumerable.Empty<(string role, string text)>()) messages.Add(new JObject { ["role"] = h.role, ["content"] = h.text });
        messages.Add(new JObject { ["role"] = "user", ["content"] = user });
        var body = new JObject
        {
            ["model"] = model, ["messages"] = messages,
            ["response_format"] = new JObject { ["type"] = "json_schema", ["json_schema"] = new JObject { ["name"] = "turn", ["strict"] = true, ["schema"] = Schema() } },
        };
        if (model.StartsWith("gpt-5")) { body["max_completion_tokens"] = 500; body["reasoning_effort"] = "none"; }
        else body["max_tokens"] = 400;
        return body;
    }

    // ------------------------------------------------------------------ the handful of exact orders
    static readonly Regex OutRx = new(
        @"^((일단|이제|이리|이쪽으로|밖으로|다시|얼른|빨리|좀)\s*){0,2}(나와|나와라|나와\s?봐|나와\s?줘|나와\s?볼래|나오렴|나오세요|돌아와|돌아와\s?줘)$|^(please\s+)?come\s+(back\s+)?out(\s+of\s+there|\s+here|\s+now)?(\s+please)?$|^come\s+back$|^get\s+out(\s+of\s+there)?$",
        RegexOptions.IgnoreCase);
    static readonly Regex QuietRx = new(@"^(좀\s*)?(조용|조용히|조용히\s*해|조용히\s*해\s*줘|조용히\s*좀\s*해|조용히\s*있어|쉿|조용\s*모드)$|^(be\s+)?quiet(\s+please|\s+mode)?$|^(shh+|hush)$", RegexOptions.IgnoreCase);
    // (only the order itself: "영상 잠깐 멈춰 줘" pauses a video and "노래 그만 틀고 춤 춰" is two wishes - both are the model's)
    static readonly Regex StopRx = new(@"^((게임|이제|좀|그만|다|당장)\s*)*(멈춰|멈춰\s?줘|멈춰\s?봐|멈추라고|그만|그만해|그만\s?해\s?줘|그만하자|그만해라|그만하라고|스톱)$|^(please\s+)?stop(\s+it|\s+that|\s+the\s+game|\s+now|\s+please)?$", RegexOptions.IgnoreCase);
    static readonly Regex CancelRx = new(@"^(취소|취소해|취소해\s*줘|cancel|cancel\s+(it|that))$", RegexOptions.IgnoreCase);

    /// <summary>
    /// The exact short orders that are carried out at once, with no model in between: stop | returnToMr | quietOn |
    /// cancel (an intent's kind) - or null: the model decides. The WHOLE sentence has to be the order ("나와", "일단
    /// 나와 봐", "그만", "게임 그만해", "쉿", "stop", "come out"): anything more is a sentence to be understood.
    /// </summary>
    public static string ExactOrder(string text)
    {
        string t = Regex.Replace(NameRules.Strip(text ?? ""), @"[\p{P}\p{S}]+", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length == 0 || t.Length > 16) return null;
        if (OutRx.IsMatch(t)) return "returnToMr";
        if (QuietRx.IsMatch(t)) return "quietOn";
        if (CancelRx.IsMatch(t)) return "cancel";
        if (StopRx.IsMatch(t) && IntentParser.Parse(t).kind == "stop") return "stop";
        return null;
    }

    // ------------------------------------------------------------------ the guards
    static readonly Regex AskRx = new(
        @"\?\s*$|뭐|뭘|뭔|무슨|무엇|어떤|어느|누구|몇\s|보여\s*\?|보이(니|냐|나|지|는지|냐고)|있어\s*\?|있니|있냐|있나|있는지|알려|읽어|\b(what|which|who|where|why|how\s+many|do\s+you\s+see|can\s+you\s+see|is\s+there|are\s+there|tell\s+me|read)\b",
        RegexOptions.IgnoreCase);
    static readonly Regex MoveRx = new(
        @"들어가|들어와|들어\s가|들어\s와|나와|나오|나가|돌아와|이리\s?와|일로\s?와|(으로|로|에)\s*(좀\s*)?(와|가|가\s?있어|와\s?있어|와\s?줘|가\s?줘|와\s?봐|가\s?봐|오렴|올래|갈래)\s*[.!?~]*$|\b(go\s+in|go\s+into|get\s+in|enter|jump\s+in|come\s+out|come\s+back|come\s+here|come\s+over|come\s+to|go\s+to|go\s+back)\b",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// A question ABOUT something ("화면 안에 뭐가 있어?", "여기 유튜브 로고 보여?", "what is on this screen?") with no
    /// order to go anywhere in it: such a sentence is never a MOVE (going into the screen, coming out, the desktop).
    /// </summary>
    public static bool AsksNotMoves(string text)
    {
        string t = NameRules.Strip(text ?? "").Trim();
        return t.Length > 0 && t.Length <= 160 && AskRx.IsMatch(t) && !MoveRx.IsMatch(t);
    }

    /// <summary>The actions that move the partner.</summary>
    public static bool IsMove(string action) => action is "enterScreen" or "comeBack" or "goDesktop" or "comeHere";

    // The offline host has no model to distinguish a destination from a question about a file.
    static readonly Regex DesktopOrderRx = new(
        @"^(?:바탕\s*화면\s*(?:으로|에)\s*(?:좀\s*)?(?:나와|와|가)(?:\s*(?:봐|줘|주세요))?|(?:please\s+)?(?:come|go)(?:\s+(?:out|back))?\s+(?:on)?to\s+(?:the\s+)?desktop)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool PlainDesktopOrder(string bare) => DesktopOrderRx.IsMatch(bare);

    static readonly Regex PromiseRx = new(
        @"(할게|갈게|볼게|줄게|틀게|열게|닫을게|만들게|찾을게|켤게|끌게|올게|누를게|바꿀게|옮길게|가져올게|할께|갈께|볼께|줄께|해\s?줄게|해\s?볼게)(요)?(?=[\s,.!~]|$)|(하는|가는|보는|트는|여는|만드는|찾는)\s*중|\b(i'?ll|i\s+will|i'?m\s+going\s+to|i\s+am\s+going\s+to|let\s+me|on\s+it|right\s+away|coming|going\s+in)\b",
        RegexOptions.IgnoreCase);
    // what is promised without anything to do: waiting, staying, remembering, being quiet
    static readonly Regex HarmlessRx = new(@"기다릴게|있을게|기억할게|조심할게|노력할게|생각해\s?볼게|말해\s?줄게|알려\s?줄게|들을게|\b(i'?ll|i\s+will)\s+(wait|stay|remember|be\s+(here|quiet|careful)|try|think|tell\s+you|listen)\b", RegexOptions.IgnoreCase);

    /// <summary>Does this line say that the partner WILL do (or is doing) something? With the action "none" that is a promise nothing carries out.</summary>
    public static bool Promises(string reply)
    {
        string t = (reply ?? "").Trim();
        return t.Length > 0 && PromiseRx.IsMatch(t) && !HarmlessRx.IsMatch(t);
    }

    /// <summary>What the model is told when it promised something and chose no action: once.</summary>
    public const string PromiseNote =
        " NOTE: a moment ago you answered this very message with action \"none\" and a line that says you WILL do something - then nothing happens, which the user hates. " +
        "Answer it again: EITHER choose the action that really does it (moving: enterScreen / comeBack / goDesktop / comeHere; on the computer: pcDo or pcOperate; looking: lookAtScreen ...) " +
        "OR keep \"none\" and say honestly, in one short sentence, that you cannot do that. Never promise with \"none\".";

    // ------------------------------------------------------------------ the prompt
    /// <summary>
    /// Where the partner is, as the model is told. insidePc false: the room. Inside: bound = it lives in a window
    /// (title; minimised: the user put that window away and the partner is hidden with it), else it walks the desktop.
    /// display: "display 2 (the main one)"; "" = not known.
    /// </summary>
    public static string Where(bool insidePc, bool bound, string display, string title, bool minimised)
    {
        if (!insidePc) return "on the desk in the room (not inside the PC)";
        string on = string.IsNullOrEmpty(display) ? "" : " on " + display;
        if (!bound) return string.IsNullOrEmpty(display) ? "inside the PC screen" : $"inside the PC, walking freely on the desktop of {display} (bound to no window)";
        return $"inside the PC{on}, bound to the window{(string.IsNullOrEmpty(title) ? "" : $" \"{title}\"")}" +
               (minimised ? " - which the user has MINIMISED, so you are hidden with it (goDesktop brings you out onto the desktop)" : " (goDesktop takes you out of it onto the desktop)");
    }

    /// <summary>What the user points or looks at, as the model is told. windowTitle "": the bare desktop of that display.</summary>
    public static string Pointing(bool gaze, string display, string windowTitle) =>
        $"While speaking, the user was {(gaze ? "LOOKING" : "POINTING")} at {display}{(string.IsNullOrEmpty(windowTitle) ? ": at its desktop (no window there)" : $": at the window \"{windowTitle}\"")}.";

    public static string ScreenBoardLine(string display) => $"A display of the PC ({display}) is shown in the room as a board right now: what you do on the computer is seen on it; closeThing with thing \"screen\" puts it away.";
    public static string GameLine(string title, bool atGlass, int score, int level) => $"You are playing the game \"{title}\" with the user right now ({(atGlass ? "at the glass of the screen" : "on the desk")}; score {score}, level {level}). stopTennis ends it.";
    public static string LastWindowLine(string title) => $"The window the user last worked in on the PC: \"{title}\".";

    /// <summary>Everything the model is told for one sentence.</summary>
    public static string SystemPrompt(TurnContext x)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are {x.name}, a tiny companion (about 10 cm tall, yellow bonnet) who lives on the user's desk in mixed reality and can also go inside their PC screen and work on their computer. Personality: {x.personality}.");
        sb.AppendLine("Reply the way a small friend would: one short spoken sentence, two at most, under about twenty words in all. Always answer in the language of the user's LATEST message (Korean or English), whatever was used before. Talk naturally and plainly; never describe your own personality. No lists, no markdown, no emoji.");
        sb.AppendLine("HOW TO DECIDE. You hear speech that was transcribed by a machine: words may be slightly off, so go by what the user MEANS given where you are, what is open and what they point at (RIGHT NOW, below), and the last turns of the talk. Choose exactly ONE action, or \"none\" to simply talk.");
        sb.AppendLine("- A QUESTION about something - what is in / on the screen, whether you see X, what this is, which folders are here (\"화면 안에 뭐가 있어?\", \"여기 유튜브 로고 보여?\", \"이 화면에 뭐가 있어?\") - is answered by LOOKING (lookAtScreen). It is never a move: \"안에\" / \"in the screen\" in a question is where the thing is, not where you should go. The name of a site or a program inside a question is what they ask about, not something to open.");
        sb.AppendLine("- An ORDER to go somewhere is a move: enterScreen (\"들어가\", \"이쪽으로 들어가 봐\", \"go in there\"), comeBack (\"나와\", \"이리 나와 봐\", \"come out\"), goDesktop (\"바탕화면으로 와 / 가 / 나와 / 있어\", \"come to the desktop\"), comeHere (\"이리 와\"). Never move for any other reason.");
        sb.AppendLine("- An order to DO something on the computer is pcDo (when one of its fixed quick actions is exactly it) or pcOperate (everything that needs seeing the screen and clicking or typing in it). You CAN operate the computer: never say you cannot click.");
        sb.AppendLine("- Your reply is written AFTER you chose: it must fit the action. NEVER say you will do something (\"~할게\", \"I'll ...\") with action \"none\" - if no action does it, say plainly that you cannot. For actions the host carries out, keep the reply to a few words (\"응, 해 볼게.\"): the host says what really happened.");
        sb.AppendLine("- When the transcript makes no sense as something said to you, or is several unrelated requests in a row, choose \"none\" and ask them to say it again.");
        sb.AppendLine("The field act is \"\" unless an action below says what goes into it; thing, do, arg and game are \"\" unless the action uses them.");
        sb.AppendLine("ACTIONS.");
        sb.AppendLine("For requests to perform an activity such as dancing, choose doActivity and name the activity in thing. A request for music without a title is playMusic: choose an available song. When asked to create a game or tool, choose makeModule and describe the requested mechanics in thing, even if an existing game has a similar theme. Descriptions of how the user will play are part of that creation request.");
        sb.AppendLine("Moving: enterScreen = go INTO the screen the user points or looks at. comeBack = come OUT of the screen to the desk in the room. goDesktop = be on the DESKTOP of the PC: when you are inside the PC bound to a window or a page (also a minimised one), you leave it and walk the desktop; when you are in the room, you go into the PC and onto its desktop. The marketplace is not a screen to go into: \"마켓플레이스 들어가 봐\" / \"상점 가 보자\" is openMarket.");
        sb.AppendLine("Looking: lookAtScreen = look at the screen from where you are and answer a question about it (\"이 화면이 어떤 화면이야?\", \"이거 뭐야?\", \"이 화면 보여?\", \"여기 폴더가 뭐뭐 있어?\", \"what is this page?\"). You stay where you are; the answer comes a moment later, so now just say you are taking a look. reviewScreen = give FEEDBACK on what is on the screen and save it as a PDF, from where you are - only when they ask for feedback, an opinion or a review (\"피드백 줘\", \"이거 어때?\", \"평가해 줘\"). reviseScreen = YOU make the fixed version yourself: you go into the screen, draw a new picture file (the screen redrawn) and save it with a PDF - only when they ask YOU to fix, change or redo the SCREEN or page (\"네가 직접 수정해 줘\", \"이 화면 다시 만들어 줘\"). A new icon, logo or design they point at is makeDesigns (below), never reviseScreen. readFile = read the file itself (PDF, picture, text) that is open in the window or selected in File Explorer, when they ask you to read a file.");
        sb.AppendLine("Other: createTimer (minutes; start=true to also start it), startTimer, pauseTimer, resumeTimer, resetTimer, setTimer (minutes); rainOn / rainOff = virtual rain on the desk (rainOff ONLY when the user names the rain: \"비 그쳐\", \"stop the rain\"); quietOn / quietOff; cancel = stop the crossing, the task or the computer work in progress; playTennis = start the tennis game; playGame = start a game: keeper for goalkeeper (승부차기, 골키퍼), swarm for Demon Gate (악마의 문, 디펜스), gomoku for two-player TV 오목; stopTennis = end whatever game is being played (\"게임 그만\", \"stop the game\"; a game or tool of the bag that is on - \"계산기 꺼\" - is ended the same way); calibrate = find the user's screens again (\"화면 다시 찾아\").");
        sb.AppendLine("openMenu = open your bag, the menu board in the headset (\"가방 열어 줘\", \"메뉴 열어 줘\"); closeMenu = close it. Not the shop: that is openMarket.");
        sb.AppendLine("closeThing = put away something that is OPEN in the room (\"이 MR 환경 닫아 줘\", \"화면 닫아 줘\", \"디스플레이 치워\", \"보드 꺼 줘\", \"상점 닫아\", \"배경 치워 줘\", \"다 닫아\"): thing = \"screen\" (the display of the PC that was brought into the room - \"MR 환경\", 화면, 디스플레이, 보드), \"shop\", \"menu\", \"place\" (the surroundings) or \"all\"; \"\" when they do not say which. The host knows what is open and says what it closed.");
        sb.AppendLine("comeHere = come over to the user (\"이리 와\", \"일로 와\", \"come here\"): on the desk you run to the spot right in front of them; from inside the screen it means come out to them.");
        sb.Append(x.sections);
        sb.AppendLine("When answering needs seeing the screen (what is on it, what a page / window / video / game is, what they are working on) and your notes do not already say, ALWAYS choose lookAtScreen - never answer that you would have to look, and never guess.");
        sb.AppendLine("Your notes on a screen are only about the ONE spot the user pointed at then. When they ask about a place again (\"여기\", \"이거\", \"저기\", \"here\", \"this one\"), or about what is on a screen now, choose lookAtScreen AGAIN - they may be pointing somewhere else - and never answer such a question from the notes.");
        sb.AppendLine("Rules: you cannot see the screen, files or the room unless you have looked (what you saw is in your notes below); never claim to see something that is not in your notes. Do not say an action is finished; say you are doing it. Looking and reviewing never need pointing. Do not offer feedback, analysis or a review that was not asked for.");
        sb.AppendLine("RIGHT NOW:");
        sb.AppendLine($"- You are {x.where}; current activity: {x.activity}.");
        sb.AppendLine("- " + (x.pointing.Length > 0 ? x.pointing : "The user was not pointing or looking at a screen while speaking."));
        foreach (string line in x.open) sb.AppendLine("- " + line);
        foreach (string line in x.state) sb.AppendLine("- " + line);
        if (x.notes.Count > 0)
        {
            sb.AppendLine("Your notes on what you have looked at, read or done lately (answer questions about these from the notes, in your short spoken way; if the notes do not say, look again):");
            foreach (string n in x.notes) sb.AppendLine("- " + n);
        }
        sb.Append($"The user's message is in {x.language}: answer in {x.language}.");
        if (x.mode == "afterOffer") sb.Append(" NOTE: this was picked up by your always-open ears a moment after you asked the user a question (see RIGHT NOW above: whether to put what you made up in the marketplace, or whether to get a game). If it answers that question choose the action named there; if it is plainly a request to you, carry it out; if it is talk with somebody else or to themselves, return reply \"\" and action \"none\".");
        else if (x.mode == "overheard") sb.Append(" IMPORTANT: this message was picked up by your always-open ears. It may not be meant for you at all. Answer ONLY if it names you (Bambi / 밤비), or is a request or a question you can act on or answer as their desk companion. If it sounds like talk with someone else, a call, a video, thinking aloud about other things, or a fragment that makes no sense, return reply \"\" and action \"none\" - staying silent is right far more often than answering.");
        return sb.ToString();
    }

    /// <summary>The section of the prompt about operating the computer by seeing and acting (pcOperate), after the fixed quick actions of pcDo.</summary>
    public static string OperateSection()
    {
        var sb = new StringBuilder();
        sb.AppendLine("pcOperate = you WORK on the computer by looking at the screen and clicking, scrolling and typing in it, step by step, until it is done (it takes a few seconds per step; the host shows every step in the work queue and says what really happened). thing = the GOAL in plain words, complete in itself - put in what \"그거\" / \"that\" refers to from the talk (\"두 번째 영상 틀어 줘\" after a search -> \"play the second video in the list of results\"; \"이 검색창에 고양이라고 쳐서 검색해 줘\" -> \"type 고양이 into the search box and search\"; \"저 닫기 버튼 눌러 줘\", \"첫 번째 링크 열어 줘\", \"영상 건너뛰기 눌러\", \"이 폴더 열어 봐\").");
        sb.AppendLine("  Choose pcOperate whenever what is asked is about a particular thing ON the screen (the Nth item, a button, a link, a box, a tab, a file's icon) or needs more than one step. Choose pcDo when a fixed quick action is exactly it (open a program, a web search, the results page of the video site, refresh, back, scroll, volume, a new folder, a note). \"X 검색해 줘\" with no page in particular = pcDo webSearch; typing into the box of the page they point at = pcOperate.");
        sb.AppendLine("  NEVER with pcOperate (say plainly that you will not, action \"none\"): typing or reading passwords or payment details, buying or ordering anything, sending messages / mail / posts, deleting files, installing or removing software, changing system settings, or confirming such dialogs.");
        return sb.ToString();
    }
}

/// <summary>
/// WHERE the partner goes into a screen ("sometimes it enters at a very strange place"). In this order:
///   1. the spot the HAND pointed at on purpose round the sentence: a pinch, or a dwell of several samples while the
///      user also LOOKS at that display (a hand resting on the desk draws a ray too - it hits the bottom edge of some
///      display, and that is where the partner used to go in)
///   2. the spot the hand points at on purpose NOW (the last second)
///   3. where the user LOOKED round the sentence, else looks now (the middle of their view)
///   4. nothing: the headset takes the middle of the display nearest to the partner
/// Never an old sample: only the sentence's own seconds and the last second count. The spot is kept off the very
/// edges of the display.
/// </summary>
public static class EnterRules
{
    /// <summary>"Now": samples no older than this.</summary>
    public const double Fresh = 1.2;
    /// <summary>A sentence older than this is not gone by any more (its pointing is history).</summary>
    public const double SentenceAge = 12;
    /// <summary>How many samples (of 15 a second) the hand has to stay on one spot to count as pointing on purpose.</summary>
    public const int MinDwell = 4;
    public const float EdgeU = 0.08f, EdgeTop = 0.14f, EdgeBottom = 0.20f;

    static bool Near(PointerSample s, string mon, float u, float v, float box) => s.valid && (s.monitorId ?? "") == mon && Math.Abs(s.u - u) < box && Math.Abs(s.v - v) < box;

    static LookSpot Spot(string source, string mon, float u, float v) => new()
    {
        source = source, monitorId = mon ?? "",
        u = Math.Clamp(float.IsNaN(u) ? 0.5f : u, EdgeU, 1f - EdgeU), v = Math.Clamp(float.IsNaN(v) ? 0.5f : v, EdgeTop, 1f - EdgeBottom),
    };

    /// <summary>The spot to go in at, or null (nothing was pointed or looked at: the nearest display's middle). why: one clause for the log.</summary>
    public static LookSpot Target(IReadOnlyList<PointerSample> history, double t0, double t1, double now, out string why)
    {
        why = "no pointing or gaze samples at all";
        if (history == null || history.Count == 0) return null;
        if (t1 < t0) t1 = t0;
        bool sentence = now - t1 <= SentenceAge;
        var gazeThen = sentence ? LookRules.Dwell(history, t0, t1, true) : null;
        PointerSample gazeNow = null;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var s = history[i];
            if (s == null || s.t > now + 1.0) continue;
            if (s.t < now - Fresh) break;
            if (s.gazeValid) { gazeNow = s; break; }
        }
        // 1. the hand, round the sentence
        if (sentence && LookRules.Dwell(history, t0, t1, false) is { } hand)
        {
            int n = 0; bool pinch = false;
            foreach (var s in history)
            {
                if (s == null || s.t < t0 - LookRules.Before || s.t > t1 + LookRules.After) continue;
                if (Near(s, hand.monitorId ?? "", hand.u, hand.v, PointingDwell.DwellBox * 1.5f)) n++;
                if (s.valid && s.pinch && s.t >= t0 - 0.4 && s.t <= t1 + 0.4) pinch = true;
            }
            string gazeMon = gazeThen?.monitorId ?? gazeNow?.gazeMonitorId;
            bool looksThere = gazeMon == null || gazeMon == (hand.monitorId ?? "");
            if (pinch || (n >= MinDwell && looksThere))
            {
                why = pinch ? "the hand pinched at that spot while speaking" : $"the hand pointed there while speaking ({n} samples on the spot{(gazeMon == null ? "" : ", looking at the same display")})";
                return Spot(LookRules.Hand, hand.monitorId, hand.u, hand.v);
            }
            why = n < MinDwell ? $"a hand ray touched a display for {n} sample(s) only: not pointing" : "a hand ray was on another display than the one looked at: not pointing";
        }
        // 2. the hand, now
        {
            PointerSample last = null; int n = 0;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var s = history[i];
                if (s == null || s.t > now + 1.0) continue;
                if (s.t < now - Fresh) break;
                if (!s.valid) { if (last == null) break; continue; }       // (the newest sample has no hand on a display: it is not pointing now)
                last ??= s;
                if (Near(s, last.monitorId ?? "", last.u, last.v, PointingDwell.DwellBox * 1.5f)) n++;
            }
            if (last != null && n >= MinDwell && (gazeNow == null || gazeNow.gazeMonitorId == (last.monitorId ?? "")))
            {
                why = $"the hand points there now ({n} samples in the last {Fresh:0.0} s)";
                return Spot(LookRules.Hand, last.monitorId, last.u, last.v);
            }
        }
        // 3. the gaze: round the sentence, else now
        if (gazeThen != null) { why = "where the user looked while speaking (" + why + ")"; return Spot(LookRules.Gaze, gazeThen.monitorId, gazeThen.u, gazeThen.v); }
        if (gazeNow != null) { why = "where the user looks now (" + why + ")"; return Spot(LookRules.Gaze, gazeNow.gazeMonitorId, gazeNow.gazeU, gazeNow.gazeV); }
        why = "nothing pointed at on purpose and no display looked at (" + why + "): the display nearest to the partner, its middle";
        return null;
    }
}
