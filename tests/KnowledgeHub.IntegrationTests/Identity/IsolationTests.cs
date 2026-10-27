using System.Net;
using System.Net.Http.Json;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.IntegrationTests.Identity;

/// <summary>
/// Одна запись описывает ресурс пользователя: как создать его у A и какие запросы к нему может сделать B.
/// Ожидание везде одно: пользователь B получает <c>404</c> (не <c>403</c>), чтобы не раскрывать существование ресурса.
/// </summary>
/// <param name="Name">Короткое имя для отчёта о тестах.</param>
/// <param name="CreateAsA">Создаёт ресурс от имени A и возвращает его идентификатор (для подстановки в запросы).</param>
/// <param name="Requests">Запросы к ресурсу по его идентификатору: метод, URL и необязательное JSON-тело.</param>
/// <param name="IsPlaceholder">Заглушка без настоящего ресурса: проверка «владелец получает не 404» пропускается.</param>
public sealed record IsolationCase(
    string Name,
    Func<HttpClient, Task<string>> CreateAsA,
    Func<string, IReadOnlyList<(HttpMethod Method, string Url, object? Body)>> Requests,
    bool IsPlaceholder = false);

public static class IsolationCases
{
    /// <summary>
    /// КАК РАСШИРЯТЬ: в каждой фиче, добавляющей пользовательский ресурс, допишите сюда IsolationCase по образцу ниже.
    /// Запросы с изменением ресурса (DELETE) ставьте последними: владелец выполняет их раньше пользователя B.
    /// </summary>
    public static readonly IReadOnlyList<IsolationCase> All =
    [
        new("ApiTokens",
            async a => (await ApiTokenTests.CreateTokenAsync(a)).Id,
            id => [(HttpMethod.Delete, $"/api/me/tokens/{id}", null)]),
        new("Items",
            async a => await Items.ItemEndpointsTests.CreateNoteIdAsync(a),
            id =>
            [
                (HttpMethod.Get, $"/api/items/{id}", null),
                (HttpMethod.Put, $"/api/items/{id}", new { title = "y" }),
                (HttpMethod.Patch, $"/api/items/{id}", new { read = true }),
                (HttpMethod.Post, $"/api/items/{id}/reprocess", null),
                (HttpMethod.Delete, $"/api/items/{id}", null),
            ]),
        // Файл документа — отдельный маршрут: чужой документ и его скачивание должны быть недоступны так же, как сама запись.
        new("DocumentFiles",
            async a => await Ingestion.DocumentUploadTests.UploadIdAsync(a, "isolation"u8.ToArray(), "secret.txt"),
            id => [(HttpMethod.Get, $"/api/items/{id}/file", null)]),
        new("Tags",
            async a =>
            {
                await Items.ItemEndpointsTests.CreateAsync(a, new { type = "note", tags = new[] { "secret" } });
                return await Items.TagsTests.TagIdAsync(a, "secret");
            },
            id =>
            [
                (HttpMethod.Put, $"/api/tags/{id}", new { name = "renamed" }),
                (HttpMethod.Delete, $"/api/tags/{id}", null),
            ]),
        new("Collections",
            async a => await Items.CollectionsTests.CreateCollectionIdAsync(a),
            id =>
            [
                (HttpMethod.Put, $"/api/collections/{id}", new { name = "renamed" }),
                (HttpMethod.Delete, $"/api/collections/{id}", null),
            ]),
        // Идентификатор здесь составной — «коллекция/items/запись»: так один случай проверяет оба конца связи.
        new("CollectionItems",
            async a =>
            {
                var collection = await Items.CollectionsTests.CreateCollectionIdAsync(a);
                return $"{collection}/items/{await Items.ItemEndpointsTests.CreateNoteIdAsync(a)}";
            },
            id =>
            [
                (HttpMethod.Post, $"/api/collections/{id}", null),
                (HttpMethod.Delete, $"/api/collections/{id}", null),
            ]),
        new("Reminders",
            async a => await Reminders.ReminderEndpointsTests.CreateIdAsync(a, await Items.ItemEndpointsTests.CreateNoteIdAsync(a)),
            id => [(HttpMethod.Delete, $"/api/reminders/{id}", null)]),
        // Идентификатор — запись A: B не может ни поставить на неё напоминание, ни прочитать её напоминания.
        new("ItemReminders",
            async a => await Items.ItemEndpointsTests.CreateNoteIdAsync(a),
            id =>
            [
                (HttpMethod.Get, $"/api/items/{id}/reminders", null),
                (HttpMethod.Post, "/api/reminders", new { itemId = id, remindAt = DateTimeOffset.UtcNow.AddHours(1), channel = "telegram" }),
            ]),
        // Архив экспорта A недоступен B: скачивание по чужому id — 404. У владельца архив ещё собирается, поэтому он получает 409, а не 404.
        new("Exports",
            async a => await Export.ExportTests.StartAsync(a),
            id => [(HttpMethod.Get, $"/api/exports/{id}/download", null)]),
    ];

