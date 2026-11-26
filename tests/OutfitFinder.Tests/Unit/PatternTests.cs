using System.Text;
using System.Text.RegularExpressions;
using OutfitFinder.Infrastructure.Patterns;

namespace OutfitFinder.Tests.Unit;

public class PatternTests
{
    private readonly PatternEngine _engine = new();

    [Fact]
    public void Tshirt_size_M_matches_the_size_chart_within_5_mm()
    {
        var m = SizeChart.Get("M");
        var p = _engine.Draft("tshirt", m, "M");
        var front = p.Pieces.Single(x => x.Name == "Front");
        var back = p.Pieces.Single(x => x.Name == "Back");
        var ah = TShirtDrafter.ArmholeDepth(m);

        // Each body piece is a half (cut on the fold), so 2 × (front + back half-width) is the finished chest.
        double HalfWidthAtChest(PatternPiece piece) => piece.Seam.Where(pt => Math.Abs(pt.Y - ah) < 0.01).Max(pt => pt.X);
        var finishedChest = 2 * (HalfWidthAtChest(front) + HalfWidthAtChest(back));
        Assert.InRange(finishedChest - (m.Bust + TShirtDrafter.Ease), -0.5, 0.5);

        var length = front.Seam.Max(pt => pt.Y);
        Assert.InRange(length - (m.BackWaistLength + 24), -0.5, 0.5);
    }

    [Theory]
    [InlineData("tshirt")]
    [InlineData("skirt")]
    [InlineData("trousers")]
    public void Every_block_drafts_in_every_size_with_allowance_outside_the_seam(string block)
    {
        foreach (var size in SizeChart.Sizes.Keys)
        {
            var p = _engine.Draft(block, SizeChart.Get(size), size);
            Assert.NotEmpty(p.Pieces);
            foreach (var piece in p.Pieces)
                Assert.True(Polygon.Area(piece.CutLine) > Polygon.Area(piece.Seam), $"{block} {size} {piece.Name}: cut line must enclose the seam line");
        }
    }

    [Fact]
    public void Larger_sizes_produce_larger_pieces()
    {
        var s = _engine.Draft("tshirt", SizeChart.Get("S"), "S").Pieces[0];
        var xl = _engine.Draft("tshirt", SizeChart.Get("XL"), "XL").Pieces[0];
        Assert.True(Polygon.Area(xl.Seam) > Polygon.Area(s.Seam));
    }

    [Fact]
    public void Custom_measurements_are_validated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Measurements.FromDictionary(new Dictionary<string, double> { ["bust"] = 400 }));
        var m = Measurements.FromDictionary(new Dictionary<string, double> { ["bust"] = 95 });
        Assert.Equal(95, m.Bust);
    }

    [Fact]
    public void Tile_pages_map_centimetres_one_to_one()
    {
        // A tile's SVG is exactly paper-sized in points with a viewBox in cm, so 1 cm in the drawing prints as 1 cm.
        var p = _engine.Draft("tshirt", SizeChart.Get("M"), "M");
        var layout = PatternRenderer.Layout(p);
        var svg = PatternRenderer.TileSvg(PatternRenderer.LayoutContent(p, layout), layout, Paper.A4, 0, 0, 2, 2);
        var width = double.Parse(Regex.Match(svg, "width=\"([0-9.]+)\"").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var vb = Regex.Match(svg, "viewBox=\"0 0 ([0-9.]+) ([0-9.]+)\"");
        var vbWidthCm = double.Parse(vb.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(21.0, vbWidthCm, 3);
        Assert.Equal(595.276, width, 1); // A4 width in points
        Assert.Equal(PatternRenderer.PointsPerCm, width / vbWidthCm, 3);
        // The test square is drawn 5 cm wide in layout units.
        Assert.Contains("width=\"5\" height=\"5\"", PatternRenderer.LayoutContent(p, layout));
    }

    [Theory]
    [InlineData(Paper.Letter)]
    [InlineData(Paper.A4)]
    public void Tiled_pdf_has_a_cover_page_plus_one_page_per_tile(Paper paper)
    {
        var p = _engine.Draft("skirt", SizeChart.Get("M"), "M");
        var (rows, cols) = PatternRenderer.TileGrid(PatternRenderer.BestLayout(p, paper), paper);
        var (r0, c0) = PatternRenderer.TileGrid(PatternRenderer.Layout(p), paper);
        Assert.True(rows * cols <= r0 * c0, "BestLayout must never use more pages than the default layout");
        Assert.True(rows * cols <= 20, $"A knee-length skirt should print on at most 20 pages, got {rows * cols}");
        var pdf = PatternRenderer.TiledPdf(p, paper);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        var pages = Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;
        Assert.Equal(1 + rows * cols, pages);
    }

    [Fact]
    public void Copy_shop_pdf_is_one_life_size_page()
    {
        var p = _engine.Draft("trousers", SizeChart.Get("L"), "L");
        var pdf = Encoding.Latin1.GetString(PatternRenderer.CopyShopPdf(p));
        Assert.Single(Regex.Matches(pdf, @"/Type\s*/Page[^s]"));
    }

    [Fact]
    public void Materials_are_computed_from_the_pieces()
    {
        var p = _engine.Draft("tshirt", SizeChart.Get("M"), "M");
        var list = MaterialsCalculator.Compute(p, "Navy jersey");
        var fabric60 = list[0]; var fabric45 = list[1];
        Assert.Equal("yd", fabric60.Unit);
        Assert.InRange(fabric60.Quantity, 1m, 3m);                  // a T-shirt needs roughly 1–2 yd
        Assert.True(fabric45.Quantity >= fabric60.Quantity);       // narrower fabric never needs less
        Assert.Equal(0m, fabric60.Quantity % 0.125m);              // rounded up to 1/8 yd
        Assert.Contains(list, l => l.Description.Contains("thread", StringComparison.OrdinalIgnoreCase));
    }
}
