using System.ComponentModel.DataAnnotations;

namespace NoteManagement.Application.Validation;

/// <summary>
/// FRS-AUTH-001 / spec "User Registration": password must contain at least one letter and
/// at least one digit. Length is enforced separately via <see cref="MinLengthAttribute"/> on
/// the same property — this attribute only checks composition.
/// </summary>
public sealed class PasswordPolicyAttribute : ValidationAttribute
{
    public PasswordPolicyAttribute()
        : base("Password must contain at least one letter and at least one digit.")
    {
    }

    public override bool IsValid(object? value)
    {
        // Null/empty is [Required]'s concern, not this attribute's — returning true here avoids
        // a duplicate "not a valid password" error alongside the "field is required" one.
        if (value is null)
        {
            return true;
        }

        if (value is not string password || password.Length == 0)
        {
            return false;
        }

        var hasLetter = false;
        var hasDigit = false;

        foreach (var c in password)
        {
            if (char.IsLetter(c))
            {
                hasLetter = true;
            }
            else if (char.IsDigit(c))
            {
                hasDigit = true;
            }

            if (hasLetter && hasDigit)
            {
                return true;
            }
        }

        return false;
    }
}
