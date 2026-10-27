using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Ingestion;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.IntegrationTests.Infrastructure;
using KnowledgeHub.IntegrationTests.Items;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KnowledgeHub.IntegrationTests.Bot;

public sealed class NotificationsTests(ApiFactory factory) : BotTestBase(factory)
{
    private const string Article =
        """
        <!doctype html><html><head><meta charset="utf-8"><title>Service Bus queues explained</title></head><body>
        <main><article><h1>Service Bus queues explained</h1>
        <p>Azure Service Bus is a fully managed enterprise message broker with message queues and publish-subscribe topics.
        Queues decouple producers from consumers, so a slow consumer never blocks the sender, and messages survive restarts.</p>
        <p>Dead-letter queues collect messages that cannot be delivered, while sessions guarantee ordering for related messages
        and duplicate detection drops repeated sends within a configurable time window.</p>
        <p>Choosing between queues and topics depends on whether one consumer or many subscribers need every message,
        and on how filtering rules should route copies of a message to each subscription.</p>
        </article></main></body></html>
        """;

    private static string Url(WireMockServer server) => $"http://127.0.0.1:{server.Port}/page";

    private static void Serve(WireMockServer server, int status, string body = "") =>
        server.Given(Request.Create().WithPath("/page").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "text/html; charset=utf-8").WithBody(body));

    private Task SendLinkAsync(string url) => SendAsync(TextUpdate(url, entities: [Entity("url", url, url)]), runIngest: true);

    /// <summary>Выполняет действие в scope от имени пользователя (как воркер задачи пользователя).</summary>
    private async Task<T> AsUserAsync<T>(Guid userId, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().RunAs(userId);
        return await action(scope.ServiceProvider);
    }

    [Fact]
    public async Task LinkSentToBot_IsFollowedByReadyMessageWithTitle()
    {
        using var server = WireMockServer.Start();
        Serve(server, 200, Article);
        await LinkedUserAsync();

        await SendLinkAsync(Url(server));

        Telegram.TextsTo(ChatId).ShouldBe(["Сохранено", "Готово: Service Bus queues explained"]);
    }

    [Fact]
    public async Task LinkThatCannotBeFetched_IsFollowedByFailureMessage()
    {
        using var server = WireMockServer.Start();
        Serve(server, 404);
        await LinkedUserAsync();

        await SendLinkAsync(Url(server));

        var texts = Telegram.TextsTo(ChatId).ToList();
        texts[0].ShouldBe("Сохранено");
        texts.Count.ShouldBe(2);
        texts[1].ShouldStartWith("Не удалось обработать: ");
    }

    [Fact]
    public async Task LinkSavedThroughTheApi_DoesNotNotifyTelegram()
    {
        using var server = WireMockServer.Start();
        Serve(server, 200, Article);
        var userId = await LinkedUserAsync();
        using var client = ClientFor(userId);
        await ItemEndpointsTests.CreateAsync(client, new { type = "link", url = Url(server) });

        await ProcessAllAsync(runIngest: true);

        (await ItemsOfAsync(userId)).ShouldHaveSingleItem().Status.ShouldBe(ItemStatus.Ready);
        Telegram.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task DocumentSentToBot_IsFollowedByReadyMessage()
    {
        await LinkedUserAsync();
        Telegram.AddFile("note-file", "Заметки по Service Bus"u8.ToArray());

        await SendAsync(DocumentUpdate("note-file", "bus-notes.txt", 40), runIngest: true);

        Telegram.TextsTo(ChatId).ShouldBe(["Сохранено", "Готово: bus-notes"]);
    }

    [Fact]
    public async Task ReadyMessage_EscapesHtmlInTheTitle()
    {
        var userId = await LinkedUserAsync();
        using var client = ClientFor(userId);
        var itemId = Guid.Parse((await ItemEndpointsTests.CreateAsync(client, new { type = "note", title = "<script>alert(1)</script>", body = "x" })).GetProperty("id").GetString()!);

        await AsUserAsync(userId, async sp =>
        {
            await sp.GetRequiredService<IIngestionCompletedHandler>().OnCompletedAsync(itemId, ItemStatus.Ready, null, ChatId, Ct);
            return 0;
        });

        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldBe("Готово: &lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    public async Task CompletionWithoutChat_SendsNothing()
    {
        var userId = await LinkedUserAsync();

        await AsUserAsync(userId, async sp =>
        {
            await sp.GetRequiredService<IIngestionCompletedHandler>().OnCompletedAsync(Guid.CreateVersion7(), ItemStatus.Failed, "ошибка", null, Ct);
            return 0;
        });

        Telegram.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Notifier_WhenBotIsBlocked_RemovesTheLink()
    {
        var userId = await LinkedUserAsync();
        Telegram.BlockChat(ChatId);

        var sent = await AsUserAsync(userId, sp => sp.GetRequiredService<IBotNotifier>().SendAsync(userId, "привет", ct: Ct));

        sent.ShouldBeFalse();
        (await LinkCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Notifier_WhenBlocked_KeepsOtherUsersLinks()
    {
        var blocked = await LinkedUserAsync(ChatId);
        await LinkedUserAsync(chatId: 2);
        Telegram.BlockChat(ChatId);

        await AsUserAsync(blocked, sp => sp.GetRequiredService<IBotNotifier>().SendAsync(blocked, "привет", ct: Ct));

        (await LinkCountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Notifier_OnTransientFailure_KeepsTheLinkAndDoesNotThrow()
    {
        var userId = await LinkedUserAsync();
        Telegram.FailSending = true;

        var sent = await AsUserAsync(userId, sp => sp.GetRequiredService<IBotNotifier>().SendAsync(userId, "привет", ct: Ct));

        sent.ShouldBeFalse();
        (await LinkCountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Notifier_ForUserWithoutLink_ReturnsFalse()
    {
        var userId = Guid.CreateVersion7();

        var sent = await AsUserAsync(userId, sp => sp.GetRequiredService<IBotNotifier>().SendAsync(userId, "привет", ct: Ct));

        sent.ShouldBeFalse();
        Telegram.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Notifier_SendsTextAndButtonsToTheLinkedChat()
    {
        var userId = await LinkedUserAsync();
        var button = BotMessageFormatter.ReadButton(Guid.CreateVersion7());

        var sent = await AsUserAsync(userId, sp => sp.GetRequiredService<IBotNotifier>().SendAsync(userId, "напоминание", [button], Ct));

        sent.ShouldBeTrue();
        var message = Telegram.Sent.ShouldHaveSingleItem();
        message.ChatId.ShouldBe(ChatId);
        message.Text.ShouldBe("напоминание");
        message.Buttons.ShouldBe([button]);
    }
}
