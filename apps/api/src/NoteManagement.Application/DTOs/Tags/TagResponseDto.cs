namespace NoteManagement.Application.DTOs.Tags;

/// <summary>FRS-TAG-001..004. The shape returned by create/list/update.</summary>
public sealed record TagResponseDto(Guid Id, string Name, string Color, int NoteCount, DateTime CreatedAt, DateTime UpdatedAt);
