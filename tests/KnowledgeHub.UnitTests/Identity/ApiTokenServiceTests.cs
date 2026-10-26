using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.UnitTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeHub.UnitTests.Identity;

public sealed class ApiTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private (ApiTokenService Service, AppDbContext Db, FakeTimeProvider Time, Guid UserId) Create()
    {
        var userId = Guid.CreateVersion7();
        var system = new SystemContext();
        var time = new FakeTimeProvider(Now);
        // Пользователь запроса задаётся через RunAs: HttpContext в unit-тесте нет.
        var db = new InMemoryAppDbContext(_options, new CurrentUser(new HttpContextAccessor(), system));
        system.RunAs(userId);
        return (new ApiTokenService(db, time, system), db, time, userId);
    }

    [Fact]
    public void GenerateToken_HasPrefixAndIsUnique()
    {
        var a = ApiTokenService.GenerateToken();
        var b = ApiTokenService.GenerateToken();

        a.ShouldStartWith("kh_");
        // 32 байта в Base64Url без паддинга = 43 символа.
        a.Length.ShouldBe(3 + 43);
        a.ShouldNotBe(b);
        a.ShouldMatch("^kh_[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Hash_IsStableSha256Hex()
    {
        ApiTokenService.Hash("abc").ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public async Task Create_StoresOnlyHash_AndSetsExpiry()
    {
        var (service, db, _, _) = Create();

        var (entity, token) = await service.CreateAsync("script", 30, Ct);

        entity.TokenHash.ShouldBe(ApiTokenService.Hash(token));
        entity.TokenHash.ShouldNotContain(token);
        entity.ExpiresAt.ShouldBe(Now.AddDays(30));
        (await db.ApiTokens.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Create_WithoutExpiry_NeverExpires()
    {
        var (service, _, _, _) = Create();

        var (entity, _) = await service.CreateAsync("script", null, Ct);

        entity.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task Authenticate_ValidToken_ReturnsOwner()
    {
        var (service, _, _, userId) = Create();
        var (_, token) = await service.CreateAsync("script", null, Ct);

        (await service.AuthenticateAsync(token, Ct)).ShouldBe(userId);
    }

    [Fact]
    public async Task Authenticate_UnknownToken_ReturnsNull()
    {
        var (service, _, _, _) = Create();

        (await service.AuthenticateAsync(ApiTokenService.GenerateToken(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Authenticate_RevokedToken_ReturnsNull()
    {
        var (service, _, _, _) = Create();
        var (entity, token) = await service.CreateAsync("script", null, Ct);
        await service.RevokeAsync(entity.Id, Ct);

        (await service.AuthenticateAsync(token, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Authenticate_ExpiredToken_ReturnsNull()
    {
        var (service, _, time, _) = Create();
        var (_, token) = await service.CreateAsync("script", 1, Ct);
        time.Advance(TimeSpan.FromDays(1));

        (await service.AuthenticateAsync(token, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Authenticate_UpdatesLastUsedAtAtMostOncePerMinute()
    {
        var (service, db, time, _) = Create();
        var (entity, token) = await service.CreateAsync("script", null, Ct);

        await service.AuthenticateAsync(token, Ct);
        var first = (await db.ApiTokens.AsNoTracking().SingleAsync(Ct)).LastUsedAt;
        first.ShouldBe(Now);

        time.Advance(TimeSpan.FromSeconds(30));
        await service.AuthenticateAsync(token, Ct);
        (await db.ApiTokens.AsNoTracking().SingleAsync(Ct)).LastUsedAt.ShouldBe(first);

        time.Advance(TimeSpan.FromSeconds(31));
        await service.AuthenticateAsync(token, Ct);
        (await db.ApiTokens.AsNoTracking().SingleAsync(Ct)).LastUsedAt.ShouldBe(Now.AddSeconds(61));
    }

    [Fact]
    public async Task Revoke_IsIdempotent_AndReportsMissing()
    {
        var (service, db, time, _) = Create();
        var (entity, _) = await service.CreateAsync("script", null, Ct);

        (await service.RevokeAsync(entity.Id, Ct)).ShouldBeTrue();
        var revokedAt = (await db.ApiTokens.AsNoTracking().SingleAsync(Ct)).RevokedAt;
        revokedAt.ShouldBe(Now);

        time.Advance(TimeSpan.FromHours(1));
        (await service.RevokeAsync(entity.Id, Ct)).ShouldBeTrue();
        (await db.ApiTokens.AsNoTracking().SingleAsync(Ct)).RevokedAt.ShouldBe(revokedAt);

        (await service.RevokeAsync(Guid.CreateVersion7(), Ct)).ShouldBeFalse();
    }
}
