using OutfitFinder.Core.Entities;

namespace OutfitFinder.Infrastructure.Patterns;

public sealed record MaterialEstimate(MaterialKind Kind, string Description, decimal Quantity, string Unit);

/// <summary>Fabric yardage computed from the actual cut pieces, plus the garment's notions.</summary>
public static class MaterialsCalculator
{
    public const double Width45In = 114.3, Width60In = 152.4;
    private const double LayoutEfficiency = 0.72; // share of the fabric rectangle the pieces actually use
    private const double ShrinkageAllowanceCm = 10;

    public static double TotalCutArea(DraftedPattern p) =>
        p.Pieces.Sum(piece => Polygon.Area(piece.CutLine) * (piece.OnFold ? 2 : piece.Cut));

    /// <summary>Length of fabric needed (cm) for a given fabric width.</summary>
    public static double FabricLengthCm(DraftedPattern p, double fabricWidthCm)
    {
        var byArea = TotalCutArea(p) / (fabricWidthCm * LayoutEfficiency);
        var longest = p.Pieces.Max(piece => { var b = Polygon.Bounds(piece.CutLine); return b.MaxY - b.MinY; });
        // Pieces wider than half the fabric (e.g. long casings) lie lengthwise.
        var widest = p.Pieces.Max(piece => { var b = Polygon.Bounds(piece.CutLine); return b.MaxX - b.MinX; });
        var minimum = Math.Max(longest, widest > fabricWidthCm ? widest : 0);
        return Math.Max(byArea, minimum) + ShrinkageAllowanceCm;
    }

    public static decimal ToYardsEighths(double cm) => Math.Ceiling((decimal)(cm / 91.44) * 8) / 8;

    public static IReadOnlyList<MaterialEstimate> Compute(DraftedPattern p, string? fabricSuggestion)
    {
        // The block decides the fabric family (a T-shirt block needs stretch knit even if the photo shows linen).
        // The AI's suggestion is only used where it fits the block.
        var knitBlock = p.Block == "tshirt";
        var fabric = string.IsNullOrWhiteSpace(fabricSuggestion) || knitBlock ? p.FabricKind : $"{fabricSuggestion}, or another {p.FabricKind.ToLowerInvariant()}";
        var list = new List<MaterialEstimate>
        {
            new(MaterialKind.Fabric, $"{fabric}, 60 in / 150 cm wide", ToYardsEighths(FabricLengthCm(p, Width60In)), "yd"),
            new(MaterialKind.Fabric, $"or {char.ToLowerInvariant(fabric[0])}{fabric[1..]}, 45 in / 115 cm wide", ToYardsEighths(FabricLengthCm(p, Width45In)), "yd"),
        };
        list.AddRange(p.Notions.Select(n => new MaterialEstimate(n.Kind, n.Description, n.Quantity, n.Unit)));
        return list;
    }
}
