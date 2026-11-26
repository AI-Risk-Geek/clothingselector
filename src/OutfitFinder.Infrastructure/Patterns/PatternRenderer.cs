using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static OutfitFinder.Infrastructure.Patterns.Polygon;

namespace OutfitFinder.Infrastructure.Patterns;

public enum Paper { Letter, A4 }

/// <summary>Pieces placed on one big sheet, in centimetres.</summary>
public sealed record PatternLayout(double Width, double Height, IReadOnlyList<(PatternPiece Piece, Pt Offset)> Placed, Pt TestSquareAt);

/// <summary>
/// Renders previews (SVG) and print files (PDF). Everything is drawn in centimetres; PDF pages map the SVG
/// viewBox (cm) onto a page of exactly the same physical size, so output is 1:1.
/// </summary>
public static class PatternRenderer
{
    public const double PointsPerCm = 72 / 2.54;
    public const double Margin = 1.0;   // unprintable border kept clear on each tile, cm
    public const double Overlap = 1.5;  // shared strip between neighbouring tiles, cm
    public const double TestSquareCm = 5;
    public const double TestSquareIn = 2 * 2.54;

    static PatternRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public static (double W, double H) PaperSize(Paper p) => p == Paper.A4 ? (21.0, 29.7) : (21.59, 27.94);

    // ---------- layout ----------

    public static PatternLayout Layout(DraftedPattern p, double maxRowWidth = 80)
    {
        const double gap = 2, pad = 1.5;
        var placed = new List<(PatternPiece, Pt)>();
        var testSquare = new Pt(pad, pad);
        double x = pad + TestSquareIn + gap * 2, y = pad, rowH = TestSquareIn + 3, width = x;
        foreach (var piece in p.Pieces.OrderByDescending(pc => Bounds(pc.CutLine).MaxY - Bounds(pc.CutLine).MinY))
        {
            var b = Bounds(piece.CutLine);
            var w = b.MaxX - b.MinX; var h = b.MaxY - b.MinY;
            if (x + w > maxRowWidth && x > pad) { x = pad; y += rowH + gap; rowH = 0; }
            placed.Add((piece, new Pt(x - b.MinX, y - b.MinY)));
            x += w + gap; rowH = Math.Max(rowH, h); width = Math.Max(width, x);
        }
        return new PatternLayout(width + pad, y + rowH + pad, placed, testSquare);
    }

    /// <summary>Tries several sheet widths and keeps the one that prints on the fewest pages.</summary>
    public static PatternLayout BestLayout(DraftedPattern p, Paper paper)
    {
        PatternLayout? best = null; var bestPages = int.MaxValue;
        for (var width = 40; width <= 100; width += 5)
        {
            var layout = Layout(p, width);
            var (rows, cols) = TileGrid(layout, paper);
            if (rows * cols < bestPages) { best = layout; bestPages = rows * cols; }
        }
        return best!;
    }

    // ---------- SVG ----------

