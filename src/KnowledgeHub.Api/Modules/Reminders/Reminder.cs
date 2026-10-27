using KnowledgeHub.Api.Common;

namespace KnowledgeHub.Api.Modules.Reminders;

public class Reminder : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ItemId { get; set; }
    public DateTimeOffset RemindAt { get; set; }
    public NotificationChannel Channel { get; set; }

    /// <summary><c>null</c> — ещё не отправлено.</summary>
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
