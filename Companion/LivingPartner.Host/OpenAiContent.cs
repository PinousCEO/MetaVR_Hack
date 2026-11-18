using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

public sealed partial class OpenAiClient
{
    public readonly string ChatModel = Environment.GetEnvironmentVariable("LP_OPENAI_CHAT_MODEL") ?? "gpt-5.4-mini";
    public readonly string SttModel = Environment.GetEnvironmentVariable("LP_OPENAI_STT_MODEL") ?? "gpt-4o-mini-transcribe";
    readonly string _shortClipModel = Environment.GetEnvironmentVariable("LP_OPENAI_STT_MODEL") ?? "gpt-4o-transcribe";
    public readonly string TtsModel = Environment.GetEnvironmentVariable("LP_OPENAI_TTS_MODEL") ?? "gpt-4o-mini-tts";
    public readonly string Voice = Environment.GetEnvironmentVariable("LP_OPENAI_VOICE") ?? "coral";
    public const int TtsSampleRate = 24000;      // "pcm" output of the speech endpoint: 24 kHz, 16-bit, mono, little endian
    /// <summary>Looking at a screen capture, a picture or a PDF (Review.cs).</summary>
    public readonly string VisionModel = Environment.GetEnvironmentVariable("LP_OPENAI_VISION_MODEL") ?? "gpt-5.4";
    /// <summary>Redrawing a screen with the suggested changes applied.</summary>
    public readonly string ImageModel = Environment.GetEnvironmentVariable("LP_OPENAI_IMAGE_MODEL") ?? "gpt-image-2";
    public readonly string ImageQuality = Environment.GetEnvironmentVariable("LP_OPENAI_IMAGE_QUALITY") ?? "medium";

    readonly HttpClient _http, _slow;
    volatile bool _transparentRefused;      // the image model has said that it cannot do see-through backgrounds (GenerateImageAsync)

    readonly Action<string> _warn;

    public OpenAiClient(string key, Action<string> warn = null, HttpMessageHandler handler = null)
    {
        _warn = warn;
        _http = Client(40);
        // reading a long document or drawing a picture takes much longer than a line of conversation
        _slow = Client(300);

        HttpClient Client(int seconds)
        {
            var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(seconds);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return client;
        }
    }

