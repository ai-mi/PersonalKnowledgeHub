using KnowledgeHub.Api.Modules.Bot;

namespace KnowledgeHub.IntegrationTests.Infrastructure;

/// <summary>Отправленное фейком сообщение.</summary>
public sealed record SentMessage(long ChatId, string Text, IReadOnlyList<InlineButton>? Buttons);

/// <summary>Telegram без сети: запоминает отправленные сообщения и отдаёт заранее заложенные файлы.</summary>
public sealed class FakeTelegramClient : ITelegramClient
{
    private readonly Lock _gate = new();
    private readonly List<SentMessage> _sent = [];
    private readonly List<(string CallbackQueryId, string? Text)> _answers = [];
    private readonly HashSet<long> _blockedChats = [];
    private readonly Dictionary<string, byte[]> _files = [];
    private readonly Dictionary<string, long> _reportedSizes = [];

    /// <summary>Имитирует сбой Telegram при отправке сообщений.</summary>
    public volatile bool FailSending;

    public IReadOnlyList<SentMessage> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent];
            }
        }
    }

    public IReadOnlyList<(string CallbackQueryId, string? Text)> Answers
    {
        get
        {
            lock (_gate)
            {
                return [.. _answers];
            }
        }
    }

    public IEnumerable<string> TextsTo(long chatId) => Sent.Where(m => m.ChatId == chatId).Select(m => m.Text);

    /// <summary>Чат, где бот заблокирован: отправка падает так же, как при ответе Telegram <c>403</c>.</summary>
    public void BlockChat(long chatId)
    {
        lock (_gate)
        {
            _blockedChats.Add(chatId);
        }
    }

    /// <summary>Файл, который «лежит» в Telegram под идентификатором <paramref name="fileId"/>.</summary>
    /// <param name="reportedSize">Размер, который Telegram назвал бы в <c>getFile</c>; по умолчанию — настоящий.</param>
    public void AddFile(string fileId, byte[] content, long? reportedSize = null)
    {
        lock (_gate)
        {
            _files[fileId] = content;
            _reportedSizes[fileId] = reportedSize ?? content.Length;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _sent.Clear();
            _answers.Clear();
            _blockedChats.Clear();
            _files.Clear();
            _reportedSizes.Clear();
        }

        FailSending = false;
    }

    public Task SendMessageAsync(long chatId, string text, CancellationToken ct, IReadOnlyList<InlineButton>? buttons = null)
    {
        if (FailSending)
        {
            throw new HttpRequestException("Telegram недоступен.");
        }

        lock (_gate)
        {
            if (_blockedChats.Contains(chatId))
            {
                throw new TelegramBlockedException(chatId);
            }

            _sent.Add(new SentMessage(chatId, text, buttons));
        }

        return Task.CompletedTask;
    }

    public Task<TelegramFile> GetFileAsync(string fileId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(new TelegramFile(fileId, _reportedSizes[fileId]));
        }
    }

    public async Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct)
    {
        byte[] content;
        lock (_gate)
        {
            content = _files[filePath];
        }

        await destination.WriteAsync(content, ct);
    }

    public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken ct)
    {
        lock (_gate)
        {
            _answers.Add((callbackQueryId, text));
        }

        return Task.CompletedTask;
    }

    public Task SetWebhookAsync(string url, string secretToken, CancellationToken ct) => Task.CompletedTask;
}
