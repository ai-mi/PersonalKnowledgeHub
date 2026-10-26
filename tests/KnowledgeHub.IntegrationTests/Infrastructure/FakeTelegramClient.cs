using KnowledgeHub.Api.Modules.Bot;

namespace KnowledgeHub.IntegrationTests.Infrastructure;

/// <summary>Telegram без сети: запоминает отправленные сообщения и отдаёт заранее заложенные файлы.</summary>
public sealed class FakeTelegramClient : ITelegramClient
{
    private readonly Lock _gate = new();
    private readonly List<(long ChatId, string Text)> _sent = [];
    private readonly Dictionary<string, byte[]> _files = [];
    private readonly Dictionary<string, long> _reportedSizes = [];

    /// <summary>Имитирует сбой Telegram при отправке сообщений.</summary>
    public volatile bool FailSending;

    public IReadOnlyList<(long ChatId, string Text)> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent];
            }
        }
    }

    public IEnumerable<string> TextsTo(long chatId) => Sent.Where(m => m.ChatId == chatId).Select(m => m.Text);

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
            _files.Clear();
            _reportedSizes.Clear();
        }

        FailSending = false;
    }

    public Task SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        if (FailSending)
        {
            throw new HttpRequestException("Telegram недоступен.");
        }

        lock (_gate)
        {
            _sent.Add((chatId, text));
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

    public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken ct) => Task.CompletedTask;

    public Task SetWebhookAsync(string url, string secretToken, CancellationToken ct) => Task.CompletedTask;
}
