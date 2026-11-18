using System.Diagnostics;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>The game author uses the Anthropic Messages API. It only runs on the companion host.</summary>
public sealed class ClaudeGameClient
{
    public const string Model = "claude-fable-5-2";
    readonly HttpClient http;

    public ClaudeGameClient(string key, HttpMessageHandler handler = null)
    {
        http = handler == null ? new HttpClient() : new HttpClient(handler, false);
        http.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        http.Timeout = TimeSpan.FromMinutes(5);
        http.DefaultRequestHeaders.Add("x-api-key", key);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public static ClaudeGameClient Open()
    {
        string key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")?.Trim();
        if (string.IsNullOrEmpty(key) && OperatingSystem.IsMacOS())
        {
            var info = new ProcessStartInfo("/usr/bin/security") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "find-generic-password", "-s", "LivingPartner.Anthropic", "-w" }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            if (process != null && process.WaitForExit(2000) && process.ExitCode == 0) key = process.StandardOutput.ReadToEnd().Trim();
            else if (process is { HasExited: false }) process.Kill();
        }
        return string.IsNullOrEmpty(key) ? null : new ClaudeGameClient(key);
    }

    internal async Task<JObject> MessageAsync(string system, JArray messages, int maxTokens, string effort, JArray tools, JObject schema, CancellationToken ct)
    {
        var config = new JObject { ["effort"] = effort is "low" or "high" or "xhigh" or "max" ? effort : "medium" };
        if (schema != null) config["format"] = new JObject { ["type"] = "json_schema", ["schema"] = schema };
        var body = new JObject { ["model"] = Model, ["system"] = system, ["messages"] = messages, ["max_tokens"] = maxTokens, ["output_config"] = config };
        if (tools != null) body["tools"] = tools;
        using var payload = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("messages", payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            string issue = response.StatusCode switch {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound => "Anthropic API 키와 Claude Fable 5.2 모델 접근 권한을 확인해 줘.",
                HttpStatusCode.TooManyRequests => "Anthropic 요청 한도에 도달했어. 잠시 뒤 다시 시도해 줘.",
                _ => "Claude 게임 제작 요청을 마치지 못했어."
            };
            throw new InvalidOperationException($"{issue} (HTTP {(int)response.StatusCode})");
        }
        var reply = JObject.Parse(await response.Content.ReadAsStringAsync(ct));
        if ((string)reply["stop_reason"] == "refusal") throw new InvalidOperationException("Claude가 이 게임 제작 요청을 거절했어.");
        if ((string)reply["stop_reason"] == "max_tokens") throw new InvalidOperationException("Claude의 게임 응답이 길이 한도에서 끊겼어.");
        if (reply["content"] is not JArray) throw new InvalidOperationException("Claude에서 게임 답변을 받지 못했어.");
        return reply;
    }

    public async Task<JObject> AskJsonAsync(string system, string prompt, JObject schema, int maxTokens, CancellationToken ct, string effort = "medium")
    {
        var reply = await MessageAsync(system, new JArray(new JObject { ["role"] = "user", ["content"] = prompt }), maxTokens, effort, null, schema, ct);
        string text = string.Concat(reply["content"].Where(b => (string)b["type"] == "text").Select(b => (string)b["text"]));
        return JObject.Parse(text);
    }
}
