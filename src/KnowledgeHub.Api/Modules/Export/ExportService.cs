using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeHub.Api.Modules.Export;

public sealed record ExportResponse(Guid Id, ExportStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt)
{
    public static ExportResponse From(ExportRecord e) => new(e.Id, e.Status, e.CreatedAt, e.ExpiresAt);
}

/// <summary>Файл архива для скачивания.</summary>
public sealed record ExportDownload(Stream Content, string FileName);

public sealed class ExportService(AppDbContext db, IJobQueue jobs, IBlobStore blobs, TimeProvider timeProvider)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private static readonly ExportStatus[] ActiveStatuses = [ExportStatus.Pending, ExportStatus.Processing];

    /// <summary>Создаёт запрос и ставит задачу сборки. Второй экспорт, пока идёт первый, → <c>409</c>.</summary>
    public async Task<ExportResponse> StartAsync(CancellationToken ct)
    {
        if (await db.Exports.AnyAsync(e => ActiveStatuses.Contains(e.Status), ct))
        {
            throw ActiveExportConflict();
        }

        var export = new ExportRecord { Id = Guid.CreateVersion7(), Status = ExportStatus.Pending, CreatedAt = timeProvider.GetUtcNow() };
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Exports.Add(export);
            await db.SaveChangesAsync(ct);
            await jobs.EnqueueAsync(BuildExportJob.JobType, new BuildExportPayload(export.Id), export.UserId, dedupKey: $"export:{export.Id}", ct: ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                          && pg.ConstraintName == ExportConfiguration.ActiveIndexName)
        {
            // Параллельный запрос успел раньше проверки выше.
            throw ActiveExportConflict();
        }

        return ExportResponse.From(export);
    }

    public async Task<List<ExportResponse>> ListAsync(CancellationToken ct) =>
        (await db.Exports.AsNoTracking().OrderByDescending(e => e.CreatedAt).ToListAsync(ct)).ConvertAll(ExportResponse.From);

    /// <summary><c>null</c> — экспорта нет, архив просрочен или удалён. Экспорт, который ещё не готов → <c>409</c>.</summary>
    public async Task<ExportDownload?> OpenDownloadAsync(Guid id, CancellationToken ct)
    {
        if (await db.Exports.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct) is not { } export)
        {
            return null;
        }

        if (export.Status != ExportStatus.Ready)
        {
            throw Errors.Conflict("The export is not ready.");
        }

        if (export.ExpiresAt <= timeProvider.GetUtcNow()
            || export.BlobPath is not { } path
            || await blobs.OpenReadAsync(BlobContainers.Exports, path, ct) is not { } stream)
        {
            return null;
        }

        return new ExportDownload(stream, $"knowledgehub-export-{export.CreatedAt:yyyy-MM-dd}.zip");
    }

    private static AppException ActiveExportConflict() => Errors.Conflict("An export is already in progress.");
}
