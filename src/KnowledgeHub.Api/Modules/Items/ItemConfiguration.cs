using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeHub.Api.Modules.Items;

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Type).HasConversion(new LowercaseEnumConverter<ItemType>()).HasMaxLength(20);
        builder.Property(i => i.Status).HasConversion(new LowercaseEnumConverter<ItemStatus>()).HasMaxLength(20);
        builder.Property(i => i.Title).HasMaxLength(Item.MaxTitleLength);
        builder.Property(i => i.Url).HasMaxLength(Item.MaxUrlLength);
        builder.Property(i => i.UserNote).HasMaxLength(Item.MaxUserNoteLength);
        builder.Property(i => i.CodeLanguage).HasMaxLength(Item.MaxCodeLanguageLength);
        builder.Property(i => i.BlobPath).HasMaxLength(1024);
        builder.Property(i => i.ContentHash).HasMaxLength(64);
        builder.Property(i => i.Error).HasMaxLength(2000);

        builder.Property(i => i.SearchVector).HasColumnType("tsvector")
            .Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(i => i.SearchVector).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.HasIndex(i => i.SearchVector).HasMethod("GIN");
        builder.HasIndex(i => new { i.UserId, i.Url }).IsUnique().HasFilter("url IS NOT NULL");
        builder.HasIndex(i => new { i.UserId, i.ContentHash }).IsUnique().HasFilter("content_hash IS NOT NULL");
        builder.HasIndex(i => new { i.UserId, i.CreatedAt }).IsDescending(false, true);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
