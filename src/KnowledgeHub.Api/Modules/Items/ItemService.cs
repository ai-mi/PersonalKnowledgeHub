using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeHub.Api.Modules.Items;

public sealed class ItemService(AppDbContext db, TimeProvider timeProvider) : IItemService
{
    public async Task<CreateItemResult> CreateAsync(CreateItemCommand command, CancellationToken ct)
    {
        var errors = ItemValidation.ValidateCreate(command, out var type, out var url);
        if (errors.Count > 0)
        {
            throw Errors.Validation(errors);
        }

        if (url is not null && await db.Items.FirstOrDefaultAsync(i => i.Url == url, ct) is { } existing)
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
        db.Items.Add(item);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (url is not null && e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Параллельный запрос успел сохранить ту же ссылку между проверкой и вставкой.
            db.Entry(item).State = EntityState.Detached;
            var winner = await db.Items.FirstAsync(i => i.Url == url, ct);
            return new CreateItemResult(winner, false);
        }

        return new CreateItemResult(item, true);
    }

    public Task<Item?> GetAsync(Guid id, CancellationToken ct) =>
        db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<PagedResult<ItemSummary>> ListAsync(ListItemsQuery query, CancellationToken ct)
    {
        var items = db.Items.AsNoTracking();
        if (query.Type is { } type)
        {
            items = items.Where(i => i.Type == type);
        }

        if (query.Unread)
        {
            items = items.Where(i => i.ReadAt == null);
        }

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
            .Select(i => new ItemSummary(i.Id, i.Type, i.Title, i.Url, i.Status, i.ReadAt, i.CreatedAt, i.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<ItemSummary>(page, query.Page, query.PageSize, total);
    }

    public async Task<Item?> UpdateAsync(Guid id, UpdateItemCommand command, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return null;
        }

        var errors = ItemValidation.ValidateContent(item.Type, command.Title, command.Body, command.UserNote, command.CodeLanguage);
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

        item.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct) =>
        await db.Items.Where(i => i.Id == id).ExecuteDeleteAsync(ct) > 0;

    public async Task<Item?> MarkReadAsync(Guid id, bool read, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
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
        return true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Пустые строки не храним; пробелы внутри текста (отступы кода) не трогаем.</summary>
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
