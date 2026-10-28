using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DXSuite.Design.Services.Mcp;

public class DesignMcpTokenAuthOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Validates dxd_-prefixed bearer tokens on the Design MCP endpoint. The token service is
/// tenant-scoped, so a token is only found in the resolved tenant's own Design database —
/// a valid token presented against another tenant's subdomain fails authentication.
/// </summary>
public class DesignMcpTokenAuthHandler(
    IOptionsMonitor<DesignMcpTokenAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DesignMcpTokenService tokenService)
    : AuthenticationHandler<DesignMcpTokenAuthOptions>(options, logger, encoder)
{
    public const string SchemeName = "DesignMcpToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = await tokenService.ValidateAsync(header["Bearer ".Length..].Trim());
        if (token is null)
            return AuthenticateResult.Fail("Invalid, expired, or revoked token.");

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, token.UserId),
            new Claim("CustomerCode", token.CustomerId),
            new Claim("McpTokenId", token.TokenId.ToString()),
        ], SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
