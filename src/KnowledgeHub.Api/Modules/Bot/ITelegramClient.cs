namespace KnowledgeHub.Api.Modules.Bot;

/// <param name="FilePath">Путь для <see cref="ITelegramClient.DownloadFileAsync"/>.</param>
public sealed record TelegramFile(string FilePath, long? Size);

/// <summary>Тонкая обёртка над <c>TelegramBotClient</c>: только вызовы, нужные боту; в тестах подменяется фейком.</summary>
public interface ITelegramClient
{
    Task SendMessageAsync(long chatId, string text, CancellationToken ct);

    /// <summary>Боты могут получить файлы только до 20 МБ.</summary>
    Task<TelegramFile> GetFileAsync(string fileId, CancellationToken ct);

    Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct);

    Task AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken ct);

    Task SetWebhookAsync(string url, string secretToken, CancellationToken ct);
}
