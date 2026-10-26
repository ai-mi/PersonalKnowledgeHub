using Testcontainers.Azurite;

[assembly: AssemblyFixture(typeof(KnowledgeHub.IntegrationTests.Infrastructure.AzuriteContainer))]

namespace KnowledgeHub.IntegrationTests.Infrastructure;

/// <summary>Один контейнер Azurite (Blob-хранилище) на всю сборку тестов.</summary>
public sealed class AzuriteContainer : IAsyncLifetime
{
    // Образ Azurite может быть новее API-версии SDK; --skipApiVersionCheck убирает такую причину отказов.
    private readonly Testcontainers.Azurite.AzuriteContainer _container =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest").WithCommand("--skipApiVersionCheck").Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
