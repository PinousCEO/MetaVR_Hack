using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>
/// Where the Tripo key comes from: the LP_TRIPO_KEY or TRIPO_API_KEY environment variable, or a file in the host's data
/// folder that only this Windows user can decrypt (DPAPI). It is written by tools\Set-TripoKey.ps1. Like the OpenAI key
/// it is never logged, never put in a snapshot and never stored in the repository.
/// </summary>
public static class TripoKey
{
    public static string File => Path.Combine(HostPaths.Root, "tripo.key.dpapi");

    public static string Load(out string source)
    {
        source = "";
        // our own name first, so that this app can be given a key of its own on a machine where another tool sets TRIPO_API_KEY
        foreach (string name in new[] { "LP_TRIPO_KEY", "TRIPO_API_KEY" })
        {
            string env = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(env)) { source = "environment"; return env.Trim(); }
        }
        try
        {
            if (System.IO.File.Exists(File))
            {
                string key = Encoding.UTF8.GetString(ProtectedData.Unprotect(System.IO.File.ReadAllBytes(File), null, DataProtectionScope.CurrentUser)).Trim();
                if (key.Length > 0) { source = "protected file"; return key; }
            }
        }
        // only the kind of error is logged: the message of a decryption error says nothing useful and nothing of the key may leak
        catch (Exception e) { Log.Warn("the stored Tripo key could not be read: " + e.GetType().Name); }
        return null;
    }
}

/// <summary>
/// Text to a 3D model with Tripo's API, version 3 (https://developers.tripo3d.ai/en/docs): a task is created, asked
/// about every two seconds until it has finished, and the GLB file it made is downloaded.
///   POST https://openapi.tripo3d.ai/v3/generation/text-to-model   -> { code: 0, data: { task_id } }
///   GET  https://openapi.tripo3d.ai/v3/tasks/{task_id}            -> { code: 0, data: { status, progress, output: { model_url }, error_message } }
///   GET  https://openapi.tripo3d.ai/v3/account/balance            -> { code: 0, data: { balance, frozen } }
/// Errors come as { code, message, suggestion } with an HTTP status; code 2010 (HTTP 403) means there is not enough credit.
/// Nothing here touches the rest of the host: it needs a key and gives back bytes.
/// </summary>
public sealed class TripoClient
{
    /// <summary>The geometry model. v3.1 is the one the documentation recommends; "P1-20260311" and "P2-20260801" are Tripo's low-poly models.</summary>
    public readonly string Model = Setting("LP_TRIPO_MODEL") ?? "v3.1-20260211";
    /// <summary>fast (needs texture model v3.5), standard, detailed or extreme (8K: far too much for a headset).</summary>
    public readonly string TextureQuality = Setting("LP_TRIPO_TEXTURE_QUALITY") ?? "standard";
    /// <summary>Also make metallic, roughness and normal maps. Off unless LP_TRIPO_PBR=1: see Request.</summary>
    public readonly bool Pbr = Setting("LP_TRIPO_PBR") == "1";

