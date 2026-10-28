namespace DXSuite.Director.Services
{
    public interface IBatchDeploymentService
    {
        // Batch Deployment Lifecycle
        Task<BatchDeploymentResult> StartBatchDeploymentAsync(int deploymentId, string? startedBy = null);
        Task<bool> PauseBatchDeploymentAsync(int deploymentId);
        Task<bool> ResumeBatchDeploymentAsync(int deploymentId);
        Task<bool> StopBatchDeploymentAsync(int deploymentId);

        // Progress Monitoring
        Task<BatchDeploymentProgress?> GetDeploymentProgressAsync(int deploymentId);
        Task<List<BatchDeploymentProgress>> GetActiveDeploymentsProgressAsync();

        // Device Status Updates (called from heartbeat/update reporting)
        Task HandleDeviceVersionUpdateAsync(string deviceId, string newVersion);
        Task HandleDeviceUpdateFailedAsync(string deviceId, string errorMessage);

        // A bootloader batch advances on the device reporting healthy rather than on
        // UPDATE_COMPLETE, because a bootloader that installs and then fails to boot is exactly
        // what batching exists to contain.
        Task HandleDeviceHealthyAsync(string deviceId);

        // A rollback is the system working, and also the strongest evidence the bundle is bad.
        Task HandleDeviceRolledBackAsync(string deviceId, string? fellBackToVersion);

        // Internal batch processing
        Task ProcessNextBatchAsync(int deploymentId);
        Task CheckBatchCompletionAsync(int deploymentId);

        // Utility
        Task<List<string>> GetTargetDeviceIdsForDeploymentAsync(int deploymentId);
        Task<bool> ShouldTriggerUpdateForDeviceAsync(string deviceId);
        Task MarkDeviceUpdateTriggeredAsync(string deviceId);
    }

    public class BatchDeploymentResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int TotalDevices { get; set; }
        public int TotalBatches { get; set; }
        public int DeploymentId { get; set; }
        public int BatchSize { get; set; }
    }

    public class BatchDeploymentProgress
    {
        public int DeploymentId { get; set; }
        public string DeploymentName { get; set; } = string.Empty;
        public string? TargetVersion { get; set; }
        public string? PlatformName { get; set; }
        public string BatchStatus { get; set; } = string.Empty;
        public int CurrentBatchNumber { get; set; }
        public int TotalBatches { get; set; }
        public int TotalDevices { get; set; }
        public int CompletedCount { get; set; }
        public int FailedCount { get; set; }
        public int PendingCount { get; set; }
        public int InProgressCount { get; set; }
        public double ProgressPercent { get; set; }
        public List<FailedDeviceInfo> FailedDevices { get; set; } = new();
        public DateTime? StartedAt { get; set; }
        public DateTime? LastUpdated { get; set; }
        public bool RequiresUserAction { get; set; }
    }

    public class FailedDeviceInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string? DeviceName { get; set; }
        public string? LocationName { get; set; }
        public string? ErrorMessage { get; set; }
        public int BatchNumber { get; set; }
    }
}
