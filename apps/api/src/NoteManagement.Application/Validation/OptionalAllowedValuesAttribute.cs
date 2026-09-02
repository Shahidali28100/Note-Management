using System.ComponentModel.DataAnnotations;

namespace NoteManagement.Application.Validation;

/// <summary>
/// Like the built-in <see cref="AllowedValuesAttribute"/>, but treats null as valid — matching
/// this codebase's established convention (<see cref="TrimmedLengthAttribute"/>) that null is
/// [Required]'s concern, not an allowlist's. The built-in attribute was verified (direct
/// probe of AllowedValuesAttribute.IsValid, not documented behavior) to reject null, which is
/// wrong for an optional, allowlisted query parameter such as NoteListQueryDto's SortBy/
/// SortDirection — a missing value must pass through so the caller's default applies, not be
/// rejected outright.
/// </summary>
public sealed class OptionalAllowedValuesAttribute : ValidationAttribute
{
    private readonly string[] _allowedValues;

    public OptionalAllowedValuesAttribute(params string[] allowedValues)
    {
        _allowedValues = allowedValues;
        ErrorMessage = $"Value must be one of: {string.Join(", ", allowedValues)}.";
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        return value is string s && _allowedValues.Contains(s, StringComparer.Ordinal);
    }
}
