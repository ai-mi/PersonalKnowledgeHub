using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeHub.Api.Modules.Export;

public sealed class ExportConfiguration : IEntityTypeConfiguration<ExportRecord>
{
    public const string ActiveIndexName = "ux_exports_user_id_active";

    public void Configure(EntityTypeBuilder<ExportRecord> builder)
    {
        builder.ToTable("exports");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Status).HasConversion(new LowercaseEnumConverter<ExportStatus>()).HasMaxLength(20);
        builder.Property(e => e.BlobPath).HasMaxLength(500);
        // Не больше одного активного экспорта на пользователя — гарантия на уровне БД, а не только проверки в сервисе.
        builder.HasIndex(e => e.UserId).IsUnique().HasFilter("status IN ('pending', 'processing')").HasDatabaseName(ActiveIndexName);
        builder.HasIndex(e => e.ExpiresAt).HasFilter("expires_at IS NOT NULL");
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
