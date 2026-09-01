using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Notes;

/// <summary>FRS-NOTE-003. Same validation as CreateNoteRequestDto — a full replace of title/content.</summary>
public sealed record UpdateNoteRequestDto(
    [Required, TrimmedLength(1, 200)] string Title,
    [Required, TrimmedLength(1, int.MaxValue)] string Content);
