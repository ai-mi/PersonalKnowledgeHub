using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.IntegrationTests.Identity;

public sealed class AuthTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Me_WithoutLogin_Returns401()
    {
        var response = await Client.GetAsync("/api/me", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithTestUser_ReturnsProfileWithDefaultSettings()
    {
        var userId = Guid.CreateVersion7();
        using var client = await Factory.CreateClientForAsync(userId, "alice@example.com");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me", Ct);

        me.GetProperty("id").GetGuid().ShouldBe(userId);
        me.GetProperty("email").GetString().ShouldBe("alice@example.com");
        var settings = me.GetProperty("settings");
        settings.GetProperty("timezone").GetString().ShouldBe("UTC");
        settings.GetProperty("digestFrequency").GetString().ShouldBe("off");
        settings.GetProperty("digestDay").GetString().ShouldBe("monday");
        settings.GetProperty("digestChannels").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Callback_FirstLogin_CreatesUserAndSignsIn()
    {
        var cookie = ExternalLogin.CreateCookie(Factory, "Google", "sub-1", "bob@example.com", "Bob");

        var callback = await Client.SendAsync(ExternalLogin.CallbackRequest(cookie, "/items"), Ct);

        callback.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.ShouldBe("/items");

        var me = await Client.GetFromJsonAsync<JsonElement>("/api/me", Ct);
        me.GetProperty("email").GetString().ShouldBe("bob@example.com");
        me.GetProperty("displayName").GetString().ShouldBe("Bob");
        me.GetProperty("settings").GetProperty("email").GetString().ShouldBe("bob@example.com");
    }

    [Fact]
    public async Task Callback_SameProviderAndSubject_DoesNotCreateSecondUser()
    {
        using var first = Factory.CreateClient();
        using var second = Factory.CreateClient();
        var cookie = ExternalLogin.CreateCookie(Factory, "Microsoft", "sub-1", "bob@example.com", "Bob");

        (await first.SendAsync(ExternalLogin.CallbackRequest(cookie), Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await second.SendAsync(ExternalLogin.CallbackRequest(cookie), Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var firstId = (await first.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();
        var secondId = (await second.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();
        secondId.ShouldBe(firstId);
        (await CountUsersAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Callback_SameSubjectFromDifferentProviders_AreDifferentUsers()
    {
        var google = ExternalLogin.CreateCookie(Factory, "Google", "sub-1");
        var microsoft = ExternalLogin.CreateCookie(Factory, "Microsoft", "sub-1");

        using var a = Factory.CreateClient();
        using var b = Factory.CreateClient();
        await a.SendAsync(ExternalLogin.CallbackRequest(google), Ct);
        await b.SendAsync(ExternalLogin.CallbackRequest(microsoft), Ct);

        (await CountUsersAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData("https://evil.com", "/")]
    [InlineData("//evil.com", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("/items/42?tab=notes", "/items/42?tab=notes")]
    public async Task Callback_ReturnUrl_IsRestrictedToLocalPaths(string returnUrl, string expected)
    {
        var cookie = ExternalLogin.CreateCookie(Factory, "Google", "sub-1");

        var response = await Client.SendAsync(ExternalLogin.CallbackRequest(cookie, returnUrl), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(expected);
    }

    [Fact]
    public async Task Callback_WithoutExternalCookie_RedirectsToLoginWithError()
    {
        var response = await Client.GetAsync("/api/auth/callback", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/login?error=external");
        (await Client.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("google")]
    [InlineData("microsoft")]
    [InlineData("unknown")]
    public async Task Login_ProviderNotConfigured_Returns404(string provider)
    {
        var response = await Client.GetAsync($"/api/auth/login/{provider}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("google", "accounts.google.com", "/signin-google")]
    [InlineData("microsoft", "login.microsoftonline.com", "/signin-microsoft")]
    public async Task Login_ConfiguredProvider_RedirectsToProvider(string provider, string host, string callbackPath)
    {
        await using var configured = Factory.WithWebHostBuilder(b => b
            .UseSetting($"Authentication:{provider}:ClientId", "client-id")
            .UseSetting($"Authentication:{provider}:ClientSecret", "client-secret"));
        using var client = configured.CreateClient();

        var response = await client.GetAsync($"/api/auth/login/{provider}?returnUrl=%2Fitems", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.Host.ShouldBe(host);
        Uri.UnescapeDataString(location.Query).ShouldContain($"redirect_uri=https://localhost{callbackPath}");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task DevLogin_IsAvailableOnlyInDevelopmentWithFlag(string environment)
    {
        var enabled = environment == "Development";
        await using var configured = Factory.WithWebHostBuilder(b => b
            .UseEnvironment(environment)
            .UseSetting("Auth:EnableDevLogin", "true"));
        using var client = configured.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/dev-login", new { email = "dev@example.com" }, Ct);

        if (enabled)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        else
        {
            response.IsSuccessStatusCode.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task DevLogin_InDevelopmentWithoutFlag_IsNotAvailable()
    {
        await using var configured = Factory.WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Auth:EnableDevLogin", "false"));
        using var client = configured.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/dev-login", new { email = "dev@example.com" }, Ct);

        response.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateSettings_SavesValidSettings()
    {
        using var client = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await client.PutAsJsonAsync("/api/me/settings", new
        {
            timezone = "Europe/Moscow",
            digestFrequency = "weekly",
            digestDay = "friday",
            digestChannels = new[] { "telegram", "email", "email" },
            email = "digest@example.com",
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me", Ct);
        var settings = me.GetProperty("settings");
        settings.GetProperty("timezone").GetString().ShouldBe("Europe/Moscow");
        settings.GetProperty("digestFrequency").GetString().ShouldBe("weekly");
        settings.GetProperty("digestDay").GetString().ShouldBe("friday");
        settings.GetProperty("digestChannels").EnumerateArray().Select(e => e.GetString()).ShouldBe(["telegram", "email"]);
        settings.GetProperty("email").GetString().ShouldBe("digest@example.com");
    }

    [Theory]
    [InlineData("Mars/Olympus", "off", "email", "timezone")]
    [InlineData("", "off", "email", "timezone")]
    [InlineData("UTC", "off", "sms", "digestChannels")]
    public async Task UpdateSettings_InvalidValues_Return400WithFieldError(
        string timezone, string frequency, string channel, string field)
    {
        using var client = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await client.PutAsJsonAsync("/api/me/settings", new
        {
            timezone,
            digestFrequency = frequency,
            digestDay = "monday",
            digestChannels = new[] { channel },
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateSettings_WithoutLogin_Returns401()
    {
        var response = await Client.PutAsJsonAsync("/api/me/settings", new { }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_EndsSession()
    {
        var cookie = ExternalLogin.CreateCookie(Factory, "Google", "sub-1", "bob@example.com");
        await Client.SendAsync(ExternalLogin.CallbackRequest(cookie), Ct);
        var xsrf = await AntiforgeryTests.FetchXsrfTokenAsync(Client, Ct);

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("X-XSRF-TOKEN", xsrf);
        (await Client.SendAsync(logout, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Client.GetAsync("/api/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<int> CountUsersAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(Ct);
    }
}
