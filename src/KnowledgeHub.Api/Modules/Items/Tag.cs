using KnowledgeHub.Api.Common;

namespace KnowledgeHub.Api.Modules.Items;

public class Tag : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public List<Item> Items { get; } = [];
}
