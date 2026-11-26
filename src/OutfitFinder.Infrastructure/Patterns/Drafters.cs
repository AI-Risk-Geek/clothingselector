namespace OutfitFinder.Infrastructure.Patterns;

public sealed class PatternPiece
{
    public required string Name { get; init; }
    public required int Cut { get; init; }
    public required bool OnFold { get; init; }
    /// <summary>The stitching line (no allowance).</summary>
    public required IReadOnlyList<Pt> Seam { get; init; }
    /// <summary>The cutting line (seam line plus allowance; 0 on the fold).</summary>
    public required IReadOnlyList<Pt> CutLine { get; init; }
    public required (Pt A, Pt B) Grain { get; init; }
    public IReadOnlyList<Pt> Notches { get; init; } = [];
    /// <summary>Extra annotations (e.g. a measurement) to print on the piece.</summary>
    public IReadOnlyDictionary<string, double> Dimensions { get; init; } = new Dictionary<string, double>();

    public static PatternPiece Build(string name, int cut, bool onFold, (IReadOnlyList<Pt> Seam, IReadOnlyList<double> Allowances) path,
        (Pt, Pt) grain, IReadOnlyList<Pt>? notches = null, IReadOnlyDictionary<string, double>? dims = null) => new()
    {
        Name = name, Cut = cut, OnFold = onFold, Seam = path.Seam, CutLine = Polygon.Offset(path.Seam, path.Allowances),
        Grain = grain, Notches = notches ?? [], Dimensions = dims ?? new Dictionary<string, double>(),
    };
}

public sealed record NotionLine(Core.Entities.MaterialKind Kind, string Description, decimal Quantity, string Unit);

public sealed class DraftedPattern
{
    public required string Block { get; init; }
    public required string GarmentName { get; init; }
    public required string SizeLabel { get; init; }
    public required IReadOnlyList<PatternPiece> Pieces { get; init; }
    public required IReadOnlyList<string> Steps { get; init; }
    public required string Difficulty { get; init; }
    public required string FabricKind { get; init; }
    public required IReadOnlyList<NotionLine> Notions { get; init; }
}

public interface IPatternDrafter
{
    string Block { get; }
    DraftedPattern Draft(Measurements m, string sizeLabel);
}

/// <summary>Our own parametric drafters (decision 6: own C# code, three garments to start).</summary>
public sealed class PatternEngine
{
    private readonly Dictionary<string, IPatternDrafter> _drafters = new IPatternDrafter[]
    {
        new TShirtDrafter(), new ALineSkirtDrafter(), new PullOnTrousersDrafter(),
    }.ToDictionary(d => d.Block);

    public IReadOnlyCollection<string> Blocks => _drafters.Keys;

    public bool Supports(string garmentType) => _drafters.ContainsKey(garmentType);

    public DraftedPattern Draft(string block, Measurements m, string sizeLabel) =>
        _drafters.TryGetValue(block, out var d) ? d.Draft(m, sizeLabel) : throw new NotSupportedException($"No pattern for '{block}' yet.");
}

public sealed class TShirtDrafter : IPatternDrafter
{
    public const double Ease = 10; // total ease at the chest, cm
    public string Block => "tshirt";

    public static double ArmholeDepth(Measurements m) => m.Bust / 6 + 5;
    public static double QuarterChest(Measurements m) => (m.Bust + Ease) / 4;

