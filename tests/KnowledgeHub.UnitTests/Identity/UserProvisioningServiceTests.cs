using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeHub.UnitTests.Identity;

public sealed class UserProvisioningServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private (UserProvisioningService Service, AppDbContext Db) Create()
    {
        var db = new AppDbContext(_options);
        return (new UserProvisioningService(db, new FakeTimeProvider(Now)), db);
    }

    [Fact]
    public async Task FirstLogin_CreatesUserLoginAndDefaultSettings()
    {
        var (service, db) = Create();

        var user = await service.FindOrCreateAsync("google", "sub-1", "alice@example.com", "Alice", Ct);

        user.Id.ShouldNotBe(Guid.Empty);
        user.Email.ShouldBe("alice@example.com");
        user.DisplayName.ShouldBe("Alice");
        user.CreatedAt.ShouldBe(Now);
        (await db.UserLogins.SingleAsync(Ct)).UserId.ShouldBe(user.Id);

        var settings = await db.UserSettings.SingleAsync(Ct);
        settings.UserId.ShouldBe(user.Id);
        settings.Timezone.ShouldBe("UTC");
        settings.DigestFrequency.ShouldBe(DigestFrequency.Off);
        settings.DigestDay.ShouldBe(DayOfWeek.Monday);
        settings.DigestChannels.ShouldBeEmpty();
        settings.Email.ShouldBe("alice@example.com");
    }

    [Fact]
    public async Task RepeatedLogin_ReturnsSameUserWithoutCreatingAnother()
    {
        var (service, db) = Create();

        var first = await service.FindOrCreateAsync("google", "sub-1", "alice@example.com", "Alice", Ct);
        var second = await service.FindOrCreateAsync("google", "sub-1", "changed@example.com", "Changed", Ct);

        second.Id.ShouldBe(first.Id);
        (await db.Users.CountAsync(Ct)).ShouldBe(1);
        (await db.UserLogins.CountAsync(Ct)).ShouldBe(1);
        (await db.UserSettings.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task SameSubjectFromAnotherProvider_IsAnotherUser()
    {
        var (service, db) = Create();

        var google = await service.FindOrCreateAsync("google", "sub-1", null, null, Ct);
        var microsoft = await service.FindOrCreateAsync("microsoft", "sub-1", null, null, Ct);

        microsoft.Id.ShouldNotBe(google.Id);
        (await db.Users.CountAsync(Ct)).ShouldBe(2);
    }

    [Theory]
    [InlineData("Alice  ", "alice@example.com", "Alice")]
    [InlineData(null, "alice@example.com", "alice")]
    [InlineData("  ", "bob@example.com", "bob")]
    [InlineData(null, null, "User")]
    public async Task DisplayName_FallsBackToEmailLocalPartThenDefault(string? name, string? email, string expected)
    {
        var (service, _) = Create();

        var user = await service.FindOrCreateAsync("google", "sub-1", email, name, Ct);

        user.DisplayName.ShouldBe(expected);
    }

    [Fact]
    public async Task TooLongName_IsTruncatedInsteadOfFailingFirstLogin()
    {
        var (service, _) = Create();

        var user = await service.FindOrCreateAsync("google", "sub-1", null, new string('я', 500), Ct);

        user.DisplayName.Length.ShouldBe(User.MaxDisplayNameLength);
    }

    [Fact]
    public async Task TooLongEmailLocalPart_FallbackNameIsTruncated()
    {
        var (service, _) = Create();

        var user = await service.FindOrCreateAsync("google", "sub-1", new string('a', 250) + "@example.com", null, Ct);

        user.DisplayName.Length.ShouldBe(User.MaxDisplayNameLength);
    }

    [Fact]
    public async Task TooLongEmail_IsDroppedFromUserAndSettings()
    {
        var (service, db) = Create();

        var user = await service.FindOrCreateAsync("google", "sub-1", new string('a', User.MaxEmailLength) + "@example.com", "Alice", Ct);

        user.Email.ShouldBeNull();
        (await db.UserSettings.SingleAsync(Ct)).Email.ShouldBeNull();
    }

    [Fact]
    public async Task EmailOfMaxLength_IsKept()
    {
        var (service, _) = Create();
        var email = new string('a', User.MaxEmailLength - "@x.io".Length) + "@x.io";

        var user = await service.FindOrCreateAsync("google", "sub-1", email, "Alice", Ct);

        user.Email.ShouldBe(email);
    }

    public void Dispose()
    {
        using var db = new AppDbContext(_options);
        db.Database.EnsureDeleted();
    }
}
