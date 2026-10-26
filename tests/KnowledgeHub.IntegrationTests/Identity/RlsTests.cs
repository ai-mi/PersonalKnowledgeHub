using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace KnowledgeHub.IntegrationTests.Identity;

/// <summary>Изоляция данных на двух уровнях: политики PostgreSQL (сырой SQL, в обход EF) и фильтр EF + перехватчик соединений.</summary>
public sealed class RlsTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Guid _a = Guid.CreateVersion7();
    private readonly Guid _b = Guid.CreateVersion7();

    // ---- Подготовка и вспомогательные методы ----

    /// <summary>Создаёт пользователя соединением postgres (оно обходит RLS); <paramref name="withSettings"/> — со строкой в user_settings.</summary>
    private async Task SeedUserAsync(Guid id, bool withSettings = true)
    {
        await using var connection = new NpgsqlConnection(Factory.Postgres.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users (id, email, display_name, created_at) VALUES (@id, NULL, 'user', now());
            INSERT INTO user_settings (user_id, timezone, digest_frequency, digest_day, digest_channels, email)
                SELECT @id, 'UTC', 'off', 'monday', ARRAY[]::text[], NULL WHERE @withSettings;
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("withSettings", withSettings);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task SeedAandBAsync()
    {
        await SeedUserAsync(_a);
        await SeedUserAsync(_b);
    }

    /// <summary>Открывает соединение ролью приложения и выставляет контекст так же, как перехватчик (null — контекст не задан вовсе).</summary>
    private async Task<NpgsqlConnection> OpenRawAsync(Guid? userId, bool system = false, bool setContext = true)
    {
        var connection = new NpgsqlConnection(Factory.Postgres.AppConnectionString);
        await connection.OpenAsync(Ct);
        if (setContext)
        {
            await using var command = new NpgsqlCommand(
                "SELECT set_config('app.user_id', @uid, false), set_config('app.is_system', @sys, false)", connection);
            command.Parameters.AddWithValue("uid", userId?.ToString() ?? "");
            command.Parameters.AddWithValue("sys", system ? "on" : "off");
            await command.ExecuteNonQueryAsync(Ct);
        }

        return connection;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string where = "true")
    {
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM user_settings WHERE {where}", connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task<List<Guid>> OwnersSeenAsync(AppDbContext db) =>
        (await db.UserSettings.IgnoreQueryFilters().Select(s => s.UserId).ToListAsync(Ct)).Order().ToList();

    private async Task<List<Guid>> RawOwnersAsync(AppDbContext db) =>
        await db.Database.SqlQueryRaw<Guid>("SELECT user_id AS \"Value\" FROM user_settings").ToListAsync(Ct);

    // ---- Политики PostgreSQL (сырой SQL) ----

    [Fact]
    public async Task RawSql_UserSeesOnlyOwnRow()
    {
        await SeedAandBAsync();

        await using var asA = await OpenRawAsync(_a);
        (await CountAsync(asA)).ShouldBe(1);
        (await CountAsync(asA, $"user_id = '{_b}'")).ShouldBe(0);

        await using var asB = await OpenRawAsync(_b);
        (await CountAsync(asB)).ShouldBe(1);
        (await CountAsync(asB, $"user_id = '{_a}'")).ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawSql_WithoutUserContext_SeesNothing(bool contextSetToEmpty)
    {
        await SeedAandBAsync();

        await using var connection = await OpenRawAsync(null, setContext: contextSetToEmpty);

        (await CountAsync(connection)).ShouldBe(0);
    }

    [Fact]
    public async Task RawSql_SystemContext_SeesEverything()
    {
        await SeedAandBAsync();

        await using var connection = await OpenRawAsync(null, system: true);

        (await CountAsync(connection)).ShouldBe(2);
    }

    [Fact]
    public async Task RawSql_InsertForAnotherUser_IsRejectedByPolicy()
    {
        await SeedAandBAsync();
        var other = Guid.CreateVersion7();
        await SeedUserAsync(other, withSettings: false);

        await using var asA = await OpenRawAsync(_a);
        await using var insert = new NpgsqlCommand(
            "INSERT INTO user_settings (user_id, timezone, digest_frequency, digest_day, digest_channels) VALUES (@id, 'UTC', 'off', 'monday', ARRAY[]::text[])",
            asA);
        insert.Parameters.AddWithValue("id", other);

        var exception = await Should.ThrowAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(Ct));

        exception.SqlState.ShouldBe("42501");
        exception.MessageText.ShouldContain("row-level security");
    }

    [Fact]
    public async Task RawSql_UpdateAndDeleteOfAnotherUsersRow_AffectNothing()
    {
        await SeedAandBAsync();
        await using var asA = await OpenRawAsync(_a);

        await using var update = new NpgsqlCommand($"UPDATE user_settings SET timezone = 'Asia/Tokyo' WHERE user_id = '{_b}'", asA);
        await using var delete = new NpgsqlCommand($"DELETE FROM user_settings WHERE user_id = '{_b}'", asA);

        (await update.ExecuteNonQueryAsync(Ct)).ShouldBe(0);
        (await delete.ExecuteNonQueryAsync(Ct)).ShouldBe(0);
        await using var asSystem = await OpenRawAsync(null, system: true);
        (await CountAsync(asSystem, $"user_id = '{_b}' AND timezone = 'UTC'")).ShouldBe(1);
    }

    [Fact]
    public async Task RawSql_ReassigningOwnRowToAnotherUser_IsRejected()
    {
        await SeedAandBAsync();
        await using var asA = await OpenRawAsync(_a);
        await using var update = new NpgsqlCommand($"UPDATE user_settings SET user_id = '{_b}' WHERE user_id = '{_a}'", asA);

        var exception = await Should.ThrowAsync<PostgresException>(() => update.ExecuteNonQueryAsync(Ct));

        exception.SqlState.ShouldBe("42501");
    }

    [Fact]
    public async Task UserSettings_HasForcedRowLevelSecurity()
    {
        await using var connection = new NpgsqlConnection(Factory.Postgres.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'user_settings'", connection);

        ((bool)(await command.ExecuteScalarAsync(Ct))!).ShouldBeTrue();
    }

    // ---- EF: фильтр, перехватчик, пул соединений ----

    [Fact]
    public async Task Ef_Filter_ShowsOnlyCurrentUsersRows_AndRlsHoldsEvenWithoutFilter()
    {
        await SeedAandBAsync();
        await using var db = TestDb.Create(Factory.Postgres.AppConnectionString, TestCurrentUser.For(_a));

        (await db.UserSettings.Select(s => s.UserId).ToListAsync(Ct)).ShouldBe([_a]);
        (await OwnersSeenAsync(db)).ShouldBe([_a]);
        (await RawOwnersAsync(db)).ShouldBe([_a]);
    }

    [Fact]
    public async Task Ef_Filter_AloneRestrictsRows_WhenDatabaseContextIsPermissive()
    {
        await SeedAandBAsync();
        // PostgreSQL видит системный режим (RLS пропускает всё), фильтр EF знает пользователя A — только он и ограничивает выборку.
        await using var db = TestDb.Create(
            Factory.Postgres.AppConnectionString, TestCurrentUser.For(_a), connectionUser: TestCurrentUser.System());

        (await RawOwnersAsync(db)).Count.ShouldBe(2);
        (await db.UserSettings.Select(s => s.UserId).ToListAsync(Ct)).ShouldBe([_a]);
        (await OwnersSeenAsync(db)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Ef_SystemContext_SeesEveryone_AnonymousSeesNobody()
    {
        await SeedAandBAsync();
        var expected = new[] { _a, _b }.Order().ToList();

        await using var system = TestDb.Create(Factory.Postgres.AppConnectionString, TestCurrentUser.System());
        (await system.UserSettings.Select(s => s.UserId).ToListAsync(Ct)).Order().ToList().ShouldBe(expected);

        await using var anonymous = TestDb.Create(Factory.Postgres.AppConnectionString, TestCurrentUser.Anonymous());
        (await anonymous.UserSettings.ToListAsync(Ct)).ShouldBeEmpty();
        (await RawOwnersAsync(anonymous)).ShouldBeEmpty();
    }

    [Fact]
    public async Task PooledConnection_DoesNotLeakPreviousUsersContext()
    {
        await SeedAandBAsync();
        // Одно физическое соединение на всех и без сброса состояния при возврате в пул:
        // чужой контекст остался бы, если бы перехватчик не выставлял его заново при каждом открытии.
        var connectionString = new NpgsqlConnectionStringBuilder(Factory.Postgres.AppConnectionString)
        {
            MaxPoolSize = 1,
            NoResetOnClose = true,
        }.ConnectionString;

        await using (var asA = TestDb.Create(connectionString, TestCurrentUser.For(_a)))
        {
            (await RawOwnersAsync(asA)).ShouldBe([_a]);
        }

        await using (var anonymous = TestDb.Create(connectionString, TestCurrentUser.Anonymous()))
        {
            (await RawOwnersAsync(anonymous)).ShouldBeEmpty();
        }

        await using (var asB = TestDb.Create(connectionString, TestCurrentUser.For(_b)))
        {
            (await RawOwnersAsync(asB)).ShouldBe([_b]);
        }

        await using (var system = TestDb.Create(connectionString, TestCurrentUser.System()))
        {
            (await RawOwnersAsync(system)).Count.ShouldBe(2);
        }

        await using (var anonymousAfterSystem = TestDb.Create(connectionString, TestCurrentUser.Anonymous()))
        {
            (await RawOwnersAsync(anonymousAfterSystem)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Ef_NewUserOwnedRecord_GetsCurrentUserAsOwner()
    {
        var c = Guid.CreateVersion7();
        await SeedUserAsync(c, withSettings: false);
        await using var db = TestDb.Create(Factory.Postgres.AppConnectionString, TestCurrentUser.For(c));

        db.UserSettings.Add(new UserSettings { Timezone = "Asia/Tokyo" });
        await db.SaveChangesAsync(Ct);

        (await db.UserSettings.SingleAsync(Ct)).UserId.ShouldBe(c);
    }

    [Fact]
    public async Task Ef_NewUserOwnedRecordWithoutUser_Throws()
    {
        await using var db = TestDb.Create(Factory.Postgres.AppConnectionString, TestCurrentUser.Anonymous());
        db.UserSettings.Add(new UserSettings());

        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Ef_InsertForAnotherUser_IsRejectedByPolicy()
    {
        await SeedUserAsync(_a);
        var victim = Guid.CreateVersion7();
        await SeedUserAsync(victim, withSettings: false);
        await using var db = TestDb.Create(Factory.Postgres.AppConnectionString, TestCurrentUser.For(_a));

        db.UserSettings.Add(new UserSettings { UserId = victim });
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        ((PostgresException)exception.InnerException!).SqlState.ShouldBe("42501");
    }

    // ---- SystemContext в настоящем DI-контейнере приложения ----

    [Fact]
    public async Task SystemContext_SwitchesWhatTheScopedDbContextSees()
    {
        await SeedAandBAsync();
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var system = scope.ServiceProvider.GetRequiredService<SystemContext>();

        (await RawOwnersAsync(db)).ShouldBeEmpty();

        using (system.Enter())
        {
            (await RawOwnersAsync(db)).Count.ShouldBe(2);
            (await db.UserSettings.CountAsync(Ct)).ShouldBe(2);
        }

        using (system.RunAs(_b))
        {
            (await RawOwnersAsync(db)).ShouldBe([_b]);
            (await db.UserSettings.Select(s => s.UserId).ToListAsync(Ct)).ShouldBe([_b]);
        }

        (await RawOwnersAsync(db)).ShouldBeEmpty();
    }
}
