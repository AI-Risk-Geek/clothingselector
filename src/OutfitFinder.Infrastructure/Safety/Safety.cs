using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Azure;
using Azure.AI.ContentSafety;
using Microsoft.Extensions.Logging;
using OutfitFinder.Core.Abstractions;

namespace OutfitFinder.Infrastructure.Safety;

/// <summary>Azure AI Content Safety image check; runs on every image before anything else.</summary>
public sealed class AzureContentSafetyModerator(ContentSafetyClient client, int rejectSeverity) : IImageModerator
{
    public async Task<ModerationResult> CheckAsync(byte[] image, CancellationToken ct)
    {
        var result = await client.AnalyzeImageAsync(new AnalyzeImageOptions(new ContentSafetyImageData(new BinaryData(image))), ct);
        var worst = result.Value.CategoriesAnalysis.OrderByDescending(c => c.Severity ?? 0).FirstOrDefault();
        var severity = worst?.Severity ?? 0;
        return severity >= rejectSeverity
            ? new ModerationResult(false, $"{worst!.Category} severity {severity}", severity)
            : new ModerationResult(true, null, severity);
    }

    public static AzureContentSafetyModerator Create(string endpoint, string key, int rejectSeverity) =>
        new(new ContentSafetyClient(new Uri(endpoint), new AzureKeyCredential(key)), rejectSeverity);
}

/// <summary>
/// Development-only stand-in, chosen explicitly with Safety:Mode=DevelopmentBypass. The host refuses to start
/// with this outside the Development environment.
/// </summary>
public sealed class DevelopmentBypassModerator(ILogger<DevelopmentBypassModerator> log) : IImageModerator
{
    public Task<ModerationResult> CheckAsync(byte[] image, CancellationToken ct)
    {
        log.LogWarning("Content Safety is BYPASSED (Safety:Mode=DevelopmentBypass). Never use this outside local development.");
        return Task.FromResult(new ModerationResult(true, null, 0));
    }
}

/// <summary>HEAD (then GET) each URL in parallel with a 5 s timeout; keeps those that answer below 400.</summary>
public sealed class LinkChecker(HttpClient http, ILogger<LinkChecker> log) : ILinkChecker
{
    public async Task<IReadOnlySet<string>> FindLiveAsync(IEnumerable<string> urls, CancellationToken ct)
    {
        var live = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        await Parallel.ForEachAsync(urls.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (url, token) =>
        {
            if (await IsLiveAsync(url, token)) live[url] = true;
        });
        return live.Keys.ToHashSet();
    }

    private async Task<bool> IsLiveAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return false;
        if (await UrlGuard.IsBlockedHostAsync(uri, ct)) return false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, uri);
            using var r = await http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if ((int)r.StatusCode < 400) return true;
            if (r.StatusCode is not (HttpStatusCode.MethodNotAllowed or HttpStatusCode.Forbidden or HttpStatusCode.NotImplemented)) return false;
            using var get = new HttpRequestMessage(HttpMethod.Get, uri);
            using var r2 = await http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            // Many stores answer bots with 403 even for live pages; treat 403 as "unknown but present".
            return (int)r2.StatusCode < 400 || r2.StatusCode == HttpStatusCode.Forbidden;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            log.LogDebug("Link check failed for {Host}: {Error}", uri.Host, e.Message);
            return false;
        }
    }
}

/// <summary>SSRF protections shared by the URL fetcher and the link checker.</summary>
public static class UrlGuard
{
    public static async Task<bool> IsBlockedHostAsync(Uri uri, CancellationToken ct)
    {
        if (uri.IsLoopback) return true;
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal)) addresses = [literal];
        else
        {
            try { addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct); }
            catch (SocketException) { return true; }
        }
        return addresses.Length == 0 || addresses.Any(IsPrivate);
    }

    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6Multicast;
        var b = ip.GetAddressBytes();
        return b[0] switch
        {
            0 or 10 or 127 => true,
            100 => b[1] >= 64 && b[1] <= 127,          // carrier-grade NAT
            169 => b[1] == 254,                          // link-local incl. cloud metadata 169.254.169.254
            172 => b[1] >= 16 && b[1] <= 31,
            192 => (b[1] == 168) || (b[1] == 0 && b[2] == 0) || (b[1] == 0 && b[2] == 2),
            198 => b[1] == 18 || b[1] == 19,
            >= 224 => true,                              // multicast / reserved
            _ => false,
        };
    }
}

