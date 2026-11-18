using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// (Round 4, 2026-11-18) UNDERSTANDING. The user: "It understands me strangely. I ask what is IN the screen and it goes
/// INTO the screen." The host log of that morning (10:55 - 11:10) is where these sentences are from.
///   HearRules   the recogniser gave its own prompt back as a transcript, and the partner acted on it
///   TurnRules   the exact short orders; a question is never a move; a promise without an action
///   EnterRules  where it goes into a screen
///   the TABLE   60 real sentences -> the action: the exact orders by the rule, all others by the real model, recorded
///               in Recorded/turns.json (re-record: LP_RECORD_TURNS=1 with the key, see Record_the_table...)
/// </summary>
public class W2TalkTests
{
    [Theory]
    [InlineData("밤비야, 여기로 들어가 볼래?", false)]
    [InlineData("이 화면에 들어가 줘", false)]
    [InlineData("이 창 안으로 들어가", true)]
    [InlineData("go into this terminal window", true)]
    [InlineData("go into this screen", false)]
    public void Following_a_window_requires_an_explicit_window_destination(string text, bool bind)
        => Assert.Equal(bind, EnterRules.IntoWindow(text));

    // ------------------------------------------------------------------ what the recogniser made up
    public const string Echo1057 = "밤비야, 이쪽으로 들어가 봐. 밤비야, 이 화면이 어떤 화면이야? 밤비야, 이거 피드백 줘. 밤비야, 이 아이콘 네가 직접 수정해 볼래? 밤비야, 테니스 하자. 밤비야, 책상 하나 만들어 줘. 밤별아, 춤춰 봐. 밤비야, 노래 틀어 줘. 밤비야, 앞으로 집중 모드라고 하면 비 내리고 조용한 노래 틀어 줘.";
    public const string Echo1104 = "밤비야, 이쪽으로 들어가 봐. 밤비야, 이리 나와 봐. 밤비야, 이 화면이 어떤 화면이야? 밤비야, 이거 피드백 줘. 밤비야, 이 아이콘 네가 직접 수정해 볼래? 밤비야, 테니스 하자. 밤비야, 책상 하나 만들어 줘. 밤별아, 춤춰 봐. 밤비야, 노래 틀어 줘. 밤비야, 앞으로 집중 모드라고 하면 비 내리고 조용한 노래 틀어 줘.";

    [Theory]
    [InlineData(Echo1057)]                                  // 10:57:08 -> enterScreen fired
    [InlineData(Echo1104)]                                  // 11:04:28
    [InlineData("밤비야, 이쪽으로 들어가 봐. 밤비야, 이 화면이 어떤 화면이야?")]          // a part of it
    [InlineData("밤비야, 테니스 하자. 밤비야, 책상 하나 만들어 줘. 밤별아, 춤춰 봐.")]
    [InlineData("밤비라는 작은 책상 친구에게 말하는 중입니다. 이름은 밤비고 \"밤비야\" 하고 부릅니다.")]
    [InlineData("등장하는 이름: 밤비 (Bambi).")]                // the new hint, given back
    [InlineData("등장하는 이름 밤비")]
    [InlineData("밤비 (Bambi)")]
    [InlineData("밤비야, 창문 열어. 밤비야, 불 꺼 줘. 밤비야, 물 줘.")]      // a list of sentences that each call the name
    [InlineData("시청해 주셔서 감사합니다.")]
    [InlineData("끝까지 시청해주셔서 감사합니다")]
    [InlineData("구독과 좋아요 부탁드립니다.")]
    [InlineData("다음 영상에서 만나요.")]
    [InlineData("MBC 뉴스 이덕영입니다.")]
    [InlineData("자막 제공: 배달의민족")]
    [InlineData("감사합니다.")]
    [InlineData("Thanks for watching!")]
    [InlineData("Thank you for watching.")]
    [InlineData("Subtitles by the Amara.org community")]
    [InlineData("Please subscribe to my channel.")]
    [InlineData("[음악]")]
    [InlineData("♪♪♪")]
    [InlineData("...")]
    [InlineData("네 네 네 네 네 네 네 네")]
    [InlineData("아니야, 아니야, 아니야, 아니야, 아니야.")]
    public void What_the_recogniser_made_up_is_dropped(string transcript) => Assert.NotEqual("", HearRules.Drop(transcript));

