using System.Text.Json;
using KnowledgeHub.IntegrationTests.Infrastructure;
using KnowledgeHub.IntegrationTests.Items;
using Npgsql;

namespace KnowledgeHub.IntegrationTests.Identity;

/// <summary>
/// RLS на таблицах без собственной политики до этой правки: связи item_tags и collection_items (у них нет user_id —
/// владелец определяется через запись, тег и коллекцию) и очередь jobs (payload хранит исходные сообщения Telegram).
/// Проверки — сырым SQL ролью приложения, в обход фильтра EF.
/// </summary>
public sealed class RlsJoinTablesAndJobsTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Owned(Guid UserId, Guid Item, Guid Tag, Guid Collection);

    /// <summary>Пользователь с заметкой, у которой есть тег и коллекция, — через API, как в жизни.</summary>
    private async Task<Owned> SeedUserAsync()
    {
        var userId = Guid.CreateVersion7();
        using var user = await Factory.CreateClientForAsync(userId);
        var collection = Guid.Parse(await CollectionsTests.CreateCollectionIdAsync(user));
        var item = (await ItemEndpointsTests.CreateAsync(user, new { type = "note", title = "секрет", tags = new[] { "личное" }, collectionIds = new[] { collection } }))
            .GetProperty("id").GetGuid();

        // В ответе API теги — имена; id тега берём из связи (соединение postgres обходит RLS).
        await using var admin = new NpgsqlConnection(Factory.Postgres.AdminConnectionString);
        await admin.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT tag_id FROM item_tags WHERE item_id = @item", admin);
        command.Parameters.AddWithValue("item", item);
        var tag = (Guid)(await command.ExecuteScalarAsync(Ct))!;
        return new Owned(userId, item, tag, collection);
    }

    private async Task SeedJobAsync(Guid? userId)
    {
        await using var admin = new NpgsqlConnection(Factory.Postgres.AdminConnectionString);
        await admin.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO jobs (id, type, payload, status, attempts, run_after, user_id, dedup_key, created_at)
            VALUES (@id, 'telegram.update', '{"Json":"сообщение"}'::jsonb, 'pending', 0, now() + interval '30 days', @user_id, NULL, now())
            """,
            admin);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)userId ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>Соединение ролью приложения с контекстом, как его выставляет перехватчик; <c>null</c> — анонимный запрос.</summary>
    private async Task<NpgsqlConnection> OpenAsAsync(Guid? userId, bool system = false)
    {
        var connection = new NpgsqlConnection(Factory.Postgres.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT set_config('app.user_id', @uid, false), set_config('app.is_system', @sys, false)", connection);
        command.Parameters.AddWithValue("uid", userId?.ToString() ?? "");
        command.Parameters.AddWithValue("sys", system ? "on" : "off");
        await command.ExecuteNonQueryAsync(Ct);
        return connection;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Uuid) { Value = value ?? DBNull.Value });
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    [Fact]
    public async Task JoinTables_ShowOnlyRowsOfTheUsersOwnItems()
    {
        var a = await SeedUserAsync();
        var b = await SeedUserAsync();
        var mine = $"item_id IN ('{a.Item}', '{b.Item}')";

        await using (var asA = await OpenAsAsync(a.UserId))
        {
            (await CountAsync(asA, $"SELECT count(*) FROM item_tags WHERE {mine}")).ShouldBe(1);
            (await CountAsync(asA, $"SELECT count(*) FROM item_tags WHERE item_id = '{b.Item}'")).ShouldBe(0);
            (await CountAsync(asA, $"SELECT count(*) FROM collection_items WHERE {mine}")).ShouldBe(1);
            (await CountAsync(asA, $"SELECT count(*) FROM collection_items WHERE item_id = '{b.Item}'")).ShouldBe(0);
        }

        await using (var anonymous = await OpenAsAsync(null))
        {
            (await CountAsync(anonymous, $"SELECT count(*) FROM item_tags WHERE {mine}")).ShouldBe(0);
            (await CountAsync(anonymous, $"SELECT count(*) FROM collection_items WHERE {mine}")).ShouldBe(0);
        }

        await using (var system = await OpenAsAsync(null, system: true))
        {
            (await CountAsync(system, $"SELECT count(*) FROM item_tags WHERE {mine}")).ShouldBe(2);
            (await CountAsync(system, $"SELECT count(*) FROM collection_items WHERE {mine}")).ShouldBe(2);
        }
    }

    [Fact]
    public async Task OwnItem_CannotBeLinkedToSomeoneElsesTagOrCollection()
    {
        // Внешний ключ проверяется в обход RLS: без политики на связи чужой id тега прошёл бы проверку.
        var a = await SeedUserAsync();
        var b = await SeedUserAsync();
        await using var asA = await OpenAsAsync(a.UserId);

        var tagError = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(asA, "INSERT INTO item_tags (item_id, tag_id) VALUES (@item, @tag)", ("item", a.Item), ("tag", b.Tag)));
        var collectionError = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(asA, "INSERT INTO collection_items (collection_id, item_id) VALUES (@collection, @item)", ("collection", b.Collection), ("item", a.Item)));

        tagError.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        collectionError.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Jobs_AreVisibleOnlyToTheirUserAndTheSystem()
    {
        var a = await SeedUserAsync();
        var b = await SeedUserAsync();
        await SeedJobAsync(a.UserId);
        await SeedJobAsync(b.UserId);
        await SeedJobAsync(null);
        const string seeded = "SELECT count(*) FROM jobs WHERE payload->>'Json' = 'сообщение'";

        await using (var asA = await OpenAsAsync(a.UserId))
        {
            (await CountAsync(asA, seeded)).ShouldBe(1);
        }

        await using (var anonymous = await OpenAsAsync(null))
        {
            (await CountAsync(anonymous, seeded)).ShouldBe(0);
        }

        await using (var system = await OpenAsAsync(null, system: true))
        {
            (await CountAsync(system, seeded)).ShouldBe(3);
        }
    }

    [Fact]
    public async Task Jobs_UserCannotEnqueueForSomeoneElseOrAsSystem()
    {
        var a = await SeedUserAsync();
        var b = await SeedUserAsync();
        const string insert =
            "INSERT INTO jobs (id, type, payload, status, attempts, run_after, user_id, created_at) " +
            "VALUES (gen_random_uuid(), 'ingest', '{}'::jsonb, 'pending', 0, now(), @user_id, now())";

        await using (var asA = await OpenAsAsync(a.UserId))
        {
            await ExecuteAsync(asA, insert, ("user_id", a.UserId));
            (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(asA, insert, ("user_id", b.UserId))))
                .SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
            // Задача без пользователя выполняется воркером в системном контексте — пользователь её ставить не может.
            (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(asA, insert, ("user_id", null))))
                .SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }

        // Анонимный запрос задач не ставит вовсе: webhook Telegram, проверив секрет, ставит задачу в системном режиме.
        await using (var anonymous = await OpenAsAsync(null))
        {
            (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(anonymous, insert, ("user_id", null))))
                .SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
            (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(anonymous, insert, ("user_id", a.UserId))))
                .SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using var system = await OpenAsAsync(null, system: true);
        await ExecuteAsync(system, insert, ("user_id", null));
    }
}