    public DraftedPattern Draft(Measurements m, string sizeLabel)
    {
        var w = QuarterChest(m);
        var s = m.Shoulder / 2;
        var n = m.Neck / 6 + 1;
        var frontNeck = m.Neck / 6 + 3.5;
        const double backNeck = 2, slope = 4;
        var ah = ArmholeDepth(m);
        var length = m.BackWaistLength + 24;
        var hem = Math.Max(w, (m.Hip + Ease) / 4);

        PatternPiece Body(string name, double neckDepth, double armCtrl)
        {
            var path = new PathBuilder().MoveTo(new(0, neckDepth))
                .CurveTo(new(n, neckDepth), new(n, 0), 0.6)            // neckline
                .LineTo(new(s, slope), 1.5)                              // shoulder
                .CurveTo(new(s - armCtrl, ah), new(w, ah), 1)            // armhole
                .LineTo(new(hem, length), 1.5)                           // side seam
                .LineTo(new(0, length), 2.5)                             // hem
                .Close(0);                                               // centre: on the fold
            return PatternPiece.Build(name, 1, true, path, (new(w / 2, ah), new(w / 2, length - 6)),
                [new(w, ah), new((s + w) / 2, ah * 0.55)],
                new Dictionary<string, double> { ["Chest (half piece)"] = w, ["Length"] = length });
        }

        var front = Body("Front", frontNeck, 1.2);
        var back = Body("Back", backNeck, 0.6);

        var halfBicep = (m.UpperArm + 6) / 2;
        var capH = 0.55 * (ah - slope);
        const double sleeveLen = 20;
        var sleevePath = new PathBuilder().MoveTo(new(-halfBicep, capH))
            .CurveTo(new(-halfBicep * 0.45, 0), new(0, 0), 1)
            .CurveTo(new(halfBicep * 0.45, 0), new(halfBicep, capH), 1)
            .LineTo(new(halfBicep - 1.5, capH + sleeveLen), 1.5)
            .LineTo(new(-halfBicep + 1.5, capH + sleeveLen), 2.5)
            .Close(1.5);
        var sleeve = PatternPiece.Build("Sleeve", 2, false, sleevePath, (new(0, capH), new(0, capH + sleeveLen - 4)), [new(0, 0)]);

        var neckline = 2 * (Polygon.Perimeter(front.Seam, 0, 14) + Polygon.Perimeter(back.Seam, 0, 14));
        var bandLen = Math.Round(neckline * 0.85, 1);
        var half = bandLen / 2; // drafted as half, cut on the fold
        var bandPath = new PathBuilder().MoveTo(new(0, 0)).LineTo(new(half, 0), 0.6).LineTo(new(half, 5), 0.6).LineTo(new(0, 5), 0.6).Close(0);
        var band = PatternPiece.Build("Neckband", 1, true, bandPath, (new(half / 2 - 3, 2.5), new(half / 2 + 3, 2.5)),
            dims: new Dictionary<string, double> { ["Band length"] = bandLen });

        return new DraftedPattern
        {
            Block = Block, GarmentName = "T-shirt", SizeLabel = sizeLabel, Pieces = [front, back, sleeve, band],
            Difficulty = "Beginner", FabricKind = "Cotton or cotton-blend jersey (stretch knit)",
            Notions = [new(Core.Entities.MaterialKind.Thread, "Polyester all-purpose thread", 1, "spool"),
                       new(Core.Entities.MaterialKind.Notion, "Ballpoint / jersey machine needles", 1, "pack")],
            Steps =
            [
                "Pre-wash and dry your fabric the way you will wash the finished T-shirt.",
                "Print the pattern at 100% and check the 5 cm test square. Tape the pages together using the page letters.",
                "Fold the fabric selvedge to selvedge. Place Front and Back on the fold, Sleeve twice, Neckband once across the stretch.",
                "Sew the front to the back at the shoulders, right sides together, with a stretch stitch or overlocker.",
                "Fold the neckband in half lengthwise, join the short ends into a loop, and stretch it evenly into the neckline.",
                "Match the sleeve cap notch to the shoulder seam and sew each sleeve into its armhole.",
                "Sew one continuous seam from sleeve hem to body hem on each side.",
                "Turn up the sleeve and body hems by 2.5 cm and stitch with a twin needle or zigzag.",
            ],
        };
    }
}

public sealed class ALineSkirtDrafter : IPatternDrafter
{
    public string Block => "skirt";

    public DraftedPattern Draft(Measurements m, string sizeLabel)
    {
        var waistQ = m.Hip / 4 + 1.5; // pull-on: waist opening must pass the hips
        var hipQ = m.Hip / 4 + 1.5;
        const double hipDepth = 20, length = 58, flare = 8;
        var hemQ = hipQ + flare;

        PatternPiece Panel(string name)
        {
            var path = new PathBuilder().MoveTo(new(0, 0))
                .CurveTo(new(waistQ * 0.6, 0), new(waistQ, -1), 1)          // waist
                .CurveTo(new(hipQ, hipDepth * 0.5), new(hipQ, hipDepth), 1.5) // hip curve
                .LineTo(new(hemQ, length), 1.5)                               // side seam
                .CurveTo(new(hemQ * 0.5, length + 1), new(0, length + 1), 3)  // hem
                .Close(0);
            return PatternPiece.Build(name, 1, true, path, (new(hipQ / 2, 8), new(hipQ / 2, length - 8)), [new(hipQ, hipDepth)],
                new Dictionary<string, double> { ["Hip (half piece)"] = hipQ, ["Length"] = length });
        }

        var casingLen = Math.Round(m.Hip + 3, 1);
        var casing = PatternPiece.Build("Waist casing", 1, true, // half length, cut on the fold
            new PathBuilder().MoveTo(new(0, 0)).LineTo(new(casingLen / 2, 0), 1).LineTo(new(casingLen / 2, 7), 1).LineTo(new(0, 7), 1).Close(0),
            (new(casingLen / 4 - 3, 3.5), new(casingLen / 4 + 3, 3.5)), dims: new Dictionary<string, double> { ["Full length"] = casingLen });

        return new DraftedPattern
        {
            Block = Block, GarmentName = "A-line skirt", SizeLabel = sizeLabel, Pieces = [Panel("Front"), Panel("Back"), casing],
            Difficulty = "Beginner", FabricKind = "Medium-weight woven (cotton, linen, chambray)",
            Notions =
            [
                new(Core.Entities.MaterialKind.Notion, "Elastic, 25 mm wide", (decimal)Math.Round((m.Waist - 5) / 100, 2), "m"),
                new(Core.Entities.MaterialKind.Thread, "Polyester all-purpose thread", 1, "spool"),
            ],
            Steps =
            [
                "Pre-wash your fabric. Print at 100% and check the test square.",
                "Cut Front and Back on the fold, and one Waist casing.",
                "Sew front to back at both side seams. Press seams open and finish the edges.",
                "Join the short ends of the casing into a loop, leaving a 3 cm gap for the elastic. Fold it in half lengthwise.",
                "Sew the casing to the skirt waist, raw edges together.",
                "Thread the elastic through the gap, overlap the ends 2 cm and stitch. Close the gap.",
                "Press up the 3 cm hem, fold the raw edge under and topstitch.",
            ],
        };
    }
}

