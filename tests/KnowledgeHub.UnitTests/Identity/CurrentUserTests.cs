using System.Security.Claims;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Modules.Identity;
using Microsoft.AspNetCore.Http;

namespace KnowledgeHub.UnitTests.Identity;

public class CurrentUserTests
{
    private static CurrentUser For(HttpContext? context, params Claim[] claims)
    {
        if (context is not null && claims.Length > 0)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        return new CurrentUser(new HttpContextAccessor { HttpContext = context });
    }

    [Fact]
    public void ValidClaim_ReturnsUserId()
    {
        var id = Guid.CreateVersion7();
        var user = For(new DefaultHttpContext(), new Claim(AuthSchemes.UserIdClaim, id.ToString()));

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
        var user = For(new DefaultHttpContext(), claims);

        user.UserId.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => user.RequireUserId());
    }

    [Fact]
    public void NoHttpContext_IsAnonymousAndNotSystem()
    {
        var user = For(null);

        user.UserId.ShouldBeNull();
        user.IsSystem.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => user.RequireUserId());
    }
}
