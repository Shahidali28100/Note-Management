namespace NoteManagement.Application.DTOs.Tags;

/// <summary>The tag shape embedded in a note's `tags` array — no noteCount, no timestamps.</summary>
public sealed record TagRefDto(Guid Id, string Name, string Color);
