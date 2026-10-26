namespace KnowledgeHub.Api.Modules.Search;

/// <summary>SQL построения <c>search_vector</c>. Один источник правды для индексатора и миграции Search.</summary>
public static class SearchSql
{
    /// <summary>tsvector допускает ~1 МБ, поэтому длинное тело обрезается.</summary>
    public const int MaxBodyLength = 500_000;

    /// <summary>Выражение над строкой таблицы <c>items</c> с псевдонимом <c>i</c>: заголовок (A) > заметка, теги, адрес (B) > текст (C).</summary>
    public static readonly string VectorExpression = $"""
        setweight(to_tsvector('russian', coalesce(i.title, '')), 'A')
        || setweight(to_tsvector('russian',
            coalesce(i.user_note, '') || ' '
            || coalesce((SELECT string_agg(t.name, ' ') FROM item_tags it JOIN tags t ON t.id = it.tag_id WHERE it.item_id = i.id), '') || ' '
            || coalesce(i.url, '')), 'B')
        || setweight(to_tsvector('russian', left(coalesce(i.body, ''), {MaxBodyLength})), 'C')
        """;

    public static readonly string Reindex = $"UPDATE items i SET search_vector = {VectorExpression} WHERE i.id = ANY(@ids)";

    public static readonly string ReindexAll = $"UPDATE items i SET search_vector = {VectorExpression}";
}
