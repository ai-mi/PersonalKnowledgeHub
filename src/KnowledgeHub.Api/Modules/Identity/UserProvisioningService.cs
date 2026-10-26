using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Identity;

public class UserProvisioningService(AppDbContext db, TimeProvider timeProvider)
{
    /// <summary>Возвращает пользователя по внешнему аккаунту, создавая его (вместе с настройками) при первом входе.</summary>
    public async Task<User> FindOrCreateAsync(
        string provider, string subject, string? email, string? name, CancellationToken ct = default)
    {
        var existing = await FindAsync(provider, subject, ct);
        if (existing is not null)
        {
            return existing;
        }

        // Значения приходят от внешнего провайдера: слишком длинные не должны ронять первый вход.
        email = email is { Length: <= User.MaxEmailLength } ? email : null;

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            DisplayName = DisplayNameFor(name, email),
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.Users.Add(user);
        db.UserSettings.Add(UserSettings.CreateDefault(user.Id, email));
        db.UserLogins.Add(new UserLogin { Provider = provider, Subject = subject, UserId = user.Id });

        try
        {
            await db.SaveChangesAsync(ct);
            return user;
        }
        catch (DbUpdateException)
        {
            // Параллельный первый вход того же аккаунта: победила другая транзакция.
            db.ChangeTracker.Clear();
            return await FindAsync(provider, subject, ct) ?? throw new InvalidOperationException(
                $"Не удалось создать пользователя для {provider}.");
        }
    }

    private Task<User?> FindAsync(string provider, string subject, CancellationToken ct) =>
        db.UserLogins
            .Where(l => l.Provider == provider && l.Subject == subject)
            .Join(db.Users, l => l.UserId, u => u.Id, (_, u) => u)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

    private static string DisplayNameFor(string? name, string? email)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var trimmed = name.Trim();
            return trimmed.Length <= User.MaxDisplayNameLength ? trimmed : trimmed[..User.MaxDisplayNameLength];
        }

        var local = email?.Split('@')[0];
        return string.IsNullOrWhiteSpace(local) ? "User" : local[..Math.Min(local.Length, User.MaxDisplayNameLength)];
    }
}
