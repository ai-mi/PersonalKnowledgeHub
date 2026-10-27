using Microsoft.AspNetCore.Http.HttpResults;

namespace KnowledgeHub.Api.Modules.Export;

public static class ExportEndpoints
{
    public static void MapExports(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/exports").RequireAuthorization();
        group.MapGet("", async (ExportService exports, CancellationToken ct) => TypedResults.Ok(await exports.ListAsync(ct)));
        group.MapPost("", Start);
        group.MapGet("/{id:guid}/download", Download);
    }

    private static async Task<Accepted<ExportResponse>> Start(ExportService exports, CancellationToken ct)
    {
        var export = await exports.StartAsync(ct);
        return TypedResults.Accepted($"/api/exports/{export.Id}", export);
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> Download(Guid id, ExportService exports, CancellationToken ct) =>
        await exports.OpenDownloadAsync(id, ct) is { } download
            ? TypedResults.File(download.Content, "application/zip", download.FileName)
            : TypedResults.NotFound();
}
