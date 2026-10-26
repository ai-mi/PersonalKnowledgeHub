using KnowledgeHub.Api.Modules.Jobs;

namespace KnowledgeHub.UnitTests.Jobs;

public sealed class CompletedJobsCleanupTests
{
    [Fact]
    public void RunsHourly_UnderStableName()
    {
        var cleanup = new CompletedJobsCleanup(null!, TimeProvider.System);

        cleanup.Name.ShouldBe("jobs-cleanup");
        cleanup.Interval.ShouldBe(TimeSpan.FromHours(1));
    }
}
