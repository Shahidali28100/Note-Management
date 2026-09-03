using Microsoft.EntityFrameworkCore;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;
using NoteManagement.Infrastructure.Data;

namespace NoteManagement.Infrastructure.Repositories;

public sealed class TagRepository : ITagRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TagRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(Tag tag) => _dbContext.Tags.Add(tag);

    public void Remove(Tag tag) => _dbContext.Tags.Remove(tag);

    public Task<Tag?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        _dbContext.Tags.SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Tag>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _dbContext.Tags.Where(t => t.UserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetActiveNoteCountsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // _dbContext.Notes.Any(...) goes through Note's global query filter (DeletedAt == null)
        // automatically — "active notes only" needs no separate predicate here (FRS-TAG-004).
        // Every tag owned by userId is enumerated, so every tag id appears in the result — including
        // with Count 0 when it currently carries no active notes.
        var counts = await _dbContext.Tags
            .Where(t => t.UserId == userId)
            .Select(t => new
            {
                t.Id,
                Count = _dbContext.NoteTags.Count(nt => nt.TagId == t.Id && _dbContext.Notes.Any(n => n.Id == nt.NoteId)),
            })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(c => c.Id, c => c.Count);
    }

    public async Task<IReadOnlyList<Guid>> GetOwnedIdsAsync(Guid userId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        return await _dbContext.Tags
            .Where(t => t.UserId == userId && tagIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
    }
}
