using System.Text.Json;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Modules.Ingestion;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace KnowledgeHub.Api.Modules.Bot;

/// <summary>Payload задачи <c>telegram.update</c>: исходный JSON <c>Update</c> (webhook кладёт его как получил).</summary>
public sealed record TelegramUpdatePayload(string Json);

/// <summary>Обрабатывает входящее сообщение бота: привязка по коду или сохранение записи от имени владельца чата.</summary>
public sealed partial class ProcessUpdateJob(
    TelegramLinkService links, ITelegramClient telegram, IItemService items, IDocumentIntake intake, BotCommands commands,
    SystemContext systemContext, IOptions<IngestionOptions> ingestionOptions, ILogger<ProcessUpdateJob> logger) : IJobHandler<TelegramUpdatePayload>
{
    public const string JobType = "telegram.update";
    public static string Type => JobType;

    private const string NotLinkedText =
        "Этот чат не привязан к аккаунту. Откройте «Настройки» в Personal Knowledge Hub, нажмите «Привязать Telegram» и перейдите по ссылке.";

    public async Task HandleAsync(TelegramUpdatePayload payload, CancellationToken ct)
    {
        var update = JsonSerializer.Deserialize<Update>(payload.Json, JsonBotAPI.Options);
        if (update?.CallbackQuery is { } callback)
        {
            await HandleCallbackAsync(callback, ct);
            return;
        }

        if (update?.Message is not { } message)
        {
            return;
        }

        var chatId = message.Chat.Id;
        var command = MessageParser.ParseCommand(message);

        if (command is { Name: "start", Argument.Length: > 0 })
        {
            await LinkChatAsync(chatId, command.Argument, ct);
            return;
        }

        // Воркер запущен в системном контексте: чат → пользователь ищем без фильтра по пользователю.
        if (await links.FindByChatAsync(chatId, ct) is not { } link)
        {
            await ReplyAsync(chatId, NotLinkedText, ct);
            return;
        }

        using var _ = systemContext.RunAs(link.UserId);
        if (command is not null)
        {
            await ReplyAsync(chatId, await commands.ExecuteAsync(command, ct), ct);
            return;
        }

        await SaveAsync(chatId, message, ct);
    }

    /// <summary>Нажатие inline-кнопки: ответ на <c>callback_query</c> обязателен, иначе у кнопки в клиенте крутится индикатор.</summary>
    private async Task HandleCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        // В личном чате идентификатор чата совпадает с идентификатором пользователя Telegram.
        var chatId = callback.Message?.Chat.Id ?? callback.From.Id;
        string answer;
        if (await links.FindByChatAsync(chatId, ct) is { } link)
        {
            using var _ = systemContext.RunAs(link.UserId);
            answer = await commands.MarkReadAsync(callback.Data, ct);
        }
        else
        {
            answer = "Чат не привязан к аккаунту";
        }

        try
        {
            await telegram.AnswerCallbackQueryAsync(callback.Id, answer, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogReplyFailed(chatId, e);
        }
    }

    private async Task LinkChatAsync(long chatId, string code, CancellationToken ct)
    {
        var text = await links.RedeemAsync(code, chatId, ct) is not null
            ? "Аккаунт привязан. Присылайте ссылки, заметки и файлы — я их сохраню. Хэштеги в тексте станут тегами."
            : "Ссылка привязки недействительна или устарела. Создайте новую в «Настройках».";
        await ReplyAsync(chatId, text, ct);
    }

    private async Task SaveAsync(long chatId, Message message, CancellationToken ct)
    {
        var parsed = MessageParser.Parse(message);
        if (parsed is null)
        {
            await ReplyAsync(chatId, "Не нашёл, что сохранить. Пришлите ссылку, текст или файл (PDF, DOCX, TXT, MD).", ct);
            return;
        }

        try
        {
            var created = parsed.Item is { } item
                ? (await items.CreateAsync(item, ct)).Created
                : await SaveDocumentAsync(chatId, parsed.Document!, ct);
            // created == null: ответ уже отправлен (файл слишком большой).
            if (created is { } isNew)
            {
                await ReplyAsync(chatId, isNew ? "Сохранено" : "Уже было сохранено", ct);
            }
        }
        catch (AppException e)
        {
            // Ошибки валидации и размера — не сбой: повтор задачи ничего не изменит.
            await ReplyAsync(chatId, $"Не удалось сохранить: {BotMessageFormatter.Escape(Describe(e))}", ct);
        }
    }

    /// <returns><c>null</c>, если файл не принят и пользователю уже ответили.</returns>
    private async Task<bool?> SaveDocumentAsync(long chatId, ParsedDocument document, CancellationToken ct)
    {
        var maxBytes = ingestionOptions.Value.MaxDocumentBytes;
        var tooBigText = $"Файл больше {maxBytes / (1024 * 1024)} МБ — Telegram не отдаёт такие файлы ботам. Загрузите его через веб-интерфейс.";
        if (document.FileSize > maxBytes)
        {
            await ReplyAsync(chatId, tooBigText, ct);
            return null;
        }

        var file = await telegram.GetFileAsync(document.FileId, ct);
        if (file.Size > maxBytes)
        {
            await ReplyAsync(chatId, tooBigText, ct);
            return null;
        }

        // До 20 МБ: приём документа всё равно копирует поток на диск, а лимит проверяет сам.
        using var content = new MemoryStream();
        await telegram.DownloadFileAsync(file.FilePath, content, ct);
        content.Position = 0;
        return (await intake.AcceptAsync(content, document.FileName, document.Metadata, ct)).Created;
    }

    private static string Describe(AppException e) =>
        e.Errors?.Values.SelectMany(v => v).FirstOrDefault() ?? e.Message;

    // Ответ (HTML-разметка Telegram) — удобство: его сбой не должен повторять задачу (запись уже сохранена, повтор создал бы дубль заметки).
    private async Task ReplyAsync(long chatId, string text, CancellationToken ct)
    {
        try
        {
            await telegram.SendMessageAsync(chatId, text, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogReplyFailed(chatId, e);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Не удалось отправить ответ в Telegram-чат {ChatId}")]
    private partial void LogReplyFailed(long chatId, Exception exception);
}
