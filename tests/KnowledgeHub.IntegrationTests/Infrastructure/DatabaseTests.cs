using Npgsql;

namespace KnowledgeHub.IntegrationTests.Infrastructure;

public class DatabaseTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Migrations_AreAppliedOnStartup()
    {
        _ = Factory.Server;

        await using var connection = new NpgsqlConnection(Factory.Postgres.AppConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""SELECT migration_id FROM "__EFMigrationsHistory" """, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var applied = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        applied.ShouldContain(id => id.EndsWith("_Initial"));
    }

    [Fact]
    public async Task Startup_Fails_WhenConnectedAsSuperuser()
    {
        using var unsafeFactory = Factory.WithWebHostBuilder(b =>
            b.UseSetting("Database:ConnectionString", Factory.Postgres.AdminConnectionString));

        var exception = await Should.ThrowAsync<Exception>(() => Task.Run(() => unsafeFactory.Server));

        exception.ToString().ShouldContain("Row-Level Security");
    }
}