using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.IntegrationTests.Bot;

public sealed class LinkingTests(ApiFactory factory) : BotTestBase(factory)
{
    private const string StartText = "/start ";

    private async Task StartAsync(string code, long chatId = ChatId)
    {
        var text = StartText + code;
        await SendAsync(TextUpdate(text, chatId, [Entity("bot_command", text, "/start")]));
    }

    private static async Task<JsonElement> StatusAsync(HttpClient user) =>
        await user.GetFromJsonAsync<JsonElement>("/api/me/telegram", Ct);

    [Fact]
    public async Task StartWithCode_LinksTheChatToTheUser()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        (await StatusAsync(user)).GetProperty("linked").GetBoolean().ShouldBeFalse();

        await StartAsync(await RequestLinkCodeAsync(user));

        var status = await StatusAsync(user);
        status.GetProperty("linked").GetBoolean().ShouldBeTrue();
        status.GetProperty("linkedAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldStartWith("Аккаунт привязан");
    }

    [Fact]
    public async Task LinkedChat_SavesItemsToTheRightUser()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        await StartAsync(await RequestLinkCodeAsync(user));

        await SendAsync(TextUpdate("после привязки"));

        var items = await user.GetFromJsonAsync<JsonElement>("/api/items", Ct);
        items.GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Code_IsStoredOnlyAsHash_AndIsSingleUse()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var code = await RequestLinkCodeAsync(user);

        var stored = await WithDbAsync((db, _) => db.TelegramLinkCodes.AsNoTracking().SingleAsync(Ct));
        stored.CodeHash.ShouldBe(TelegramLinkService.Hash(code));
        stored.CodeHash.ShouldNotContain(code);

        await StartAsync(code);
        await StartAsync(code, chatId: 999);

        (await LinkCountAsync()).ShouldBe(1);
        Telegram.TextsTo(999).ShouldHaveSingleItem().ShouldContain("недействительна");
        (await WithDbAsync((db, _) => db.TelegramLinkCodes.CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task ExpiredCode_DoesNotLink()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var code = await RequestLinkCodeAsync(user);

        Time.Advance(TelegramLinkService.CodeLifetime + TimeSpan.FromMinutes(1));
        await StartAsync(code);

        (await StatusAsync(user)).GetProperty("linked").GetBoolean().ShouldBeFalse();
        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldContain("недействительна");
    }

    [Fact]
    public async Task UnknownCode_DoesNotLink()
    {
        await StartAsync("no-such-code");

        (await LinkCountAsync()).ShouldBe(0);
        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldContain("недействительна");
    }

    [Fact]
    public async Task NewCode_InvalidatesThePreviousOne()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var first = await RequestLinkCodeAsync(user);
        var second = await RequestLinkCodeAsync(user);

        await StartAsync(first);
        (await LinkCountAsync()).ShouldBe(0);

        await StartAsync(second);
        (await LinkCountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task RelinkingFromAnotherChat_MovesTheLink()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        await StartAsync(await RequestLinkCodeAsync(user), chatId: 1);
        await StartAsync(await RequestLinkCodeAsync(user), chatId: 2);

        var link = await WithDbAsync((db, _) => db.TelegramLinks.AsNoTracking().SingleAsync(Ct));
        link.ChatId.ShouldBe(2);
    }

    [Fact]
    public async Task ChatLinkedToAnotherUser_PassesToTheNewOwner()
    {
        using var a = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        using var b = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        await StartAsync(await RequestLinkCodeAsync(a));
        await StartAsync(await RequestLinkCodeAsync(b));

        (await StatusAsync(a)).GetProperty("linked").GetBoolean().ShouldBeFalse();
        (await StatusAsync(b)).GetProperty("linked").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Unlink_RemovesTheLinkAndStopsSaving()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        await StartAsync(await RequestLinkCodeAsync(user));

        (await user.DeleteAsync("/api/me/telegram/link", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.DeleteAsync("/api/me/telegram/link", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await SendAsync(TextUpdate("после отвязки"));

        (await StatusAsync(user)).GetProperty("linked").GetBoolean().ShouldBeFalse();
        (await user.GetFromJsonAsync<JsonElement>("/api/items", Ct)).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task LinkEndpoints_RequireSignIn()
    {
        (await Client.GetAsync("/api/me/telegram", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.PostAsync("/api/me/telegram/link", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.DeleteAsync("/api/me/telegram/link", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LinkRequest_WithoutConfiguredBot_Returns503()
    {
        using var unconfigured = Factory.WithWebHostBuilder(b => b.UseSetting("Bot:Username", ""));
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        using var client = unconfigured.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, user.DefaultRequestHeaders.GetValues(TestAuthHandler.HeaderName).Single());

        (await client.PostAsync("/api/me/telegram/link", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task StartWithoutCode_ForUnlinkedChat_ExplainsHowToLink()
    {
        await SendAsync(TextUpdate("/start", entities: [Entity("bot_command", "/start", "/start")]));

        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldContain("Привязать Telegram");
    }
}
