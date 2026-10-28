using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Shared.Authorization;

/// <summary>
/// Gates a controller or action on a named <see cref="TenantFeatureFlags"/> property (e.g.
/// <c>[RequiresTenantFeature(nameof(TenantFeatureFlags.Messaging))]</c>), refusing with 404 when the
/// resolved tenant's flag is off — so a disabled capability's API surface does not exist for that
/// tenant, matching the page-level check the corresponding Razor page already makes for itself via
/// <c>ISessionState.HasPermissionAsync</c>.
/// </summary>
/// <remarks>
/// Also valid as a Razor <c>@attribute</c> on a page component. The Blazor router does not act on
/// arbitrary component attributes, so there it is documentation only — the page is still responsible
/// for its own runtime check, the same way <c>Messages.razor</c> checks <c>PermissionKeys.PageMessages</c>.
/// Only the MVC controller case is enforced by this attribute itself.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresTenantFeatureAttribute(string featureName) : Attribute, IAsyncAuthorizationFilter
{
    public string FeatureName { get; } = featureName;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var tenant = context.HttpContext.RequestServices.GetService<ITenantContext>();
        var enabled = tenant?.IsPopulated == true && tenant.Features.IsEnabled(FeatureName);

        if (!enabled) context.Result = new NotFoundResult();
        return Task.CompletedTask;
    }

    /// <summary>The same check, for Razor markup deciding whether to render a feature-gated child component at
    /// all (e.g. Approvals.razor's Message approval queue, which only exists when Messaging is on).</summary>
    public static bool IsOpen(string featureName, ITenantContext? tenant) =>
        tenant?.IsPopulated == true && tenant.Features.IsEnabled(featureName);
}
