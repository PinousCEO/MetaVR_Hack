using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>
/// The language model the WRITER asks (ScriptWriter.cs): the strongest code-capable model the host already uses -
/// the one that sculpts things (Sculptor.Model: gpt-5.4 unless LP_OPENAI_SCULPT_MODEL names another). The FIRST
/// writing - of a game, a thing or a skill - thinks at LOW effort: the user is waiting in a headset, and the program is
/// run in the checker anyway. MEASURED on 2026-11-18 through the published host (gpt-5.4, low; pictures not counted -
/// each picture a program asks for adds about half a minute), seconds until the module was in the bag: a shooting
/// game across the glass 52 (first writing 47, critic 4), tic-tac-toe 37, a to-do list 46 and 79, catch across the
/// glass 58, the script of a wearable hat 16. Of seven first writings of games five were right as they came; one
/// had two keys overlapping and a key on its line of hints, one drew "Nilo's clones" as the stock monsters. Every
/// game across the glass used shoot_out / out_events / aim / shots rightly at low effort: medium (143 - 157 s for
/// the first writing) buys nothing the user would wait for. What low gets wrong is caught WITHOUT the model where
/// it can be: names that do not exist (the rules list them now), keys that overlap or lie on a display
/// (ScriptRules.Layout measures them and sends the numbers back), Korean text on the screen (ScriptRules.ShownKorean).
/// A REPAIR of a program that failed its check thinks at medium (it has an error to think about; about two
/// minutes). The second writing for the CRITIC does not (ScriptWriter: repair = false): that program runs, and at
/// medium the one call took 111 s and 187 s - both times for a defect that was none (a picture the module itself
/// had painted; "only the left hand may block", which the API cannot do, and the repair made the game unfair).
/// The critic is now told the pictures and what the API has no means for.
/// LP_OPENAI_SCRIPT_MODEL, LP_OPENAI_SCRIPT_EFFORT (the first writing, of every kind)
/// and LP_OPENAI_SCRIPT_REPAIR_EFFORT name others. A hiccup of the service ("server error", "too many requests") is not a failed writing: the same question
/// is simply put again, up to three times. Nothing here ever logs the key; the client's own errors do not quote it.
/// </summary>
public sealed class OpenAiScriptModel : IScriptModel
{
    readonly OpenAiClient _ai;
    readonly bool _game;
    /// <summary>game: this model writes a game (said for the log; every first writing thinks at the same effort now: EffortFor).</summary>
    public OpenAiScriptModel(OpenAiClient ai, bool game = false) { _ai = ai; _game = game; }

    internal static string Model(OpenAiClient ai) => Environment.GetEnvironmentVariable("LP_OPENAI_SCRIPT_MODEL") is { Length: > 0 } m ? m : Sculptor.Model(ai);
    internal static string EffortFor(bool game) => Environment.GetEnvironmentVariable("LP_OPENAI_SCRIPT_EFFORT") is { Length: > 0 } e ? e : "low";      // (a game written at medium took 2.5 - 4.5 minutes with its critic round on 2026-11-18: too long to wait in a headset. Low is 35 - 80 s, and five of seven first writings were right as they came; the checker and the critic catch its slips.)
    internal string Effort => EffortFor(_game);
    internal static string RepairEffort => Environment.GetEnvironmentVariable("LP_OPENAI_SCRIPT_REPAIR_EFFORT") is { Length: > 0 } e ? e : "medium";
    /// <summary>The CRITIC: the quick model of the conversation at low effort, unless LP_OPENAI_SCRIPT_CRITIC_MODEL / _EFFORT name others ("off" = no critic).</summary>
    internal static string CriticModel(OpenAiClient ai) => Environment.GetEnvironmentVariable("LP_OPENAI_SCRIPT_CRITIC_MODEL") is { Length: > 0 } m ? m : ai.ChatModel;
    internal static string CriticEffort => Environment.GetEnvironmentVariable("LP_OPENAI_SCRIPT_CRITIC_EFFORT") is { Length: > 0 } e ? e : "low";

