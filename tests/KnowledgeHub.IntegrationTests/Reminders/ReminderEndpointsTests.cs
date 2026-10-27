using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.IntegrationTests.Infrastructure;
using KnowledgeHub.IntegrationTests.Items;

namespace KnowledgeHub.IntegrationTests.Reminders;

public sealed class ReminderEndpointsTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset InAnHour => DateTimeOffset.UtcNow.AddHours(1);

    private async Task<HttpClient> UserAsync() => await Factory.CreateClientForAsync(Guid.CreateVersion7());

    public static async Task<JsonElement> CreateAsync(HttpClient client, string itemId, DateTimeOffset? remindAt = null, string channel = "telegram")
    {
        var response = await client.PostAsJsonAsync("/api/reminders", new { itemId, remindAt = remindAt ?? InAnHour, channel }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    public static async Task<string> CreateIdAsync(HttpClient client, string itemId) =>
        (await CreateAsync(client, itemId)).GetProperty("id").GetString()!;

    [Fact]
    public async Task Reminder_IsCreatedAndListed()
    {
        using var user = await UserAsync();
        var itemId = await ItemEndpointsTests.CreateNoteIdAsync(user);
        var remindAt = InAnHour;

        var created = await CreateAsync(user, itemId, remindAt, "both");

        created.GetProperty("itemId").GetString().ShouldBe(itemId);
        created.GetProperty("channel").GetString().ShouldBe("both");
        created.GetProperty("sentAt").ValueKind.ShouldBe(JsonValueKind.Null);
        created.GetProperty("remindAt").GetDateTimeOffset().ShouldBe(remindAt, TimeSpan.FromMilliseconds(1));

        var all = await user.GetFromJsonAsync<JsonElement>("/api/reminders", Ct);
        all.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task ReminderInThePast_IsRejected()
    {
        using var user = await UserAsync();
        var itemId = await ItemEndpointsTests.CreateNoteIdAsync(user);

        var response = await user.PostAsJsonAsync("/api/reminders", new { itemId, remindAt = DateTimeOffset.UtcNow.AddMinutes(-1), channel = "email" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("remindAt", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("sms")]
    [InlineData("")]
    public async Task UnknownChannel_IsRejected(string channel)
    {
        using var user = await UserAsync();
        var itemId = await ItemEndpointsTests.CreateNoteIdAsync(user);

        var response = await user.PostAsJsonAsync("/api/reminders", new { itemId, remindAt = InAnHour, channel }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReminderForMissingOrForeignItem_IsNotFound()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();
        var foreignItem = await ItemEndpointsTests.CreateNoteIdAsync(a);

        foreach (var itemId in new[] { foreignItem, Guid.CreateVersion7().ToString() })
        {
            var response = await b.PostAsJsonAsync("/api/reminders", new { itemId, remindAt = InAnHour, channel = "telegram" }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Upcoming_ReturnsOnlyUnsentRemindersSoonestFirst()
    {
        using var user = await UserAsync();
        var itemId = await ItemEndpointsTests.CreateNoteIdAsync(user);
        var later = await CreateAsync(user, itemId, DateTimeOffset.UtcNow.AddDays(2));
        var sooner = await CreateAsync(user, itemId, DateTimeOffset.UtcNow.AddHours(2));

        var upcoming = await user.GetFromJsonAsync<JsonElement>("/api/reminders?upcoming=true", Ct);

        upcoming.EnumerateArray().Select(r => r.GetProperty("id").GetString())
            .ShouldBe([sooner.GetProperty("id").GetString(), later.GetProperty("id").GetString()]);
    }

    [Fact]
    public async Task RemindersOfAnItem_AreListedPerItem()
    {
        using var user = await UserAsync();
        var first = await ItemEndpointsTests.CreateNoteIdAsync(user, "one");
        var second = await ItemEndpointsTests.CreateNoteIdAsync(user, "two");
        await CreateAsync(user, first);
        await CreateAsync(user, first, DateTimeOffset.UtcNow.AddDays(1));
        await CreateAsync(user, second);

        var ofFirst = await user.GetFromJsonAsync<JsonElement>($"/api/items/{first}/reminders", Ct);

        ofFirst.GetArrayLength().ShouldBe(2);
        (await user.GetAsync($"/api/items/{Guid.CreateVersion7()}/reminders", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reminder_IsDeleted()
    {
        using var user = await UserAsync();
        var id = await CreateIdAsync(user, await ItemEndpointsTests.CreateNoteIdAsync(user));

        (await user.DeleteAsync($"/api/reminders/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.DeleteAsync($"/api/reminders/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await user.GetFromJsonAsync<JsonElement>("/api/reminders", Ct)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task DeletingTheItem_DeletesItsReminders()
    {
        using var user = await UserAsync();
        var itemId = await ItemEndpointsTests.CreateNoteIdAsync(user);
        await CreateAsync(user, itemId);

        (await user.DeleteAsync($"/api/items/{itemId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await user.GetFromJsonAsync<JsonElement>("/api/reminders", Ct)).GetArrayLength().ShouldBe(0);
    }
}
