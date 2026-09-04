using NoteManagement.Application.DTOs.Search;
using NoteManagement.Application.DTOs.Tags;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Services;

public sealed class SearchService : ISearchService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly ISearchRepository _searchRepository;
    private readonly INoteRepository _noteRepository;

    public SearchService(ISearchRepository searchRepository, INoteRepository noteRepository)
    {
        _searchRepository = searchRepository;
        _noteRepository = noteRepository;
    }

    /// <summary>FRS-SEARCH-001..004. An all-sanitized-away q (e.g. "!!!") short-circuits to an empty page rather than querying the repository — still 200, per spec's "no matching notes -> empty page, not an error."</summary>
    public async Task<SearchResponseDto> SearchAsync(Guid userId, SearchQueryDto query, CancellationToken cancellationToken)
    {
        var terms = SearchTermTokenizer.Tokenize(query.Q);
        var page = query.Page ?? DefaultPage;
        var pageSize = Math.Min(query.PageSize ?? DefaultPageSize, MaxPageSize);

        if (terms.Count == 0)
        {
            return new SearchResponseDto(Array.Empty<SearchResultDto>(), page, pageSize, 0, 0);
        }

        var (items, totalCount) = await _searchRepository.SearchAsync(userId, terms, page, pageSize, cancellationToken);
        var tagsByNote = await _noteRepository.GetTagsForNotesAsync(items.Select(n => n.Id).ToList(), cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var results = items
            .Select(n => new SearchResultDto(
                n.Id, n.Title, n.Content,
                tagsByNote.GetValueOrDefault(n.Id, Array.Empty<Tag>()).Select(t => new TagRefDto(t.Id, t.Name, t.Color)).ToList(),
                n.CreatedAt, n.UpdatedAt,
                SearchHighlighter.Build(n.Title, n.Content, terms)))
            .ToList();

        return new SearchResponseDto(results, page, pageSize, totalCount, totalPages);
    }
}
