using System.Diagnostics;
using System.Text;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>
/// The partner's own toy maker. When a thing gets no generated 3D model (no Tripo key, no credit, the service failed),
/// the language model SCULPTS it instead: it is told what the thing is and answers with a list of simple solids -
/// boxes with rounded edges, cylinders, cones, balls, rods and rings, each with a place, a size, a turn and a colour.
/// The headset joins them into one mesh and draws it like the built-in toy shapes (PropMeshes.BuildParts), so that
/// anything the user can say can be made, in the look of the partner's world.
/// The craft is in the prompt below; what comes back is checked by WorldRules (which never trusts it) before it is
/// shown. Nothing in here touches the host's state: it is used by the Maker on the thread pool, and by the command
/// line (`--sculpt`) for trying it out with nothing else running.
/// </summary>
public static class Sculptor
{
    /// <summary>Seeing a thing in space out of numbers is the hard part: the stronger model the host already looks at screens with, unless LP_OPENAI_SCULPT_MODEL names another.</summary>
    internal static string Model(OpenAiClient ai) => Environment.GetEnvironmentVariable("LP_OPENAI_SCULPT_MODEL") is { Length: > 0 } m ? m : ai.VisionModel;
    /// <summary>How long it thinks first (none | low | medium | high); LP_OPENAI_SCULPT_EFFORT.</summary>
    internal static string Effort => Environment.GetEnvironmentVariable("LP_OPENAI_SCULPT_EFFORT") is { Length: > 0 } e ? e : "low";
    const int MaxTokens = 12000;        // forty parts are about 3000 tokens; the rest is room to think

    static JObject Shape => Maker.Obj(("plan", Maker.Str),
        ("parts", Maker.Arr(Maker.Obj(("name", Maker.Str), ("shape", Maker.OneOf(ShapePart.Shapes)),
            ("x", Maker.Num), ("y", Maker.Num), ("z", Maker.Num), ("sx", Maker.Num), ("sy", Maker.Num), ("sz", Maker.Num),
            ("rx", Maker.Num), ("ry", Maker.Num), ("rz", Maker.Num), ("round", Maker.Num), ("color", Maker.Str)))),
        ("seat", Maker.Num), ("top", Maker.Num));

    static string Brief => SculptureDesign.Brief;

