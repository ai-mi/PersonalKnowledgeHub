using System.Reflection;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    private static readonly MethodInfo ApplyUserFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyUserFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<User> Users => Set<User>();
    public DbSet<UserLogin> UserLogins => Set<UserLogin>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<TelegramLink> TelegramLinks => Set<TelegramLink>();
    public DbSet<TelegramLinkCode> TelegramLinkCodes => Set<TelegramLinkCode>();
    public DbSet<Job> Jobs => Set<Job>();

    // Свойства читаются при выполнении запроса: EF подставляет их значения из текущего экземпляра контекста.
    private Guid? CurrentUserId => currentUser.UserId;
    private bool IsSystemContext => currentUser.IsSystem;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        var userOwned = modelBuilder.Model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(t => typeof(IUserOwned).IsAssignableFrom(t))
            .ToList();
        foreach (var type in userOwned)
        {
            ApplyUserFilterMethod.MakeGenericMethod(type).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyUserFilter<T>(ModelBuilder modelBuilder) where T : class, IUserOwned =>
        modelBuilder.Entity<T>().HasQueryFilter(e => IsSystemContext || e.UserId == CurrentUserId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AssignOwner();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AssignOwner();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Новым записям пользователя без владельца проставляет текущего пользователя.</summary>
    private void AssignOwner()
    {
        foreach (var entry in ChangeTracker.Entries<IUserOwned>().Where(e => e.State == EntityState.Added && e.Entity.UserId == Guid.Empty))
        {
            entry.Entity.UserId = currentUser.RequireUserId();
        }
    }
}
