using System.Security.Claims;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Modules.Identity;
using Microsoft.AspNetCore.Http;

namespace KnowledgeHub.UnitTests.Identity;

public class CurrentUserTests
{
    private static CurrentUser For(HttpContext? context, SystemContext? system, params Claim[] claims)
    {
        if (context is not null && claims.Length > 0)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        return new CurrentUser(new HttpContextAccessor { HttpContext = context }, system ?? new SystemContext());
    }

    [Fact]
    public void ValidClaim_ReturnsUserId()
    {
        var id = Guid.CreateVersion7();
        var user = For(new DefaultHttpContext(), null, new Claim(AuthSchemes.UserIdClaim, id.ToString()));

        user.UserId.ShouldBe(id);
        user.RequireUserId().ShouldBe(id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void MissingOrInvalidClaim_IsAnonymous(string? value)
    {
        var claims = value is null ? [] : new[] { new Claim(AuthSchemes.UserIdClaim, value) };
        var user = For(new DefaultHttpContext(), null, claims);

        user.UserId.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => user.RequireUserId());
    }

    [Fact]
    public void NoHttpContext_IsAnonymousAndNotSystem()
    {
        var user = For(null, null);

        user.UserId.ShouldBeNull();
        user.IsSystem.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => user.RequireUserId());
    }

    private static DefaultHttpContext SignedIn(Guid id) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthSchemes.UserIdClaim, id.ToString())], "test")),
    };

    [Fact]
    public void SystemMode_HasNoUserAndIsSystem_AndRestoresOnDispose()
    {
        var id = Guid.CreateVersion7();
        var system = new SystemContext();
        var user = For(SignedIn(id), system);

        using (system.Enter())
        {
            user.IsSystem.ShouldBeTrue();
            user.UserId.ShouldBeNull();
        }

        user.IsSystem.ShouldBeFalse();
        user.UserId.ShouldBe(id);
    }

    [Fact]
    public void RunAs_OverridesRequestUser_AndRestoresOnDispose()
    {
        var requestUser = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var system = new SystemContext();
        var user = For(SignedIn(requestUser), system);

        using (system.RunAs(other))
        {
            user.UserId.ShouldBe(other);
            user.IsSystem.ShouldBeFalse();
        }

        user.UserId.ShouldBe(requestUser);
    }

    [Fact]
    public void NestedContexts_RestoreThePreviousOneNotTheOriginal()
    {
        var other = Guid.CreateVersion7();
        var system = new SystemContext();
        var user = For(SignedIn(Guid.CreateVersion7()), system);

        using (system.RunAs(other))
        {
            using (system.Enter())
            {
                user.IsSystem.ShouldBeTrue();
            }

            user.IsSystem.ShouldBeFalse();
            user.UserId.ShouldBe(other);
        }
    }

    [Fact]
    public void RunAs_WorksWithoutHttpContext_ForBackgroundWorkers()
    {
        var id = Guid.CreateVersion7();
        var system = new SystemContext();
        var user = For(null, system);

        using var _ = system.RunAs(id);

        user.RequireUserId().ShouldBe(id);
    }
}
