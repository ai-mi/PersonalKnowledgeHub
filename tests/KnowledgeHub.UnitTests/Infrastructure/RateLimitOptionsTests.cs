using KnowledgeHub.Api.Infrastructure.Web;

namespace KnowledgeHub.UnitTests.Infrastructure;

public sealed class RateLimitOptionsTests
{
    [Fact]
    public void DefaultLimit_CoversTwoTabsPollingWhileItemsProcess()
    {
        // Пока записи обрабатываются, SPA опрашивает список и открытую запись каждые 2 с (itemsApi.ts, POLL_INTERVAL_MS):
        // 30 + 30 запросов в минуту на вкладку. Общий лимит пользователя делят все его вкладки, поэтому две вкладки
        // с опросом плюс обычная навигация (до 60 запросов в минуту) не должны упираться в 429.
        const int pollsPerMinutePerQuery = 60 / 2;
        const int tabs = 2;
        const int navigationPerMinute = 60;

        new RateLimitOptions().DefaultPerMinute.ShouldBeGreaterThanOrEqualTo(tabs * 2 * pollsPerMinutePerQuery + navigationPerMinute);
    }
}