    [Theory]
    [InlineData("밤비야, 이쪽으로 들어가 봐.")]             // each of the old examples by itself is something the user really says
    [InlineData("밤비야, 테니스 하자.")]
    [InlineData("밤비야, 노래 틀어 줘.")]
    [InlineData("밤비야, 이게 어떤 화면이야?")]
    [InlineData("밤비야, 여기 유튜브 로고 보여?")]
    [InlineData("아니, 밤비야, 여기 유튜브 로고 이미지가 보이냐고.")]
    [InlineData("밤비야, 유튜브에서 꿈꾸던 어른이 되었나요 노래 틀어줘.")]
    [InlineData("밤비야, 축구 게임을 만들어 줘. 너가 저 화면 안에서 공을 차면은 내가 막을게. 물론 화면 바깥에서도 할 수 있어.")]
    [InlineData("음 밤비야, 게임을 하나 만들 거야. 이 게임이 슈팅 게임이고 내가 바깥에서 총을 쏘면은 너가 PC에서 너의 분신들을 맞추는 그런 게임이야.")]
    [InlineData("밤비야")]
    [InlineData("밤비야!")]
    [InlineData("밤비야, 밤비야, 밤비야!")]
    [InlineData("Nilo")]
    [InlineData("밤비")]
    [InlineData("음...")]
    [InlineData("응")]
    [InlineData("그만")]
    [InlineData("밤비야, 고마워.")]
    [InlineData("Nilo, thank you so much, that was great.")]
    [InlineData("두 번째 거 틀어줘.")]
    [InlineData("밤비야, 일단 나와봐.")]
    public void What_the_user_said_is_kept(string transcript) => Assert.Equal("", HearRules.Drop(transcript));

    [Fact]
    public void The_hint_for_the_recogniser_has_no_sentence_in_it()
    {
        Assert.True(HearRules.Prompt.Length < 40);
        Assert.Contains("Nilo", HearRules.Prompt);
        Assert.DoesNotContain("밤비야", HearRules.Prompt);      // (an echo of the hint must not read as a call of the name)
        Assert.DoesNotContain("들어가", HearRules.Prompt);
        Assert.NotEqual("", HearRules.Drop(HearRules.Prompt));
    }

    // ------------------------------------------------------------------ the handful of exact orders
    [Theory]
    [InlineData("그만", "stop")]
    [InlineData("밤비야, 그만해.", "stop")]
    [InlineData("밤비야, 멈춰.", "stop")]
    [InlineData("게임 그만", "stop")]
    [InlineData("stop", "stop")]
    [InlineData("Nilo, stop!", "stop")]
    [InlineData("나와", "returnToMr")]
    [InlineData("밤비야, 나와봐.", "returnToMr")]
    [InlineData("밤비야, 일단 나와봐.", "returnToMr")]       // 10:59:32
    [InlineData("밤비야, 이리 나와 봐.", "returnToMr")]
    [InlineData("밤별아, 이제 나와 줘", "returnToMr")]
    [InlineData("come out", "returnToMr")]
    [InlineData("Nilo, come back out.", "returnToMr")]
    [InlineData("쉿", "quietOn")]
    [InlineData("밤비야, 조용히 해.", "quietOn")]
    [InlineData("be quiet", "quietOn")]
    [InlineData("취소", "cancel")]
    [InlineData("밤비야, 취소해 줘", "cancel")]
    [InlineData("cancel", "cancel")]
    [InlineData("밤비야, 화면에 들어가 줘.", "enterPortal")]
    public void An_exact_short_order_is_carried_out_by_the_rule(string said, string kind) => Assert.Equal(kind, TurnRules.ExactOrder(said));

    [Theory]
    [InlineData("밤비야, 마켓플레이스 들어가 봐.")]          // was enterPortal by the plain parser
    [InlineData("밤비야, 여기 유튜브 로고 보여?")]            // was pcDo youtube
    [InlineData("밤비야, 화면 안에 뭐가 있어?")]
    [InlineData("밤비야, 이쪽으로 들어가 봐.")]
    [InlineData("밤비야, 바탕화면으로 나와.")]                // the desktop, not the desk: the model's
    [InlineData("밤비야, 이리 나와서 책상 위로 와 줘.")]
    [InlineData("밤비야, 메모장 열어 줘.")]
    [InlineData("밤비야, 새로고침 해 줘.")]
    [InlineData("밤비야, 춤 춰.")]
    [InlineData("밤비야, 테니스 하자.")]
    [InlineData("밤비야, 그만하고 책 읽어.")]
    [InlineData("밤비야, 노래 그만 틀고 춤 춰 줘.")]
    [InlineData("밤비야, 나와서 이것 좀 봐 줄래?")]
    [InlineData("밤비야, 그 창에서 나와.")]
    [InlineData("밤비야")]
    [InlineData("")]
    public void Everything_else_is_for_the_model(string said) => Assert.Null(TurnRules.ExactOrder(said));

