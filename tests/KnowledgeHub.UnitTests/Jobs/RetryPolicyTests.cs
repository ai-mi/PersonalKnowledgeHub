using KnowledgeHub.Api.Modules.Jobs;

namespace KnowledgeHub.UnitTests.Jobs;

public sealed class RetryPolicyTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 30)]
    public void NextDelay_FollowsOneFiveThirtyMinutes(int attempts, int expectedMinutes) =>
        RetryPolicy.NextDelay(attempts).ShouldBe(TimeSpan.FromMinutes(expectedMinutes));

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public void NextDelay_AfterFourthAttempt_IsNull(int attempts) =>
        RetryPolicy.NextDelay(attempts).ShouldBeNull();

    [Fact]
    public void MaxAttempts_IsFour() => RetryPolicy.MaxAttempts.ShouldBe(4);
}
