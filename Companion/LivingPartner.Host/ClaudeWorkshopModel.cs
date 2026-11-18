using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>Hosts the existing workshop tools, playtests, and critic in a native Claude conversation.</summary>
sealed class ClaudeWorkshopModel : IWorkshopModel, IScriptModel
{
    readonly ClaudeGameClient client;
    readonly string effort;
    public string Name => ClaudeGameClient.Model;
    public ClaudeWorkshopModel(ClaudeGameClient client, string effort = "medium") { this.client = client; this.effort = effort; }

    public Task<JObject> AskAsync(string system, string user, JObject schema, int maxTokens, CancellationToken ct, bool repair = false) =>
        client.AskJsonAsync(system,user,schema,maxTokens,ct,repair ? "medium" : effort);
    public Task<JObject> JudgeAsync(string system, string user, JObject schema, int maxTokens, CancellationToken ct) =>
        client.AskJsonAsync(system,user,schema,maxTokens,ct,effort);

    public async Task<JObject> RespondAsync(JObject request, CancellationToken ct)
    {
        var messages = new JArray();
        void User(JObject part)
        {
            if ((string)messages.Last?["role"] != "user") messages.Add(new JObject { ["role"] = "user", ["content"] = new JArray() });
            ((JArray)messages.Last["content"]).Add(part);
        }
        foreach (var item in (JArray)request["input"])
        {
            // Preserve every signed thinking and tool-use block in its original turn. The other
            // projections in the workshop history are for its local tool runner and text reader.
            if ((string)item["type"] == "claude_turn")
                messages.Add(new JObject { ["role"] = "assistant", ["content"] = item["content"] });
            else if ((string)item["type"] == "function_call_output")
                User(new JObject { ["type"] = "tool_result", ["tool_use_id"] = item["call_id"], ["content"] = item["output"] });
            else if ((string)item["role"] == "user")
                foreach (var part in item["content"])
                {
                    if ((string)part["type"] == "input_text") User(new JObject { ["type"] = "text", ["text"] = part["text"] });
                    else if ((string)part["type"] == "input_image")
                    {
                        const string prefix = "data:image/png;base64,";
                        string image = (string)part["image_url"];
                        if (image == null || !image.StartsWith(prefix,StringComparison.Ordinal)) throw new InvalidDataException("Expected a rendered PNG frame.");
                        User(new JObject { ["type"] = "image", ["source"] = new JObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = image[prefix.Length..] } });
                    }
                }
        }
        JArray tools = request["tools"] is JArray source ? new JArray(source.Select(t => new JObject {
            ["name"] = t["name"], ["description"] = t["description"], ["input_schema"] = t["parameters"]
        })) : null;
        var reply = await client.MessageAsync((string)request["instructions"], messages, (int)request["max_output_tokens"],
            (string)request["reasoning"]?["effort"], tools, request["text"]?["format"]?["schema"] as JObject, ct);
        var output = new JArray(new JObject { ["type"] = "claude_turn", ["content"] = reply["content"] });
        foreach (var part in reply["content"])
        {
            if ((string)part["type"] == "tool_use")
                output.Add(new JObject { ["type"] = "function_call", ["call_id"] = part["id"], ["name"] = part["name"], ["arguments"] = part["input"].ToString(Formatting.None) });
            else if ((string)part["type"] == "text")
                output.Add(new JObject { ["type"] = "message", ["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = part["text"] }) });
        }
        return new JObject { ["id"] = reply["id"], ["status"] = reply["stop_reason"], ["usage"] = reply["usage"], ["output"] = output };
    }
}
