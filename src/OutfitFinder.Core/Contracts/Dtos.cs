using OutfitFinder.Core.Entities;

namespace OutfitFinder.Core.Contracts;

public static class Headers
{
    public const string VisitorId = "X-Visitor-Id";
    public const string Turnstile = "X-Turnstile-Token";
}

public sealed record CreatedSearchDto(Guid Id);

public sealed record SearchFromUrlRequest(string Url, string? PageUrl);

/// <param name="Title">The look's main garment (e.g. "Linen camp shirt"), so looks of similar photos can be told apart.</param>
public sealed record SearchSummaryDto(Guid Id, SearchStatus Status, int ItemCount, string? ImageUrl, DateTime CreatedUtc, string? Title = null)
{
    /// <summary>Garments name a look better than accessories: a dress or top first, sunglasses last.</summary>
    public static string? PickTitle(IEnumerable<(string Name, string Category)> items)
    {
        string[] order = ["Dress", "Top", "Outerwear", "Bottoms", "Shoes", "Bag"];
        return items.OrderBy(i => Array.IndexOf(order, i.Category) is var r && r >= 0 ? r : order.Length).Select(i => i.Name).FirstOrDefault();
    }
}

public sealed record SearchDto(
    Guid Id,
    SearchStatus Status,
    SearchStage Stage,
    string? Error,
    string? ImageUrl,
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    IReadOnlyList<ItemDto> Items)
{
    public bool IsFinished => Status is SearchStatus.Completed or SearchStatus.Failed or SearchStatus.Rejected;

    /// <summary>Cheapest and dearest whole-look totals, using the cheapest / dearest priced match per item.</summary>
    public (decimal Min, decimal Max, string Currency)? PriceRange()
    {
        var priced = Items
            .Select(i => i.Matches.Where(m => m.Price is not null).ToList())
            .Where(l => l.Count > 0).ToList();
        if (priced.Count == 0) return null;
        var currency = priced.SelectMany(l => l).GroupBy(m => m.Currency ?? "USD").OrderByDescending(g => g.Count()).First().Key;
        var same = priced.Select(l => l.Where(m => (m.Currency ?? "USD") == currency).ToList()).Where(l => l.Count > 0).ToList();
        return (same.Sum(l => l.Min(m => m.Price!.Value)), same.Sum(l => l.Max(m => m.Price!.Value)), currency);
    }
}

public sealed record ItemDto(
    Guid Id,
    int Number,
    string Category,
    string Name,
    string Description,
    string? BrandHints,
    BoxDto Box,
    bool IsUserAdjusted,
    bool Makeable,
    IReadOnlyList<MatchDto> Matches);

public sealed record BoxDto(double X, double Y, double W, double H)
{
    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;
}

public sealed record MatchDto(
    Guid Id,
    string Store,
    string Title,
    decimal? Price,
    string? Currency,
    string Url,
    string? ImageUrl,
    MatchKind MatchType,
    int Rank,
    MatchFeedback? Feedback);

public sealed record FeedbackRequest(MatchFeedback? Feedback);

/// <summary>Fix-a-box: the full list of items the user wants to keep, with any moved or new boxes.</summary>
public sealed record AdjustItemsRequest(IReadOnlyList<AdjustedItem> Items);
public sealed record AdjustedItem(Guid? Id, string? Category, BoxDto Box);

public sealed record ReportRequest(Guid? SearchId, string Reason, string Details, string? ContactEmail, string? ContentUrl);

public sealed record ConsentRequest(string Document, string Version);
public sealed record ConsentStatusDto(IReadOnlyList<LegalDocumentVersion> Outstanding);
public sealed record LegalDocumentVersion(string Document, string Version, DateOnly Effective);

public sealed record MeasurementProfileDto(Guid? Id, string Name, Dictionary<string, double> Measurements);

public sealed record PatternRequest(string? Size, Guid? MeasurementProfileId, Dictionary<string, double>? Measurements);

public sealed record PatternDto(
    Guid Id,
    string Block,
    string GarmentName,
    string SizeLabel,
    IReadOnlyList<PatternPieceDto> Pieces,
    IReadOnlyList<MaterialDto> Materials,
    IReadOnlyList<string> Steps,
    string Difficulty,
    string TiledLetterUrl,
    string TiledA4Url,
    string CopyShopUrl);

public sealed record PatternPieceDto(string Name, int Cut, bool OnFold, string PreviewSvg);

public sealed record MaterialDto(Guid Id, MaterialKind Kind, string Description, decimal Quantity, string Unit, string? Url, decimal? Price);

public sealed record FindMaterialResult(IReadOnlyList<MatchDto> Matches);

public sealed record AiSettingsDto(
    AiProviderKind Provider,
    string Model,
    string? Deployment,
    string? Endpoint,
    string? KeyLast4,
    bool WebSearchEnabled,
    string? UpdatedBy,
    DateTime? UpdatedUtc,
    string Source);

public sealed record AiSettingsUpdate(
    AiProviderKind Provider,
    string Model,
    string? Deployment,
    string? Endpoint,
    string? ApiKey,
    bool WebSearchEnabled);

public sealed record AiTestResult(bool Ok, string Message, int Items, int WorkingLinks, int TotalLinks, double Seconds, decimal CostUsd);

public sealed record AuditEntryDto(string Actor, string Action, string Details, DateTime CreatedUtc);

public sealed record ApiError(string Message);

public sealed record ExtensionRelease(string Version, string DownloadUrl, string? WebStoreUrl, DateTime PublishedUtc);
