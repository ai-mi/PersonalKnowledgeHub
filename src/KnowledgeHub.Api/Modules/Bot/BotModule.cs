using KnowledgeHub.Api.Modules.Jobs;

namespace KnowledgeHub.Api.Modules.Bot;

public static class BotModule
{
    public static IServiceCollection AddBotModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));
        services.AddSingleton<ITelegramClient, TelegramClient>();
        services.AddScoped<TelegramLinkService>();
        services.AddJobHandler<ProcessUpdateJob, TelegramUpdatePayload>();
        services.AddHostedService<WebhookRegistration>();
        return services;
    }

    /// <summary>Эндпоинты привязки для группы <c>/api</c>. Webhook (<see cref="WebhookEndpoint.MapWebhookEndpoint"/>) — отдельно: ему не нужны cookie и antiforgery.</summary>
    public static IEndpointRouteBuilder MapBotEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapLinkEndpoints();
        return routes;
    }
}
