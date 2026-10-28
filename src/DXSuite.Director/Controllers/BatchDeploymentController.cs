using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DXSuite.Director.Authorization;
using DXSuite.Director.Services;

namespace DXSuite.Director.Controllers
{
    /// <summary>
    /// Deployment start, pause, resume and stop were anonymous to anyone who could reach the host:
    /// this controller carried no [Authorize] at all.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
    public class BatchDeploymentController : ControllerBase
    {
        private readonly IBatchDeploymentService _batchService;
        private readonly ILogger<BatchDeploymentController> _logger;

        public BatchDeploymentController(
            IBatchDeploymentService batchService,
            ILogger<BatchDeploymentController> logger)
        {
            _batchService = batchService;
            _logger = logger;
        }

        /// <summary>
        /// Start a batch deployment.
        /// </summary>
        [HttpPost("{deploymentId}/start")]
        public async Task<IActionResult> StartBatchDeployment(int deploymentId)
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var result = await _batchService.StartBatchDeploymentAsync(deploymentId, User.Identity?.Name);
                if (!result.Success)
                {
                    return BadRequest(new { error = result.ErrorMessage });
                }

                return Ok(new
                {
                    success = true,
                    deploymentId = result.DeploymentId,
                    totalDevices = result.TotalDevices,
                    totalBatches = result.TotalBatches,
                    batchSize = result.BatchSize,
                    message = $"Batch deployment started with {result.TotalBatches} batches of {result.BatchSize} devices"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting batch deployment {DeploymentId}", deploymentId);
                return StatusCode(500, new { error = "Failed to start batch deployment" });
            }
        }

        /// <summary>
        /// Pause a running batch deployment.
        /// </summary>
        [HttpPost("{deploymentId}/pause")]
        public async Task<IActionResult> PauseBatchDeployment(int deploymentId)
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var success = await _batchService.PauseBatchDeploymentAsync(deploymentId);
                if (!success)
                {
                    return BadRequest(new { error = "Cannot pause deployment - it may not be in progress" });
                }

                return Ok(new { success = true, message = "Deployment paused" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pausing batch deployment {DeploymentId}", deploymentId);
                return StatusCode(500, new { error = "Failed to pause batch deployment" });
            }
        }

        /// <summary>
        /// Resume a paused batch deployment.
        /// </summary>
        [HttpPost("{deploymentId}/resume")]
        public async Task<IActionResult> ResumeBatchDeployment(int deploymentId)
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var success = await _batchService.ResumeBatchDeploymentAsync(deploymentId);
                if (!success)
                {
                    return BadRequest(new { error = "Cannot resume deployment - it may not be paused" });
                }

                return Ok(new { success = true, message = "Deployment resumed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming batch deployment {DeploymentId}", deploymentId);
                return StatusCode(500, new { error = "Failed to resume batch deployment" });
            }
        }

        /// <summary>
        /// Stop (cancel) a batch deployment.
        /// </summary>
        [HttpPost("{deploymentId}/stop")]
        public async Task<IActionResult> StopBatchDeployment(int deploymentId)
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var success = await _batchService.StopBatchDeploymentAsync(deploymentId);
                if (!success)
                {
                    return BadRequest(new { error = "Cannot stop deployment" });
                }

                return Ok(new { success = true, message = "Deployment stopped" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping batch deployment {DeploymentId}", deploymentId);
                return StatusCode(500, new { error = "Failed to stop batch deployment" });
            }
        }

        /// <summary>
        /// Get detailed progress for a specific deployment.
        /// </summary>
        [HttpGet("{deploymentId}/progress")]
        public async Task<IActionResult> GetDeploymentProgress(int deploymentId)
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var progress = await _batchService.GetDeploymentProgressAsync(deploymentId);
                if (progress == null)
                {
                    return NotFound(new { error = "Deployment not found" });
                }

                return Ok(progress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting progress for deployment {DeploymentId}", deploymentId);
                return StatusCode(500, new { error = "Failed to get deployment progress" });
            }
        }

        /// <summary>
        /// Get progress for all active batch deployments.
        /// </summary>
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveDeployments()
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var deployments = await _batchService.GetActiveDeploymentsProgressAsync();
                return Ok(deployments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active deployments");
                return StatusCode(500, new { error = "Failed to get active deployments" });
            }
        }

        /// <summary>
        /// Mark a device as having failed its update.
        /// Called by devices reporting errors.
        /// </summary>
        [HttpPost("device/{deviceId}/failed")]
        public async Task<IActionResult> ReportDeviceFailed(string deviceId, [FromBody] DeviceFailedRequest request)
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                await _batchService.HandleDeviceUpdateFailedAsync(deviceId, request.ErrorMessage);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reporting device failure for {DeviceId}", deviceId);
                return StatusCode(500, new { error = "Failed to report device failure" });
            }
        }
    }

    public class DeviceFailedRequest
    {
        public string? ErrorMessage { get; set; }
    }
}
