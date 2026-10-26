using System.Text;
using DocumentFormat.OpenXml.Packaging;

namespace KnowledgeHub.Api.Modules.Ingestion;

public enum DocumentKind
{
    Pdf,
    Docx,
    Text,
}

/// <summary>Определяет тип документа по содержимому, а не по заявленному имени и Content-Type.</summary>
public static class FileSignature
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".txt"] = "text/plain; charset=utf-8",
        [".md"] = "text/markdown; charset=utf-8",
    };

    /// <summary>
    /// Тип документа или <c>null</c>, если файл не поддерживается. Поток должен поддерживать перемотку:
    /// DOCX проверяется открытием пакета, текст — декодированием всего файла. Позицию потока метод не сохраняет.
    /// </summary>
    public static DocumentKind? Detect(Stream stream, string fileName)
    {
        stream.Position = 0;
        var header = new byte[PdfMagic.Length];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        if (header.AsSpan(0, read).StartsWith(PdfMagic))
        {
            return DocumentKind.Pdf;
        }

        if (header.AsSpan(0, read).StartsWith(ZipMagic))
        {
            return IsWordDocument(stream) ? DocumentKind.Docx : null;
        }

        var extension = Path.GetExtension(fileName);
        return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            ? IsUtf8Text(stream) ? DocumentKind.Text : null
            : null;
    }

    /// <summary>Расширение, под которым файл хранится для типа; у текста сохраняется заявленное (<c>.txt</c> или <c>.md</c>).</summary>
    public static string? CanonicalExtension(DocumentKind kind) => kind switch
    {
        DocumentKind.Pdf => ".pdf",
        DocumentKind.Docx => ".docx",
        _ => null,
    };

    /// <summary>Content-Type для скачивания: только из белого списка по расширению, иначе <c>application/octet-stream</c>.</summary>
    public static string ContentTypeFor(string fileName) =>
        ContentTypes.GetValueOrDefault(Path.GetExtension(fileName), "application/octet-stream");

    private static bool IsWordDocument(Stream stream)
    {
        try
        {
            stream.Position = 0;
            using var document = WordprocessingDocument.Open(stream, isEditable: false);
            return document.MainDocumentPart?.Document is not null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Битый или чужой zip-архив: библиотека бросает разные типы исключений.
            return false;
        }
    }

    private static bool IsUtf8Text(Stream stream)
    {
        stream.Position = 0;
        var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetDecoder();
        var bytes = new byte[81920];
        var chars = new char[bytes.Length];
        try
        {
            int count;
            while ((count = stream.Read(bytes, 0, bytes.Length)) > 0)
            {
                var decoded = decoder.GetChars(bytes, 0, count, chars, 0, flush: false);
                if (chars.AsSpan(0, decoded).Contains('\0'))
                {
                    return false;
                }
            }

            decoder.GetChars([], 0, 0, chars, 0, flush: true);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
