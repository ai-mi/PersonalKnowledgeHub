using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Items;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.UnitTests.Infrastructure;

/// <summary>AppDbContext для провайдера InMemory: он не поддерживает tsvector, поэтому колонка поиска исключается из модели.</summary>
public sealed class InMemoryAppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : AppDbContext(options, currentUser)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Item>().Ignore(i => i.SearchVector);
    }
}
