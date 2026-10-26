using KnowledgeHub.Api.Infrastructure.Persistence;
using Npgsql;

namespace KnowledgeHub.IntegrationTests.Infrastructure;

public class DatabaseRoleGuardTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task RegularApplicationRole_IsAccepted()
    {
        await using var dataSource = NpgsqlDataSource.Create(Factory.Postgres.AppConnectionString);

        await Should.NotThrowAsync(() => DatabaseRoleGuard.EnsureSafeRoleAsync(dataSource, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Superuser_IsRejected()
    {
        await using var dataSource = NpgsqlDataSource.Create(Factory.Postgres.AdminConnectionString);

        await Should.ThrowAsync<InvalidOperationException>(
            () => DatabaseRoleGuard.EnsureSafeRoleAsync(dataSource, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NonSuperuserWithBypassRls_IsRejected()
    {
        var connectionString = await CreateRoleAsync("kh_bypass", "NOSUPERUSER BYPASSRLS");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);

        await Should.ThrowAsync<InvalidOperationException>(
            () => DatabaseRoleGuard.EnsureSafeRoleAsync(dataSource, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RoleThatCanOnlyLogin_IsAccepted()
    {
        var connectionString = await CreateRoleAsync("kh_plain", "NOSUPERUSER NOBYPASSRLS");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);

        await Should.NotThrowAsync(() => DatabaseRoleGuard.EnsureSafeRoleAsync(dataSource, TestContext.Current.CancellationToken));
    }

    private async Task<string> CreateRoleAsync(string name, string attributes)
    {
        // Роли живут на уровне кластера и переживают сброс данных, поэтому создаём идемпотентно.
        await using var connection = new NpgsqlConnection(Factory.Postgres.AdminConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            DROP ROLE IF EXISTS {name};
            CREATE ROLE {name} LOGIN PASSWORD '{name}' {attributes};
            """,
            connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        return new NpgsqlConnectionStringBuilder(Factory.Postgres.AppConnectionString)
        {
            Username = name,
            Password = name,
        }.ConnectionString;
    }
}