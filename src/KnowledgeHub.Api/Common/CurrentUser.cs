using KnowledgeHub.Api.Modules.Identity;

namespace KnowledgeHub.Api.Common;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(AuthSchemes.UserIdClaim)?.Value, out var id)
            ? id
            : null;

    public bool IsSystem { get; set; }

    public Guid RequireUserId() =>
        UserId ?? throw new InvalidOperationException("Текущий пользователь не определён.");
}
