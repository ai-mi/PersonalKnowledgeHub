using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.Api.Modules.Search;

/// <summary>Публичный интерфейс модуля Search: им пользуются API и Telegram-бот. Ищет среди записей текущего пользователя.</summary>
public interface ISearchService
{
    /// <summary>Пустой запрос — ошибка валидации (<c>400</c>).</summary>
    Task<PagedResult<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct);
}
