namespace KnowledgeHub.Api.Modules.Jobs;

/// <summary>
/// Фоновая задача. Таблица системная: <c>user_id</c> нужен только для запуска обработчика от имени пользователя,
/// поэтому <see cref="Job"/> не <c>IUserOwned</c> и RLS на неё не включается.
/// </summary>
public class Job
{
    public const int MaxTypeLength = 100;
    public const int MaxDedupKeyLength = 200;
    public const int MaxErrorLength = 4000;

    public Guid Id { get; set; }
    public required string Type { get; set; }

    /// <summary>JSON-payload (<c>jsonb</c>), хранится строкой.</summary>
    public required string Payload { get; set; }
    public JobStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset RunAfter { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public Guid? UserId { get; set; }
    public string? DedupKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
