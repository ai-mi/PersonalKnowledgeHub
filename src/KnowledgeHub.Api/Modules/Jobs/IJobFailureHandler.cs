namespace KnowledgeHub.Api.Modules.Jobs;

/// <summary>Вызывается, когда задача своего типа окончательно провалилась (например, чтобы пометить запись как <c>failed</c>).</summary>
public interface IJobFailureHandler
{
    string JobType { get; }

    /// <param name="error">Короткое сообщение об ошибке без стека.</param>
    Task OnFailedAsync(Job job, string error, CancellationToken ct);
}