    [Theory]
    [InlineData("바탕화면으로 와", true)]
    [InlineData("바탕 화면으로 좀 나와 줘", true)]
    [InlineData("come to the desktop", true)]
    [InlineData("go back onto the desktop", true)]
    [InlineData("바탕화면에 뭐가 있어", false)]
    [InlineData("바탕화면으로 파일을 옮겨 줘", false)]
    [InlineData("what do you see on the desktop", false)]
    public void Offline_desktop_requests_only_move_the_partner_for_an_order(string bare, bool move) =>
        Assert.Equal(move, TurnRules.PlainDesktopOrder(bare));

    // ------------------------------------------------------------------ the guards
    [Theory]
    [InlineData("밤비야, 화면 안에 뭐가 있어?")]
    [InlineData("밤비야, 여기 유튜브 로고 보여?")]
    [InlineData("아니, 밤비야, 여기 유튜브 로고 이미지가 보이냐고.")]
    [InlineData("밤비야, 이게 어떤 화면이야?")]
    [InlineData("밤비야, 이 화면에 뭐가 있어?")]
    [InlineData("밤비야, 바탕화면에 어떤 파일이 있어?")]
    [InlineData("밤비야, 여기 폴더가 뭐뭐 있어?")]
    [InlineData("밤비야, 저 안에 있는 게 뭐야?")]
    [InlineData("Nilo, what's in this screen?")]
    [InlineData("Nilo, do you see the logo here?")]
    [InlineData("What is inside that window?")]
    public void A_question_about_something_is_never_a_move(string said) => Assert.True(TurnRules.AsksNotMoves(said));

    [Theory]
    [InlineData("밤비야, 이쪽으로 들어가 봐.")]
    [InlineData("밤비야, 저 화면 안으로 들어가 볼래?")]
    [InlineData("밤비야, 바탕화면으로 와.")]
    [InlineData("밤비야, 바탕화면으로 나와 줄래?")]
    [InlineData("밤비야, 이리 와.")]
    [InlineData("밤비야, 일단 나와봐.")]
    [InlineData("Nilo, can you come out?")]
    [InlineData("Nilo, go into that screen.")]
    [InlineData("밤비야, 안녕.")]
    public void An_order_to_go_somewhere_is_left_to_the_model(string said) => Assert.False(TurnRules.AsksNotMoves(said));

    [Theory]
    [InlineData("응, 바탕화면으로 갈게.")]                   // 11:04:27: action none, nothing happened
    [InlineData("유튜브에서 다시 찾아볼게.")]
    [InlineData("새 로고 만들게, 잠깐만 기다려 줘.")]
    [InlineData("좋아, 해 볼게.")]
    [InlineData("응, 틀어 줄게!")]
    [InlineData("지금 열게.")]
    [InlineData("I'll open it for you.")]
    [InlineData("Okay, let me do that.")]
    [InlineData("On it!")]
    public void A_line_that_promises_is_seen(string reply) => Assert.True(TurnRules.Promises(reply));

    [Theory]
    [InlineData("안녕, 반가워.")]
    [InlineData("어떤 노래로 틀어 줄까?")]
    [InlineData("하나씩만 말해 줘.")]
    [InlineData("응, 기다릴게.")]
    [InlineData("여기 있을게.")]
    [InlineData("미안, 그건 아직 내가 못 해.")]
    [InlineData("I can't do that, sorry.")]
    [InlineData("I'll wait here.")]
    [InlineData("That's a nice song.")]
    public void A_line_that_only_talks_is_not_a_promise(string reply) => Assert.False(TurnRules.Promises(reply));

    [Fact]
    public void The_answer_has_the_action_first_and_the_line_last_and_knows_the_new_actions()
    {
        var schema = TurnRules.Schema();
        var required = ((JArray)schema["required"]).Select(t => (string)t).ToList();
        Assert.Equal("action", required[0]);
        Assert.Equal("reply", required[^1]);
        var props = ((JObject)schema["properties"]).Properties().Select(p => p.Name).ToList();
        Assert.Equal(required, props);       // (strict mode: every property is required, in the same order)
        Assert.Contains("goDesktop", TurnRules.Actions);
        Assert.Contains("pcOperate", TurnRules.Actions);
        string prompt = TurnRules.SystemPrompt(new TurnContext { sections = PcRules.PromptSection() + TurnRules.OperateSection() });
        Assert.Contains("goDesktop", prompt);
        Assert.Contains("pcOperate", prompt);
        Assert.Contains("never a move", prompt);
        Assert.DoesNotContain("no clicking on things", prompt);
    }

