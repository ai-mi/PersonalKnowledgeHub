namespace KnowledgeHub.Api.Modules.Items;

/// <summary>Общие фильтры списка записей и поиска.</summary>
public static class ItemQueryExtensions
{
    /// <summary>Теги — логическое И (имена уже нормализованы <see cref="TagName.Normalize"/>).</summary>
    public static IQueryable<Item> Filter(
        this IQueryable<Item> items, ItemType? type, bool unread, IReadOnlyList<string>? tags, Guid? collectionId)
    {
        if (type is { } t)
        {
            items = items.Where(i => i.Type == t);
        }

        if (unread)
        {
            items = items.Where(i => i.ReadAt == null);
        }

        foreach (var tag in tags ?? [])
        {
            var key = tag.ToLowerInvariant();
            items = items.Where(i => i.Tags.Any(x => x.Name.ToLower() == key));
        }

        if (collectionId is { } id)
        {
            items = items.Where(i => i.Collections.Any(c => c.Id == id));
        }

        return items;
    }
}
