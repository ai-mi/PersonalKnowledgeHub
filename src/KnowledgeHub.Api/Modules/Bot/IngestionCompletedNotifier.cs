using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Modules.Ingestion;
using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.Api.Modules.Bot;

/// <summary>Сообщает в Telegram об окончании обработки записи, если её прислали боту (в задаче <c>ingest</c> есть чат).</summary>
public sealed class IngestionCompletedNotifier(IItemService items, IBotNotifier notifier, ICurrentUser currentUser) : IIngestionCompletedHandler
{
    public async Task OnCompletedAsync(Guid itemId, ItemStatus status, string? error, long? notifyChatId, CancellationToken ct)
    {
        if (notifyChatId is null)
        {
            return;
        }

        string text;
        if (status == ItemStatus.Ready)
        {
            if (await items.GetAsync(itemId, ct) is not { } item)
            {
                return;
            }

            text = BotMessageFormatter.Ready(BotMessageFormatter.DisplayTitle(item.Title, item.Url));
        }
        else
        {
            text = BotMessageFormatter.Failed(error ?? "неизвестная ошибка");
        }

        await notifier.SendAsync(currentUser.RequireUserId(), text, ct: ct);
    }
}
