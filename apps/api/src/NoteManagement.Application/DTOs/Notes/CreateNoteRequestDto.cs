using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Notes;

/// <summary>
/// FRS-NOTE-001. Field shapes match delta-openapi.yaml's CreateNoteRequest exactly.
/// Validation attributes target the constructor parameters, per RegisterRequestDto's
/// established remark — ASP.NET Core's record model-binding validation reads metadata from the
/// primary constructor parameters, not the compiler-generated properties.
/// </summary>
public sealed record CreateNoteRequestDto(
    [Required, TrimmedLength(1, 200)] string Title,
    [Required, TrimmedLength(1, int.MaxValue)] string Content);
