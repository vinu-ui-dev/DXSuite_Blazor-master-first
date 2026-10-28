using Microsoft.AspNetCore.Mvc;
using DXSuite.Director.Services;

namespace DXSuite.Director.Authorization;

/// <summary>
/// Extension methods for checking API key scopes in controller actions.
/// Reads the permissions string stored in HttpContext.Items by ApiKeyAuthorization middleware.
/// </summary>
public static class ApiKeyScopeExtensions
{
    /// <summary>
    /// Checks whether the current API key has the required scope.
    /// Returns null if authorized, or a 403 ObjectResult if the scope is missing.
    /// Usage: if (this.CheckScope(ApiKeyScopes.TokenValidate) is { } denied) return denied;
    /// </summary>
    /// <summary>
    /// Checks whether the current API key holds any one of the given scopes. Returns null if it
    /// does, or a 403 naming all of them. For reads that either side of a split privilege needs:
    /// a deploy-only key still has to list the versions it is choosing between.
    /// </summary>
    public static ObjectResult? CheckAnyScope(this ControllerBase controller, params string[] acceptedScopes)
    {
        var permissions = controller.HttpContext.Items["ApiKeyPermissions"] as string;
        if (acceptedScopes.Any(scope => ApiKeyScopes.HasScope(permissions, scope)))
            return null;

        return new ObjectResult(new { message = $"API key lacks any of the required scopes: {string.Join(", ", acceptedScopes)}" })
        {
            StatusCode = 403
        };
    }

    public static ObjectResult? CheckScope(this ControllerBase controller, string requiredScope)
    {
        var permissions = controller.HttpContext.Items["ApiKeyPermissions"] as string;
        if (ApiKeyScopes.HasScope(permissions, requiredScope))
            return null;

        return new ObjectResult(new { message = $"API key lacks required scope: {requiredScope}" })
        {
            StatusCode = 403
        };
    }
}
