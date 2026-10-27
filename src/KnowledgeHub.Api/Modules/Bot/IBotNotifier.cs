namespace KnowledgeHub.Api.Modules.Bot;

/// <summary>Отправка сообщений пользователю в привязанный Telegram-чат. Общий путь для уведомлений, напоминаний и дайджеста.</summary>
public interface IBotNotifier
{
    /// <param name="text">HTML-разметка Telegram с экранированным пользовательским текстом (<see cref="BotMessageFormatter"/>).</param>
    /// <returns>
    /// <c>true</c> — сообщение отправлено. <c>false</c> — чат не привязан или отправка не удалась; сбой не бросается, чтобы
    /// уведомление не роняло и не повторяло основную работу. Если бот заблокирован (<c>403</c>), привязка пользователя удаляется.
    /// </returns>
    Task<bool> SendAsync(Guid userId, string text, IReadOnlyList<InlineButton>? buttons = null, CancellationToken ct = default);
}
