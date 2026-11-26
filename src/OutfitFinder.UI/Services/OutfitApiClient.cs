using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OutfitFinder.Core.Contracts;
using OutfitFinder.Core.Entities;

namespace OutfitFinder.UI.Services;

/// <summary>Supplies the anonymous visitor id (website cookie or extension storage). Null when the visitor declined.</summary>
public interface IVisitorIdProvider
{
    ValueTask<Guid?> GetAsync();
}

public sealed class ApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Typed client for OutfitFinder.Api, shared by the website (server-side) and the extension (WASM).</summary>
public sealed class OutfitApiClient(HttpClient http, IVisitorIdProvider visitor)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Uri? BaseAddress => http.BaseAddress;

    private async Task<HttpRequestMessage> Request(HttpMethod method, string url, HttpContent? content = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        if (await visitor.GetAsync() is { } id) req.Headers.Add(Headers.VisitorId, id.ToString());
        return req;
    }

    private async Task<T> Send<T>(HttpMethod method, string url, object? body = null, CancellationToken ct = default)
    {
        using var req = await Request(method, url, body is null ? null : JsonContent.Create(body, options: Json));
        using var res = await http.SendAsync(req, ct);
        await EnsureOk(res, ct);
        return (await res.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task Send(HttpMethod method, string url, object? body = null, CancellationToken ct = default)
    {
        using var req = await Request(method, url, body is null ? null : JsonContent.Create(body, options: Json));
        using var res = await http.SendAsync(req, ct);
        await EnsureOk(res, ct);
    }

    private static async Task EnsureOk(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        string message;
        try { message = (await res.Content.ReadFromJsonAsync<ApiError>(Json, ct))?.Message ?? ""; }
        catch (Exception) { message = ""; }
        if (string.IsNullOrWhiteSpace(message))
            message = res.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Please sign in to do that.",
                HttpStatusCode.Forbidden => "You don't have access to that.",
                HttpStatusCode.NotFound => "We couldn't find that look. It may have been deleted.",
                HttpStatusCode.TooManyRequests => "You've reached today's search limit. Try again tomorrow.",
                _ => "Something went wrong. Please try again.",
            };
        throw new ApiException(res.StatusCode, message);
    }

    // ----- Searches -----

    public async Task<Guid> CreateSearchAsync(Stream file, string fileName, string contentType, SearchSource source, string? turnstileToken, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var stream = new StreamContent(file);
        stream.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var ctHeader) ? ctHeader : new MediaTypeHeaderValue("application/octet-stream");
        form.Add(stream, "photo", fileName);
        form.Add(new StringContent(source.ToString()), "source");
        using var req = await Request(HttpMethod.Post, "api/searches", form);
        if (!string.IsNullOrEmpty(turnstileToken)) req.Headers.Add(Headers.Turnstile, turnstileToken);
        using var res = await http.SendAsync(req, ct);
        await EnsureOk(res, ct);
        return (await res.Content.ReadFromJsonAsync<CreatedSearchDto>(Json, ct))!.Id;
    }

    public async Task<Guid> CreateSearchFromUrlAsync(string url, string? pageUrl, CancellationToken ct = default) =>
        (await Send<CreatedSearchDto>(HttpMethod.Post, "api/searches/from-url", new SearchFromUrlRequest(url, pageUrl), ct)).Id;

    public Task<SearchDto> GetSearchAsync(Guid id, CancellationToken ct = default) => Send<SearchDto>(HttpMethod.Get, $"api/searches/{id}", ct: ct);

    public Task<List<SearchSummaryDto>> ListSearchesAsync(int take = 60, CancellationToken ct = default) => Send<List<SearchSummaryDto>>(HttpMethod.Get, $"api/searches?take={take}", ct: ct);

    public Task DeleteSearchAsync(Guid id) => Send(HttpMethod.Delete, $"api/searches/{id}");

    public Task<CreatedSearchDto> SearchAgainAsync(Guid id) => Send<CreatedSearchDto>(HttpMethod.Post, $"api/searches/{id}/search-again");

    public Task<SearchDto> AdjustItemsAsync(Guid id, AdjustItemsRequest request) => Send<SearchDto>(HttpMethod.Put, $"api/searches/{id}/items", request);

    public Task SetFeedbackAsync(Guid matchId, MatchFeedback? feedback) => Send(HttpMethod.Put, $"api/matches/{matchId}/feedback", new FeedbackRequest(feedback));

    public Task ReportAsync(ReportRequest report) => Send(HttpMethod.Post, "api/reports", report);

    // ----- Make it -----

    public Task<PatternDto> CreatePatternAsync(Guid itemId, PatternRequest request) => Send<PatternDto>(HttpMethod.Post, $"api/items/{itemId}/pattern", request);

    public Task<FindMaterialResult> FindMaterialAsync(Guid materialId) => Send<FindMaterialResult>(HttpMethod.Post, $"api/materials/{materialId}/find");

    public Task<List<MeasurementProfileDto>> GetMeasurementsAsync() => Send<List<MeasurementProfileDto>>(HttpMethod.Get, "api/me/measurements");

    public Task<MeasurementProfileDto> SaveMeasurementsAsync(MeasurementProfileDto profile) => Send<MeasurementProfileDto>(HttpMethod.Put, "api/me/measurements", profile);

    public Task DeleteMeasurementsAsync(Guid id) => Send(HttpMethod.Delete, $"api/me/measurements/{id}");

    // ----- Me / privacy -----

    public Task MergeVisitorAsync(Guid visitorId) => Send(HttpMethod.Post, $"api/me/merge-visitor/{visitorId}");

    public Task ClearHistoryAsync() => Send(HttpMethod.Delete, "api/me/history");

    public Task DeleteAccountAsync() => Send(HttpMethod.Delete, "api/me");

    public async Task<byte[]> ExportAsync()
    {
        using var req = await Request(HttpMethod.Get, "api/me/export");
        using var res = await http.SendAsync(req);
        await EnsureOk(res, default);
        return await res.Content.ReadAsByteArrayAsync();
    }

    public Task<ConsentStatusDto> GetConsentStatusAsync() => Send<ConsentStatusDto>(HttpMethod.Get, "api/me/consents");

    public Task AcceptConsentAsync(ConsentRequest consent) => Send(HttpMethod.Post, "api/me/consents", consent);

    // ----- Admin -----

    public Task<AiSettingsDto> GetAiSettingsAsync() => Send<AiSettingsDto>(HttpMethod.Get, "api/admin/ai");

    public Task<AiSettingsDto> SaveAiSettingsAsync(AiSettingsUpdate update) => Send<AiSettingsDto>(HttpMethod.Put, "api/admin/ai", update);

    public Task<AiTestResult> TestAiAsync(AiSettingsUpdate update) => Send<AiTestResult>(HttpMethod.Post, "api/admin/ai/test", update);

    public Task<List<AuditEntryDto>> GetAuditAsync() => Send<List<AuditEntryDto>>(HttpMethod.Get, "api/admin/audit");

    public Task<Dictionary<string, string>> GetPublicConfigAsync() => Send<Dictionary<string, string>>(HttpMethod.Get, "api/config");
}
