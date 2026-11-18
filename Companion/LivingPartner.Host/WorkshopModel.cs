using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

// The workshop's two ends in the host: the MODEL it talks to (the OpenAI Responses API with the stored key) and the
// COMMAND LINE for trying it without anything else running (--work, --work-try). The loop, its tools and the harness's
// rules are Workshop.cs (nothing of the host in them: the tests run them with a model that answers from a list).

/// <summary>The Responses API with the stored key. Nothing here ever logs or quotes the key.</summary>
public sealed class OpenAiWorkshopModel : IWorkshopModel
{
    static string[] Models => WorkshopOptions.Models;
    readonly HttpClient _http;
    string _model;
    public string Name => _model;

    public OpenAiWorkshopModel(string key, string model)
    {
        _model = string.IsNullOrEmpty(model) ? Models[0] : model;
        _http = new HttpClient { BaseAddress = new Uri("https://api.openai.com/v1/"), Timeout = TimeSpan.FromSeconds(300) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    /// <summary>The model for the stored key (null when there is no key).</summary>
    public static OpenAiWorkshopModel Open(string model = "")
    {
        string key = AiKey.Load(out _);
        return key == null ? null : new OpenAiWorkshopModel(key, model);
    }

    public async Task<JObject> RespondAsync(JObject body, CancellationToken ct)
    {
        for (int tries = 1; ; tries++)
        {
            body["model"] = _model;
            using var content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            HttpResponseMessage r;
            try { r = await _http.PostAsync("responses", content, ct); }
            catch (HttpRequestException e) when (tries < 3 && !ct.IsCancellationRequested) { Log.Info($"workshop: the request did not get through ({e.GetType().Name}); once more"); await Task.Delay(1500 * tries, ct); continue; }
            using (r)
            {
                string text = await r.Content.ReadAsStringAsync(ct);
                if (r.IsSuccessStatusCode) return JObject.Parse(text);
                string msg = "";
                try { msg = JObject.Parse(text)["error"]?["message"]?.ToString() ?? ""; } catch { }
                int code = (int)r.StatusCode;
                // a model this key does not have: the next one of the list
                if ((code == 404 || code == 400) && msg.Contains("model", StringComparison.OrdinalIgnoreCase) && (msg.Contains("not exist", StringComparison.OrdinalIgnoreCase) || msg.Contains("not found", StringComparison.OrdinalIgnoreCase) || msg.Contains("access", StringComparison.OrdinalIgnoreCase)))
                {
                    int at = Array.IndexOf(Models, _model);
                    if (at + 1 < Models.Length && tries < 4) { Log.Info($"workshop: the model {_model} is not to be had ({WorldRules.Short(msg, 80)}); taking {Models[at + 1]}"); _model = Models[at + 1]; continue; }
                }
                if ((code == 429 || code >= 500) && tries < 3 && !ct.IsCancellationRequested) { Log.Info($"workshop: turned away (HTTP {code}); asking again"); await Task.Delay(2000 * tries, ct); continue; }
                throw new Exception($"the workshop's model: HTTP {code} {(msg.Length > 0 ? WorldRules.Short(msg, 300) : WorldRules.Short(text, 200))}");
            }
        }
    }
}

public static class WorkshopCli
{
    /// <summary>
    /// `--work-try &lt;file.lua | example id&gt; [--words "&lt;request&gt;"] [--out &lt;folder&gt;]`: no model at all - a game script is
    /// checked, played by the three players and rendered, exactly as the workshop would, and what the harness says
    /// about it is written down (result.txt, play-*.txt, frame-*.png).
    /// </summary>
    public static int TryScript(string source, string words, string outDir)
    {
        string dir = Path.GetFullPath(string.IsNullOrEmpty(outDir) ? "work-try" : outDir);
        Directory.CreateDirectory(dir);
        var utf8 = new UTF8Encoding(false);
        string lua = File.Exists(source) ? File.ReadAllText(source) : GlassScriptExamples.Find(source)?.lua ?? "";
        var lines = new List<string>();
        if (lua.Length == 0) { File.WriteAllText(Path.Combine(dir, "result.txt"), "no script: " + source, utf8); return 2; }
        var v = ScriptRules.CheckGame(lua, null, ScriptRules.MadeGameSeconds, 1);
        lines.Add("check: " + v.Line);
        var plays = WorkshopPlay.Players.Select(p => WorkshopPlay.Run(lua, null, p, 40f, 1, true, new[] { 3f, 16f, 32f })).ToList();
        foreach (var p in plays) { lines.Add(p.Digest()); File.WriteAllText(Path.Combine(dir, $"play-{p.player}.txt"), p.Text(), utf8); }
        var objections = WorkshopRules.AcceptGame(words ?? "", lua, v, plays[0], plays[1], plays[2]);
        lines.Add(objections.Count == 0 ? "harness: ACCEPTED" : "harness objects:"); lines.AddRange(objections.Select(o => "- " + o));
        foreach (var p in plays.Where(p => p.player != WorkshopPlay.Random))
            foreach (var f in p.frames) { byte[] png = WorkshopLook.Png(f); if (png != null) File.WriteAllBytes(Path.Combine(dir, $"frame-{p.player}-{f.t:000.0}s.png"), png); }
        File.WriteAllLines(Path.Combine(dir, "result.txt"), lines, utf8);
        return objections.Count == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ trying it without anything else running
    /// <summary>
    /// `--work "&lt;request&gt;" [--kind game|thing|skill] [--out &lt;folder&gt;] [--effort e] [--model m]`: the workshop alone. The
    /// steps are printed as they happen (and written to steps.txt), the final script goes to script.lua, the rendered
    /// frames to frame-*.png, everything that happened to result.txt. Nothing of the host starts; nothing is installed.
    /// </summary>
    public static async Task<int> RunAsync(string words, string kind, string outDir, string effort, string modelName)
    {
        string dir = Path.GetFullPath(string.IsNullOrEmpty(outDir) ? "work-trial" : outDir);
        Directory.CreateDirectory(dir);
        var utf8 = new UTF8Encoding(false);
        string stepsFile = Path.Combine(dir, "steps.txt");
        File.WriteAllText(stepsFile, "", utf8);
        var lines = new List<string>();
        void Out(string line) { lines.Add(line); try { Console.WriteLine(line); File.AppendAllText(stepsFile, line + "\n", utf8); } catch { } }
        int code = 2;
        try
        {
            // ("@file": the request is that file's text, UTF-8 - a script of the shell need not carry Korean)
            if (words.StartsWith("@") && File.Exists(words[1..])) words = File.ReadAllText(words[1..], Encoding.UTF8).Trim();
            kind = kind is ScriptRequest.Thing or ScriptRequest.Skill ? kind : ScriptRequest.Game;
            var o = WorkshopOptions.For(kind);
            if (!string.IsNullOrEmpty(effort)) o.effort = effort;
            if (!string.IsNullOrEmpty(modelName)) o.model = modelName;
            IWorkshopModel model;
            if (kind == ScriptRequest.Game)
            {
                model = new ClaudeWorkshopModel(ClaudeGameClient.Open() ?? throw new InvalidOperationException("Set ANTHROPIC_API_KEY for game creation."));
                o.model = model.Name;
                o.store = false;
            }
            else model = OpenAiWorkshopModel.Open(o.model) ?? throw new Exception("there is no OpenAI key");
            bool korean = words.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3);
            var request = new ScriptRequest { kind = kind, words = words, language = korean ? "Korean" : "English", id = kind + "-trial" };
            if (kind == ScriptRequest.Thing)
            {
                var guess = ItemKinds.Guess(words);
                request.item = new ItemDef { id = "item-trial", name = WorldRules.RoughName(words, guess), kind = guess.id, uses = (string[])guess.uses.Clone(), sizeM = guess.sizeM };
            }
            Out($"asked: {words}"); Out($"kind: {kind}; model: {o.model} ({o.effort}); caps: {o.maxVersions} versions, {o.maxTurns} turns, {o.maxSeconds} s");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(o.maxSeconds + 240));
            string lastStep = "";
            var sw = Stopwatch.StartNew();
            var res = await Workshop.MakeAsync(model, request, o, (text, p, round) => { if (text != lastStep) { lastStep = text; Out($"[{sw.Elapsed.TotalSeconds,5:0.0}s {p * 100,3:0}% r{round}] {text}"); } }, l => Out("  " + l), cts.Token);
            Out($"RESULT: {(res.ok ? "MADE" : "FAILED")} in {res.seconds:0.0} s; model {res.model}; turns {res.turns}; versions {res.rounds}; critic calls {res.criticCalls}; tokens {res.tokensIn} in + {res.tokensOut} out; ended by: {res.endedBy}");
            if (!res.ok) Out("why: " + res.why);
            else { Out($"title: {res.title}"); Out($"how to: {res.howTo}"); Out($"summary: {res.summary}"); if (res.missingApi.Length > 0) Out("missing in the API: " + res.missingApi); if (res.art.Count > 0) Out("pictures asked for: " + string.Join("; ", res.art.Select(a => a.name + " = " + a.drawing))); if (res.phrases.Length > 0) Out("phrases: " + string.Join(" / ", res.phrases)); }
            foreach (var v in res.versions)
            {
                Out($"version {v.number}{(v.note.Length > 0 ? " (" + v.note + ")" : "")}: {ScriptRules.Lines(v.script)} lines; check: {v.verdict?.Line}; harness: {(v.Accepted ? "accepted" : string.Join(" | ", v.objections))}; critic: {(v.criticAsked ? (v.criticOk ? "content" : string.Join(" | ", v.criticSaid)) : "not asked")}");
                if (v.nobody != null) Out("    " + v.nobody.Digest());
                if (v.random != null) Out("    " + v.random.Digest());
                if (v.seeker != null) Out("    " + v.seeker.Digest());
                File.WriteAllText(Path.Combine(dir, $"version{v.number}.lua"), v.script, utf8);
                if (v.room.Length > 0) File.WriteAllText(Path.Combine(dir, $"version{v.number}.room.txt"), v.room, utf8);
                if (v.seeker != null) File.WriteAllText(Path.Combine(dir, $"version{v.number}.play.txt"), v.seeker.Text(), utf8);
            }
            if (res.ok) File.WriteAllText(Path.Combine(dir, "script.lua"), res.script, utf8);
            foreach (var (name, png) in res.frames) File.WriteAllBytes(Path.Combine(dir, name + ".png"), png);
            code = res.ok ? 0 : 1;
        }
        catch (Exception e) { Out("FAILED: " + e.Message); }
        try { File.WriteAllLines(Path.Combine(dir, "result.txt"), lines, utf8); } catch { }
        return code;
    }
}