    /// <summary>Small inline preview for the website. Uses CSS classes so colours come from tokens.css.</summary>
    public static string PreviewSvg(PatternPiece piece, int colorIndex)
    {
        var b = Bounds(piece.CutLine);
        var pad = 1.0;
        var vb = $"{F(b.MinX - pad)} {F(b.MinY - pad)} {F(b.MaxX - b.MinX + 2 * pad)} {F(b.MaxY - b.MinY + 2 * pad)}";
        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"{vb}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"{piece.Name} pattern piece\" class=\"piece-svg\">");
        sb.Append($"<path d=\"{ToSvgPath(piece.CutLine)}\" class=\"piece-fill piece-fill-{colorIndex % 5}\" />");
        sb.Append($"<path d=\"{ToSvgPath(piece.Seam)}\" class=\"piece-seam\" />");
        sb.Append($"<line x1=\"{F(piece.Grain.A.X)}\" y1=\"{F(piece.Grain.A.Y)}\" x2=\"{F(piece.Grain.B.X)}\" y2=\"{F(piece.Grain.B.Y)}\" class=\"piece-grain\" />");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>The whole layout as SVG content (no root element), in cm.</summary>
    public static string LayoutContent(DraftedPattern p, PatternLayout layout)
    {
        var sb = new StringBuilder();
        // Test squares: 5 cm and 2 in.
        var t = layout.TestSquareAt;
        sb.Append($"<rect x=\"{F(t.X)}\" y=\"{F(t.Y)}\" width=\"{F(TestSquareCm)}\" height=\"{F(TestSquareCm)}\" fill=\"none\" stroke=\"black\" stroke-width=\"0.04\"/>");
        sb.Append(Text(t.X + 0.3, t.Y + 2.4, 0.55, "5 cm", bold: true));
        sb.Append(Text(t.X + 0.3, t.Y + 3.2, 0.35, "test square"));
        sb.Append($"<rect x=\"{F(t.X)}\" y=\"{F(t.Y)}\" width=\"{F(TestSquareIn)}\" height=\"{F(TestSquareIn)}\" fill=\"none\" stroke=\"gray\" stroke-width=\"0.03\" stroke-dasharray=\"0.3 0.2\"/>");
        sb.Append(Text(t.X + 0.3, t.Y + TestSquareIn - 0.3, 0.35, "2 in"));

        foreach (var (piece, off) in layout.Placed)
        {
            sb.Append($"<path d=\"{ToSvgPath(piece.CutLine, off)}\" fill=\"none\" stroke=\"black\" stroke-width=\"0.05\"/>");
            sb.Append($"<path d=\"{ToSvgPath(piece.Seam, off)}\" fill=\"none\" stroke=\"gray\" stroke-width=\"0.03\" stroke-dasharray=\"0.4 0.25\"/>");
            // Grainline with arrowheads.
            var a = piece.Grain.A + off; var bpt = piece.Grain.B + off;
            sb.Append($"<line x1=\"{F(a.X)}\" y1=\"{F(a.Y)}\" x2=\"{F(bpt.X)}\" y2=\"{F(bpt.Y)}\" stroke=\"black\" stroke-width=\"0.04\"/>");
            sb.Append(Arrow(a, bpt)).Append(Arrow(bpt, a));
            foreach (var n in piece.Notches)
            {
                var q = n + off;
                sb.Append($"<path d=\"M{F(q.X - 0.25)} {F(q.Y)} L{F(q.X + 0.25)} {F(q.Y)} L{F(q.X)} {F(q.Y + 0.4)} Z\" fill=\"black\"/>");
            }
            if (piece.OnFold)
            {
                var foldTop = piece.Seam.Where(s => Math.Abs(s.X) < 1e-6).Min(s => s.Y) + off.Y;
                var foldBottom = piece.Seam.Where(s => Math.Abs(s.X) < 1e-6).Max(s => s.Y) + off.Y;
                sb.Append($"<line x1=\"{F(off.X - 0.15)}\" y1=\"{F(foldTop)}\" x2=\"{F(off.X - 0.15)}\" y2=\"{F(foldBottom)}\" stroke=\"black\" stroke-width=\"0.12\"/>");
                sb.Append($"<text x=\"{F(off.X + 0.5)}\" y=\"{F((foldTop + foldBottom) / 2)}\" font-family=\"Helvetica, Arial, sans-serif\" font-size=\"0.5\" transform=\"rotate(90 {F(off.X + 0.5)} {F((foldTop + foldBottom) / 2)})\" text-anchor=\"middle\">PLACE ON FOLD</text>");
            }
            var c = Centroid(piece.Seam) + off;
            var cut = piece.OnFold ? "Cut 1 on fold" : $"Cut {piece.Cut}" + (piece.Cut == 2 ? " (mirror)" : "");
            sb.Append(Text(c.X + 0.6, c.Y - 1.2, 0.9, piece.Name, bold: true));
            sb.Append(Text(c.X + 0.6, c.Y - 0.2, 0.5, $"{p.GarmentName} · size {p.SizeLabel}"));
            sb.Append(Text(c.X + 0.6, c.Y + 0.6, 0.5, cut));
            sb.Append(Text(c.X + 0.6, c.Y + 1.4, 0.4, $"{Core.Brand.Name} · seam allowance included"));
        }
        return sb.ToString();
    }

