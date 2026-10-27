using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Reminders;

/// <summary>Раз в минуту ставит в очередь отправку напоминаний, срок которых наступил.</summary>
public sealed class DueRemindersTask(AppDbContext db, IJobQueue queue, TimeProvider timeProvider) : IRecurringTask
{
    private const int BatchSize = 500;

    public string Name => "reminders-due";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var due = await db.Reminders.AsNoTracking()
            .Where(r => r.RemindAt <= now && r.SentAt == null)
            .OrderBy(r => r.RemindAt)
            .Select(r => new { r.Id, r.UserId })
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var reminder in due)
        {
            // Тот же dedupKey на каждом тике: пока задача не отправлена, вторая не появится.
            await queue.EnqueueAsync(SendReminderJob.JobType, new SendReminderPayload(reminder.Id), reminder.UserId,
                dedupKey: $"reminder:{reminder.Id}", ct: ct);
        }
    }
}
