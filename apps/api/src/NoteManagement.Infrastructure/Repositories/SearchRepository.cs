using Microsoft.EntityFrameworkCore;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;
using NoteManagement.Infrastructure.Data;
using NoteManagement.Infrastructure.Search;

namespace NoteManagement.Infrastructure.Repositories;

public sealed class SearchRepository : ISearchRepository
{
    private readonly ApplicationDbContext _dbContext;

    public SearchRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(IReadOnlyList<Note> Items, int TotalCount)> SearchAsync(Guid userId, IReadOnlyList<string> terms, int page, int pageSize, CancellationToken cancellationToken)
    {
        // Every term is already sanitized to [\p{L}\p{Nd}'-] by SearchTermTokenizer — it can
        // contain no '"' and cannot break out of its quoted phrase (AGENTS.md §6/§11: this string
        // is still passed as a single parameter below, never concatenated into the command text).
        var searchCondition = string.Join(" AND ", terms.Select(t => $"FORMSOF(INFLECTIONAL, \"{t}\")"));

        var matches = _dbContext.Set<FullTextMatch>()
            .FromSqlInterpolated($"SELECT [KEY], [RANK] FROM CONTAINSTABLE(Notes, (Title, Content), {searchCondition})");

        // Joining against _dbContext.Notes (not the raw table) applies Note's own
        // HasQueryFilter(DeletedAt == null) automatically — soft-deleted notes are excluded
        // without a second, separately-maintained predicate (same precedent as NoteRepository).
        var query =
            from n in _dbContext.Notes
            join m in matches on n.Id equals m.Key
            where n.UserId == userId
            select new { Note = n, m.Rank };

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.Rank)
            .ThenByDescending(x => x.Note.UpdatedAt) // stable tiebreak for equal-rank rows across pages
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.Note)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
