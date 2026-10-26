namespace KnowledgeHub.Api.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Строка подключения (локально — Azurite). Если пусто, используется <see cref="AccountUri"/>.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Адрес Blob-эндпоинта аккаунта (<c>https://&lt;account&gt;.blob.core.windows.net</c>); вход — Managed Identity.</summary>
    public string? AccountUri { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString) || !string.IsNullOrWhiteSpace(AccountUri);
}

/// <summary>Имена приватных контейнеров (создаются Bicep и, если их нет, при старте приложения).</summary>
public static class BlobContainers
{
    public const string Documents = "documents";
    public const string Exports = "exports";

    public static readonly string[] All = [Documents, Exports];
}
