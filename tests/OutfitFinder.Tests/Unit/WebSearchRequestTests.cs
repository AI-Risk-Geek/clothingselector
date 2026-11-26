using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Ai;

namespace OutfitFinder.Tests.Unit;

/// <summary>The AI must search the web before answering, or it returns no links or made-up ones ("No shops found").</summary>
public class WebSearchRequestTests
{
    private const string Analysis = """{"flags":{"noPersonOrClothing":false,"appearsToBeMinor":false,"inappropriate":false},"faces":[],"items":[]}""";

    private static string Ok() => new JsonObject
    {
        ["output"] = new JsonArray
        {
            new JsonObject { ["type"] = "web_search_call" },
            new JsonObject { ["type"] = "message", ["content"] = new JsonArray { new JsonObject { ["type"] = "output_text", ["text"] = Analysis } } },
        },
        ["usage"] = new JsonObject { ["input_tokens"] = 1000, ["output_tokens"] = 200 },
    }.ToJsonString();

    private sealed class FakeOpenAi(params (HttpStatusCode Status, string Body)[] replies) : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject());
            var (status, body) = replies[Math.Min(Requests.Count, replies.Length) - 1];
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly AiRuntimeSettings Settings = new(AiProviderKind.OpenAI, "gpt-test", null, null, "test-key", true, 30);

    [Fact]
    public async Task Web_search_is_required_not_optional()
    {
        var fake = new FakeOpenAi((HttpStatusCode.OK, Ok()));
        var analyzer = new OpenAiAnalyzer(new HttpClient(fake), NullLogger<OpenAiAnalyzer>.Instance);

        await analyzer.AnalyzeAsync([1, 2, 3], Settings, default);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("web_search", request["tools"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("required", request["tool_choice"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_model_that_refuses_required_tools_is_retried_with_auto()
    {
        var refusal = """{"error":{"message":"tool_choice 'required' is not supported with this model","param":"tool_choice"}}""";
        var fake = new FakeOpenAi((HttpStatusCode.BadRequest, refusal), (HttpStatusCode.OK, Ok()));
        var analyzer = new OpenAiAnalyzer(new HttpClient(fake), NullLogger<OpenAiAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync([1, 2, 3], Settings, default);

        Assert.Equal(2, fake.Requests.Count);
        Assert.False(fake.Requests[1].ContainsKey("tool_choice"));
        Assert.Equal("web_search", fake.Requests[1]["tools"]![0]!["type"]!.GetValue<string>());
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task Without_web_search_no_tool_is_forced()
    {
        var fake = new FakeOpenAi((HttpStatusCode.OK, Ok()));
        var analyzer = new OpenAiAnalyzer(new HttpClient(fake), NullLogger<OpenAiAnalyzer>.Instance);

        await analyzer.AnalyzeAsync([1, 2, 3], Settings with { WebSearchEnabled = false }, default);

        Assert.False(Assert.Single(fake.Requests).ContainsKey("tool_choice"));
    }

    [Fact]
    public void Instructions_ask_for_tight_face_boxes_and_real_product_pages()
    {
        var text = OutfitFinder.Core.Ai.AiResources.Instructions;
        Assert.DoesNotContain("generously padded", text);
        Assert.Contains("tight bounding box for every human face", text);
        Assert.Contains("never answer from memory", text);
    }
}
