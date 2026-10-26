using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace KnowledgeHub.IntegrationTests.Items;

public sealed class ItemCreateRaceTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task LinkSavedByCompetingRequestAfterTheDuplicateCheck_ReturnsTheWinner()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.CreateVersion7();
        using var owner = await Factory.CreateClientForAsync(userId);
        var competitor = new CompetingInsertInterceptor(Factory.Postgres.AdminConnectionString, userId, "https://example.com/race");

        // Проверка дубликата уже пройдена, а перед самой вставкой «соседний запрос» успевает сохранить ту же ссылку.
        using var racing = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(competitor))));
        using var client = racing.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, userId.ToString());

        var response = await client.PostAsJsonAsync("/api/items", new { type = "link", url = "https://example.com/race", title = "loser" }, ct);

        competitor.Fired.ShouldBeTrue();
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var item = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        item.GetProperty("title").GetString().ShouldBe("winner");
        (await owner.GetFromJsonAsync<JsonElement>("/api/items", ct)).GetProperty("total").GetInt32().ShouldBe(1);
    }

    private sealed class CompetingInsertInterceptor(string connectionString, Guid userId, string url) : SaveChangesInterceptor
    {
        private int _fired;

        public bool Fired => _fired == 1;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var adding = eventData.Context!.ChangeTracker.Entries<Item>().Any(e => e.State == EntityState.Added);
            if (adding && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                // Админ-соединение: роль приложения подчиняется RLS, а здесь нужна запись «от лица другого запроса».
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand(
                    """
                    INSERT INTO items (id, user_id, type, title, url, status, created_at, updated_at)
                    VALUES (@id, @user, 'link', 'winner', @url, 'ready', now(), now())
                    """,
                    connection);
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("user", userId);
                command.Parameters.AddWithValue("url", url);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
