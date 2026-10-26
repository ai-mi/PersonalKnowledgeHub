using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>Уведомление об окончании обработки записи (готова или окончательно провалилась). Бот подписывается на него в фиче 19.</summary>
public interface IIngestionCompletedHandler
{
    /// <param name="status"><see cref="ItemStatus.Ready"/> или <see cref="ItemStatus.Failed"/>.</param>
    Task OnCompletedAsync(Guid itemId, ItemStatus status, string? error, CancellationToken ct);
}

/// <summary>Реализация по умолчанию: никого не уведомляет.</summary>
public sealed class NoOpIngestionCompletedHandler : IIngestionCompletedHandler
{
    public Task OnCompletedAsync(Guid itemId, ItemStatus status, string? error, CancellationToken ct) => Task.CompletedTask;
}
