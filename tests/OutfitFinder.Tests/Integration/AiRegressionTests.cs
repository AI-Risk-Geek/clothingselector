using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Ai;
using OutfitFinder.Infrastructure.Imaging;
using OutfitFinder.Infrastructure.Safety;

namespace OutfitFinder.Tests.Integration;

/// <summary>
/// Runs tests/photos against the REAL configured AI provider (costs money). Opt in with OF_AI_REGRESSION=1 and
/// AI__Provider / AI__Model / AI__ApiKey (/ AI__Endpoint, AI__Deployment) environment variables or the Api's user-secrets.
/// Checks the Phase 0 bar: ≥ 80% of items found, ≥ 60% of links live, under 2 minutes per photo.
/// </summary>
[Trait("Category", "AiRegression")]
public class AiRegressionTests
{
    [SkippableFact]
    public async Task Photo_set_meets_the_phase_0_bar()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("OF_AI_REGRESSION") == "1", "Set OF_AI_REGRESSION=1 to run against the real AI provider.");
        var config = new ConfigurationBuilder().AddUserSecrets("outfitfinder-api-8f7c2d1e").AddEnvironmentVariables().Build();
        var settings = AiSettingsProvider.FromOptions(config.GetSection("AI").Get<AiOptions>() ?? new AiOptions());
        settings.EnsureConfigured();

        IOutfitAnalyzer analyzer = settings.Provider switch
        {
            AiProviderKind.OpenAI => new OpenAiAnalyzer(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, NullLogger<OpenAiAnalyzer>.Instance),
            AiProviderKind.AzureAiFoundry => new AzureFoundryAnalyzer(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, NullLogger<AzureFoundryAnalyzer>.Instance),
            AiProviderKind.Google => new GeminiAnalyzer(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, NullLogger<GeminiAnalyzer>.Instance),
            _ => throw new InvalidOperationException("Use a real provider."),
        };
        var links = new LinkChecker(new HttpClient(), NullLogger<LinkChecker>.Instance);
        var photos = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "photos"), "*.jpg").Order().ToList();
        Skip.If(photos.Count == 0, "No photos in tests/photos yet.");

        var report = new StringBuilder($"# AI regression: {settings.Provider} {settings.Model} {DateTime.UtcNow:yyyy-MM-dd}\n\n| Photo | Found/expected | Live links | Seconds | Cost USD |\n|---|---|---|---|---|\n");
        int expectedTotal = 0, foundTotal = 0, linksTotal = 0, liveTotal = 0;
        foreach (var photo in photos)
        {
            var expectedFile = Path.ChangeExtension(photo, ".expected.json");
            var expected = File.Exists(expectedFile)
                ? JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(expectedFile)).GetProperty("items").EnumerateArray().Select(e => e.GetString()!).ToList()
                : [];
            var jpeg = new ImageProcessor().Normalise(File.ReadAllBytes(photo)).Jpeg;
            var sw = Stopwatch.StartNew();
            var result = await analyzer.AnalyzeAsync(jpeg, settings, default);
            sw.Stop();
            var urls = result.Value.Items.SelectMany(i => i.Matches).Select(m => m.Url).ToList();
            var live = await links.FindLiveAsync(urls, default);
            var found = expected.Count(e => result.Value.Items.Any(i => i.Category.Contains(e, StringComparison.OrdinalIgnoreCase) || i.Name.Contains(e, StringComparison.OrdinalIgnoreCase)));
            expectedTotal += expected.Count; foundTotal += found; linksTotal += urls.Count; liveTotal += live.Count;
            report.AppendLine($"| {Path.GetFileName(photo)} | {found}/{expected.Count} | {live.Count}/{urls.Count} | {sw.Elapsed.TotalSeconds:0} | {result.CostUsd:0.0000} |");
            Assert.True(sw.Elapsed < TimeSpan.FromMinutes(2), $"{photo} took {sw.Elapsed}");
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "photos", $"results-{settings.Provider}-{DateTime.UtcNow:yyyyMMdd}.md"), report.ToString());
        if (expectedTotal > 0) Assert.True(foundTotal >= 0.8 * expectedTotal, $"Found {foundTotal}/{expectedTotal} items (< 80%)");
        if (linksTotal > 0) Assert.True(liveTotal >= 0.6 * linksTotal, $"{liveTotal}/{linksTotal} live links (< 60%)");
    }
}
