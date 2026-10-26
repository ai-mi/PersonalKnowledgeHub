namespace KnowledgeHub.Api.Modules.Identity;

/// <summary>Внешний аккаунт (Microsoft/Google), привязанный к пользователю.</summary>
public class UserLogin
{
    public required string Provider { get; set; }
    public required string Subject { get; set; }
    public Guid UserId { get; set; }
}
