using DXSuite.Shared.Services;

namespace DXSuite.Director.Services
{
    public interface IContentModerationService
    {
        /// <summary>
        /// Scans text for inappropriate content using AI.
        /// Returns null if API fails (graceful degradation).
        /// </summary>
        Task<ModerationResult?> ScanTextAsync(params string[] textInputs);

        /// <summary>
        /// Scans text with caller context for event logging (userId, displayId, locationId).
        /// </summary>
        Task<ModerationResult?> ScanTextAsync(ModerationContext context, params string[] textInputs);

        /// <summary>
        /// Scans an image for inappropriate content using AI vision.
        /// Returns null if no vision-capable provider is configured or the API fails (graceful degradation).
        /// </summary>
        Task<ModerationResult?> ScanImageAsync(byte[] imageData, string mimeType, ModerationContext? context = null);
    }
}
