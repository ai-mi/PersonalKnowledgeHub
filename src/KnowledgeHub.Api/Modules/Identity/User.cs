namespace KnowledgeHub.Api.Modules.Identity;

public class User
{
    public const int MaxEmailLength = 320;
    public const int MaxDisplayNameLength = 200;

    public Guid Id { get; set; }
    public string? Email { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
