namespace OutfitFinder.Core.Ai;

/// <summary>Instructions and JSON schemas live in this repo and are sent with every call.</summary>
public static class AiResources
{
    public static string Instructions { get; } = Read("instructions.md");
    public static string OutfitSchema { get; } = Read("outfit-analysis.schema.json");
    public static string ItemSearchSchema { get; } = Read("item-search.schema.json");

    public static string ItemSearchInstructions(string category, string description) =>
        "Use web search to find up to 5 products currently for sale from reputable online stores that match this item.\n" +
        $"Category: {category}\nDescription: {description}\n" +
        "Mark \"exact\" only when brand AND style are confirmed by the product page; else \"similar\".\n" +
        "Only return URLs that appeared in your search results. Never construct or guess a URL.\n" +
        "Prices as numbers with ISO currency code. Skip out-of-stock products.\n" +
        "Respond only with JSON matching the provided schema.";

    private static string Read(string name)
    {
        using var s = typeof(AiResources).Assembly.GetManifestResourceStream($"OutfitFinder.Core.Ai.{name}")
            ?? throw new InvalidOperationException($"Missing embedded resource {name}");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
