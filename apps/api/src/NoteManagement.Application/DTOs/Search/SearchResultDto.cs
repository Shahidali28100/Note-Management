using NoteManagement.Application.DTOs.Tags;

namespace NoteManagement.Application.DTOs.Search;

/// <summary>The shape of one item in GET /api/search's results — the standard note shape plus a highlight.</summary>
public sealed record SearchResultDto(Guid Id, string Title, string Content, IReadOnlyList<TagRefDto> Tags, DateTime CreatedAt, DateTime UpdatedAt, NoteHighlightDto Highlight);
