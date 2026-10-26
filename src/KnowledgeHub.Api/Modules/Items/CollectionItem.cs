namespace KnowledgeHub.Api.Modules.Items;

/// <summary>Вхождение записи в коллекцию (таблица <c>collection_items</c>); защищено внешними ключами на <c>collections</c> и <c>items</c>.</summary>
public class CollectionItem
{
    public Guid CollectionId { get; set; }
    public Guid ItemId { get; set; }
}
