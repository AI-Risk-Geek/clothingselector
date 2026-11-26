using System.Globalization;
using System.Text;

namespace OutfitFinder.Infrastructure.Patterns;

/// <summary>A point in centimetres. X grows right, Y grows down.</summary>
public readonly record struct Pt(double X, double Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(Pt a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Pt Normalised => Length < 1e-9 ? this : this * (1 / Length);
}

/// <summary>Builds a closed seamline with a seam allowance per edge (0 on a fold).</summary>
public sealed class PathBuilder
{
    private readonly List<Pt> _points = new();
    private readonly List<double> _allowances = new(); // allowance of edge i: points[i] -> points[i+1]

    public PathBuilder MoveTo(Pt p) { _points.Clear(); _allowances.Clear(); _points.Add(p); return this; }

    public PathBuilder LineTo(Pt p, double sa) { _allowances.Add(sa); _points.Add(p); return this; }

    /// <summary>Quadratic Bézier from the current point through control <paramref name="c"/> to <paramref name="end"/>.</summary>
    public PathBuilder CurveTo(Pt c, Pt end, double sa, int steps = 14)
    {
        var start = _points[^1];
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            var p = start * ((1 - t) * (1 - t)) + c * (2 * (1 - t) * t) + end * (t * t);
            LineTo(p, sa);
        }
        return this;
    }

    /// <summary>Close back to the first point with the given allowance.</summary>
    public (IReadOnlyList<Pt> Seam, IReadOnlyList<double> Allowances) Close(double sa)
    {
        var allowances = new List<double>(_allowances) { sa };
        return (_points.ToList(), allowances);
    }
}

public static class Polygon
{
    public static double SignedArea(IReadOnlyList<Pt> p)
    {
        double a = 0;
        for (var i = 0; i < p.Count; i++) { var j = (i + 1) % p.Count; a += p[i].X * p[j].Y - p[j].X * p[i].Y; }
        return a / 2;
    }

    public static double Area(IReadOnlyList<Pt> p) => Math.Abs(SignedArea(p));

    public static double Perimeter(IReadOnlyList<Pt> p, int from, int to)
    {
        double l = 0;
        for (var i = from; i < to; i++) l += (p[i + 1] - p[i]).Length;
        return l;
    }

    /// <summary>
    /// Offsets each edge outward by its own distance and joins neighbouring edges at their intersection
    /// (mitre, clamped). Works for the simple, mostly-convex outlines our drafters produce.
    /// </summary>
    public static List<Pt> Offset(IReadOnlyList<Pt> pts, IReadOnlyList<double> dist)
    {
        var n = pts.Count;
        // For a positively oriented polygon (shoelace area > 0) the outward normal of edge d is (d.Y, -d.X).
        var outwardSign = SignedArea(pts) > 0 ? 1 : -1;
        var lines = new (Pt A, Pt B)[n];
        for (var i = 0; i < n; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % n];
            var d = (b - a).Normalised;
            var normal = new Pt(d.Y, -d.X) * outwardSign;
            lines[i] = (a + normal * dist[i], b + normal * dist[i]);
        }
        var result = new List<Pt>(n);
        for (var i = 0; i < n; i++)
        {
            var prev = lines[(i - 1 + n) % n];
            var cur = lines[i];
            var p = Intersect(prev.A, prev.B, cur.A, cur.B);
            // Fall back to the plain offset point for near-parallel edges or runaway mitres.
            if (p is null || (p.Value - cur.A).Length > 4 * Math.Max(dist[i], dist[(i - 1 + n) % n]) + 0.5)
                p = cur.A;
            result.Add(p.Value);
        }
        return result;
    }

    private static Pt? Intersect(Pt p1, Pt p2, Pt p3, Pt p4)
    {
        var d = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
        if (Math.Abs(d) < 1e-9) return null;
        var t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / d;
        return new Pt(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y));
    }

    public static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IEnumerable<Pt> pts)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in pts) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        return (minX, minY, maxX, maxY);
    }

    public static string ToSvgPath(IEnumerable<Pt> pts, Pt offset = default)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var p in pts)
        {
            sb.Append(first ? 'M' : 'L').Append(F(p.X + offset.X)).Append(' ').Append(F(p.Y + offset.Y)).Append(' ');
            first = false;
        }
        return sb.Append('Z').ToString();
    }

    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
