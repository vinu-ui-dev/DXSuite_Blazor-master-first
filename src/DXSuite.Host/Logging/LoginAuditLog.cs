using System.Security.Claims;
using DXSuite.Shared.Authorization;

namespace DXSuite.Host.Logging;

/// <summary>
/// Writes every user login (and every 401/403 refusal, e.g. an Airline Agent being blocked) to the
/// console AND to logs/login-yyyyMMdd.log, independent of ILogger providers and levels. Tries the app
/// folder first and falls back to the temp folder when the app folder is not writable (common under
/// IIS). Never throws. Never receives passwords, codes, tokens or secrets: only identity details
/// (user, role, tenant, permissions) and request details (path, IP, status).
/// </summary>
public static class LoginAuditLog
{
    private static readonly object Gate = new();
    private static string? _lastDir;

    /// <summary>A successful login. <paramref name="method"/> is e.g. "local", "ldap", "launch", "bridge".</summary>
    public static void LoginSucceeded(string method, string? user, HttpContext? http, string? role = null,
        string? tenant = null, string? customer = null, IEnumerable<string>? permissions = null,
        IEnumerable<string>? operations = null, string? extra = null) =>
        Write("LOGIN-OK", $"method={method} user='{user}' role='{role}' tenant='{tenant}' customer='{customer}' " +
            $"permissions=[{Join(permissions)}] operations=[{Join(operations)}]{Extra(extra)}", http);

    /// <summary>A failed login attempt (wrong password, redeem rejected, tenant unavailable, ...).</summary>
    public static void LoginFailed(string method, string? user, string reason, HttpContext? http, string? extra = null) =>
        Write("LOGIN-FAIL", $"method={method} user='{user}' reason={reason}{Extra(extra)}", http);

    /// <summary>A 401/403 response: who asked for what, with the role/permissions they were holding.</summary>
    public static void AccessRefused(int status, HttpContext http)
    {
        var u = http.User;
        var label = status == 401 ? "UNAUTHORIZED-401" : "FORBIDDEN-403";
        Write(label,
            $"{http.Request.Method} {http.Request.Path}{http.Request.QueryString} user='{u.Identity?.Name ?? "anonymous"}' " +
            $"authenticated={u.Identity?.IsAuthenticated == true} role='{u.FindFirst(BridgeSessionClaimTypes.RoleName)?.Value}' " +
            $"tenant='{u.FindFirst(BridgeSessionClaimTypes.TenantId)?.Value}' " +
            $"permissions=[{u.FindFirst(BridgeSessionClaimTypes.Permissions)?.Value}] " +
            $"operations=[{u.FindFirst(BridgeSessionClaimTypes.Operations)?.Value}] " +
            $"referer='{http.Request.Headers.Referer}'", http);
    }

    /// <summary>Free-form line for refusals that do not go through the HTTP pipeline (e.g. Director said 403).</summary>
    public static void Note(string tag, string message, HttpContext? http = null) => Write(tag, message, http);

    private static string Join(IEnumerable<string>? values) => values is null ? "" : string.Join(',', values);
    private static string Extra(string? extra) => string.IsNullOrWhiteSpace(extra) ? "" : " " + extra;

    private static void Write(string tag, string message, HttpContext? http)
    {
        var ip = http?.Connection.RemoteIpAddress?.ToString();
        var host = http?.Request.Host.Value;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {message}" +
                   (http is null ? "" : $" ip={ip} host={host}");

        try { Console.WriteLine(line); } catch { /* console may be detached under IIS */ }

        foreach (var dir in new[] { Path.Combine(AppContext.BaseDirectory, "logs"), Path.Combine(Path.GetTempPath(), "dxsuite-logs") })
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(dir);
                    var file = Path.Combine(dir, $"login-{DateTime.Now:yyyyMMdd}.log");
                    if (_lastDir != dir)
                    {
                        _lastDir = dir;
                        File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [LOGIN-LOG] log file: {file}{Environment.NewLine}");
                    }
                    File.AppendAllText(file, line + Environment.NewLine);
                }
                return;
            }
            catch { /* try the next location */ }
        }
    }
}
