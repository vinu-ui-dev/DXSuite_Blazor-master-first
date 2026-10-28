namespace DXSuite.Shared.Services;

/// <summary>
/// Scoped service that orchestrates re-authentication confirmation for high-privilege actions
/// (SOC 2 CC6). Pages call RequestConfirmationAsync() to show the modal and await the user's
/// decision; the returned token is passed to service methods that require re-auth.
/// </summary>
public class ReAuthConfirmationService
{
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(2);

    private Guid? _pendingToken;
    private DateTime _tokenIssuedAt;

    // Raised when the page should show the confirmation modal.
    public event Func<Task>? OnShowRequested;

    // Raised when the user confirms or cancels, so the awaiting page can proceed.
    private TaskCompletionSource<bool>? _pendingTcs;

    /// <summary>
    /// Shows the re-auth modal and waits for the user to confirm or cancel.
    /// Returns a short-lived token on confirmation, or null on cancellation.
    /// </summary>
    public async Task<Guid?> RequestConfirmationAsync()
    {
        _pendingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (OnShowRequested != null)
            await OnShowRequested.Invoke();

        var confirmed = await _pendingTcs.Task;
        if (!confirmed) return null;

        var token = Guid.NewGuid();
        _pendingToken = token;
        _tokenIssuedAt = DateTime.UtcNow;
        return token;
    }

    /// <summary>
    /// Called by the modal component when the user clicks Confirm.
    /// </summary>
    public void Confirm() => _pendingTcs?.TrySetResult(true);

    /// <summary>
    /// Called by the modal component when the user clicks Cancel or closes the dialog.
    /// </summary>
    public void Cancel() => _pendingTcs?.TrySetResult(false);

    /// <summary>
    /// Validates that the token was issued by this service and has not expired.
    /// Service methods that require re-auth call this before proceeding.
    /// </summary>
    public bool ValidateToken(Guid token)
        => _pendingToken == token && DateTime.UtcNow - _tokenIssuedAt <= TokenTtl;

    /// <summary>
    /// Consumes the token, preventing it from being used again.
    /// Should be called immediately after ValidateToken succeeds.
    /// </summary>
    public void ConsumeToken() => _pendingToken = null;
}
