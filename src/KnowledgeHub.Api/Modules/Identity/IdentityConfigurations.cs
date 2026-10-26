using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeHub.Api.Modules.Identity;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Email).HasMaxLength(User.MaxEmailLength);
        builder.Property(u => u.DisplayName).HasMaxLength(User.MaxDisplayNameLength);
    }
}

public sealed class UserLoginConfiguration : IEntityTypeConfiguration<UserLogin>
{
    public void Configure(EntityTypeBuilder<UserLogin> builder)
    {
        builder.ToTable("user_logins");
        builder.HasKey(l => new { l.Provider, l.Subject });
        builder.Property(l => l.Provider).HasMaxLength(50);
        builder.Property(l => l.Subject).HasMaxLength(256);
        builder.HasIndex(l => l.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("user_settings");
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.UserId).ValueGeneratedNever();
        builder.Property(s => s.Timezone).HasMaxLength(100);
        builder.Property(s => s.Email).HasMaxLength(User.MaxEmailLength);
        builder.Property(s => s.DigestFrequency).HasConversion(new LowercaseEnumConverter<DigestFrequency>()).HasMaxLength(20);
        builder.Property(s => s.DigestDay).HasConversion(new LowercaseEnumConverter<DayOfWeek>()).HasMaxLength(20);
        builder.HasOne<User>().WithOne().HasForeignKey<UserSettings>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
