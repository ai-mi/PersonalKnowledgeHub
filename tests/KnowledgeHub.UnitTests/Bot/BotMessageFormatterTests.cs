using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.UnitTests.Bot;

public sealed class BotMessageFormatterTests
{
    private static ItemSummary Summary(Guid id, string? title, string? url = null) =>
        new(id, ItemType.Link, title, url, ItemStatus.Ready, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, []);

    [Fact]
    public void Escape_ReplacesHtmlSpecialCharacters()
    {
        BotMessageFormatter.Escape("<script>alert(\"x\") & more</script>")
            .ShouldBe("&lt;script&gt;alert(&quot;x&quot;) &amp; more&lt;/script&gt;");
    }

    [Fact]
    public void Ready_EscapesTitle()
    {
        BotMessageFormatter.Ready("<script>x</script>").ShouldBe("Готово: &lt;script&gt;x&lt;/script&gt;");
    }

    [Fact]
    public void Failed_EscapesError()
    {
        BotMessageFormatter.Failed("a < b").ShouldBe("Не удалось обработать: a &lt; b");
    }

    [Theory]
    [InlineData("Заголовок", "https://example.com", "Заголовок")]
    [InlineData(null, "https://example.com", "https://example.com")]
    [InlineData("  ", "https://example.com", "https://example.com")]
    [InlineData(null, null, "Без названия")]
    public void DisplayTitle_FallsBackToUrlThenPlaceholder(string? title, string? url, string expected)
    {
        BotMessageFormatter.DisplayTitle(title, url).ShouldBe(expected);
    }

    [Fact]
    public void ItemList_NumbersItemsAndLinksToTheWebApp()
    {
        var id = Guid.CreateVersion7();

        var text = BotMessageFormatter.ItemList("Найдено:", [Summary(id, "Про <шину>")], "https://kh.example.com/");

        text.ShouldBe($"Найдено:\n1. <a href=\"https://kh.example.com/items/{id}\">Про &lt;шину&gt;</a>");
    }

    [Fact]
    public void ItemList_WithoutWebBaseUrl_ShowsPlainTitles()
    {
        var text = BotMessageFormatter.ItemList("Последние записи:", [Summary(Guid.CreateVersion7(), "A"), Summary(Guid.CreateVersion7(), null, "https://a.b")], "");

        text.ShouldBe("Последние записи:\n1. A\n2. https://a.b");
    }

    [Fact]
    public void ReadButton_RoundTripsThroughCallbackParser()
    {
        var id = Guid.CreateVersion7();

        var button = BotMessageFormatter.ReadButton(id);

        button.Text.ShouldBe("Прочитано");
        BotMessageFormatter.TryParseReadCallback(button.CallbackData, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("read:")]
    [InlineData("read:not-a-guid")]
    [InlineData("unread:6c6a2c2e-0d6a-4a4f-9b1a-1d7d2b0f7a11")]
    public void TryParseReadCallback_RejectsMalformedData(string? data)
    {
        BotMessageFormatter.TryParseReadCallback(data, out _).ShouldBeFalse();
    }
}
