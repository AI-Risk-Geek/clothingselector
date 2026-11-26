using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Ai;
using OutfitFinder.Core.Entities;

namespace OutfitFinder.Infrastructure.Ai;

/// <summary>
/// OpenAI Responses API (also served by Azure AI Foundry at {endpoint}/openai/v1/responses).
/// One call: image input + the hosted web_search tool + strict JSON schema output.
/// </summary>
public abstract class ResponsesApiAnalyzer(HttpClient http, ILogger logger) : IOutfitAnalyzer
{
    public abstract AiProviderKind Kind { get; }
    protected abstract Uri ResponsesUri(AiRuntimeSettings s);
    protected abstract void Authorize(HttpRequestMessage req, AiRuntimeSettings s);
    protected virtual string ModelName(AiRuntimeSettings s) => s.Model;

    public async Task<AnalyzerResult<OutfitAnalysis>> AnalyzeAsync(byte[] jpeg, AiRuntimeSettings settings, CancellationToken ct)
    {
        settings.EnsureConfigured();
        var (json, cost, elapsed) = await CallAsync(settings, AiResources.Instructions,
            "Analyze this outfit photo.", jpeg, "outfit_analysis", AiResources.OutfitSchema, ct);
        var analysis = JsonSerializer.Deserialize<OutfitAnalysis>(json, AnalysisJson.Options)
            ?? throw new AiResponseException("The AI returned an empty result.");
        var errors = AnalysisValidator.Validate(analysis);
        if (errors.Count > 0) logger.LogWarning("AI output had {Count} schema problems (normalised): {Errors}", errors.Count, string.Join("; ", errors.Take(5)));
        AnalysisValidator.Normalise(analysis);
        return new(analysis, Kind.ToString(), ModelName(settings), cost, elapsed);
    }

    public async Task<AnalyzerResult<ItemSearchResult>> FindProductsAsync(string category, string description, byte[]? cropJpeg, AiRuntimeSettings settings, CancellationToken ct)
    {
        settings.EnsureConfigured();
        var (json, cost, elapsed) = await CallAsync(settings, AiResources.ItemSearchInstructions(category, description),
            "Find products for this item.", cropJpeg, "item_search", AiResources.ItemSearchSchema, ct);
        var result = JsonSerializer.Deserialize<ItemSearchResult>(json, AnalysisJson.Options) ?? new ItemSearchResult();
        result.Matches = AnalysisValidator.NormaliseMatches(result.Matches);
        return new(result, Kind.ToString(), ModelName(settings), cost, elapsed);
    }