    private static string Text(double x, double y, double size, string text, bool bold = false) =>
        $"<text x=\"{F(x)}\" y=\"{F(y)}\" font-family=\"Helvetica, Arial, sans-serif\" font-size=\"{F(size)}\"{(bold ? " font-weight=\"bold\"" : "")}>{System.Net.WebUtility.HtmlEncode(text)}</text>";

    private static string Arrow(Pt from, Pt tip)
    {
        var d = (tip - from).Normalised; var n = new Pt(-d.Y, d.X);
        var b1 = tip - d * 0.6 + n * 0.3; var b2 = tip - d * 0.6 - n * 0.3;
        return $"<path d=\"M{F(tip.X)} {F(tip.Y)} L{F(b1.X)} {F(b1.Y)} L{F(b2.X)} {F(b2.Y)} Z\" fill=\"black\"/>";
    }

    private static Pt Centroid(IReadOnlyList<Pt> pts)
    {
        var b = Bounds(pts);
        return new Pt(b.MinX + (b.MaxX - b.MinX) * 0.3, b.MinY + (b.MaxY - b.MinY) * 0.45);
    }

    /// <summary>One printable tile as a standalone SVG whose viewBox is the paper in cm.</summary>
    public static string TileSvg(string content, PatternLayout layout, Paper paper, int row, int col, int rows, int cols)
    {
        var (w, h) = PaperSize(paper);
        var pw = w - 2 * Margin; var ph = h - 2 * Margin;
        var wx = col * (pw - Overlap); var wy = row * (ph - Overlap);
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(w * PointsPerCm)}\" height=\"{F(h * PointsPerCm)}\" viewBox=\"0 0 {F(w)} {F(h)}\">");
        sb.Append($"<defs><clipPath id=\"c\"><rect x=\"{F(Margin)}\" y=\"{F(Margin)}\" width=\"{F(pw)}\" height=\"{F(ph)}\"/></clipPath></defs>");
        sb.Append($"<g clip-path=\"url(#c)\"><g transform=\"translate({F(Margin - wx)} {F(Margin - wy)})\">{content}</g></g>");
        // Page frame and overlap strips (glue/tape zone).
        sb.Append($"<rect x=\"{F(Margin)}\" y=\"{F(Margin)}\" width=\"{F(pw)}\" height=\"{F(ph)}\" fill=\"none\" stroke=\"gray\" stroke-width=\"0.02\"/>");
        if (col > 0) sb.Append($"<line x1=\"{F(Margin + Overlap)}\" y1=\"{F(Margin)}\" x2=\"{F(Margin + Overlap)}\" y2=\"{F(h - Margin)}\" stroke=\"gray\" stroke-width=\"0.02\" stroke-dasharray=\"0.2 0.2\"/>");
        if (row > 0) sb.Append($"<line x1=\"{F(Margin)}\" y1=\"{F(Margin + Overlap)}\" x2=\"{F(w - Margin)}\" y2=\"{F(Margin + Overlap)}\" stroke=\"gray\" stroke-width=\"0.02\" stroke-dasharray=\"0.2 0.2\"/>");
        // Alignment diamonds centred in each overlap strip, matched by the neighbour's strip.
        void Diamond(double cx, double cy) => sb.Append($"<path d=\"M{F(cx)} {F(cy - 0.5)} L{F(cx + 0.5)} {F(cy)} L{F(cx)} {F(cy + 0.5)} L{F(cx - 0.5)} {F(cy)} Z\" fill=\"none\" stroke=\"black\" stroke-width=\"0.03\"/><circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"0.08\" fill=\"black\"/>");
        if (col > 0) { Diamond(Margin + Overlap / 2, h * 0.3); Diamond(Margin + Overlap / 2, h * 0.7); }
        if (col < cols - 1) { Diamond(w - Margin - Overlap / 2, h * 0.3); Diamond(w - Margin - Overlap / 2, h * 0.7); }
        if (row > 0) { Diamond(w * 0.3, Margin + Overlap / 2); Diamond(w * 0.7, Margin + Overlap / 2); }
        if (row < rows - 1) { Diamond(w * 0.3, h - Margin - Overlap / 2); Diamond(w * 0.7, h - Margin - Overlap / 2); }
        sb.Append(Text(Margin + 0.3 + (col > 0 ? Overlap : 0), Margin + 0.8 + (row > 0 ? Overlap : 0), 0.6, TileName(row, col), bold: true));
        sb.Append("</svg>");
        return sb.ToString();
    }

    public static string TileName(int row, int col) => $"{(char)('A' + row)}{col + 1}";

    public static (int Rows, int Cols) TileGrid(PatternLayout layout, Paper paper)
    {
        var (w, h) = PaperSize(paper);
        var sx = w - 2 * Margin - Overlap; var sy = h - 2 * Margin - Overlap;
        var cols = Math.Max(1, (int)Math.Ceiling((layout.Width - Overlap) / sx));
        var rows = Math.Max(1, (int)Math.Ceiling((layout.Height - Overlap) / sy));
        return (rows, cols);
    }

    private static string AssemblyMapSvg(PatternLayout layout, string content, Paper paper, int rows, int cols)
    {
        var (w, h) = PaperSize(paper);
        var sx = w - 2 * Margin - Overlap; var sy = h - 2 * Margin - Overlap;
        var totalW = cols * sx + Overlap; var totalH = rows * sy + Overlap;
        var sb = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(totalW)} {F(totalH)}\">");
        sb.Append($"<g opacity=\"0.6\">{content}</g>");
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                sb.Append($"<rect x=\"{F(c * sx)}\" y=\"{F(r * sy)}\" width=\"{F(sx + Overlap)}\" height=\"{F(sy + Overlap)}\" fill=\"none\" stroke=\"black\" stroke-width=\"{F(totalW / 300)}\"/>");
                sb.Append($"<text x=\"{F(c * sx + sx / 2)}\" y=\"{F(r * sy + sy / 2)}\" font-family=\"Helvetica, Arial, sans-serif\" font-size=\"{F(Math.Min(sx, sy) / 4)}\" font-weight=\"bold\" text-anchor=\"middle\" fill=\"black\">{TileName(r, c)}</text>");
            }
        return sb.Append("</svg>").ToString();
    }

    // ---------- PDF ----------

    public static byte[] TiledPdf(DraftedPattern p, Paper paper)
    {
        var layout = BestLayout(p, paper);
        var content = LayoutContent(p, layout);
        var (rows, cols) = TileGrid(layout, paper);
        var (w, h) = PaperSize(paper);
        var pageSize = new PageSize((float)(w * PointsPerCm), (float)(h * PointsPerCm), Unit.Point);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(pageSize);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10));
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text($"{Core.Brand.Name} — {p.GarmentName}, size {p.SizeLabel}").FontSize(18).Bold();
                    col.Item().Text("Print at 100% / \"Actual size\". Do not \"fit to page\". Measure the test square on page A1 before cutting: it must be exactly 5 cm.").Bold();
                    col.Item().Text($"{rows * cols} pages: {rows} rows (A–{(char)('A' + rows - 1)}) × {cols} columns. Trim nothing; overlap each page onto its neighbour so the diamonds line up, then tape.");
                    col.Item().Text("Pattern pieces: " + string.Join(", ", p.Pieces.Select(pc => $"{pc.Name} ({(pc.OnFold ? "cut 1 on fold" : $"cut {pc.Cut}")})")) + ". Seam allowance is included.");
                    col.Item().Text("This pattern is drafted in the style of the garment in your photo for personal use. It is not a copy of the original design.").Italic();
                    col.Item().PaddingTop(8).Text("Assembly map").FontSize(13).Bold();
                    col.Item().MaxHeight(14, Unit.Centimetre).Svg(AssemblyMapSvg(layout, content, paper, rows, cols));
                });
            });
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var svg = TileSvg(content, layout, paper, r, c, rows, cols);
                    doc.Page(page => { page.Size(pageSize); page.Margin(0); page.Content().Svg(svg); });
                }
        }).GeneratePdf();
    }

    /// <summary>One sheet at full size for a copy shop (wide-format printer).</summary>
    public static byte[] CopyShopPdf(DraftedPattern p)
    {
        var layout = Layout(p, maxRowWidth: 88);
        var content = LayoutContent(p, layout);
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(layout.Width * PointsPerCm)}\" height=\"{F(layout.Height * PointsPerCm)}\" viewBox=\"0 0 {F(layout.Width)} {F(layout.Height)}\">{content}</svg>";
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(new PageSize((float)(layout.Width * PointsPerCm), (float)(layout.Height * PointsPerCm), Unit.Point));
            page.Margin(0);
            page.Content().Svg(svg);
        })).GeneratePdf();
    }
}
