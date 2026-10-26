namespace KnowledgeHub.Api.Modules.Items;

/// <summary>Создание записи; он же тело <c>POST /api/items</c>. Документы создаются только загрузкой файла.</summary>
public sealed record CreateItemCommand(
    ItemType? Type,
    string? Title = null,
    string? Url = null,
    string? Body = null,
    string? UserNote = null,
    string? CodeLanguage = null);

/// <summary>Замена редактируемых полей; он же тело <c>PUT /api/items/{id}</c>. Адрес ссылки не меняется.</summary>
public sealed record UpdateItemCommand(
    string? Title = null,
    string? Body = null,
    string? UserNote = null,
    string? CodeLanguage = null);

public sealed record ListItemsQuery(ItemType? Type, bool Unread, int Page, int PageSize);

public sealed record CreateItemResult(Item Item, bool Created);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

/// <summary>Запись для списка: без тела, которое может быть большим.</summary>
public sealed record ItemSummary(
    Guid Id,
    ItemType Type,
    string? Title,
    string? Url,
    ItemStatus Status,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ItemResponse(
    Guid Id,
    ItemType Type,
    string? Title,
    string? Url,
    string? Body,
    string? UserNote,
    string? CodeLanguage,
    ItemStatus Status,
    string? Error,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ItemResponse From(Item i) => new(
        i.Id, i.Type, i.Title, i.Url, i.Body, i.UserNote, i.CodeLanguage, i.Status, i.Error, i.ReadAt, i.CreatedAt, i.UpdatedAt);
}
