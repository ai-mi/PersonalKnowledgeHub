using Microsoft.AspNetCore.Http.HttpResults;

namespace KnowledgeHub.Api.Modules.Items;

public static class CollectionEndpoints
{
    public static void MapCollectionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/collections").RequireAuthorization();
        group.MapGet("", async (CollectionService collections, CancellationToken ct) => TypedResults.Ok(await collections.ListAsync(ct)));
        group.MapPost("", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);
        group.MapPost("/{id:guid}/items/{itemId:guid}", AddItem);
        group.MapDelete("/{id:guid}/items/{itemId:guid}", RemoveItem);
    }

    private static async Task<Created<CollectionResponse>> Create(
        CollectionRequest request, CollectionService collections, CancellationToken ct)
    {
        var created = await collections.CreateAsync(request, ct);
        return TypedResults.Created($"/api/collections/{created.Id}", created);
    }

    private static async Task<Results<Ok<CollectionResponse>, NotFound>> Update(
        Guid id, CollectionRequest request, CollectionService collections, CancellationToken ct) =>
        await collections.UpdateAsync(id, request, ct) is { } collection ? TypedResults.Ok(collection) : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> Delete(Guid id, CollectionService collections, CancellationToken ct) =>
        await collections.DeleteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> AddItem(
        Guid id, Guid itemId, CollectionService collections, CancellationToken ct) =>
        await collections.AddItemAsync(id, itemId, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> RemoveItem(
        Guid id, Guid itemId, CollectionService collections, CancellationToken ct) =>
        await collections.RemoveItemAsync(id, itemId, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
}
