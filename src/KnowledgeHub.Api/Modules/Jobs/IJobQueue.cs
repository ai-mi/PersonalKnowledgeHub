namespace KnowledgeHub.Api.Modules.Jobs;

/// <summary>Постановка задач в очередь. Работает в текущей транзакции <c>AppDbContext</c>, если она открыта.</summary>
public interface IJobQueue
{
    /// <returns><c>false</c>, если задача с таким <paramref name="dedupKey"/> уже есть.</returns>
    Task<bool> EnqueueAsync<TPayload>(
        string type,
        TPayload payload,
        Guid? userId = null,
        DateTimeOffset? runAfter = null,
        string? dedupKey = null,
        CancellationToken ct = default);
}
