using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.Api.Modules.Ingestion;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeHub.Api.Modules.Items;

public static class ItemEndpoints
{
    internal const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    // Лимит файла (20 МБ) проверяет приём документа; запрос чуть больше — на поля формы и границы multipart.
    private const long MaxUploadRequestBytes = 21 * 1024 * 1024;

    public static void MapItemEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/items").RequireAuthorization();
        group.MapPost("", Create);
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Get);
        group.MapPut("/{id:guid}", Update);
        group.MapPatch("/{id:guid}", MarkRead);
        group.MapDelete("/{id:guid}", Delete);
        group.MapPost("/{id:guid}/reprocess", Reprocess);
        group.MapGet("/{id:guid}/file", DownloadFile);

        // Встроенная antiforgery-проверка форм отклонила бы загрузку с API-токеном; cookie-запросы проверяет наш фильтр группы /api.
        group.MapPost("/upload", Upload)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(MaxUploadRequestBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxUploadRequestBytes });
    }

    // Форма читается вручную: биндер Minimal API для коллекций из формы капризен, а поля здесь простые.
    private static async Task<Results<Created<ItemResponse>, Ok<ItemResponse>>> Upload(
        HttpRequest request, IDocumentIntake intake, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            throw Errors.Validation("file", "A multipart/form-data request with a file is required.");
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? throw Errors.Validation("file", "A file is required.");
        var collectionIds = form["collectionIds"].Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => Guid.TryParse(v, out var id) ? id : (Guid?)null).ToList();
        if (collectionIds.Contains(null))
        {
            throw Errors.Validation("collectionIds", "Must be a list of collection identifiers.");
        }

        var metadata = new DocumentMetadata(
            form["title"].FirstOrDefault(), form["userNote"].FirstOrDefault(),
            form["tags"].Where(t => t is not null).Select(t => t!).ToList(), collectionIds.Select(id => id!.Value).ToList());

        await using var content = file.OpenReadStream();
        var (item, created) = await intake.AcceptAsync(content, file.FileName, metadata, ct);
        var response = ItemResponse.From(item);
        return created ? TypedResults.Created($"/api/items/{item.Id}", response) : TypedResults.Ok(response);
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> DownloadFile(
        Guid id, IItemService items, IBlobStore blobs, HttpContext http, CancellationToken ct)
    {
        if (await items.GetAsync(id, ct) is not { Type: ItemType.Document, BlobPath: { } path }
            || await blobs.OpenReadAsync(BlobContainers.Documents, path, ct) is not { } stream)
        {
            return TypedResults.NotFound();
        }

        // Скачивание, а не показ в браузере; тип не угадывается по содержимому.
        http.Response.Headers.XContentTypeOptions = "nosniff";
        var fileName = Path.GetFileName(path);
        return TypedResults.File(stream, FileSignature.ContentTypeFor(fileName), fileName);
    }

    private static async Task<Results<Ok<ItemResponse>, NotFound>> Reprocess(Guid id, IItemService items, CancellationToken ct) =>
        Found(await items.ReprocessAsync(id, ct));

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
        var itemType = ParseType(type, errors);
        ValidatePaging(page, pageSize, errors);
        if (errors.Count > 0)
        {
            throw Errors.Validation(errors);
        }

        return TypedResults.Ok(await items.ListAsync(new ListItemsQuery(itemType, unread, page, pageSize, ParseTags(tags), collection), ct));
    }

    // Значения enum в API — camelCase ("note"), а биндер query-параметров сравнивает регистр строго.
    internal static ItemType? ParseType(string? type, Dictionary<string, string[]> errors)
    {
        var itemType = Enum.GetValues<ItemType>().Cast<ItemType?>()
            .FirstOrDefault(t => string.Equals(t.ToString(), type, StringComparison.OrdinalIgnoreCase));
        if (type is not null && itemType is null)
        {
            errors["type"] = [$"Allowed values: {string.Join(", ", Enum.GetNames<ItemType>().Select(n => n.ToLowerInvariant()))}."];
        }

        return itemType;
    }

    internal static void ValidatePaging(int page, int pageSize, Dictionary<string, string[]> errors)
    {
        if (page < 1)
        {
            errors["page"] = ["Must be at least 1."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"Must be between 1 and {MaxPageSize}."];
        }
    }

    /// <summary>Фильтр <c>tags=a,b</c>: имена нормализуются так же, как при сохранении.</summary>
    internal static List<string> ParseTags(string? tags) =>
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
