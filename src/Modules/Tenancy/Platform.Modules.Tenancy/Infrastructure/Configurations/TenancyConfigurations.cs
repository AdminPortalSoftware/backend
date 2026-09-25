using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Infrastructure.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.Property(x => x.Slug).HasMaxLength(63).IsCaseInsensitive();
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.LegalName).HasMaxLength(200);
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.Kind).HasMaxLength(32);
        builder.Property(x => x.PlanCode).HasMaxLength(64);
        builder.Property(x => x.TimeZone).HasMaxLength(64);
        builder.Property(x => x.DefaultCurrency).HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.DefaultLocale).HasMaxLength(16);
        builder.Property(x => x.ContactEmail).HasMaxLength(256);
        builder.Property(x => x.ContactPhone).HasMaxLength(32);
        builder.Property(x => x.WebsiteUrl).HasMaxLength(512);
        builder.Property(x => x.LogoUrl).HasMaxLength(1024);
        builder.HasAddress(x => x.Address);

        builder.HasMany(x => x.Domains).WithOne().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Domains).AutoInclude(false);
    }
}

internal sealed class TenantDomainConfiguration : IEntityTypeConfiguration<TenantDomain>
{
    public void Configure(EntityTypeBuilder<TenantDomain> builder)
    {
        builder.ToTable("tenant_domains");
        builder.Property(x => x.Host).HasMaxLength(253).IsCaseInsensitive();
        builder.HasIndex(x => x.Host).IsUnique();
        builder.Property(x => x.VerificationToken).HasMaxLength(64);
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Code).HasMaxLength(16).IsCaseInsensitive();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.Phone).HasMaxLength(32);
        builder.Property(x => x.TimeZone).HasMaxLength(64);
        builder.HasAddress(x => x.Address);
    }
}

internal sealed class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> builder)
    {
        builder.ToTable("settings");
        builder.Property(x => x.Key).HasMaxLength(128);
        builder.Property(x => x.Value).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
    }
}
