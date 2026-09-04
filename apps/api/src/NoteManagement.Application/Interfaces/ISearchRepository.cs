using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Interfaces;

/// <summary>terms is already sanitized/tokenized by SearchService — every term is safe to embed in a FORMSOF(...) predicate. Ownership (userId) and the soft-delete exclusion are baked into the query (Note's global query filter), same precedent as INoteRepository.</summary>
public interface ISearchRepository
{
    Task<(IReadOnlyList<Note> Items, int TotalCount)> SearchAsync(Guid userId, IReadOnlyList<string> terms, int page, int pageSize, CancellationToken cancellationToken);
}
