using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Interfaces;

/// <summary>
/// Ownership (UserId) is baked into every lookup query rather than checked after the fact in the
/// service, so a non-owned note is never loaded into memory at all — defense in depth beyond the
/// spec's "404 either way" requirement (SDS §58: resources scoped to the authenticated user).
/// </summary>
public interface INoteRepository
{
    void Add(Note note);

    /// <summary>Active (non-deleted) note owned by userId — the global query filter already excludes soft-deleted rows. Backs GET/PUT/DELETE.</summary>
    Task<Note?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>Same ownership scoping as GetByIdAsync, but bypasses the soft-delete filter — the only lookup that can see a deleted note. Backs restore, which must distinguish "not found" from "found but not deleted."</summary>
    Task<Note?> GetByIdIncludingDeletedAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Active notes owned by userId, paged and sorted per the caller's resolved (already
    /// defaulted/clamped) parameters. AB-1004 shipped this always called with page=1/pageSize=20/
    /// UpdatedAt desc (the fixed default view); AB-1005 wires real query-string-driven values
    /// through. sortBy must be one of "createdAt"/"updatedAt"/"title"; sortDirection must be one
    /// of "asc"/"desc" — both are allowlisted upstream by NoteListQueryDto's [AllowedValues], and
    /// the implementation must still map them via an explicit switch, never a dynamic/reflection-
    /// based column lookup (AGENTS.md §6, SDS §41/§59).
    /// </summary>
    Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, string sortBy, string sortDirection, CancellationToken cancellationToken);
}
