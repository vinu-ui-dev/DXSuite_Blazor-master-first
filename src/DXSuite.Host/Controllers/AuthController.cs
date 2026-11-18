using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using DXSuite.Host.Logging;
using DXSuite.Shared.Models;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace DXSuite.Host.Controllers;

[Route("auth")]
public class AuthController : Controller
{
    private readonly AuthenticationConfig _authConfig;
    private readonly ExternalIntegrationConfig _extConfig;
    private readonly ITenantContext _tenantContext;
    private readonly ILocalAccountService? _localAccountService;
    private readonly ILdapCredentialService? _ldapCredentialService;
    private readonly IEmailService? _emailService;
    private readonly ILogger<AuthController> _logger;
    private readonly TokenValidationParameters? _tokenValidationParams;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly DXSuite.Host.Bridge.EditorBridgeClient? _bridgeClient;

    public AuthController(
        IOptions<AuthenticationConfig> authConfig,
        IOptions<ExternalIntegrationConfig> extConfig,
        ITenantContext tenantContext,
        ILogger<AuthController> logger,
        ILocalAccountService? localAccountService = null,
        ILdapCredentialService? ldapCredentialService = null,
        IEmailService? emailService = null,
        TokenValidationParameters? tokenValidationParams = null,
        IHttpClientFactory? httpClientFactory = null,
        DXSuite.Host.Bridge.EditorBridgeClient? bridgeClient = null)
    {
        _authConfig = authConfig.Value;
        _extConfig = extConfig.Value;
        _tenantContext = tenantContext;
        _logger = logger;
        _localAccountService = localAccountService;
        _ldapCredentialService = ldapCredentialService;
        _emailService = emailService;
        _tokenValidationParams = tokenValidationParams;
        _httpClientFactory = httpClientFactory;
        _bridgeClient = bridgeClient;
    }

    [HttpGet("challenge/{provider}")]
    public IActionResult Challenge(string provider)
    {
        _logger.LogInformation("AUTH challenge requested: provider={Provider}, tenant={Tenant}", provider, _tenantContext.Subdomain);
        var props = new AuthenticationProperties { RedirectUri = "/" };

        // Per-tenant scheme: if this tenant has an explicit OIDC config for the provider,
        // challenge the tenant-specific scheme registered at startup as "{subdomain}_{provider}".
        if (_tenantContext.AuthConfig.Providers.TryGetValue(provider, out var tenantProvider)
            && tenantProvider.Enabled)
        {
            var tenantScheme = $"{_tenantContext.Subdomain}_{provider}";
            return Challenge(props, tenantScheme);
        }

        // Backward compat: tenant uses Provider="multi", fall back to top-level scheme.
        if (!_authConfig.Providers.TryGetValue(provider, out var config) || !config.Enabled)
            return BadRequest($"Provider '{provider}' is not enabled for tenant '{_tenantContext.Subdomain}'.");

        // Microsoft Identity Web uses "OpenIdConnect" as its scheme name.
        var scheme = provider == "Microsoft" ? "OpenIdConnect" : provider;
        return Challenge(props, scheme);
    }

    [HttpPost("local/login")]
    public async Task<IActionResult> LocalLogin([FromForm] string email, [FromForm] string password)
    {
        _logger.LogInformation("AUTH local login attempt: email={Email}", email);
        if (!_authConfig.LocalAccounts.Enabled || _localAccountService == null)
        {
            _logger.LogWarning("AUTH local login rejected: local accounts not enabled");
            return BadRequest("Local accounts are not enabled.");
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("AUTH local login rejected: missing credentials");
            return Redirect($"{Request.PathBase}/?error=missing-credentials");
        }

        try
        {
            var user = await _localAccountService.ValidateCredentialsAsync(email, password);
            if (user == null)
            {
                _logger.LogWarning("AUTH local login failed: invalid credentials for {Email}", email);
                LoginAuditLog.LoginFailed("local", email, "invalid-credentials", HttpContext, $"tenant='{_tenantContext.Subdomain}'");
                return Redirect($"{Request.PathBase}/?error=invalid-credentials");
            }

            _logger.LogInformation("AUTH local login succeeded for {Email}", email);
            return await IssueAuthCookieAndRedirect(user, email, "local");
        }
        catch (InvalidOperationException ex) when (ex.Message == "email-not-confirmed")
        {
            _logger.LogWarning("AUTH local login failed: email not confirmed for {Email}", email);
            LoginAuditLog.LoginFailed("local", email, "email-not-confirmed", HttpContext);
            return Redirect($"{Request.PathBase}/?error=email-not-confirmed");
        }
    }

