using System.Text.RegularExpressions;
using OutfitFinder.Core;
using OutfitFinder.UI.Legal;

namespace OutfitFinder.Tests;

/// <summary>Design-system guards: colors only in tokens.css, fonts only in tokens/fonts.css, brand only from BrandName.</summary>
public partial class GuardTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OutfitFinder.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    private static IEnumerable<string> UiFiles(params string[] extensions) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}"));

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b(?![\w-])|rgba?\(|\bhsla?\(")] private static partial Regex ColorPattern();
    [GeneratedRegex(@"font-family\s*:(?>\s*)(?!var\(|inherit)[^;""]+")] private static partial Regex FontFamilyPattern();

    [Fact]
    public void No_color_literals_outside_tokens_css()
    {
        var offenders = UiFiles(".css", ".razor", ".cshtml", ".js", ".html")
            .Where(f => Path.GetFileName(f) != "tokens.css")
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            // ignore element ids like href="#main" and entity refs
            .Where(x => ColorPattern().Matches(x.line).Any(m => !x.line.Contains($"\"{m.Value}") && !x.line.Contains($"'{m.Value}") && !x.line.Contains($"&{m.Value}")))
            .Select(x => $"{Path.GetRelativePath(Root, x.f)}:{x.i + 1}: {x.line.Trim()}").ToList();
        Assert.True(offenders.Count == 0, "Color literals found (use tokens.css variables):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void No_font_names_outside_tokens_and_fonts_css()
    {
        var offenders = UiFiles(".css", ".razor", ".cshtml")
            .Where(f => Path.GetFileName(f) is not ("tokens.css" or "fonts.css"))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => FontFamilyPattern().IsMatch(x.line) || Regex.IsMatch(x.line, "(?<![\\w.])(DM Sans|'Outfit'|\"Outfit\")"))
            .Select(x => $"{Path.GetRelativePath(Root, x.f)}:{x.i + 1}: {x.line.Trim()}").ToList();
        Assert.True(offenders.Count == 0, "Font names found (use --font-* variables):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void No_hard_coded_brand_name()
    {
        var offenders = UiFiles(".cs", ".razor", ".cshtml", ".js", ".css", ".md", ".json")
            .Where(f => File.ReadAllText(f).Contains(Brand.Name, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Root, f)).ToList();
        Assert.True(offenders.Count == 0, "Brand name hard-coded (use Brand.Name or {BRAND}):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void No_secrets_in_appsettings()
    {
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Root, "src"), "appsettings*.json", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(f);
            var name = Path.GetFileName(f);
            // Real third-party keys and passwords never belong in any settings file (they're public in the repo).
            Assert.False(Regex.IsMatch(text, "\"(ApiKey|ContentSafetyKey|SafeBrowsingApiKey|Password|LicenseKey|BlobConnection)\"\\s*:\\s*\"(?!UseDevelopmentStorage=true\")[^\"]+\""),
                $"{f}: real keys go in user-secrets (local) or Key Vault (Azure)");
            Assert.DoesNotMatch("sk-[A-Za-z0-9_-]{16,}|AIza[0-9A-Za-z_-]{30,}", text);
            // Production settings (appsettings.json) hold no secrets at all. The local-only Development files may hold the
            // local sign-in secret and URL signing key, which only work against this machine's localhost.
            if (!name.Equals("appsettings.Development.json", StringComparison.OrdinalIgnoreCase))
                Assert.DoesNotMatch("\"(Secret|ClientSecret|UrlSigningKey|SecretKey)\"\\s*:\\s*\"[^\"]+\"", text);
        }
    }

    [Fact]
    public void Legal_documents_have_versions_and_use_the_brand_token()
    {
        Assert.Equal(3, LegalDocuments.All.Count);
        foreach (var d in LegalDocuments.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Version));
            Assert.DoesNotContain("{BRAND}", d.Html);
        }
        Assert.Contains(Brand.Name, LegalDocuments.Get("privacy")!.Html);
        Assert.Contains(LegalDocuments.Current, c => c.Document == "terms");
    }

    [Fact]
    public void Legal_documents_fill_every_provider_and_contact_token()
    {
        var values = LegalDocuments.TokenSettings.Values.ToDictionary(k => k, k => "VALUE-" + k.Split(':')[1]);
        Assert.Empty(LegalDocuments.Configure(k => values.GetValueOrDefault(k)));
        var all = string.Join("\n", LegalDocuments.All.Select(d => d.Html));
        Assert.Contains("VALUE-AiProvider", all);
        Assert.Contains("VALUE-PrivacyEmail", all);
        Assert.Contains("VALUE-AbuseEmail", all);
        Assert.DoesNotMatch(@"\{[A-Z_]+\}|\[[A-Z ]{4,}\]", all); // no token or old [PLACEHOLDER] left anywhere

        Assert.Contains("Legal:PrivacyEmail", LegalDocuments.Configure(_ => null));
    }
}
