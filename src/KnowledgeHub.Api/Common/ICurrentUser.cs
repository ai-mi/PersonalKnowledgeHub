namespace KnowledgeHub.Api.Common;

/// <summary>Пользователь текущего запроса (или фоновой задачи).</summary>
public interface ICurrentUser
{
    /// <summary>Идентификатор пользователя; <c>null</c> для анонимного запроса.</summary>
    Guid? UserId { get; }

    /// <summary>Системный контекст (воркеры): доступ ко всем данным без привязки к пользователю.</summary>
    bool IsSystem { get; }

    /// <summary>Возвращает <see cref="UserId"/> или бросает исключение, если пользователь не определён.</summary>
    Guid RequireUserId();
}
