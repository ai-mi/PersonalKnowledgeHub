using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;

namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>Payload задачи <c>ingest</c>. Публичный контракт: задачу ставит модуль Items.</summary>
public sealed record IngestItemPayload(Guid ItemId);

/// <summary>Скачивает страницу ссылки, извлекает статью и обновляет запись (и, через неё, поисковый индекс).</summary>
public sealed class IngestItemJob(
    IItemService items, SafeHttpFetcher fetcher, IIngestionCompletedHandler completed) : IJobHandler<IngestItemPayload>
{
    public const string JobType = "ingest";
    public static string Type => JobType;

    public async Task HandleAsync(IngestItemPayload payload, CancellationToken ct)
    {
        // Запись могли удалить, пока задача ждала в очереди.
        if (await items.GetAsync(payload.ItemId, ct) is not { Url: { } url } item)
        {
            return;
        }

        await items.SetProcessingResultAsync(item.Id, ItemStatus.Processing, null, item.Body, null, ct);

        ExtractedArticle article;
        try
        {
            var page = await fetcher.FetchAsync(new Uri(url), ct);
            article = ArticleExtractor.Extract(page.FinalUrl, page.Html);
        }
        catch (IngestionException e) when (!e.Retryable)
        {
            await FailAsync(item, e.Message, ct);
            return;
        }
        catch (Exception e) when (e is not (OperationCanceledException or IngestionException))
        {
            // Внутренние детали (стек, тексты библиотек) пользователю не показываем.
            throw new IngestionException("Не удалось обработать страницу.", retryable: true, e);
        }

        // Заголовок, заданный пользователем, не перезаписываем.
        var title = string.IsNullOrWhiteSpace(item.Title) ? article.Title : null;
        await items.SetProcessingResultAsync(item.Id, ItemStatus.Ready, title, article.Body, null, ct);
        await completed.OnCompletedAsync(item.Id, ItemStatus.Ready, null, ct);
    }

    private async Task FailAsync(Item item, string error, CancellationToken ct)
    {
        await items.SetProcessingResultAsync(item.Id, ItemStatus.Failed, null, item.Body, error, ct);
        await completed.OnCompletedAsync(item.Id, ItemStatus.Failed, error, ct);
    }
}
