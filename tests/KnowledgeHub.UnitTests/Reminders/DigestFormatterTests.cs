using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Reminders;

namespace KnowledgeHub.UnitTests.Reminders;

public sealed class DigestFormatterTests
{
    private const string WebBaseUrl = "https://kh.example.com/";

    private static ItemSummary Summary(Guid id, string? title) =>
        new(id, ItemType.Note, title, null, ItemStatus.Ready, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, []);

    [Fact]
    public void AllSections_AreRenderedInBothVersions()
    {
        var unreadId = Guid.CreateVersion7();
        var oldId = Guid.CreateVersion7();
        var reminderId = Guid.CreateVersion7();
        var digest = new Digest(
            [Summary(unreadId, "Непрочитанная")],
            [Summary(oldId, "Старая")],
            [new DigestReminder(reminderId, "Напомнить", new DateTime(2026, 11, 2, 18, 30, 0))]);

        var message = DigestFormatter.Format(digest, WebBaseUrl);

        message.Subject.ShouldBe(DigestFormatter.Subject);
        message.Html.ShouldContain("Непрочитанное");
        message.Html.ShouldContain("Сохранено год назад");
        message.Html.ShouldContain("Ближайшие напоминания");
        message.Html.ShouldContain($"<a href=\"https://kh.example.com/items/{unreadId}\">Непрочитанная</a>");
        message.Html.ShouldContain("Напомнить</a> — 02.11 18:30");
        message.Text.ShouldContain($"1. Старая\n   https://kh.example.com/items/{oldId}");
        message.Text.ShouldContain("Напомнить — 02.11 18:30");
        message.Text.ShouldNotContain("<a ");
    }

    [Fact]
    public void EmptySections_AreOmitted()
    {
        var message = DigestFormatter.Format(new Digest([Summary(Guid.CreateVersion7(), "Одна")], [], []), WebBaseUrl);

        message.Html.ShouldNotContain("Сохранено год назад");
        message.Html.ShouldNotContain("Ближайшие напоминания");
    }

    [Fact]
    public void UserText_IsEscaped()
    {
        var digest = new Digest(
            [Summary(Guid.CreateVersion7(), "<script>alert(1)</script>")],
            [],
            [new DigestReminder(Guid.CreateVersion7(), "a & <b>", new DateTime(2026, 11, 2, 9, 0, 0))]);

        var message = DigestFormatter.Format(digest, WebBaseUrl);

        message.Html.ShouldNotContain("<script>");
        message.Html.ShouldNotContain("<b>");
        message.Html.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
        message.Html.ShouldContain("a &amp; &lt;b&gt;");
        message.Text.ShouldContain("<script>alert(1)</script>");
    }

    [Fact]
    public void WithoutWebBaseUrl_NoLinksAreRendered()
    {
        var message = DigestFormatter.Format(new Digest([Summary(Guid.CreateVersion7(), "Одна")], [], []), "");

        message.Html.ShouldNotContain("<a ");
        message.Text.ShouldNotContain("http");
    }

    [Fact]
    public void ReadButtons_AreNumberedAndLimited()
    {
        var unread = Enumerable.Range(0, 8).Select(i => Summary(Guid.CreateVersion7(), $"Запись {i}")).ToList();

        var message = DigestFormatter.Format(new Digest(unread, [], []), WebBaseUrl);

        message.Buttons.ShouldNotBeNull().Count.ShouldBe(DigestFormatter.MaxReadButtons);
        message.Buttons[0].ShouldBe(BotMessageFormatter.ReadButton(unread[0].Id, "Прочитано 1"));
        message.Buttons[^1].Text.ShouldBe($"Прочитано {DigestFormatter.MaxReadButtons}");
    }

    [Fact]
    public void WithoutUnread_HasNoButtons()
    {
        var message = DigestFormatter.Format(new Digest([], [Summary(Guid.CreateVersion7(), "Старая")], []), WebBaseUrl);

        message.Buttons.ShouldNotBeNull().ShouldBeEmpty();
    }
}