public sealed class PullOnTrousersDrafter : IPatternDrafter
{
    public string Block => "trousers";

    public DraftedPattern Draft(Measurements m, string sizeLabel)
    {
        var hipQ = m.Hip / 4 + 2;
        var cd = m.CrotchDepth + 2;
        var inseam = m.Inseam;
        var frontExt = m.Hip / 16;
        var backExt = m.Hip / 10;

        var fCrease = (hipQ + frontExt) / 2;
        const double frontHalfHem = 12;
        var front = PatternPiece.Build("Front", 2, false,
            new PathBuilder().MoveTo(new(0, 0))
                .LineTo(new(hipQ, 0), 1)                                         // waist
                .LineTo(new(hipQ, cd - frontExt * 1.2), 1.5)                     // centre front
                .CurveTo(new(hipQ, cd), new(hipQ + frontExt, cd), 1)             // front crotch
                .LineTo(new(fCrease + frontHalfHem, cd + inseam), 1.5)           // inseam
                .LineTo(new(fCrease - frontHalfHem, cd + inseam), 3)             // hem
                .Close(1.5),                                                     // side seam
            (new(fCrease, cd + 5), new(fCrease, cd + inseam - 10)), [new(hipQ, cd - frontExt * 1.2)],
            new Dictionary<string, double> { ["Hip (quarter)"] = hipQ, ["Crotch depth"] = cd });

        var bCrease = (hipQ + backExt) / 2;
        const double backHalfHem = 14;
        var back = PatternPiece.Build("Back", 2, false,
            new PathBuilder().MoveTo(new(0, 0))
                .LineTo(new(hipQ + 1, -3), 1)                                    // raised back waist
                .LineTo(new(hipQ, cd - backExt), 1.5)                            // centre back
                .CurveTo(new(hipQ, cd + 1), new(hipQ + backExt, cd + 1), 1)      // back crotch
                .LineTo(new(bCrease + backHalfHem, cd + inseam), 1.5)
                .LineTo(new(bCrease - backHalfHem, cd + inseam), 3)
                .Close(1.5),
            (new(bCrease, cd + 5), new(bCrease, cd + inseam - 10)), [new(hipQ, cd - backExt)]);

        var casingLen = Math.Round(m.Hip + 4, 1);
        var casing = PatternPiece.Build("Waist casing", 1, true, // half length, cut on the fold
            new PathBuilder().MoveTo(new(0, 0)).LineTo(new(casingLen / 2, 0), 1).LineTo(new(casingLen / 2, 8), 1).LineTo(new(0, 8), 1).Close(0),
            (new(casingLen / 4 - 3, 4), new(casingLen / 4 + 3, 4)), dims: new Dictionary<string, double> { ["Full length"] = casingLen });

        return new DraftedPattern
        {
            Block = Block, GarmentName = "Pull-on trousers", SizeLabel = sizeLabel, Pieces = [front, back, casing],
            Difficulty = "Intermediate", FabricKind = "Cotton twill, linen or drill (woven)",
            Notions =
            [
                new(Core.Entities.MaterialKind.Notion, "Elastic, 30 mm wide", (decimal)Math.Round((m.Waist - 4) / 100, 2), "m"),
                new(Core.Entities.MaterialKind.Thread, "Polyester all-purpose thread", 1, "spool"),
            ],
            Steps =
            [
                "Pre-wash your fabric. Print at 100% and check the test square.",
                "Cut two Fronts and two Backs (mirror them), and one Waist casing.",
                "Sew each front to a back at the side seam, then at the inseam. You now have two legs.",
                "Turn one leg right side out and slip it inside the other. Sew the crotch seam in one curve.",
                "Clip the crotch curve, then finish the seam.",
                "Make the waist casing loop, attach it, and thread the elastic (see the skirt steps).",
                "Press up the 3 cm hem, fold the raw edge under and topstitch.",
            ],
        };
    }
}
