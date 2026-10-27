using KnowledgeHub.IntegrationTests.Infrastructure;
using KnowledgeHub.IntegrationTests.Items;

namespace KnowledgeHub.IntegrationTests.Bot;

public sealed class CommandsTests(ApiFactory factory) : BotTestBase(factory)
{
    private async Task<string> SaveNoteAsync(Guid userId, string title, string body = "kubernetes")
    {
        using var client = ClientFor(userId);
        return (await ItemEndpointsTests.CreateAsync(client, new { type = "note", title, body })).GetProperty("id").GetString()!;
    }

    private string[] RepliedLines(long chatId = ChatId) =>
        Telegram.TextsTo(chatId).ShouldHaveSingleItem().Split('\n');

    [Fact]
    public async Task Search_ReturnsAtMostFiveResults_OnlyFromTheChatOwner()
    {
        var owner = await LinkedUserAsync(ChatId);
        var stranger = await LinkedUserAsync(chatId: 2);
        for (var i = 1; i <= 7; i++)
        {
            await SaveNoteAsync(owner, $"Мой документ {i}");
        }

        await SaveNoteAsync(stranger, "Чужой секрет");

        await SendAsync(CommandUpdate("search", "kubernetes"));

        var lines = RepliedLines();
        lines[0].ShouldBe("Найдено:");
        lines.Length.ShouldBe(6);
        lines.Skip(1).ShouldAllBe(l => l.Contains("Мой документ"));
        Telegram.Sent.ShouldAllBe(m => !m.Text.Contains("Чужой секрет"));
    }

    [Fact]
    public async Task Search_ForAnotherUser_DoesNotSeeTheFirstUsersItems()
    {
        var owner = await LinkedUserAsync(ChatId);
        await LinkedUserAsync(chatId: 2);
        await SaveNoteAsync(owner, "Приватная запись");

        await SendAsync(CommandUpdate("search", "kubernetes", chatId: 2));

        Telegram.TextsTo(2).ShouldHaveSingleItem().ShouldBe("Ничего не найдено");
    }

    [Fact]
    public async Task Search_WithoutQuery_ExplainsUsage()
    {
        await LinkedUserAsync();

        await SendAsync(CommandUpdate("search"));

        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldBe("Использование: /search &lt;запрос&gt;");
    }

    [Fact]
    public async Task Search_EscapesHtmlInTitles()
    {
        var userId = await LinkedUserAsync();
        await SaveNoteAsync(userId, "<script>alert(1)</script>");

        await SendAsync(CommandUpdate("search", "kubernetes"));

        var reply = Telegram.TextsTo(ChatId).ShouldHaveSingleItem();
        reply.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
        reply.ShouldNotContain("<script>");
    }

    [Fact]
    public async Task Search_ByUnlinkedChat_IsNotExecuted()
    {
        await SendAsync(CommandUpdate("search", "kubernetes", chatId: 99));

        Telegram.TextsTo(99).ShouldHaveSingleItem().ShouldContain("Привязать Telegram");
    }

    [Fact]
    public async Task Recent_ListsFiveNewestItemsFirst()
    {
        var userId = await LinkedUserAsync();
        for (var i = 1; i <= 6; i++)
        {
            await SaveNoteAsync(userId, $"Запись {i}", body: $"текст {i}");
        }

        await SendAsync(CommandUpdate("recent"));

        var lines = RepliedLines();
        lines[0].ShouldBe("Последние записи:");
        lines.Skip(1).ShouldBe(["1. Запись 6", "2. Запись 5", "3. Запись 4", "4. Запись 3", "5. Запись 2"]);
    }

    [Fact]
    public async Task Recent_WithNoItems_SaysSo()
    {
        await LinkedUserAsync();

        await SendAsync(CommandUpdate("recent"));

        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldBe("Записей пока нет");
    }

    [Fact]
    public async Task ReadButton_MarksTheItemRead()
    {
        var userId = await LinkedUserAsync();
        var itemId = await SaveNoteAsync(userId, "К прочтению");

        await SendAsync(CallbackUpdate($"read:{itemId}"));

        (await ItemsOfAsync(userId)).ShouldHaveSingleItem().ReadAt.ShouldNotBeNull();
        Telegram.Answers.ShouldHaveSingleItem().ShouldBe(("cb-1", "Отмечено прочитанным"));
    }

    [Fact]
    public async Task ReadButton_WithSomeoneElsesItem_ReportsNotFoundAndChangesNothing()
    {
        await LinkedUserAsync(ChatId);
        var stranger = await LinkedUserAsync(chatId: 2);
        var foreignItem = await SaveNoteAsync(stranger, "Чужая запись");

        await SendAsync(CallbackUpdate($"read:{foreignItem}"));

        (await ItemsOfAsync(stranger)).ShouldHaveSingleItem().ReadAt.ShouldBeNull();
        Telegram.Answers.ShouldHaveSingleItem().Text.ShouldBe("Запись не найдена");
    }

    [Theory]
    [InlineData("read:not-a-guid")]
    [InlineData("read:00000000-0000-0000-0000-000000000001")]
    [InlineData("garbage")]
    public async Task ReadButton_WithUnknownOrMalformedData_ReportsNotFound(string data)
    {
        await LinkedUserAsync();

        await SendAsync(CallbackUpdate(data));

        Telegram.Answers.ShouldHaveSingleItem().Text.ShouldBe("Запись не найдена");
    }

    [Fact]
    public async Task ReadButton_FromUnlinkedChat_IsRejected()
    {
        var stranger = await LinkedUserAsync(chatId: 2);
        var itemId = await SaveNoteAsync(stranger, "Чужая запись");

        await SendAsync(CallbackUpdate($"read:{itemId}", chatId: 99));

        (await ItemsOfAsync(stranger)).ShouldHaveSingleItem().ReadAt.ShouldBeNull();
        Telegram.Answers.ShouldHaveSingleItem().Text.ShouldBe("Чат не привязан к аккаунту");
    }
}
