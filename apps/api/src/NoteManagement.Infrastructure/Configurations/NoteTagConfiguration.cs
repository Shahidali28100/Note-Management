using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Infrastructure.Configurations;

public sealed class NoteTagConfiguration : IEntityTypeConfiguration<NoteTag>
{
    public void Configure(EntityTypeBuilder<NoteTag> builder)
    {
        builder.ToTable("NoteTags");

        builder.HasKey(nt => new { nt.NoteId, nt.TagId });

        // Tag -> NoteTags cascades: deleting a Tag (TagService.DeleteAsync) removes its NoteTags
        // rows without app code (FRS-TAG-003). Note -> NoteTags is deliberately Restrict, not
        // Cascade: SQL Server rejects a second cascade path here — Users -> Notes -> NoteTags and
        // Users -> Tags -> NoteTags would both cascade-delete NoteTags from the same ancestor
        // (Users), which SQL Server disallows ("may cause cycles or multiple cascade paths",
        // verified by actually applying this migration). Restrict is safe today because nothing
        // hard-deletes a Note yet — soft delete is an UPDATE, not a DELETE (SDS §14) — but a future
        // hard-purge process (no ticket owns one yet) will need to delete a note's NoteTags rows
        // itself before deleting the note.
        builder.HasOne<Note>()
            .WithMany()
            .HasForeignKey(nt => nt.NoteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(nt => nt.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        // The composite PK already indexes (NoteId, TagId) — a secondary index on TagId alone
        // supports GetActiveNoteCountsAsync's per-tag lookups (AGENTS.md §9).
        builder.HasIndex(nt => nt.TagId);

        // No HasQueryFilter here: association rows must remain visible even for a soft-deleted
        // note (Notes' own filter already hides the note itself), so a restored note keeps its tags.
    }
}