    static string Ask(ItemIdea idea, string asked)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"To sculpt: {(idea.name.Length > 0 ? idea.name : asked)}{(idea.modelPrompt.Length > 0 ? " - " + idea.modelPrompt : "")}");
        sb.AppendLine($"The user's own words for it: \"{Maker.Quote(asked)}\"");
        sb.AppendLine($"What it is for: {(idea.uses.Length > 0 ? string.Join(", ", idea.uses) : "decor")}. The plain shape it is nearest to: {idea.kind}.");
        sb.AppendLine(idea.color.Length > 0 ? $"Its main colour: {idea.color}." : "Its main colour: choose one from the palette.");
        if (idea.realCm > 0) sb.AppendLine($"The real thing is about {idea.realCm:0} cm on its longest side (that tells you its proportions; your sculpture is 1 unit on its longest side whatever the real size).");
        return sb.ToString();
    }

    /// <summary>
    /// Sculpts the thing the idea describes. Asks once more when the answer could not be used at all; parts that hang
    /// in the air are brought in (WorldRules.Join). null when it did not work - the caller keeps the built-in shape.
    /// progress: called now and then with 0..1 while waiting (there is no real measure of how far a model has thought).
    /// answer: handed each raw answer of the model as it comes (the command line keeps them, for reading).
    /// </summary>
    /// pictures / more / effortOverride (Sight.cs, "make what I am holding"): a photo of the real object for the model
    /// to look at while it sculpts, what it is told on top of its craft because of that, and how long it thinks.
    internal static async Task<Sculpture> SculptAsync(OpenAiClient ai, string id, ItemIdea idea, string asked, CancellationToken ct, Action<float> progress = null, Action<JObject> answer = null,
        (string mime, byte[] data, string name)[] pictures = null, string more = "", string effortOverride = null)
    {
        string model = Model(ai), effort = string.IsNullOrEmpty(effortOverride) ? Effort : effortOverride, user = Ask(idea, asked) + (more ?? ""), note = "";
        var sw = Stopwatch.StartNew();
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (progress != null) _ = Tick(progress, waiting.Token);
        try
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var json = await AskAsync(ai, user + note, model, effort, ct, pictures);
                    answer?.Invoke(json);
                    var s = WorldRules.Sculpt(json, out string why);
                    if (s == null)
                    {
                        Log.Warn($"world: {id}: sculpting attempt {attempt} cannot be used: {why}");
                        note = $"\nYour last answer could not be used: {why}. Sculpt it again, out of 6 to 28 parts.";
                        continue;
                    }
                    int hung = s.loose.Count, moved = WorldRules.Join(s);
                    Log.Info($"world: {id} sculpted out of {s.parts.Count} parts in {sw.Elapsed.TotalSeconds:0.0} s by {model} ({effort}), attempt {attempt}: height {s.height:0.00}, seat {s.seat:0.00}, top {s.top:0.00}" +
                             (hung > 0 ? $"; {hung} part(s) hung in the air, {moved} brought in{(s.loose.Count > 0 ? $", {s.loose.Count} still loose" : "")}" : ""));
                    return s;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception e) { Log.Warn($"world: {id}: sculpting attempt {attempt} failed after {sw.Elapsed.TotalSeconds:0.0} s ({model}): {e.Message}"); note = ""; }
            }
            return null;
        }
        finally { waiting.Cancel(); }
    }

    /// <summary>
    /// One answer of the model. A hiccup of the service is not a failed sculpture: every few requests it answers
    /// "server error" within a second (or "too many requests"), and the same request passes a moment later - so it is
    /// simply put again, up to three times, before that counts as a failure.
    /// </summary>
    static async Task<JObject> AskAsync(OpenAiClient ai, string user, string model, string effort, CancellationToken ct, (string mime, byte[] data, string name)[] pictures = null)
    {
        for (int tries = 1; ; tries++)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(180));
            try { return await ai.AskJsonAsync(Brief, user, pictures ?? Maker.NoFiles, Shape, MaxTokens, limit.Token, model, effort); }
            catch (Exception e) when (tries < 3 && !ct.IsCancellationRequested && (e.Message.Contains("HTTP 5") || e.Message.Contains("HTTP 429")))
            {
                Log.Info($"world: the sculptor's request was turned away ({WorldRules.Short(e.Message, 60)}); asking again");
                await Task.Delay(TimeSpan.FromSeconds(1.5 * tries), ct);
            }
        }
    }

    /// <summary>While the model thinks, the ring over the thing fills: fast at first, never quite to the end.</summary>
    static async Task Tick(Action<float> progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                progress(0.95f * (1f - MathF.Exp(-(float)sw.Elapsed.TotalSeconds / 14f)));
                await Task.Delay(600, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// The command line's `--sculpt "words" --out file.json`: understands the thing as the Maker does, sculpts it and
    /// writes the ItemDef as JSON (and, next to it, file.raw.json: what the model answered, and the log). Starts
    /// nothing else of the host. Returns the exit code: 0 = sculpted, 1 = understood but not sculpted (the file then
    /// holds the built-in shape), 2 = nothing could be done.
    /// </summary>
    public static async Task<int> RunAsync(string words, string outPath)
    {
        var report = new JObject { ["asked"] = words };
        int code = 2;
        try
        {
            string key = AiKey.Load(out _);
            if (key == null) throw new Exception("there is no OpenAI key (OPENAI_API_KEY, or the one stored with tools\\Set-OpenAiKey.ps1)");
            var ai = new OpenAiClient(key);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            var guess = ItemKinds.Guess(words);
            bool korean = words.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3);
            var sw = Stopwatch.StartNew();
            var understood = await ai.AskJsonAsync(Maker.ItemSystem, Maker.ItemAsk(words, "(nothing yet)", "", korean ? "Korean" : "English"), Maker.NoFiles, Maker.ItemShape, 1500, cts.Token, Maker.JsonModel(ai));
            var idea = WorldRules.Item(understood, guess);
            report["understood"] = understood; report["understoodMs"] = sw.ElapsedMilliseconds;
            var item = new ItemDef
            {
                id = "item-" + Maker.NewId(), name = idea.name.Length > 0 ? idea.name : WorldRules.RoughName(words, guess), asked = words, kind = idea.kind, uses = idea.uses,
                sizeM = idea.sizeM, color = idea.color, note = idea.note, status = "ready", progress = 1, inRoom = true, madeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            var answers = new JArray();
            sw.Restart();
            var s = await SculptAsync(ai, item.id, idea, words, cts.Token, null, a => answers.Add(a));
            report["model"] = Model(ai); report["effort"] = Effort; report["sculptMs"] = sw.ElapsedMilliseconds; report["answers"] = answers;
            if (s != null) { item.parts = s.parts; item.seat = s.seat; item.top = s.top; }
            string full = Path.GetFullPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, Proto.ToJson(item, true), new UTF8Encoding(false));
            code = s != null ? 0 : 1;
            Console.WriteLine($"{item.name}: {(s != null ? $"{s.parts.Count} parts, height {s.height:0.00}" : "not sculpted (built-in " + item.kind + ")")} in {sw.Elapsed.TotalSeconds:0.0} s -> {full}");
        }
        catch (Exception e) { report["error"] = e.Message; Console.WriteLine("sculpt: " + e.Message); }
        try
        {
            report["log"] = new JArray(Log.Recent.ToArray());
            string raw = Path.ChangeExtension(Path.GetFullPath(outPath), ".raw.json");
            Directory.CreateDirectory(Path.GetDirectoryName(raw));
            File.WriteAllText(raw, report.ToString(), new UTF8Encoding(false));
        }
        catch { }
        return code;
    }
}
