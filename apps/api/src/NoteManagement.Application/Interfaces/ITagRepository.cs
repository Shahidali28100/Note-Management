using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Interfaces;

/// <summary>Ownership (UserId) is baked into every lookup, same precedent as INoteRepository.</summary>
public interface ITagRepository
{
    void Add(Tag tag);

    void Remove(Tag tag);

    Task<Tag?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tag>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>FRS-TAG-004: tagId -&gt; count of the owner's active (non-deleted) notes carrying it. Every tag owned by userId appears in the result, including with a count of 0 when it currently carries no active notes.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetActiveNoteCountsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Returns the subset of tagIds that exist and are owned by userId — callers diff this against what they submitted to find invalid ids (never reveals *why* an id was rejected).</summary>
    Task<IReadOnlyList<Guid>> GetOwnedIdsAsync(Guid userId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);
}
