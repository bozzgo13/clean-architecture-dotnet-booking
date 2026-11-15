using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(t => t.Id);

        // Only the hash is persisted; a SHA-256 digest is a fixed 64 hexadecimal characters.
        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        // Every redemption looks the token up by its hash, so the column is indexed and unique: no two
        // refresh tokens can ever hash to the same value.
        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        builder.Property(t => t.CreatedAtUtc)
            .IsRequired();

        builder.Property(t => t.ExpiresAtUtc)
            .IsRequired();

        // Treat RevokedAtUtc as an optimistic-concurrency token. Revocation is the only mutation a
        // refresh token ever undergoes, so guarding it serialises the active -> revoked transition:
        // if two requests redeem the same token simultaneously, the first UPDATE wins and the second
        // affects no row and raises DbUpdateConcurrencyException instead of minting a second token.
        builder.Property(t => t.RevokedAtUtc)
            .IsConcurrencyToken();

        // The hash of the rotating token is optional and has the same shape as TokenHash.
        builder.Property(t => t.ReplacedByTokenHash)
            .HasMaxLength(64);

        // Deleting a user removes their refresh tokens, so no orphaned credentials can be redeemed.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
