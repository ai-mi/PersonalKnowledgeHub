using KnowledgeHub.Api.Modules.Ingestion;

namespace KnowledgeHub.UnitTests.Ingestion;

public sealed class StoredNameTests
{
    [Theory]
    [InlineData("report.pdf", DocumentKind.Pdf, "report.pdf")]
    [InlineData("..\\..\\windows\\system32\\report.pdf", DocumentKind.Pdf, "report.pdf")]
    [InlineData("../../etc/passwd.txt", DocumentKind.Text, "passwd.txt")]
    [InlineData("report.docx", DocumentKind.Pdf, "report.pdf")]
    [InlineData("scan", DocumentKind.Pdf, "scan.pdf")]
    [InlineData("  .pdf", DocumentKind.Pdf, "file.pdf")]
    [InlineData("notes.MD", DocumentKind.Text, "notes.MD")]
    [InlineData("отчёт за год.docx", DocumentKind.Docx, "отчёт за год.docx")]
    public void Name_IsStrippedOfPathAndMatchesDetectedType(string fileName, DocumentKind kind, string expected) =>
        DocumentIntake.StoredName(fileName, kind).ShouldBe(expected);

    [Fact]
    public void LongName_IsShortened() =>
        DocumentIntake.StoredName(new string('a', 300) + ".pdf", DocumentKind.Pdf).ShouldBe(new string('a', 100) + ".pdf");

    [Fact]
    public void ControlCharacters_AreRemoved() =>
        DocumentIntake.StoredName("a\r\nb\0.txt", DocumentKind.Text).ShouldBe("ab.txt");
}
