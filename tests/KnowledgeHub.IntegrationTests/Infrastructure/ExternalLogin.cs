using System.Security.Claims;
using KnowledgeHub.Api.Modules.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.IntegrationTests.Infrastructure;

/// <summary>Имитирует возврат от внешнего провайдера: готовит временную cookie External, как это делает OAuth-handler.</summary>
public static class ExternalLogin
{
    /// <returns>Значение заголовка <c>Cookie</c>.</returns>
    public static string CreateCookie(
        ApiFactory factory, string scheme, string? subject, string? email = null, string? name = null)
    {
        List<Claim> claims = [];
        if (subject is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));
        }

        if (email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        if (name is not null)
        {
            claims.Add(new Claim(ClaimTypes.Name, name));
        }

        return Protect(factory, new ClaimsPrincipal(new ClaimsIdentity(claims, scheme)));
    }

    /// <summary>Билет без единой identity: так выглядит испорченная или нестандартная внешняя cookie.</summary>
    public static string CreateCookieWithoutIdentity(ApiFactory factory) => Protect(factory, new ClaimsPrincipal());

    private static string Protect(ApiFactory factory, ClaimsPrincipal principal)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AuthSchemes.External);
        var value = options.TicketDataFormat.Protect(new AuthenticationTicket(principal, AuthSchemes.External));
        return $"{options.Cookie.Name}={value}";
    }

    public static HttpRequestMessage CallbackRequest(string cookie, string? returnUrl = null)
    {
        var url = returnUrl is null ? "/api/auth/callback" : $"/api/auth/callback?returnUrl={Uri.EscapeDataString(returnUrl)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);
        return request;
    }
}
