namespace NoteManagement.Application.DTOs.Notes;

/// <summary>FRS-NOTE-001/002. The shape returned by create/get/update/restore.</summary>
public sealed record NoteResponseDto(Guid Id, string Title, string Content, DateTime CreatedAt, DateTime UpdatedAt);
