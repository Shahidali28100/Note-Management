using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Tags;

/// <summary>
/// FRS-TAG-001. Field shapes match delta-openapi.yaml's CreateTagRequest exactly. Validation
/// attributes target the constructor parameters, same precedent as CreateNoteRequestDto.
/// </summary>
public sealed record CreateTagRequestDto(
    [Required, TrimmedLength(1, 50)] string Name,
    [Required, RegularExpression(@"^#[0-9A-Fa-f]{6}$", ErrorMessage = "color must be a #RRGGBB hex value.")] string Color);
