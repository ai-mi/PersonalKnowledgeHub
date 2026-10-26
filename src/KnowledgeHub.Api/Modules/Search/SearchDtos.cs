using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.Api.Modules.Search;

/// <param name="Text">Запрос в синтаксисе <c>websearch_to_tsquery</c>: слова, "фраза", -исключение, or.</param>
/// <param name="From">Записи, созданные не раньше.</param>
/// <param name="To">Записи, созданные не позже.</param>
public sealed record SearchQuery(
    string Text,
    ItemType? Type = null,
    IReadOnlyList<string>? Tags = null,
    Guid? CollectionId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool Unread = false,
    int Page = 1,
    int PageSize = 20);

/// <param name="Highlights">Фрагменты текста; единственный HTML в них — <c>&lt;mark&gt;</c>, остальное экранировано.</param>
public sealed record SearchHit(ItemSummary Item, float Rank, IReadOnlyList<string> Highlights);