/// <summary>Fetches an image URL for the extension's right-click flow with SSRF protections.</summary>
public sealed class SafeImageFetcher(HttpClient http) : ISafeImageFetcher
{
    public const int MaxBytes = 10 * 1024 * 1024;
    private const int MaxRedirects = 3;

    public async Task<byte[]> FetchAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new ImageFetchException("That is not a valid link.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps) throw new ImageFetchException("Only https image links are supported.");
                if (await UrlGuard.IsBlockedHostAsync(uri, cts.Token)) throw new ImageFetchException("That address is not allowed.");

                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                req.Headers.Accept.ParseAdd("image/jpeg,image/png,image/webp");
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } loc)
                {
                    uri = loc.IsAbsoluteUri ? loc : new Uri(uri, loc);
                    continue;
                }
                if (!res.IsSuccessStatusCode) throw new ImageFetchException($"The image could not be downloaded ({(int)res.StatusCode}).");
                var type = res.Content.Headers.ContentType?.MediaType ?? "";
                if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) throw new ImageFetchException("That link is not an image.");
                if (res.Content.Headers.ContentLength > MaxBytes) throw new ImageFetchException("The image is larger than 10 MB.");

                await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
                using var ms = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, cts.Token)) > 0)
                {
                    ms.Write(buffer, 0, read);
                    if (ms.Length > MaxBytes) throw new ImageFetchException("The image is larger than 10 MB.");
                }
                return ms.ToArray();
            }
            throw new ImageFetchException("Too many redirects.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ImageFetchException("The image took too long to download.");
        }
        catch (HttpRequestException e)
        {
            throw new ImageFetchException($"The image could not be downloaded ({e.Message}).");
        }
    }
}

/// <summary>Cloudflare Turnstile verification.</summary>
public sealed class TurnstileVerifier(HttpClient http, string? secret, ILogger<TurnstileVerifier> log) : ICaptchaVerifier
{
    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Turnstile:SecretKey is not configured.");
        if (string.IsNullOrWhiteSpace(token)) return false;
        var form = new Dictionary<string, string> { ["secret"] = secret, ["response"] = token };
        if (remoteIp is not null) form["remoteip"] = remoteIp;
        try
        {
            using var res = await http.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", new FormUrlEncodedContent(form), ct);
            var json = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            return json.TryGetProperty("success", out var s) && s.GetBoolean();
        }
        catch (HttpRequestException e)
        {
            log.LogError(e, "Turnstile verification failed");
            return false;
        }
    }
}

/// <summary>Google Safe Browsing v4 lookup for outbound store links.</summary>
public sealed class SafeBrowsingChecker(HttpClient http, string? apiKey, ILogger<SafeBrowsingChecker> log) : IOutboundLinkSafety
{
    public async Task<IReadOnlySet<string>> FindUnsafeAsync(IEnumerable<string> urls, CancellationToken ct)
    {
        var list = urls.Distinct().ToList();
        if (list.Count == 0 || string.IsNullOrWhiteSpace(apiKey)) return new HashSet<string>();
        var body = new
        {
            client = new { clientId = "outfit-finder", clientVersion = "1.0" },
            threatInfo = new
            {
                threatTypes = new[] { "MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE", "POTENTIALLY_HARMFUL_APPLICATION" },
                platformTypes = new[] { "ANY_PLATFORM" },
                threatEntryTypes = new[] { "URL" },
                threatEntries = list.Select(u => new { url = u }).ToArray(),
            },
        };
        try
        {
            using var res = await http.PostAsJsonAsync($"https://safebrowsing.googleapis.com/v4/threatMatches:find?key={apiKey}", body, ct);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            var bad = new HashSet<string>();
            if (json.TryGetProperty("matches", out var matches))
                foreach (var m in matches.EnumerateArray()) bad.Add(m.GetProperty("threat").GetProperty("url").GetString()!);
            return bad;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Fail closed would hide every result when Google is down; we log loudly instead.
            log.LogError(e, "Safe Browsing lookup failed; links were not screened for this search");
            return new HashSet<string>();
        }
    }
}