    [HttpPost("ldap/login")]
    public async Task<IActionResult> LdapLogin([FromForm] string username, [FromForm] string password)
    {
        _logger.LogInformation("AUTH ldap login attempt: username={Username}", username);
        var effectiveLdap = _tenantContext.AuthConfig.Ldap ?? _authConfig.Ldap;
        if (effectiveLdap == null || !effectiveLdap.Enabled || _ldapCredentialService == null)
        {
            _logger.LogWarning("AUTH ldap login rejected: LDAP not enabled");
            return BadRequest("LDAP authentication is not enabled.");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("AUTH ldap login rejected: missing credentials");
            return Redirect($"{Request.PathBase}/?error=missing-credentials");
        }

        try
        {
            var user = await _ldapCredentialService.ValidateAsync(username, password, effectiveLdap);
            if (user == null)
            {
                _logger.LogWarning("AUTH ldap login failed: invalid credentials for {Username}", username);
                LoginAuditLog.LoginFailed("ldap", username, "invalid-credentials", HttpContext, $"tenant='{_tenantContext.Subdomain}'");
                return Redirect($"{Request.PathBase}/?error=ldap-invalid");
            }

            _logger.LogInformation("AUTH ldap login succeeded for {Username}", username);
            return await IssueAuthCookieAndRedirect(user, user.UserPrincipalName ?? username, "ldap");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "LDAP configuration error during login for '{Username}'", username);
            return Redirect($"{Request.PathBase}/?error=ldap-config-error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected LDAP login error for '{Username}'", username);
            return Redirect($"{Request.PathBase}/?error=ldap-failed");
        }
    }

    [HttpPost("local/register")]
    public async Task<IActionResult> Register([FromForm] string email, [FromForm] string password, [FromForm] string confirmPassword)
    {
        if (!_authConfig.LocalAccounts.Enabled || !_authConfig.LocalAccounts.AllowSelfRegistration || _localAccountService == null)
            return Redirect($"{Request.PathBase}/?error=registration-disabled");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Redirect($"{Request.PathBase}/?error=missing-credentials&mode=register");

        if (password != confirmPassword)
            return Redirect($"{Request.PathBase}/?error=passwords-mismatch&mode=register");

        try
        {
            if (_authConfig.LocalAccounts.RequireEmailConfirmation)
            {
                var (token, user) = await _localAccountService.CreateAccountWithConfirmationAsync(email, password);

                // Send confirmation email if SMTP is available
                _logger.LogInformation("Registration: EmailService={HasService}, checking SMTP config...",
                    _emailService != null);

                if (_emailService != null && await _emailService.IsConfiguredAsync())
                {
                    var baseUrl = $"{Request.Scheme}://{Request.Host}";
                    _logger.LogInformation("Sending confirmation email to {Email}, baseUrl={BaseUrl}", email, baseUrl);
                    var confirmUrl = $"{baseUrl}/auth/local/confirm-email?email={Uri.EscapeDataString(email)}&token={token}";
                    await _emailService.SendAsync(
                        email,
                        "Confirm your DXSuite account",
                        $"<p>Click the link below to confirm your email address:</p><p><a href=\"{confirmUrl}\">Confirm Email</a></p><p>This link expires in 24 hours.</p>");
                    _logger.LogInformation("Confirmation email sent to {Email}", email);
                }
                else
                {
                    _logger.LogWarning("Cannot send confirmation email: EmailService={HasService}, SMTP not configured or unavailable", _emailService != null);
                }

                return Redirect($"{Request.PathBase}/?message=confirm-email");
            }
            else
            {
                var user = await _localAccountService.CreateAccountAsync(email, password);
                return await IssueAuthCookieAndRedirect(user, email, "register");
            }
        }
        catch (InvalidOperationException ex)
        {
            return Redirect($"{Request.PathBase}/?error={Uri.EscapeDataString(ex.Message)}&mode=register");
        }
        catch (ArgumentException ex)
        {
            return Redirect($"{Request.PathBase}/?error={Uri.EscapeDataString(ex.Message)}&mode=register");
        }
    }

    [HttpGet("local/confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromQuery] string email, [FromQuery] string token)
    {
        if (_localAccountService == null)
            return BadRequest("Local accounts are not enabled.");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return Redirect($"{Request.PathBase}/?error=invalid-token");

        var confirmed = await _localAccountService.ConfirmEmailAsync(email, token);
        return confirmed
            ? Redirect($"{Request.PathBase}/?message=email-confirmed")
            : Redirect($"{Request.PathBase}/?error=invalid-token");
    }

    /// <summary>
    /// External launch token authentication.
    /// Validates a signed JWT from an external platform, confirms with the external API,
    /// then issues an auth cookie and redirects to /.
    /// </summary>
    /// 
    [AllowAnonymous]
    [HttpGet("launch")]
    public async Task<IActionResult> Launch([FromQuery] string? token)
    {
        _logger.LogInformation("AUTH launch called: tokenPresent={Present}", !string.IsNullOrWhiteSpace(token));
        if (!_extConfig.Enabled || _tokenValidationParams == null || _httpClientFactory == null)
        {
            _logger.LogWarning("AUTH launch rejected: external integration not enabled");
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("AUTH launch rejected: missing token");
            return Redirect($"{Request.PathBase}/?error=missing-token");
        }

        try
        {
            // Validate JWT signature, issuer, audience, lifetime
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _tokenValidationParams, out _);
            _logger.LogInformation("AUTH launch: JWT signature/lifetime validated");

            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
                      ?? principal.FindFirst("jti")?.Value;

            if (string.IsNullOrEmpty(jti))
            {
                _logger.LogWarning("Launch token missing jti claim");
                return Redirect($"{Request.PathBase}/?error=invalid-token");
            }

            // Generate or retrieve session ID for replay prevention
            var sessionId = HttpContext.Session.GetString("LaunchSessionId");
            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = Guid.NewGuid().ToString();
                HttpContext.Session.SetString("LaunchSessionId", sessionId);
            }

            // Call external API to validate jti (replay prevention)
            var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.PostAsJsonAsync(
                _extConfig.ValidationEndpoint,
                new { Jti = jti, SessionId = sessionId });

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Launch token validation failed: {Status} for jti={Jti}",
                    response.StatusCode, jti);
                return Redirect($"{Request.PathBase}/?error=token-already-used");
            }

            _logger.LogInformation("Launch token validated for jti={Jti}, sessionId={SessionId}", jti, sessionId);

            // Extract identity from JWT claims
            var email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value
                        ?? principal.FindFirst(ClaimTypes.Email)?.Value
                        ?? principal.FindFirst("email")?.Value
                        ?? principal.FindFirst("preferred_username")?.Value
                        ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            var name = principal.FindFirst("name")?.Value
                       ?? principal.FindFirst(ClaimTypes.Name)?.Value;

            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("Launch token has no email/identity claim, using jti as fallback");
                email = $"launch-{jti}";
            }

            // Issue auth cookie
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, email),
                new("preferred_username", email),
            };

            if (!string.IsNullOrEmpty(name))
                claims.Add(new Claim("name", name));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var claimsPrincipal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                claimsPrincipal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });

            LoginAuditLog.LoginSucceeded("launch", email, HttpContext, tenant: _tenantContext.Subdomain, extra: $"name='{name}'");
            _logger.LogInformation("AUTH launch succeeded: cookie issued for {Email}", email);
            return Redirect($"{Request.PathBase}/");
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Launch token expired");
            return Redirect($"{Request.PathBase}/?error=token-expired");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Launch token validation failed");
            return Redirect($"{Request.PathBase}/?error=invalid-token");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during launch token validation");
            return Redirect($"{Request.PathBase}/?error=launch-failed");
        }
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        _logger.LogInformation("AUTH logout: user={User}", User.Identity?.Name);
        var cameFromDirector = User.FindFirst(DXSuite.Shared.Authorization.BridgeSessionClaimTypes.IsBridgeSession) is not null;
        var bridgeHandle = User.FindFirst(DXSuite.Shared.Authorization.BridgeSessionClaimTypes.SessionHandle)?.Value;
        if (!string.IsNullOrEmpty(bridgeHandle) && _bridgeClient is not null)
            await _bridgeClient.EndAsync(bridgeHandle, HttpContext.RequestAborted);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("AUTH logout completed");

        // Signed in from Director: send the user back to Director's Angular app (hash routing: https://site/#/home).
        var directorUi = cameFromDirector
            ? HttpContext.RequestServices.GetService<IOptions<EditorBridgeClientOptions>>()?.Value.DirectorUiBaseUrl
            : null;
        if (!string.IsNullOrWhiteSpace(directorUi))
            return Redirect($"{directorUi.TrimEnd('/')}/#/home");

        return Redirect($"{Request.PathBase}/");
    }

    private async Task<IActionResult> IssueAuthCookieAndRedirect(DXSuite.Shared.Entities.Identity.TblUser user, string email, string method)
    {
        LoginAuditLog.LoginSucceeded(method, user.UserPrincipalName ?? email, HttpContext, tenant: _tenantContext.Subdomain,
            extra: $"userId={user.UserId} friendlyName='{user.FriendlyName}'");
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.UserPrincipalName ?? email),
            new("preferred_username", user.UserPrincipalName ?? email),
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        };

        if (user.FriendlyName != null)
            claims.Add(new Claim("name", user.FriendlyName));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
            });

        _logger.LogInformation("AUTH cookie issued for {User}", user.UserPrincipalName ?? email);

        return Redirect($"{Request.PathBase}/");
    }
}
