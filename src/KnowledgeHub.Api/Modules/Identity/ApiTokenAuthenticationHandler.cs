using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Modules.Identity;

/// <summary>Схема <c>ApiToken</c>: заголовок <c>Authorization: Bearer kh_...</c>.</summary>
public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var userId = await tokens.AuthenticateAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        if (userId is null)
        {
            return AuthenticateResult.Fail("Invalid API token.");
        }

        var identity = new ClaimsIdentity([new Claim(AuthSchemes.UserIdClaim, userId.Value.ToString())], AuthSchemes.ApiToken);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), AuthSchemes.ApiToken));
    }
}
