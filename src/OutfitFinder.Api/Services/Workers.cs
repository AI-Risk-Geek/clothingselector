using Microsoft.EntityFrameworkCore;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Api.Services;

/// <summary>
/// EF-backed queue. Polls Pending rows, claims one with an optimistic concurrency token (so several Api
/// instances never process the same row), and reclaims rows left Running by a crash or restart.
/// </summary>
public sealed class SearchWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<SearchWorker> log) : BackgroundService
{
    private readonly TimeSpan _poll = TimeSpan.FromMilliseconds(config.GetValue("Worker:PollMilliseconds", 1500));
    private readonly TimeSpan _stuckAfter = TimeSpan.FromSeconds(config.GetValue("Worker:StuckAfterSeconds", 300));
    private readonly int _parallel = config.GetValue("Worker:Parallel", 2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = new List<Task>();
        while (!stoppingToken.IsCancellationRequested)
        {
            running.RemoveAll(t => t.IsCompleted);
            try
            {
                if (running.Count < _parallel && await ClaimNextAsync(stoppingToken) is { } id)
                {
                    running.Add(Task.Run(() => ProcessAsync(id, stoppingToken), stoppingToken));
                    continue; // look for more work straight away
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Search worker poll failed");
            }
            try { await Task.Delay(_poll, stoppingToken); } catch (OperationCanceledException) { break; }
        }
        await Task.WhenAll(running.Select(t => t.ContinueWith(_ => { }, TaskScheduler.Default)));
    }

    internal async Task<Guid?> ClaimNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
        var stuckBefore = DateTime.UtcNow - _stuckAfter;

        // Rows stuck past the retry limit are failed with a readable message.
        var dead = await db.Searches.Where(s => s.Status == SearchStatus.Running && s.ClaimedUtc < stuckBefore && s.AttemptCount >= SearchPipeline.MaxAttempts).ToListAsync(ct);
        foreach (var d in dead)
        {
            d.Status = SearchStatus.Failed; d.Stage = SearchStage.Done; d.CompletedUtc = DateTime.UtcNow;
            d.Error = "This search took too long and was stopped. Please try again.";
            d.RowVersion = Guid.NewGuid();
        }
        if (dead.Count > 0) { try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { } }

        var candidate = await db.Searches
            .Where(s => s.Status == SearchStatus.Pending || (s.Status == SearchStatus.Running && s.ClaimedUtc < stuckBefore && s.AttemptCount < SearchPipeline.MaxAttempts))
            .OrderBy(s => s.CreatedUtc).FirstOrDefaultAsync(ct);
        if (candidate is null) return null;

        candidate.Status = SearchStatus.Running;
        candidate.ClaimedUtc = DateTime.UtcNow;
        candidate.AttemptCount++;
        candidate.RowVersion = Guid.NewGuid(); // the original value is the concurrency check
        try
        {
            await db.SaveChangesAsync(ct);
            return candidate.Id;
        }
        catch (DbUpdateConcurrencyException)
        {
            return null; // another worker won
        }
    }

    private async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SearchPipeline>().ProcessAsync(id, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { log.LogError(e, "Unhandled error processing search {Id}", id); }
    }
}

/// <summary>Nightly: delete searches (and blobs) older than Retention:Days, and stray uploads.</summary>
public sealed class RetentionWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<RetentionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(DateTime.UtcNow, stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Retention run failed"); }
            var next = DateTime.UtcNow.Date.AddDays(1).AddHours(3); // 03:00 UTC
            try { await Task.Delay(next - DateTime.UtcNow, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        var days = config.GetValue("Retention:Days", 90);
        var cutoff = nowUtc.AddDays(-days);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
        var blobs = scope.ServiceProvider.GetRequiredService<IBlobStore>();
        var deleter = scope.ServiceProvider.GetRequiredService<DataDeleter>();

        var old = await db.Searches.Where(s => s.CreatedUtc < cutoff).Select(s => s.Id).ToListAsync(ct);
        foreach (var batch in old.Chunk(100)) await deleter.DeleteSearchesAsync(batch, ct);

        db.ModerationEvents.RemoveRange(await db.ModerationEvents.Where(m => m.CreatedUtc < cutoff).ToListAsync(ct));
        db.ProblemReports.RemoveRange(await db.ProblemReports.Where(m => m.CreatedUtc < cutoff.AddDays(-275)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        // Uploads never processed (e.g. a crash between upload and claim) older than a day.
        var activePending = (await db.Searches.Where(s => s.PendingBlobPath != null).Select(s => s.PendingBlobPath!).ToListAsync(ct)).ToHashSet();
        await foreach (var path in blobs.ListAsync("pending/", ct))
            if (!activePending.Contains(path)) await blobs.DeleteAsync(path, ct);

        log.LogInformation("Retention: deleted {Count} searches older than {Days} days", old.Count, days);
        return old.Count;
    }
}
