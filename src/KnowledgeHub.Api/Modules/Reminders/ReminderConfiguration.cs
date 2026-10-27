using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.Api.Modules.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeHub.Api.Modules.Reminders;

public sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ToTable("reminders");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Channel).HasConversion(new LowercaseEnumConverter<NotificationChannel>()).HasMaxLength(20);
        // Планировщик каждую минуту ищет только неотправленные: частичный индекс остаётся маленьким.
        builder.HasIndex(r => r.RemindAt).HasFilter("sent_at IS NULL");
        builder.HasIndex(r => r.ItemId);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Item>().WithMany().HasForeignKey(r => r.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}
