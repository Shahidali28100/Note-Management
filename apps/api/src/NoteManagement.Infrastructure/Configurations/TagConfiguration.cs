using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Infrastructure.Configurations;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .HasMaxLength(50)
            .IsRequired();

        // Fixed-width #RRGGBB — 7 characters. No format CHECK constraint at the DB layer, same
        // precedent as Note.Content: format validation lives at the Application layer
        // (CreateTagRequestDto's [RegularExpression]), not duplicated here.
        builder.Property(t => t.Color)
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .IsRequired();

        // FRS-TAG-001 case-insensitive per-user uniqueness relies on the database's default
        // collation (case-insensitive) — the same reliance UserConfiguration's Users.Email index
        // already makes; see plan.md architecture decisions. This composite index's leftmost
        // column (UserId) already serves a UserId-only lookup, so no separate solo index is added.
        builder.HasIndex(t => new { t.UserId, t.Name })
            .IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
