using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Web;

namespace KnowledgeHub.Api.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<SystemContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<UserProvisioningService>();

        services.AddAuthentication(AuthSchemes.Smart)
            .AddPolicyScheme(AuthSchemes.Smart, displayName: null, o =>
                o.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? AuthSchemes.ApiToken
                        : AuthSchemes.Cookies)
            .AddCookie(AuthSchemes.Cookies, o =>
            {
                o.Cookie.Name = "kh.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
            })
            .AddCookie(AuthSchemes.External, o =>
            {
                o.Cookie.Name = "kh.external";
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            })
            // Провайдеры регистрируются всегда: remote-handler с пустым ClientId падает на каждом запросе.
            // Настройки читаются лениво, а «настроен ли провайдер» проверяет ExternalProviders при запросе входа.
            .AddMicrosoftAccount(AuthSchemes.Microsoft, o =>
            {
                o.SignInScheme = AuthSchemes.External;
                (o.ClientId, o.ClientSecret) = ExternalProviders.Credentials(configuration, AuthSchemes.Microsoft);
            })
            .AddGoogle(AuthSchemes.Google, o =>
            {
                o.SignInScheme = AuthSchemes.External;
                (o.ClientId, o.ClientSecret) = ExternalProviders.Credentials(configuration, AuthSchemes.Google);
            });

        services.AddAuthorization();
        services.AddAntiforgeryProtection();
        return services;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapAuthEndpoints();
        routes.MapMeEndpoints();
        return routes;
    }
}

/// <summary>Внешние провайдеры входа: ключ в URL → схема аутентификации; учётные данные из <c>Authentication:{Scheme}</c>.</summary>
public static class ExternalProviders
{
    private const string Unconfigured = "not-configured";

    public static (string ClientId, string ClientSecret) Credentials(IConfiguration configuration, string scheme)
    {
        var section = configuration.GetSection($"Authentication:{scheme}");
        return (Value(section["ClientId"]), Value(section["ClientSecret"]));
    }

    public static bool TryGetScheme(IConfiguration configuration, string provider, out string scheme)
    {
        scheme = provider.ToLowerInvariant() switch
        {
            "microsoft" => AuthSchemes.Microsoft,
            "google" => AuthSchemes.Google,
            _ => "",
        };

        if (scheme.Length == 0)
        {
            return false;
        }

        var (clientId, clientSecret) = Credentials(configuration, scheme);
        return clientId != Unconfigured && clientSecret != Unconfigured;
    }

    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? Unconfigured : value;
}
