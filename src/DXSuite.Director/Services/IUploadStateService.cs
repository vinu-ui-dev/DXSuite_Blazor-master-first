namespace DXSuite.Director.Services;

public interface IUploadStateService
{
    IReadOnlyList<UploadTask> Tasks { get; }
    bool HasVisibleBar { get; }
    Guid? PendingExpandTaskId { get; }
    event Action OnChanged;
    event Action<Guid> OnExpandRequested;

    UploadTask StartUpload(string fileName, int locationId);
    void UpdateProgress(Guid id, int percent, string statusMessage);
    void MarkAwaitingSubmit(Guid id);
    void CompleteUpload(Guid id);
    void FailUpload(Guid id, string error);
    void CancelUpload(Guid id);
    void DismissTask(Guid id);
    void RequestExpand(Guid id);
    void SetPendingExpand(Guid id);
    void ClearPendingExpand();
    UploadTask? GetTask(Guid id);
}
