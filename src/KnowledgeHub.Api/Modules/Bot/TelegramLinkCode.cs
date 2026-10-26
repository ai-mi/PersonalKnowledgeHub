using KnowledgeHub.Api.Common;

namespace KnowledgeHub.Api.Modules.Bot;

/// <summary>Одноразовый код привязки Telegram. Сам код не хранится — только его SHA-256.</summary>
public class TelegramLinkCode : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
