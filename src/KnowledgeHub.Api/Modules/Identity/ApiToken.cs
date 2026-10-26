using KnowledgeHub.Api.Common;

namespace KnowledgeHub.Api.Modules.Identity;

/// <summary>Персональный API-токен для скриптов. Сам токен не хранится — только его SHA-256.</summary>
public class ApiToken : IUserOwned
{
    public const int MaxNameLength = 100;
    public const int MaxExpiresInDays = 3650;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
