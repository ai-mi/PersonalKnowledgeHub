using System.IO.Compression;
using System.Text;
using KnowledgeHub.Api.Modules.Ingestion;

namespace KnowledgeHub.UnitTests.Ingestion;

public sealed class FileSignatureTests
{
    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static MemoryStream Fixture(string name) =>
        new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Ingestion", "Fixtures", name)));

    [Fact]
    public void Pdf_IsRecognizedByContent() =>
        FileSignature.Detect(Fixture("sample.pdf"), "report.pdf").ShouldBe(DocumentKind.Pdf);

    [Fact]
    public void Docx_IsRecognizedByContent() =>
        FileSignature.Detect(Fixture("sample.docx"), "notes.docx").ShouldBe(DocumentKind.Docx);

    [Fact]
    public void Pdf_IsRecognizedEvenWithWrongExtension() =>
        FileSignature.Detect(Fixture("sample.pdf"), "report.docx").ShouldBe(DocumentKind.Pdf);

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("README.MD")]
    public void Utf8Text_WithTextExtension_IsAccepted(string fileName) =>
        FileSignature.Detect(Fixture("sample.txt"), fileName).ShouldBe(DocumentKind.Text);

    [Fact]
    public void ExecutableRenamedToPdf_IsRejected() =>
        FileSignature.Detect(new MemoryStream([(byte)'M', (byte)'Z', 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]), "invoice.pdf").ShouldBeNull();

    [Fact]
    public void ExecutableRenamedToTxt_IsRejectedByNullByte() =>
        FileSignature.Detect(new MemoryStream([(byte)'M', (byte)'Z', 0x00, 0x03]), "invoice.txt").ShouldBeNull();

    [Fact]
    public void Text_WithUnsupportedExtension_IsRejected() =>
        FileSignature.Detect(Bytes("hello"), "notes.html").ShouldBeNull();

    [Fact]
    public void Text_WithInvalidUtf8_IsRejected() =>
        FileSignature.Detect(new MemoryStream([0xC3, 0x28, 0x41]), "notes.txt").ShouldBeNull();

    [Fact]
    public void EmptyFile_IsRejectedAsPdfButAcceptedAsEmptyText()
    {
        FileSignature.Detect(new MemoryStream(), "a.pdf").ShouldBeNull();
        FileSignature.Detect(new MemoryStream(), "a.txt").ShouldBe(DocumentKind.Text);
    }

    [Fact]
    public void ArbitraryZip_RenamedToDocx_IsRejected()
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("hello.txt").Open());
            writer.Write("hi");
        }

        FileSignature.Detect(zip, "evil.docx").ShouldBeNull();
    }

    [Fact]
    public void TruncatedDocx_IsRejected()
    {
        var bytes = Fixture("sample.docx").ToArray();

        FileSignature.Detect(new MemoryStream(bytes[..(bytes.Length / 2)]), "broken.docx").ShouldBeNull();
    }

    [Theory]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("a.DOCX", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("a.txt", "text/plain; charset=utf-8")]
    [InlineData("a.md", "text/markdown; charset=utf-8")]
    [InlineData("a.html", "application/octet-stream")]
    [InlineData("a.svg", "application/octet-stream")]
    [InlineData("noextension", "application/octet-stream")]
    public void ContentType_IsTakenFromWhitelist(string fileName, string expected) =>
        FileSignature.ContentTypeFor(fileName).ShouldBe(expected);
}
