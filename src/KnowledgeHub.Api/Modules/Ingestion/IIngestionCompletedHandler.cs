using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>Уведомление об окончании обработки записи (готова или окончательно провалилась). Бот подписывается на него, чтобы сообщить в Telegram.</summary>
public interface IIngestionCompletedHandler
{
    /// <param name="status"><see cref="ItemStatus.Ready"/> или <see cref="ItemStatus.Failed"/>.</param>
    /// <param name="notifyChatId">Чат из задачи <c>ingest</c>; <c>null</c> — уведомлять некого.</param>
    Task OnCompletedAsync(Guid itemId, ItemStatus status, string? error, long? notifyChatId, CancellationToken ct);
}

/// <summary>Реализация по умолчанию: никого не уведомляет.</summary>
public sealed class NoOpIngestionCompletedHandler : IIngestionCompletedHandler
{
    public Task OnCompletedAsync(Guid itemId, ItemStatus status, string? error, long? notifyChatId, CancellationToken ct) => Task.CompletedTask;
}
