using System.Text.Json;
using System.Text.Json.Nodes;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Ai;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Ai;

namespace OutfitFinder.Tests.Unit;

public class AiTests
{
    [Fact]
    public void Instructions_and_schemas_are_embedded_and_valid_json()
    {
        Assert.Contains("Never construct or guess a URL", AiResources.Instructions);
        Assert.Equal("object", JsonNode.Parse(AiResources.OutfitSchema)!["type"]!.GetValue<string>());
        Assert.NotNull(JsonNode.Parse(AiResources.ItemSearchSchema));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bing")]
    public void Unconfigured_provider_refuses_loudly(string provider)
    {
        var e = Assert.Throws<AiNotConfiguredException>(() => AiSettingsProvider.FromOptions(new AiOptions { Provider = provider, Model = "m", ApiKey = "k" }));
        Assert.Contains("AI:Provider", e.Message);
    }

    [Fact]
    public void Missing_key_or_endpoint_refuses_loudly()
    {
        var s = new AiRuntimeSettings(AiProviderKind.AzureAiFoundry, "gpt", "dep", null, "", true, 60);
        var e = Assert.Throws<AiNotConfiguredException>(s.EnsureConfigured);
        Assert.Contains("AI:ApiKey", e.Message);
        Assert.Contains("AI:Endpoint", e.Message);
    }

    [Fact]
    public void Responses_output_text_is_extracted_and_refusals_throw()
    {
        var ok = JsonNode.Parse("""{"output":[{"type":"web_search_call"},{"type":"message","content":[{"type":"output_text","text":"{\"a\":1}"}]}]}""")!;
        Assert.Equal("{\"a\":1}", ResponsesApiAnalyzer.ExtractOutputText(ok));
        var refusal = JsonNode.Parse("""{"output":[{"type":"message","content":[{"type":"refusal","refusal":"no"}]}]}""")!;
        Assert.Throws<AiResponseException>(() => ResponsesApiAnalyzer.ExtractOutputText(refusal));
    }

    [Fact]
    public void Model_output_is_validated_and_normalised()
    {
        var json = """
        {"flags":{"noPersonOrClothing":false,"appearsToBeMinor":false,"inappropriate":false},
         "faces":[{"x":0.4,"y":0.05,"w":0.2,"h":0.2}],
         "items":[{"category":"Top","name":"Shirt","description":"Blue","brandHints":null,"box":{"x":0.9,"y":0.2,"w":0.3,"h":0.3},"confidence":0.9,
           "makeIt":{"makeable":true,"garmentType":"other","fit":"","length":"","neckline":"","closures":"","fabricSuggestion":"","difficulty":"beginner"},
           "matches":[{"store":"A","title":"t","price":10,"currency":"usd","url":"https://shop.test/1","imageUrl":"javascript:alert(1)","matchType":"exact"},
                      {"store":"B","title":"t","price":-1,"currency":"DOLLARS","url":"ftp://bad","imageUrl":null,"matchType":"weird"}]}]}
        """;
        var a = JsonSerializer.Deserialize<OutfitAnalysis>(json, AnalysisJson.Options)!;
        Assert.NotEmpty(AnalysisValidator.Validate(a));
        AnalysisValidator.Normalise(a);
        var item = a.Items.Single();
        Assert.True(item.Box.X + item.Box.W <= 1.0000001);         // clamped into the image
        Assert.False(item.MakeIt.Makeable);                          // "other" is never makeable
        var m = Assert.Single(item.Matches);                         // ftp:// link dropped
        Assert.Null(m.ImageUrl);                                     // javascript: image dropped
    }

    [Fact]
    public async Task Demo_analyzer_returns_the_prototype_look()
    {
        var r = await new DemoAnalyzer().AnalyzeAsync([], new AiRuntimeSettings(AiProviderKind.Demo, "", null, null, "", true, 10), default);
        Assert.Equal(5, r.Value.Items.Count);
        Assert.Single(r.Value.Faces);
        Assert.Empty(AnalysisValidator.Validate(r.Value));
    }
}
