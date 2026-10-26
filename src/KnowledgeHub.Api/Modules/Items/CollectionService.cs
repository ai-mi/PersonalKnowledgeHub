using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeHub.Api.Modules.Items;

public sealed class CollectionService(AppDbContext db)
{
    public Task<List<CollectionResponse>> ListAsync(CancellationToken ct) =>
        db.Collections.AsNoTracking()
            .OrderBy(c => c.Name.ToLower())
            .Select(c => new CollectionResponse(c.Id, c.Name, c.Description, c.Items.Count))
            .ToListAsync(ct);

    public async Task<CollectionResponse> CreateAsync(CollectionRequest request, CancellationToken ct)
    {
        var collection = new Collection { Id = Guid.CreateVersion7() };
        Apply(collection, request);
        db.Collections.Add(collection);
        await db.SaveChangesAsync(ct);
        return new CollectionResponse(collection.Id, collection.Name, collection.Description, 0);
    }

    /// <summary><c>null</c> — коллекции нет.</summary>
    public async Task<CollectionResponse?> UpdateAsync(Guid id, CollectionRequest request, CancellationToken ct)
    {
        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collection is null)
        {
            return null;
        }

        Apply(collection, request);
        await db.SaveChangesAsync(ct);
        var count = await db.Collections.Where(c => c.Id == id).Select(c => c.Items.Count).SingleAsync(ct);
        return new CollectionResponse(id, collection.Name, collection.Description, count);
    }

    /// <summary>Удаляет коллекцию; записи остаются. <c>false</c> — коллекции нет.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct) =>
        await db.Collections.Where(c => c.Id == id).ExecuteDeleteAsync(ct) > 0;

    /// <summary>Добавляет запись в коллекцию (повторное добавление ничего не меняет). <c>false</c> — нет коллекции или записи.</summary>
    public async Task<bool> AddItemAsync(Guid collectionId, Guid itemId, CancellationToken ct)
    {
        // Связующая таблица без user_id: принадлежность обеих сторон проверяем здесь через отфильтрованные наборы.
        if (!await db.Collections.AnyAsync(c => c.Id == collectionId, ct) || !await db.Items.AnyAsync(i => i.Id == itemId, ct))
        {
            return false;
        }

        var links = db.Set<CollectionItem>();
        if (!await links.AnyAsync(l => l.CollectionId == collectionId && l.ItemId == itemId, ct))
        {
            links.Add(new CollectionItem { CollectionId = collectionId, ItemId = itemId });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Параллельный запрос добавил ту же запись — результат тот же.
            }
        }

        return true;
    }

    /// <summary>Убирает запись из коллекции (если её там не было — ничего не меняется). <c>false</c> — коллекции нет.</summary>
    public async Task<bool> RemoveItemAsync(Guid collectionId, Guid itemId, CancellationToken ct)
    {
        if (!await db.Collections.AnyAsync(c => c.Id == collectionId, ct))
        {
            return false;
        }

        await db.Set<CollectionItem>().Where(l => l.CollectionId == collectionId && l.ItemId == itemId).ExecuteDeleteAsync(ct);
        return true;
    }

    private static void Apply(Collection collection, CollectionRequest request)
    {
        var name = request.Name?.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(name) || name.Length > Collection.MaxNameLength)
        {
            errors["name"] = [$"Name is required and must be at most {Collection.MaxNameLength} characters."];
        }

        if (description?.Length > Collection.MaxDescriptionLength)
        {
            errors["description"] = [$"Must be at most {Collection.MaxDescriptionLength} characters."];
        }

        if (errors.Count > 0)
        {
            throw Errors.Validation(errors);
        }

        collection.Name = name!;
        collection.Description = description;
    }
}
