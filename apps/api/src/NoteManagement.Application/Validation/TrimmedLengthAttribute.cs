using System.ComponentModel.DataAnnotations;

namespace NoteManagement.Application.Validation;

/// <summary>
/// spec "Note Creation"/"Note Update": valid only if, after trimming leading/trailing
/// whitespace, the value's length is within [minLength, maxLength] — so a whitespace-only
/// string is rejected even though its raw length is nonzero, unlike a plain
/// StringLength/MinLength pair. Mirrors PasswordPolicyAttribute's precedent of a small
/// hand-written composite check where the built-in attributes can't express the rule.
/// </summary>
public sealed class TrimmedLengthAttribute : ValidationAttribute
{
    private readonly int _minLength;
    private readonly int _maxLength;

    public TrimmedLengthAttribute(int minLength, int maxLength)
    {
        _minLength = minLength;
        _maxLength = maxLength;
        ErrorMessage = $"Value must be between {minLength} and {maxLength} characters after trimming whitespace.";
    }

    public override bool IsValid(object? value)
    {
        // Null is [Required]'s concern, not this attribute's — same precedent as PasswordPolicyAttribute.
        if (value is null)
        {
            return true;
        }

        if (value is not string s)
        {
            return false;
        }

        var trimmedLength = s.Trim().Length;
        return trimmedLength >= _minLength && trimmedLength <= _maxLength;
    }
}
