using KnowledgeHub.Api.Infrastructure.Email;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Identity;

namespace KnowledgeHub.Api.Modules.Reminders;

public sealed partial class NotificationDispatcher(
    IBotNotifier bot, IEmailSender email, IUserContactService contacts, ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    public async Task<bool> SendAsync(Guid userId, NotificationChannel channel, NotificationMessage message, CancellationToken ct)
    {
        var delivered = false;
        if (channel.IncludesTelegram())
        {
            // BotNotifier сам логирует сбой и блокировку бота; «false» здесь — чат не привязан или отправка не удалась.
            delivered |= await bot.SendAsync(userId, message.Html, message.Buttons, ct);
        }

        if (channel.IncludesEmail())
        {
            delivered |= await SendEmailAsync(userId, message, ct);
        }

        if (!delivered)
        {
            LogNotDelivered(userId, channel);
        }

        return delivered;
    }

    private async Task<bool> SendEmailAsync(Guid userId, NotificationMessage message, CancellationToken ct)
    {
        if (await contacts.FindEmailAsync(userId, ct) is not { } address)
        {
            LogNoEmailAddress(userId);
            return false;
        }

        try
        {
            await email.SendAsync(address, message.Subject, message.Html, message.Text, ct);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogEmailFailed(userId, e);
            return false;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "У пользователя {UserId} нет адреса e-mail: письмо пропущено")]
    private partial void LogNoEmailAddress(Guid userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Не удалось отправить письмо пользователю {UserId}")]
    private partial void LogEmailFailed(Guid userId, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Уведомление пользователю {UserId} не доставлено (канал {Channel})")]
    private partial void LogNotDelivered(Guid userId, NotificationChannel channel);
}
