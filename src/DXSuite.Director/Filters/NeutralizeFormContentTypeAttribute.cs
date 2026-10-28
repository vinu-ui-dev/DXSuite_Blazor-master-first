using Microsoft.AspNetCore.Mvc.Filters;

namespace DXSuite.Director.Filters;

/// <summary>
/// BrightSign's SignageOS screenshot upload shells out to on-device curl --data-binary
/// without a Content-Type, so the binary payload arrives declared as
/// application/x-www-form-urlencoded. MVC's form value provider then parses (and consumes)
/// the request body during model binding, leaving the action's raw body read empty.
/// Rewriting the declared type before binding keeps the bytes intact for the action;
/// genuine multipart uploads are untouched.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class NeutralizeFormContentTypeAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var request = context.HttpContext.Request;
        if (request.ContentType != null &&
            request.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            request.ContentType = "application/octet-stream";
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
