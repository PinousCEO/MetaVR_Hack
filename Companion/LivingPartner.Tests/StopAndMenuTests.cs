using LivingPartner.Host;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// What the plain parser (Shared/Modules.cs, IntentParser - no language model in it) makes of "멈춰", "게임 멈춰",
/// "비 그쳐", "가방 열어 줘": on 2026-11-17 "밤비야, 멈춰" and "밤비야, 게임 멈춰" stopped the RAIN ("비 그칠게") while a
/// game went on, because the 비 of the partner's own name was taken for rain. And what a plain stop ends
/// (WorldRules.StopWhat): the first thing that is on, a game before everything else.
/// </summary>
public class StopAndMenuTests
{
    // ------------------------------------------------------------------ stop
    [Theory]
    [InlineData("밤비야, 멈춰.", "")]                 // the exact sentences of that evening
    [InlineData("밤비야, 게임 멈춰.", "game")]
    [InlineData("게임 그만 멈춰.", "game")]
    [InlineData("밤비야, 게임 멈추라고.", "game")]
    [InlineData("멈춰", "")]
    [InlineData("멈춰 줘", "")]
    [InlineData("그만", "")]
    [InlineData("그만해", "")]
    [InlineData("이제 그만하자", "")]
    [InlineData("스톱", "")]
    [InlineData("스톱!", "")]
    [InlineData("stop", "")]
    [InlineData("Stop!", "")]
    [InlineData("Bambi, stop.", "")]
    [InlineData("stop the game", "game")]
    [InlineData("게임 그만", "game")]
    [InlineData("게임 그만해", "game")]
    [InlineData("게임 스톱", "game")]
    [InlineData("밤별아, 게임 멈춰 줘", "game")]
    [InlineData("밤비야 그만해", "")]
    public void A_plain_stop_is_the_general_stop_and_never_the_rain(string said, string game)
    {
        var it = IntentParser.Parse(said);
        Assert.Equal("stop", it.kind);
        Assert.Equal(game, it.name);
        Assert.False(IntentParser.NamesRain(said));
    }

    [Theory]
    [InlineData("비 그쳐", "rainOff")]
    [InlineData("비 멈춰", "rainOff")]
    [InlineData("밤비야, 비 멈춰", "rainOff")]
    [InlineData("밤비야, 비 그쳐 줘", "rainOff")]
    [InlineData("비 좀 그만 내려", "rainOff")]
    [InlineData("비를 멈춰 줘", "rainOff")]
    [InlineData("비 꺼 줘", "rainOff")]
    [InlineData("이제 비 그치게 해", "rainOff")]
    [InlineData("stop the rain", "rainOff")]
    [InlineData("stop rain", "rainOff")]
    [InlineData("rain off", "rainOff")]
    [InlineData("turn the rain off", "rainOff")]
    [InlineData("비 내려 줘", "rainOn")]
    [InlineData("비 좀 내려줘", "rainOn")]
    [InlineData("밤비야, 비 내리게 해 줘", "rainOn")]
    [InlineData("비가 오게 해줘", "rainOn")]
    [InlineData("let it rain", "rainOn")]
    [InlineData("make it rainy", "rainOn")]
    public void The_rain_stops_and_starts_only_when_rain_is_named(string said, string kind)
    {
        Assert.Equal(kind, IntentParser.Parse(said).kind);
        Assert.True(IntentParser.NamesRain(said));
    }

    [Theory]
    [InlineData("밤비야")]              // the 비 of the name
    [InlineData("밤비 내려와")]         // "Bambi, come down" - not "let it rain"
    [InlineData("밤비야 꺼 줘")]
    [InlineData("방비야 멈춰")]         // the name as the transcription sometimes writes it
    [InlineData("비디오 꺼 줘")]
    [InlineData("비밀이야")]
    [InlineData("준비 그만")]
    [InlineData("나비 그려 줘")]
    [InlineData("rainbow colours")]
    [InlineData("train stop")]
    [InlineData("")]
    [InlineData(null)]
    public void What_only_sounds_like_rain_is_not_rain(string said)
    {
        Assert.False(IntentParser.NamesRain(said));
        Assert.DoesNotContain(IntentParser.Parse(said).kind, new[] { "rainOn", "rainOff" });
    }

    [Theory]
    [InlineData("타이머 멈춰", "timerPause")]                       // a timer's own stop
    [InlineData("타이머 그만", "timerPause")]
    [InlineData("테니스 그만", "stopTennis")]
    [InlineData("테니스 그만하자", "stopTennis")]
    [InlineData("stop tennis", "stopTennis")]
    [InlineData("취소", "cancel")]
    [InlineData("취소해 줘", "cancel")]
    [InlineData("cancel", "cancel")]
    [InlineData("조용히 해", "quietOn")]
    [InlineData("조용 모드 꺼", "quietOff")]
    [InlineData("그만하고 책 읽어", "unknown")]                     // two wishes in one breath: for the language model
    [InlineData("그 노래 말고 다른 거 틀어", "unknown")]
    [InlineData("이제 그 얘기는 그만하고 다른 얘기 하자", "unknown")]
    [InlineData("don't stop the music", "unknown")]
    [InlineData("멈추지 마", "unknown")]
    [InlineData("이리 나와", "returnToMr")]
    [InlineData("여기로 들어가", "enterPortal")]
    [InlineData("오늘 날씨 좋다", "unknown")]
    public void The_other_commands_are_what_they_were(string said, string kind)
    {
        Assert.Equal(kind, IntentParser.Parse(said).kind);
    }

