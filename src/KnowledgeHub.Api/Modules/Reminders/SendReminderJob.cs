using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Modules.Reminders;

public sealed record SendReminderPayload(Guid ReminderId);

/// <summary>Отправляет одно напоминание от имени его владельца (задача ставится с <c>userId</c>).</summary>
public sealed class SendReminderJob(
    AppDbContext db, IItemService items, INotificationDispatcher dispatcher, TimeProvider timeProvider, IOptions<BotOptions> botOptions)
    : IJobHandler<SendReminderPayload>
{
    public const string JobType = "reminder.send";
    public static string Type => JobType;

    public async Task HandleAsync(SendReminderPayload payload, CancellationToken ct)
    {
        if (await db.Reminders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == payload.ReminderId && r.SentAt == null, ct) is not { } reminder)
        {
            return;
        }

        var item = await items.GetAsync(reminder.ItemId, ct);

        // Отметка идёт до отправки: «ровно один раз» важнее гарантии доставки (повтор задачи после сбоя дал бы дубль в Telegram).
        // Условный UPDATE заодно защищает от двух одновременных исполнителей.
        var now = timeProvider.GetUtcNow();
        if (await db.Reminders.Where(r => r.Id == reminder.Id && r.SentAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.SentAt, now), ct) == 0 || item is null)
        {
            return;
        }

        await dispatcher.SendAsync(reminder.UserId, reminder.Channel, BuildMessage(item, botOptions.Value.WebBaseUrl), ct);
    }

    private static NotificationMessage BuildMessage(Item item, string webBaseUrl)
    {
        var title = BotMessageFormatter.DisplayTitle(item.Title, item.Url);
        var heading = $"Напоминание: {title}";
        var html = BotMessageFormatter.Escape(heading);
        var text = heading;
        if (!string.IsNullOrWhiteSpace(webBaseUrl))
        {
            var link = $"{webBaseUrl.TrimEnd('/')}/items/{item.Id}";
            html += $"\n<a href=\"{BotMessageFormatter.Escape(link)}\">Открыть запись</a>";
            text += $"\n{link}";
        }

        return new NotificationMessage(heading, html, text, [BotMessageFormatter.ReadButton(item.Id)]);
    }
}