    public async Task<JObject> JudgeAsync(string system, string user, JObject schema, int maxTokens, CancellationToken ct)
    {
        string model = CriticModel(_ai);
        if (model == "off") return null;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(75));
        try { return await _ai.AskJsonAsync(system, user, Maker.NoFiles, schema, maxTokens, limit.Token, model, CriticEffort); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new Exception("the critic did not answer within 75 s"); }
    }

    /// <summary>
    /// `--write-script "<words>" --out <path without extension> [--effort low|medium] [--max-repairs n]`: the writer
    /// alone, for trying it out and for measuring - the request is written as a GAME exactly as the host would (same
    /// examples, same checker, same critic), the program goes to <out>.lua and what happened to <out>.txt. Nothing of
    /// the host starts and nothing is installed.
    /// </summary>
    public static async Task<int> RunAsync(string words, string outPath, string effort, int maxRepairs)
    {
        var lines = new List<string>();
        int code = 2;
        string full = Path.GetFullPath(outPath);
        try
        {
            var client = ClaudeGameClient.Open() ?? throw new InvalidOperationException("Set ANTHROPIC_API_KEY for game creation.");
            bool korean = words.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3);
            var request = new ScriptRequest { kind = ScriptRequest.Game, words = words, language = korean ? "Korean" : "English", id = "game-trial", maxRepairs = maxRepairs };
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(12));
            var model = new ClaudeWorkshopModel(client, string.IsNullOrEmpty(effort) ? "medium" : effort);
            lines.Add($"asked: {words}"); lines.Add($"model and critic: {model.Name}; repairs allowed: {ScriptWriter.Repairs(request)}");
            var res = await ScriptWriter.WriteAsync(model, request, ScriptWriter.GameExamples(), null, l => lines.Add(l), cts.Token);
            lines.Add($"ok: {res.ok}; calls: {res.calls}; repairs: {res.repairs}; seconds: {res.seconds:0.0}; lines: {res.Lines}; title: {res.title}; howTo: {res.howTo}");
            for (int i = 0; i < res.attempts.Count; i++) lines.Add($"attempt {res.attempts[i].number}: {res.attempts[i].seconds:0.0} s, {ScriptRules.Lines(res.attempts[i].script)} lines, check: {res.attempts[i].verdict?.Line ?? res.attempts[i].failure}");
            lines.Add($"critic: {(res.judged ? $"{res.judgeSeconds:0.0} s, {res.defects.Count} defect(s){(res.judgedRepairTaken ? ", its repair was taken" : "")}" : "not asked")}");
            foreach (string d in res.defects) lines.Add("  defect: " + d);
            lines.Add("design: " + res.design);
            if (res.verdict != null && res.verdict.report.Length > 0) lines.Add("checker report:\n" + res.verdict.report);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            for (int i = 0; i < res.attempts.Count; i++) File.WriteAllText($"{full}.attempt{res.attempts[i].number}.lua", res.attempts[i].script ?? "", new System.Text.UTF8Encoding(false));
            if (res.ok) File.WriteAllText(full + ".lua", res.script, new System.Text.UTF8Encoding(false));
            code = res.ok ? 0 : 1;
        }
        catch (Exception e) { lines.Add("FAILED: " + e.Message); }
        try { Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllLines(full + ".txt", lines, new System.Text.UTF8Encoding(false)); } catch { }
        return code;
    }

    /// <summary>One answer may take this long (a program of 160 lines, thought about first).</summary>
    const int Seconds = 240;

    public string Name => $"{Model(_ai)} ({Effort}; repairs {RepairEffort})";

    public async Task<JObject> AskAsync(string system, string user, JObject schema, int maxTokens, CancellationToken ct, bool repair = false)
    {
        string model = Model(_ai), effort = repair ? RepairEffort : Effort;
        for (int tries = 1; ; tries++)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(Seconds));
            try { return await _ai.AskJsonAsync(system, user, Maker.NoFiles, schema, maxTokens, limit.Token, model, effort); }
            catch (Exception e) when (tries < 3 && !ct.IsCancellationRequested && (e.Message.Contains("HTTP 5") || e.Message.Contains("HTTP 429")))
            {
                Log.Info($"script: the writer's request was turned away ({WorldRules.Short(e.Message, 60)}); asking again");
                await Task.Delay(TimeSpan.FromSeconds(1.5 * tries), ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new Exception($"the model did not answer within {Seconds} s"); }
        }
    }
}
