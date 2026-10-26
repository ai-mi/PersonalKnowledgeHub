using System.Text.Json;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;

namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>Задача <c>ingest</c> исчерпала попытки: запись получает статус <c>failed</c> и понятный текст ошибки.</summary>
public sealed class IngestFailureHandler(IItemService items, IIngestionCompletedHandler completed) : IJobFailureHandler
{
    public string JobType => IngestItemJob.JobType;

    public async Task OnFailedAsync(Job job, string error, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<IngestItemPayload>(job.Payload);
        if (payload is null || await items.GetAsync(payload.ItemId, ct) is not { } item)
        {
            return;
        }

        await items.SetProcessingResultAsync(item.Id, ItemStatus.Failed, null, item.Body, error, ct);
        await completed.OnCompletedAsync(item.Id, ItemStatus.Failed, error, ct);
    }
}
