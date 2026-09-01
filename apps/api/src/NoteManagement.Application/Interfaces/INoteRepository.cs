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

    /// <summary>Active notes owned by userId, sorted by UpdatedAt descending. AB-1004 always calls this with page=1/pageSize=20 (the fixed default view); the page/pageSize parameters exist now so AB-1005 can wire real query-string values through without changing this signature.</summary>
    Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);
}
