using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
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
        error.Message.ShouldBe("Could not extract text from the document.");
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

    /// <summary>Процесс разбора с этим id завершился (или его не было).</summary>
    private static bool HasExited(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>Поток, чтение которого «зависает»: процесс разбора ждёт конца stdin и сам не завершится никогда.</summary>
    private sealed class HangingStream() : MemoryStream("начало"u8.ToArray())
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task SlowParsing_TimesOut_AndTheParserIsKilled()
    {
        // Без Kill ожидание выхода процесса разбора (он ждёт конца stdin) не закончилось бы — тест упал бы по таймауту.
        int? started = null;

        var error = await Should.ThrowAsync<IngestionException>(() => DocumentTextExtractor.ExtractAsync(
            new HangingStream(), DocumentKind.Text, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(500), id => started = id));

        error.Retryable.ShouldBeFalse();
        error.Message.ShouldBe("The document is taking too long to process.");
        started.ShouldNotBeNull();
        HasExited(started.Value).ShouldBeTrue();
    }

    [Fact(Timeout = 30_000)]
    public async Task Cancellation_KillsTheParser()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int? started = null;

        await Should.ThrowAsync<OperationCanceledException>(() => DocumentTextExtractor.ExtractAsync(
            new HangingStream(), DocumentKind.Text, cts.Token, null, id =>
            {
                started = id;
                cts.CancelAfter(500);
            }));

        started.ShouldNotBeNull();
        HasExited(started.Value).ShouldBeTrue();
    }

    [Fact]
    public async Task Parsers_RunOneAtATime()
    {
        // Каждый новый процесс разбора стартует, только когда все предыдущие уже завершились.
        var startedIds = new List<int>();
        var overlaps = 0;
        void OnStarted(int id)
        {
            lock (startedIds)
            {
                overlaps += startedIds.Count(previous => !HasExited(previous));
                startedIds.Add(id);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            DocumentTextExtractor.ExtractAsync(Fixture("sample.pdf"), DocumentKind.Pdf, Ct, null, OnStarted)));

        startedIds.Count.ShouldBe(3);
        overlaps.ShouldBe(0);
    }

    [Fact]
    public async Task PdfDecompressionBomb_FailsInsteadOfExhaustingMemory()
    {
        // ~1 МБ файла распаковывается в 1 ГБ — больше предела кучи процесса разбора. PdfPig сам ловит нехватку памяти
        // и считает страницу пустой, поэтому исход — «нет текста» или «не удалось извлечь». Главное: распаковка идёт не
        // в процессе приложения (при разборе внутри него выделялось больше 1 ГБ).
        using var bomb = PdfWithFlateContent(1L << 30);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

        var error = await Should.ThrowAsync<IngestionException>(() => DocumentTextExtractor.ExtractAsync(bomb, DocumentKind.Pdf, Ct));

        (GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore).ShouldBeLessThan(300L * 1024 * 1024);
        error.Retryable.ShouldBeFalse();
        error.Message.ShouldBeOneOf("Could not extract text from the document.", "No text found in the document (it may be a scan).");
    }

    [Fact]
    public void Parser_RunsAsSeparateProcessWithBoundedHeap()
    {
        var info = DocumentTextExtractor.ParserStartInfo(DocumentKind.Pdf);

        info.ArgumentList.ShouldBe([typeof(DocumentTextExtractor).Assembly.Location, DocumentTextExtractor.ChildCommand, "Pdf"]);
        Convert.ToInt64(info.Environment["DOTNET_GCHeapHardLimit"], 16).ShouldBe(DocumentTextExtractor.ParserHeapLimitBytes);
        // Секреты приложения (Bot__Token, строки подключения) процессу разбора не передаются.
        info.Environment.Keys
            .Where(key => !DocumentTextExtractor.ParserEnvironment.Contains(key) && !key.StartsWith("DOTNET_gc", StringComparison.OrdinalIgnoreCase)
                          && key != "DOTNET_GCHeapHardLimit")
            .ShouldBeEmpty();
        Environment.GetEnvironmentVariables().Count.ShouldBeGreaterThan(info.Environment.Count);
    }

    /// <summary>Одностраничный PDF, содержимое страницы — поток FlateDecode из <paramref name="decodedBytes"/> нулевых байт.</summary>
    internal static MemoryStream PdfWithFlateContent(long decodedBytes)
    {
        var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            var chunk = new byte[1 << 20];
            for (long written = 0; written < decodedBytes; written += chunk.Length)
            {
                zlib.Write(chunk, 0, (int)Math.Min(chunk.Length, decodedBytes - written));
            }
        }

        var pdf = new MemoryStream();
        var offsets = new List<long>();
        void Write(string text) => pdf.Write(System.Text.Encoding.ASCII.GetBytes(text));
        void Object(string body)
        {
            offsets.Add(pdf.Position);
            Write($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        Write("%PDF-1.4\n");
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        Object("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>");
        offsets.Add(pdf.Position);
        Write($"4 0 obj\n<< /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n");
        compressed.Position = 0;
        compressed.CopyTo(pdf);
        Write("\nendstream\nendobj\n");

        var xref = pdf.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }

        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        pdf.Position = 0;
        return pdf;
    }

    private static MemoryStream DocxWith(Action<WordprocessingDocument> build)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true))
        {
            build(document);
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task CompressionBombDocx_IsRejectedBeforeParsing()
    {
        using var bomb = ZipLimitsTests.CompressionBombDocx();

        var error = await Should.ThrowAsync<IngestionException>(() => DocumentTextExtractor.ExtractAsync(bomb, DocumentKind.Docx, Ct));

        error.Message.ShouldContain("unpacked");
        error.Retryable.ShouldBeFalse();
    }

    [Fact]
    public async Task Docx_WithoutMainPart_HasNoText()
    {
        using var docx = DocxWith(_ => { });

        await Should.ThrowAsync<IngestionException>(() => DocumentTextExtractor.ExtractAsync(docx, DocumentKind.Docx, Ct));
    }

    [Fact]
    public async Task Docx_WithoutBody_HasNoText()
    {
        using var docx = DocxWith(d => d.AddMainDocumentPart().Document = new Document());

        var error = await Should.ThrowAsync<IngestionException>(() => DocumentTextExtractor.ExtractAsync(docx, DocumentKind.Docx, Ct));

        error.Message.ShouldContain("No text");
    }

    [Fact]
    public async Task Docx_MainPartWithoutDocument_HasNoText()
    {
        using var docx = DocxWith(d => d.AddMainDocumentPart());

        await Should.ThrowAsync<IngestionException>(() => DocumentTextExtractor.ExtractAsync(docx, DocumentKind.Docx, Ct));
    }
}
