namespace KnowledgeHub.Api.Common;

/// <summary>
/// Сущность, принадлежащая пользователю. Для таких сущностей <c>AppDbContext</c> включает глобальный фильтр по <c>UserId</c>,
/// а в миграции таблицы нужно вызвать <c>EnableUserRls</c>.
/// </summary>
public interface IUserOwned
{
    Guid UserId { get; set; }
}
