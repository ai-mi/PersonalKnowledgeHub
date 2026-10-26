using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.IntegrationTests.Infrastructure;
using KnowledgeHub.IntegrationTests.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.IntegrationTests.Jobs;

public sealed class JobWorkerTests(ApiFactory factory) : JobsTestBase(factory)
{
    [Fact]
    public async Task ProcessNext_NoJobs_ReturnsFalse() =>
        (await Worker.ProcessNextAsync(Ct)).ShouldBeFalse();

    [Fact]
    public async Task ProcessNext_Success_CompletesJobAndPassesPayload()
    {
        string? received = null;
        Probe.OnHandle = (payload, _) =>
        {
            received = payload.Value;
            return Task.CompletedTask;
        };
        await EnqueueAsync(value: "payload-1");

        (await Worker.ProcessNextAsync(Ct)).ShouldBeTrue();

        received.ShouldBe("payload-1");
        var job = (await AllJobsAsync()).Single();
        job.Status.ShouldBe(JobStatus.Completed);
        job.CompletedAt.ShouldNotBeNull();
        job.LockedUntil.ShouldBeNull();
        (await Worker.ProcessNextAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task ProcessNext_Failure_RetriesAfterOneFiveThirtyMinutesThenFails()
    {
        Probe.OnHandle = (_, _) => throw new InvalidOperationException("упало");
        await EnqueueAsync();

        foreach (var (attempt, delayMinutes) in new[] { (1, 1), (2, 5), (3, 30) })
        {
            (await Worker.ProcessNextAsync(Ct)).ShouldBeTrue();
            var pending = (await AllJobsAsync()).Single();
            pending.Status.ShouldBe(JobStatus.Pending);
            pending.Attempts.ShouldBe(attempt);
            pending.RunAfter.ShouldBe(Time.GetUtcNow() + TimeSpan.FromMinutes(delayMinutes), TimeSpan.FromMilliseconds(1));
            pending.LastError.ShouldNotBeNull().ShouldContain("упало");

            // До истечения паузы задачу не берут.
            Time.Advance(TimeSpan.FromMinutes(delayMinutes) - TimeSpan.FromSeconds(1));
            (await Worker.ProcessNextAsync(Ct)).ShouldBeFalse();
            Time.Advance(TimeSpan.FromSeconds(1));
        }

        (await Worker.ProcessNextAsync(Ct)).ShouldBeTrue();

        var failed = (await AllJobsAsync()).Single();
        failed.Status.ShouldBe(JobStatus.Failed);
        failed.Attempts.ShouldBe(4);
        failed.LastError.ShouldNotBeNull().ShouldContain("упало");
        Probe.Failures.ShouldBe(["упало"]);
        Time.Advance(TimeSpan.FromHours(1));
        (await Worker.ProcessNextAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task ProcessNext_FailureHandlerNotCalled_WhileRetriesRemain()
    {
        Probe.OnHandle = (_, _) => throw new InvalidOperationException("упало");
        await EnqueueAsync();

        await Worker.ProcessNextAsync(Ct);

        Probe.Failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handler_SeesOnlyDataOfJobOwner()
    {
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();
        using var clientA = await Factory.CreateClientForAsync(userA);
        using var clientB = await Factory.CreateClientForAsync(userB);
        await ItemEndpointsTests.CreateAsync(clientA, new { type = "note", body = "a" });
        await ItemEndpointsTests.CreateAsync(clientB, new { type = "note", body = "b1" });
        await ItemEndpointsTests.CreateAsync(clientB, new { type = "note", body = "b2" });

        Guid? seenUser = null;
        var seenItems = 0;
        Probe.OnHandle = async (_, sp) =>
        {
            seenUser = sp.GetRequiredService<ICurrentUser>().UserId;
            seenItems = await sp.GetRequiredService<AppDbContext>().Items.CountAsync(Ct);
        };
        await EnqueueAsync(userA);

        await Worker.ProcessNextAsync(Ct);

        seenUser.ShouldBe(userA);
        seenItems.ShouldBe(1);
    }

    [Fact]
    public async Task Handler_OfSystemJob_RunsInSystemContext()
    {
        using var clientA = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        using var clientB = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        await ItemEndpointsTests.CreateAsync(clientA, new { type = "note", body = "a" });
        await ItemEndpointsTests.CreateAsync(clientB, new { type = "note", body = "b" });

        var seenItems = 0;
        var isSystem = false;
        Probe.OnHandle = async (_, sp) =>
        {
            isSystem = sp.GetRequiredService<ICurrentUser>().IsSystem;
            seenItems = await sp.GetRequiredService<AppDbContext>().Items.CountAsync(Ct);
        };
        await EnqueueAsync();

        await Worker.ProcessNextAsync(Ct);

        isSystem.ShouldBeTrue();
        seenItems.ShouldBe(2);
    }

    [Fact]
    public async Task Cleanup_RemovesOnlyCompletedJobsOlderThanSevenDays()
    {
        await EnqueueAsync(value: "old");
        await EnqueueAsync(value: "fresh");
        await Worker.ProcessNextAsync(Ct);
        await Worker.ProcessNextAsync(Ct);
        var now = Time.GetUtcNow();
        await WithDbAsync(async (db, _) =>
        {
            foreach (var job in await db.Jobs.ToListAsync(Ct))
            {
                job.CompletedAt = job.Payload.Contains("old") ? now.AddDays(-8) : now.AddDays(-6);
            }

            return await db.SaveChangesAsync(Ct);
        });

        await Services.GetRequiredService<RecurringScheduler>().ExecuteOnceAsync(typeof(CompletedJobsCleanup), "jobs-cleanup", Ct);

        var left = (await AllJobsAsync()).Single();
        left.Payload.ShouldContain("fresh");
    }

    [Fact]
    public async Task Scheduler_FailingTask_DoesNotStopOthers()
    {
        var scheduler = Services.GetRequiredService<RecurringScheduler>();

        await scheduler.ExecuteOnceAsync(typeof(ThrowingRecurringTask), "throwing", Ct);
        await scheduler.ExecuteOnceAsync(typeof(CountingRecurringTask), "counting", Ct);

        Probe.RecurringRuns.ShouldBe(1);
    }
}
