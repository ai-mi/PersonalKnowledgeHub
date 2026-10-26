using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>Необязательные поля загружаемого документа; заголовок по умолчанию — имя файла без расширения.</summary>
public sealed record DocumentMetadata(
    string? Title = null,
    string? UserNote = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<Guid>? CollectionIds = null);

/// <summary>Приём файла (API и Telegram-бот): проверка, хэш, блоб, запись и задача обработки.</summary>
public interface IDocumentIntake
{
    /// <summary>
    /// Сохраняет документ текущего пользователя. Тот же файл (по SHA-256) возвращает существующую запись (<c>Created = false</c>).
    /// Неподдерживаемый тип → ошибка валидации (400), размер больше лимита → 413.
    /// </summary>
    Task<CreateItemResult> AcceptAsync(Stream content, string fileName, DocumentMetadata metadata, CancellationToken ct);
}
