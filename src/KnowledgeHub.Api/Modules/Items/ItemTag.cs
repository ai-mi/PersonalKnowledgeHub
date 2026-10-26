namespace KnowledgeHub.Api.Modules.Items;

/// <summary>Связь записи с тегом (таблица <c>item_tags</c>); защищена внешними ключами на <c>items</c> и <c>tags</c>.</summary>
public class ItemTag
{
    public Guid ItemId { get; set; }
    public Guid TagId { get; set; }
}
