using System.Linq.Expressions;

namespace KnowledgeHub.Api.Modules.Items;

/// <summary>
/// Создание записи; он же тело <c>POST /api/items</c>. Документы создаются только загрузкой файла.
/// <paramref name="Tags"/> и <paramref name="CollectionIds"/>: имена тегов (создаются при необходимости) и коллекции пользователя.
/// </summary>
public sealed record CreateItemCommand(
    ItemType? Type,
    string? Title = null,
    string? Url = null,
    string? Body = null,
    string? UserNote = null,
    string? CodeLanguage = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<Guid>? CollectionIds = null);

/// <summary>
/// Замена редактируемых полей; он же тело <c>PUT /api/items/{id}</c>. Адрес ссылки не меняется.
/// Теги и коллекции: <c>null</c> — оставить как есть, пустой список — снять все.
/// </summary>
public sealed record UpdateItemCommand(
    string? Title = null,
    string? Body = null,
    string? UserNote = null,
    string? CodeLanguage = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<Guid>? CollectionIds = null);

/// <summary><paramref name="Tags"/> — нормализованные имена, запись должна иметь все (логическое И); <paramref name="CollectionId"/> — запись входит в коллекцию.</summary>
public sealed record ListItemsQuery(
    ItemType? Type,
    bool Unread,
    int Page,
    int PageSize,
    IReadOnlyList<string>? Tags = null,
    Guid? CollectionId = null);

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
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Tags)
{
    public static readonly Expression<Func<Item, ItemSummary>> Projection = i => new ItemSummary(
        i.Id, i.Type, i.Title, i.Url, i.Status, i.ReadAt, i.CreatedAt, i.UpdatedAt, i.Tags.Select(t => t.Name).ToList());

    /// <summary>Порядок тегов в SQL не определён, поэтому после выборки сортируем.</summary>
    public ItemSummary WithSortedTags() => this with { Tags = TagName.Sort(Tags) };
}

public sealed record CollectionRef(Guid Id, string Name);

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
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Tags,
    IReadOnlyList<CollectionRef> Collections)
{
    /// <summary>Теги и коллекции записи должны быть загружены.</summary>
    public static ItemResponse From(Item i) => new(
        i.Id, i.Type, i.Title, i.Url, i.Body, i.UserNote, i.CodeLanguage, i.Status, i.Error, i.ReadAt, i.CreatedAt, i.UpdatedAt,
        TagName.Sort(i.Tags.Select(t => t.Name)),
        i.Collections.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => new CollectionRef(c.Id, c.Name)).ToList());
}

public sealed record TagResponse(Guid Id, string Name, int ItemCount);

public sealed record TagRequest(string? Name);

public sealed record CollectionResponse(Guid Id, string Name, string? Description, int ItemCount);

public sealed record CollectionRequest(string? Name, string? Description);
