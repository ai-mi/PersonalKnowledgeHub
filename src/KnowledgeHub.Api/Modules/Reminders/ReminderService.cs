using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Items;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Reminders;

public sealed record CreateReminderRequest(Guid ItemId, DateTimeOffset RemindAt, NotificationChannel Channel);

public sealed record ReminderResponse(Guid Id, Guid ItemId, DateTimeOffset RemindAt, NotificationChannel Channel, DateTimeOffset? SentAt)
{
    public static ReminderResponse From(Reminder r) => new(r.Id, r.ItemId, r.RemindAt, r.Channel, r.SentAt);
}

public sealed class ReminderService(AppDbContext db, IItemService items, TimeProvider timeProvider)
{
    /// <summary><c>null</c> — записи нет (или она чужая).</summary>
    public async Task<ReminderResponse?> CreateAsync(CreateReminderRequest request, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        if (request.RemindAt <= now)
        {
            throw Errors.Validation("remindAt", "Reminder time must be in the future.");
        }

        if (!Enum.IsDefined(request.Channel))
        {
            throw Errors.Validation("channel", "Allowed channels: telegram, email, both.");
        }

        if (await items.GetAsync(request.ItemId, ct) is null)
        {
            return null;
        }

        var reminder = new Reminder
        {
            Id = Guid.CreateVersion7(),
            ItemId = request.ItemId,
            RemindAt = request.RemindAt,
            Channel = request.Channel,
            CreatedAt = now,
        };
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(ct);
        return ReminderResponse.From(reminder);
    }

    /// <param name="upcoming"><c>true</c> — только неотправленные, ближайшие первыми; иначе все, новые первыми.</param>
    public async Task<List<ReminderResponse>> ListAsync(bool upcoming, CancellationToken ct)
    {
        var query = db.Reminders.AsNoTracking();
        var ordered = upcoming
            ? query.Where(r => r.SentAt == null).OrderBy(r => r.RemindAt)
            : query.OrderByDescending(r => r.RemindAt);
        return (await ordered.ToListAsync(ct)).ConvertAll(ReminderResponse.From);
    }

    /// <summary><c>null</c> — записи нет (или она чужая).</summary>
    public async Task<List<ReminderResponse>?> ListForItemAsync(Guid itemId, CancellationToken ct)
    {
        if (await items.GetAsync(itemId, ct) is null)
        {
            return null;
        }

        var reminders = await db.Reminders.AsNoTracking().Where(r => r.ItemId == itemId).OrderBy(r => r.RemindAt).ToListAsync(ct);
        return reminders.ConvertAll(ReminderResponse.From);
    }

    /// <summary><c>false</c> — напоминания нет.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct) =>
        await db.Reminders.Where(r => r.Id == id).ExecuteDeleteAsync(ct) > 0;
}
