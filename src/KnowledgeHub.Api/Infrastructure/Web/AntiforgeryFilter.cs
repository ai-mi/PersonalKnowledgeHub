using KnowledgeHub.Api.Modules.Identity;
using Microsoft.AspNetCore.Antiforgery;

namespace KnowledgeHub.Api.Infrastructure.Web;

/// <summary>Проверяет antiforgery-токен у изменяющих запросов, выполненных с cookie-сессией; API-токены и анонимные запросы пропускает.</summary>
public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!IsSafeMethod(http.Request.Method) && AntiforgeryExtensions.IsCookieSession(http))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                return TypedResults.Problem(title: "Invalid antiforgery token.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return await next(context);
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);
}

public static class AntiforgeryExtensions
{
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string CookieName = "XSRF-TOKEN";

    public static IServiceCollection AddAntiforgeryProtection(this IServiceCollection services) =>
        services.AddAntiforgery(o =>
        {
            o.HeaderName = HeaderName;
            o.Cookie.Name = "kh.antiforgery";
            // Always запрещает работу по http (в т.ч. локально на localhost:5000); за прокси схема берётся из forwarded headers.
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

    /// <summary>
    /// На GET выставляет читаемую из JS cookie <c>XSRF-TOKEN</c> с request-токеном. Токен привязан к пользователю,
    /// поэтому выдаётся только после входа и обновляется на каждом GET (в том числе сразу после входа).
    /// </summary>
    public static IApplicationBuilder UseXsrfCookie(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method) && IsCookieSession(context))
            {
                var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
                context.Response.Cookies.Append(CookieName, tokens.RequestToken!, new CookieOptions
                {
                    HttpOnly = false,
                    Secure = context.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                });
            }

            await next(context);
        });

    internal static bool IsCookieSession(HttpContext context) =>
        context.User.Identity is { IsAuthenticated: true, AuthenticationType: AuthSchemes.Cookies };
}