    [Fact]
    public void A_plain_stop_ends_the_first_thing_that_is_on_and_a_game_before_everything()
    {
        // (glass game, tennis, a crossing or task, the user said "game", a headset is there, the world has something on)
        Assert.Equal("glass", WorldRules.StopWhat(true, true, true, false, true, true));
        Assert.Equal("glass", WorldRules.StopWhat(true, false, false, true, false, true));
        Assert.Equal("tennis", WorldRules.StopWhat(false, true, true, false, true, true));
        Assert.Equal("tennis", WorldRules.StopWhat(false, true, false, true, true, true));
        Assert.Equal("cancel", WorldRules.StopWhat(false, false, true, false, true, true));
        // "게임 멈춰" with no game the host knows of: the headset's own game, and NOTHING else is touched - not the music, not a crossing
        Assert.Equal("game", WorldRules.StopWhat(false, false, false, true, true, true));
        Assert.Equal("game", WorldRules.StopWhat(false, false, true, true, true, true));
        Assert.Equal("nothing", WorldRules.StopWhat(false, false, false, true, false, true));
        // "멈춰" with no game on: the activity, the skill, the tune, the surroundings
        Assert.Equal("world", WorldRules.StopWhat(false, false, false, false, true, true));
        Assert.Equal("world", WorldRules.StopWhat(false, false, false, false, false, true));
        // nothing the host knows of: the headset is told all the same (it may be playing rock paper scissors)
        Assert.Equal("local", WorldRules.StopWhat(false, false, false, false, true, false));
        Assert.Equal("nothing", WorldRules.StopWhat(false, false, false, false, false, false));

        // one line that says what was stopped, in the language being spoken
        Assert.Equal("응, 게임 그만할게.", WorldRules.StopLine("glass", true, true));
        Assert.Equal("응, 게임 그만할게.", WorldRules.StopLine("game", true, true));
        Assert.Equal("응, 테니스 그만할게.", WorldRules.StopLine("tennis", false, true));
        Assert.Equal("Okay, I stopped the game.", WorldRules.StopLine("glass", false, false));
        Assert.Equal("지금은 게임 안 하고 있어.", WorldRules.StopLine("nothing", true, true));
        Assert.Equal("지금은 아무것도 안 하고 있어.", WorldRules.StopLine("nothing", false, true));
        Assert.Equal("", WorldRules.StopLine("world", false, true));       // (the Maker says what it put back)
        foreach (string what in new[] { "glass", "tennis", "cancel", "game", "local", "nothing" })
            foreach (bool ko in new[] { true, false })
            {
                string line = WorldRules.StopLine(what, false, ko);
                Assert.True(line.Length > 5 && !line.Contains("비 ") && !line.ToLowerInvariant().Contains("rain"), line);
            }
    }

    // ------------------------------------------------------------------ the bag / the menu
    [Theory]
    [InlineData("밤비야, 가방 열어 줘", "openMenu")]
    [InlineData("밤비야, 메뉴 열어 줘", "openMenu")]
    [InlineData("밤비야, 가방 보여 줘", "openMenu")]
    [InlineData("가방 열어줘", "openMenu")]
    [InlineData("가방 좀 열어 줄래?", "openMenu")]
    [InlineData("메뉴 보여줘", "openMenu")]
    [InlineData("메뉴판 보여 줘", "openMenu")]
    [InlineData("가방을 열어 봐", "openMenu")]
    [InlineData("가방 꺼내 줘", "openMenu")]
    [InlineData("메뉴 켜 줘", "openMenu")]
    [InlineData("open the menu", "openMenu")]
    [InlineData("Open your bag", "openMenu")]
    [InlineData("show me the bag", "openMenu")]
    [InlineData("밤비야, 가방 닫아 줘", "closeMenu")]
    [InlineData("밤비야, 메뉴 닫아 줘", "closeMenu")]
    [InlineData("가방 닫아", "closeMenu")]
    [InlineData("메뉴 닫아줘", "closeMenu")]
    [InlineData("가방 치워 줘", "closeMenu")]
    [InlineData("메뉴 꺼 줘", "closeMenu")]
    [InlineData("가방 이제 닫아", "closeMenu")]
    [InlineData("close the menu", "closeMenu")]
    [InlineData("hide the bag", "closeMenu")]
    public void The_bag_is_opened_and_closed_by_voice(string said, string kind)
    {
        var it = IntentParser.Parse(said);
        Assert.Equal(kind, it.kind);
    }

    [Theory]
    [InlineData("가방에서 의자 꺼내 줘")]           // a thing out of the bag: not the bag itself
    [InlineData("의자 가방에 넣어 줘")]
    [InlineData("가방에 뭐 있어?")]
    [InlineData("상점 열어 줘")]                     // the shop is not the bag
    [InlineData("메뉴 뭐 있어?")]
    [InlineData("창문 열어 줘")]
    [InlineData("오늘 점심 메뉴 추천해 줘")]
    [InlineData("open the shop")]
    [InlineData("close the window")]
    public void What_is_not_about_the_bag_itself_does_not_open_it(string said)
    {
        Assert.DoesNotContain(IntentParser.Parse(said).kind, new[] { "openMenu", "closeMenu" });
    }

    [Fact]
    public void The_intent_for_a_game_carries_the_models_words_beside_its_choice()
    {
        // (Intent.name: for playGame the words the model gave for the game; for stop "game" when a game was named)
        var it = new Intent { kind = "playGame", text = "vampire", name = "오목놀이" };
        var back = Proto.FromJson<Intent>(Proto.ToJson(it));
        Assert.Equal("vampire", back.text); Assert.Equal("오목놀이", back.name);
        Assert.Equal("", new Intent().name);
        Assert.Equal("", IntentParser.Parse("여기로 들어가").name);
    }
}
