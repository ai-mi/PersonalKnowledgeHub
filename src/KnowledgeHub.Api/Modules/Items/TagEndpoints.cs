using Microsoft.AspNetCore.Http.HttpResults;

namespace KnowledgeHub.Api.Modules.Items;

public static class TagEndpoints
{
    public static void MapTagEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tags").RequireAuthorization();
        group.MapGet("", async (TagService tags, CancellationToken ct) => TypedResults.Ok(await tags.ListAsync(ct)));
        group.MapPut("/{id:guid}", Rename);
        group.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<Results<Ok<TagResponse>, NotFound>> Rename(
        Guid id, TagRequest request, TagService tags, CancellationToken ct) =>
        await tags.RenameAsync(id, request.Name, ct) is { } tag ? TypedResults.Ok(tag) : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> Delete(Guid id, TagService tags, CancellationToken ct) =>
        await tags.DeleteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
}
