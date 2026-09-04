using NoteManagement.Application.DTOs.Search;

namespace NoteManagement.Application.Interfaces;

public interface ISearchService
{
    Task<SearchResponseDto> SearchAsync(Guid userId, SearchQueryDto query, CancellationToken cancellationToken);
}
