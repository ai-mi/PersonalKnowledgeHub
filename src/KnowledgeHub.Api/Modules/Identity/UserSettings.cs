namespace KnowledgeHub.Api.Modules.Identity;

public enum DigestFrequency
{
    Off,
    Daily,
    Weekly,
}

public class UserSettings
{
    public static readonly IReadOnlySet<string> AllowedDigestChannels =
        new HashSet<string>(["telegram", "email"], StringComparer.Ordinal);

    public Guid UserId { get; set; }
    public string Timezone { get; set; } = "UTC";
    public DigestFrequency DigestFrequency { get; set; } = DigestFrequency.Off;
    public DayOfWeek DigestDay { get; set; } = DayOfWeek.Monday;
    public string[] DigestChannels { get; set; } = [];
    public string? Email { get; set; }

    public static UserSettings CreateDefault(Guid userId, string? email) => new() { UserId = userId, Email = email };
}
