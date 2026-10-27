using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Identity;

public sealed class UserContactService(AppDbContext db, SystemContext systemContext) : IUserContactService
{
    public async Task<string?> FindEmailAsync(Guid userId, CancellationToken ct)
    {
        // Вызывающий может работать от имени другого пользователя (или без него): ищем по явному userId.
        using var _ = systemContext.Enter();
        var email = await db.UserSettings.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.Email).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(email))
        {
            email = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        }

        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    public async Task<UserSettings?> FindSettingsAsync(Guid userId, CancellationToken ct)
    {
        using var _ = systemContext.Enter();
        return await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
    }
}
