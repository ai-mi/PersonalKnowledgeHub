using KnowledgeHub.Api.Common;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Modules.Jobs;

/// <summary>Запускает <see cref="IRecurringTask"/> с их интервалами; сбой одной задачи не останавливает остальные.</summary>
public sealed partial class RecurringScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<JobOptions> options,
    TimeProvider timeProvider,
    ILogger<RecurringScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        List<(Type Type, string Name, TimeSpan Interval)> tasks;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            tasks = scope.ServiceProvider.GetServices<IRecurringTask>().Select(t => (t.GetType(), t.Name, t.Interval)).ToList();
        }

        await Task.WhenAll(tasks.Select(t => RunLoopAsync(t.Type, t.Name, t.Interval, stoppingToken)));
    }

    private async Task RunLoopAsync(Type taskType, string name, TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await ExecuteOnceAsync(taskType, name, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    /// <summary>Один запуск задачи в новом scope и системном контексте. Исключения логируются и не пробрасываются.</summary>
    public async Task ExecuteOnceAsync(Type taskType, string name, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
            var task = scope.ServiceProvider.GetServices<IRecurringTask>().Single(t => t.GetType() == taskType);
            await task.ExecuteAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTaskFailed(name, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Периодическая задача {TaskName} завершилась ошибкой")]
    private partial void LogTaskFailed(string taskName, Exception exception);
}
