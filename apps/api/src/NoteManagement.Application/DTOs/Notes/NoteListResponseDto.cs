namespace NoteManagement.Application.DTOs.Notes;

/// <summary>
/// FRS-NOTE-002/006/007. The standard list envelope (AGENTS.md §6). AB-1004 ships a fixed
/// default view only (Page=1, PageSize=20, sorted UpdatedAt desc) — AB-1005 adds real
/// client-driven pagination/sorting/filtering on top of this same shape.
/// </summary>
public sealed record NoteListResponseDto(
    IReadOnlyList<NoteResponseDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
