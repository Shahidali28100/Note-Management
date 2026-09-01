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

    public async Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.Notes.Where(n => n.UserId == userId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
