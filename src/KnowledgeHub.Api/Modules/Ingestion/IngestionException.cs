namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>
/// Ошибка обработки страницы с понятным пользователю текстом (<see cref="Exception.Message"/>).
/// Неповторяемая ошибка (<c>Retryable = false</c>) сразу помечает запись как <c>failed</c>, без повторов задачи.
/// </summary>
public sealed class IngestionException(string message, bool retryable, Exception? inner = null) : Exception(message, inner)
{
    public bool Retryable { get; } = retryable;
}
