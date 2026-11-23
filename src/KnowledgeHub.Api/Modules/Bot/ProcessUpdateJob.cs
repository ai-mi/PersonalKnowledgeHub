using System.Text.Json;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Modules.Ingestion;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

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

    private const string PrivateChatOnlyText = "Я работаю только в личном чате. Напишите мне напрямую.";

    private const string InvalidCodeText = "Ссылка привязки недействительна или устарела. Создайте новую в «Настройках».";

    /// <summary>Callback кнопки подтверждения переноса: префикс + код привязки (22 символа, вместе меньше лимита Telegram в 64 байта).</summary>
    private const string RelinkCallbackPrefix = "relink:";

    /// <summary>{0} — аккаунт, к которому хотят перенести чат (<see cref="BotMessageFormatter.Account"/>).</summary>
    private const string RelinkWarningText =
        "Этот чат уже привязан к другому аккаунту Personal Knowledge Hub. Ссылка переносит его в аккаунт {0}. " +
        "Если вы сами не открывали ссылку привязки к этому аккаунту — ничего не нажимайте: возможно, вам прислали " +
        "чужую ссылку, чтобы ваши сообщения сохранялись у другого человека.";

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

        // Только личный чат: привязанная группа или канал дали бы всем участникам права владельца аккаунта.
        // На команды отвечаем подсказкой, обычные сообщения группы молча пропускаем — бот не должен в ней шуметь.
        if (message.Chat.Type != ChatType.Private)
        {
            if (command is not null)
            {
                await ReplyAsync(chatId, PrivateChatOnlyText, ct);
            }

            return;
        }

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
        if (callback.Message is { } source && source.Chat.Type != ChatType.Private)
        {
            answer = PrivateChatOnlyText;
        }
        else if (callback.Data?.StartsWith(RelinkCallbackPrefix, StringComparison.Ordinal) == true)
        {
            // Владелец чата сам подтвердил перенос кнопкой из предупреждения (см. LinkChatAsync).
            answer = (await links.RedeemAsync(callback.Data[RelinkCallbackPrefix.Length..], chatId, replaceOtherOwner: true, ct)).Outcome == RedeemOutcome.Linked
                ? "Чат привязан к новому аккаунту"
                : InvalidCodeText;
        }
        else if (await links.FindByChatAsync(chatId, ct) is { } link)
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
        var result = await links.RedeemAsync(code, chatId, replaceOtherOwner: false, ct);
        switch (result.Outcome)
        {
            case RedeemOutcome.Linked:
                // Аккаунт назван явно: открыв чужую ссылку, пользователь увидит не своё имя и почту.
                await ReplyAsync(chatId,
                    $"Аккаунт привязан: {result.Account}. Если это не ваш аккаунт — отвяжите чат в «Настройках» или пришлите ссылку из своего. " +
                    "Присылайте ссылки, заметки и файлы — я их сохраню. Хэштеги в тексте станут тегами.", ct);
                break;
            case RedeemOutcome.NeedsConfirmation:
                // Сообщение уходит в этот же чат, то есть текущему владельцу привязки: он и решает, отдавать ли чат.
                await ReplyAsync(chatId, string.Format(RelinkWarningText, result.Account), ct,
                    [new InlineButton("Привязать к новому аккаунту", RelinkCallbackPrefix + code)]);
                break;
            default:
                await ReplyAsync(chatId, InvalidCodeText, ct);
                break;
        }
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
    private async Task ReplyAsync(long chatId, string text, CancellationToken ct, IReadOnlyList<InlineButton>? buttons = null)
    {
        try
        {
            await telegram.SendMessageAsync(chatId, text, ct, buttons);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogReplyFailed(chatId, e);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Не удалось отправить ответ в Telegram-чат {ChatId}")]
    private partial void LogReplyFailed(long chatId, Exception exception);
}
