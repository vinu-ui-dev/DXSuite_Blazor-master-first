using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.ContentSource;

/// <summary>
/// Queues a replaced or removed file for deletion after a grace period, so a screen still holding a cached URL to
/// the old file never 404s mid-request. Minimal, in-process implementation: a queued delete that hasn't fired yet
/// is lost if the process restarts, which only means a stale file outlives its grace period — never that a file
/// still in use is deleted early.
/// </summary>
public sealed class PendingFileDeletes
{
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Queues <paramref name="path"/> for deletion at <paramref name="readyUtc"/>. Takes <paramref name="db"/> only
    /// so call sites that already hold a <see cref="DirectorDbContext"/> from the surrounding save don't need a
    /// separate instance injected as well — the delete itself needs no transaction and does not touch <paramref name="db"/>.
    /// </summary>
    public static void Queue(DirectorDbContext db, string path, DateTime queuedUtc, DateTime readyUtc) =>
        _ = DeleteAfterAsync(path, readyUtc - queuedUtc);

    /// <summary>Instance form for callers injecting this as a service rather than holding a DbContext already.</summary>
    public void Add(string path, DateTime readyUtc) => _ = DeleteAfterAsync(path, readyUtc - DateTime.UtcNow);

    /// <summary>Queues a file a newly approved replacement made obsolete, kept for <see cref="DefaultGracePeriod"/>
    /// so a delivery cell that has not yet refreshed can still serve it (INK-16).</summary>
    public void QueueReplaced(DirectorDbContext db, string path) => Add(path, DateTime.UtcNow + DefaultGracePeriod);

    private static async Task DeleteAfterAsync(string path, TimeSpan delay)
    {
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort — a file still locked or already gone is not this queue's problem to report.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
