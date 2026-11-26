using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Ai;
using OutfitFinder.Core.Entities;

namespace OutfitFinder.Infrastructure.Ai;

/// <summary>
/// Canned results for local UI work and automated UX tests. Chosen explicitly with AI:Provider=Demo and
/// refused at startup outside the Development environment. The site shows a "Demo data" banner while active.
/// </summary>
public sealed class DemoAnalyzer : IOutfitAnalyzer
{
    public AiProviderKind Kind => AiProviderKind.Demo;

    public async Task<AnalyzerResult<OutfitAnalysis>> AnalyzeAsync(byte[] jpeg, AiRuntimeSettings settings, CancellationToken ct)
    {
        await Task.Delay(2500, ct); // let the loading steps show
        var a = new OutfitAnalysis
        {
            Faces = [new() { X = 0.40, Y = 0.08, W = 0.20, H = 0.14 }],
            Items =
            [
                Item("Eyewear", "Rectangular sunglasses", "Black acetate frame, dark lens", 0.42, 0.12, 0.16, 0.05, false, "other",
                    ("Sample Store F", "Slim rectangle sunglasses", 38m, false), ("Sample Store B", "Square acetate shades", 65m, false), ("Sample Store G", "Classic black frames", 24m, false)),
                Item("Top", "Linen camp shirt", "Navy, short sleeve, open collar", 0.30, 0.27, 0.40, 0.30, true, "tshirt",
                    ("Sample Store A", "Linen camp-collar shirt", 68m, true), ("Sample Store B", "Relaxed linen shirt", 45m, false), ("Sample Store C", "Resort collar shirt", 89m, false)),
                Item("Bottoms", "Pleated trousers", "Stone, wide leg, single pleat", 0.33, 0.57, 0.34, 0.33, true, "trousers",
                    ("Sample Store D", "Single-pleat wide trouser", 110m, false), ("Sample Store A", "Pleated chino", 79m, false), ("Sample Store E", "Wide-leg cotton trouser", 135m, false)),
                Item("Shoes", "Leather loafers", "Brown, penny strap, low heel", 0.30, 0.90, 0.40, 0.07, false, "other",
                    ("Sample Store H", "Penny loafer, brown", 120m, true), ("Sample Store D", "Soft leather loafer", 95m, false), ("Sample Store F", "Classic slip-on", 70m, false)),
                Item("Bag", "Canvas tote bag", "Coral, short handles", 0.08, 0.48, 0.18, 0.18, false, "other",
                    ("Sample Store C", "Everyday canvas tote", 32m, false), ("Sample Store G", "Heavy cotton tote", 28m, false), ("Sample Store A", "Colour-block tote", 48m, false)),
            ],
        };
        return new(a, "Demo", "demo", 0m, TimeSpan.FromSeconds(2.5));
    }

    public async Task<AnalyzerResult<ItemSearchResult>> FindProductsAsync(string category, string description, byte[]? cropJpeg, AiRuntimeSettings settings, CancellationToken ct)
    {
        await Task.Delay(800, ct);
        var q = Uri.EscapeDataString(description);
        return new(new ItemSearchResult
        {
            Matches =
            [
                new() { Store = "Sample Store A", Title = description, Price = 12m, Currency = "USD", Url = $"https://www.google.com/search?tbm=shop&q={q}", MatchType = "similar" },
                new() { Store = "Sample Store B", Title = description + " (alt)", Price = 15m, Currency = "USD", Url = $"https://www.bing.com/shop?q={q}", MatchType = "similar" },
            ],
        }, "Demo", "demo", 0m, TimeSpan.FromSeconds(0.8));
    }

    private static AnalyzedItem Item(string category, string name, string desc, double x, double y, double w, double h, bool makeable, string garment,
        params (string Store, string Title, decimal Price, bool Exact)[] matches) => new()
    {
        Category = category, Name = name, Description = desc, Confidence = 0.9,
        Box = new() { X = x, Y = y, W = w, H = h },
        MakeIt = new()
        {
            Makeable = makeable, GarmentType = garment, Fit = "relaxed", Length = "regular", Neckline = garment == "tshirt" ? "open collar" : "",
            Closures = garment == "trousers" ? "elastic waist" : "", FabricSuggestion = garment == "trousers" ? "Cotton twill" : "Mid-weight linen",
            Difficulty = "beginner",
        },
        Matches = matches.Select(m => new ProductMatch
        {
            Store = m.Store, Title = m.Title, Price = m.Price, Currency = "USD", MatchType = m.Exact ? "exact" : "similar",
            Url = $"https://www.google.com/search?tbm=shop&q={Uri.EscapeDataString(m.Title)}",
        }).ToList(),
    };
}