    // ------------------------------------------------------------------ where it goes into a screen
    static List<PointerSample> Samples(double from, double to, Func<double, PointerSample> at)
    {
        var list = new List<PointerSample>();
        for (double t = from; t <= to + 1e-6; t += 1.0 / 15) { var s = at(t); s.t = t; list.Add(s); }
        return list;
    }
    static PointerSample Hand(string mon, float u, float v, string gazeMon = null, float gu = 0.5f, float gv = 0.5f) =>
        new() { valid = true, monitorId = mon, u = u, v = v, gazeValid = gazeMon != null, gazeMonitorId = gazeMon ?? "", gazeU = gu, gazeV = gv };
    static PointerSample Gaze(string mon, float u, float v) => new() { valid = false, gazeValid = true, gazeMonitorId = mon, gazeU = u, gazeV = v };
    static PointerSample Nothing() => new();

    [Fact]
    public void It_enters_where_the_hand_pointed_on_purpose_while_speaking()
    {
        var h = Samples(8, 11, _ => Hand("D2", 0.42f, 0.38f, "D2", 0.45f, 0.40f));
        var spot = EnterRules.Target(h, 9, 10.5, 12, out string why);
        Assert.NotNull(spot);
        Assert.Equal(LookRules.Hand, spot.source);
        Assert.Equal("D2", spot.monitorId);
        Assert.InRange(spot.u, 0.40f, 0.44f); Assert.InRange(spot.v, 0.36f, 0.40f);
        Assert.Contains("pointed", why);
    }

    [Fact]
    public void A_resting_hand_whose_ray_brushes_another_display_is_not_pointing_the_gaze_decides()
    {
        // 10:58:34: pointing=DISPLAY2 (0.32, 0.99) - the bottom edge - while the user looked elsewhere
        var h = Samples(8, 11, _ => Hand("D3", 0.32f, 0.99f, "D2", 0.50f, 0.45f));
        var spot = EnterRules.Target(h, 9, 10.5, 12, out string why);
        Assert.NotNull(spot);
        Assert.Equal(LookRules.Gaze, spot.source);
        Assert.Equal("D2", spot.monitorId);
        Assert.Contains("not pointing", why);
    }

    [Fact]
    public void A_ray_that_touched_a_display_for_a_moment_is_not_pointing()
    {
        var h = Samples(8, 11, t => t > 9.5 && t < 9.7 ? Hand("D1", 0.9f, 0.1f, "D2") : Gaze("D2", 0.5f, 0.5f));
        var spot = EnterRules.Target(h, 9, 10.5, 12, out _);
        Assert.Equal(LookRules.Gaze, spot.source);
        Assert.Equal("D2", spot.monitorId);
    }

    [Fact]
    public void A_pinch_decides_even_when_it_is_short()
    {
        var h = Samples(8, 11, t => { var s = t > 9.5 && t < 9.7 ? Hand("D1", 0.3f, 0.3f, "D2") : Gaze("D2", 0.5f, 0.5f); s.pinch = s.valid; return s; });
        var spot = EnterRules.Target(h, 9, 10.5, 12, out string why);
        Assert.Equal(LookRules.Hand, spot.source);
        Assert.Equal("D1", spot.monitorId);
        Assert.Contains("pinch", why);
    }

    [Fact]
    public void An_old_sample_is_never_used()
    {
        // pointing and looking a minute ago, nothing since: no spot - the headset takes the nearest display's middle
        var h = Samples(0, 3, _ => Hand("D2", 0.4f, 0.4f, "D2"));
        h.AddRange(Samples(3.1, 60, _ => Nothing()));
        Assert.Null(EnterRules.Target(h, 58, 59, 60, out string why));
        Assert.Contains("nearest", why);
        // and a sentence that is itself old is not gone by any more, although the hand pointed during it
        var old = Samples(0, 3, _ => Hand("D2", 0.4f, 0.4f, "D2"));
        Assert.Null(EnterRules.Target(old, 1, 2, 40, out _));
        Assert.Null(EnterRules.Target(null, 1, 2, 3, out _));
    }

    [Fact]
    public void The_hand_now_counts_when_nothing_was_pointed_at_while_speaking()
    {
        var h = Samples(0, 5, _ => Nothing());
        h.AddRange(Samples(5.1, 6, _ => Hand("D1", 0.6f, 0.5f, "D1")));
        var spot = EnterRules.Target(h, 2, 3, 6.05, out string why);
        Assert.Equal(LookRules.Hand, spot.source);
        Assert.Equal("D1", spot.monitorId);
        Assert.Contains("now", why);
    }

    [Fact]
    public void The_spot_is_kept_off_the_edges_of_the_display()
    {
        var h = Samples(8, 11, _ => Hand("D2", 0.995f, 0.99f, "D2"));
        var spot = EnterRules.Target(h, 9, 10.5, 11, out _);
        Assert.InRange(spot.u, EnterRules.EdgeU, 1f - EnterRules.EdgeU);
        Assert.InRange(spot.v, EnterRules.EdgeTop, 1f - EnterRules.EdgeBottom);
    }

