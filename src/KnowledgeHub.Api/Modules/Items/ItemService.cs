using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeHub.Api.Modules.Items;

public sealed class ItemService(AppDbContext db, TagService tags, ISearchIndexer searchIndexer, TimeProvider timeProvider) : IItemService
{
    /// <summary>Записи вместе с тегами и коллекциями (нужны для <see cref="ItemResponse"/>).</summary>
    private IQueryable<Item> WithRelations() => db.Items.Include(i => i.Tags).Include(i => i.Collections).AsSplitQuery();

    public Task<CreateItemResult> CreateAsync(CreateItemCommand command, CancellationToken ct) =>
        RetryOnTagRaceAsync(() => CreateOnceAsync(command, ct));

    public Task<Item?> UpdateAsync(Guid id, UpdateItemCommand command, CancellationToken ct) =>
        RetryOnTagRaceAsync(() => UpdateOnceAsync(id, command, ct));

    /// <summary>
    /// Параллельный запрос мог создать тот же новый тег между поиском и сохранением (нарушение уникального индекса тегов).
    /// Тогда сбрасываем контекст и повторяем: теперь тег уже найдётся.
    /// </summary>
    private async Task<T> RetryOnTagRaceAsync<T>(Func<Task<T>> operation)
    {
        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (DbUpdateException e) when (attempt < attempts && IsUnique(e, TagNameIndex))
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private const string TagNameIndex = "ix_tags_user_id_lower_name";
    private const string UrlIndex = "ix_items_user_id_url";

    private static bool IsUnique(DbUpdateException e, string constraint) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == constraint;

    private async Task<CreateItemResult> CreateOnceAsync(CreateItemCommand command, CancellationToken ct)
    {
        var errors = ItemValidation.ValidateCreate(command, out var type, out var url);
        if (errors.Count > 0)
        {
            throw Errors.Validation(errors);
        }

        if (url is not null && await WithRelations().FirstOrDefaultAsync(i => i.Url == url, ct) is { } existing)
        {
            return new CreateItemResult(existing, false);
        }

        var now = timeProvider.GetUtcNow();
        var item = new Item
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Title = Clean(command.Title),
            Url = url,
            Body = type is ItemType.Note or ItemType.Snippet ? NullIfBlank(command.Body) : null,
            UserNote = NullIfBlank(command.UserNote),
            CodeLanguage = type == ItemType.Snippet ? Clean(command.CodeLanguage) : null,
            // Ссылки получат статус pending, когда появится извлечение контента (Ingestion).
            Status = ItemStatus.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        };
        item.Tags.AddRange(await tags.ResolveAsync(command.Tags ?? [], ct));
        item.Collections.AddRange(await ResolveCollectionsAsync(command.CollectionIds ?? [], ct));
        db.Items.Add(item);

        try
        {
            await db.SaveChangesAsync(ct);
            await searchIndexer.ReindexAsync([item.Id], ct);
        }
        catch (DbUpdateException e) when (url is not null && IsUnique(e, UrlIndex))
        {
            // Параллельный запрос успел сохранить ту же ссылку между проверкой и вставкой.
            db.ChangeTracker.Clear();
            var winner = await WithRelations().FirstAsync(i => i.Url == url, ct);
            return new CreateItemResult(winner, false);
        }

        return new CreateItemResult(item, true);
    }

    public Task<Item?> GetAsync(Guid id, CancellationToken ct) =>
        WithRelations().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<PagedResult<ItemSummary>> ListAsync(ListItemsQuery query, CancellationToken ct)
    {
        var items = db.Items.AsNoTracking().Filter(query.Type, query.Unread, query.Tags, query.CollectionId);

        var total = await items.CountAsync(ct);

        // long: при огромном номере страницы int переполнился бы, и Skip получил бы отрицательное число.
        var skip = (long)(query.Page - 1) * query.PageSize;
        if (skip >= total)
        {
            return new PagedResult<ItemSummary>([], query.Page, query.PageSize, total);
        }

        var page = await items
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Skip((int)skip)
            .Take(query.PageSize)
            .Select(ItemSummary.Projection)
            .ToListAsync(ct);

        return new PagedResult<ItemSummary>(page.Select(s => s.WithSortedTags()).ToList(), query.Page, query.PageSize, total);
    }

    private async Task<Item?> UpdateOnceAsync(Guid id, UpdateItemCommand command, CancellationToken ct)
    {
        var item = await WithRelations().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return null;
        }

        var errors = ItemValidation.ValidateContent(item.Type, command.Title, command.Body, command.UserNote, command.CodeLanguage);
        ItemValidation.ValidateTags(errors, command.Tags);
        if (errors.Count > 0)
        {
            throw Errors.Validation(errors);
        }

        item.Title = Clean(command.Title);
        item.UserNote = NullIfBlank(command.UserNote);
        if (item.Type is ItemType.Note or ItemType.Snippet)
        {
            item.Body = NullIfBlank(command.Body);
        }

        if (item.Type == ItemType.Snippet)
        {
            item.CodeLanguage = Clean(command.CodeLanguage);
        }

        if (command.Tags is not null)
        {
            ReplaceAll(item.Tags, await tags.ResolveAsync(command.Tags, ct));
        }

        if (command.CollectionIds is not null)
        {
            ReplaceAll(item.Collections, await ResolveCollectionsAsync(command.CollectionIds, ct));
        }

        item.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await searchIndexer.ReindexAsync([item.Id], ct);
        return item;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct) =>
        await db.Items.Where(i => i.Id == id).ExecuteDeleteAsync(ct) > 0;

    public async Task<Item?> MarkReadAsync(Guid id, bool read, CancellationToken ct)
    {
        var item = await WithRelations().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        item.ReadAt = read ? item.ReadAt ?? now : null;
        item.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task<bool> SetProcessingResultAsync(
        Guid id, ItemStatus status, string? title, string? body, string? error, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return false;
        }

        item.Status = status;
        item.Body = body;
        item.Error = error;
        if (title is not null)
        {
            item.Title = title;
        }

        item.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await searchIndexer.ReindexAsync([item.Id], ct);
        return true;
    }

    /// <summary>Находит коллекции пользователя; неизвестный или чужой идентификатор — ошибка валидации.</summary>
    private async Task<List<Collection>> ResolveCollectionsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        var found = await db.Collections.Where(c => distinct.Contains(c.Id)).ToListAsync(ct);
        if (found.Count != distinct.Count)
        {
            throw Errors.Validation("collectionIds", "Unknown collection.");
        }

        return found;
    }

    private static void ReplaceAll<T>(List<T> target, List<T> replacement)
    {
        target.RemoveAll(e => !replacement.Contains(e));
        target.AddRange(replacement.Where(e => !target.Contains(e)));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Пустые строки не храним; пробелы внутри текста (отступы кода) не трогаем.</summary>
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
