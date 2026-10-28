namespace DXSuite.Director.Services;

public class BatchDeploymentStateService : IBatchDeploymentStateService
{
    private readonly IBatchDeploymentService _batchService;
    private readonly ILogger<BatchDeploymentStateService> _logger;
    private Timer? _pollingTimer;
    private bool _isPolling;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    public bool IsModalVisible { get; private set; }
    public bool IsMinimized { get; private set; }
    public BatchDeploymentProgress? CurrentProgress { get; private set; }
    public List<BatchDeploymentProgress> ActiveDeployments { get; private set; } = new();
    public bool HasActiveDeployments => ActiveDeployments.Any();
    public int? SelectedDeploymentId { get; private set; }

    public event Action? OnProgressUpdated;
    public event Action? OnModalVisibilityChanged;

    public BatchDeploymentStateService(
        IBatchDeploymentService batchService,
        ILogger<BatchDeploymentStateService> logger)
    {
        _batchService = batchService;
        _logger = logger;
    }

    public async Task ShowModalAsync(int deploymentId)
    {
        SelectedDeploymentId = deploymentId;
        IsModalVisible = true;
        IsMinimized = false;

        await RefreshProgressAsync();

        OnModalVisibilityChanged?.Invoke();
    }

    public void HideModal()
    {
        IsModalVisible = false;
        OnModalVisibilityChanged?.Invoke();
    }

    public void Minimize()
    {
        IsModalVisible = false;
        IsMinimized = true;
        OnModalVisibilityChanged?.Invoke();
    }

    public void Expand()
    {
        IsModalVisible = true;
        IsMinimized = false;
        OnModalVisibilityChanged?.Invoke();
    }

    public void Dismiss()
    {
        IsModalVisible = false;
        IsMinimized = false;
        SelectedDeploymentId = null;
        CurrentProgress = null;
        OnModalVisibilityChanged?.Invoke();
    }

    public void StartPolling()
    {
        if (_isPolling) return;

        _isPolling = true;
        _pollingTimer = new Timer(async _ =>
        {
            try
            {
                await RefreshProgressAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling batch deployment progress");
            }
        }, null, TimeSpan.Zero, _pollingInterval);

        _logger.LogDebug("Started polling for batch deployment progress");
    }

    public void StopPolling()
    {
        _isPolling = false;
        _pollingTimer?.Dispose();
        _pollingTimer = null;

        _logger.LogDebug("Stopped polling for batch deployment progress");
    }

    public async Task RefreshProgressAsync()
    {
        try
        {
            // Get all active deployments
            ActiveDeployments = await _batchService.GetActiveDeploymentsProgressAsync();

            // Update current progress if we have a selected deployment
            if (SelectedDeploymentId.HasValue)
            {
                CurrentProgress = ActiveDeployments.FirstOrDefault(d => d.DeploymentId == SelectedDeploymentId.Value);

                // If the selected deployment is no longer active, try to fetch it anyway
                if (CurrentProgress == null)
                {
                    CurrentProgress = await _batchService.GetDeploymentProgressAsync(SelectedDeploymentId.Value);
                }
            }
            else if (ActiveDeployments.Any())
            {
                // Auto-select the first active deployment
                SelectedDeploymentId = ActiveDeployments.First().DeploymentId;
                CurrentProgress = ActiveDeployments.First();
            }

            // Auto-show minimized bar if there are active deployments and nothing is visible
            if (HasActiveDeployments && !IsModalVisible && !IsMinimized)
            {
                IsMinimized = true;
                OnModalVisibilityChanged?.Invoke();
            }

            // Auto-dismiss if deployment completed and was minimized
            if (CurrentProgress != null && IsMinimized)
            {
                if (CurrentProgress.BatchStatus == "COMPLETED" || CurrentProgress.BatchStatus == "FAILED")
                {
                    // Keep showing for a bit so user sees the final state
                    // The user can manually dismiss
                }
            }

            OnProgressUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing batch deployment progress");
        }
    }

    public void Dispose()
    {
        StopPolling();
    }
}
