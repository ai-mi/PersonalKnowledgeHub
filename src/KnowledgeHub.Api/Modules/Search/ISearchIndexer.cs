namespace KnowledgeHub.Api.Modules.Search;

public interface ISearchIndexer
{
    /// <summary>Пересчитывает поисковый индекс записей текущего пользователя (заголовок, заметка, теги, адрес, текст).</summary>
    Task ReindexAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct);
}
