using KnowledgeHub.Api.Common;

namespace KnowledgeHub.Api.Modules.Identity;

public enum DigestFrequency
{
    Off,
    Daily,
    Weekly,
}

public class UserSettings : IUserOwned
{
    public static readonly IReadOnlySet<string> AllowedDigestChannels =
        new HashSet<string>(["telegram", "email"], StringComparer.Ordinal);

    public Guid UserId { get; set; }
    public string Timezone { get; set; } = "UTC";
    public DigestFrequency DigestFrequency { get; set; } = DigestFrequency.Off;
    public DayOfWeek DigestDay { get; set; } = DayOfWeek.Monday;
    public string[] DigestChannels { get; set; } = [];
    /// <summary>Действующий адрес для писем; <c>null</c> — адрес профиля. Новый адрес попадает сюда только после подтверждения.</summary>
    public string? Email { get; set; }

    /// <summary>Адрес, ждущий подтверждения кодом (см. <c>EmailConfirmationService</c>); писем на него не шлём.</summary>
    public string? PendingEmail { get; set; }

    /// <summary>SHA-256 кода подтверждения: сам код хранится только в письме.</summary>
    public string? PendingEmailCodeHash { get; set; }

    public DateTimeOffset? PendingEmailExpiresAt { get; set; }

    /// <summary>Неверных попыток ввода кода для <see cref="PendingEmail"/>.</summary>
    public int PendingEmailAttempts { get; set; }

    public void ClearPendingEmail()
    {
        PendingEmail = null;
        PendingEmailCodeHash = null;
        PendingEmailExpiresAt = null;
        PendingEmailAttempts = 0;
    }

    public static UserSettings CreateDefault(Guid userId, string? email) => new() { UserId = userId, Email = email };
}
