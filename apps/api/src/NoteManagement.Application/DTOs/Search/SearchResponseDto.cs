namespace NoteManagement.Application.DTOs.Search;

/// <summary>FRS-SEARCH-004. The standard list envelope (AGENTS.md §6).</summary>
public sealed record SearchResponseDto(IReadOnlyList<SearchResultDto> Items, int Page, int PageSize, int TotalCount, int TotalPages);
