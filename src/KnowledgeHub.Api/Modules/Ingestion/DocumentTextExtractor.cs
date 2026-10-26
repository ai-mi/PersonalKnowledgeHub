using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace KnowledgeHub.Api.Modules.Ingestion;

public static class DocumentTextExtractor
{
    public const int MaxPdfPages = 2000;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Достаёт текст документа. PdfPig и OpenXML синхронны и не прерываются <see cref="CancellationToken"/>,
    /// поэтому разбор идёт в пуле потоков с таймаутом (просроченный разбор дорабатывает в фоне, но ответ не задерживает).
    /// Поток должен поддерживать перемотку.
    /// </summary>
    public static async Task<string> ExtractAsync(Stream stream, DocumentKind kind, CancellationToken ct, TimeSpan? timeout = null)
    {
        string text;
        try
        {
            text = await Task.Run(() => Extract(stream, kind), CancellationToken.None).WaitAsync(timeout ?? Timeout, ct);
        }
        catch (TimeoutException)
        {
            throw new IngestionException("Документ обрабатывается слишком долго.", retryable: false);
        }
        catch (Exception e) when (e is not (OperationCanceledException or IngestionException))
        {
            throw new IngestionException("Не удалось извлечь текст из документа.", retryable: false, e);
        }

        text = text.Trim();
        if (text.Length == 0)
        {
            throw new IngestionException("В документе не найден текст (возможно, это скан).", retryable: false);
        }

        return text.Length > ArticleExtractor.MaxBodyLength ? text[..ArticleExtractor.MaxBodyLength] : text;
    }

    private static string Extract(Stream stream, DocumentKind kind)
    {
        stream.Position = 0;
        return kind switch
        {
            DocumentKind.Pdf => ExtractPdf(stream),
            DocumentKind.Docx => ExtractDocx(stream),
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(ReadAll(stream)).TrimStart('﻿'),
        };
    }

    private static string ExtractPdf(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var text = new StringBuilder();
        for (var number = 1; number <= Math.Min(pdf.NumberOfPages, MaxPdfPages); number++)
        {
            text.AppendLine(pdf.GetPage(number).Text);
        }

        return text.ToString();
    }

    private static string ExtractDocx(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var paragraphs = document.MainDocumentPart?.Document?.Body?.Descendants<Paragraph>() ?? [];
        return string.Join('\n', paragraphs.Select(p => p.InnerText).Where(t => t.Length > 0));
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
