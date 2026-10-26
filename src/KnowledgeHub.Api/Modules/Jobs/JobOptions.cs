namespace KnowledgeHub.Api.Modules.Jobs;

public sealed class JobOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Выключает воркер и планировщик (в тестах задачи обрабатываются вызовом <c>ProcessNextAsync</c>).</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}
