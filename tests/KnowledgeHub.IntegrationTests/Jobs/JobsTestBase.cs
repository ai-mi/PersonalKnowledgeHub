using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeHub.IntegrationTests.Jobs;

public sealed record TestPayload(string Value);

/// <summary>Позволяет тесту подменять поведение тестового обработчика и смотреть, что он получил.</summary>
public sealed class JobProbe
{
    public Func<TestPayload, IServiceProvider, Task> OnHandle { get; set; } = (_, _) => Task.CompletedTask;
    public List<string> Failures { get; } = [];
    public bool ThrowInFailureHandler { get; set; }
    public int RecurringRuns { get; set; }
}

public sealed class TestJobHandler(JobProbe probe, IServiceProvider services) : IJobHandler<TestPayload>
{
    public const string JobType = "test";
    public static string Type => JobType;

    public Task HandleAsync(TestPayload payload, CancellationToken ct) => probe.OnHandle(payload, services);
}

public sealed class TestFailureHandler(JobProbe probe) : IJobFailureHandler
{
    public string JobType => TestJobHandler.JobType;

    public Task OnFailedAsync(Job job, string error, CancellationToken ct)
    {
        probe.Failures.Add(error);
        return probe.ThrowInFailureHandler ? throw new InvalidOperationException("обработчик отказа упал") : Task.CompletedTask;
    }
}

public sealed class ThrowingRecurringTask : IRecurringTask
{
    public string Name => "throwing";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);
    public Task ExecuteAsync(CancellationToken ct) => throw new InvalidOperationException("boom");
}

public sealed class CountingRecurringTask(JobProbe probe) : IRecurringTask
{
    public string Name => "counting";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public Task ExecuteAsync(CancellationToken ct)
    {
        probe.RecurringRuns++;
        return Task.CompletedTask;
    }
}

/// <summary>Хост с управляемым временем, тестовым обработчиком задач и выключенным фоновым воркером.</summary>
public abstract class JobsTestBase : IntegrationTest, IDisposable
{
    private readonly WebApplicationFactory<Program> _jobsFactory;

    protected JobsTestBase(ApiFactory factory) : base(factory)
    {
        Time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Probe = new JobProbe();
        _jobsFactory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton(Probe);
            services.AddJobHandler<TestJobHandler, TestPayload>();
            services.AddJobFailureHandler<TestFailureHandler>();
            services.AddRecurringTask<ThrowingRecurringTask>();
            services.AddRecurringTask<CountingRecurringTask>();
        }));
    }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected FakeTimeProvider Time { get; }
    protected JobProbe Probe { get; }
    protected IServiceProvider Services => _jobsFactory.Services;
    protected JobWorker Worker => Services.GetRequiredService<JobWorker>();

    /// <summary>Выполняет действие в новом scope в системном контексте.</summary>
    protected async Task<T> WithDbAsync<T>(Func<AppDbContext, JobQueue, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<JobQueue>());
    }

    protected Task<bool> EnqueueAsync(Guid? userId = null, DateTimeOffset? runAfter = null, string? dedupKey = null, string value = "v") =>
        WithDbAsync((_, queue) => queue.EnqueueAsync(TestJobHandler.JobType, new TestPayload(value), userId, runAfter, dedupKey, Ct));

    /// <summary>Задачи ставились по реальным часам хоста запроса, а воркер живёт на FakeTimeProvider — подтягиваем его вперёд.</summary>
    protected async Task<bool> ProcessAsync()
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        if (Time.GetUtcNow() < now)
        {
            Time.SetUtcNow(now);
        }

        return await Worker.ProcessNextAsync(Ct);
    }

    protected Task<List<Job>> AllJobsAsync() =>
        WithDbAsync((db, _) => db.Jobs.AsNoTracking().OrderBy(j => j.CreatedAt).ToListAsync(Ct));

    public void Dispose() => _jobsFactory.Dispose();
}