    public static IsolationCase Get(string name) => All.Single(c => c.Name == name);
}

public sealed class IsolationTests(ApiFactory factory) : IntegrationTest(factory)
{
    public static IEnumerable<TheoryDataRow<string>> CaseNames() => IsolationCases.All.Select(c => new TheoryDataRow<string>(c.Name));

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task UserB_GetsNotFoundForResourcesOfUserA(string caseName)
    {
        var isolation = IsolationCases.Get(caseName);
        using var a = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        using var b = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var id = await isolation.CreateAsA(a);

        foreach (var (method, url, body) in isolation.Requests(id))
        {
            // Владелец ресурс видит: без этой проверки 404 мог бы означать просто «такого маршрута нет».
            if (!isolation.IsPlaceholder)
            {
                (await Send(a, method, url, body)).StatusCode.ShouldNotBe(HttpStatusCode.NotFound, $"владелец: {method} {url}");
            }

            (await Send(b, method, url, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound, $"пользователь B: {method} {url}");
        }
    }

    [Fact]
    public async Task Settings_AreAlwaysThoseOfTheCaller()
    {
        // Настоящая проверка для единственного пока пользовательского ресурса: нет адреса, по которому B мог бы дотянуться до настроек A.
        using var a = await Factory.CreateClientForAsync(Guid.CreateVersion7(), "a@example.com");
        using var b = await Factory.CreateClientForAsync(Guid.CreateVersion7(), "b@example.com");

        var ct = TestContext.Current.CancellationToken;
        var meA = await a.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me", ct);
        var meB = await b.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me", ct);

        meA.GetProperty("settings").GetProperty("email").GetString().ShouldBe("a@example.com");
        meB.GetProperty("settings").GetProperty("email").GetString().ShouldBe("b@example.com");
    }

    [Fact]
    public async Task TelegramLink_IsAlwaysThatOfTheCaller()
    {
        // Как и настройки, привязка Telegram адресуется только «собой» (/api/me/telegram): чужую по id не достать.
        var aId = Guid.CreateVersion7();
        using var a = await Factory.CreateClientForAsync(aId);
        using var b = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TelegramLinks.Add(new TelegramLink { UserId = aId, ChatId = 1, LinkedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var ct = TestContext.Current.CancellationToken;
        (await a.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me/telegram", ct)).GetProperty("linked").GetBoolean().ShouldBeTrue();
        (await b.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me/telegram", ct)).GetProperty("linked").GetBoolean().ShouldBeFalse();

        // Отвязка у B не задевает привязку A.
        (await b.DeleteAsync("/api/me/telegram/link", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await a.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me/telegram", ct)).GetProperty("linked").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Search_NeverReturnsItemsOfOtherUsers()
    {
        // Поиск не обращается к ресурсу по id, поэтому в общую таблицу случаев не вписывается: проверяем выдачу.
        using var a = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        using var b = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        await Items.ItemEndpointsTests.CreateAsync(a, new { type = "note", title = "zebra", body = "zebra", tags = new[] { "zebra" } });

        (await Search.SearchTests.SearchAsync(a, "zebra")).GetProperty("total").GetInt32().ShouldBe(1);
        (await Search.SearchTests.SearchAsync(b, "zebra")).GetProperty("total").GetInt32().ShouldBe(0);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
