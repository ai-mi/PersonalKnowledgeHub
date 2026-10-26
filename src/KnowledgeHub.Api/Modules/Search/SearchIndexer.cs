using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeHub.Api.Modules.Search;

public sealed class SearchIndexer(AppDbContext db) : ISearchIndexer
{
    public async Task ReindexAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(SearchSql.Reindex, [new NpgsqlParameter("ids", itemIds.ToArray())], ct);
        }
    }
}
