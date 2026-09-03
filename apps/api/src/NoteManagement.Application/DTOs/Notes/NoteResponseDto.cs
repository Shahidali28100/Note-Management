using NoteManagement.Application.DTOs.Tags;

namespace NoteManagement.Application.DTOs.Notes;

/// <summary>FRS-NOTE-001/002. The shape returned by create/get/update/restore. Tags (AB-1006) reflects the note's current tag assignment.</summary>
public sealed record NoteResponseDto(Guid Id, string Title, string Content, IReadOnlyList<TagRefDto> Tags, DateTime CreatedAt, DateTime UpdatedAt);
