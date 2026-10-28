namespace DXSuite.Director.Services;

public class UploadStateService : IUploadStateService
{
    private readonly List<UploadTask> _tasks = new();
    private readonly ILogger<UploadStateService> _logger;

    public IReadOnlyList<UploadTask> Tasks => _tasks;
    public bool HasVisibleBar => _tasks.Count > 0;
    public Guid? PendingExpandTaskId { get; private set; }

    public event Action? OnChanged;

    // Fired when a component requests that a specific task's modal be re-opened
    public event Action<Guid>? OnExpandRequested;

    public UploadStateService(ILogger<UploadStateService> logger)
    {
        _logger = logger;
    }

    public UploadTask StartUpload(string fileName, int locationId)
    {
        var task = new UploadTask
        {
            FileName = fileName,
            LocationId = locationId,
            Stage = "uploading",
            ProgressPercent = 0,
            StatusMessage = "Uploading…"
        };
        _tasks.Add(task);
        OnChanged?.Invoke();
        return task;
    }

    public void UpdateProgress(Guid id, int percent, string statusMessage)
    {
        var task = GetTask(id);
        if (task == null) return;
        task.ProgressPercent = percent;
        task.StatusMessage = statusMessage;
        OnChanged?.Invoke();
    }

    public void MarkAwaitingSubmit(Guid id)
    {
        var task = GetTask(id);
        if (task == null) return;
        task.Stage = "awaiting-submit";
        task.ProgressPercent = 100;
        task.StatusMessage = "Ready — click to submit";
        task.NeedsAttention = true;
        OnChanged?.Invoke();

        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ =>
        {
            task.NeedsAttention = false;
            try { OnChanged?.Invoke(); } catch { }
        });
    }

    public void CompleteUpload(Guid id)
    {
        var task = GetTask(id);
        if (task == null) return;
        task.Stage = "complete";
        task.ProgressPercent = 100;
        task.StatusMessage = "Complete";
        task.NeedsAttention = true;
        OnChanged?.Invoke();

        // Clear flash after 3s, then fade out and remove at 4.5s
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ =>
        {
            task.NeedsAttention = false;
            try { OnChanged?.Invoke(); } catch { /* circuit may be gone */ }
            return Task.Delay(TimeSpan.FromSeconds(1.5));
        }).Unwrap().ContinueWith(_ => DismissTask(id));
    }

    public void FailUpload(Guid id, string error)
    {
        var task = GetTask(id);
        if (task == null) return;
        task.Stage = "failed";
        task.ErrorMessage = error;
        task.StatusMessage = "Failed";
        task.NeedsAttention = true;
        OnChanged?.Invoke();

        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ =>
        {
            task.NeedsAttention = false;
            try { OnChanged?.Invoke(); } catch { }
        });
    }

    public void CancelUpload(Guid id)
    {
        var task = GetTask(id);
        if (task == null) return;
        try { task.Cts.Cancel(); } catch (Exception ex) { _logger.LogWarning(ex, "Error cancelling upload CTS for {Id}", id); }
        task.Stage = "cancelled";
        task.StatusMessage = "Cancelled";
        OnChanged?.Invoke();
    }

    public void DismissTask(Guid id)
    {
        var task = GetTask(id);
        if (task == null) return;

        // If already fading, skip straight to removal to avoid double-delay
        if (task.IsAutoRemoving)
        {
            _tasks.Remove(task);
            try { OnChanged?.Invoke(); } catch { }
            return;
        }

        // Trigger fade-out, then remove after the CSS transition completes
        task.IsAutoRemoving = true;
        try { OnChanged?.Invoke(); } catch { }
        _ = Task.Delay(TimeSpan.FromMilliseconds(500)).ContinueWith(_ =>
        {
            _tasks.Remove(task);
            try { OnChanged?.Invoke(); } catch { }
        });
    }

    public void RequestExpand(Guid id)
    {
        OnExpandRequested?.Invoke(id);
    }

    public void SetPendingExpand(Guid id) => PendingExpandTaskId = id;
    public void ClearPendingExpand() => PendingExpandTaskId = null;

    public UploadTask? GetTask(Guid id) => _tasks.FirstOrDefault(t => t.Id == id);
}
