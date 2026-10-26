using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KnowledgeHub.Api.Infrastructure.Persistence;

/// <summary>
/// Отказывается стартовать под ролью, способной обойти Row-Level Security, и только затем применяет миграции.
/// </summary>
public sealed class DatabaseRoleGuard(
    NpgsqlDataSource dataSource,
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options) : IHostedService
{
    // CASE гарантирует, что pg_has_role не вызовется для несуществующей роли azure_pg_admin (локально её нет).
    private const string UnsafeRoleSql = """
        SELECT r.rolsuper OR r.rolbypassrls
            OR CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'azure_pg_admin')
                    THEN pg_has_role(current_user, 'azure_pg_admin', 'MEMBER')
                    ELSE false END
        FROM pg_roles r
        WHERE r.rolname = current_user
        """;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureSafeRoleAsync(dataSource, cancellationToken);

        if (options.Value.MigrateOnStartup)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task EnsureSafeRoleAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(UnsafeRoleSql);
        var isUnsafe = (bool?)await command.ExecuteScalarAsync(cancellationToken) ?? true;

        if (isUnsafe)
        {
            throw new InvalidOperationException(
                "The database role is superuser, has BYPASSRLS or is a member of azure_pg_admin: " +
                "Row-Level Security would be bypassed. Use a regular application role.");
        }
    }
}