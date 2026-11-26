namespace OutfitFinder.Infrastructure.Patterns;

/// <summary>Body measurements in centimetres.</summary>
public sealed record Measurements(
    double Bust, double Waist, double Hip, double Neck, double Shoulder,
    double BackWaistLength, double UpperArm, double CrotchDepth, double Inseam)
{
    public static readonly string[] Keys = ["bust", "waist", "hip", "neck", "shoulder", "backWaistLength", "upperArm", "crotchDepth", "inseam"];

    public Dictionary<string, double> ToDictionary() => new()
    {
        ["bust"] = Bust, ["waist"] = Waist, ["hip"] = Hip, ["neck"] = Neck, ["shoulder"] = Shoulder,
        ["backWaistLength"] = BackWaistLength, ["upperArm"] = UpperArm, ["crotchDepth"] = CrotchDepth, ["inseam"] = Inseam,
    };

    /// <summary>Builds from user input, filling any missing value from the nearest standard size by bust/hip.</summary>
    public static Measurements FromDictionary(IReadOnlyDictionary<string, double> d)
    {
        var basis = SizeChart.Nearest(d.GetValueOrDefault("bust"), d.GetValueOrDefault("hip"));
        double V(string k, double fallback) => d.TryGetValue(k, out var v) && v > 0 ? v : fallback;
        var m = new Measurements(
            V("bust", basis.Bust), V("waist", basis.Waist), V("hip", basis.Hip), V("neck", basis.Neck), V("shoulder", basis.Shoulder),
            V("backWaistLength", basis.BackWaistLength), V("upperArm", basis.UpperArm), V("crotchDepth", basis.CrotchDepth), V("inseam", basis.Inseam));
        m.Validate();
        return m;
    }

    public void Validate()
    {
        void Range(string name, double v, double lo, double hi)
        {
            if (v < lo || v > hi) throw new ArgumentOutOfRangeException(name, $"{name} must be between {lo} and {hi} cm.");
        }
        Range("bust", Bust, 60, 170); Range("waist", Waist, 45, 170); Range("hip", Hip, 65, 180);
        Range("neck", Neck, 25, 55); Range("shoulder", Shoulder, 28, 55); Range("backWaistLength", BackWaistLength, 30, 55);
        Range("upperArm", UpperArm, 18, 55); Range("crotchDepth", CrotchDepth, 18, 40); Range("inseam", Inseam, 55, 95);
    }
}

/// <summary>Standard size chart (cm). Values follow common misses' body-measurement charts.</summary>
public static class SizeChart
{
    public static readonly IReadOnlyDictionary<string, Measurements> Sizes = new Dictionary<string, Measurements>
    {
        ["XS"] = new(81, 63, 89, 34, 37, 40, 26, 25, 76),
        ["S"] = new(86, 68, 94, 35, 38, 40.5, 27.5, 26, 77),
        ["M"] = new(91, 73, 99, 36, 39, 41, 29, 27, 78),
        ["L"] = new(99, 81, 107, 38, 40.5, 41.5, 31.5, 28, 78.5),
        ["XL"] = new(107, 89, 115, 40, 42, 42, 34, 29, 79),
    };

    public static Measurements Get(string size) =>
        Sizes.TryGetValue(size.ToUpperInvariant(), out var m) ? m : throw new ArgumentException($"Unknown size '{size}'. Use XS, S, M, L or XL.");

    public static Measurements Nearest(double bust, double hip)
    {
        if (bust <= 0 && hip <= 0) return Sizes["M"];
        return Sizes.Values.OrderBy(m => (bust > 0 ? Math.Abs(m.Bust - bust) : 0) + (hip > 0 ? Math.Abs(m.Hip - hip) : 0)).First();
    }
}
