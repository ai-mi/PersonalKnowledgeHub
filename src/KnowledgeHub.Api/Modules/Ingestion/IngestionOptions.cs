namespace KnowledgeHub.Api.Modules.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Только для тестов: разрешает loopback-адреса и любые порты, чтобы ходить в WireMock на localhost.</summary>
    public bool AllowLoopbackForTests { get; set; }

    public long MaxResponseBytes { get; set; } = 5 * 1024 * 1024;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
    public int MaxRedirects { get; set; } = 5;

    /// <summary>Лимит загружаемого документа (совпадает с лимитом скачивания файлов Telegram Bot API).</summary>
    public long MaxDocumentBytes { get; set; } = 20 * 1024 * 1024;
}
