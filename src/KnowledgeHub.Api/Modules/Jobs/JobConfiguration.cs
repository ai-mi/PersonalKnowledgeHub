using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeHub.Api.Modules.Jobs;

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Type).HasMaxLength(Job.MaxTypeLength);
        builder.Property(j => j.Payload).HasColumnType("jsonb");
        builder.Property(j => j.Status).HasConversion(new LowercaseEnumConverter<JobStatus>()).HasMaxLength(20);
        builder.Property(j => j.LastError).HasMaxLength(Job.MaxErrorLength);
        builder.Property(j => j.DedupKey).HasMaxLength(Job.MaxDedupKeyLength);
        builder.HasIndex(j => j.DedupKey).IsUnique();
        builder.HasIndex(j => new { j.Status, j.RunAfter });
    }
}
