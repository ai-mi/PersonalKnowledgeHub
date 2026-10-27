using KnowledgeHub.Api.Modules.Ingestion;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgeHub.Api.Modules.Bot;

public static class BotModule
{
    public static IServiceCollection AddBotModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));
        services.AddSingleton<ITelegramClient, TelegramClient>();
        services.AddScoped<TelegramLinkService>();
        services.AddScoped<IBotNotifier, BotNotifier>();
        services.AddScoped<BotCommands>();
        // Заменяет заглушку модуля Ingestion: окончание обработки записи, присланной боту, сообщается в чат.
        services.Replace(ServiceDescriptor.Scoped<IIngestionCompletedHandler, IngestionCompletedNotifier>());
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
