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
}
