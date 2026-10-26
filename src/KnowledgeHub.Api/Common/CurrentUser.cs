using KnowledgeHub.Api.Modules.Identity;

namespace KnowledgeHub.Api.Common;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor, SystemContext systemContext) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            if (systemContext.IsSystem)
            {
                return null;
            }

            if (systemContext.UserOverride is { } overridden)
            {
                return overridden;
            }

            return Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(AuthSchemes.UserIdClaim)?.Value, out var id)
                ? id
                : null;
        }
    }

    public bool IsSystem => systemContext.IsSystem;

    public Guid RequireUserId() =>
        UserId ?? throw new InvalidOperationException("Текущий пользователь не определён.");
}