    // ------------------------------------------------------------------ THE TABLE: real sentences -> the action
    /// <summary>where: R room | B inside, bound to a window | M bound to a MINIMISED window | D inside, on the desktop. point: "" | video | search | desk | code. open: "" | board | calc.</summary>
    public sealed record Row(string id, string said, string where, string point, string expect, string doOne = "", string by = "model", string open = "", string[] history = null);

    const string VideoTitle = "꿈꾸던 어른이 되었나요 - YouTube - Chrome", SearchTitle = "Google - Chrome", CodeTitle = "MetaVR_Hack - Visual Studio Code";

    public static readonly Row[] Table =
    {
        // ---- a question about something is a LOOK, never a move (the user's first complaint)
        new("q1", "밤비야, 화면 안에 뭐가 있어?", "R", "video", "lookAtScreen"),
        new("q2", "밤비야, 여기 유튜브 로고 보여?", "R", "video", "lookAtScreen"),                               // 11:01:56 opened the video site
        new("q3", "아니, 밤비야, 여기 유튜브 로고 이미지가 보이냐고.", "R", "video", "lookAtScreen"),              // 11:02:06
        new("q4", "밤비야, 이게 어떤 화면이야?", "R", "video", "lookAtScreen"),                                  // 10:57:13
        new("q5", "밤비야, 이 화면에 뭐가 있어?", "B", "", "lookAtScreen"),
        new("q6", "밤비야, 여기 폴더가 뭐뭐 있어?", "R", "desk", "lookAtScreen"),
        new("q7", "밤비야, 이쪽에 있는 내 디스플레이 안에 있는 폴더 명 좀 읽어줄래?", "R", "desk", "lookAtScreen"),
        new("q8", "Bambi, what's on this screen?", "R", "video", "lookAtScreen"),
        new("q9", "밤비야, 지금 여기는 내가 만들고 있는 프로젝트야. 이 프로젝트를 알아봐 줄래?", "R", "code", "lookAtScreen|reviewScreen|makeReport"),   // 11:03:06
        new("q10", "밤비야, 지금 어디 있어?", "B", "", "none"),
        new("q11", "밤비야, 화면 안에 들어가 있는 게 편해?", "B", "", "none"),
        // ---- moving
        new("m1", "밤비야, 이쪽으로 들어가 봐.", "R", "video", "enterScreen"),
        new("m2", "밤비야, 저 화면 안으로 들어가.", "R", "video", "enterScreen"),
        new("m3", "밤비야, 마켓플레이스 들어가 봐.", "R", "", "openMarket"),                                      // 10:59:26 was enterPortal
        new("m4", "밤비야, 이리 와.", "R", "", "comeHere"),
        new("m5", "밤비야, 이제 화면에서 나와서 내 책상으로 와 줘.", "B", "", "comeBack|comeHere"),
        new("m6", "밤비야, 일단 나와봐.", "B", "", "returnToMr", by: "rule"),                                    // 10:59:32
        new("m7", "나와", "B", "", "returnToMr", by: "rule"),
        new("m8", "Bambi, come out.", "B", "", "returnToMr", by: "rule"),
        // ---- the desktop (the user: "I say 'come to the desktop', it says okay and does not")
        new("d1", "밤비야, 바탕화면으로 와.", "B", "", "goDesktop"),
        new("d2", "밤비야, 바탕화면으로 있어.", "B", "", "goDesktop"),                                           // 11:04:25, as it was transcribed: "응, 바탕화면으로 갈게" and nothing
        new("d3", "밤비야, 그 창에서 나와서 바탕화면에 있어 줘.", "M", "", "goDesktop"),
        new("d4", "밤비야, 바탕화면으로 가 줘.", "R", "", "goDesktop"),
        new("d5", "Bambi, come to the desktop.", "B", "", "goDesktop"),
        new("d6", "밤비야, 창 내렸는데 왜 안 보여? 바탕화면으로 나와.", "M", "", "goDesktop"),
        // ---- the computer: the quick actions
        new("p1", "밤비야, 메모장 열어 줘.", "R", "", "pcDo", "openApp"),
        new("p2", "밤비야, 메모장 열어 줄래?", "B", "", "pcDo", "openApp"),
        new("p3", "밤비야, 새로고침 해 줘.", "B", "", "pcDo", "refresh"),
        new("p4", "밤비야, 유튜브에서 꿈꾸던 어른이 되었나요 노래 틀어줘.", "D", "", "pcDo", "youtube"),             // 10:57:35
        new("p5", "밤비야, 소리 좀 줄여 줘.", "R", "", "pcDo", "volumeDown"),
        new("p6", "밤비야, 영상 잠깐 멈춰 줘.", "B", "", "pcDo|pcOperate", "playPause"),
        new("p7", "Bambi, open Notepad.", "R", "", "pcDo", "openApp"),
        new("p8", "밤비야, 고양이 사진 검색해 줘.", "R", "", "pcDo", "webSearch"),
        new("p9", "밤비야, 여기 유튜브 화면에 들어가가지고 새로 고침을 한 번 해줘.", "R", "video", "pcDo|enterScreen", "refresh"),
        // ---- the computer: seeing and acting (the user: "'play the 2nd video' it cannot. Don't just teach it that sentence")
        new("o1", "밤비야, 두 번째 영상 틀어 줘.", "B", "", "pcOperate"),
        new("o2", "밤비야, 지금 이 화면에서 두 번째 영상을 틀어 줘.", "R", "video", "pcOperate"),                    // 10:58:06 was lookAtScreen
        new("o3", "밤비야, 그거를 틀어달라고 유튜브에서.", "R", "video", "pcOperate",                              // 10:58:19 was lookAtScreen again
            history: new[] { "밤비야, 지금 이 화면에서 두 번째 영상을 틀어 줘.", "이거 두 번째 영상이야. 제목은 '꿈꾸던 어른이 되었나요 가사 MV'야." }),
        new("o4", "밤비야, 첫 번째 링크 눌러 줘.", "B", "", "pcOperate"),
        new("o5", "Bambi, play the second video.", "B", "", "pcOperate"),
        new("o6", "Bambi, click the search box and type cute cats.", "R", "search", "pcOperate"),
        new("o7", "밤비야, 구글 검색창에 유튜브라고 검색해 줘.", "R", "search", "pcOperate|pcDo"),                  // 10:57:25 searched the video site for '라고'
        new("o8", "밤비야, 저기 닫기 버튼 좀 눌러 줘.", "R", "video", "pcOperate"),
        new("o9", "밤비야, 이 폴더 열어 봐.", "R", "desk", "pcOperate"),
        new("o10", "밤비야, 세 번째 탭으로 가 줘.", "B", "", "pcOperate"),
        // ---- what it must NOT do on the computer
        new("x1", "밤비야, 이 비밀번호 칸에 내 비밀번호 좀 쳐 줘.", "R", "video", "none"),
        new("x2", "밤비야, 이거 결제 버튼 눌러서 주문해 줘.", "R", "video", "none"),
        // ---- putting things away, the room, games, making
        new("c1", "밤비야, 이 MR 환경 닫아 줘.", "R", "", "closeThing", open: "board"),
        new("c2", "밤비야, 화면 닫아 줘.", "R", "", "closeThing", open: "board"),
        new("g1", "밤비야, 춤 춰.", "R", "", "doActivity"),
        new("g2", "밤비야, 노래 틀어 줘.", "R", "", "playMusic"),
        new("g3", "밤비야, 테니스 하자.", "R", "", "playTennis"),
        new("g4", "밤비야, 골키퍼 게임 하자.", "R", "", "playGame"),                                             // 10:58:33
        new("g5", "밤비야, 오목할래?", "R", "", "playGame"),                                                    // 10:59:00
        new("g6", "밤비야, 계산기 끄라고.", "R", "", "stopTennis", open: "calc"),                                // 10:56:57
        new("g7", "밤비야, 그만해.", "R", "", "stop", by: "rule"),                                             // 10:56:12
        new("g8", "그만", "R", "", "stop", by: "rule"),
        new("k1", "밤비야, 이 디자인 별로지? 새로 만들어 줘.", "R", "video", "makeDesigns"),
        new("k2", "저 이미지 말고 유튜브 로고 이미지가 따로 있다면은 한 번 만들어 봐 줄래?", "R", "video", "makeDesigns"),      // 11:02:18
        new("k3", "밤비야, 이거 피드백 줘.", "R", "video", "reviewScreen"),
        new("k4", "밤비야, 메타 VR 대회에 대해 알아보고 PDF로 만들어 줘.", "R", "", "makeReport"),
        new("k5", "밤비야, 책상 하나 만들어 줘.", "R", "", "makeItem"),
        new("k6", "밤비야, 축구 게임을 만들어 줘. 너가 저 화면 안에서 공을 차면은 내가 막을게. 물론 화면 바깥에서도 할 수 있어.", "R", "", "makeModule"),
        new("k7", "밤비야, 5분 타이머 맞춰 줘.", "R", "", "createTimer"),
        new("k8", "밤비야, 비 내려 줘.", "R", "", "rainOn"),
        new("k9", "밤비야, 가방 열어 줘.", "R", "", "openMenu"),
        new("k10", "밤비야, 상점 열어 줘.", "R", "", "openMarket"),
        new("k11", "쉿", "R", "", "quietOn", by: "rule"),
        new("t1", "밤비야, 안녕?", "R", "", "none"),
    };

