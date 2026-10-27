using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Items;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Reminders;

public sealed record DigestReminder(Guid ItemId, string Title, DateTime LocalRemindAt);

/// <summary>Содержимое дайджеста; <see cref="DigestBuilder.BuildAsync"/> возвращает его только непустым.</summary>
public sealed record Digest(IReadOnlyList<ItemSummary> Unread, IReadOnlyList<ItemSummary> YearAgo, IReadOnlyList<DigestReminder> Reminders);

/// <summary>Собирает дайджест текущего пользователя. Записи берёт только через <see cref="IItemService"/>.</summary>
public sealed class DigestBuilder(AppDbContext db, IItemService items, TimeProvider timeProvider)
{
    public const int MaxUnread = 10;
    public const int MaxYearAgo = 10;
    public const int MaxReminders = 10;
    public static readonly TimeSpan UnreadMinAge = TimeSpan.FromDays(3);
    public static readonly TimeSpan YearAgoTolerance = TimeSpan.FromDays(3);
    public static readonly TimeSpan ReminderHorizon = TimeSpan.FromDays(7);

    /// <returns><c>null</c> — отправлять нечего.</returns>
    public async Task<Digest?> BuildAsync(TimeZoneInfo zone, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var unread = await items.ListUnreadCreatedBeforeAsync(now - UnreadMinAge, MaxUnread, ct);

        var yearAgo = now.AddYears(-1);
        var yearAgoItems = await items.ListCreatedBetweenAsync(yearAgo - YearAgoTolerance, yearAgo + YearAgoTolerance, MaxYearAgo, ct);

        var reminders = await UpcomingRemindersAsync(now, zone, ct);

        return unread.Count + yearAgoItems.Count + reminders.Count == 0 ? null : new Digest(unread, yearAgoItems, reminders);
    }

    private async Task<List<DigestReminder>> UpcomingRemindersAsync(DateTimeOffset now, TimeZoneInfo zone, CancellationToken ct)
    {
        var upcoming = await db.Reminders.AsNoTracking()
            .Where(r => r.SentAt == null && r.RemindAt > now && r.RemindAt <= now + ReminderHorizon)
            .OrderBy(r => r.RemindAt)
            .Take(MaxReminders)
            .ToListAsync(ct);

        var result = new List<DigestReminder>(upcoming.Count);
        foreach (var reminder in upcoming)
        {
            if (await items.GetAsync(reminder.ItemId, ct) is { } item)
            {
                result.Add(new DigestReminder(
                    item.Id, BotMessageFormatter.DisplayTitle(item.Title, item.Url), TimeZoneInfo.ConvertTimeFromUtc(reminder.RemindAt.UtcDateTime, zone)));
            }
        }

        return result;
    }
}
