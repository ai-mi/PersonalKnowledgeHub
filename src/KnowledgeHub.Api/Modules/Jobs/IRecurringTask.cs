namespace KnowledgeHub.Api.Modules.Jobs;

/// <summary>Периодическое действие; выполняется в системном контексте.</summary>
public interface IRecurringTask
{
    string Name { get; }
    TimeSpan Interval { get; }
    Task ExecuteAsync(CancellationToken ct);
}
