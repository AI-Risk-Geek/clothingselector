using System.Globalization;
using Markdig;
using OutfitFinder.Core;
using OutfitFinder.Core.Contracts;

namespace OutfitFinder.UI.Legal;

public sealed record LegalDocument(string Key, string Title, string Version, DateOnly Effective, string Html);

/// <summary>Legal pages are Markdown files (embedded) with a version and effective date in front matter.</summary>
public static class LegalDocuments
{
    public const string Privacy = "privacy", Terms = "terms", Cookies = "cookies";
    /// <summary>Documents a user must accept (and re-accept when the version changes).</summary>
    public static readonly string[] RequireConsent = [Terms, Privacy];

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
    private static Lazy<IReadOnlyDictionary<string, LegalDocument>> Docs = new(Load);

    /// <summary>Business facts the legal text names, filled from the <c>Legal</c> configuration section.</summary>
    public static readonly IReadOnlyDictionary<string, string> TokenSettings = new Dictionary<string, string>
    {
        ["AI_PROVIDER"] = "Legal:AiProvider",
        ["CONTENT_SAFETY_PROVIDER"] = "Legal:ContentSafetyProvider",
        ["HOSTING_PROVIDER"] = "Legal:HostingProvider",
        ["PRIVACY_EMAIL"] = "Legal:PrivacyEmail",
        ["ABUSE_EMAIL"] = "Legal:AbuseEmail",
    };

    private static IReadOnlyDictionary<string, string> _values = new Dictionary<string, string>();

    /// <summary>Supplies the token values (key = token name). Returns the setting names that are still missing.</summary>
    public static IReadOnlyList<string> Configure(Func<string, string?> setting)
    {
        var values = new Dictionary<string, string>();
        var missing = new List<string>();
        foreach (var (token, key) in TokenSettings)
        {
            if (setting(key) is { Length: > 0 } v) values[token] = v.Trim();
            else missing.Add(key);
        }
        _values = values;
        Docs = new(Load);
        return missing;
    }

    /// <summary>A missing value is shown as the setting name, so it is obvious in development and never silently blank.</summary>
    private static string Fill(string body)
    {
        foreach (var (token, key) in TokenSettings)
            body = body.Replace("{" + token + "}", _values.TryGetValue(token, out var v) ? v : $"({key} not set)");
        return body;
    }

    public static IReadOnlyCollection<LegalDocument> All => Docs.Value.Values.ToList();

    public static LegalDocument? Get(string key) => Docs.Value.GetValueOrDefault(key.ToLowerInvariant());

    public static IReadOnlyList<LegalDocumentVersion> Current =>
        RequireConsent.Select(k => Docs.Value[k]).Select(d => new LegalDocumentVersion(d.Key, d.Version, d.Effective)).ToList();

    private static IReadOnlyDictionary<string, LegalDocument> Load()
    {
        var asm = typeof(LegalDocuments).Assembly;
        var result = new Dictionary<string, LegalDocument>();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".md", StringComparison.Ordinal)))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            var text = r.ReadToEnd().Replace("\r\n", "\n");
            var key = Path.GetFileNameWithoutExtension(name).Split('.').Last();
            result[key] = Parse(key, text);
        }
        return result;
    }

    public static LegalDocument Parse(string key, string text)
    {
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) throw new FormatException($"{key}.md has no front matter");
        var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        var meta = text[4..end].Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(':', 2)).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        var body = Fill(text[(end + 5)..].Replace("{BRAND}", Brand.Name));
        return new LegalDocument(key, meta["title"], meta["version"],
            DateOnly.ParseExact(meta["effective"], "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Markdown.ToHtml(body, Pipeline));
    }
}