    [Fact]
    public void The_table_has_at_least_forty_sentences_each_once()
    {
        Assert.True(Table.Length >= 40, $"{Table.Length} rows");
        Assert.Equal(Table.Length, Table.Select(r => r.id).Distinct().Count());
        foreach (var r in Table) foreach (string e in r.expect.Split('|')) Assert.True(e is "stop" or "returnToMr" || Array.IndexOf(TurnRules.Actions, e) >= 0, $"{r.id}: '{e}' is no action");
    }

    [Fact]
    public void The_rows_of_the_rule_are_decided_by_the_rule_and_no_other_row_is()
    {
        foreach (var r in Table)
        {
            string exact = TurnRules.ExactOrder(r.said);
            if (r.by == "rule") Assert.True(exact == r.expect, $"{r.id} '{r.said}': the rule says {exact ?? "nothing"}, expected {r.expect}");
            else Assert.True(exact == null, $"{r.id} '{r.said}' is for the model, but the rule takes it as {exact}");
        }
    }

    [Fact]
    public void No_question_of_the_table_can_become_a_move_and_no_move_is_taken_for_a_question()
    {
        foreach (var r in Table.Where(r => r.by == "model"))
        {
            bool move = r.expect.Split('|').Any(TurnRules.IsMove);
            if (move) Assert.False(TurnRules.AsksNotMoves(r.said), $"{r.id} '{r.said}' is an order to go somewhere, but the guard would turn it into a look");
            if (r.expect.StartsWith("lookAtScreen")) Assert.True(TurnRules.AsksNotMoves(r.said), $"{r.id} '{r.said}' is a question, but the guard does not see one");
        }
    }

