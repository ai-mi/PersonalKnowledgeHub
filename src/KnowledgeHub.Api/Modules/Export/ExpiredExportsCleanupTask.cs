using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Export;

/// <summary>Раз в сутки удаляет просроченные архивы и их записи.</summary>
public sealed class ExpiredExportsCleanupTask(AppDbContext db, IBlobStore blobs, TimeProvider timeProvider) : IRecurringTask
{
    private const int BatchSize = 100;

    public string Name => "exports-cleanup";
    public TimeSpan Interval => TimeSpan.FromDays(1);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var expired = await db.Exports.Where(e => e.ExpiresAt <= now).OrderBy(e => e.ExpiresAt).Take(BatchSize).ToListAsync(ct);
        foreach (var export in expired)
        {
            // Сначала блоб: если удаление упадёт, запись останется и очистка повторится в следующий раз.
            if (export.BlobPath is { } path)
            {
                await blobs.DeleteAsync(BlobContainers.Exports, path, ct);
            }

            db.Exports.Remove(export);
            await db.SaveChangesAsync(ct);
        }
    }
}
