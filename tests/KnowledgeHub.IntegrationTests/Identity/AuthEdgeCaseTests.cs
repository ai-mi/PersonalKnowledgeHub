using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace KnowledgeHub.IntegrationTests.Identity;

/// <summary>Пограничные случаи входа, сессии, antiforgery и настроек.</summary>
public sealed class AuthEdgeCaseTests : IntegrationTest, IDisposable
{
    private readonly WebApplicationFactory<Program> _devFactory;

    public AuthEdgeCaseTests(ApiFactory factory) : base(factory)
    {
        _devFactory = factory.WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Auth:EnableDevLogin", "true"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _devFactory.Dispose();

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static StringContent Settings(
        string timezone = "UTC", string frequency = "off", string day = "monday", string[]? channels = null, string? email = null) =>
        Json(JsonSerializer.Serialize(new
        {
            timezone,
            digestFrequency = frequency,
            digestDay = day,
            digestChannels = channels ?? [],
            email,
        }));

    /// <summary>Заголовок Cookie из Set-Cookie ответов (для клиента без обработки cookie, в т.ч. по http).</summary>
    private static string CookieHeader(params HttpResponseMessage[] responses) => string.Join("; ", responses
        .SelectMany(r => r.Headers.TryGetValues("Set-Cookie", out var v) ? v : [])
        .Select(c => c.Split(';')[0]));

    private static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    private async Task<int> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Factory.Postgres.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(Ct));
    }

    // ---- Работа по http (регрессия: antiforgery с SecurePolicy=Always падал с 500 на http://localhost) ----

    [Fact]
    public async Task OverPlainHttp_XsrfFlowWorks_AndTokenCookieIsNotSecure()
    {
        using var client = _devFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

        var login = await client.PostAsJsonAsync("/api/auth/dev-login", new { email = "http@example.com" }, Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        me.Headers.Add("Cookie", CookieHeader(login));
        var meResponse = await client.SendAsync(me, Ct);
        meResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var xsrf = SetCookie(meResponse, "XSRF-TOKEN");
        xsrf.ShouldNotBeNull();
        xsrf.ToLowerInvariant().ShouldNotContain("secure");
        var token = xsrf.Split(';')[0]["XSRF-TOKEN=".Length..];

        using var put = new HttpRequestMessage(HttpMethod.Put, "/api/me/settings") { Content = Settings() };
        put.Headers.Add("Cookie", CookieHeader(login, meResponse));
        put.Headers.Add("X-XSRF-TOKEN", token);
        (await client.SendAsync(put, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OverHttps_XsrfTokenCookieIsSecureAndReadableFromJs()
    {
        using var client = _devFactory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/dev-login", new { email = "https@example.com" }, Ct);

        var me = await client.GetAsync("/api/me", Ct);

        var cookie = SetCookie(me, "XSRF-TOKEN")!.ToLowerInvariant();
        cookie.ShouldContain("secure");
        cookie.ShouldNotContain("httponly");
        cookie.ShouldContain("samesite=lax");
    }

    // ---- Флаги сессионной cookie ----

    [Fact]
    public async Task SessionCookie_IsHttpOnlySecureLaxAndPersistentFor30Days()
    {
        using var client = _devFactory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/dev-login", new { email = "flags@example.com" }, Ct);

        var cookie = SetCookie(login, "kh.auth");
        cookie.ShouldNotBeNull();
        var lower = cookie.ToLowerInvariant();
        lower.ShouldContain("httponly");
        lower.ShouldContain("secure");
        lower.ShouldContain("samesite=lax");

        var expires = cookie.Split(';').Select(p => p.Trim()).Single(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        var date = DateTimeOffset.Parse(expires["expires=".Length..]);
        (date - DateTimeOffset.UtcNow).TotalDays.ShouldBeInRange(29.9, 30.1);
    }

    // ---- Callback ----

    [Theory]
    [InlineData("Facebook", "sub-1")]
    [InlineData("Google", null)]
    [InlineData("Google", "")]
    public async Task Callback_UnknownSchemeOrMissingSubject_RedirectsWithErrorAndCreatesNobody(string scheme, string? subject)
    {
        var cookie = ExternalLogin.CreateCookie(Factory, scheme, subject, "x@example.com");

        var response = await Client.SendAsync(ExternalLogin.CallbackRequest(cookie), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/login?error=external");
        SetCookie(response, "kh.auth").ShouldBeNull();
        (await ScalarAsync("SELECT count(*) FROM users")).ShouldBe(0);
    }

    [Fact]
    public async Task Callback_TicketWithoutIdentity_RedirectsWithErrorAndCreatesNobody()
    {
        var cookie = ExternalLogin.CreateCookieWithoutIdentity(Factory);

        var response = await Client.SendAsync(ExternalLogin.CallbackRequest(cookie), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/login?error=external");
        SetCookie(response, "kh.auth").ShouldBeNull();
        (await ScalarAsync("SELECT count(*) FROM users")).ShouldBe(0);
    }

    [Fact]
    public async Task Callback_VeryLongNameAndEmail_StillSignsIn()
    {
        var cookie = ExternalLogin.CreateCookie(Factory, "Google", "sub-long", new string('a', 400) + "@example.com", new string('N', 1000));

        var response = await Client.SendAsync(ExternalLogin.CallbackRequest(cookie), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var me = await Client.GetFromJsonAsync<JsonElement>("/api/me", Ct);
        me.GetProperty("displayName").GetString()!.Length.ShouldBe(User.MaxDisplayNameLength);
        me.GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Callback_ParallelFirstLoginsOfSameAccount_CreateOneUser()
    {
        var cookie = ExternalLogin.CreateCookie(Factory, "Google", "sub-race", "race@example.com", "Race");
        var clients = Enumerable.Range(0, 8).Select(_ => Factory.CreateClient()).ToList();

        try
        {
            var responses = await Task.WhenAll(clients.Select(c => c.SendAsync(ExternalLogin.CallbackRequest(cookie), Ct)));

            responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location!.OriginalString == "/");
            (await ScalarAsync("SELECT count(*) FROM users")).ShouldBe(1);
            (await ScalarAsync("SELECT count(*) FROM user_logins")).ShouldBe(1);
            (await ScalarAsync("SELECT count(*) FROM user_settings")).ShouldBe(1);
        }
        finally
        {
            clients.ForEach(c => c.Dispose());
        }
    }

    // ---- dev-login ----

    [Theory]
    [InlineData("""{"email":""}""")]
    [InlineData("""{"email":"   "}""")]
    [InlineData("""{"email":"not-an-email"}""")]
    [InlineData("""{"email":null}""")]
    [InlineData("""{}""")]
    public async Task DevLogin_InvalidEmail_Returns400AndCreatesNobody(string body)
    {
        using var client = _devFactory.CreateClient();

        var response = await client.PostAsync("/api/auth/dev-login", Json(body), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ScalarAsync("SELECT count(*) FROM users")).ShouldBe(0);
    }

    [Fact]
    public async Task DevLogin_SameEmailInDifferentCase_IsSameUser()
    {
        using var first = _devFactory.CreateClient();
        using var second = _devFactory.CreateClient();

        await first.PostAsJsonAsync("/api/auth/dev-login", new { email = "Case@Example.com" }, Ct);
        await second.PostAsJsonAsync("/api/auth/dev-login", new { email = "case@example.com" }, Ct);

        (await ScalarAsync("SELECT count(*) FROM users")).ShouldBe(1);
    }

    // ---- Сессия ----

    [Fact]
    public async Task Me_WithValidCookieButDeletedUser_Returns401()
    {
        using var client = _devFactory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/dev-login", new { email = "gone@example.com" }, Ct);
        (await client.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ScalarAsync("WITH d AS (DELETE FROM users RETURNING 1) SELECT count(*) FROM d");

        (await client.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithInvalidSessionCookie_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("Cookie", "kh.auth=garbage");

        (await Client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Antiforgery на других методах ----

    [Fact]
    public async Task Logout_WithCookieSessionWithoutXsrf_Returns400_AndSessionSurvives()
    {
        using var client = _devFactory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/dev-login", new { email = "csrf@example.com" }, Ct);
        await client.GetAsync("/api/me", Ct);

        (await client.PostAsync("/api/auth/logout", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_WithoutSession_IsHarmlessAndDoesNotRequireXsrf()
    {
        (await Client.PostAsync("/api/auth/logout", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ---- Настройки: границы ----

    [Fact]
    public async Task Settings_PersistedFormat_IsLowercaseStrings()
    {
        using var client = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        (await client.PutAsync("/api/me/settings", Settings(frequency: "daily", day: "sunday"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ScalarAsync("SELECT count(*) FROM user_settings WHERE digest_frequency = 'daily' AND digest_day = 'sunday'")).ShouldBe(1);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"timezone":"UTC"}""")]
    [InlineData("""not json""")]
    [InlineData("""{"timezone":"UTC","digestFrequency":"hourly","digestDay":"monday","digestChannels":[]}""")]
    [InlineData("""{"timezone":"UTC","digestFrequency":"off","digestDay":"someday","digestChannels":[]}""")]
    [InlineData("""{"timezone":"UTC","digestFrequency":"off","digestDay":"monday","digestChannels":null}""")]
    [InlineData("""{"timezone":"UTC","digestFrequency":"off","digestDay":"monday","digestChannels":[null]}""")]
    [InlineData("""{"timezone":"UTC","digestFrequency":"off","digestDay":"monday","digestChannels":["Email"]}""")]
    [InlineData("""{"timezone":null,"digestFrequency":"off","digestDay":"monday","digestChannels":[]}""")]
    [InlineData("""{"timezone":"UTC","digestFrequency":"off","digestDay":"monday","digestChannels":[],"email":"not-an-email"}""")]
    public async Task Settings_MalformedOrInvalidBody_Returns400(string body)
    {
        using var client = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await client.PutAsync("/api/me/settings", Json(body), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Settings_EmailOverMaxLength_Returns400_AtMaxLengthIsAccepted()
    {
        using var client = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var atLimit = new string('a', User.MaxEmailLength - "@x.io".Length) + "@x.io";
        var overLimit = new string('a', User.MaxEmailLength - "@x.io".Length + 1) + "@x.io";

        (await client.PutAsync("/api/me/settings", Settings(email: overLimit), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PutAsync("/api/me/settings", Settings(email: atLimit), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Settings_BlankEmail_IsStoredAsNull_AndEmailIsTrimmed()
    {
        using var client = await Factory.CreateClientForAsync(Guid.CreateVersion7(), "kept@example.com");

        var blank = await client.PutAsync("/api/me/settings", Settings(email: "   "), Ct);
        (await blank.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null);

        var trimmed = await client.PutAsync("/api/me/settings", Settings(email: "  me@example.com "), Ct);
        (await trimmed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("email").GetString().ShouldBe("me@example.com");
    }

    [Fact]
    public async Task Settings_DuplicateChannels_AreCollapsed()
    {
        using var client = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await client.PutAsync("/api/me/settings", Settings(channels: ["email", "email", "telegram", "email"]), Ct);

        var channels = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("digestChannels");
        channels.EnumerateArray().Select(e => e.GetString()).ShouldBe(["email", "telegram"]);
    }

    [Fact]
    public async Task Settings_OfOneUser_AreNotVisibleToAnother()
    {
        using var alice = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        using var bob = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        await alice.PutAsync("/api/me/settings", Settings(timezone: "Asia/Tokyo"), Ct);

        var bobMe = await bob.GetFromJsonAsync<JsonElement>("/api/me", Ct);
        bobMe.GetProperty("settings").GetProperty("timezone").GetString().ShouldBe("UTC");
    }
}
