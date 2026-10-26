using System.Text.Json;
using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace KnowledgeHub.Api.Modules.Jobs;

public sealed class JobQueue(AppDbContext db, TimeProvider timeProvider) : IJobQueue
{
    private const string InsertSql =
        """
        INSERT INTO jobs (id, type, payload, status, attempts, run_after, user_id, dedup_key, created_at)
        VALUES (@id, @type, @payload::jsonb, 'pending', 0, @run_after, @user_id, @dedup_key, @now)
        ON CONFLICT (dedup_key) DO NOTHING
        """;

    public async Task<bool> EnqueueAsync<TPayload>(
        string type, TPayload payload, Guid? userId = null, DateTimeOffset? runAfter = null, string? dedupKey = null,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var rows = await db.Database.ExecuteSqlRawAsync(
            InsertSql,
            [
                new NpgsqlParameter("id", Guid.CreateVersion7()),
                new NpgsqlParameter("type", type),
                new NpgsqlParameter("payload", NpgsqlDbType.Text) { Value = JsonSerializer.Serialize(payload) },
                new NpgsqlParameter("run_after", runAfter ?? now),
                new NpgsqlParameter("user_id", NpgsqlDbType.Uuid) { Value = (object?)userId ?? DBNull.Value },
                new NpgsqlParameter("dedup_key", NpgsqlDbType.Varchar) { Value = (object?)dedupKey ?? DBNull.Value },
                new NpgsqlParameter("now", now),
            ],
            ct);
        return rows > 0;
    }

    /// <summary>
    /// Захватывает следующую задачу: готовую к запуску или зависшую (истёк <c>locked_until</c>).
    /// <c>FOR UPDATE SKIP LOCKED</c> гарантирует, что параллельные воркеры не получат одну задачу.
    /// </summary>
    public async Task<Job?> ClaimNextAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var lockedUntil = now + JobWorker.LockDuration;
        var claimed = await db.Jobs.FromSql(
            $"""
            UPDATE jobs SET status = 'processing', locked_until = {lockedUntil}, attempts = attempts + 1
            WHERE id = (
                SELECT id FROM jobs
                WHERE (status = 'pending' AND run_after <= {now}) OR (status = 'processing' AND locked_until < {now})
                ORDER BY run_after
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING *
            """).AsNoTracking().ToListAsync(ct);
        return claimed.SingleOrDefault();
    }
}
