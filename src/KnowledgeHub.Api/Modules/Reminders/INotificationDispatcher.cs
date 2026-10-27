using KnowledgeHub.Api.Modules.Bot;

namespace KnowledgeHub.Api.Modules.Reminders;

/// <param name="Subject">Тема письма.</param>
/// <param name="Html">HTML для Telegram и e-mail: только экранированный текст и теги <c>&lt;a&gt;</c> (<see cref="BotMessageFormatter.Escape"/>).</param>
/// <param name="Text">Текстовая версия письма.</param>
/// <param name="Buttons">Inline-кнопки Telegram; в письме их нет.</param>
public sealed record NotificationMessage(string Subject, string Html, string Text, IReadOnlyList<InlineButton>? Buttons = null);

/// <summary>Общая отправка уведомлений в Telegram и/или на e-mail — для напоминаний и дайджеста.</summary>
public interface INotificationDispatcher
{
    /// <returns>
    /// <c>true</c> — доставлено хотя бы по одному каналу. Канал без настройки (нет привязки Telegram, нет адреса, e-mail не настроен)
    /// пропускается с записью в лог; сбой отправки не бросается — основная работа не должна повторяться из-за уведомления.
    /// </returns>
    Task<bool> SendAsync(Guid userId, NotificationChannel channel, NotificationMessage message, CancellationToken ct);
}
