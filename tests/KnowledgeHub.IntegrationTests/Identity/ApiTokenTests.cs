using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.IntegrationTests.Infrastructure;

namespace KnowledgeHub.IntegrationTests.Identity;

public sealed class ApiTokenTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static readonly object ValidSettings = new
    {
        timezone = "UTC",
        digestFrequency = "off",
        digestDay = "monday",
        digestChannels = Array.Empty<string>(),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Создаёт токен от имени пользователя и возвращает (id, значение).</summary>
    public static async Task<(string Id, string Token)> CreateTokenAsync(HttpClient client, object? body = null)
    {
        var response = await client.PostAsJsonAsync("/api/me/tokens", body ?? new { name = "script" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (json.GetProperty("id").GetString()!, json.GetProperty("token").GetString()!);
    }

    private HttpClient BearerClient(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task CreatedToken_AuthenticatesApiRequests()
    {
        var userId = Guid.CreateVersion7();
        using var user = await Factory.CreateClientForAsync(userId, "owner@example.com");
        var (_, token) = await CreateTokenAsync(user);
        token.ShouldStartWith("kh_");

        using var bearer = BearerClient(token);
        var response = await bearer.GetAsync("/api/me", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid().ShouldBe(userId);
    }

    [Fact]
    public async Task List_DoesNotExposeTokenValue_AndShowsLastUse()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var (id, token) = await CreateTokenAsync(user);
        using (var bearer = BearerClient(token))
        {
            (await bearer.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var raw = await user.GetStringAsync("/api/me/tokens", Ct);
        raw.ShouldNotContain(token);

        var item = JsonDocument.Parse(raw).RootElement.EnumerateArray().Single();
        item.GetProperty("id").GetString().ShouldBe(id);
        item.GetProperty("name").GetString().ShouldBe("script");
        item.GetProperty("lastUsedAt").ValueKind.ShouldBe(JsonValueKind.String);
        item.TryGetProperty("token", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task UnknownToken_Returns401()
    {
        using var bearer = BearerClient("kh_unknown");

        (await bearer.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokedToken_Returns401()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var (id, token) = await CreateTokenAsync(user);
        using var bearer = BearerClient(token);
        (await bearer.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await user.DeleteAsync($"/api/me/tokens/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await bearer.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var (id, token) = await CreateTokenAsync(user, new { name = "short", expiresInDays = 1 });
        using var bearer = BearerClient(token);
        (await bearer.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ExpireAsync(Guid.Parse(id));

        (await bearer.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnsafeRequestWithToken_DoesNotRequireXsrfToken()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var (_, token) = await CreateTokenAsync(user);
        using var bearer = BearerClient(token);

        var response = await bearer.PutAsJsonAsync("/api/me/settings", ValidSettings, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OtherUsersToken_CannotBeRevoked()
    {
        using var a = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        using var b = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var (id, token) = await CreateTokenAsync(a);

        (await b.DeleteAsync($"/api/me/tokens/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var bearer = BearerClient(token);
        (await bearer.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(await b.GetStringAsync("/api/me/tokens", Ct)).RootElement.GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutName_Returns400(string name)
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await user.PostAsJsonAsync("/api/me/tokens", new { name }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithInvalidExpiry_Returns400()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await user.PostAsJsonAsync("/api/me/tokens", new { name = "x", expiresInDays = 0 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        (await Client.GetAsync("/api/me/tokens", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.PostAsJsonAsync("/api/me/tokens", new { name = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Сдвигает срок действия в прошлое прямо в БД (соединением postgres: kh_app подчиняется RLS).</summary>
    private async Task ExpireAsync(Guid tokenId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Factory.Postgres.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new Npgsql.NpgsqlCommand(
            "UPDATE api_tokens SET expires_at = now() - interval '1 minute' WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", tokenId);
        (await command.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
    }
}
