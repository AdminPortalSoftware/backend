using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Platform.Infrastructure.Outbox;

/// <summary>
/// Transactional outbox row. Written in the same transaction as the aggregate change and
/// delivered at-least-once by <see cref="OutboxProcessor{TContext}"/>.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
    public required string Type { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public string? CorrelationId { get; init; }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Type).HasMaxLength(512);
        builder.Property(x => x.Content).HasColumnType("jsonb");
        builder.Property(x => x.Error).HasMaxLength(4000);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);

        // Partial index: the processor only ever scans pending rows.
        builder.HasIndex(x => x.OccurredAt).HasFilter("processed_at IS NULL").HasDatabaseName("ix_outbox_messages_pending");
    }
}
