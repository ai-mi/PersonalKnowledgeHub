using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.Api.Modules.Ingestion;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KnowledgeHub.IntegrationTests.Items;

/// <summary>
/// Редкие пути: гонки двух запросов, сбои базы и хранилища. Для них хост подменяется вариантом с перехватчиком сохранения
/// или хранилищем, которое ломается по команде.
/// </summary>
public sealed class FaultInjectionTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Перед сохранением выполняет действие; при <c>null</c> в <paramref name="onSaving"/> ничего не делает.</summary>
    private sealed class SavingInterceptor(Func<DbContext, CancellationToken, Task> onSaving) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await onSaving(eventData.Context!, cancellationToken);
            return result;
        }
    }

    private sealed class FailingBlobStore(IBlobStore inner) : IBlobStore
    {
        public bool FailDelete { get; set; }
        public bool FailRead { get; set; }

        public Task EnsureContainersAsync(CancellationToken ct) => inner.EnsureContainersAsync(ct);

        public Task UploadAsync(string container, string path, Stream content, string contentType, CancellationToken ct) =>
            inner.UploadAsync(container, path, content, contentType, ct);

        public Task<Stream?> OpenReadAsync(string container, string path, CancellationToken ct) =>
            FailRead ? throw new IOException("хранилище недоступно") : inner.OpenReadAsync(container, path, ct);

        public Task<Stream> OpenWriteAsync(string container, string path, string contentType, CancellationToken ct) =>
            inner.OpenWriteAsync(container, path, contentType, ct);

        public Task DeleteAsync(string container, string path, CancellationToken ct) =>
            FailDelete ? throw new IOException("хранилище недоступно") : inner.DeleteAsync(container, path, ct);
    }

    private WebApplicationFactory<Program> WithInterceptor(SaveChangesInterceptor interceptor) =>
        Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(interceptor))));

    private (WebApplicationFactory<Program> Host, FailingBlobStore Blobs) WithFailingBlobs()
    {
        var blobs = new FailingBlobStore(new AzureBlobStore(Options.Create(new StorageOptions { ConnectionString = Factory.AzuriteConnectionString })));
        return (Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services => services.AddSingleton<IBlobStore>(blobs))), blobs);
    }

    private static HttpClient ClientOf(WebApplicationFactory<Program> host, Guid userId)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, userId.ToString());
        return client;
    }

    private async Task ExecuteAsAdminAsync(string sql, params (string Name, object Value)[] parameters)
    {
        // Админ-соединение: роль приложения подчиняется RLS, а здесь нужна запись «от лица другого запроса».
        await using var connection = new NpgsqlConnection(Factory.Postgres.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    private static DbUpdateException Failure(string sqlState, string? constraint) =>
        new("save failed", new PostgresException("save failed", "ERROR", "ERROR", sqlState, constraintName: constraint));

    // --- гонки ---

    [Fact]
    public async Task DocumentSavedByCompetingRequestAfterTheCheck_ReturnsTheWinner()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        var fired = 0;
        using var host = WithInterceptor(new SavingInterceptor(async (db, ct) =>
        {
            if (db.ChangeTracker.Entries<Item>().Any(e => e.State == EntityState.Added) && Interlocked.Exchange(ref fired, 1) == 0)
            {
                await ExecuteAsAdminAsync(
                    "INSERT INTO items (id, user_id, type, title, content_hash, status, created_at, updated_at) VALUES (@id, @user, 'document', 'winner', 'hash-1', 'ready', now(), now())",
                    ("id", Guid.CreateVersion7()), ("user", userId));
            }
        }));

        await using var scope = host.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().RunAs(userId);
        var id = Guid.CreateVersion7();

        var result = await scope.ServiceProvider.GetRequiredService<IItemService>()
            .CreateDocumentAsync(new CreateDocumentCommand(id, $"{userId}/{id}/a.txt", "hash-1", "loser"), Ct);

        fired.ShouldBe(1);
        result.Created.ShouldBeFalse();
        result.Item.Title.ShouldBe("winner");
    }

    [Fact]
    public async Task DocumentWithKnownHash_IsReturnedInsteadOfCreatedTwice()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        await using var scope = Factory.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().RunAs(userId);
        var items = scope.ServiceProvider.GetRequiredService<IItemService>();
        var first = Guid.CreateVersion7();

        var created = await items.CreateDocumentAsync(new CreateDocumentCommand(first, $"{userId}/{first}/a.txt", "hash-2", "one"), Ct);
        var second = Guid.CreateVersion7();
        var again = await items.CreateDocumentAsync(new CreateDocumentCommand(second, $"{userId}/{second}/a.txt", "hash-2", "two"), Ct);

        created.Created.ShouldBeTrue();
        again.Created.ShouldBeFalse();
        again.Item.Id.ShouldBe(first);
    }

    [Fact]
    public async Task ItemAddedToCollectionByCompetingRequest_StillSucceeds()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        var collectionId = await CollectionsTests.CreateCollectionIdAsync(owner);
        var itemId = await ItemEndpointsTests.CreateNoteIdAsync(owner);
        var fired = 0;
        using var host = WithInterceptor(new SavingInterceptor(async (db, ct) =>
        {
            if (db.ChangeTracker.Entries<CollectionItem>().Any(e => e.State == EntityState.Added) && Interlocked.Exchange(ref fired, 1) == 0)
            {
                await ExecuteAsAdminAsync(
                    "INSERT INTO collection_items (collection_id, item_id) VALUES (@c, @i)",
                    ("c", Guid.Parse(collectionId)), ("i", Guid.Parse(itemId)));
            }
        }));
        using var racing = ClientOf(host, userId);

        var response = await racing.PostAsync($"/api/collections/{collectionId}/items/{itemId}", null, Ct);

        fired.ShouldBe(1);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // --- сбои базы при сохранении ---

    [Fact]
    public async Task TagRaceThatNeverResolves_GivesUpAfterThreeAttempts()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        var interceptor = new SavingInterceptor((db, _) =>
        {
            if (db.ChangeTracker.Entries<Item>().Any(e => e.State == EntityState.Added))
            {
                throw Failure(PostgresErrorCodes.UniqueViolation, "ix_tags_user_id_lower_name");
            }

            return Task.CompletedTask;
        });
        using var host = WithInterceptor(interceptor);
        using var racing = ClientOf(host, userId);

        var response = await racing.PostAsJsonAsync("/api/items", new { type = "note", body = "x", tags = new[] { "t" } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "ix_some_other_index")]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "ix_tags_user_id_lower_name")]
    public async Task UnrelatedDatabaseErrors_AreNotMistakenForRaces(string sqlState, string constraint)
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        using var host = WithInterceptor(new SavingInterceptor((db, _) =>
            db.ChangeTracker.Entries<Item>().Any(e => e.State == EntityState.Added) ? throw Failure(sqlState, constraint) : Task.CompletedTask));
        using var racing = ClientOf(host, userId);

        (await racing.PostAsJsonAsync("/api/items", new { type = "note", body = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await racing.PostAsJsonAsync("/api/items", new { type = "link", url = "https://example.com/a" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task DatabaseErrorOnTagRename_IsNotReportedAsNameConflict()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        await ItemEndpointsTests.CreateAsync(owner, new { type = "note", body = "x", tags = new[] { "old" } });
        var tagId = await TagsTests.TagIdAsync(owner, "old");
        using var host = WithInterceptor(new SavingInterceptor((db, _) =>
            db.ChangeTracker.Entries<Tag>().Any(e => e.State == EntityState.Modified)
                ? throw new DbUpdateException("save failed", new IOException("диск"))
                : Task.CompletedTask));
        using var racing = ClientOf(host, userId);

        (await racing.PutAsJsonAsync($"/api/tags/{tagId}", new { name = "new" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    // --- сбои хранилища ---

    [Fact]
    public async Task BlobDeleteFailure_DoesNotTurnDeletionIntoAnError()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        var (host, blobs) = WithFailingBlobs();
        using var _ = host;
        using var client = ClientOf(host, userId);
        var id = await Ingestion.DocumentUploadTests.UploadIdAsync(client, "isolation"u8.ToArray(), "to-delete.txt");
        blobs.FailDelete = true;

        (await client.DeleteAsync($"/api/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnexpectedStorageFailureDuringIngest_IsRetryableAndHidesInternals()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        var (host, blobs) = WithFailingBlobs();
        using var _ = host;
        using var client = ClientOf(host, userId);
        await Ingestion.DocumentUploadTests.UploadIdAsync(client, "text of the file"u8.ToArray(), "ingest-me.txt");
        blobs.FailRead = true;

        (await host.Services.GetRequiredService<JobWorker>().ProcessNextAsync(Ct)).ShouldBeTrue();

        await using var scope = host.Services.CreateAsyncScope();
        using var system = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
        var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Jobs.AsNoTracking().SingleAsync(j => j.Type == "ingest", Ct);
        job.Status.ShouldBe(JobStatus.Pending);
        job.LastError.ShouldNotBeNull().ShouldContain("Не удалось обработать запись");
    }

    // --- прочее ---

    [Fact]
    public async Task ListingWithoutTagFilter_ReturnsEverything()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        await ItemEndpointsTests.CreateNoteIdAsync(owner);
        await using var scope = Factory.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().RunAs(userId);

        var page = await scope.ServiceProvider.GetRequiredService<IItemService>().ListAsync(new ListItemsQuery(null, false, 1, 10), Ct);

        page.Total.ShouldBe(1);
    }

    [Fact]
    public async Task SynchronousSave_AssignsTheCurrentUserAsOwner()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        await using var scope = Factory.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().RunAs(userId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Collections.Add(new Collection { Id = Guid.CreateVersion7(), Name = "sync" });
        db.SaveChanges();

        db.Collections.AsNoTracking().Single().UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task IngestFailureHandler_IgnoresMissingItemsAndEmptyPayloads()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
        var handler = scope.ServiceProvider.GetServices<IJobFailureHandler>().Single(h => h.JobType == IngestItemJob.JobType);

        await handler.OnFailedAsync(new Job { Type = "ingest", Payload = JsonSerializer.Serialize(new IngestItemPayload(Guid.CreateVersion7())) }, "error", Ct);
        await handler.OnFailedAsync(new Job { Type = "ingest", Payload = "null" }, "error", Ct);
    }

    [Fact]
    public async Task StorageNotConfigured_StartsWithoutCreatingContainers()
    {
        using var host = Factory.WithWebHostBuilder(b => b.UseSetting("Storage:ConnectionString", ""));

        // Запуск хоста выполняет инициализатор контейнеров; без настроек он ничего не делает и не падает.
        (await host.CreateClient().GetAsync("/health", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DatabaseErrorWhenAddingToCollection_IsNotSwallowed()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        var collectionId = await CollectionsTests.CreateCollectionIdAsync(owner);
        var itemId = await ItemEndpointsTests.CreateNoteIdAsync(owner);
        using var host = WithInterceptor(new SavingInterceptor((db, _) =>
            db.ChangeTracker.Entries<CollectionItem>().Any(e => e.State == EntityState.Added)
                ? throw Failure(PostgresErrorCodes.ForeignKeyViolation, "fk_collection_items")
                : Task.CompletedTask));
        using var racing = ClientOf(host, userId);

        (await racing.PostAsync($"/api/collections/{collectionId}/items/{itemId}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task BlobStore_WritesThroughStreamAndReadsBack()
    {
        var store = new AzureBlobStore(Options.Create(new StorageOptions { ConnectionString = Factory.AzuriteConnectionString }));
        await store.EnsureContainersAsync(Ct);
        var path = $"{Guid.CreateVersion7()}/stream.txt";

        await using (var write = await store.OpenWriteAsync(BlobContainers.Exports, path, "text/plain", Ct))
        {
            await write.WriteAsync("через поток"u8.ToArray(), Ct);
        }

        await using var read = (await store.OpenReadAsync(BlobContainers.Exports, path, Ct)).ShouldNotBeNull();
        using var reader = new StreamReader(read);
        (await reader.ReadToEndAsync(Ct)).ShouldBe("через поток");
        await store.DeleteAsync(BlobContainers.Exports, path, Ct);
        (await store.OpenReadAsync(BlobContainers.Exports, path, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task NonPostgresErrorWhenAddingToCollection_IsNotSwallowedEither()
    {
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        var collectionId = await CollectionsTests.CreateCollectionIdAsync(owner);
        var itemId = await ItemEndpointsTests.CreateNoteIdAsync(owner);
        using var host = WithInterceptor(new SavingInterceptor((db, _) =>
            db.ChangeTracker.Entries<CollectionItem>().Any(e => e.State == EntityState.Added)
                ? throw new DbUpdateException("save failed", new IOException("диск"))
                : Task.CompletedTask));
        using var racing = ClientOf(host, userId);

        (await racing.PostAsync($"/api/collections/{collectionId}/items/{itemId}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }
}
