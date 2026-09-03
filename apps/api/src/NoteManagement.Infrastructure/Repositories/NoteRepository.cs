using Microsoft.EntityFrameworkCore;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;
using NoteManagement.Infrastructure.Data;

namespace NoteManagement.Infrastructure.Repositories;

public sealed class NoteRepository : INoteRepository
{
    private readonly ApplicationDbContext _dbContext;

    public NoteRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(Note note) => _dbContext.Notes.Add(note);

    public Task<Note?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        _dbContext.Notes.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    public Task<Note?> GetByIdIncludingDeletedAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        _dbContext.Notes.IgnoreQueryFilters().SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    public async Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, string sortBy, string sortDirection, Guid? tagId, CancellationToken cancellationToken)
    {
        var query = _dbContext.Notes.Where(n => n.UserId == userId);

        // Applied only when tagId is supplied, so the no-filter path's generated SQL is
        // unchanged from AB-1005 (AGENTS.md §6, SDS §42 — tagId is already validated as owned by
        // userId before this is called).
        if (tagId is Guid t)
        {
            query = query.Where(n => _dbContext.NoteTags.Any(nt => nt.NoteId == n.Id && nt.TagId == t));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Explicit allowlist mapping (AGENTS.md §6, SDS §41/§59) — sortBy/sortDirection are never
        // used to build a query expression dynamically. Falls back to updatedAt desc for any
        // combination outside the six allowlisted (sortBy, sortDirection) pairs, which
        // NoteListQueryDto's [AllowedValues] should already have rejected before this is reached.
        IOrderedQueryable<Note> ordered = (sortBy, sortDirection) switch
        {
            ("createdAt", "asc") => query.OrderBy(n => n.CreatedAt),
            ("createdAt", "desc") => query.OrderByDescending(n => n.CreatedAt),
            ("title", "asc") => query.OrderBy(n => n.Title),
            ("title", "desc") => query.OrderByDescending(n => n.Title),
            ("updatedAt", "asc") => query.OrderBy(n => n.UpdatedAt),
            _ => query.OrderByDescending(n => n.UpdatedAt),
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Tag>> GetTagsForNoteAsync(Guid noteId, CancellationToken cancellationToken) =>
        await _dbContext.NoteTags
            .Where(nt => nt.NoteId == noteId)
            .Join(_dbContext.Tags, nt => nt.TagId, t => t.Id, (nt, t) => t)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Tag>>> GetTagsForNotesAsync(IReadOnlyCollection<Guid> noteIds, CancellationToken cancellationToken)
    {
        if (noteIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<Tag>>();
        }

        var rows = await _dbContext.NoteTags
            .Where(nt => noteIds.Contains(nt.NoteId))
            .Join(_dbContext.Tags, nt => nt.TagId, t => t.Id, (nt, t) => new { nt.NoteId, Tag = t })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.NoteId).ToDictionary(g => g.Key, g => (IReadOnlyList<Tag>)g.Select(r => r.Tag).ToList());
    }

    public async Task ReplaceTagsForNoteAsync(Guid noteId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.NoteTags.Where(nt => nt.NoteId == noteId).ToListAsync(cancellationToken);
        _dbContext.NoteTags.RemoveRange(existing);

        foreach (var tagId in tagIds)
        {
            _dbContext.NoteTags.Add(NoteTag.Create(noteId, tagId));
        }
    }
}
