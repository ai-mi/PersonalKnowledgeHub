using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;

namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>Payload задачи <c>ingest</c>. Публичный контракт: задачу ставит модуль Items.</summary>
public sealed record IngestItemPayload(Guid ItemId, long? NotifyChatId = null);

/// <summary>
/// Извлекает содержимое записи и обновляет её (и, через неё, поисковый индекс): ссылку скачивает и разбирает как статью,
/// документ читает из хранилища.
/// </summary>
public sealed class IngestItemJob(
    IItemService items, SafeHttpFetcher fetcher, IBlobStore blobs, IIngestionCompletedHandler completed) : IJobHandler<IngestItemPayload>
{
    public const string JobType = "ingest";
    public static string Type => JobType;

    public async Task HandleAsync(IngestItemPayload payload, CancellationToken ct)
    {
        // Запись могли удалить, пока задача ждала в очереди.
        if (await items.GetAsync(payload.ItemId, ct) is not { } item || (item.Url ?? item.BlobPath) is null)
        {
            return;
        }

        await items.SetProcessingResultAsync(item.Id, ItemStatus.Processing, null, item.Body, null, ct);

        ExtractedArticle extracted;
        try
        {
            extracted = item.Type == ItemType.Document ? await ExtractDocumentAsync(item, ct) : await ExtractLinkAsync(item, ct);
        }
        catch (IngestionException e) when (!e.Retryable)
        {
            await FailAsync(item, e.Message, payload.NotifyChatId, ct);
            return;
        }
        catch (Exception e) when (e is not (OperationCanceledException or IngestionException))
        {
            // Внутренние детали (стек, тексты библиотек) пользователю не показываем.
            throw new IngestionException("Не удалось обработать запись.", retryable: true, e);
        }

        // Заголовок, заданный пользователем, не перезаписываем.
        var title = string.IsNullOrWhiteSpace(item.Title) ? extracted.Title : null;
        await items.SetProcessingResultAsync(item.Id, ItemStatus.Ready, title, extracted.Body, null, ct);
        await completed.OnCompletedAsync(item.Id, ItemStatus.Ready, null, payload.NotifyChatId, ct);
    }

    private async Task<ExtractedArticle> ExtractLinkAsync(Item item, CancellationToken ct)
    {
        var page = await fetcher.FetchAsync(new Uri(item.Url!), ct);
        return ArticleExtractor.Extract(page.FinalUrl, page.Html);
    }

    private async Task<ExtractedArticle> ExtractDocumentAsync(Item item, CancellationToken ct)
    {
        var kind = KindOf(item.BlobPath!);
        await using var blob = await blobs.OpenReadAsync(BlobContainers.Documents, item.BlobPath!, ct)
            ?? throw new IngestionException("Файл документа не найден.", retryable: false);

        // Парсеры PDF и DOCX работают с перематываемым потоком; файл не больше лимита загрузки.
        using var content = new MemoryStream();
        await blob.CopyToAsync(content, ct);
        return new ExtractedArticle(null, await DocumentTextExtractor.ExtractAsync(content, kind, ct));
    }

    private static DocumentKind KindOf(string blobPath) => Path.GetExtension(blobPath).ToLowerInvariant() switch
    {
        ".pdf" => DocumentKind.Pdf,
        ".docx" => DocumentKind.Docx,
        _ => DocumentKind.Text,
    };

    private async Task FailAsync(Item item, string error, long? notifyChatId, CancellationToken ct)
    {
        await items.SetProcessingResultAsync(item.Id, ItemStatus.Failed, null, item.Body, error, ct);
        await completed.OnCompletedAsync(item.Id, ItemStatus.Failed, error, notifyChatId, ct);
    }
}
