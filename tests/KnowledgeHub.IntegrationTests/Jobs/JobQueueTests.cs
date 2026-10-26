using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.IntegrationTests.Infrastructure;

namespace KnowledgeHub.IntegrationTests.Jobs;

public sealed class JobQueueTests(ApiFactory factory) : JobsTestBase(factory)
{
    private Task<Job?> ClaimAsync() => WithDbAsync((_, queue) => queue.ClaimNextAsync(Ct));

    [Fact]
    public async Task Enqueue_StoresPendingJobWithPayload()
    {
        var userId = Guid.CreateVersion7();

        (await EnqueueAsync(userId, value: "hello")).ShouldBeTrue();

        var job = (await AllJobsAsync()).Single();
        job.Status.ShouldBe(JobStatus.Pending);
        job.Type.ShouldBe("test");
        job.UserId.ShouldBe(userId);
        job.Attempts.ShouldBe(0);
        job.Payload.ShouldContain("hello");
    }

    [Fact]
    public async Task Enqueue_RepeatedDedupKey_DoesNotCreateSecondJob()
    {
        (await EnqueueAsync(dedupKey: "same")).ShouldBeTrue();
        (await EnqueueAsync(dedupKey: "same")).ShouldBeFalse();

        (await AllJobsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Claim_ParallelClaims_GetTheJobOnlyOnce()
    {
        await EnqueueAsync();

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ClaimAsync()));

        claims.Count(c => c is not null).ShouldBe(1);
        var job = (await AllJobsAsync()).Single();
        job.Status.ShouldBe(JobStatus.Processing);
        job.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task Claim_JobWithFutureRunAfter_IsNotTaken()
    {
        await EnqueueAsync(runAfter: Time.GetUtcNow().AddMinutes(10));

        (await ClaimAsync()).ShouldBeNull();

        Time.Advance(TimeSpan.FromMinutes(11));
        (await ClaimAsync()).ShouldNotBeNull();
    }

    [Fact]
    public async Task Claim_ExpiredLock_IsPickedUpAgain()
    {
        await EnqueueAsync();
        var first = await ClaimAsync();
        first.ShouldNotBeNull();

        Time.Advance(JobWorker.LockDuration - TimeSpan.FromSeconds(1));
        (await ClaimAsync()).ShouldBeNull();

        Time.Advance(TimeSpan.FromSeconds(2));
        var second = await ClaimAsync();
        second.ShouldNotBeNull();
        second.Id.ShouldBe(first.Id);
        second.Attempts.ShouldBe(2);
    }
}
