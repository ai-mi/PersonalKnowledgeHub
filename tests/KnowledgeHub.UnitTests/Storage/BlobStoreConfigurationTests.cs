using KnowledgeHub.Api.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.UnitTests.Storage;

public sealed class BlobStoreConfigurationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", "  ", false)]
    [InlineData("UseDevelopmentStorage=true", null, true)]
    [InlineData(null, "https://acc.blob.core.windows.net", true)]
    [InlineData("UseDevelopmentStorage=true", "https://acc.blob.core.windows.net", true)]
    public void IsConfigured_NeedsConnectionStringOrAccountUri(string? connectionString, string? accountUri, bool expected) =>
        new StorageOptions { ConnectionString = connectionString, AccountUri = accountUri }.IsConfigured.ShouldBe(expected);

    [Fact]
    public async Task WithoutSettings_FailsOnFirstUse()
    {
        var store = new AzureBlobStore(Options.Create(new StorageOptions()));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => store.DeleteAsync("documents", "a", Ct));

        error.Message.ShouldContain("Storage:ConnectionString");
    }

    [Fact]
    public async Task WithAccountUri_BuildsManagedIdentityClientLazily()
    {
        var store = new AzureBlobStore(Options.Create(new StorageOptions { AccountUri = "https://acc.blob.core.windows.net" }));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Клиент создаётся (без обращения к сети), а отменённая операция обрывается до отправки запроса.
        await Should.ThrowAsync<OperationCanceledException>(() => store.DeleteAsync("documents", "a", cancelled.Token));
    }
}
