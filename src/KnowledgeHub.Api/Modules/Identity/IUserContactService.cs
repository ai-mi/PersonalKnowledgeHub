namespace KnowledgeHub.Api.Modules.Identity;

/// <summary>Публичный интерфейс модуля Identity: контакты пользователя для уведомлений вне его запроса.</summary>
public interface IUserContactService
{
    /// <summary>Адрес для уведомлений: <c>user_settings.email</c>, иначе e-mail профиля; <c>null</c> — адреса нет.</summary>
    Task<string?> FindEmailAsync(Guid userId, CancellationToken ct);
}
