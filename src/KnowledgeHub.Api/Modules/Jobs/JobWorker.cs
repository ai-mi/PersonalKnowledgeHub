using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Modules.Jobs;

public sealed partial class JobWorker(
    IServiceScopeFactory scopeFactory,
    IEnumerable<JobHandlerRegistration> registrations,
    IOptions<JobOptions> options,
    TimeProvider timeProvider,
    ILogger<JobWorker> logger) : BackgroundService
{
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, JobHandlerRegistration> _handlers = registrations.ToDictionary(r => r.Type);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessNextAsync(stoppingToken))
                {
                    continue;
                }

                await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Сбой самой очереди (например, БД недоступна) не должен останавливать воркер.
                LogWorkerError(ex);
                await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
            }
        }
    }

    /// <summary>Захватывает и выполняет одну задачу. <c>false</c> — готовых задач нет.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct = default)
    {
        Job? job;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
            job = await scope.ServiceProvider.GetRequiredService<JobQueue>().ClaimNextAsync(ct);
        }

        if (job is null)
        {
            return false;
        }

        try
        {
            await RunInUserContextAsync(job, async sp =>
            {
                var handler = _handlers.GetValueOrDefault(job.Type)
                    ?? throw new InvalidOperationException($"Для задач типа «{job.Type}» нет обработчика.");
                await handler.Invoke(sp, job.Payload, ct);
            });
            await UpdateAsync(job, JobStatus.Completed, null, null, ct);
        }
        // Остановка приложения не считается неудачей: блокировка истечёт, и задачу подхватят снова.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(job, ex, ct);
        }

        return true;
    }

    private async Task HandleFailureAsync(Job job, Exception ex, CancellationToken ct)
    {
        var lastError = ex.ToString();
        if (lastError.Length > Job.MaxErrorLength)
        {
            lastError = lastError[..Job.MaxErrorLength];
        }

        if (RetryPolicy.NextDelay(job.Attempts) is { } delay)
        {
            await UpdateAsync(job, JobStatus.Pending, timeProvider.GetUtcNow() + delay, lastError, ct);
            return;
        }

        await UpdateAsync(job, JobStatus.Failed, null, lastError, ct);
        LogJobFailed(job.Id, job.Type, job.Attempts, ex);

        try
        {
            await RunInUserContextAsync(job, async sp =>
            {
                foreach (var handler in sp.GetServices<IJobFailureHandler>().Where(h => h.JobType == job.Type))
                {
                    await handler.OnFailedAsync(job, ex.Message, ct);
                }
            });
        }
        catch (Exception handlerEx) when (handlerEx is not OperationCanceledException)
        {
            LogFailureHandlerError(job.Id, job.Type, handlerEx);
        }
    }

    /// <summary>Выполняет действие в новом DI-scope от имени пользователя задачи (или в системном контексте).</summary>
    private async Task RunInUserContextAsync(Job job, Func<IServiceProvider, Task> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemContext>();
        using var _ = job.UserId is { } userId ? context.RunAs(userId) : context.Enter();
        await action(scope.ServiceProvider);
    }

    private async Task UpdateAsync(Job job, JobStatus status, DateTimeOffset? runAfter, string? lastError, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var completedAt = status == JobStatus.Completed ? timeProvider.GetUtcNow() : (DateTimeOffset?)null;
        var newRunAfter = runAfter ?? job.RunAfter;

        await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(
            s => s
                .SetProperty(j => j.Status, status)
                .SetProperty(j => j.RunAfter, newRunAfter)
                .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(j => j.LastError, lastError)
                .SetProperty(j => j.CompletedAt, completedAt),
            ct);
    }

    [LoggerMessage(EventId = 1, EventName = "JobFailed", Level = LogLevel.Error,
        Message = "JobFailed: задача {JobId} типа {JobType} провалена после {Attempts} попыток")]
    private partial void LogJobFailed(Guid jobId, string jobType, int attempts, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Обработчик отказа задачи {JobId} типа {JobType} завершился ошибкой")]
    private partial void LogFailureHandlerError(Guid jobId, string jobType, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Сбой цикла обработки очереди задач")]
    private partial void LogWorkerError(Exception exception);
}
