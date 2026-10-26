using System.Net;
using System.Net.Http.Json;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KnowledgeHub.IntegrationTests.Identity;

public sealed class AntiforgeryTests : IntegrationTest, IDisposable
{
    private static readonly object ValidSettings = new
    {
        timezone = "UTC",
        digestFrequency = "off",
        digestDay = "monday",
        digestChannels = Array.Empty<string>(),
    };

    private readonly WebApplicationFactory<Program> _devFactory;
    private readonly HttpClient _cookieClient;

    public AntiforgeryTests(ApiFactory factory) : base(factory)
    {
        _devFactory = factory.WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Auth:EnableDevLogin", "true"));
        _cookieClient = _devFactory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Читает токен из cookie <c>XSRF-TOKEN</c>, которую SPA получает на обычном GET.</summary>
    public static async Task<string> FetchXsrfTokenAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/me", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var setCookie = response.Headers.GetValues("Set-Cookie").SingleOrDefault(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        setCookie.ShouldNotBeNull("GET с cookie-сессией должен выдавать XSRF-TOKEN");
        return setCookie.Split(';')[0]["XSRF-TOKEN=".Length..];
    }

    private async Task LoginAsync()
    {
        var response = await _cookieClient.PostAsJsonAsync("/api/auth/dev-login", new { email = "dev@example.com" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UnsafeRequestWithCookieSession_WithoutXsrfToken_Returns400()
    {
        await LoginAsync();

        var response = await _cookieClient.PutAsJsonAsync("/api/me/settings", ValidSettings, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnsafeRequestWithCookieSession_WithWrongXsrfToken_Returns400()
    {
        await LoginAsync();
        await FetchXsrfTokenAsync(_cookieClient, Ct);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/me/settings") { Content = JsonContent.Create(ValidSettings) };
        request.Headers.Add("X-XSRF-TOKEN", "forged");

        (await _cookieClient.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnsafeRequestWithCookieSession_WithXsrfToken_Succeeds()
    {
        await LoginAsync();
        var token = await FetchXsrfTokenAsync(_cookieClient, Ct);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/me/settings") { Content = JsonContent.Create(ValidSettings) };
        request.Headers.Add("X-XSRF-TOKEN", token);

        (await _cookieClient.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task XsrfTokenIssuedBeforeLogin_IsNotAcceptedForAnotherSession()
    {
        await LoginAsync();
        var token = await FetchXsrfTokenAsync(_cookieClient, Ct);

        // Другой пользователь с тем же токеном в заголовке: токен привязан к своей сессии.
        using var other = _devFactory.CreateClient();
        await other.PostAsJsonAsync("/api/auth/dev-login", new { email = "other@example.com" }, Ct);
        await FetchXsrfTokenAsync(other, Ct);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/me/settings") { Content = JsonContent.Create(ValidSettings) };
        request.Headers.Add("X-XSRF-TOKEN", token);

        (await other.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnsafeRequestWithoutCookieScheme_DoesNotRequireXsrfToken()
    {
        using var client = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await client.PutAsJsonAsync("/api/me/settings", ValidSettings, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnonymousGet_DoesNotIssueXsrfCookie()
    {
        var response = await Client.GetAsync("/api/me", Ct);

        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).ShouldNotContain(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _cookieClient.Dispose();
        _devFactory.Dispose();
    }
}
