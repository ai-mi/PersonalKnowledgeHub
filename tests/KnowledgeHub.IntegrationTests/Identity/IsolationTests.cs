using System.Net;
using System.Net.Http.Json;
using KnowledgeHub.IntegrationTests.Infrastructure;

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
                (HttpMethod.Delete, $"/api/items/{id}", null),
            ]),
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