    /// <summary>
    /// One question with pictures and / or a PDF attached, answered as JSON of the given shape.
    /// Attachments: image/* are sent as images, application/pdf as a file (its text and its pages).
    /// </summary>
    public async Task<JObject> AskJsonAsync(string system, string user, IEnumerable<(string mime, byte[] data, string name)> attachments, JObject schema, int maxTokens, CancellationToken ct, string model = null, string effort = "low")
    {
        model ??= VisionModel;
        var content = new JArray { new JObject { ["type"] = "text", ["text"] = user } };
        foreach (var a in attachments)
        {
            string b64 = Convert.ToBase64String(a.data);
            if (a.mime == "application/pdf") content.Add(new JObject { ["type"] = "file", ["file"] = new JObject { ["filename"] = a.name, ["file_data"] = "data:application/pdf;base64," + b64 } });
            else content.Add(new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = $"data:{a.mime};base64,{b64}", ["detail"] = "high" } });
        }
        var body = new JObject
        {
            ["model"] = model,
            ["messages"] = new JArray { new JObject { ["role"] = "system", ["content"] = system }, new JObject { ["role"] = "user", ["content"] = content } },
            ["response_format"] = new JObject { ["type"] = "json_schema", ["json_schema"] = new JObject { ["name"] = "answer", ["strict"] = true, ["schema"] = schema } },
        };
        // (effort: how long the model thinks before it answers - "low" for everything but the sculptor, who has to see a thing in space)
        if (model.StartsWith("gpt-5")) { body["max_completion_tokens"] = maxTokens; body["reasoning_effort"] = effort; }
        else body["max_tokens"] = Math.Min(maxTokens, 4000);
        using var r = await _slow.PostAsync("chat/completions", new StringContent(body.ToString(), Encoding.UTF8, "application/json"), ct);
        if (!r.IsSuccessStatusCode) throw new Exception("looking: " + await Fail(r));
        var reply = JObject.Parse(await r.Content.ReadAsStringAsync(ct));
        string text = reply["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
        if (text.Length == 0) throw new Exception("looking: the model gave no answer (" + (reply["choices"]?[0]?["finish_reason"]?.ToString() ?? "no reason given") + ")");
        try { return JObject.Parse(text); } catch { throw new Exception("looking: the answer was not the expected JSON"); }
    }

    /// <summary>The picture redrawn by the image model as the prompt says. size: 1536x1024, 1024x1536 or 1024x1024. Returns a PNG.</summary>
    public async Task<byte[]> EditImageAsync(byte[] png, string prompt, string size, CancellationToken ct, bool transparent = false)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(ImageModel), "model");
        if (transparent) { form.Add(new StringContent("transparent"), "background"); form.Add(new StringContent("png"), "output_format"); }
        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent(size), "size");
        form.Add(new StringContent(ImageQuality), "quality");
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "image", "screen.png");
        using var r = await _slow.PostAsync("images/edits", form, ct);
        if (!r.IsSuccessStatusCode) throw new Exception("drawing: " + await Fail(r));
        var first = JObject.Parse(await r.Content.ReadAsStringAsync(ct))["data"]?[0];
        string b64 = first?["b64_json"]?.ToString();
        if (!string.IsNullOrEmpty(b64)) return Convert.FromBase64String(b64);
        string url = first?["url"]?.ToString();
        if (!string.IsNullOrEmpty(url)) { using var plain = new HttpClient(); return await plain.GetByteArrayAsync(url, ct); }
        throw new Exception("drawing: no picture came back");
    }

    /// <summary>
    /// A new picture drawn from a description alone (the icons of the partner's things and skills). size as for
    /// EditImageAsync. transparent: asks for a PNG with a see-through background. Not every image model can do that:
    /// when the request is refused for it, it is made once more without, on a plain white background (the caller can
    /// cut that away). Returns the picture file's bytes.
    /// </summary>
    public async Task<byte[]> GenerateImageAsync(string prompt, string size, bool transparent, CancellationToken ct)
    {
        Task<HttpResponseMessage> Ask(string words, bool seeThrough)
        {
            var body = new JObject { ["model"] = ImageModel, ["prompt"] = words, ["size"] = size, ["quality"] = ImageQuality };
            if (seeThrough) { body["background"] = "transparent"; body["output_format"] = "png"; }
            return _slow.PostAsync("images/generations", new StringContent(body.ToString(), Encoding.UTF8, "application/json"), ct);
        }
        // The words have to change with the fields: told "transparent background" without the field, the model PAINTS a
        // grey chequerboard. A white one can be cut away afterwards, as long as nothing of the object is that white.
        string plain = System.Text.RegularExpressions.Regex.Replace(prompt, "transparent background", "plain pure white background (nothing of the object itself is pure white)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // a model that has refused once is not asked again every time: that was a second of waiting per picture
        bool seeThrough = transparent && !_transparentRefused;
        var r = await Ask(seeThrough ? prompt : plain, seeThrough);
        try
        {
            // 400 = the request itself was not accepted (a field this model does not know). Anything else - no credit,
            // too many requests, their server - would fail the same way a second time.
            if (!r.IsSuccessStatusCode && seeThrough && (int)r.StatusCode == 400)
            {
                _warn?.Invoke("drawing: a transparent background was refused (" + await Fail(r) + "); asking again for a plain white one");
                _transparentRefused = true;
                r.Dispose();
                r = await Ask(plain, false);
            }
            if (!r.IsSuccessStatusCode) throw new Exception("drawing: " + await Fail(r));
            var first = JObject.Parse(await r.Content.ReadAsStringAsync(ct))["data"]?[0];
            string b64 = first?["b64_json"]?.ToString();
            if (!string.IsNullOrEmpty(b64)) return Convert.FromBase64String(b64);
            string url = first?["url"]?.ToString();
            if (!string.IsNullOrEmpty(url)) { using var fetch = new HttpClient(); return await fetch.GetByteArrayAsync(url, ct); }
            throw new Exception("drawing: no picture came back");
        }
        finally { r.Dispose(); }
    }

    /// <summary>
    /// (H6) Research on the WEB: one call of the Responses API with its web search tool. Returns what the model wrote, the
    /// pages it cites (the url_citation annotations: these are pages it really opened, not its word for it) and how often
    /// it searched - 0 searches means the text is its memory. Throws when the API does not offer the tool to this key or model.
    /// </summary>
    public async Task<(string text, List<(string title, string url)> sources, int searches)> WebResearchAsync(string system, string user, CancellationToken ct, string model = null, string effort = "medium")
    {
        model ??= VisionModel;
        string refused = "";
        // (the tool has had two names; a request with a name the API does not know is refused with 400 before anything is spent)
        foreach (string tool in new[] { "web_search", "web_search_preview" })
        {
            var body = new JObject
            {
                ["model"] = model, ["instructions"] = system, ["input"] = user,
                ["tools"] = new JArray { new JObject { ["type"] = tool } }, ["max_output_tokens"] = 12000,
            };
            if (model.StartsWith("gpt-5")) body["reasoning"] = new JObject { ["effort"] = effort };
            using var r = await _slow.PostAsync("responses", new StringContent(body.ToString(), Encoding.UTF8, "application/json"), ct);
            if ((int)r.StatusCode == 400 && tool == "web_search") { refused = await Fail(r); continue; }
            if (!r.IsSuccessStatusCode) throw new Exception("research: " + await Fail(r) + (refused.Length > 0 ? " (and before: " + refused + ")" : ""));
            var reply = JObject.Parse(await r.Content.ReadAsStringAsync(ct));
            var text = new StringBuilder(); var sources = new List<(string title, string url)>(); int searches = 0;
            foreach (var item in reply["output"] as JArray ?? new JArray())
            {
                string type = (string)item["type"] ?? "";
                if (type == "web_search_call") searches++;
                if (type != "message") continue;
                foreach (var part in item["content"] as JArray ?? new JArray())
                {
                    if ((string)part["type"] != "output_text") continue;
                    text.Append((string)part["text"] ?? "");
                    foreach (var a in part["annotations"] as JArray ?? new JArray())
                    {
                        string url = (string)a["url"] ?? "";
                        if ((string)a["type"] == "url_citation" && url.Length > 0 && !sources.Any(s => s.url == url)) sources.Add(((string)a["title"] ?? "", url));
                    }
                }
            }
            if (text.Length == 0) throw new Exception("research: the model gave no answer (" + ((string)reply["status"] ?? "no status") + " " + (reply["incomplete_details"]?["reason"]?.ToString() ?? "") + ")");
            return (text.ToString(), sources, searches);
        }
        throw new Exception("research: " + refused);
    }

    static async Task<string> Fail(HttpResponseMessage r)
    {
        string body = await r.Content.ReadAsStringAsync();
        string msg = "";
        try { msg = JObject.Parse(body)["error"]?["message"]?.ToString() ?? ""; } catch { }
        return $"HTTP {(int)r.StatusCode} {(msg.Length > 0 ? msg : body.Length > 200 ? body[..200] : body)}";
    }
}
