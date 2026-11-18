using System.Text.RegularExpressions;

namespace LivingPartner.Host;

/// <summary>
/// The lines the host itself makes the partner say (results, refusals, the tennis score) are written in English. When
/// the user is speaking Korean they are said in Korean: on the first evening with a headset the partner answered in
/// Korean and then announced the tennis game in English.
/// </summary>
public static class HostLines
{
    static readonly Dictionary<string, string> Exact = new()
    {
        ["Let me finish what I'm doing first."] = "지금 하던 것부터 끝낼게.",
        ["We are already playing!"] = "이미 하고 있잖아!",
        ["We are not playing right now."] = "지금은 안 하고 있어.",
        ["I'm already inside the PC."] = "나 이미 화면 안에 있어.",
        ["I'm already out here on the desk."] = "나 이미 밖에 나와 있어.",
        ["I'm done looking. Open the window again or call me and I'll bring what I found."] = "다 봤어. 부르면 찾은 걸 가지고 나갈게.",
        ["There is no timer yet. Shall I make one first?"] = "아직 타이머가 없어. 먼저 하나 만들까?",
        ["The timer can't do that in its current state."] = "지금 타이머 상태로는 그건 안 돼.",
        ["I can't find that file."] = "그 파일을 못 찾겠어.",
        ["Show me which file: point at its window, or select it in File Explorer."] = "어떤 파일인지 알려 줘. 그 창을 가리키거나 탐색기에서 골라 줘.",
        ["I can read PDFs, pictures and text files, not that kind."] = "PDF, 그림, 텍스트 파일만 읽을 수 있어.",
        ["That file is too big for me to read."] = "그 파일은 너무 커서 못 읽겠어.",
        ["I didn't catch that. Could you say it again?"] = "잘 못 들었어. 다시 말해 줄래?",
        ["Sorry, I lost my train of thought. Could you say that again?"] = "미안, 다시 말해 줄래?",
        ["I couldn't make out what you said just now."] = "방금 한 말을 잘 못 알아들었어.",
        ["I need the headset on for that."] = "그건 헤드셋을 써야 할 수 있어.",
        ["I need the headset on to play tennis."] = "테니스는 헤드셋을 써야 할 수 있어.",
        ["I need the headset on to go through the screen."] = "화면으로 들어가려면 헤드셋을 써야 해.",
        ["I need the headset on to come out of the screen."] = "화면에서 나오려면 헤드셋을 써야 해.",
        ["I don't have a game like that yet."] = "그런 게임은 아직 없어.",
        ["You can see it in the headset."] = "헤드셋에서 볼 수 있어.",
        ["Here's my bag!"] = "응, 가방 열게!",
        ["Okay, I closed it."] = "응, 닫을게.",
    };

    static readonly (Regex rx, string ko)[] Patterns =
    {
        (new Regex(@"^Let's play! First to (\d+)\. Hit the ball back with your hand\.$"), "테니스 하자! $1점 먼저 내면 이겨. 손으로 공을 쳐서 넘겨 줘."),
        (new Regex(@"^You win, (\d+) to (\d+)! That was fun\.$"), "네가 이겼어, $1 대 $2! 재밌었다."),
        (new Regex(@"^I win this one, (\d+) to (\d+)! Shall we play again\?$"), "이번엔 내가 이겼어, $1 대 $2! 한 판 더 할래?"),
        (new Regex(@"^(.+) is done!$"), "$1 끝났어!"),
        (new Regex(@"^Hold on, I'll wait\..*$"), "잠깐, 기다릴게."),
        (new Regex(@"^I can't go right now: .*$"), "지금은 못 가. 하던 것부터 끝낼게."),
    };

    public static string Ko(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;
        if (Exact.TryGetValue(line, out var ko)) return ko;
        foreach (var (rx, to) in Patterns) if (rx.IsMatch(line)) return rx.Replace(line, to);
        return line;
    }
}
