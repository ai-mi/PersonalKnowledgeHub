namespace KnowledgeHub.Api.Modules.Jobs;

public interface IJobHandler<in TPayload>
{
    /// <summary>Тип задач, которые обрабатывает обработчик.</summary>
    static abstract string Type { get; }

    Task HandleAsync(TPayload payload, CancellationToken ct);
}
