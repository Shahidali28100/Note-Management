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

    public async Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, string sortBy, string sortDirection, CancellationToken cancellationToken)
    {
        var query = _dbContext.Notes.Where(n => n.UserId == userId);
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
}
