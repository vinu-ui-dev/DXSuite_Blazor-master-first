using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using DXSuite.Shared.Models;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services;
using DXSuite.Shared.Services.Identity;

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
        if (!_authConfig.LocalAccounts.Enabled || _localAccountService == null)
            return BadRequest("Local accounts are not enabled.");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Redirect("/?error=missing-credentials");

        try
        {
            var user = await _localAccountService.ValidateCredentialsAsync(email, password);
            if (user == null)
                return Redirect("/?error=invalid-credentials");

            return await IssueAuthCookieAndRedirect(user, email);
        }
        catch (InvalidOperationException ex) when (ex.Message == "email-not-confirmed")
        {
            return Redirect("/?error=email-not-confirmed");
        }
    }

    [HttpPost("ldap/login")]
    public async Task<IActionResult> LdapLogin([FromForm] string username, [FromForm] string password)
    {
        var effectiveLdap = _tenantContext.AuthConfig.Ldap ?? _authConfig.Ldap;
        if (effectiveLdap == null || !effectiveLdap.Enabled || _ldapCredentialService == null)
            return BadRequest("LDAP authentication is not enabled.");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Redirect("/?error=missing-credentials");

        try
        {
            var user = await _ldapCredentialService.ValidateAsync(username, password, effectiveLdap);
            if (user == null)
                return Redirect("/?error=ldap-invalid");

            return await IssueAuthCookieAndRedirect(user, user.UserPrincipalName ?? username);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "LDAP configuration error during login for '{Username}'", username);
            return Redirect("/?error=ldap-config-error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected LDAP login error for '{Username}'", username);
            return Redirect("/?error=ldap-failed");
        }
    }

    [HttpPost("local/register")]
    public async Task<IActionResult> Register([FromForm] string email, [FromForm] string password, [FromForm] string confirmPassword)
    {
        if (!_authConfig.LocalAccounts.Enabled || !_authConfig.LocalAccounts.AllowSelfRegistration || _localAccountService == null)
            return Redirect("/?error=registration-disabled");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Redirect("/?error=missing-credentials&mode=register");

        if (password != confirmPassword)
            return Redirect("/?error=passwords-mismatch&mode=register");

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

                return Redirect("/?message=confirm-email");
            }
            else
            {
                var user = await _localAccountService.CreateAccountAsync(email, password);
                return await IssueAuthCookieAndRedirect(user, email);
            }
        }
        catch (InvalidOperationException ex)
        {
            return Redirect($"/?error={Uri.EscapeDataString(ex.Message)}&mode=register");
        }
        catch (ArgumentException ex)
        {
            return Redirect($"/?error={Uri.EscapeDataString(ex.Message)}&mode=register");
        }
    }

    [HttpGet("local/confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromQuery] string email, [FromQuery] string token)
    {
        if (_localAccountService == null)
            return BadRequest("Local accounts are not enabled.");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return Redirect("/?error=invalid-token");

        var confirmed = await _localAccountService.ConfirmEmailAsync(email, token);
        return confirmed
            ? Redirect("/?message=email-confirmed")
            : Redirect("/?error=invalid-token");
    }

    /// <summary>
    /// External launch token authentication.
    /// Validates a signed JWT from an external platform, confirms with the external API,
    /// then issues an auth cookie and redirects to /.
    /// </summary>
    [HttpGet("launch")]
    public async Task<IActionResult> Launch([FromQuery] string? token)
    {
        if (!_extConfig.Enabled || _tokenValidationParams == null || _httpClientFactory == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(token))
            return Redirect("/?error=missing-token");

        try
        {
            // Validate JWT signature, issuer, audience, lifetime
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _tokenValidationParams, out _);

            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
                      ?? principal.FindFirst("jti")?.Value;

            if (string.IsNullOrEmpty(jti))
            {
                _logger.LogWarning("Launch token missing jti claim");
                return Redirect("/?error=invalid-token");
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
                return Redirect("/?error=token-already-used");
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

            return Redirect("/");
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Launch token expired");
            return Redirect("/?error=token-expired");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Launch token validation failed");
            return Redirect("/?error=invalid-token");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during launch token validation");
            return Redirect("/?error=launch-failed");
        }
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        var bridgeHandle = User.FindFirst(DXSuite.Shared.Authorization.BridgeSessionClaimTypes.SessionHandle)?.Value;
        if (!string.IsNullOrEmpty(bridgeHandle) && _bridgeClient is not null)
            await _bridgeClient.EndAsync(bridgeHandle, HttpContext.RequestAborted);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    private async Task<IActionResult> IssueAuthCookieAndRedirect(DXSuite.Shared.Entities.Identity.TblUser user, string email)
    {
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

        return Redirect("/");
    }
}
