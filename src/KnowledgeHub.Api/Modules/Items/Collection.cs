using KnowledgeHub.Api.Common;

namespace KnowledgeHub.Api.Modules.Items;

/// <summary>Плоская коллекция записей; запись может входить в несколько коллекций.</summary>
public class Collection : IUserOwned
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<Item> Items { get; } = [];
}
