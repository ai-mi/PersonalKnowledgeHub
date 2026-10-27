using System.Net.Mail;
using System.Security.Claims;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KnowledgeHub.Api.Modules.Identity;

public static class AuthEndpoints
{
    private const string DevProvider = "dev";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth").RequireRateLimiting(RateLimitPolicies.Auth);
        group.MapGet("/login/{provider}", Login);
        group.MapGet("/callback", Callback);
        group.MapPost("/logout", (Func<HttpContext, Task<NoContent>>)Logout);

        var environment = routes.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var configuration = routes.ServiceProvider.GetRequiredService<IConfiguration>();
        if (environment.IsDevelopment() && configuration.GetValue<bool>("Auth:EnableDevLogin"))
        {
            group.MapPost("/dev-login", DevLogin);
        }
    }

    private static Results<ChallengeHttpResult, NotFound> Login(string provider, string? returnUrl, IConfiguration configuration)
    {
        if (!ExternalProviders.TryGetScheme(configuration, provider, out var scheme))
        {
            return TypedResults.NotFound();
        }

        var callback = $"/api/auth/callback?returnUrl={Uri.EscapeDataString(ReturnUrl.Sanitize(returnUrl))}";
        return TypedResults.Challenge(new AuthenticationProperties { RedirectUri = callback }, [scheme]);
    }

    private static async Task<RedirectHttpResult> Callback(
        string? returnUrl, HttpContext context, UserProvisioningService provisioning, CancellationToken ct)
    {
        var result = await context.AuthenticateAsync(AuthSchemes.External);
        var principal = result.Principal;
        var scheme = principal?.Identity?.AuthenticationType;
        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!result.Succeeded || scheme is not (AuthSchemes.Microsoft or AuthSchemes.Google) || string.IsNullOrEmpty(subject))
        {
            return TypedResults.LocalRedirect("/login?error=external");
        }

        var user = await provisioning.FindOrCreateAsync(
            scheme.ToLowerInvariant(), subject, principal!.FindFirstValue(ClaimTypes.Email), principal!.FindFirstValue(ClaimTypes.Name), ct);

        await context.SignOutAsync(AuthSchemes.External);
        await SignInAsync(context, user);
        return TypedResults.LocalRedirect(ReturnUrl.Sanitize(returnUrl));
    }

    private static async Task<NoContent> Logout(HttpContext context)
    {
        await context.SignOutAsync(AuthSchemes.Cookies);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> DevLogin(
        DevLoginRequest request, HttpContext context, UserProvisioningService provisioning, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email, out _))
        {
            throw Errors.Validation("email", "A valid e-mail is required.");
        }

        var email = request.Email.Trim();
        var user = await provisioning.FindOrCreateAsync(DevProvider, email.ToLowerInvariant(), email, null, ct);
        await SignInAsync(context, user);
        return TypedResults.NoContent();
    }

    private static Task SignInAsync(HttpContext context, User user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(AuthSchemes.UserIdClaim, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
            ],
            AuthSchemes.Cookies);
        return context.SignInAsync(AuthSchemes.Cookies, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }

    public sealed record DevLoginRequest(string? Email);
}
