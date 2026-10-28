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

    /// <summary>
    /// Always sets the response's ETag to <paramref name="etag"/> (so a 200 carries it too); returns a 304 when
    /// the request's If-None-Match already names it, or null when the caller should answer normally (INK-05).
    /// </summary>
    public static StatusCodeResult? NotModified(this ControllerBase controller, string etag)
    {
        var quoted = etag.StartsWith('"') ? etag : $"\"{etag}\"";
        controller.Response.Headers.ETag = quoted;

        var ifNoneMatch = controller.Request.Headers.IfNoneMatch.ToString();
        return !string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == quoted
            ? controller.StatusCode(StatusCodes.Status304NotModified)
            : null;
    }
}
