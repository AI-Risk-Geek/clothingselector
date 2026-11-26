using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Ai;
using OutfitFinder.Core.Entities;

namespace OutfitFinder.Infrastructure.Ai;

/// <summary>
/// Google Gemini. Grounding with Google Search cannot be combined with a JSON response schema in one call,
/// so this adapter uses the plan's two-call fallback: (1) search-grounded analysis as text, (2) structure into JSON.
/// </summary>
public sealed class GeminiAnalyzer(HttpClient http, ILogger<GeminiAnalyzer> logger) : IOutfitAnalyzer
{
    public AiProviderKind Kind => AiProviderKind.Google;

    public async Task<AnalyzerResult<OutfitAnalysis>> AnalyzeAsync(byte[] jpeg, AiRuntimeSettings s, CancellationToken ct)
    {
        s.EnsureConfigured();
        var sw = Stopwatch.StartNew();
        var (json, cost) = await TwoCallAsync(s, AiResources.Instructions, "Analyze this outfit photo.", jpeg, AiResources.OutfitSchema, ct);
        var analysis = JsonSerializer.Deserialize<OutfitAnalysis>(json, AnalysisJson.Options) ?? throw new AiResponseException("The AI returned an empty result.");
        var errors = AnalysisValidator.Validate(analysis);
        if (errors.Count > 0) logger.LogWarning("Gemini output had {Count} schema problems (normalised)", errors.Count);
        AnalysisValidator.Normalise(analysis);
        return new(analysis, Kind.ToString(), s.Model, cost, sw.Elapsed);
    }

    public async Task<AnalyzerResult<ItemSearchResult>> FindProductsAsync(string category, string description, byte[]? cropJpeg, AiRuntimeSettings s, CancellationToken ct)
    {
        s.EnsureConfigured();
        var sw = Stopwatch.StartNew();
        var (json, cost) = await TwoCallAsync(s, AiResources.ItemSearchInstructions(category, description), "Find products for this item.", cropJpeg, AiResources.ItemSearchSchema, ct);
        var result = JsonSerializer.Deserialize<ItemSearchResult>(json, AnalysisJson.Options) ?? new();
        result.Matches = AnalysisValidator.NormaliseMatches(result.Matches);
        return new(result, Kind.ToString(), s.Model, cost, sw.Elapsed);
    }

    private async Task<(string Json, decimal Cost)> TwoCallAsync(AiRuntimeSettings s, string instructions, string userText, byte[]? image, string schema, CancellationToken ct)
    {
        var parts = new JsonArray { new JsonObject { ["text"] = userText } };
        if (image is not null)
            parts.Add(new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = "image/jpeg", ["data"] = Convert.ToBase64String(image) } });

        // Call 1: grounded search, free text.
        var first = new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = instructions + "\nWrite your findings as detailed notes including every URL you found." } } },
            ["contents"] = new JsonArray { new JsonObject { ["role"] = "user", ["parts"] = parts } },
        };
        if (s.WebSearchEnabled) first["tools"] = new JsonArray { new JsonObject { ["google_search"] = new JsonObject() } };
        var (notes, c1) = await PostAsync(s, first, ct);

        // Call 2: structure the notes into the schema. No tools; JSON only.
        var second = new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = "Convert the notes into JSON matching the schema. Do not invent URLs; only use URLs present in the notes." } } },
            ["contents"] = new JsonArray { new JsonObject { ["role"] = "user", ["parts"] = new JsonArray { new JsonObject { ["text"] = notes } } } },
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseJsonSchema"] = JsonNode.Parse(schema),
            },
        };
        var (json, c2) = await PostAsync(s, second, ct);
        return (json, c1 + c2);
    }

    private async Task<(string Text, decimal Cost)> PostAsync(AiRuntimeSettings s, JsonObject body, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(s.Endpoint) ? "https://generativelanguage.googleapis.com" : s.Endpoint.TrimEnd('/');
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1beta/models/{Uri.EscapeDataString(s.Model)}:generateContent")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-goog-api-key", s.ApiKey);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(s.TimeoutSeconds));
        HttpResponseMessage res;
        try { res = await http.SendAsync(req, cts.Token); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new AiResponseException($"The AI took longer than {s.TimeoutSeconds} seconds."); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogError("Gemini returned {Status}: {Body}", (int)res.StatusCode, text.Length > 500 ? text[..500] : text);
                throw new AiResponseException($"The AI provider returned an error ({(int)res.StatusCode}).");
            }
            var doc = JsonNode.Parse(text)!;
            var sb = new StringBuilder();
            foreach (var p in doc["candidates"]?[0]?["content"]?["parts"]?.AsArray() ?? []) sb.Append(p?["text"]?.GetValue<string>());
            if (sb.Length == 0) throw new AiResponseException("The AI returned no text output.");
            var usage = doc["usageMetadata"];
            var price = AiPricing.For(s.Model);
            var cost = (usage?["promptTokenCount"]?.GetValue<long>() ?? 0) / 1_000_000m * price.InputPerMillion
                     + (usage?["candidatesTokenCount"]?.GetValue<long>() ?? 0) / 1_000_000m * price.OutputPerMillion
                     + (body["tools"] is null ? 0 : price.PerSearch);
            return (sb.ToString(), cost);
        }
    }
}
