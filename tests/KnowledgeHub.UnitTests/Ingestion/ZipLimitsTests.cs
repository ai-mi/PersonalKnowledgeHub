using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KnowledgeHub.Api.Modules.Ingestion;

namespace KnowledgeHub.UnitTests.Ingestion;

public sealed class ZipLimitsTests
{
    private static MemoryStream Fixture(string name) =>
        new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Ingestion", "Fixtures", name)));

    /// <summary>
    /// Настоящий DOCX, чей document.xml сжимается примерно в тысячу раз: уменьшенная копия zip-бомбы,
    /// где файл в несколько мегабайт распаковывается в гигабайты.
    /// </summary>
    public static MemoryStream CompressionBombDocx()
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true))
        {
            document.AddMainDocumentPart().Document = new Document(new Body(new Paragraph(new Run(new Text(new string('a', 3_000_000))))));
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void OrdinaryDocx_IsWithinLimits() =>
        ZipLimits.IsWithinLimits(Fixture("sample.docx")).ShouldBeTrue();

    [Fact]
    public void HighlyCompressedDocx_ExceedsTheRatio()
    {
        using var bomb = CompressionBombDocx();

        ZipLimits.IsWithinLimits(bomb).ShouldBeFalse();
    }

    [Fact]
    public void UnpackedSize_IsLimited()
    {
        // Обычный документ, но предел распакованного размера меньше его содержимого.
        ZipLimits.IsWithinLimits(Fixture("sample.docx"), maxUncompressedBytes: 100).ShouldBeFalse();
    }

    [Fact]
    public void NotAZip_IsNotWithinLimits() =>
        ZipLimits.IsWithinLimits(new MemoryStream(Encoding.UTF8.GetBytes("PK not really a zip"))).ShouldBeFalse();

    [Fact]
    public void Check_LeavesTheStreamUsable()
    {
        using var docx = Fixture("sample.docx");

        ZipLimits.IsWithinLimits(docx).ShouldBeTrue();

        FileSignature.Detect(docx, "notes.docx").ShouldBe(DocumentKind.Docx);
    }
}
