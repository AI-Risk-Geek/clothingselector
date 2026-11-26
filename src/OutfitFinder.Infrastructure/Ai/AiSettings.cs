using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Infrastructure.Ai;

public sealed class AiOptions
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string? Deployment { get; set; }
    public string? Endpoint { get; set; }
    public string ApiKey { get; set; } = "";
    public bool WebSearchEnabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// The admin page row wins (so a provider switch takes effect on the next search without a restart);
/// otherwise configuration. Unknown or empty provider throws: no silent fallback.
/// </summary>
public sealed class AiSettingsProvider(OutfitDbContext db, IConfiguration config, IDataProtectionProvider dp) : IAiSettingsProvider
{
    public const string ProtectorPurpose = "OutfitFinder.AiProviderSetting.ApiKey";

    public async Task<AiRuntimeSettings> GetAsync(CancellationToken ct)
    {
        var options = config.GetSection("AI").Get<AiOptions>() ?? new AiOptions();
        var row = await db.AiProviderSettings.AsNoTracking().OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(ct);
        if (row is not null)
        {
            var key = string.IsNullOrEmpty(row.EncryptedKey) ? "" : dp.CreateProtector(ProtectorPurpose).Unprotect(row.EncryptedKey);
            return new AiRuntimeSettings(row.Provider, row.Model, row.Deployment, row.Endpoint, key, row.WebSearchEnabled, options.TimeoutSeconds);
        }
        return FromOptions(options);
    }

    public static AiRuntimeSettings FromOptions(AiOptions o)
    {
        if (!Enum.TryParse<AiProviderKind>(o.Provider, ignoreCase: true, out var kind))
            throw new AiNotConfiguredException(string.IsNullOrWhiteSpace(o.Provider)
                ? "AI:Provider is not set. Set it to AzureAiFoundry, OpenAI or Google (or configure it on /admin/ai)."
                : $"AI:Provider '{o.Provider}' is not supported. Use AzureAiFoundry, OpenAI or Google.");
        return new AiRuntimeSettings(kind, o.Model, o.Deployment, o.Endpoint, o.ApiKey, o.WebSearchEnabled, o.TimeoutSeconds);
    }
}

public sealed class AnalyzerFactory(IServiceProvider sp, Microsoft.Extensions.Hosting.IHostEnvironment env) : IAnalyzerFactory
{
    public IOutfitAnalyzer For(AiProviderKind kind) => kind switch
    {
        AiProviderKind.OpenAI => sp.GetRequiredService<OpenAiAnalyzer>(),
        AiProviderKind.AzureAiFoundry => sp.GetRequiredService<AzureFoundryAnalyzer>(),
        AiProviderKind.Google => sp.GetRequiredService<GeminiAnalyzer>(),
        AiProviderKind.Demo when env.EnvironmentName is "Development" or "Testing" => sp.GetRequiredService<DemoAnalyzer>(),
        AiProviderKind.Demo => throw new AiNotConfiguredException("The Demo AI provider is only allowed in Development."),
        _ => throw new AiNotConfiguredException($"No adapter for {kind}."),
    };
}
