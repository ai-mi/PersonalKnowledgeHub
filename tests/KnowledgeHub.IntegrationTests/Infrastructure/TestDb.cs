using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace KnowledgeHub.IntegrationTests.Infrastructure;

/// <summary>Пользователь, которого тест выставляет вручную (в приложении его даёт <c>CurrentUser</c>).</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public bool IsSystem { get; set; }

    public Guid RequireUserId() => UserId ?? throw new InvalidOperationException("Пользователь не задан.");

    public static TestCurrentUser For(Guid userId) => new() { UserId = userId };

    public static TestCurrentUser System() => new() { IsSystem = true };

    public static TestCurrentUser Anonymous() => new();
}

public static class TestDb
{
    /// <summary>
    /// Контекст приложения с тем же перехватчиком соединений, что и в Program.cs.
    /// <paramref name="connectionUser"/> — кого видит PostgreSQL (RLS); по умолчанию совпадает с <paramref name="user"/> (фильтр EF).
    /// </summary>
    public static AppDbContext Create(
        string connectionString, ICurrentUser user, ICurrentUser? connectionUser = null, params IInterceptor[] extraInterceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new UserContextConnectionInterceptor(connectionUser ?? user))
            .AddInterceptors(extraInterceptors)
            .Options, user);
}
