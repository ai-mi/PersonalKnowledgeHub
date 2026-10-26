using KnowledgeHub.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeHub.Api.Modules.Bot;

public sealed class TelegramLinkConfiguration : IEntityTypeConfiguration<TelegramLink>
{
    public void Configure(EntityTypeBuilder<TelegramLink> builder)
    {
        builder.ToTable("telegram_links");
        builder.HasKey(l => l.UserId);
        builder.Property(l => l.UserId).ValueGeneratedNever();
        builder.HasIndex(l => l.ChatId).IsUnique();
        builder.HasOne<User>().WithOne().HasForeignKey<TelegramLink>(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TelegramLinkCodeConfiguration : IEntityTypeConfiguration<TelegramLinkCode>
{
    public void Configure(EntityTypeBuilder<TelegramLinkCode> builder)
    {
        builder.ToTable("telegram_link_codes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.CodeHash).HasMaxLength(64);
        builder.HasIndex(c => c.CodeHash).IsUnique();
        builder.HasIndex(c => c.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
