using System.Text;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.Api.Modules.Ingestion;
using KnowledgeHub.Api.Modules.Items;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.UnitTests.Ingestion;

public sealed class DocumentIntakeTests
{
    private sealed class StubItems(Func<CreateDocumentCommand, CreateItemResult> create) : IItemService
    {
        public Task<CreateItemResult> CreateDocumentAsync(CreateDocumentCommand command, CancellationToken ct) => Task.FromResult(create(command));

        public Task<Item?> FindByContentHashAsync(string contentHash, CancellationToken ct) => Task.FromResult<Item?>(null);

        public Task<CreateItemResult> CreateAsync(CreateItemCommand command, CancellationToken ct) => throw new NotSupportedException();

        public Task<Item?> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();

        public Task<PagedResult<ItemSummary>> ListAsync(ListItemsQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ItemSummary>> ListUnreadCreatedBeforeAsync(DateTimeOffset createdBefore, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ItemSummary>> ListCreatedBetweenAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct) => throw new NotSupportedException();

        public Task<Item?> UpdateAsync(Guid id, UpdateItemCommand command, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();

        public Task<Item?> MarkReadAsync(Guid id, bool read, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> SetProcessingResultAsync(Guid id, ItemStatus status, string? title, string? body, string? error, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Item?> ReprocessAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingBlobs(bool failDelete = false) : IBlobStore
    {
        public List<string> Uploaded { get; } = [];
        public List<string> Deleted { get; } = [];

        public Task UploadAsync(string container, string path, Stream content, string contentType, CancellationToken ct)
        {
            Uploaded.Add(path);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string container, string path, CancellationToken ct)
        {
            Deleted.Add(path);
            return failDelete ? Task.FromException(new IOException("storage down")) : Task.CompletedTask;
        }

        public Task EnsureContainersAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string container, string path, CancellationToken ct) => throw new NotSupportedException();

        public Task<Stream> OpenWriteAsync(string container, string path, string contentType, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.CreateVersion7();
        public bool IsSystem => false;
        public Guid RequireUserId() => UserId!.Value;
    }

    private static Item AnItem() => new() { Id = Guid.CreateVersion7(), Type = ItemType.Document };

    private static Task<CreateItemResult> AcceptAsync(IItemService items, IBlobStore blobs) =>
        new DocumentIntake(items, blobs, new FixedUser(), Options.Create(new IngestionOptions()))
            .AcceptAsync(new MemoryStream(Encoding.UTF8.GetBytes("plain text")), "note.txt", new DocumentMetadata(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task NewDocument_KeepsItsBlob()
    {
        var blobs = new RecordingBlobs();

        var result = await AcceptAsync(new StubItems(_ => new CreateItemResult(AnItem(), true)), blobs);

        result.Created.ShouldBeTrue();
        blobs.Uploaded.ShouldHaveSingleItem();
        blobs.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task ParallelUploadWon_DeletesOurBlob()
    {
        var blobs = new RecordingBlobs();

        var result = await AcceptAsync(new StubItems(_ => new CreateItemResult(AnItem(), false)), blobs);

        result.Created.ShouldBeFalse();
        blobs.Deleted.ShouldBe(blobs.Uploaded);
    }

    [Fact]
    public async Task ParallelUploadWon_StorageFailureOnCleanup_IsIgnored()
    {
        var blobs = new RecordingBlobs(failDelete: true);

        var result = await AcceptAsync(new StubItems(_ => new CreateItemResult(AnItem(), false)), blobs);

        result.Created.ShouldBeFalse();
        blobs.Deleted.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ItemCreationFails_DeletesBlobAndRethrowsOriginalError()
    {
        var blobs = new RecordingBlobs(failDelete: true);

        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            AcceptAsync(new StubItems(_ => throw new InvalidOperationException("db down")), blobs));

        error.Message.ShouldBe("db down");
        blobs.Deleted.ShouldBe(blobs.Uploaded);
    }
}
