using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.IntegrationTests.Infrastructure;

namespace KnowledgeHub.IntegrationTests.Jobs;

public sealed class JobWorkerEdgeTests(ApiFactory factory) : JobsTestBase(factory)
{
    private async Task<Job> OnlyJobAsync() => (await AllJobsAsync()).Single();

    [Fact]
    public async Task JobOfUnknownType_FailsWithClearMessageAndIsRetried()
    {
        await WithDbAsync((_, queue) => queue.EnqueueAsync("nobody-handles-this", new TestPayload("x"), ct: Ct));

        (await ProcessAsync()).ShouldBeTrue();

        var job = await OnlyJobAsync();
        job.Status.ShouldBe(JobStatus.Pending);
        job.LastError.ShouldNotBeNull().ShouldContain("нет обработчика");
    }

    [Fact]
    public async Task JobWithNullPayload_FailsWithClearMessage()
    {
        await WithDbAsync((_, queue) => queue.EnqueueAsync<TestPayload?>(TestJobHandler.JobType, null, ct: Ct));

        (await ProcessAsync()).ShouldBeTrue();

        (await OnlyJobAsync()).LastError.ShouldNotBeNull().ShouldContain("Пустой payload");
    }

    [Fact]
    public async Task VeryLongError_IsTruncatedToTheColumnLimit()
    {
        Probe.OnHandle = (_, _) => throw new InvalidOperationException(new string('x', Job.MaxErrorLength * 2));
        await EnqueueAsync();

        (await ProcessAsync()).ShouldBeTrue();

        (await OnlyJobAsync()).LastError.ShouldNotBeNull().Length.ShouldBe(Job.MaxErrorLength);
    }

    [Fact]
    public async Task ShutdownDuringJob_IsNotAFailure()
    {
        using var shutdown = new CancellationTokenSource();
        Probe.OnHandle = (_, _) =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        };
        await EnqueueAsync();
        Time.SetUtcNow(DateTimeOffset.UtcNow.AddSeconds(1));

        await Should.ThrowAsync<OperationCanceledException>(() => Worker.ProcessNextAsync(shutdown.Token));

        // Задача осталась «в обработке»: когда истечёт блокировка, её подхватят снова, а попытка не сгорела впустую.
        var job = await OnlyJobAsync();
        job.Status.ShouldBe(JobStatus.Processing);
        Probe.Failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task FailingFailureHandler_DoesNotBreakTheWorker()
    {
        Probe.OnHandle = (_, _) => throw new InvalidOperationException("упало");
        Probe.ThrowInFailureHandler = true;
        await EnqueueAsync();

        foreach (var delayMinutes in new[] { 1, 5, 30 })
        {
            (await Worker.ProcessNextAsync(Ct)).ShouldBeTrue();
            Time.Advance(TimeSpan.FromMinutes(delayMinutes));
        }

        (await Worker.ProcessNextAsync(Ct)).ShouldBeTrue();

        (await OnlyJobAsync()).Status.ShouldBe(JobStatus.Failed);
        Probe.Failures.ShouldBe(["упало"]);
    }
}
