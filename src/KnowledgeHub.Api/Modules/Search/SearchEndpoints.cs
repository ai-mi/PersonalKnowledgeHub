using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Modules.Items;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KnowledgeHub.Api.Modules.Search;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/search", Search).RequireAuthorization();
    }

    private static async Task<Ok<PagedResult<SearchHit>>> Search(
        ISearchService search, CancellationToken ct, string? q = null, string? type = null, string? tags = null, Guid? collection = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, bool unread = false, int page = 1, int pageSize = ItemEndpoints.DefaultPageSize)
    {
        var errors = new Dictionary<string, string[]>();
        var itemType = ItemEndpoints.ParseType(type, errors);
        ItemEndpoints.ValidatePaging(page, pageSize, errors);
        if (string.IsNullOrWhiteSpace(q))
        {
            errors["q"] = ["Query is required."];
        }

        if (errors.Count > 0)
        {
            throw Errors.Validation(errors);
        }

        var query = new SearchQuery(q!, itemType, ItemEndpoints.ParseTags(tags), collection, from, to, unread, page, pageSize);
        return TypedResults.Ok(await search.SearchAsync(query, ct));
    }
}
