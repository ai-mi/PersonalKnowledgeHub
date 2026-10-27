using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KnowledgeHub.IntegrationTests.Infrastructure;

public class RateLimitingTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Пользователь создаётся через основную фабрику, а запросы идут в хост с переопределёнными лимитами (та же БД).
    private async Task<HttpClient> ClientForNewUserAsync(WebApplicationFactory<Program> limited)
    {
        var userId = Guid.CreateVersion7();
        using var _ = await Factory.CreateClientForAsync(userId);
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, userId.ToString());
        return client;
    }

    [Fact]
    public async Task Login_ElevenRequestsPerMinute_ReturnsTooManyRequests()
    {
        // Рабочее значение из плана: 10 запросов в минуту на IP для /api/auth/*.
        await using var factory = Factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPerMinute", "10"));
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            // Неизвестный провайдер даёт 404, но запрос уже учтён лимитом.
            var allowed = await client.GetAsync("/api/auth/login/unknown", Ct);
            allowed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var rejected = await client.GetAsync("/api/auth/login/unknown", Ct);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        rejected.Headers.RetryAfter.Delta.ShouldNotBeNull();
        rejected.Headers.RetryAfter.Delta.Value.ShouldBeGreaterThan(TimeSpan.Zero);
        rejected.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Auth_LimitIsIndependentFromDefaultLimit()
    {
        // Строгий лимит входа не должен действовать на остальной API, а общий — на вход.
        await using var factory = Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthPerMinute", "1");
            b.UseSetting("RateLimiting:DefaultPerMinute", "100000");
        });
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/auth/login/unknown", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/auth/login/unknown", Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Любой другой маршрут группы /api (здесь — 401 без входа) по-прежнему доступен.
        (await client.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UsersDoNotShareDefaultLimit()
    {
        await using var factory = Factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:DefaultPerMinute", "3"));
        using var userA = await ClientForNewUserAsync(factory);
        using var userB = await ClientForNewUserAsync(factory);

        for (var i = 0; i < 3; i++)
        {
            (await userA.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await userA.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Тот же IP, но другой пользователь — своя партиция.
        (await userB.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webhook_IsLimitedPerIp()
    {
        await using var factory = Factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:WebhookPerMinute", "2"));
        using var client = factory.CreateClient();

        // Без секретного заголовка webhook отвечает 401 — этого достаточно, чтобы запрос учёлся.
        (await client.PostAsync("/telegram/webhook", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/telegram/webhook", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/telegram/webhook", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Upload_HasOwnLimit()
    {
        await using var factory = Factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:UploadPerMinute", "1"));
        using var client = await ClientForNewUserAsync(factory);

        // Не multipart → 400 от валидации, но запрос уже учтён лимитом загрузки; общий лимит при этом высокий.
        (await client.PostAsync("/api/items/upload", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsync("/api/items/upload", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await client.GetAsync("/api/items", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
