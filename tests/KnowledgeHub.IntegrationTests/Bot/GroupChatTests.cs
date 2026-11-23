using KnowledgeHub.IntegrationTests.Infrastructure;

namespace KnowledgeHub.IntegrationTests.Bot;

/// <summary>
/// Бот работает только в личном чате: привязанная к аккаунту группа дала бы всем её участникам права владельца —
/// читать записи через /recent и /search, сохранять и отмечать прочитанным.
/// </summary>
public sealed class GroupChatTests(ApiFactory factory) : BotTestBase(factory)
{
    private const long GroupChatId = -1001234567890;

    [Theory]
    [InlineData("group")]
    [InlineData("supergroup")]
    [InlineData("channel")]
    public async Task StartWithCode_OutsidePrivateChat_DoesNotLink(string chatType)
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        var code = await RequestLinkCodeAsync(user);
        var text = "/start " + code;

        await SendAsync(TextUpdate(text, GroupChatId, [Entity("bot_command", text, "/start")], chatType: chatType));

        (await LinkCountAsync()).ShouldBe(0);
        Telegram.TextsTo(GroupChatId).ShouldHaveSingleItem().ShouldContain("private chat");
    }

    [Fact]
    public async Task LinkedGroup_DoesNotExposeOrChangeTheOwnersItems()
    {
        // Привязка, оставшаяся с версии без проверки: группа не должна работать и с ней.
        var owner = await LinkedUserAsync(GroupChatId);
        var itemId = await SaveNoteAsync(owner, "Зарплата за ноябрь");

        await SendAsync(CommandUpdate("recent", chatId: GroupChatId, chatType: "group"));
        await SendAsync(TextUpdate("спам от участника группы", GroupChatId, chatType: "group"));
        await SendAsync(CallbackUpdate($"read:{itemId}", GroupChatId, chatType: "group"));

        var items = await ItemsOfAsync(owner);
        items.ShouldHaveSingleItem().ReadAt.ShouldBeNull();
        Telegram.TextsTo(GroupChatId).ShouldAllBe(t => !t.Contains("Зарплата"));
        Telegram.Answers.ShouldHaveSingleItem().Text.ShouldNotBe("Marked as read");
    }
}
