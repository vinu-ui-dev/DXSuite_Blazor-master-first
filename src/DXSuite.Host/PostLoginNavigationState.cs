namespace DXSuite.Host;

/// <summary>
/// Scoped (per-circuit) flag so the post-login landing page redirect fires only once per session,
/// not on every subsequent navigation to "/".
/// </summary>
public sealed class PostLoginNavigationState
{
    public bool LandingPageRedirectDone { get; set; }
}
