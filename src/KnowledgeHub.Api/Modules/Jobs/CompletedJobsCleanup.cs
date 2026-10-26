using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Jobs;

/// <summary>Удаляет выполненные задачи старше 7 дней.</summary>
public sealed class CompletedJobsCleanup(AppDbContext db, TimeProvider timeProvider) : IRecurringTask
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public string Name => "jobs-cleanup";
    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var threshold = timeProvider.GetUtcNow() - Retention;
        await db.Jobs.Where(j => j.Status == JobStatus.Completed && j.CompletedAt < threshold).ExecuteDeleteAsync(ct);
    }
}
