namespace KnowledgeHub.Api.Modules.Jobs;

/// <summary>Связывает тип задачи с обработчиком: десериализует payload и вызывает <see cref="IJobHandler{TPayload}"/>.</summary>
public sealed record JobHandlerRegistration(string Type, Func<IServiceProvider, string, CancellationToken, Task> Invoke);
