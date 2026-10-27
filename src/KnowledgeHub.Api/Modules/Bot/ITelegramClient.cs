namespace KnowledgeHub.Api.Modules.Bot;

/// <param name="FilePath">Путь для <see cref="ITelegramClient.DownloadFileAsync"/>.</param>
public sealed record TelegramFile(string FilePath, long? Size);

/// <summary>Inline-кнопка под сообщением; нажатие приходит боту как <c>callback_query</c> с <paramref name="CallbackData"/>.</summary>
public sealed record InlineButton(string Text, string CallbackData);

/// <summary>Telegram ответил <c>403</c>: пользователь заблокировал бота (или удалил чат).</summary>
public sealed class TelegramBlockedException(long chatId, Exception? inner = null)
    : Exception($"Telegram-чат {chatId} недоступен боту.", inner);

/// <summary>Тонкая обёртка над <c>TelegramBotClient</c>: только вызовы, нужные боту; в тестах подменяется фейком.</summary>
public interface ITelegramClient
{
    /// <param name="text">HTML-разметка Telegram: пользовательский текст должен быть экранирован (<see cref="BotMessageFormatter.Escape"/>).</param>
    /// <param name="buttons">Кнопки одним рядом или <c>null</c>.</param>
    /// <exception cref="TelegramBlockedException">Бот заблокирован в этом чате.</exception>
    Task SendMessageAsync(long chatId, string text, CancellationToken ct, IReadOnlyList<InlineButton>? buttons = null);

    /// <summary>Боты могут получить файлы только до 20 МБ.</summary>
    Task<TelegramFile> GetFileAsync(string fileId, CancellationToken ct);

    Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct);

    Task AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken ct);

    Task SetWebhookAsync(string url, string secretToken, CancellationToken ct);
}
