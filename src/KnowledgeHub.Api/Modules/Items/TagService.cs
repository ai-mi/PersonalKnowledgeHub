using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeHub.Api.Modules.Items;

public sealed class TagService(AppDbContext db)
{
    /// <summary>
    /// Находит теги пользователя по именам без учёта регистра, недостающие добавляет в контекст (сохраняет вызывающий).
    /// Имена нормализуются через <see cref="TagName.Normalize"/>, пустые и повторы отбрасываются.
    /// </summary>
    public async Task<List<Tag>> ResolveAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var wanted = names.Select(TagName.Normalize).Where(n => n.Length > 0).Distinct(TagName.Comparer).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var keys = wanted.Select(n => n.ToLowerInvariant()).ToList();
        var existing = (await db.Tags.Where(t => keys.Contains(t.Name.ToLower())).ToListAsync(ct))
            .ToDictionary(t => t.Name, TagName.Comparer);

        var result = new List<Tag>(wanted.Count);
        foreach (var name in wanted)
        {
            if (!existing.TryGetValue(name, out var tag))
            {
                tag = new Tag { Id = Guid.CreateVersion7(), Name = name };
                db.Tags.Add(tag);
            }

            result.Add(tag);
        }

        return result;
    }

    public Task<List<TagResponse>> ListAsync(CancellationToken ct) =>
        db.Tags.AsNoTracking()
            .OrderBy(t => t.Name.ToLower())
            .Select(t => new TagResponse(t.Id, t.Name, t.Items.Count))
            .ToListAsync(ct);

    /// <summary><c>null</c> — тега нет. Имя, уже занятое другим тегом, — <c>409</c>.</summary>
    public async Task<TagResponse?> RenameAsync(Guid id, string? rawName, CancellationToken ct)
    {
        var name = TagName.Normalize(rawName);
        if (name.Length is 0 or > TagName.MaxLength)
        {
            throw Errors.Validation("name", $"Name is required and must be at most {TagName.MaxLength} characters.");
        }

        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null)
        {
            return null;
        }

        tag.Name = name;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw Errors.Conflict("A tag with this name already exists.");
        }

        var count = await db.Tags.Where(t => t.Id == id).Select(t => t.Items.Count).SingleAsync(ct);
        return new TagResponse(id, tag.Name, count);
    }

    /// <summary>Удаляет тег и снимает его со всех записей. <c>false</c> — тега нет.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct) =>
        await db.Tags.Where(t => t.Id == id).ExecuteDeleteAsync(ct) > 0;
}
