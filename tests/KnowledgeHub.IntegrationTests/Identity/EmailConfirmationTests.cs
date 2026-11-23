using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeHub.IntegrationTests.Identity;

/// <summary>
/// Адрес для дайджестов и напоминаний (S10): новый адрес начинает получать письма только после подтверждения кодом,
/// отправленным на него. Без кода — только очистка (письма идут на адрес профиля), адрес профиля и уже действующий адрес.
/// </summary>
public sealed class EmailConfirmationTests : IntegrationTest, IDisposable
{
    private readonly WebApplicationFactory<Program> _host;

    public EmailConfirmationTests(ApiFactory factory) : base(factory)
    {
        Time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Time)));
    }

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeTimeProvider Time { get; }

    /// <summary>Пользователь с адресом профиля <c>profile-…@example.com</c>; клиент — на хосте с управляемым временем.</summary>
    private async Task<(Guid Id, string ProfileEmail, HttpClient Client)> CreateUserAsync()
    {
        var id = Guid.CreateVersion7();
        var profileEmail = $"profile-{id:N}@example.com";
        using (await Factory.CreateClientForAsync(id, profileEmail))
        {
        }

        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, id.ToString());
        return (id, profileEmail, client);
    }

    private static string NewAddress() => $"digest-{Guid.NewGuid():N}@example.com";

    private static object Settings(string? email) => new
    {
        timezone = "UTC",
        digestFrequency = "daily",
        digestDay = "monday",
        digestChannels = new[] { "email" },
        email,
    };

    private static async Task<JsonElement> SettingsOfAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("settings");

    private static string? EmailOf(JsonElement settings) => settings.GetProperty("email").GetString();

    private static string? PendingOf(JsonElement settings) =>
        settings.TryGetProperty("pendingEmail", out var pending) ? pending.GetString() : null;

    private static Task<HttpResponseMessage> RequestCodeAsync(HttpClient client, string? email) =>
        client.PostAsJsonAsync("/api/me/email", new { email }, Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/me/email/confirm", new { code }, Ct);

    /// <summary>Код из последнего письма на <paramref name="address"/>.</summary>
    private string CodeSentTo(string address)
    {
        var mail = Factory.Email.Sent.Last(m => m.To == address);
        return Regex.Match(mail.Text, @"\b\d{6}\b").Value;
    }

    private static string OtherCode(string code) => code == "000000" ? "111111" : "000000";

    private async Task SetSettingsEmailInDbAsync(Guid userId, string email)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserSettings
            .Where(s => s.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, email), Ct);
    }

    // ---- PUT /me/settings: новый адрес без подтверждения не принимается ----

    [Fact]
    public async Task UpdateSettings_WithNewAddress_IsRejected_AndAddressStays()
    {
        var (_, profileEmail, client) = await CreateUserAsync();

        var response = await client.PutAsJsonAsync("/api/me/settings", Settings(NewAddress()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("email", out _).ShouldBeTrue();
        EmailOf(await SettingsOfAsync(client)).ShouldBe(profileEmail);
    }

    [Fact]
    public async Task UpdateSettings_ClearingProfileOrCurrentAddress_NeedsNoConfirmation()
    {
        var (id, profileEmail, client) = await CreateUserAsync();

        (await client.PutAsJsonAsync("/api/me/settings", Settings(null), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        EmailOf(await SettingsOfAsync(client)).ShouldBeNull();

        (await client.PutAsJsonAsync("/api/me/settings", Settings(profileEmail.ToUpperInvariant()), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Адрес, сохранённый до появления подтверждения, остаётся действующим, и настройки с ним сохраняются.
        var earlier = NewAddress();
        await SetSettingsEmailInDbAsync(id, earlier);
        (await client.PutAsJsonAsync("/api/me/settings", Settings(earlier), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        EmailOf(await SettingsOfAsync(client)).ShouldBe(earlier);
    }

    // ---- Подтверждение кодом ----

    [Fact]
    public async Task NewAddress_StartsReceivingMailOnlyAfterTheCodeIsConfirmed()
    {
        var (_, profileEmail, client) = await CreateUserAsync();
        var address = NewAddress();

        var requested = await RequestCodeAsync(client, address);

        requested.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pending = await SettingsOfAsync(client);
        EmailOf(pending).ShouldBe(profileEmail);
        PendingOf(pending).ShouldBe(address);
        var code = CodeSentTo(address);
        code.Length.ShouldBe(6);

        var confirmed = await ConfirmAsync(client, code);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = await SettingsOfAsync(client);
        EmailOf(settings).ShouldBe(address);
        PendingOf(settings).ShouldBeNull();
        (await ConfirmAsync(client, code)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddressThatNeedsNoConfirmation_IsAppliedWithoutAnEmail()
    {
        var (id, profileEmail, client) = await CreateUserAsync();
        await SetSettingsEmailInDbAsync(id, NewAddress());

        var response = await RequestCodeAsync(client, profileEmail);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = await SettingsOfAsync(client);
        EmailOf(settings).ShouldBe(profileEmail);
        PendingOf(settings).ShouldBeNull();
        Factory.Email.Sent.ShouldNotContain(m => m.To == profileEmail);

        // Пустой адрес — письма на адрес профиля; заодно отменяет ожидающий подтверждения.
        await RequestCodeAsync(client, NewAddress());
        (await RequestCodeAsync(client, "  ")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var cleared = await SettingsOfAsync(client);
        EmailOf(cleared).ShouldBeNull();
        PendingOf(cleared).ShouldBeNull();
    }

    [Fact]
    public async Task WrongCodes_UseUpTheAttempts_ThenEvenTheRightCodeFails()
    {
        var (_, profileEmail, client) = await CreateUserAsync();
        var address = NewAddress();
        await RequestCodeAsync(client, address);
        var code = CodeSentTo(address);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await ConfirmAsync(client, OtherCode(code))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await ConfirmAsync(client, code)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var settings = await SettingsOfAsync(client);
        EmailOf(settings).ShouldBe(profileEmail);
        PendingOf(settings).ShouldBeNull();
    }

    [Fact]
    public async Task Code_ExpiresAfter15Minutes()
    {
        var (_, profileEmail, client) = await CreateUserAsync();
        var address = NewAddress();
        await RequestCodeAsync(client, address);

        Time.Advance(TimeSpan.FromMinutes(16));

        (await ConfirmAsync(client, CodeSentTo(address))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        EmailOf(await SettingsOfAsync(client)).ShouldBe(profileEmail);
    }

    [Fact]
    public async Task NewRequest_ReplacesThePendingAddressAndCode()
    {
        var (_, _, client) = await CreateUserAsync();
        var first = NewAddress();
        var second = NewAddress();
        await RequestCodeAsync(client, first);
        var firstCode = CodeSentTo(first);
        await RequestCodeAsync(client, second);
        var secondCode = CodeSentTo(second);

        if (firstCode != secondCode)
        {
            (await ConfirmAsync(client, firstCode)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await ConfirmAsync(client, secondCode)).StatusCode.ShouldBe(HttpStatusCode.OK);
        EmailOf(await SettingsOfAsync(client)).ShouldBe(second);
    }

    [Fact]
    public async Task InvalidAddress_Returns400()
    {
        var (_, _, client) = await CreateUserAsync();

        var response = await RequestCodeAsync(client, "not an address");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("email", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task SendFailure_Returns503_AndNothingIsPending()
    {
        var (_, _, client) = await CreateUserAsync();
        var address = $"fail-{Guid.NewGuid():N}@example.com";
        using var failing = _host.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<KnowledgeHub.Api.Infrastructure.Email.IEmailSender>(new ThrowingEmailSender())));
        using var failingClient = failing.CreateClient();
        failingClient.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, client.DefaultRequestHeaders.GetValues(TestAuthHandler.HeaderName).Single());

        var response = await RequestCodeAsync(failingClient, address);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        PendingOf(await SettingsOfAsync(client)).ShouldBeNull();
    }

    private sealed class ThrowingEmailSender : KnowledgeHub.Api.Infrastructure.Email.IEmailSender
    {
        public Task SendAsync(string to, string subject, string html, string text, CancellationToken ct) =>
            throw new HttpRequestException("Сервис e-mail недоступен.");
    }

    [Fact]
    public async Task CodeRequests_AreLimitedPerUser()
    {
        var (_, _, client) = await CreateUserAsync();

        for (var i = 0; i < 5; i++)
        {
            (await RequestCodeAsync(client, NewAddress())).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await RequestCodeAsync(client, NewAddress())).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ApiToken_CannotChangeTheAddress()
    {
        // Утёкший токен не должен уметь перенаправить дайджесты на свой адрес.
        var (_, _, client) = await CreateUserAsync();
        var (_, token) = await ApiTokenTests.CreateTokenAsync(client);
        using var bearer = _host.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await RequestCodeAsync(bearer, NewAddress())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ConfirmAsync(bearer, "123456")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bearer.PutAsJsonAsync("/api/me/settings", Settings(NewAddress()), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
