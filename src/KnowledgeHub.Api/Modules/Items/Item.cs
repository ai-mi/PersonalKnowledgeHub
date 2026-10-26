using KnowledgeHub.Api.Common;

namespace KnowledgeHub.Api.Modules.Items;

/// <summary>Запись пользователя: ссылка, заметка, фрагмент кода или документ. Различия типов — в nullable-полях.</summary>
public class Item : IUserOwned
{
    public const int MaxTitleLength = 500;
    public const int MaxUserNoteLength = 20_000;
    public const int MaxBodyLength = 200_000;
    public const int MaxCodeLanguageLength = 50;
    public const int MaxUrlLength = 2048;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ItemType Type { get; set; }
    public string? Title { get; set; }

    /// <summary>Нормализованный адрес ссылки (см. <see cref="UrlNormalizer"/>).</summary>
    public string? Url { get; set; }

    /// <summary>Текст заметки или фрагмента кода; для ссылок и документов — извлечённый текст.</summary>
    public string? Body { get; set; }
    public string? UserNote { get; set; }
    public string? CodeLanguage { get; set; }
    public string? BlobPath { get; set; }
    public string? ContentHash { get; set; }
    public ItemStatus Status { get; set; }
    public string? Error { get; set; }

    /// <summary><c>null</c> — не прочитано.</summary>
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
