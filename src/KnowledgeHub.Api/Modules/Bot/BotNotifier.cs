using KnowledgeHub.Api.Common;

namespace KnowledgeHub.Api.Modules.Bot;

public sealed partial class BotNotifier(
    TelegramLinkService links, ITelegramClient telegram, SystemContext systemContext, ILogger<BotNotifier> logger) : IBotNotifier
{
    public async Task<bool> SendAsync(Guid userId, string text, IReadOnlyList<InlineButton>? buttons = null, CancellationToken ct = default)
    {
        // Вызывающий может работать в контексте другого пользователя (или без него): привязку ищем по явному userId.
        using var _ = systemContext.Enter();
        if (await links.FindByUserAsync(userId, ct) is not { } link)
        {
            return false;
        }

        try
        {
            await telegram.SendMessageAsync(link.ChatId, text, ct, buttons);
            return true;
        }
        catch (TelegramBlockedException)
        {
            await links.RemoveForUserAsync(userId, ct);
            LogBlocked(userId, link.ChatId);
            return false;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogSendFailed(userId, e);
            return false;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Бот заблокирован в чате {ChatId} пользователя {UserId}: привязка Telegram удалена")]
    private partial void LogBlocked(Guid userId, long chatId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Не удалось отправить сообщение в Telegram пользователю {UserId}")]
    private partial void LogSendFailed(Guid userId, Exception exception);
}
