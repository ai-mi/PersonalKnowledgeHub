using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Email;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Respawn;

namespace KnowledgeHub.IntegrationTests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // Первый запуск миграций на пустой БД не защищён блокировкой (таблицы истории ещё нет),
    // поэтому параллельный старт нескольких хостов на одной БД запускаем по очереди.
    private static readonly Lock HostStartLock = new();

    public const string BotWebhookSecret = "test-webhook-secret";

    private readonly PostgresContainer _postgres;
    private readonly AzuriteContainer _azurite;
    private Respawner? _respawner;

    public ApiFactory(PostgresContainer postgres, AzuriteContainer azurite)
    {
        _postgres = postgres;
        _azurite = azurite;

        // Cookie приложения помечены Secure — по http они не отправляются, поэтому клиенты ходят по https.
        // Редиректы не следуем автоматически: тесты проверяют сами ответы 302.
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    public PostgresContainer Postgres => _postgres;

    /// <summary>Один фейк на все хосты фабрики (включая производные): тест видит ответы бота, не зная, какой хост их отправил.</summary>
    public FakeTelegramClient Telegram { get; } = new();

    public FakeEmailSender Email { get; } = new();

    public string AzuriteConnectionString => _azurite.ConnectionString;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (HostStartLock)
        {
            return base.CreateHost(builder);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", _postgres.AppConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("Storage:ConnectionString", _azurite.ConnectionString);
        // Тесты Ingestion ходят в WireMock на localhost.
        builder.UseSetting("Ingestion:AllowLoopbackForTests", "true");
        builder.UseSetting("Bot:WebhookSecret", BotWebhookSecret);
        builder.UseSetting("Bot:Username", "kh_test_bot");
        // Лимиты не должны мешать обычным тестам; RateLimitingTests возвращает рабочие значения через WithWebHostBuilder.
        foreach (var limit in new[] { "Default", "Auth", "Webhook", "Upload" })
        {
            builder.UseSetting($"RateLimiting:{limit}PerMinute", "100000");
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ITelegramClient>(Telegram);
            services.AddSingleton<IEmailSender>(Email);

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Заголовок X-Test-User направляет запрос в схему Test; остальные запросы идут обычным путём.
            services.PostConfigure<PolicySchemeOptions>(AuthSchemes.Smart, o =>
            {
                var inner = o.ForwardDefaultSelector!;
                o.ForwardDefaultSelector = context =>
                    context.Request.Headers.ContainsKey(TestAuthHandler.HeaderName) ? TestAuthHandler.SchemeName : inner(context);
            });
        });
    }

    /// <summary>Создаёт пользователя в БД и возвращает клиента, аутентифицированного как он (заголовок X-Test-User).</summary>
    public async Task<HttpClient> CreateClientForAsync(Guid userId, string? email = null)
    {
        email ??= $"{userId:N}@test.local";

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // user_settings защищена RLS, а «пользователя» запроса при подготовке данных ещё нет.
            using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
            db.Users.Add(new User
            {
                Id = userId,
                Email = email,
                DisplayName = $"Test {userId:N}",
                CreatedAt = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(),
            });
            db.UserSettings.Add(UserSettings.CreateDefault(userId, email));
            await db.SaveChangesAsync();
        }

        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, userId.ToString());
        return client;
    }

    /// <summary>Сбрасывает данные соединением postgres: роль kh_app подчиняется RLS и не увидела бы чужие строки.</summary>
    public async Task ResetDatabaseAsync()
    {
        // Принудительно запускаем хост, чтобы миграции уже были применены.
        _ = Server;

        await using var connection = new NpgsqlConnection(_postgres.AdminConnectionString);
        await connection.OpenAsync();

        // Пока в схеме нет пользовательских таблиц, Respawn создать нельзя — сбрасывать нечего.
        if (_respawner is null && !await HasUserTablesAsync(connection))
        {
            return;
        }

        _respawner ??= await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")],
        });

        await _respawner.ResetAsync(connection);
    }

    private static async Task<bool> HasUserTablesAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory')
            """,
            connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
