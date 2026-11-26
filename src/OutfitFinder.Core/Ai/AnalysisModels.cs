using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutfitFinder.Core.Ai;

public sealed class OutfitAnalysis
{
    public AnalysisFlags Flags { get; set; } = new();
    public List<NormalizedBox> Faces { get; set; } = new();
    public List<AnalyzedItem> Items { get; set; } = new();
}

public sealed class AnalysisFlags
{
    public bool NoPersonOrClothing { get; set; }
    public bool AppearsToBeMinor { get; set; }
    public bool Inappropriate { get; set; }
}

public sealed class NormalizedBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

public sealed class AnalyzedItem
{
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? BrandHints { get; set; }
    public NormalizedBox Box { get; set; } = new();
    public double Confidence { get; set; }
    public MakeItInfo MakeIt { get; set; } = new();
    public List<ProductMatch> Matches { get; set; } = new();
}

public sealed class MakeItInfo
{
    public bool Makeable { get; set; }
    public string GarmentType { get; set; } = "other";
    public string Fit { get; set; } = "";
    public string Length { get; set; } = "";
    public string Neckline { get; set; } = "";
    public string Closures { get; set; } = "";
    public string FabricSuggestion { get; set; } = "";
    public string Difficulty { get; set; } = "beginner";
}

public sealed class ProductMatch
{
    public string Store { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string Url { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string MatchType { get; set; } = "similar";
}

public sealed class ItemSearchResult
{
    public List<ProductMatch> Matches { get; set; } = new();
}

/// <summary>What one analyzer call produced, plus accounting.</summary>
public sealed record AnalyzerResult<T>(T Value, string Provider, string Model, decimal CostUsd, TimeSpan Duration);

public static class AnalysisJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
