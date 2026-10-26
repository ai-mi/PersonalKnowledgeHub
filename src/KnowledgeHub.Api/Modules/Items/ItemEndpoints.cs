using KnowledgeHub.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KnowledgeHub.Api.Modules.Items;

public static class ItemEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static void MapItemEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/items").RequireAuthorization();
        group.MapPost("", Create);
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Get);
        group.MapPut("/{id:guid}", Update);
        group.MapPatch("/{id:guid}", MarkRead);
        group.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<Results<Created<ItemResponse>, Ok<ItemResponse>>> Create(
        CreateItemCommand command, IItemService items, CancellationToken ct)
    {
        var (item, created) = await items.CreateAsync(command, ct);
        var response = ItemResponse.From(item);
        return created ? TypedResults.Created($"/api/items/{item.Id}", response) : TypedResults.Ok(response);
    }

    private static async Task<Ok<PagedResult<ItemSummary>>> List(
        IItemService items, CancellationToken ct, string? type = null, bool unread = false, int page = 1, int pageSize = DefaultPageSize,
        string? tags = null, Guid? collection = null)
    {
        var errors = new Dictionary<string, string[]>();
        // Значения enum в API — camelCase ("note"), а биндер query-параметров сравнивает регистр строго.
        var itemType = Enum.GetValues<ItemType>().Cast<ItemType?>()
            .FirstOrDefault(t => string.Equals(t.ToString(), type, StringComparison.OrdinalIgnoreCase));
        if (type is not null && itemType is null)
        {
            errors["type"] = [$"Allowed values: {string.Join(", ", Enum.GetNames<ItemType>().Select(n => n.ToLowerInvariant()))}."];
        }

        if (page < 1)
        {
            errors["page"] = ["Must be at least 1."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"Must be between 1 and {MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            throw Errors.Validation(errors);
        }

        return TypedResults.Ok(await items.ListAsync(new ListItemsQuery(itemType, unread, page, pageSize, ParseTags(tags), collection), ct));
    }

    /// <summary>Фильтр <c>tags=a,b</c>: имена нормализуются так же, как при сохранении.</summary>
    private static List<string> ParseTags(string? tags) =>
        (tags ?? "").Split(',').Select(TagName.Normalize).Where(t => t.Length > 0).ToList();

    private static async Task<Results<Ok<ItemResponse>, NotFound>> Get(Guid id, IItemService items, CancellationToken ct) =>
        Found(await items.GetAsync(id, ct));

    private static async Task<Results<Ok<ItemResponse>, NotFound>> Update(
        Guid id, UpdateItemCommand command, IItemService items, CancellationToken ct) =>
        Found(await items.UpdateAsync(id, command, ct));

    private static async Task<Results<Ok<ItemResponse>, NotFound>> MarkRead(
        Guid id, MarkReadRequest request, IItemService items, CancellationToken ct) =>
        request.Read is { } read
            ? Found(await items.MarkReadAsync(id, read, ct))
            : throw Errors.Validation("read", "read is required.");

    private static async Task<Results<NoContent, NotFound>> Delete(Guid id, IItemService items, CancellationToken ct) =>
        await items.DeleteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static Results<Ok<ItemResponse>, NotFound> Found(Item? item) =>
        item is null ? TypedResults.NotFound() : TypedResults.Ok(ItemResponse.From(item));

    public sealed record MarkReadRequest(bool? Read);
}