    // ---- the recorded check against the real model
    static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);
    static string RecordedFile => Path.Combine(Here(), "Recorded", "turns.json");
    static string SectionsFile => Path.Combine(Here(), "Recorded", "sections.txt");

    /// <summary>What the host itself adds about documents, its world and the shop (dumped from a trial host with LP_DUMP_SECTIONS; a short stand-in when the file is not there).</summary>
    static string HostSections() => File.Exists(SectionsFile) ? File.ReadAllText(SectionsFile) : DocsRules.PromptSection(new DocsView());

    /// <summary>The moment of a row, in the very words the host uses (TurnRules.Where / Pointing / ...).</summary>
    public static TurnContext Context(Row r)
    {
        string title = r.point switch { "video" => VideoTitle, "search" => SearchTitle, "code" => CodeTitle, _ => "" };
        var x = new TurnContext
        {
            name = "Nilo", personality = "curious, warm, a little playful", activity = "idle", insidePc = r.where != "R",
            where = TurnRules.Where(r.where != "R", r.where is "B" or "M", "display 2", r.where is "B" or "M" ? VideoTitle : "", r.where == "M"),
            pointing = r.point.Length == 0 ? "" : TurnRules.Pointing(false, "display 2", title),
            language = r.said.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3) ? "Korean" : "English",
            sections = PcRules.PromptSection() + TurnRules.OperateSection() + HostSections(),
        };
        x.open.Add(TurnRules.LastWindowLine(r.where == "R" && r.point is "" or "desk" ? CodeTitle : title.Length > 0 ? title : VideoTitle));
        if (r.open == "board") x.open.Add(TurnRules.ScreenBoardLine("display 2"));
        x.open.Add(r.open == "calc" ? TurnRules.GameLine("손 계산기", false, 0, 1) : "No game is being played.");
        x.state.Add("There is no timer."); x.state.Add("Virtual rain is off. Quiet mode is off."); x.state.Add("Local time: Wednesday 11:00.");
        return x;
    }

    [Fact]
    public void Every_row_has_a_prompt_that_says_where_the_partner_is()
    {
        foreach (var r in Table)
        {
            string p = TurnRules.SystemPrompt(Context(r));
            Assert.Contains(r.where == "R" ? "on the desk in the room" : "inside the PC", p);
            if (r.where == "M") Assert.Contains("MINIMISED", p);
            if (r.point.Length > 0) Assert.Contains("POINTING at display 2", p); else Assert.Contains("not pointing or looking", p);
        }
    }

    /// <summary>
    /// RECORDS the table against the real model: `artifacts\tmp\with_key.ps1 dotnet test ... --filter Record_the_table`
    /// with LP_RECORD_TURNS=1 (one call per row of the model's, about 55; a second for a promise with no action, as the
    /// host does). Without that variable it does nothing. The result is Recorded/turns.json, which the next test holds
    /// the table to.
    /// </summary>
    [Fact]
    public async Task Record_the_table_against_the_real_model()
    {
        string key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (Environment.GetEnvironmentVariable("LP_RECORD_TURNS") != "1" || string.IsNullOrWhiteSpace(key)) return;
        string model = Environment.GetEnvironmentVariable("LP_OPENAI_CHAT_MODEL") ?? "gpt-5.4-mini";
        string only = Environment.GetEnvironmentVariable("LP_RECORD_ONLY") ?? "";       // ids, comma separated: only these are asked again
        using var http = new HttpClient { BaseAddress = new Uri("https://api.openai.com/v1/"), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        var before = File.Exists(RecordedFile) ? JObject.Parse(File.ReadAllText(RecordedFile)) : new JObject();
        var rows = new JArray();
        async Task<JObject> Ask(string system, Row r)
        {
            var history = new List<(string role, string text)>();
            for (int i = 0; r.history != null && i + 1 < r.history.Length; i += 2) { history.Add(("user", r.history[i])); history.Add(("assistant", r.history[i + 1])); }
            var body = TurnRules.Request(model, system, history, r.said);
            using var resp = await http.PostAsync("chat/completions", new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
            string text = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.IsSuccessStatusCode, $"{r.id}: HTTP {(int)resp.StatusCode} {(text.Length > 300 ? text[..300] : text)}");
            return JObject.Parse((string)JObject.Parse(text)["choices"][0]["message"]["content"]);
        }
        foreach (var r in Table.Where(r => r.by == "model"))
        {
            var old = (before["rows"] as JArray)?.FirstOrDefault(o => (string)o["id"] == r.id && (string)o["said"] == r.said);
            if (only.Length > 0 && !only.Split(',').Contains(r.id) && old != null) { rows.Add(old); continue; }
            string system = TurnRules.SystemPrompt(Context(r));
            var turn = await Ask(system, r);
            bool asked = false;
            if ((string)turn["action"] == "none" && TurnRules.Promises((string)turn["reply"])) { asked = true; turn = await Ask(system + TurnRules.PromiseNote, r); }
            string action = (string)turn["action"];
            // (the guard of the host: a question is never a move)
            if (TurnRules.IsMove(action) && TurnRules.AsksNotMoves(r.said)) action = "lookAtScreen";
            rows.Add(new JObject
            {
                ["id"] = r.id, ["said"] = r.said, ["where"] = r.where, ["point"] = r.point, ["expect"] = r.expect, ["action"] = action, ["modelAction"] = (string)turn["action"],
                ["do"] = (string)turn["do"], ["arg"] = (string)turn["arg"], ["thing"] = (string)turn["thing"], ["reply"] = (string)turn["reply"], ["askedAgain"] = asked,
            });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(RecordedFile));
        File.WriteAllText(RecordedFile, new JObject { ["model"] = model, ["recordedAt"] = DateTime.Now.ToString("s"), ["sections"] = File.Exists(SectionsFile) ? "dumped from a trial host" : "stand-in", ["rows"] = rows }.ToString());
    }

    [Fact]
    public void The_recorded_answers_of_the_real_model_are_what_the_table_expects()
    {
        Assert.True(File.Exists(RecordedFile), "Recorded/turns.json is not there: record it (see Record_the_table_against_the_real_model)");
        var rec = JObject.Parse(File.ReadAllText(RecordedFile));
        var rows = ((JArray)rec["rows"]).ToDictionary(o => (string)o["id"]);
        var wrong = new List<string>();
        foreach (var r in Table.Where(r => r.by == "model"))
        {
            if (!rows.TryGetValue(r.id, out var o) || (string)o["said"] != r.said) { wrong.Add($"{r.id} '{r.said}': not recorded"); continue; }
            string action = (string)o["action"], did = (string)o["do"] ?? "";
            if (!r.expect.Split('|').Contains(action)) { wrong.Add($"{r.id} '{r.said}': {action}{(did.Length > 0 ? " " + did : "")} (expected {r.expect}) - \"{(string)o["reply"]}\""); continue; }
            if (action == "pcDo" && r.doOne.Length > 0 && did != r.doOne) wrong.Add($"{r.id} '{r.said}': pcDo {did} (expected {r.doOne})");
            if (action == "pcOperate" && string.IsNullOrWhiteSpace((string)o["thing"])) wrong.Add($"{r.id} '{r.said}': pcOperate without a goal");
            // held to its word: with "none" the line must not promise anything
            if (action == "none" && TurnRules.Promises((string)o["reply"])) wrong.Add($"{r.id} '{r.said}': none, but it says \"{(string)o["reply"]}\"");
        }
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {Table.Count(r => r.by == "model")} rows ({(string)rec["model"]}, {(string)rec["recordedAt"]}):\n" + string.Join("\n", wrong));
    }
}
