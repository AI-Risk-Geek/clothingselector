namespace OutfitFinder.Core.Ai;

/// <summary>
/// Validates and normalises model output after parsing. The schema is also sent to the model
/// (strict mode where supported); this is the second line of defence.
/// </summary>
public static class AnalysisValidator
{
    private static readonly HashSet<string> GarmentTypes = ["tshirt", "skirt", "trousers", "other"];

    public static IReadOnlyList<string> Validate(OutfitAnalysis a)
    {
        var errors = new List<string>();
        for (var i = 0; i < a.Items.Count; i++)
        {
            var item = a.Items[i];
            if (string.IsNullOrWhiteSpace(item.Category)) errors.Add($"items[{i}].category is empty");
            if (!BoxOk(item.Box)) errors.Add($"items[{i}].box is outside 0-1");
            if (!GarmentTypes.Contains(item.MakeIt.GarmentType)) errors.Add($"items[{i}].makeIt.garmentType invalid");
            errors.AddRange(ValidateMatches(item.Matches, $"items[{i}]"));
        }
        for (var f = 0; f < a.Faces.Count; f++)
            if (!BoxOk(a.Faces[f])) errors.Add($"faces[{f}] is outside 0-1");
        return errors;
    }

    public static IEnumerable<string> ValidateMatches(IReadOnlyList<ProductMatch> matches, string path)
    {
        for (var m = 0; m < matches.Count; m++)
        {
            var match = matches[m];
            if (!IsHttpUrl(match.Url)) yield return $"{path}.matches[{m}].url is not an http(s) URL";
            if (match.MatchType is not ("exact" or "similar")) yield return $"{path}.matches[{m}].matchType invalid";
            if (match.Price is < 0) yield return $"{path}.matches[{m}].price negative";
            if (match.Currency is { Length: not 3 }) yield return $"{path}.matches[{m}].currency is not ISO 4217";
        }
    }

    /// <summary>Clamp boxes into the image and drop broken rows rather than fail the whole search.</summary>
    public static void Normalise(OutfitAnalysis a)
    {
        foreach (var b in a.Faces.Concat(a.Items.Select(i => i.Box))) Clamp(b);
        a.Items = a.Items.Where(i => i.Box.W > 0.005 && i.Box.H > 0.005).ToList();
        foreach (var item in a.Items)
        {
            item.Matches = NormaliseMatches(item.Matches);
            if (item.MakeIt.Makeable && item.MakeIt.GarmentType == "other") item.MakeIt.Makeable = false;
        }
    }

    public static List<ProductMatch> NormaliseMatches(IEnumerable<ProductMatch> matches) =>
        matches.Where(m => IsHttpUrl(m.Url))
            .Select(m =>
            {
                if (m.Currency is { Length: not 3 }) m.Currency = null;
                if (m.Price is < 0) m.Price = null;
                if (m.ImageUrl is not null && !IsHttpUrl(m.ImageUrl)) m.ImageUrl = null;
                m.MatchType = m.MatchType == "exact" ? "exact" : "similar";
                return m;
            })
            .DistinctBy(m => m.Url).Take(5).ToList();

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    private static bool BoxOk(NormalizedBox b) =>
        b.X >= -0.01 && b.Y >= -0.01 && b.W > 0 && b.H > 0 && b.X + b.W <= 1.01 && b.Y + b.H <= 1.01;

    private static void Clamp(NormalizedBox b)
    {
        b.X = Math.Clamp(b.X, 0, 1); b.Y = Math.Clamp(b.Y, 0, 1);
        b.W = Math.Clamp(b.W, 0, 1 - b.X); b.H = Math.Clamp(b.H, 0, 1 - b.Y);
    }
}
