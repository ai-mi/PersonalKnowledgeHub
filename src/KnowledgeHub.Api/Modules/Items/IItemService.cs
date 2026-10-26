namespace KnowledgeHub.Api.Modules.Items;

/// <summary>Публичный интерфейс модуля Items: им пользуются API, Telegram-бот и Ingestion. Все методы работают с записями текущего пользователя.</summary>
public interface IItemService
{
    /// <summary>Создаёт запись; если такая ссылка у пользователя уже есть — возвращает существующую (<c>Created = false</c>).</summary>
    Task<CreateItemResult> CreateAsync(CreateItemCommand command, CancellationToken ct);

    /// <summary>
    /// Создаёт запись-документ для уже загруженного блоба (<see cref="CreateDocumentCommand.BlobPath"/>) и ставит задачу <c>ingest</c>.
    /// Если документ с таким хэшем есть — возвращает его (<c>Created = false</c>), и блоб команды остаётся на совести вызывающего.
    /// </summary>
    Task<CreateItemResult> CreateDocumentAsync(CreateDocumentCommand command, CancellationToken ct);

    /// <summary>Запись пользователя с таким хэшем содержимого или <c>null</c>.</summary>
    Task<Item?> FindByContentHashAsync(string contentHash, CancellationToken ct);

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

    /// <summary>
    /// Повторная обработка ссылки или документа: статус <c>pending</c> и новая задача <c>ingest</c>.
    /// Если запись уже в обработке — ничего не меняет. Заметки и фрагменты кода → ошибка валидации. <c>null</c> — записи нет.
    /// </summary>
    Task<Item?> ReprocessAsync(Guid id, CancellationToken ct);
}
