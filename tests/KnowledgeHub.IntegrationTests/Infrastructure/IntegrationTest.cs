namespace KnowledgeHub.IntegrationTests.Infrastructure;

/// <summary>Базовый класс: HttpClient к приложению и чистая база перед каждым тестом.</summary>
public abstract class IntegrationTest(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    protected ApiFactory Factory { get; } = factory;

    protected HttpClient Client { get; } = factory.CreateClient();

    public ValueTask InitializeAsync() => new(ResetDatabaseAsync());

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        return ValueTask.CompletedTask;
    }

    protected Task ResetDatabaseAsync() => Factory.ResetDatabaseAsync();
}