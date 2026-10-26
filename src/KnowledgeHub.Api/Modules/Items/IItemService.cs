namespace KnowledgeHub.Api.Modules.Items;

/// <summary>Публичный интерфейс модуля Items: им пользуются API, Telegram-бот и Ingestion. Все методы работают с записями текущего пользователя.</summary>
public interface IItemService
{
    /// <summary>Создаёт запись; если такая ссылка у пользователя уже есть — возвращает существующую (<c>Created = false</c>).</summary>
    Task<CreateItemResult> CreateAsync(CreateItemCommand command, CancellationToken ct);

    Task<Item?> GetAsync(Guid id, CancellationToken ct);

    Task<PagedResult<ItemSummary>> ListAsync(ListItemsQuery query, CancellationToken ct);

    /// <summary><c>null</c> — записи нет.</summary>
    Task<Item?> UpdateAsync(Guid id, UpdateItemCommand command, CancellationToken ct);

    /// <summary><c>false</c> — записи нет.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Отмечает запись прочитанной или непрочитанной; повторная отметка «прочитано» сохраняет исходное время. <c>null</c> — записи нет.</summary>
    Task<Item?> MarkReadAsync(Guid id, bool read, CancellationToken ct);

    /// <summary>
    /// Результат фоновой обработки (Ingestion): статус, извлечённый текст и ошибка. Заголовок меняется, только если он передан.
    /// <c>false</c> — записи нет.
    /// </summary>
    Task<bool> SetProcessingResultAsync(Guid id, ItemStatus status, string? title, string? body, string? error, CancellationToken ct);
}
