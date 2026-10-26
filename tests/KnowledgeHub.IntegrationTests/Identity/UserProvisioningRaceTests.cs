using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KnowledgeHub.IntegrationTests.Identity;

/// <summary>Детерминированно воспроизводит гонку первого входа: конкурент успевает записать аккаунт между проверкой и сохранением.</summary>
public sealed class UserProvisioningRaceTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AppDbContext NewDb(params IInterceptor[] interceptors) =>
        TestDb.Create(Factory.Postgres.AppConnectionString, TestCurrentUser.System(), extraInterceptors: interceptors);

    [Fact]
    public async Task CompetingInsertBeforeSave_ReturnsTheWinnersUser_WithoutDuplicates()
    {
        var winnerId = Guid.CreateVersion7();
        var interceptor = new BeforeFirstSaveInterceptor(async () =>
        {
            await using var other = NewDb();
            other.Users.Add(new User { Id = winnerId, Email = "winner@example.com", DisplayName = "Winner", CreatedAt = DateTimeOffset.UtcNow });
            other.UserSettings.Add(UserSettings.CreateDefault(winnerId, "winner@example.com"));
            other.UserLogins.Add(new UserLogin { Provider = "google", Subject = "sub-race", UserId = winnerId });
            await other.SaveChangesAsync(Ct);
        });
        await using var db = NewDb(interceptor);
        var service = new UserProvisioningService(db, TimeProvider.System, new SystemContext());

        var user = await service.FindOrCreateAsync("google", "sub-race", "loser@example.com", "Loser", Ct);

        interceptor.Fired.ShouldBeTrue("конкурирующая вставка должна была сработать до сохранения");
        user.Id.ShouldBe(winnerId);
        await using var check = NewDb();
        (await check.Users.CountAsync(Ct)).ShouldBe(1);
        (await check.UserLogins.CountAsync(Ct)).ShouldBe(1);
        (await check.UserSettings.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task SaveFailureNotCausedByRace_IsNotSwallowed()
    {
        var interceptor = new BeforeFirstSaveInterceptor(() => throw new DbUpdateException("boom"));
        await using var db = NewDb(interceptor);
        var service = new UserProvisioningService(db, TimeProvider.System, new SystemContext());

        await Should.ThrowAsync<InvalidOperationException>(
            () => service.FindOrCreateAsync("google", "sub-x", null, null, Ct));
    }

    private sealed class BeforeFirstSaveInterceptor(Func<Task> beforeSave) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await beforeSave();
            }

            return result;
        }
    }
}
