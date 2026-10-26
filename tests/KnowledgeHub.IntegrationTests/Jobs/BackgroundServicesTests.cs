using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeHub.IntegrationTests.Jobs;

/// <summary>Фоновые службы в «боевом» режиме (<c>Jobs:Enabled=true</c>): цикл воркера и планировщик периодических задач.</summary>
public sealed class BackgroundServicesTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Системные часы, у которых заданное число вызовов <c>GetUtcNow</c> падает: так проверяется устойчивость цикла к сбою очереди.</summary>
    private sealed class FlakyClock : TimeProvider
    {
        private int _failures;

        public int PendingFailures => Volatile.Read(ref _failures);

        public void FailNext(int count) => Volatile.Write(ref _failures, count);

        public override DateTimeOffset GetUtcNow() =>
            Interlocked.Decrement(ref _failures) >= 0 ? throw new InvalidOperationException("часы недоступны") : base.GetUtcNow();
    }

    private WebApplicationFactory<Program> RunningHost(TimeProvider time, JobProbe probe) =>
        Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Jobs:Enabled", "true");
            b.UseSetting("Jobs:PollInterval", "00:00:00.050");
            b.ConfigureTestServices(services =>
            {
                services.AddSingleton(time);
                services.AddSingleton(probe);
                services.AddJobHandler<TestJobHandler, TestPayload>();
                services.AddRecurringTask<CountingRecurringTask>();
            });
        });

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "условие не выполнилось вовремя");
            await Task.Delay(25, Ct);
        }
    }

    private static async Task EnqueueAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<Api.Common.SystemContext>().Enter();
        await scope.ServiceProvider.GetRequiredService<JobQueue>().EnqueueAsync(TestJobHandler.JobType, new TestPayload("live"), ct: Ct);
    }

    private static async Task<List<Job>> JobsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<Api.Common.SystemContext>().Enter();
        return await scope.ServiceProvider.GetRequiredService<Api.Infrastructure.Persistence.AppDbContext>().Jobs.AsNoTracking().ToListAsync(Ct);
    }

    [Fact]
    public async Task Worker_PicksUpAndCompletesQueuedJobs()
    {
        var probe = new JobProbe();
        using var host = RunningHost(TimeProvider.System, probe);
        _ = host.Server;

        await EnqueueAsync(host.Services);

        await WaitUntilAsync(async () => (await JobsAsync(host.Services)).Any(j => j.Status == JobStatus.Completed));
    }

    [Fact]
    public async Task Worker_SurvivesQueueFailureAndKeepsWorking()
    {
        var clock = new FlakyClock();
        var probe = new JobProbe();
        using var host = RunningHost(clock, probe);
        _ = host.Server;

        // Ближайший опрос очереди упадёт; воркер должен записать ошибку и продолжить.
        clock.FailNext(1);
        await WaitUntilAsync(() => Task.FromResult(clock.PendingFailures <= 0));

        await EnqueueAsync(host.Services);
        await WaitUntilAsync(async () => (await JobsAsync(host.Services)).Any(j => j.Status == JobStatus.Completed));
    }

    [Fact]
    public async Task Scheduler_RunsPeriodicTasksOnTheirInterval()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var probe = new JobProbe();
        using var host = RunningHost(time, probe);
        _ = host.Server;

        // Таймеры создаются асинхронно после старта хоста, поэтому «тикаем» до первого запуска.
        await WaitUntilAsync(() =>
        {
            time.Advance(TimeSpan.FromMinutes(1));
            return Task.FromResult(probe.RecurringRuns > 0);
        });
    }
}
