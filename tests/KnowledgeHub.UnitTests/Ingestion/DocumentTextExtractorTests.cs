using KnowledgeHub.Api.Modules.Ingestion;

namespace KnowledgeHub.UnitTests.Ingestion;

public sealed class DocumentTextExtractorTests
{
    private static MemoryStream Fixture(string name) =>
        new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Ingestion", "Fixtures", name)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pdf_YieldsPageText()
    {
        var text = await DocumentTextExtractor.ExtractAsync(Fixture("sample.pdf"), DocumentKind.Pdf, Ct);

        text.ShouldContain("Zeppelin maintenance handbook");
        text.ShouldContain("Inspect the gas cells every quarter.");
    }

    [Fact]
    public async Task Docx_YieldsParagraphsOnePerLine()
    {
        var text = await DocumentTextExtractor.ExtractAsync(Fixture("sample.docx"), DocumentKind.Docx, Ct);

        text.Split('\n').ShouldBe(
            ["Quokka migration report", "Dead-letter queues collect undeliverable messages.", "Очередь сообщений обрабатывается воркером."]);
    }

    [Fact]
    public async Task Text_IsReturnedAsIs()
    {
        var text = await DocumentTextExtractor.ExtractAsync(Fixture("sample.txt"), DocumentKind.Text, Ct);

        text.ShouldContain("Заметки про Azure Service Bus");
        text.ShouldContain("ocelot gateway notes");
    }

    [Fact]
    public async Task BrokenPdf_IsNotRetryable()
    {
        var bytes = Fixture("sample.pdf").ToArray();

        var error = await Should.ThrowAsync<IngestionException>(() =>
            DocumentTextExtractor.ExtractAsync(new MemoryStream(bytes[..40]), DocumentKind.Pdf, Ct));

        error.Retryable.ShouldBeFalse();
        error.Message.ShouldBe("Не удалось извлечь текст из документа.");
    }

    [Fact]
    public async Task DocumentWithoutText_IsNotRetryable()
    {
        var error = await Should.ThrowAsync<IngestionException>(() =>
            DocumentTextExtractor.ExtractAsync(new MemoryStream("  \n "u8.ToArray()), DocumentKind.Text, Ct));

        error.Retryable.ShouldBeFalse();
    }

    [Fact]
    public async Task HugeText_IsTruncatedToSearchLimit()
    {
        var text = await DocumentTextExtractor.ExtractAsync(
            new MemoryStream(new string('a', ArticleExtractor.MaxBodyLength + 10).Select(c => (byte)c).ToArray()), DocumentKind.Text, Ct);

        text.Length.ShouldBe(ArticleExtractor.MaxBodyLength);
    }

    [Fact]
    public async Task SlowParsing_TimesOut()
    {
        using var stream = new SlowStream(Fixture("sample.txt").ToArray(), TimeSpan.FromSeconds(2));

        var error = await Should.ThrowAsync<IngestionException>(() =>
            DocumentTextExtractor.ExtractAsync(stream, DocumentKind.Text, Ct, timeout: TimeSpan.FromMilliseconds(100)));

        error.Retryable.ShouldBeFalse();
        error.Message.ShouldBe("Документ обрабатывается слишком долго.");
    }

    /// <summary>Поток, который «зависает» при чтении, как зависший парсер.</summary>
    private sealed class SlowStream(byte[] data, TimeSpan delay) : MemoryStream(data)
    {
        // MemoryStream.CopyTo не вызывает Read, поэтому «зависаем» здесь.
        public override void CopyTo(Stream destination, int bufferSize)
        {
            Thread.Sleep(delay);
            base.CopyTo(destination, bufferSize);
        }
    }
}
