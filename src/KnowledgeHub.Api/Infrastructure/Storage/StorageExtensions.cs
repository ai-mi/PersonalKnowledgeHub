using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Infrastructure.Storage;

public static class StorageExtensions
{
    public static IServiceCollection AddBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IBlobStore, AzureBlobStore>();
        services.AddHostedService<BlobContainerInitializer>();
        return services;
    }

    /// <summary>Создаёт контейнеры при старте. Без настроек хранилища (например, в тестах без файлов) ничего не делает.</summary>
    private sealed class BlobContainerInitializer(IBlobStore store, IOptions<StorageOptions> options) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            options.Value.IsConfigured ? store.EnsureContainersAsync(cancellationToken) : Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
