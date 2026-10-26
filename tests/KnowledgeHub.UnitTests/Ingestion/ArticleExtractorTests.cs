using System.Text;
using KnowledgeHub.Api.Modules.Ingestion;

namespace KnowledgeHub.UnitTests.Ingestion;

public sealed class ArticleExtractorTests
{
    private static readonly Uri PageUrl = new("https://example.com/post");

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ingestion", "Fixtures", name));

    [Fact]
    public void Article_YieldsTitleAndText()
    {
        var result = ArticleExtractor.Extract(PageUrl, Fixture("article.html"));

        result.Title.ShouldNotBeNull().ShouldContain("Service Bus queues explained");
        result.Body.ShouldNotBeNull().ShouldContain("dead-letter queues");
        result.Body.ShouldNotContain("Copyright Example");
        result.Body.ShouldNotContain("<p>");
    }

    [Fact]
    public void PageWithoutArticle_YieldsOnlyTitle()
    {
        var result = ArticleExtractor.Extract(PageUrl, Fixture("no-article.html"));

        result.Title.ShouldBe("Dashboard");
        result.Body.ShouldBeNull();
    }

    [Fact]
    public void Cyrillic_Windows1251_IsExtracted()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var html = Encoding.GetEncoding("windows-1251").GetString(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Ingestion", "Fixtures", "cp1251.html")));

        var result = ArticleExtractor.Extract(PageUrl, html);

        result.Title.ShouldNotBeNull().ShouldContain("Очереди сообщений");
        result.Body.ShouldNotBeNull().ShouldContain("мёртвые очереди");
    }

    [Fact]
    public void EmptyPage_Throws()
    {
        var error = Should.Throw<IngestionException>(() => ArticleExtractor.Extract(PageUrl, "<html><body></body></html>"));

        error.Retryable.ShouldBeFalse();
        error.Message.ShouldContain("Не удалось извлечь содержимое");
    }

    [Fact]
    public void ReadablePageWithoutTitle_YieldsOnlyBody()
    {
        const string html =
            """
            <!doctype html><html><head></head><body><article>
            <p>Azure Service Bus is a fully managed enterprise message broker with message queues and publish-subscribe topics.
            Queues decouple producers from consumers, so a slow consumer never blocks the sender, and messages survive restarts.</p>
            <p>Dead-letter queues collect messages that cannot be delivered, while sessions guarantee ordering for related messages
            and duplicate detection drops repeated sends within a configurable time window.</p>
            <p>Choosing between queues and topics depends on whether one consumer or many subscribers need every message,
            and on how filtering rules should route copies of a message to each subscription.</p>
            </article></body></html>
            """;

        var result = ArticleExtractor.Extract(PageUrl, html);

        result.Title.ShouldBeNull();
        result.Body.ShouldNotBeNull().ShouldContain("Dead-letter queues");
    }

    [Fact]
    public void VeryLongTitleAndBody_AreTruncated()
    {
        var sentence = "Azure Service Bus is a fully managed enterprise message broker with queues and topics for decoupled systems. ";
        var html = $"<!doctype html><html><head><title>{new string('T', 800)}</title></head><body><article>" +
                   string.Concat(Enumerable.Repeat($"<p>{sentence}{sentence}{sentence}</p>", 3000)) + "</article></body></html>";

        var result = ArticleExtractor.Extract(PageUrl, html);

        result.Title.ShouldNotBeNull().Length.ShouldBe(Api.Modules.Items.Item.MaxTitleLength);
        result.Body.ShouldNotBeNull().Length.ShouldBe(ArticleExtractor.MaxBodyLength);
    }
}
