namespace KnowledgeHub.Api.Modules.Jobs;

/// <summary>Паузы между попытками: после 1-й неудачи 1 мин, после 2-й — 5, после 3-й — 30; после 4-й задача считается проваленной.</summary>
public static class RetryPolicy
{
    private static readonly TimeSpan[] Delays = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)];

    public static int MaxAttempts => Delays.Length + 1;

    /// <summary>Пауза перед следующей попыткой или <c>null</c>, если попытки исчерпаны.</summary>
    public static TimeSpan? NextDelay(int attempts) =>
        attempts >= 1 && attempts <= Delays.Length ? Delays[attempts - 1] : null;
}
