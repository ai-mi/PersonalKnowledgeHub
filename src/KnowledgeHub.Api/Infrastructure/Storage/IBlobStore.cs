namespace KnowledgeHub.Api.Infrastructure.Storage;

/// <summary>Приватное хранилище файлов (документы, архивы экспорта). Пути — <c>{userId}/{itemId}/{имя}</c>.</summary>
public interface IBlobStore
{
    /// <summary>Создаёт недостающие контейнеры из <see cref="BlobContainers.All"/>.</summary>
    Task EnsureContainersAsync(CancellationToken ct);

    /// <summary>Записывает файл, заменяя существующий.</summary>
    Task UploadAsync(string container, string path, Stream content, string contentType, CancellationToken ct);

    /// <summary>Поток для чтения; <c>null</c> — файла нет.</summary>
    Task<Stream?> OpenReadAsync(string container, string path, CancellationToken ct);

    /// <summary>Поток для записи (экспорт собирает архив на лету); данные фиксируются при закрытии потока.</summary>
    Task<Stream> OpenWriteAsync(string container, string path, string contentType, CancellationToken ct);

    /// <summary>Удаляет файл; отсутствие файла — не ошибка.</summary>
    Task DeleteAsync(string container, string path, CancellationToken ct);
}