    static readonly TimeSpan Limit = TimeSpan.FromMinutes(6);       // a model usually takes one to two minutes; the documentation suggests giving up after about five
    static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);   // the documentation asks for one question every 1 to 2 seconds
    const int MaxPrompt = 1024;                                     // longer prompts are refused by the API
    const int MaxMisses = 5;                                        // questions in a row that may go unanswered before we give up

    readonly HttpClient _http, _plain;

    public TripoClient(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("no Tripo key");
        _http = new HttpClient { BaseAddress = new Uri("https://openapi.tripo3d.ai/v3/"), Timeout = TimeSpan.FromSeconds(30) };
        // a key with a line break or another character that cannot go into a header makes .NET throw an error whose
        // text QUOTES the value: that error must not travel on, so it is replaced by one that says nothing of the key
        try { _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim()); }
        catch (FormatException) { throw new ArgumentException("the Tripo key contains characters that cannot be sent (was something else pasted with it?)"); }
        // The finished file lies on a storage server, at an address that already carries its own permission. It is
        // fetched by a second client WITHOUT the Authorization header: the key is for Tripo's API and nobody else.
        _plain = new HttpClient { Timeout = TimeSpan.FromSeconds(120), MaxResponseContentBufferSize = 96L * 1024 * 1024 };
    }

    /// <summary>A failed call. Transient: the network, a rate limit or the server - asking again may work.</summary>
    sealed class Problem : Exception
    {
        public readonly bool Transient;
        public Problem(string message, bool transient) : base(message) { Transient = transient; }
    }

    static string Setting(string name)
    {
        string v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    // ------------------------------------------------------------------ the key
    /// <summary>
    /// Proves that the key works without spending anything: the balance of the account is asked for.
    /// Returns "ok, balance 123"; throws with a short reason ("... HTTP 401 ...") when the key is refused.
    /// </summary>
    public async Task<string> CheckAsync(CancellationToken ct)
    {
        JToken data = await Call(HttpMethod.Get, "account/balance", null, "checking the key", ct);
        double? balance = Number(data["balance"]), frozen = Number(data["frozen"]);
        if (balance == null) return "ok (the key was accepted; the answer named no balance)";
        string text = "ok, balance " + balance.Value.ToString("0.##", CultureInfo.InvariantCulture);
        // "frozen" = credit set aside for tasks that are still running; it is given back when a task fails
        if (frozen > 0) text += " (" + frozen.Value.ToString("0.##", CultureInfo.InvariantCulture) + " more held for running tasks)";
        return text;
    }

    // ------------------------------------------------------------------ text to model
    /// <summary>
    /// Makes a model from a description and returns the GLB file. progress: 0..1 and a few words, called one call
    /// after the other (never two at once), from a thread-pool thread. faceLimit: the most triangles wanted (0 = let
    /// Tripo decide). Throws an Exception with a short message when it does not work; an OperationCanceledException
    /// when ct is cancelled. A task that was started is NOT withdrawn at Tripo when we stop waiting: if it still
    /// succeeds there it is charged, and it can be found in the Tripo console by the task id in the log.
    /// </summary>
    public async Task<byte[]> TextToModelAsync(string prompt, int faceLimit, Action<float, string> progress, CancellationToken ct)
    {
        prompt = (prompt ?? "").Trim();
        if (prompt.Length == 0) throw new Exception("Tripo: there is nothing to make (the description is empty)");
        if (prompt.Length > MaxPrompt) { Log.Warn($"tripo: the description is {prompt.Length} characters long, cut to {MaxPrompt}"); prompt = prompt[..MaxPrompt]; }

        // one clock for the whole job (queue + making + download), on top of the caller's own cancellation
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Limit);
        CancellationToken t = limit.Token;

        // Everything below runs as ONE chain of awaits, so the callback is never inside two calls at the same time.
        // The number never goes back (Tripo's own percentage sometimes does), and a callback that throws must not
        // lose a model that has already been paid for.
        float shown = 0;
        void Report(float p, string text)
        {
            shown = Math.Clamp(Math.Max(p, shown), 0f, 1f);
            try { progress?.Invoke(shown, text); } catch (Exception e) { Log.Warn("tripo: the progress callback failed: " + e.Message); }
        }

        string taskId = "";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Report(0f, "asking Tripo for a model");
            // Not repeated when it fails: the task may have been created although the answer got lost, and a second
            // request would then be a second model on the bill.
            JToken made = await Call(HttpMethod.Post, "generation/text-to-model", Request(prompt, faceLimit), "creating the task", t);
            taskId = Text(made["task_id"]);
            if (taskId.Length == 0) taskId = Text(made["id"]);
            if (taskId.Length == 0) throw new Exception("Tripo: the request was accepted but no task id came back");
            Log.Info($"tripo: task {taskId} created (model={Model} faces<={faceLimit} pbr={Pbr} texture={TextureQuality}) for '{prompt}'");
            Report(0.02f, "waiting in Tripo's queue");

            JToken output = null;
            double? cost = null;
            int misses = 0;
            string lastStatus = "";
            while (output == null)
            {
                // after an unanswered question the next one comes a little later each time (rate limits ask for that)
                await Task.Delay(PollEvery * (1 + misses), t);
                JToken task;
                try { task = await Call(HttpMethod.Get, "tasks/" + Uri.EscapeDataString(taskId), null, "asking how far the task is", t); misses = 0; }
                catch (Problem p) when (p.Transient && misses < MaxMisses)
                {
                    // one lost answer must not throw away a model that is being made (and paid for) at this moment
                    misses++;
                    Log.Warn($"tripo: {p.Message} (asking again, {misses}/{MaxMisses})");
                    continue;
                }
                string status = Text(task["status"]).ToLowerInvariant();
                int percent = Math.Clamp((int)Math.Round(Number(task["progress"]) ?? 0), 0, 100);
                if (status != lastStatus) { Log.Info($"tripo: task {taskId} is {status} ({percent}%) after {sw.Elapsed.TotalSeconds:0} s"); lastStatus = status; }
                switch (status)
                {
                    case "success":
                        output = task["output"] ?? new JObject();
                        cost = Number(task["credits_consumed"]) ?? Number(task["consumed_credit"]);     // the second is the name version 2 used
                        break;
                    // "banned" and "expired" are old names that the documentation says may still appear: both are final
                    case "failed": case "cancelled": case "banned": case "expired":
                        throw new Exception(TaskFailure(status, task, taskId));
                    case "queued":
                        Report(0.03f, "waiting in Tripo's queue");
                        break;
                    case "running":
                        // the task's own 0..100 fills the range 5%..90%; the rest is the download
                        Report(0.05f + 0.85f * percent / 100f, $"making the model {percent}%");
                        break;
                    default:
                        // "unknown" or a state added later: the documentation says not to take it for an end, so we keep
                        // asking until the time limit
                        Report(shown, "waiting for Tripo");
                        break;
                }
            }

            string url = ModelUrl(output);
            if (url == null)
                throw new Exception($"Tripo: the task succeeded but named no model file (it has: {(output is JObject o && o.Count > 0 ? string.Join(", ", o.Properties().Select(p => p.Name)) : "nothing")}) (task {taskId})");
            // at once: the documentation says the address stops working five minutes after the task has finished
            Report(0.92f, "downloading the model");
            byte[] glb = await Download(url, t);
            string what = Inspect(glb);
            Log.Info($"tripo: task {taskId} done in {sw.Elapsed.TotalSeconds:0} s: {glb.Length / 1024} KB from {new Uri(url).Host}, {what}{(cost == null ? "" : ", cost " + cost.Value.ToString("0.##", CultureInfo.InvariantCulture) + " credits")}");
            Report(1f, "done");
            return glb;
        }
        // our own clock ran out (not the caller's cancellation, which travels on as it is)
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new Exception($"Tripo: no model after {Limit.TotalMinutes:0} minutes, gave up waiting" + (taskId.Length > 0 ? $" (task {taskId})" : ""));
        }
    }

    /// <summary>What is asked for: a model that a Quest can draw and that our own GLB reader can open.</summary>
    JObject Request(string prompt, int faceLimit)
    {
        // The options depend on the model family (documentation: "Text to 3D Model" H series and P series pages). A
        // field a model does not know is refused with error 1004, so only what that family documents is sent.
        bool p1 = Model.StartsWith("P1", StringComparison.OrdinalIgnoreCase), p2 = Model.StartsWith("P2", StringComparison.OrdinalIgnoreCase);
        bool v3 = Model.StartsWith("v3", StringComparison.OrdinalIgnoreCase);
        var body = new JObject
        {
            ["prompt"] = prompt,
            ["model"] = Model,          // required in version 3 of the API; there is no default
            // texture = true: the model comes painted (false would be bare grey geometry)
            ["texture"] = true,
            // pbr = false: ONE colour picture instead of four (colour, metallic, roughness, normal). The headset draws
            // the partner's things with a simple storybook material, and the extra pictures would only make the file
            // several times larger. Careful: pbr = true would force texture = true as well.
            ["pbr"] = Pbr,
        };
        // face_limit: the most triangles the mesh may have - what keeps it light enough for a headset. Left out, Tripo
        // decides by itself and gives hundreds of thousands. The low-poly models accept a narrower range than the others.
        if (faceLimit > 0)
            body["face_limit"] = p1 ? Math.Clamp(faceLimit, 50, 20000) : p2 ? Math.Clamp(faceLimit, 48, 50000) : Math.Min(faceLimit, 1500000);
        // texture_quality: "standard" is the default; "detailed" and "extreme" (8K) are bigger than a headset can use
        if (v3 || p1 || p2) body["texture_quality"] = TextureQuality;
        // quad = false: triangles. This matters twice: our reader wants triangles, and quad = true makes Tripo deliver
        // an FBX file instead of a GLB. (P1 has no such field: it is always triangles. v2.5 does not know it either.)
        if (v3 || p2) body["quad"] = false;
        // NOT sent, on purpose:
        //   compress       - its only value, "geometry", packs the mesh with meshopt, which our GLB reader cannot unpack.
        //                    Without the field the file is plain. (The API has no Draco switch on this call.)
        //   smart_low_poly - hand-made-looking low-poly topology, but the documentation warns that it can fail on
        //                    anything complicated; face_limit alone is the dependable way to a small mesh.
        //   auto_size      - would scale the model to its real-world size in metres; the caller sizes it to the desk.
        //   export_orientation - the documentation advises against it; the caller turns the model if it has to.
        // There is no output-format field: a text-to-model task always gives a GLB unless quad is set.
        return body;
    }

    /// <summary>
    /// The address of the finished file. Version 3 documents output.model_url. The other names are what version 2 of
    /// the API used (pbr_model / model / base_model, sometimes as { url }) and what a later version might plausibly
    /// use: accepted so that a renamed field does not lose a finished model. The untextured base model comes last.
    /// </summary>
    static string ModelUrl(JToken output)
    {
        if (output is not JObject) return null;
        foreach (string name in new[] { "model_url", "pbr_model_url", "glb_url", "pbr_model", "model", "glb", "url", "base_model_url", "base_model" })
        {
            JToken v = output[name];
            string url = v is JObject o ? Text(o["url"]) : Text(v);
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)) return url;
        }
        return null;
    }

    static string TaskFailure(string status, JToken task, string taskId)
    {
        // A failed task says why in error_message / error_code. That code is NOT the same list as the request errors
        // (the documentation warns that the same number can mean something else), so it is shown, never interpreted.
        string why = Text(task["error_message"]);
        if (why.Length == 0) why = Text(task["message"]);
        if (NoCredit(why)) return $"Tripo: not enough Tripo credit (task {taskId})";
        string code = Text(task["error_code"]);
        if (why.Length > 200) why = why[..200];
        return $"Tripo: the task {(status == "failed" ? "failed" : "was " + status)}" + (why.Length > 0 ? ": " + why : "") + (code.Length > 0 && code != "0" ? $" (error {code})" : "") + $" (task {taskId})";
    }

    // ------------------------------------------------------------------ the file
    async Task<byte[]> Download(string url, CancellationToken ct)
    {
        // The model has been paid for by now, so a hiccup of the network is worth a second and a third try.
        // The address is never logged or put into a message: it carries a signature that lets anyone fetch the file.
        Exception last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            if (attempt > 1) await Task.Delay(1500, ct);
            try
            {
                using var r = await _plain.GetAsync(url, ct);
                if (r.IsSuccessStatusCode) return await r.Content.ReadAsByteArrayAsync(ct);
                int http = (int)r.StatusCode;
                last = new Exception($"Tripo: downloading the model failed: HTTP {http}");
                if (http is 400 or 401 or 403 or 404 or 410) break;      // the address is wrong or has run out: asking again cannot help
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { last = new Exception($"Tripo: downloading the model took longer than {_plain.Timeout.TotalSeconds:0} s"); }
            catch (HttpRequestException e) { last = new Exception("Tripo: downloading the model failed: " + e.Message); }
            Log.Warn($"tripo: {last.Message} (attempt {attempt}/3)");
        }
        throw last;
    }

    /// <summary>
    /// Looks into the downloaded file before it is handed on: is it a GLB at all, and is its mesh stored plainly?
    /// The request asks for exactly that, but the documentation does not promise what a default GLB contains, and a
    /// Draco- or meshopt-packed mesh would only fail later, in our reader, with a message nobody understands.
    /// Returns a few words for the log.
    /// </summary>
    static string Inspect(byte[] glb)
    {
        const uint Magic = 0x46546C67, Json = 0x4E4F534A;       // "glTF" and "JSON", little endian, as the GLB container writes them
        if (glb == null || glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != Magic)
        {
            string looks = glb != null && glb.Length > 1 && glb[0] == 'P' && glb[1] == 'K' ? " (it looks like a zip archive)"
                         : glb != null && glb.Length > 7 && Encoding.ASCII.GetString(glb, 0, 7) == "Kaydara" ? " (it looks like an FBX file)" : "";
            throw new Exception("Tripo: what was downloaded is not a GLB file" + looks);
        }
        uint length = BitConverter.ToUInt32(glb, 12);
        if (BitConverter.ToUInt32(glb, 16) != Json || 20L + length > glb.Length) throw new Exception("Tripo: the downloaded GLB file is damaged");
        JObject doc;
        try { doc = JObject.Parse(Encoding.UTF8.GetString(glb, 20, (int)length)); }
        catch { throw new Exception("Tripo: the downloaded GLB file is damaged"); }
        string[] required = (doc["extensionsRequired"] as JArray)?.Select(x => x.ToString()).ToArray() ?? Array.Empty<string>();
        string[] used = (doc["extensionsUsed"] as JArray)?.Select(x => x.ToString()).ToArray() ?? Array.Empty<string>();
        // "required" = the file cannot be read without it. Merely "used" means a plain copy of the data is there as well.
        string packed = required.FirstOrDefault(x => x is "KHR_draco_mesh_compression" or "EXT_meshopt_compression");
        if (packed != null) throw new Exception($"Tripo: the model came back with a compressed mesh ({packed}), which this app cannot read");
        if (used.Length > 0) Log.Info("tripo: the GLB uses " + string.Join(", ", used) + (required.Length > 0 ? " and requires " + string.Join(", ", required) : ""));
        return $"{(doc["meshes"] as JArray)?.Count ?? 0} mesh(es), {(doc["images"] as JArray)?.Count ?? 0} picture(s)";
    }

    // ------------------------------------------------------------------ one call
    /// <summary>
    /// One call to the API. Every answer is an envelope { code, data } - code 0 = fine - or, when something is wrong,
    /// { code, message, suggestion } with an HTTP status that says the same thing. Returns "data"; throws a Problem
    /// whose text can be shown to the user (never the key: neither the request nor Tripo's answers contain it).
    /// </summary>
    async Task<JToken> Call(HttpMethod method, string path, JObject body, string what, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
        HttpResponseMessage r;
        try { r = await _http.SendAsync(request, ct); }
        // HttpClient reports its OWN timeout as a cancellation too: that is told apart from the caller's by the token
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new Problem($"Tripo, {what}: no answer within {_http.Timeout.TotalSeconds:0} s", true); }
        catch (HttpRequestException e) { throw new Problem($"Tripo, {what}: the server could not be reached ({e.Message})", true); }
        using (r)
        {
            string text = await r.Content.ReadAsStringAsync(ct);
            JObject reply = null;
            try { reply = JObject.Parse(text); } catch { }
            int http = (int)r.StatusCode;
            int code = (int)(Number(reply?["code"]) ?? 0);
            if (r.IsSuccessStatusCode && reply != null && code == 0) return reply["data"] ?? new JObject();

            string message = Text(reply?["message"]);
            if (message.Length == 0) message = reply?["error"] is JObject err ? Text(err["message"]) : Text(reply?["error"]);
            if (message.Length == 0) message = reply == null ? text.Trim() : "";
            if (message.Length > 200) message = message[..200];
            // 2010 is the documented code (with HTTP 403) for "You don't have enough credit to create this task"; the
            // wording is checked as well in case the number changes
            if (code == 2010 || NoCredit(message)) throw new Problem($"Tripo, {what}: not enough Tripo credit (HTTP {http})", false);
            // 429 = too many questions or too many tasks at once, 5xx = their side: both may pass. 400 / 401 / 403 / 404
            // will be the same the next time.
            bool transient = http == 429 || http >= 500;
            throw new Problem($"Tripo, {what}: HTTP {http}{(message.Length > 0 ? " " + message : "")}{(code != 0 ? $" (code {code})" : "")}", transient);
        }
    }

    static bool NoCredit(string message)
    {
        string m = (message ?? "").ToLowerInvariant();
        return m.Contains("enough credit") || (m.Contains("insufficient") && (m.Contains("credit") || m.Contains("balance")));
    }

    /// <summary>A value of the answer as text; "" when it is missing, null, or a whole object or list.</summary>
    static string Text(JToken t) => t is JValue v && v.Value != null ? Convert.ToString(v.Value, CultureInfo.InvariantCulture).Trim() : "";

    /// <summary>A number of the answer, whether it was sent as a number or as text; null when there is none.</summary>
    static double? Number(JToken t) => double.TryParse(Text(t), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
}
