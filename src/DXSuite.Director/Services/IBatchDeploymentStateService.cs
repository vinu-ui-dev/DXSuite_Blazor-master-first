namespace DXSuite.Director.Services;

/// <summary>
/// Scoped service for managing batch deployment UI state in Blazor.
/// Handles modal visibility, minimized state, and progress polling.
/// </summary>
public interface IBatchDeploymentStateService : IDisposable
{
    /// <summary>
    /// Whether the progress modal is currently visible.
    /// </summary>
    bool IsModalVisible { get; }

    /// <summary>
    /// Whether the minimized progress bar is visible in the toolbar.
    /// </summary>
    bool IsMinimized { get; }

    /// <summary>
    /// Current active deployment progress (null if none).
    /// </summary>
    BatchDeploymentProgress? CurrentProgress { get; }

    /// <summary>
    /// All active batch deployments.
    /// </summary>
    List<BatchDeploymentProgress> ActiveDeployments { get; }

    /// <summary>
    /// Whether there are any active batch deployments.
    /// </summary>
    bool HasActiveDeployments { get; }

    /// <summary>
    /// ID of the currently selected deployment (for modal display).
    /// </summary>
    int? SelectedDeploymentId { get; }

    /// <summary>
    /// Event fired when progress data is updated.
    /// </summary>
    event Action? OnProgressUpdated;

    /// <summary>
    /// Event fired when modal visibility changes.
    /// </summary>
    event Action? OnModalVisibilityChanged;

    /// <summary>
    /// Show the progress modal for a deployment.
    /// </summary>
    Task ShowModalAsync(int deploymentId);

    /// <summary>
    /// Hide the progress modal.
    /// </summary>
    void HideModal();

    /// <summary>
    /// Minimize the modal to toolbar bar.
    /// </summary>
    void Minimize();

    /// <summary>
    /// Expand from minimized state back to modal.
    /// </summary>
    void Expand();

    /// <summary>
    /// Dismiss the minimized bar (when deployment is complete).
    /// </summary>
    void Dismiss();

    /// <summary>
    /// Start polling for progress updates.
    /// </summary>
    void StartPolling();

    /// <summary>
    /// Stop polling for progress updates.
    /// </summary>
    void StopPolling();

    /// <summary>
    /// Force refresh progress data immediately.
    /// </summary>
    Task RefreshProgressAsync();
}
