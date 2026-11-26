using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Infrastructure.Ai;
using OutfitFinder.Infrastructure.Data;
using OutfitFinder.Infrastructure.Imaging;
using OutfitFinder.Infrastructure.Patterns;
using OutfitFinder.Infrastructure.Safety;
using OutfitFinder.Infrastructure.Storage;

namespace OutfitFinder.Infrastructure;

public static class InfrastructureSetup
{
    /// <summary>Services used by the Api (search pipeline, storage, safety, AI, patterns).</summary>
    public static IServiceCollection AddOutfitInfrastructure(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.AddOutfitDatabase(config);
        services.AddDataProtection().SetApplicationName("OutfitFinder").PersistKeysToDbContext<OutfitDbContext>();

        services.AddSingleton<IImageProcessor, ImageProcessor>();
        services.AddSingleton<PatternEngine>();

        // Blob storage
        services.AddSingleton<IBlobStore>(sp =>
        {
            var provider = config["Storage:Provider"] ?? "Azure";
            return provider switch
            {
                "Azure" => new AzureBlobStore(
                    Require(config, "Storage:BlobConnection"), config["Storage:Container"] ?? "outfits"),
                "FileSystem" when !env.IsProduction() => new FileSystemBlobStore(
                    // Relative roots are taken from the project folder, so IIS Express and Kestrel share the same files.
                    Path.GetFullPath(Path.Combine(env.ContentRootPath, config["Storage:Root"] ?? Path.Combine("App_Data", "blobs")))),
                _ => throw new InvalidOperationException($"Storage:Provider '{provider}' is not allowed here. Use Azure."),
            };
        });

        // Moderation: Azure AI Content Safety, or an explicit development bypass.
        var safetyMode = config["Safety:Mode"] ?? "AzureContentSafety";
        if (safetyMode == "DevelopmentBypass")
        {
            if (!env.IsDevelopment()) throw new InvalidOperationException("Safety:Mode=DevelopmentBypass is only allowed in Development.");
            services.AddSingleton<IImageModerator, DevelopmentBypassModerator>();
        }
        else
        {
            services.AddSingleton<IImageModerator>(_ => AzureContentSafetyModerator.Create(
                Require(config, "Safety:ContentSafetyEndpoint"), Require(config, "Safety:ContentSafetyKey"),
                config.GetValue("Safety:RejectSeverity", 2)));
        }

        services.AddHttpClient<ILinkChecker, LinkChecker>(c =>
        {
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; OutfitFinderLinkCheck/1.0)");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 });

        services.AddHttpClient<ISafeImageFetcher, SafeImageFetcher>(c =>
        {
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; OutfitFinderFetch/1.0)");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddHttpClient("turnstile");
        services.AddSingleton<ICaptchaVerifier>(sp => new TurnstileVerifier(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("turnstile"), config["Turnstile:SecretKey"],
            sp.GetRequiredService<ILogger<TurnstileVerifier>>()));

        services.AddHttpClient("safebrowsing");
        services.AddSingleton<IOutboundLinkSafety>(sp => new SafeBrowsingChecker(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("safebrowsing"), config["Safety:SafeBrowsingApiKey"],
            sp.GetRequiredService<ILogger<SafeBrowsingChecker>>()));

        // AI adapters (one per provider) + settings resolved per call.
        services.AddHttpClient<OpenAiAnalyzer>(c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient<AzureFoundryAnalyzer>(c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient<GeminiAnalyzer>(c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<DemoAnalyzer>();
        services.AddSingleton<IAnalyzerFactory, AnalyzerFactory>();
        services.AddScoped<IAiSettingsProvider, AiSettingsProvider>();
        return services;
    }

    public static string Require(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } v ? v : throw new InvalidOperationException(MissingSetting(key));

    /// <summary>Points at the quickest fix: most local failures are someone running the "MySQL (Docker)" profile without secrets.</summary>
    public static string MissingSetting(string key) =>
        $"Configuration value '{key}' is required. To try the app without any keys, start the solution with the " +
        "\"Demo (SQLite, no keys)\" launch profile (it starts the sign-in server, API and website together). " +
        "For the \"MySQL (Docker)\" profile, set it with dotnet user-secrets as shown in README.md; in Azure it comes from Key Vault.";
}
