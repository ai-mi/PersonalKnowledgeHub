using System.Globalization;
using System.Threading.RateLimiting;
using KnowledgeHub.Api.Modules.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Infrastructure.Web;

/// <summary>Лимиты запросов в минуту; секция конфигурации <c>RateLimiting</c> (тесты переопределяют значения).</summary>
public sealed class RateLimitOptions
{
    public int DefaultPerMinute { get; set; } = 120;

    public int AuthPerMinute { get; set; } = 10;

    public int WebhookPerMinute { get; set; } = 60;

    public int UploadPerMinute { get; set; } = 10;
}

public static class RateLimitPolicies
{
    /// <summary>Вся группа <c>/api</c>; партиция — пользователь, иначе IP.</summary>
    public const string Default = "default";

    /// <summary>Вход (<c>/api/auth/*</c>); партиция — всегда IP: пользователя ещё нет.</summary>
    public const string Auth = "auth";

    /// <summary>Webhook Telegram; партиция — IP отправителя.</summary>
    public const string Webhook = "webhook";

    /// <summary>Загрузка документов; партиция — пользователь, иначе IP.</summary>
    public const string Upload = "upload";
}

public static class RateLimitingExtensions
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddRateLimitingPolicies(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection("RateLimiting"));
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitOptions>>((options, limits) =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            var value = limits.Value;
            options.AddPolicy(RateLimitPolicies.Default, context => FixedWindow(UserOrIp(context), value.DefaultPerMinute));
            options.AddPolicy(RateLimitPolicies.Auth, context => FixedWindow(Ip(context), value.AuthPerMinute));
            options.AddPolicy(RateLimitPolicies.Webhook, context => FixedWindow(Ip(context), value.WebhookPerMinute));
            options.AddPolicy(RateLimitPolicies.Upload, context => FixedWindow(UserOrIp(context), value.UploadPerMinute));
        });
        return services;
    }

    private static RateLimitPartition<string> FixedWindow(string key, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = Window,
            QueueLimit = 0,
        });

    // Ключи с разными префиксами: пользователь и IP не должны попадать в одну партицию.
    private static string UserOrIp(HttpContext context) =>
        context.User.FindFirst(AuthSchemes.UserIdClaim)?.Value is { Length: > 0 } userId ? $"user:{userId}" : Ip(context);

    private static string Ip(HttpContext context) => $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}
