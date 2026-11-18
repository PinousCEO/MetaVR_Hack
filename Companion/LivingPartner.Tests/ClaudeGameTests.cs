using System.Net;
using System.Text;
using LivingPartner.Host;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

public partial class WorkshopTests
{
    sealed class ClaudeTransport : HttpMessageHandler
    {
        public readonly List<JObject> Requests = new();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Func<JObject,int,JArray> Answer;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("https://api.anthropic.com/v1/messages",request.RequestUri.AbsoluteUri);
            Assert.Equal("test-credential",Assert.Single(request.Headers.GetValues("x-api-key")));
            Assert.Equal("2023-06-01",Assert.Single(request.Headers.GetValues("anthropic-version")));
            Assert.Null(request.Headers.Authorization);
            var body = JObject.Parse(await request.Content.ReadAsStringAsync(ct));
            Assert.Equal("claude-fable-5-2",(string)body["model"]);
            Assert.Null(body["tool_choice"]); Assert.Null(body["temperature"]); Assert.Null(body["previous_response_id"]);
            Requests.Add(body);
            var response = new JObject { ["id"] = "m" + Requests.Count, ["content"] = Answer?.Invoke(body,Requests.Count) ?? new JArray(),
                ["usage"] = new JObject { ["input_tokens"] = 70, ["output_tokens"] = 20 }, ["stop_reason"] = body["tools"] == null ? "end_turn" : "tool_use" };
            return new HttpResponseMessage(Status) { Content = new StringContent(response.ToString(),Encoding.UTF8,"application/json") };
        }
    }
    [Fact]
    public async Task Claude_workshop_writes_and_reviews_a_game_with_signed_tool_history_and_rendered_images()
    {
        var transport = new ClaudeTransport();
        JArray first = null;
        transport.Answer = (body,index) =>
        {
            if (body["tools"] == null)
            {
                Assert.Equal("json_schema", (string)body["output_config"]["format"]["type"]);
                Assert.Contains(body["messages"].SelectTokens("$..source.media_type"), t => (string)t == "image/png");
                return new JArray(new JObject { ["type"]="text", ["text"]=FakeModel.Review().ToString() });
            }
            Assert.Contains(body["tools"], t => (string)t["name"]=="write" && t["input_schema"]?["properties"]?["script"]!=null);
            var (name,args) = index == 1 ? Write(Dots) : Done();
            if (index > 1)
            {
                Assert.True(JToken.DeepEquals(first,body["messages"][1]["content"]));
                Assert.Equal("tool_result",(string)body["messages"][2]["content"][0]["type"]);
                Assert.Equal("use1",(string)body["messages"][2]["content"][0]["tool_use_id"]);
            }
            var blocks = new JArray(new JObject { ["type"]="thinking",["thinking"]="test only",["signature"]="fixture-signature" },
                new JObject { ["type"]="tool_use",["id"]="use"+index,["name"]=name,["input"]=args });
            first ??= (JArray)blocks.DeepClone(); return blocks;
        };
        var model = new ClaudeWorkshopModel(new ClaudeGameClient("test-credential",transport));
        var result = await Workshop.MakeAsync(model,Request(),Options(),null,_=>{},CancellationToken.None);
        Assert.True(result.ok,result.why);
        Assert.Equal(2,result.turns); Assert.Equal(1,result.criticCalls); Assert.Equal(Dots.Trim(),result.script.Trim());
        Assert.Equal(3,transport.Requests.Count);
        Assert.Equal(ClaudeGameClient.Model,result.model);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    public async Task Claude_game_author_reports_provider_failure_after_one_request(int status)
    {
        var transport = new ClaudeTransport { Status=(HttpStatusCode)status };
        var client = new ClaudeGameClient("test-credential",transport);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(()=>client.AskJsonAsync("game","make a game",new JObject(),12000,CancellationToken.None));
        Assert.Contains(status.ToString(),error.Message); Assert.DoesNotContain("test-credential",error.Message); Assert.Single(transport.Requests);
    }
}
