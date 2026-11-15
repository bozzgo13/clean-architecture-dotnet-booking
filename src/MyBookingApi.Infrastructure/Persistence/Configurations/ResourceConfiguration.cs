using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Infrastructure.Persistence.Configurations;

public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(150);

        // Enforce resource-name uniqueness at the database level. This is the authoritative guard
        // against duplicate names (OWASP API6:2023 - Unrestricted Access to Sensitive Business Flows):
        // an application-level pre-check alone is not atomic, so a concurrent insert race is rejected
        // by this index and translated into a safe 400 Bad Request by the handler.
        builder.HasIndex(r => r.Name)
            .IsUnique();

        builder.Property(r => r.Description)
            .HasMaxLength(500);
    }
}