    private async Task<(string Json, decimal Cost, TimeSpan Elapsed)> CallAsync(
        AiRuntimeSettings s, string instructions, string userText, byte[]? image, string schemaName, string schema, CancellationToken ct)
    {
        var content = new JsonArray { new JsonObject { ["type"] = "input_text", ["text"] = userText } };
        if (image is not null)
            content.Add(new JsonObject { ["type"] = "input_image", ["image_url"] = "data:image/jpeg;base64," + Convert.ToBase64String(image), ["detail"] = "high" });

        var body = new JsonObject
        {
            ["model"] = ModelName(s),
            ["instructions"] = instructions,
            ["input"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = content } },
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = schemaName,
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(schema),
                },
            },
            ["store"] = false,
        };
        if (s.WebSearchEnabled)
        {
            body["tools"] = new JsonArray { new JsonObject { ["type"] = "web_search" } };
            // With "auto" a small model sometimes skips searching and answers from memory: no links, or made-up
            // ones that fail the link check ("No shops found"). "required" makes it search before answering.
            body["tool_choice"] = "required";
        }

        var sw = Stopwatch.StartNew();
        var (status, text) = await SendAsync(body, s, ct);
        if (status == 400 && body.ContainsKey("tool_choice") && text.Contains("tool_choice", StringComparison.OrdinalIgnoreCase))
        {
            // A model or deployment that can't be forced to use a tool: fall back to letting it choose.
            logger.LogWarning("{Provider} model {Model} doesn't accept tool_choice=required; retrying with auto. {Body}", Kind, ModelName(s), Truncate(text, 300));
            body.Remove("tool_choice");
            (status, text) = await SendAsync(body, s, ct);
        }
        if (status is < 200 or > 299)
        {
            logger.LogError("{Provider} returned {Status}: {Body}", Kind, status, Truncate(text, 500));
            throw new AiResponseException($"The AI provider returned an error ({status}).");
        }
        var doc = JsonNode.Parse(text)!;
        var output = ExtractOutputText(doc) ?? throw new AiResponseException("The AI returned no text output.");
        var cost = EstimateCost(doc, s);
        logger.LogInformation("{Provider} {Model} answered in {Ms} ms after {Searches} web searches ({Cost:0.0000} USD)",
            Kind, ModelName(s), (int)sw.ElapsedMilliseconds, CountWebSearches(doc), cost);
        return (output, cost, sw.Elapsed);
    }

    private async Task<(int Status, string Body)> SendAsync(JsonObject body, AiRuntimeSettings s, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ResponsesUri(s))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        Authorize(req, s);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(s.TimeoutSeconds));
        try
        {
            using var res = await http.SendAsync(req, cts.Token);
            return ((int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiResponseException($"The AI took longer than {s.TimeoutSeconds} seconds.");
        }
    }

    public static int CountWebSearches(JsonNode doc) =>
        doc["output"]?.AsArray().Count(o => o?["type"]?.GetValue<string>() == "web_search_call") ?? 0;

    public static string? ExtractOutputText(JsonNode doc)
    {
        if (doc["output_text"]?.GetValue<string>() is { Length: > 0 } direct) return direct;
        foreach (var item in doc["output"]?.AsArray() ?? [])
        {
            if (item?["type"]?.GetValue<string>() != "message") continue;
            foreach (var c in item["content"]?.AsArray() ?? [])
            {
                if (c?["type"]?.GetValue<string>() == "refusal") throw new AiResponseException("The AI declined to analyze this photo.");
                if (c?["type"]?.GetValue<string>() == "output_text") return c["text"]?.GetValue<string>();
            }
        }
        return null;
    }

    /// <summary>Rough cost from token usage and web-search calls. Rates are configurable per model in AI:Pricing.</summary>
    private static decimal EstimateCost(JsonNode doc, AiRuntimeSettings s)
    {
        var usage = doc["usage"];
        var input = usage?["input_tokens"]?.GetValue<long>() ?? 0;
        var output = usage?["output_tokens"]?.GetValue<long>() ?? 0;
        var searches = CountWebSearches(doc);
        var p = AiPricing.For(s.Model);
        return input / 1_000_000m * p.InputPerMillion + output / 1_000_000m * p.OutputPerMillion + searches * p.PerSearch;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}

public sealed class OpenAiAnalyzer(HttpClient http, ILogger<OpenAiAnalyzer> logger) : ResponsesApiAnalyzer(http, logger)
{
    public override AiProviderKind Kind => AiProviderKind.OpenAI;

    protected override Uri ResponsesUri(AiRuntimeSettings s) =>
        new(string.IsNullOrWhiteSpace(s.Endpoint) ? "https://api.openai.com/v1/responses" : s.Endpoint.TrimEnd('/') + "/v1/responses");

    protected override void Authorize(HttpRequestMessage req, AiRuntimeSettings s) =>
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.ApiKey);
}

public sealed class AzureFoundryAnalyzer(HttpClient http, ILogger<AzureFoundryAnalyzer> logger) : ResponsesApiAnalyzer(http, logger)
{
    public override AiProviderKind Kind => AiProviderKind.AzureAiFoundry;

    protected override Uri ResponsesUri(AiRuntimeSettings s) => new(s.Endpoint!.TrimEnd('/') + "/openai/v1/responses");

    protected override void Authorize(HttpRequestMessage req, AiRuntimeSettings s) => req.Headers.Add("api-key", s.ApiKey);

    protected override string ModelName(AiRuntimeSettings s) => string.IsNullOrWhiteSpace(s.Deployment) ? s.Model : s.Deployment;
}

public sealed class AiResponseException(string message) : Exception(message);

public sealed record ModelPrice(decimal InputPerMillion, decimal OutputPerMillion, decimal PerSearch);

public static class AiPricing
{
    /// <summary>Approximate list prices (USD). Unknown models use a conservative default so the budget cap still works.</summary>
    public static ModelPrice For(string model) => model.ToLowerInvariant() switch
    {
        var m when m.Contains("mini") => new(0.25m, 2m, 0.01m),
        var m when m.Contains("gemini") && m.Contains("flash") => new(0.30m, 2.5m, 0.035m),
        var m when m.Contains("gemini") => new(1.25m, 10m, 0.035m),
        _ => new(1.25m, 10m, 0.01m),
    };
}
