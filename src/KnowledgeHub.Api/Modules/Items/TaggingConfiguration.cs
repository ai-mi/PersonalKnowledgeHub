using KnowledgeHub.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeHub.Api.Modules.Items;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(TagName.MaxLength);
        // Уникальность (user_id, lower(name)) — индекс по выражению, EF его не описывает; создаётся SQL-ом в миграции Tagging.
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Items).WithMany(i => i.Tags).UsingEntity<ItemTag>(
            right => right.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId),
            left => left.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId),
            join =>
            {
                join.ToTable("item_tags");
                join.HasKey(x => new { x.ItemId, x.TagId });
            });
    }
}

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("collections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Collection.MaxNameLength);
        builder.Property(c => c.Description).HasMaxLength(Collection.MaxDescriptionLength);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Items).WithMany(i => i.Collections).UsingEntity<CollectionItem>(
            right => right.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId),
            left => left.HasOne<Collection>().WithMany().HasForeignKey(x => x.CollectionId),
            join =>
            {
                join.ToTable("collection_items");
                join.HasKey(x => new { x.CollectionId, x.ItemId });
            });
    }
}
