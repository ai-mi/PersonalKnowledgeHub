using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(KnowledgeHub.IntegrationTests.Infrastructure.PostgresContainer))]

namespace KnowledgeHub.IntegrationTests.Infrastructure;

/// <summary>Один контейнер PostgreSQL на всю сборку тестов; роли и база создаются тем же init.sql, что и в docker-compose.</summary>
public sealed class PostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    /// <summary>Подключение ролью приложения kh_app (NOSUPERUSER, NOBYPASSRLS).</summary>
    public string AppConnectionString => Build("kh_app", "kh_app");

    /// <summary>Подключение суперпользователем postgres — только для сброса данных и проверки guard.</summary>
    public string AdminConnectionString => Build("postgres", "postgres");

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var result = await _container.ExecScriptAsync(await File.ReadAllTextAsync(FindInitScript()));
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"init.sql failed: {result.Stderr}");
        }
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private string Build(string user, string password) => new NpgsqlConnectionStringBuilder
    {
        Host = _container.Hostname,
        Port = _container.GetMappedPublicPort(5432),
        Database = "knowledgehub",
        Username = user,
        Password = password,
        Pooling = true,
    }.ConnectionString;

    private static string FindInitScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docker", "postgres", "init.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("docker/postgres/init.sql not found above the test output directory.");
    }
}