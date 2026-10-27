using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Reminders;

/// <summary>Раз в час ставит в очередь дайджест пользователям, у которых наступило 9:00 в выбранный день.</summary>
public sealed class DigestTask(AppDbContext db, IJobQueue queue, TimeProvider timeProvider) : IRecurringTask
{
    public string Name => "digest-due";
    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var candidates = await db.UserSettings.AsNoTracking()
            .Where(s => s.DigestFrequency != DigestFrequency.Off && s.DigestChannels.Length > 0)
            .ToListAsync(ct);

        foreach (var settings in candidates)
        {
            if (DigestSchedule.IsDue(settings, now, out var localDate))
            {
                // Ключ с локальной датой: час, пропущенный после рестарта, не теряет дайджест, а повторный тик не создаёт второй.
                await queue.EnqueueAsync(SendDigestJob.JobType, new SendDigestPayload(localDate), settings.UserId,
                    dedupKey: $"digest:{settings.UserId}:{localDate:yyyy-MM-dd}", ct: ct);
            }
        }
    }
}
