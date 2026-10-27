using KnowledgeHub.Api.Common;

namespace KnowledgeHub.Api.Modules.Export;

public enum ExportStatus
{
    Pending,
    Processing,
    Ready,
    Failed,
}

/// <summary>Запрос на экспорт данных пользователя и результат — ZIP-архив в Blob Storage.</summary>
public class ExportRecord : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ExportStatus Status { get; set; }

    /// <summary>Путь архива в контейнере <c>exports</c>; <c>null</c>, пока архив не готов.</summary>
    public string? BlobPath { get; set; }

    /// <summary>Когда архив будет удалён; <c>null</c>, пока экспорт выполняется.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
