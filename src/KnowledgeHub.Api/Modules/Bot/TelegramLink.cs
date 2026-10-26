using KnowledgeHub.Api.Common;

namespace KnowledgeHub.Api.Modules.Bot;

/// <summary>Привязка Telegram-чата к пользователю: один чат на пользователя, один пользователь на чат.</summary>
public class TelegramLink : IUserOwned
{
    public Guid UserId { get; set; }
    public long ChatId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}
