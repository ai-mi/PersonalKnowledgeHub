using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace KnowledgeHub.Api.Modules.Bot;

/// <param name="httpClient">Только для тестов: подменяет сетевой транспорт; в приложении не регистрируется, и клиент создаёт свой.</param>
public sealed class TelegramClient(IOptions<BotOptions> options, HttpClient? httpClient = null) : ITelegramClient
{
    // Клиент создаётся при первом обращении: без токена (локально, в тестах) приложение всё равно должно стартовать.
    private readonly Lazy<TelegramBotClient> _client = new(() =>
        string.IsNullOrWhiteSpace(options.Value.Token)
            ? throw new InvalidOperationException("Не задан токен Telegram-бота (Bot:Token).")
            : new TelegramBotClient(options.Value.Token, httpClient));

    public async Task SendMessageAsync(long chatId, string text, CancellationToken ct) =>
        await _client.Value.SendMessage(chatId, text, cancellationToken: ct);

    public async Task<TelegramFile> GetFileAsync(string fileId, CancellationToken ct)
    {
        var file = await _client.Value.GetFile(fileId, ct);
        return new TelegramFile(file.FilePath ?? throw new InvalidOperationException("Telegram не вернул путь к файлу."), file.FileSize);
    }

    public Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct) =>
        _client.Value.DownloadFile(filePath, destination, ct);

    public async Task AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken ct) =>
        await _client.Value.AnswerCallbackQuery(callbackQueryId, text, cancellationToken: ct);

    public Task SetWebhookAsync(string url, string secretToken, CancellationToken ct) =>
        _client.Value.SetWebhook(url, secretToken: secretToken, allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery], cancellationToken: ct);
}
