namespace OutfitFinder.Core.Entities;

/// <summary>An anonymous browser (website cookie or extension storage). Linked to a user after sign-in.</summary>
public class Visitor
{
    public Guid Id { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string? UserId { get; set; }
}

public class OutfitSearch
{
    public Guid Id { get; set; }
    public Guid VisitorId { get; set; }
    public string? UserId { get; set; }
    /// <summary>Unprocessed upload, kept only until the worker finishes. Never the long-term copy.</summary>
    public string? PendingBlobPath { get; set; }
    /// <summary>The only image kept: faces blurred.</summary>
    public string? BlurredBlobPath { get; set; }
    public string ImageHash { get; set; } = "";
    public string? IpHash { get; set; }
    public SearchStatus Status { get; set; }
    public SearchStage Stage { get; set; }
    public string? Error { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public SearchSource Source { get; set; }
    public DateTime? ClaimedUtc { get; set; }
    public int AttemptCount { get; set; }
    /// <summary>Optimistic concurrency token (MySQL has no rowversion type, so this is a Guid rotated on every claim).</summary>
    public Guid RowVersion { get; set; }
    public decimal CostUsd { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public List<OutfitItem> Items { get; set; } = new();
}

public class OutfitItem
{
    public Guid Id { get; set; }
    public Guid OutfitSearchId { get; set; }
    public int Number { get; set; }
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? BrandHints { get; set; }
    public double BoxX { get; set; }
    public double BoxY { get; set; }
    public double BoxW { get; set; }
    public double BoxH { get; set; }
    public double Confidence { get; set; }
    public bool IsUserAdjusted { get; set; }
    /// <summary>"Make it" fields from the agent, as JSON (garment type, fit, lengths, neckline...).</summary>
    public string? MakeItJson { get; set; }
    public bool Makeable { get; set; }
    public List<ItemMatch> Matches { get; set; } = new();
}

public class ItemMatch
{
    public Guid Id { get; set; }
    public Guid OutfitItemId { get; set; }
    public string Store { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string Url { get; set; } = "";
    public string? ImageUrl { get; set; }
    public MatchKind MatchType { get; set; }
    public int Rank { get; set; }
    public bool LinkVerified { get; set; }
    public MatchFeedback? Feedback { get; set; }
}

public class ModerationEvent
{
    public Guid Id { get; set; }
    public string ImageHash { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid? VisitorId { get; set; }
    public string? UserId { get; set; }
    public string? IpHash { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class ConsentRecord
{
    public Guid Id { get; set; }
    public Guid? VisitorId { get; set; }
    public string? UserId { get; set; }
    public string Document { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime AcceptedUtc { get; set; }
}

public class MeasurementProfile
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public Guid? VisitorId { get; set; }
    public string Name { get; set; } = "";
    public string MeasurementsJson { get; set; } = "{}";
    public DateTime UpdatedUtc { get; set; }
}

public class PatternJob
{
    public Guid Id { get; set; }
    public Guid OutfitItemId { get; set; }
    public string Block { get; set; } = "";
    public string OptionsJson { get; set; } = "{}";
    public string MeasurementsSnapshot { get; set; } = "{}";
    public PatternJobStatus Status { get; set; }
    public string? PdfBlobPath { get; set; }
    public string? A0PdfBlobPath { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<MaterialLine> Materials { get; set; } = new();
}

public class MaterialLine
{
    public Guid Id { get; set; }
    public Guid PatternJobId { get; set; }
    public MaterialKind Kind { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public string? Url { get; set; }
    public decimal? Price { get; set; }
}

public class AiProviderSetting
{
    public Guid Id { get; set; }
    public AiProviderKind Provider { get; set; }
    public string Model { get; set; } = "";
    public string? Deployment { get; set; }
    public string? Endpoint { get; set; }
    public string EncryptedKey { get; set; } = "";
    public string? KeyLast4 { get; set; }
    public bool WebSearchEnabled { get; set; } = true;
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedUtc { get; set; }
}

public class AuditEntry
{
    public Guid Id { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public class ProblemReport
{
    public Guid Id { get; set; }
    public ReportKind Kind { get; set; }
    public Guid? OutfitSearchId { get; set; }
    public Guid? VisitorId { get; set; }
    public string? UserId { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContentUrl { get; set; }
    public string Reason { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
