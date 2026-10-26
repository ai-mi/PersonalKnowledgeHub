namespace KnowledgeHub.Api.Common;

/// <summary>
/// Переключает <see cref="ICurrentUser"/> текущего scope в системный режим (все данные) или в режим конкретного пользователя.
/// Нужен воркерам и местам, где пользователь ещё не известен (первый вход, поиск API-токена).
/// Возвращённый объект восстанавливает предыдущий режим при <c>Dispose</c>.
/// </summary>
public sealed class SystemContext
{
    public bool IsSystem { get; private set; }

    /// <summary>Пользователь, под которым выполняется <see cref="RunAs"/>; <c>null</c> — берётся пользователь запроса.</summary>
    public Guid? UserOverride { get; private set; }

    /// <summary>Системный режим: фильтр EF отключён, RLS пропускает все строки.</summary>
    public IDisposable Enter() => Switch(true, null);

    /// <summary>Режим конкретного пользователя (для воркеров, обрабатывающих данные пользователя).</summary>
    public IDisposable RunAs(Guid userId) => Switch(false, userId);

    private Restore Switch(bool isSystem, Guid? userOverride)
    {
        var restore = new Restore(this, IsSystem, UserOverride);
        IsSystem = isSystem;
        UserOverride = userOverride;
        return restore;
    }

    private sealed class Restore(SystemContext owner, bool isSystem, Guid? userOverride) : IDisposable
    {
        public void Dispose()
        {
            owner.IsSystem = isSystem;
            owner.UserOverride = userOverride;
        }
    }
}
