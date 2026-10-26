using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgeHub.Api.Modules.Ingestion;

public static class IngestionModule
{
    public static IServiceCollection AddIngestionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));
        // Один экземпляр: внутри пул соединений SocketsHttpHandler.
        services.AddSingleton<SafeHttpFetcher>();
        services.TryAddScoped<IIngestionCompletedHandler, NoOpIngestionCompletedHandler>();
        services.AddJobHandler<IngestItemJob, IngestItemPayload>();
        services.AddJobFailureHandler<IngestFailureHandler>();
        return services;
    }
}
