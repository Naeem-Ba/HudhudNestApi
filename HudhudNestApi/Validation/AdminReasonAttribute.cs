using System.ComponentModel.DataAnnotations;

namespace HudhudNestApi.Validation;

/// <summary>
/// The free-text "reason" an administrator attaches to a decision. It ends up in the audit log and sometimes in a notice to
/// the user, so it is capped and may not carry a NUL (QA Phase 13, P13-D07: a 100000-character reason made a 600 KB audit
/// row). Null and empty are fine: the reason is optional.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class AdminReasonAttribute : ValidationAttribute
{
    public const int MaxLength = 500;

    public AdminReasonAttribute()
        : base($"The reason must be at most {MaxLength} characters and contain no NUL character.")
    {
    }

    public override bool IsValid(object? value)
        => value is not string text || (text.Length <= MaxLength && !text.Contains('\0'));
}
