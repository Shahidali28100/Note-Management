using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Tags;

/// <summary>FRS-TAG-002. Same validation as CreateTagRequestDto — a full replace of name/color.</summary>
public sealed record UpdateTagRequestDto(
    [Required, TrimmedLength(1, 50)] string Name,
    [Required, RegularExpression(@"^#[0-9A-Fa-f]{6}$", ErrorMessage = "color must be a #RRGGBB hex value.")] string Color);
